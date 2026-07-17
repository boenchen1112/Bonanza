# Build Plan v2 — All-Star Swingers (Post-Audit Stabilization, Tuning, and v1 Closeout)

## 0. Context (read this first)

v1 (`Swinger_Build_Plan_v1.md`, Phases 0–4) got all nine modules
code-complete, but the two-pass review in `reviews/Bug_Audit_2026-07-17.md`
found real defects before any of it could be trusted: a crash path (A4), a
sampling-rate bug that was inflating live ictus detections by ~50% (A5),
and — in the deeper Fable pass — ten more (F1–F10), several of which mean
the sharpness axis and the calibration seed have never actually worked
correctly on real hardware. **Claude Code CLI is fixing these now, in the
order the audit specifies** (F3+A5 → F2 → F1 → A4 → F4 → F5+F6 → F7–F10).

This document does not change v1's scope — still 2/4 time, right Joy-Con
only, fixed-BPM metronome, no Unity. It picks up where v1 left off: getting
from "code-complete, bugs found" to the actual "v0 prototype-ready" bar v1
Section 0 defined, which nothing has reached yet, since the rendering/menu
loop has never once been exercised by a human or a test.

**Do not skip straight to Phase 4 tuning or manual playtesting once the
fixes land.** The audit is explicit that several of these bugs compound —
tuning thresholds or capturing a "clean" calibration before F3 (unified
sampling rate) is verified would waste that work. Phase order below exists
to prevent that.

---

## 1. Phase 3.5 — Bugfix Verification (gate before anything else)

**Goal:** don't take the fix PR on faith. Re-verify each audit item against
running code and real data, the same way the audit itself was produced —
independent verification, not just reading the diff.

Per fix group, from `reviews/Bug_Audit_2026-07-17.md`'s "Summary for
Sonnet":

- **F3 + A5 (unified sampling rate + duplicate-skip):** confirm the same
  poll rate is actually used at all three call sites (`joycon_stream.py`'s
  own capture path, `game.py::run_game()`, and
  `settings_menu.py::run_calibration()`) — grep for `clock.tick(` and
  `poll_interval_s` across all three files, don't just check one. Recapture
  a fresh Phase 0 CSV and confirm the raw-vs-deduplicated event count now
  matches (it should be 1:1 — no more artificial inflation). Confirm
  `RISE_RATE_THRESHOLD` was re-expressed in a rate-independent unit
  (units/second, not units/sample) per the audit's fix note.
- **F2 (max-derivative sharpness):** confirm `IctusEvent` has a new field
  for it, confirm `ictus_detector.py` actually computes it during `RISING`,
  confirm it's threaded through to `game.py::_judge` and the calibration
  seed computation in `settings_menu.py` — not just added to the dataclass
  and left unused. Confirm the `rise_duration == 0` edge case is guarded
  (construct a synthetic single-sample-peak swing and check it doesn't
  bucket as "Low").
- **F1 (fallback sharpness range):** confirm the new constant was derived
  from a **fresh** capture taken *after* F3 landed, not recomputed from the
  old (rate-contaminated) `swings.csv`. The old file's sharpness values are
  themselves suspect until F2+F3 are both in.
- **A4 (`max_measures` guard):** don't just read the diff — rebuild the
  synthetic full-loop probe from this session's audit (a fake gyro stream
  that never crosses a miss) and run it for >500 measures. Confirm a clean
  round-end instead of `IndexError`.
- **F4 (non-blocking popup):** run a full synthetic or real round and check
  `session_log.windup_interval_variance()` returns a real number, not
  `None` — that's the direct symptom of the old blocking-wait bug.
- **F5 + F6 (calibration save guard; seed from matched swings only):** test
  the calibration-failure path (stand still / cover the Joy-Con during
  calibration) and confirm `calibration.json` is left untouched rather than
  overwritten with an empty or noise-derived seed. Separately, confirm the
  seed only contains values from swings that were actually matched to a
  practice measure, not every detected event during the calibration window.
- **F7–F10:** spot-check each; none of these block Phase 4, but note
  whether they were actually addressed or deferred on purpose.

**Exit criterion:** `tests/` suite still green, the synthetic full-loop
probe survives a 500+ measure run without a miss, a freshly recaptured
Phase 0 CSV shows matching raw/deduplicated event counts, and a full
synthetic or real round produces a non-empty wind-up variance stat. Do not
proceed to Phase 4 until all of the above hold — tuning against a still-
contaminated sampling path would need to be redone.

---

## 2. Phase 4 — Threshold Tuning Pass (now actually unblocked)

This is v1's original Phase 4, deliberately not run until now: F3 confirms
thresholds are tuned against one consistent, physically-meaningful sample
rate; F2 confirms sharpness is a real per-swing measurement instead of dead
code. Doing this earlier would have produced numbers that don't survive the
bugfix.

- Recapture several Phase 0 CSVs at deliberately varied swing intensity
  (soft / medium / hard) under the now-fixed poll rate, mirroring the
  approach `tests/test_ictus_intensity_invariance.py` used synthetically —
  this time with real human swings.
- Retune `RISE_RATE_THRESHOLD`, `PEAK_MIN_THRESHOLD`, `DROP_FRACTION`,
  `DROP_WINDOW_S`, `REFRACTORY_S` against this real data, now in the
  rate-independent units F3's fix established.
- Retune the sharpness bucket boundaries (replacing the corrected F1
  constant with data-driven bounds if the fresh capture's spread differs)
  now that sharpness is measured via max-derivative rather than
  peak/rise_duration.
- Replay each captured CSV through the tuned detector and manually confirm
  the event count matches the actual number of swings made, across all
  three intensities — this is the same validation method used to catch A5,
  just done as confirmation instead of discovery.

**Exit criterion:** across soft/medium/hard real captures, detected event
count equals actual swing count with zero false positives and zero missed
swings, and sharpness values visibly separate into distinct low/mid/high
bands rather than clustering (the F1 symptom) or inverting (the F2
symptom).

---

## 3. Phase 5 — Full Manual Playtest (the part that's never happened)

Every verification so far — this session's synthetic probe, the CSV
replays, the offline test suite — has deliberately bypassed pygame
rendering and the Settings/Calibrate menu navigation to stay fast. Per the
audit's A6/A7 and F10, **no one and nothing has ever exercised that code
path.** This phase is where that finally happens.

- Add the flat placeholder batter/ball visuals v1 Section 4d called for
  and F10 flagged as still missing — legibility of the judgment popup
  matters more than art quality here, but there should be *something* on
  screen beyond text.
- Run a real, human, end-to-end round: launch `game.py`, hear the
  metronome, swing once per measure, see the Perfect/Great/Good/Miss +
  Bunt/Line Drive/Home Run popup update live (not delayed by the F4 bug),
  reach 3 misses, reach the summary screen and confirm it shows real
  offsets, real sharpness values, and a real (non-`None`) wind-up variance.
- From the summary screen, press `S`, navigate to Calibrate with arrow
  keys, run a calibration pass, confirm the completion message shows a
  sane offset, then quit and relaunch to confirm both the offset and the
  sharpness seed persisted (this directly re-tests F5/F6 under real
  conditions, not just the failure path).
- Hand the controller to someone else — ideally someone who hasn't seen the
  code — with zero explanation beyond "swing when you hear the click," and
  watch whether they naturally get through a round. This is the literal
  wording of v1 Section 0's "v0 prototype-ready" bar; it has not been
  attempted with a second person yet.

**Exit criterion:** v1 Section 0's definition of "v0 prototype-ready" is
met, for real, with a human who is not the developer.

---

## 4. Phase 6 — v1 Closeout and Unity Port Readiness

Once Phase 5 passes, v1 is actually finished — not just code-complete.
Close it out properly before treating this as a base for the next stage:

- **Restore `primer.md`.** The audit's A8 found only a stale
  `primer.md.lnk` in the project root — a Windows shortcut, invisible to
  git and to anything running outside that machine. Copy the real file
  into the project or recreate it; a link that only resolves on one
  person's machine isn't a deliverable.
- **Update `Swinger_Build_Plan_v1.md` Section 6** (deferred scope) with
  anything the audit or tuning pass surfaced that changes what's safely
  deferrable — e.g. if F3's rate-unification work revealed something about
  real device behavior that affects the dual-Joy-Con or higher-tempo
  deferred items, note it there rather than losing it.
- **Confirm `.gitignore` covers `calibration.json`** (F9c) — it's
  runtime-generated per the plan and shouldn't be checked in, especially
  now that it also carries the sharpness seed.
- **Unity port readiness note.** Per the platform roadmap in v1 Section 0,
  confirm which modules are already pygame/hardware-free pure logic
  (`beat_schedule.py`, `ictus_detector.py`, `scoring.py`, most of
  `calibration.py`, `session_log.py` — this was true even before the
  bugfixes, per the audit's static review) and write a short note on what
  actually changes when porting to C#: the input layer (Joy-Con read →
  Unity's input system) and the rendering layer (pygame draws →
  Unity scene), nothing in the detection/scoring/calibration logic itself.
  This is not a task to start building — just confirm the separation the
  code already has still holds after the bugfix, so the "ports largely
  as-is" claim in v1 Section 0 remains true rather than assumed.

**Exit criterion:** `primer.md` exists as a real file in the project,
`Swinger_Build_Plan_v1.md` Section 6 reflects current reality, and a short
written note confirms which files are pure logic vs. presentation/input —
ready for whoever picks up the Unity stage next.

---

## 5. Explicitly out of scope for v2

Everything in v1 Section 6 still applies unchanged: per-axis/dominant-axis
calibration, beat 2 scoring, real song/audio-driven tempo, the
conductor-leads-audio inversion, tempo ramps/time-signature changes, dual
Joy-Con, the Unity 2.5D port itself (Phase 6 above only *assesses*
readiness, doesn't start it), VR, difficulty-adaptive systems, and any
minigame beyond All-Star Swingers. None of the F1–F10 fixes should grow
into any of these — if a fix seems to require touching one of them, stop
and flag it rather than expanding scope mid-fix, per v1's closing
principle.

---

## Summary — phase order

1. **Phase 3.5** — verify the fix PR against running code and fresh data,
   item by item, not just by reading the diff.
2. **Phase 4** — tune detection/sharpness thresholds against real data, now
   that the sampling path is trustworthy.
3. **Phase 5** — the first real end-to-end human playtest, including
   rendering and the Settings/Calibrate menu, which nothing has touched
   until now.
4. **Phase 6** — close out v1 for real: restore `primer.md`, update the
   deferred-scope doc, confirm the pure-logic/presentation split still
   holds for the eventual Unity port.

Do not reorder these — each phase's validity depends on the one before it
having actually passed, not just been claimed.
