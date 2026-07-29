# Primer

## Active project
Swinger — All-Star Swingers (D:\VS Code\Swinger). On `Swinger_Build_Plan_v5.md`:
reframing the validated web hand-tracking conducting-trace tool
(`research/hand_tracking_web/`) into a complete playable game, not a research
spike. **Baseball minigame + Unity/Joy-Con port are paused, not abandoned**,
parked in current Unity state until v5 hits its own "complete" bar. **v5
Phases 0 and 1 are done. Phase 2 is next**, but Phase 1's changes — including
this session's follow-up fixes — still need a live playtest.

## Completed (chronological, most recent last)
- v5 Phase 0 (commits `88d20e0`, `330a8c5`, `418b746`): Unity 6000.4.1f1→
  6000.5.5f1 migration fallout committed (confirmed mechanical), `Assets/
  _Recovery/` deleted (confirmed crash-autosave junk), dead gyro-orientation
  files labeled, camera/model-load failure now shows a message instead of
  hanging, primer.md rewritten (was 2 weeks/4 build-plan-versions stale).
  `service.conf.lock`/`system.conf.lock` left alone per explicit user answer
  — not junk, don't delete or gitignore.
- v5 Phase 1 (commit `75ca32b`): reference-image calibration (2-point
  similarity transform via `find_anchors.py`, baked into
  `REFERENCE_IMAGE_ANCHORS`), image drawn directly into canvas replacing the
  gray vector line for 2/4, trace color → gray, `shape_distance`/
  `resample_path` ported to JS for live per-measure scoring + session summary,
  `VIDEO_ASPECT` fix for MediaPipe's independent x/y normalization, `/score`
  endpoint added to `server.py`. No fail state (formative practice).
- **This session's follow-up (commit `4f9886a`)**, from live-test feedback:
  1. **Trace too small to match reference** — real unit mismatch:
     `REFERENCE`'s numbers are an abstract shape scale, but the trace was raw
     camera-frame fractions. Fixed with **two-point calibration**: hold TOP,
     then hold BOTTOM (beat 1's lowest point); the ratio of the user's real
     physical range to `REFERENCE`'s prep→beat1 distance becomes
     `calibScaleFactor`, applied to every live sample. Matches the user's own
     suggested fix (calibrate to the user's real range, don't force-fit to a
     fixed reference size).
  2. **Reset erasing the just-finished top→bottom stroke** — the downbeat
     click (which triggers reset) fires exactly when the hand reaches beat 1,
     wiping the trace the instant it's complete. Deliberately did **not**
     switch to live bottom-of-motion detection (the user's alternate
     suggestion) — that's the same live-extremum approach that already
     misfired for top-detection (real motion bounces per-beat, not
     per-measure), so it'd likely fail the same way pointed at the opposite
     extremum. Instead: `lastCompletedTraceXY` keeps the just-finished trace
     visible (faded) for one full extra measure, drawn under the live trace.
  - Verified via `node --check` (JS) and `python -m py_compile` (server.py)
    only — **not yet tested live**.

## Exact next step
1. **Playtest required**: run `research/hand_tracking_web/server.py`, open
   localhost:8090, run the two-point TOP/BOTTOM calibration flow, and confirm
   (a) the trace now visibly matches the reference image's scale, (b) the
   faded just-completed stroke is actually visible for the extra measure
   instead of vanishing, (c) per-measure match % feels reasonable. Report
   back before treating this round of fixes — or Phase 1 overall — as proven.
2. Once confirmed, move to **v5 Phase 2** (`Swinger_Build_Plan_v5.md` §4):
   - Get real 3/4 and 4/4 reference diagrams from the user (same treatment
     2/4 got: photo + anchor calibration) — currently unverified schematic
     guesses with only a placeholder vector line.
   - Write a closing note on v4 Phase B (gyro approach superseded by the
     working webcam pivot) — update/replace
     `reviews/Phase_B_Spike_Status_2026-07-28.md`.
   - **Explicit decision needed from the user**: stay a browser tool, or
     eventually port to Unity (MediaPipe has Unity plugins — separate
     bring-up effort)? Don't default to "Unity is the platform" from v3/v4.
   - Remaining UX polish: audible distinction between calibration countdown
     and the calibrating-hold itself (currently identical); a replay/
     practice-again flow that doesn't need a full page reload.

## Open blockers / notes
- **Standing rules** (memory, not in this repo): commit automatically per
  verified bug fix or phase; always persist logs + final-state snapshots for
  live/interactive tools, never just print/show — both applied this session.
- `src/swings_counted.csv` — unexplained standing local modification, never
  touch without being asked.
- `service.conf.lock` / `system.conf.lock` at repo root — leave alone per
  explicit user answer.
- Python: use `C:\Users\user\AppData\Local\Programs\Python\Python313\
  python.exe` for anything needing pygame/OpenCV (e.g. `find_anchors.py`) —
  default `python`/`py -3` resolves to 3.14, no pygame wheel yet. Not needed
  for the web tool itself (pure browser JS + stdlib-only `server.py`).
