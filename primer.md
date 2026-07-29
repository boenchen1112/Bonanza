# Primer

## Active project
Swinger — All-Star Swingers (D:\VS Code\Swinger). Started as a v1 Python
conducting-rhythm baseball prototype (`Swinger_Build_Plan_v1.md`), ported
to Unity as v3 (`UnitySwinger/`, 2.5D fixed-camera, Super Mario Party
style). v4 (`Swinger_Build_Plan_v4.md`) pivoted the *design* from a
timing-verdict minigame to a conducting-gesture trace trainer; v4 Phases A
and C are done. v4 Phase B asked the right question (can motion be tracked
well enough to show a student their own conducting shape?) but got the
wrong first answer: Joy-Con gyro orientation integration was tried and
correctly diagnosed as wrong (gyro measures rotation, not the hand's
actual position; real hardware showed tremor at thousands of deg/s, no
stable shape signal). The pivot to webcam hand-position tracking worked.

**`Swinger_Build_Plan_v5.md` is now the active plan**, reframing the
validated web hand-tracking tool (`research/hand_tracking_web/`) as the
thing to build into a complete playable game, not a research spike. **The
baseball minigame and the Unity/Joy-Con port are paused, not abandoned** —
parked in their current Unity state until v5 reaches its own "complete"
bar. **v5 Phases 0 and 1 are both done this session.** Phase 2 is next.

## Completed (chronological, most recent last)
- v1/v3/v4 Phases A+C: see prior entries in git history if needed — the
  short version is bug-audit fixes ported into Unity's `MeasureJudge.cs`
  etc. (Phase A) and canonical 2/4/3/4/4/4 conducting reference patterns
  in `src/conducting_patterns.py` (Phase C, 2/4 later corrected against a
  real hand-drawn diagram; 3/4 and 4/4 remain unverified schematic
  guesses — still no real diagrams for those two).
- v4 Phase B (superseded): gyro-orientation approach correctly abandoned;
  `research/live_trace_view.py` and `integrate_orientation()` in
  `gesture_trace_spike.py` are explicitly marked dead/superseded in
  file-header comments (v5 Phase 0, audit A6) — don't build on them.
  `shape_distance()`/`resample_path()` in the same spike file are still
  alive and now also ported into the web tool (see Phase 1 below).
- Pivot validated: `research/live_position_view.py` (OpenCV color-blob
  spike) → `research/hand_tracking_web/` (MediaPipe `HandLandmarker`, the
  real tool) — calibration-based origin, metronome-driven per-measure
  reset, merged single-canvas view.
- **v5 Phase 0 (commits `88d20e0`, `330a8c5`, `418b746`)**: committed the
  Unity 6000.4.1f1→6000.5.5f1 migration fallout (confirmed mechanical,
  not feature work — user approved after seeing the diff), deleted
  `Assets/_Recovery/` (confirmed crash-autosave junk), labeled the two
  dead gyro-orientation files, added a `.catch()` around
  `ensureModelAndCamera()` so a denied/missing camera shows a message
  instead of hanging forever, and rewrote this file (it had gone stale
  describing only the original 17-bug v1 pass — 2 weeks and 4 build-plan-
  versions out of date, per audit A1). `Swinger_Build_Plan_v5.md` and a
  real 2/4 capture take (`conduct_24_take2*`) committed too.
  **Left alone per explicit user answer**: `service.conf.lock` and
  `system.conf.lock` (two empty root files of unknown origin — user said
  leave them, not junk to delete or gitignore).
- **v5 Phase 1 (commit `75ca32b`)** — the four requested changes:
  1. **Reference image calibration (audit A3)**: `find_anchors.py` locates
     the two red dots in `reference_2_4.png` (beat 1/beat 2) via HSV
     thresholding; a 2-point complex-number similarity solve (scale + ~13°
     rotation + translation — NOT a bounding-box approximation, per
     advisor guidance) maps the image's pixel space exactly onto
     `REFERENCE[2]`'s logical units. Baked into `REFERENCE_IMAGE_ANCHORS`
     in `index.html`; verified numerically (both anchor points map back
     exactly).
  2. Image now draws directly into `mainCanvas` (semi-transparent,
     centered on the calibrated origin, one composed affine transform via
     `ctx.setTransform`) — replaces the gray vector line for 2/4. 3/4 and
     4/4 still have no real diagram, so they keep the vector line, now
     explicitly labeled on-screen as a placeholder.
  3. **Real bug caught along the way**: `REFERENCE[2]`'s prep point wasn't
     at logical (0,0), but calibration puts the trace's own start there —
     recentered the whole 2/4 pattern by its prep offset (translation-
     invariant for `shape_distance`, so scoring wasn't affected, only the
     drawing).
  4. Trace color changed cream (`#f5f0e0`) → gray (`#b0b0b0`).
  5. `shape_distance()`/`resample_path()` ported into JS (in-browser, no
     network round-trip), scoring each measure the instant its trace
     resets, shown live as a match % plus a session summary (average,
     best measure) at the end. **Another real bug caught**: MediaPipe
     landmark x/y normalize against width/height separately, so a 640×480
     (4:3) feed read every trace ~33% taller than it really was —
     `VIDEO_ASPECT` now corrects `nx`. Per-measure scores persist to disk
     via a new `/score` endpoint in `server.py` (standing rule: live tools
     must log, not just render). **No fail state**, per v5's recommended
     default — formative practice, not survival.

## Exact next step
Move to **v5 Phase 2** (see `Swinger_Build_Plan_v5.md` Section 4):
1. **Get real 3/4 and 4/4 reference diagrams** from the user — the same
   hand-drawn-photo treatment 2/4 got, needed for both a real image
   reference and a complete anchor-calibrated experience across all three
   signatures.
2. **Write a closing note on v4 Phase B** — its gyro-orientation question
   is superseded by the working webcam pivot; update or replace
   `reviews/Phase_B_Spike_Status_2026-07-28.md` so it doesn't sit open
   forever.
3. **Explicit decision needed**: does this stay a browser tool, or
   eventually port to Unity (MediaPipe has Unity plugins, but that's its
   own bring-up effort)? Don't let v3/v4's "Unity is the platform" premise
   keep being assumed by default now that the working prototype is a web
   page — ask the user.
4. **Remaining UX polish**: calibration countdown vs. calibrating-hold
   currently reads as two identical waits with no audible distinction
   (audit A8); a replay/practice-again flow that doesn't need a full page
   reload.
5. Try the tool live at least once this session's Phase 1 changes haven't
   been playtested yet — confirm the anchor-mapped image actually looks
   right centered on a live hand, and that per-measure scores feel
   reasonable/legible, before treating Phase 1 as fully proven rather
   than just "compiles and the math checks out."

## Open blockers / notes
- **Standing rules** (memory, not in this repo): commit automatically per
  verified bug fix or phase; always persist logs + final-state snapshots
  for live/interactive tools, never just print/show.
- `src/swings_counted.csv` — still an unexplained standing local
  modification, never touch without being asked.
- `service.conf.lock` / `system.conf.lock` at repo root — leave alone,
  per explicit user answer this session (not junk, not to be gitignored).
- Python: use `C:\Users\user\AppData\Local\Programs\Python\Python313\
  python.exe` for anything needing pygame/OpenCV (used this session for
  `find_anchors.py`) — default `python`/`py -3` resolves to 3.14, no
  pygame wheel yet. Not needed for the web tool itself (pure browser JS +
  a stdlib-only `server.py`).
