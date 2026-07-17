# Build Plan v3 — All-Star Swingers (Unity 2.5D Port)

## 0. Context (read this first)

v1/v2 got the Python prototype to a verified, bug-fixed state: 17 real
defects found and fixed (`reviews/Bug_Audit_2026-07-17.md`), a synthetic
500-measure probe added as a regression test, and real hardware captures
used to rebuild the sharpness constants from actual device data instead of
placeholders. `CLAUDE.md`'s platform roadmap already confirms the module
split needed for this port: `beat_schedule.py`, `ictus_detector.py`,
`scoring.py`, `session_log.py`, and `calibration.py` (file I/O only) have
no `pygame` or hardware dependency and consume only `(timestamp,
magnitude)` sample tuples. `game.py`, `metronome.py`, and
`settings_menu.py` are the presentation/input layer this port replaces.

**One open item is carried over, not resolved, and it matters here more
than it did in Python.** The last Python session ended paused on an
unresolved report: live `game.py` playtesting felt like it had latency/
offset "too large," severely enough to not trust whether scoring was
working, and it was never pinned down whether the `JUDGE_GRACE_S` fix (a
250ms grace period added for a real judging-window race condition) actually
addressed it. That investigation stopped at the user's request rather than
being guessed at further.

**This matters for the port because Unity's audio/threading stack is a
different latency profile from `pygame.mixer` + a Python thread — the
Python latency number will not transfer.** Porting the detector logic
without first getting a trustworthy latency number risks reproducing the
same "can't tell if scoring is working" experience, just with a new set of
possible causes (Unity audio buffer size, `AudioSettings.dspTime` vs
`Time.time` divergence, input polling in `Update` vs `FixedUpdate`, etc.)
tangled up with whatever the old unresolved cause was. Phase 0 below exists
to close that out first, cheaply, in the environment where it's already
half-diagnosed, before it becomes a second unknown stacked on a new one.

---

## 1. Phase 0 — Close out the Python latency report (do this first)

**Goal:** get an actual number and a reproducible example before touching
Unity, so any latency complaint in the Unity build can be diffed against a
known-good Python baseline instead of debugged from scratch.

- Get the specific data the last session asked for: the actual calibration
  offset value from `calibration.json` at the time of the complaint, and
  2–3 concrete "I swung on the click and it said X" examples (expected
  tier vs. observed tier, ideally with the printed/logged
  `corrected_offset_ms` for that swing).
- Reproduce with logging on: run `game.py` with per-swing
  `raw_offset_ms`/`corrected_offset_ms` printed live (not just saved to the
  end-of-round summary), so the felt latency can be correlated to an actual
  number in real time instead of relying on memory after the round ends.
- Determine whether `JUDGE_GRACE_S` already fixed it (bogus Misses from the
  race condition), whether it's a calibration offset that's simply wrong
  (bad practice-swing data during calibration), or whether it's a genuine
  audio/output latency `DEFAULT_OFFSET_S` was never correctly measuring in
  the first place (recall: `calibration.py`'s `DEFAULT_OFFSET_S = 0.05` was
  flagged in v1 as a guess, never replaced with a real dev-machine
  measurement).
- If it's the latter, this is the moment to actually do that measurement
  properly — e.g. an audible/visual dual-reference test (clap on the click,
  compare recorded audio timestamp vs. detected ictus timestamp) rather
  than trusting the self-referential calibration loop to catch its own
  systematic bias.

- While in here, close the last v1-era placeholder: `calibration.py`'s
  `DEFAULT_OFFSET_S` is *still* the 0.05 guess flagged in v1 — replace it
  with the dev-machine number this investigation produces. This is the last
  chance to do it in the environment where it's cheap.

**Exit criterion:** a concrete, written explanation of what the "too large"
latency actually was and confirmation it's resolved or at least understood
well enough to know it *won't* also be a Unity-side bug — with an actual
before/after offset number, not "it feels better now." Write the findings
to `reviews/` (same discipline as the bug audit): the measured offset, the
identified cause, and whether `JUDGE_GRACE_S`'s value/mechanism changed —
Phase 3 carries over Phase 0's *conclusion* about the grace period, not
blindly the current 250ms constant.

---

## 2. Phase 1 — Unity Project Setup & Input Bring-Up

**Goal:** prove the right Joy-Con can stream data into Unity at all before
porting any logic on top of it — this mirrors v1 Phase 0's approach
(hardware first, logic second) for good reason.

- Create the Unity project (2D/3D fixed-camera hybrid — see Phase 3 for
  what "2.5D" means concretely here). Pick and pin a Unity LTS version now;
  don't drift versions mid-port.
- Bring in a Joy-Con input path for Windows. `joycon-python`/`hid` don't
  apply in C#; evaluate existing Unity/C# Joy-Con libraries (e.g.
  community HID-based Joy-Con packages) against what `joycon_stream.py`
  actually needed: right-Joy-Con-only, Bluetooth, raw gyro x/y/z at
  roughly the same real device rate this session measured (~66Hz,
  confirmed from real hardware in the bug-fix pass — use that number, not
  the "60–200Hz" guess from v1, when picking/configuring a library).
- **Timebox this with a fallback ladder (the v1 Phase 0 lesson, repeated):**
  Unity Joy-Con libraries are community-maintained and mostly stale. If the
  chosen library can't produce a stable gyro stream within the timebox,
  fall back in order: (1) a direct hidapi P/Invoke port of
  `joycon_stream.py`'s protocol logic (the pyjoycon parts actually used —
  enable-IMU subcommand + input-report parsing — are small), (2) keep the
  proven Python `joycon_stream.py` running as an input bridge streaming
  `(timestamp, gx, gy, gz)` to Unity over local UDP. Option 2 is not just a
  stopgap: it decouples the whole port from HID debugging, and lets Phase 4
  A/B the same physical swing against the Python baseline directly.
- **Verify the units, not just the stream.** Every tuned constant in the
  Python code (`RISE_RATE_THRESHOLD = 7500`, `PEAK_MIN_THRESHOLD`,
  `FALLBACK_SHARPNESS_LOW_HIGH = (60000, 420000)`) is expressed in
  pyjoycon's *raw* gyro units. A C# library may emit calibrated deg/s or
  rad/s instead — silently wrong-by-a-constant-factor thresholds, which no
  test will catch because synthetic tests use synthetic magnitudes. In this
  phase, do an idle-vs-hard-swing capture and confirm the magnitude scale
  matches the Python captures (idle ~8–27, hard-swing peaks ~20k–30k); if
  it doesn't, define one conversion factor at the input boundary and keep
  all ported constants in the original raw units.
- **Read the HID device on a dedicated background thread, not in
  `Update()`.** `Update()` at 60fps under-samples the ~66Hz device — the
  same class of bug as the old 60Hz calibration path (F3). The reader
  thread polls at the fixed chosen rate, timestamps each sample at read
  time on that thread, applies the duplicate-read skip, and hands samples
  to the main thread via a concurrent queue. This also structurally
  prevents re-introducing F4 (UI stalls silently eating wind-up samples).
- Reproduce v1 Phase 0's exit criterion in Unity: stream raw gyro data,
  compute combined magnitude, and confirm visible magnitude spikes on a
  deliberate swing with a debug log or on-screen readout — no game logic
  yet, just proving the sensor path.
- **Reproduce the sampling-rate fix from F3/A5 from day one, don't
  reintroduce the bug.** The Python fix unified polling rate across three
  call sites and added a duplicate-read skip; replicate both in whatever
  Unity input-polling approach is chosen (e.g. don't poll a cached HID
  status every `Update()` call unthrottled — that's exactly what caused
  the ~50% inflated detection count in Python).

**Exit criterion:** Unity console/on-screen log shows clean gyro magnitude
data with visible, distinguishable swing spikes, at a known, deliberately
chosen (not accidental) polling rate.

---

## 3. Phase 2 — Port the Pure-Logic Modules to C#

**Goal:** get `beat_schedule.py`, `ictus_detector.py`, `scoring.py`,
`session_log.py`, and `calibration.py`'s non-file-I/O logic into C#,
functionally identical to the Python versions — this is meant to be a
faithful port, not a rewrite or a redesign.

- **Use `double`, not `float`, throughout the ported logic.** Python floats
  are 64-bit; Unity code defaults to 32-bit `float`, which loses sub-ms
  precision on seconds-scale timestamps within minutes of a session start.
  Timestamps, magnitudes, offsets, and thresholds are all `double` in the
  logic layer; convert to `float` only at the rendering boundary.
- **Keep the logic modules in their own assembly (asmdef) with zero
  `UnityEngine` references.** This enforces the same purity the Python
  split had (the whole reason this port is "largely as-is"), makes the
  edit-mode tests trivially runnable, and keeps the door open for the VR
  stage to consume the same assembly.
- Port module by module, preserving names/structure where reasonable so
  the two implementations stay easy to compare:
  - `BeatSchedule`/`BeatScheduleConfig` → C# class/struct, same
    precomputed-timestamp-list approach.
  - `IctusDetector`'s state machine (`ARMED` → `RISING` →
    `POST_DROP_SEEK_MIN` → `REFRACTORY`) → port as-is, including the
    `MIN_SEEK_TIMEOUT_S` bound (A2/A6 in the audit — don't reintroduce the
    original stall bug) and the max-derivative sharpness tracking added in
    F2 (don't port the old peak/rise_duration-only version).
  - `scoring.py`'s tier tables and `SharpnessReference` percentile
    bucketing → port directly; carry over the corrected, hardware-derived
    fallback range from F1, not the original placeholder.
  - `session_log.py` → C# equivalent, still just a data holder decoupled
    from rendering.
  - `calibration.py`'s offset/seed computation logic → port; the
    JSON-file-I/O part can use Unity's `JsonUtility` or an equivalent,
    same persisted shape (`offset_s`, `sharpness_seed`).
- **Port the tests, not just the code.** `tests/test_ictus_detector_smoke.py`,
  `test_ictus_intensity_invariance.py`, `test_measure_judge_smoke.py`, and
  `test_full_loop_probe.py` (the 500-measure regression test added for A4)
  all exercise pure logic with synthetic data — translate them into Unity
  Test Framework (edit-mode) tests using the same synthetic trace
  generators. These tests already encode every bug this project has found
  so far.
- **Golden-trace parity tests — the real port verification.** Ported test
  generators share a weakness: a misunderstanding can be ported into the
  test *and* the code and pass anyway. So, before porting, generate golden
  files from the Python side: run the real captures already in `src/`
  (`swings_fresh.csv` etc.) plus the synthetic traces through the Python
  detector/scorer and dump the expected outputs (ictus timestamps, peak,
  max_derivative, tier decisions) to JSON. The C# port must replay the
  *identical* input CSVs and match those outputs — timestamps within a
  small epsilon (~1e-6s), tiers exactly. This is the single strongest check
  in this phase: it catches unit mismatches, float-width mistakes,
  off-by-one state-machine differences, and anything else a "faithful port"
  can silently get wrong. A small Python `tools/` script to emit the golden
  files is in scope here.
- Do not touch detection thresholds during the port. Carry over whatever
  Phase 4 (Python, `Swinger_Build_Plan_v2.md`) tuned them to — retuning
  happens later in Phase 5 below, against Unity's own real captures, not
  now.

**Exit criterion:** all ported tests pass in Unity Test Framework with the
same pass/fail behavior as their Python originals, including the
500-measure probe (proving A4's fix carried over) and the intensity/
quantization edge cases from F2 — **and the golden-trace parity tests match
the Python outputs on every real capture file.**

---

## 4. Phase 3 — 2.5D Scene, Rendering, and the Batting Loop

**Goal:** replace `game.py`'s pygame render loop with an actual Unity
scene, per the platform roadmap's "3D characters/environment, fixed
camera, no free player movement" definition (Super Mario Party style, not
free-roam 3D).

- **First milestone inside this phase: a metronome-only scene** (the Unity
  equivalent of v1 Phase 1) before any batting logic — clicks scheduled on
  the audio clock, drift logged over a 2+ minute run, and the clock-domain
  mapping below verified in isolation. Getting this wrong is a likely
  reintroduction point for a latency bug; prove it while it's the only
  thing in the scene.
- **One clock-domain design, decided explicitly (the Unity version of v1's
  single-`perf_counter` rule):** Unity has *two* relevant clocks —
  `AudioSettings.dspTime` (sample-accurate, drives when clicks are heard)
  and the CPU clock the input thread stamps samples with
  (`Stopwatch`/`Time.realtimeSinceStartupAsDouble`). The beat schedule, the
  judge windows, and the sample timestamps must end up in ONE of them.
  Recommended: keep the CPU clock as the canonical domain (samples are born
  there), schedule clicks via `AudioSource.PlayScheduled(dspTime)`, and
  maintain a measured dspTime↔CPU-clock offset (sampled at startup, checked
  periodically for drift) to convert scheduled click times into the
  canonical domain. Do not mix domains implicitly — that divergence is
  exactly the "second unknown" Section 0 warns about.
- Build a minimal fixed-camera scene: a batter position, a pitch/ball
  origin, no player movement or navigation. Placeholder 3D models are fine
  here (the same "flat placeholder, not final look" spirit v1 applied to
  its 2D rectangles) — the goal is proving the camera/staging approach
  works, not final art.
- Port the `MeasureJudge` state machine (`WINDUP` → `EXPECTING_ICTUS` →
  `JUDGE`) as a MonoBehaviour or plain C# class driven by Unity's update
  loop, feeding it from the Phase 1 input stream and the Phase 2 detector.
  Carry over the grace-period mechanism from the Python fix, at whatever
  value/form **Phase 0 concluded** (not blindly the current 250ms) — the
  race condition it addresses (window closing before the detector's own
  pipeline lag resolves) is a property of the detection pipeline, not of
  Python specifically, and will exist in Unity too. Judgment popups must be
  non-blocking (a timer/flag, not a coroutine that pauses input
  consumption) — the input thread makes this structural, but don't undo it
  by blocking the consumer side (F4's lesson).
- Metronome: use `AudioSettings.dspTime` for scheduling (Unity's
  sample-accurate audio clock), not `Time.time`/`Update()` timing — this is
  the direct Unity equivalent of why v1's `metronome.py` used
  `time.perf_counter()` instead of pygame frame timing. Getting this wrong
  is a likely reintroduction point for a latency bug like the one Phase 0
  is closing out.
- Visual/audio feedback per judgment: contact popup or animation scaled by
  timing tier and hit tier, matching v1 Section 4d's design (Bunt/Line
  Drive/Home Run), but as real Unity UI/animation instead of a pygame text
  blit.
- Post-round summary screen: same data v1's summary showed (timing
  offsets, sharpness values, wind-up interval variance, out count), as a
  real Unity UI screen.
- Settings/Calibrate flow: port `settings_menu.py`'s logic (not its pygame
  rendering) into a Unity UI menu — same behavior: calibration is
  Settings-accessed post-round, never automatic, offset+seed persist to the
  same `calibration.json` shape, stored under
  `Application.persistentDataPath`. **Do NOT import the Python
  `calibration.json`'s offset** — it measures the pygame+Bluetooth stack's
  latency, not Unity's; starting from it would bake the wrong number in.
  The Unity build starts from its own measured default (Phase 4) until the
  player calibrates. (Carrying the *seed* over is also unsafe if Phase 1
  found a unit-scale difference; simplest is a clean file.)

**Exit criterion:** a full round is playable in the Unity build — metronome
audible, swing detected, judgment shown, 3 misses ends the round, summary
screen shown, Settings → Calibrate reachable and functional — using the
ported logic from Phase 2, not reimplemented logic.

---

## 5. Phase 4 — Unity-Side Hardware Validation & Tuning

**Goal:** don't assume Python's tuned thresholds transfer. Different input
library, different polling approach, possibly different effective latency
— validate against real captures taken in Unity itself, the same
discipline the Python side used (v2 Phase 4).

- Capture real Joy-Con data through the Unity input path (Phase 1) across
  soft/medium/hard swings, the same methodology used to catch the Python
  sampling-rate bug and tune its thresholds.
- Replay-test the ported detector (via the Unity Test Framework tests from
  Phase 2, fed this new Unity-captured data) and confirm detected event
  count matches actual swing count — this is the same validation that
  caught A5 in Python; expect to find at least one Unity-specific surprise
  here rather than assuming a clean port.
- Re-run the Phase 0 latency investigation's methodology (not necessarily
  its conclusion) against the Unity build specifically — confirm the
  audio-to-detection latency in Unity is measured and calibrated the same
  rigorous way, not inherited by assumption from the Python number. If
  Phase 1's UDP-bridge fallback exists (even unused), it enables the
  cleanest possible A/B here: the same physical swing judged by both
  stacks simultaneously.
- If Phase 1 found a magnitude unit-scale difference between the C# input
  path and pyjoycon, re-derive the sharpness fallback range and re-verify
  every magnitude-denominated threshold from these Unity captures — a
  conversion factor at the input boundary should make this a no-op, but
  verify it rather than assume it.

**Exit criterion:** a fresh set of real Unity-side captures replay cleanly
through the ported detector with correct event counts, and a Unity-specific
calibration offset has been measured and confirmed sane (not just copied
from the Python `DEFAULT_OFFSET_S`).

---

## 6. Phase 5 — Full Manual Playtest (Unity build)

Mirrors v2 Phase 5, repeated for the new platform since nothing about a
successful Python playtest guarantees a successful Unity one:

- A full round played by someone other than the developer, zero
  explanation beyond "swing when you hear the click."
- Confirm the post-round summary and Settings/Calibrate flow work
  end-to-end in the Unity UI.
- Explicitly ask the playtester whether timing feels correct/responsive —
  given Phase 0's history, don't treat a vague "feels off" as fully
  resolved by Phase 4's numbers alone; get the same kind of concrete
  "I swung on the click and it said X" example if anything feels wrong.

**Exit criterion:** a human who isn't the developer completes a round and
confirms scoring feels attributable to their actual swings, not random or
delayed.

---

## 7. Explicitly out of scope for v3

Unchanged from v1/v2's deferred list: per-axis/dominant-axis calibration,
beat 2 scoring, real song/audio-driven tempo, the conductor-leads-audio
inversion, tempo ramps/time-signature changes, dual Joy-Con, VR (the
long-term stage after this one), difficulty-adaptive systems, and any
minigame beyond All-Star Swingers. Also explicitly out of scope for v3
specifically: final art/animation polish (placeholder models are fine per
Phase 3), and free player movement/navigation — the fixed-camera
constraint is a deliberate design choice carried from the platform roadmap,
not a stopgap to fix later.

---

## Summary — phase order

1. **Phase 0** — close out the unresolved Python latency report with an
   actual number (and finally replace the `DEFAULT_OFFSET_S` placeholder),
   before it becomes a second unknown inside a new platform. Findings
   written to `reviews/`.
2. **Phase 1** — Unity input bring-up on a dedicated reader thread at a
   deliberately chosen rate, with a timeboxed library-fallback ladder
   (P/Invoke port → Python UDP bridge) and an explicit gyro unit-scale
   check against the Python captures.
3. **Phase 2** — faithful C# port (`double` precision, UnityEngine-free
   assembly) of the pure-logic modules and their tests, verified by
   golden-trace parity against Python outputs on the real capture files.
   No threshold changes, no redesign.
4. **Phase 3** — metronome-only scene first (clock-domain mapping proven in
   isolation), then the 2.5D scene and batting loop, carrying over Phase
   0's grace-period conclusion; fresh `calibration.json`, never imported
   from Python.
5. **Phase 4** — real hardware validation and tuning against Unity-side
   captures, not inherited Python numbers; unit-scale and latency
   re-measured, ideally A/B'd against the Python stack.
6. **Phase 5** — first real human playtest of the Unity build, with the
   same rigor about "feels off" reports that Phase 0 exists because of.

Do not skip Phase 0. A latency complaint that was never pinned down in the
simpler, already-instrumented Python environment will be strictly harder to
diagnose once it's tangled up with a new engine's audio/threading stack.
