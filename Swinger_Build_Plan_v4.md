# Build Plan v4 — All-Star Swingers → Conducting Trace Trainer

## 0. Context (read this first — the purpose has changed)

Every prior phase (v1–v3) was validation infrastructure, not the product.
The baseball minigame existed to prove three things could work together:
real Joy-Con Bluetooth input, a rule-based ictus/timing detector, and a
Unity build wired to both. **That's now proven.** The actual goal, per the
original NTNU research topics this project grew out of, is a conducting
*technique trainer*, and the real gap it needs to close is that a student
practicing alone has no way to see their own gesture — they can only find
out, after the fact, whether one instant (the downbeat) was on time and
sharp. That's a timing detector, not a conducting trainer.

**What v4 actually asks for: project the captured motion onto the screen
as a live trace, the way handwriting practice shows the pen's path against
a guide letter, so a student can see their own conducting pattern's shape
— not just get a Perfect/Miss verdict on one point of it.** This is a
materially different and harder problem than anything built so far, and
this document says so plainly rather than pretending it's an incremental
feature.

One piece of good news up front: **the data pipeline doesn't need to
change to support this.** `joycon_stream.py`, `JoyConUdpReceiver.cs`, and
the Python bridge all already carry `gx, gy, gz` individually — the
collapse to a single scalar `magnitude` (v1's explicit, deliberate
simplification) happens only in what consumes those samples
(`IctusDetector`), not in what's captured or transmitted. The three axes
needed for a path have been flowing through the system since v1 Phase 0.
What's missing is entirely downstream: nothing currently turns a sequence
of `(gx, gy, gz)` samples into a 2D shape, and nothing renders one.

This document keeps the four near-term items from the last brainstorm as
**Phase A** — they're real, scoped, and cheap, and doing them first means
the trace-trainer work (Phase B onward) starts from a synced, non-buggy
base instead of building new features on top of the G1/G2/S1/T1 gaps this
session found still forked into Unity.

---

## 1. Phase A — Close the near-term gaps before extending scope

**1. Port the fixes pass.** Verified during this session that
`UnitySwinger/Assets/Scripts/Logic/MeasureJudge.cs` and `Scoring.cs` still
carry the pre-audit Python state, forked before today's fixes:

- `MeasureJudge.cs` line 88: `if (now < windowEnd + JudgeGraceS) return null;`
  — the exact G1 bug (judges ~625ms late even once the event is already
  locked). Apply the same fix as Python: judge immediately when
  `_lockedEvent != null`, keep the grace wait only for the no-event case.
- `JudgeGraceS` (line 32) is still a flat `0.25` constant, not derived from
  the detector's own bound and not clamped to `beat_interval/2` — G2,
  unfixed, same silent-break-above-120-BPM risk.
- `Scoring.cs` line 39: `FallbackSharpnessLowHigh = (60000.0, 420000.0)` —
  S1's flawed pooled-capture range, carried over verbatim.
- `MeasureJudgeTests.cs` never sets `MaxDerivative` on synthetic events —
  T1's test blind spot, validating the fallback branch the real game
  doesn't use. Fix the test fixture the same way the Python fix did, before
  trusting this suite as a regression net for Phase B's changes.

**2. Unity becomes the sole game.** Per your call: stop developing
`src/game.py` as a product. Repurpose `src/` as capture/tuning tooling only
(the Phase 0/4 capture scripts, offline replay/validation, the UDP bridge
`joycon_udp_bridge.py` that Unity's input path depends on) — update
`CLAUDE.md` and the folder-structure notes to say this explicitly, so a
future session doesn't accidentally treat `game.py`'s pygame loop as
something to keep fixing in parallel.

**3. J1/J2 Unity-side equivalent, checked not assumed.** Confirmed this
session: `JoyConUdpReceiver.cs`'s `ReceiveLoop()` calls `_client.Receive()`
with no timeout — if the Python bridge dies or the UDP stream goes quiet,
the receive thread blocks forever with no on-screen indication, and
`RunCalibrationFlow()`'s `while (measureIdx < practiceMeasures)` loop would
simply hang waiting for samples that never arrive, no timeout, no visible
"input lost" state. This is J2's failure mode, structurally present even
though the input path is UDP, not direct HID. J1 itself (double HID handle)
is moot here since Unity never opens the device directly. **Fix:** add a
receive timeout (`UdpClient.Client.ReceiveTimeout`) with a heartbeat/last-
sample-age check surfaced in the UI ("Joy-Con signal lost"), and a timeout
on the calibration wait loop that fails visibly instead of hanging.

**4. Batter/ball visuals.** Still placeholder-or-absent per the last audit
(H7). Add minimal 3D placeholders in the existing `Batting.unity` scene —
this is the last item needed to call the baseball prototype actually done,
even though it's being retired as the primary product; it's cheap, and an
honest "prototype-complete" scene is a better handoff than an unfinished
one when v4 shifts focus away from it.

**Exit criterion:** `tests/` (Python, frozen) and Unity Test Framework both
green, including a fixed `MeasureJudgeTests.cs`; a Unity playtest no longer
shows the ~625ms judgment delay; disconnecting the Joy-Con mid-round shows
a visible message instead of a silent hang; `Batting.unity` has visible
batter/ball placeholders. This closes out v3 for real before v4 begins.

---

## 2. Phase B — Research spike: is a usable gesture trace even recoverable?

**Do this before any Unity trace-rendering work.** This is the one phase
in this whole project so far with a real chance of "no, not like this" —
say that plainly rather than committing engineering time to Phase D first.

**The physically right representation, and why it's more tractable than it
sounds:** conducting is a rotational gesture — the baton/hand traces an
angular pattern pivoting from the wrist/elbow, it doesn't translate through
space in a way that matters to the pattern's shape. That means the target
is **orientation over time** (pitch/yaw, or roll/pitch depending on grip),
not position — which sidesteps the much harder problem double-integrating
accelerometer data into position would create (that drifts badly within
seconds; nobody should attempt it here). Orientation from gyro alone still
drifts, but conducting patterns are periodic and beat 1 is already a known,
detected reference point — the same insight `calibration.json`'s offset and
`JUDGE_GRACE_S` already exploit. **Resetting the integrated orientation at
each detected downbeat bounds drift to within a single measure**, which may
be short enough to look right on screen even with a plain gyro integration,
no accelerometer fusion required.

**Spike plan (offline, Python, using hardware already in hand):**

- Capture real conducting gestures — not baseball swings — across at least
  2/4 and 3/4 patterns (down-up; down-right-up), with a few repetitions
  each, using the existing raw-axis capture path (`joycon_stream.py`
  already logs `gx, gy, gz`, no new capture code needed).
- Prototype the simplest possible orientation integration (Euler/gyro-only,
  reset at each detected downbeat) and plot the resulting 2D projection
  against the known canonical shape for that time signature, for several
  repetitions. This is a plotting script, not game code — keep it in
  `tools/` or a new `research/` folder, disconnected from `src/`'s frozen
  game logic.
- **Don't validate by eyeballing the plot — score it.** "Does this look
  like a conducting pattern" is exactly the kind of vague, hard-to-pin-down
  judgment that turned one Python playtest report into a multi-session
  investigation (Phase 0, v3). Avoid repeating that here:
  - Resample both the reconstructed trace and the canonical reference
    pattern to the same number of points along their path length (e.g. 50
    points via arc-length interpolation), so they're directly comparable
    regardless of swing speed or sample count.
  - Compute a normalized shape-distance — average point-to-nearest-
    reference-point distance, divided by the reference pattern's own
    bounding size, so the number is scale-independent and comparable
    across capture sessions. Report this number per repetition, not just a
    plot.
  - Set a threshold empirically from the first batch of real captures
    (e.g., "distinct repetitions of the same pattern should score closer
    to each other than to a different time signature's pattern") rather
    than picking one blind.
  - The plots still matter — keep generating them — but as a way for
    whoever reviews this (including the agent doing the analysis, who can
    view the generated images directly rather than relying on the human
    to describe them) to sanity-check the number, not as the verdict
    itself.
- Decide, from the scored results (not the plots alone), whether: (a)
  gyro-only with per-beat reset scores consistently close to the reference
  and tightly clustered across repetitions, (b) it needs a lightweight
  complementary filter (blend in accelerometer tilt to correct gyro drift
  between resets) to close the gap, or (c) the drift is bad enough within a
  single beat that this representation needs to be abandoned or
  substantially constrained (e.g., only show the trace live and never trust
  it for scoring, or heavily smooth/normalize it rather than render it raw).

**Exit criterion:** a written verdict (`reviews/` — same discipline as
every prior phase) with the per-repetition shape-distance numbers and
plots attached, stating which of (a)/(b)/(c) above is true, and if (b),
which filter and how much accelerometer data it needs alongside the gyro
stream already being captured. **Do not start Phase D until this verdict
exists.** If the answer is "this doesn't work well enough," that's a real,
useful result — it means the trainer's visual feedback needs a different
design (e.g., a simplified schematic replay rather than a raw traced
path), and it's far cheaper to learn that from a scored Python spike than
from a half-built Unity feature.

---

## 3. Phase C — Generalize beyond 2/4

Independent of Phase B's verdict, and can happen in parallel: the trainer
needs multiple time signatures, which v1–v3 explicitly deferred.

- Extend `BeatScheduleConfig` beyond a fixed `beats_per_measure` assumption
  baked around 2/4 — 3/4 and 4/4 at minimum, since those cover the
  canonical down/side/up-pattern vocabulary conducting students actually
  learn.
- Define each pattern's canonical shape as a small set of reference
  points/segments per time signature (e.g., 4/4's down-left-right-up), in
  whatever coordinate space Phase B's verdict settles on — this is the
  "guide letter" a student's live trace gets shown against.
- Keep the existing scored-beat-1-only detection working per signature; it
  remains useful (a per-beat timing/sharpness verdict is still valuable
  feedback) even once path visualization exists alongside it, not instead
  of it.

**Exit criterion:** a canonical reference pattern defined and renderable
(even as a static reference shape, before live tracing exists) for 2/4,
3/4, and 4/4.

---

## 4. Phase D — Live trace rendering and pattern feedback (gated on Phase B)

Only start this once Phase B has a verdict and Phase C has reference
patterns to trace against.

- Render the student's live gesture as a drawn path (the traced-letter UX
  you described), using whichever representation and drift-mitigation
  Phase B validated.
- Overlay the canonical pattern from Phase C as a guide the student traces
  against, per selected time signature.
- Design a path-fidelity measure once there's a real trace to score against
  a real reference — options to evaluate empirically rather than pick
  blind: distance-to-reference-curve sampled at intervals, or a
  Fréchet/DTW-style shape comparison if simple point-distance proves too
  sensitive to timing variation. This is a genuinely open design question;
  don't commit to one without testing against a few real captured
  gestures.
- Decide how (or whether) this coexists with the existing timing/sharpness/
  hit-tier scoring — likely both are shown (a per-beat verdict plus a
  whole-gesture shape score), but that's a product decision to make once
  there's something on screen to react to, not before.

**Exit criterion:** a student's live conducting gesture is visibly drawn
against a reference pattern for at least one time signature, and there's a
first-pass (even if rough) numeric or visual sense of "how close was that
to the reference shape."

---

## 5. Phase E — Playtest with an actual conducting learner

This is the phase every prior build plan's "hand it to someone who isn't
the developer" step was implicitly building toward. Now it means something
closer to the original research intent:

- Get a real conducting student or choir member (this is where 怡潔's
  students, mentioned as the target end-user across the original five
  research topics, become directly relevant) to try tracing a pattern
  against the on-screen guide.
- Ask specifically whether the visual trace helps them notice something
  about their own gesture they couldn't tell from the timing/sharpness
  verdict alone — that's the actual hypothesis this whole pivot is testing.

**Exit criterion:** qualitative feedback from a real learner on whether the
traced-gesture visualization is legible and useful, not just technically
functional.

---

## 6. Explicitly out of scope for v4

Left/dual Joy-Con, VR (still the long-term stage after this one), tempo
ramps and audio-driven tempo, additional baseball-style minigames (paused,
not abandoned — the pure-logic assembly split still supports adding them
later), and any machine-learned gesture scoring (Phase D's fidelity measure
should stay a simple geometric comparison for now, not a model — that's a
much bigger and separate research question if simple approaches prove
insufficient).

---

## Summary — phase order

1. **Phase A** — sync Unity to today's Python fixes, retire `game.py` as a
   product, harden the UDP input path against silent stalls, finish the
   baseball scene's visuals. Closes out v3 properly.
2. **Phase B** — the load-bearing research spike: can gyro-integrated
   orientation, reset per downbeat, actually produce a recognizable
   conducting-pattern trace? Answer this in Python with plots before
   building anything in Unity. A "no, not like this" is a valid, useful
   outcome here.
3. **Phase C** — generalize the beat/pattern model to 3/4 and 4/4,
   independent of Phase B, can run in parallel.
4. **Phase D** — build the live trace + reference-pattern overlay in Unity,
   gated on Phase B's verdict.
5. **Phase E** — put it in front of an actual conducting learner and find
   out if seeing their own gesture shape is the missing piece this project
   has been aiming at since the original research topics.

Do not start Phase D before Phase B has a written verdict. Every prior
phase in this project that skipped validating a hard assumption first
(the sampling rate, the sharpness metric, the judge timing) ended up
costing more to unwind later than it would have cost to check up front.
