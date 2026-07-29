# Primer

## Active project
Swinger — All-Star Swingers (D:\VS Code\Swinger). Started as a v1 Python
conducting-rhythm baseball prototype (`Swinger_Build_Plan_v1.md`), ported
to Unity as v3 (`UnitySwinger/`, 2.5D fixed-camera, Super Mario Party
style). v4 (`Swinger_Build_Plan_v4.md`) pivoted the *design* from a
timing-verdict minigame to a conducting-gesture trace trainer; v4 Phases A
and C are done. v4 Phase B (can motion be tracked well enough to show a
student their own conducting shape?) asked the right question but got the
wrong first answer: Joy-Con gyro orientation integration was tried and
correctly diagnosed as wrong (gyro measures rotation, not the hand's
actual position; real hardware showed tremor at thousands of deg/s, no
stable shape signal). The pivot to webcam hand-position tracking worked.

**`Swinger_Build_Plan_v5.md` is now the active plan.** It reframes the
validated web hand-tracking tool (`research/hand_tracking_web/`) as the
thing to build into a complete playable game, not a research spike. **The
baseball minigame and the Unity/Joy-Con port are paused, not abandoned** —
parked in their current Unity state until v5 reaches its own "complete"
bar. v5 also folds in an audit of the current tool (Phase 0 fixes) before
the four requested feature changes (Phase 1) and further polish (Phase 2).

## Completed (chronological, most recent last)
- v1: 17-bug audit fully fixed, offline tests green (see git history —
  this file used to describe only this phase and had gone stale by 2 weeks
  and 4 build-plan-versions; that staleness is exactly what v5's audit
  finding A1 flagged and this rewrite fixes).
- v3: Unity port of the v1 logic (`Assets/Scripts/Logic/` — engine-free,
  kept in sync with `src/`'s Python modules).
- v4 Phase A: G1/G2/G3/J1/J2/etc. bug-audit fixes ported into
  `MeasureJudge.cs`/`JoyConUdpReceiver.cs`, signal-loss UI, Unity session
  log export. Verified via Unity batch-mode tests (8/8 green) and a real
  playtest (6 Perfect/5 Great/3 Miss, no drift).
- v4 Phase C: canonical 2/4, 3/4, 4/4 conducting reference patterns in
  `src/conducting_patterns.py` (`resample_path()`, `CANONICAL_PATTERNS`).
  2/4 later corrected against a real hand-drawn diagram the user supplied
  (prep top-left → down to beat 1, the lowest point → rebound up-right to
  beat 2). 3/4 and 4/4 remain unverified schematic guesses — no real
  diagrams for those two yet.
- v4 Phase B (superseded, see above): `research/gesture_trace_spike.py`'s
  `integrate_orientation()` and `research/live_trace_view.py` implement
  the gyro approach — **both now explicitly marked dead/superseded in
  file-header comments** (v5 Phase 0, audit A6). `shape_distance()` and
  `resample_path()` in the same spike file are still alive and reused by
  the web tool.
- Pivot validated: `research/live_position_view.py` (OpenCV color-blob,
  proof of concept) → `research/hand_tracking_web/` (MediaPipe
  `HandLandmarker`, the real tool). Iterated through real bugs found by
  live testing: non-mirrored feed, wrong reference shape, origin landing
  wherever a timer caught the hand (fixed via an explicit calibration
  hold), a live "top-detection" approach that double-fired per measure
  (replaced by the calibration + metronome-driven reset), 3 separate UI
  panels merged into one canvas. Latest tool commit before v5: `3231252`.
- **v5 Phase 0 (this session, in progress)**: dead-code files labeled
  (`live_trace_view.py`, `integrate_orientation()`); `.catch()` added
  around `ensureModelAndCamera()` so a denied/missing camera shows a
  message instead of hanging on "Loading model..." forever; this file
  rewritten for real. **Still open**: committing/triaging the working
  tree (see below) and the rest of Phase 0's exit criterion.

## Exact next step
Finish v5 Phase 0, then move to Phase 1 (see `Swinger_Build_Plan_v5.md`):
1. **Triage the working tree** (v5 audit A2) — needs a user decision on:
   Unity `Packages/manifest.json`, `packages-lock.json`,
   `ProjectSettings/PackageManagerSettings.asset`, `ProjectVersion.txt`,
   untracked `ProjectSettings/PhysicsCoreProjectSettings2D.asset` (side
   effects of an earlier 6000.4.1f1→6000.5.5f1 migration, never
   confirmed deliberate); untracked `Assets/_Recovery/` (Unity crash-
   autosave debris); two empty untracked root files, `service.conf.lock`
   and `system.conf.lock`, of unknown origin — not created by any tool
   used this session, don't delete blindly. `Swinger_Build_Plan_v5.md`
   itself and `research/captures/conduct_24_take2*` are real deliberate
   artifacts and should just be committed.
2. **Phase 1** — the four requested changes to `hand_tracking_web/`:
   calibrate the reference image's pixel space into the same logical unit
   space `REFERENCE` uses (audit A3 — currently just a decorative
   thumbnail), render it centered/semi-transparent on the canvas in place
   of the gray vector line (only for signatures with a real photo — 2/4
   only, so far), change the trace color to gray, and port
   `shape_distance()`/`resample_path()` into JS for real per-measure
   scoring + a session summary.
3. **Phase 2** — real 3/4 and 4/4 reference diagrams, a closing note on
   v4 Phase B's now-superseded question, an explicit decision on whether
   this stays a browser tool or eventually ports to Unity, remaining UX
   polish (calibration countdown audible cue, replay-without-reload flow).

## Open blockers / notes
- **Standing rules** (memory, not in this repo): commit automatically per
  verified bug fix or phase; always persist logs + final-state snapshots
  for live/interactive tools, never just print/show. Both were already
  being followed in `hand_tracking_web/` before v5 started.
- Unity migration files and `Assets/_Recovery/` (see step 1 above) have
  been sitting uncommitted/untriaged across at least two sessions now —
  don't keep deferring without at least asking the user directly.
- `src/swings_counted.csv` — still an unexplained standing local
  modification, never touch without being asked.
- Python: use `C:\Users\user\AppData\Local\Programs\Python\Python313\
  python.exe` for anything needing pygame — default `python`/`py -3`
  resolves to 3.14, no pygame wheel yet. Not needed for the web tool
  itself (pure browser JS + a stdlib-only `server.py`).
