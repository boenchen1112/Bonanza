# Primer

## Active project
Swinger — All-Star Swingers (D:\VS Code\Swinger) — conducting-rhythm baseball
minigame, v1 Python prototype. `CLAUDE.md`, `Swinger_Build_Plan_v1.md`
(original spec) + `Swinger_Build_Plan_v2.md` (post-audit stabilization plan,
appeared mid-session — Phase 3.5 bugfix-verification → Phase 4 tuning →
Phase 5 manual playtest) are source of truth.

## Completed
- All v1 modules code-complete (unchanged from prior session): `src/`'s 9
  modules + 4 offline tests, all green.
- **This session: fixed all 17 findings in `reviews/Bug_Audit_2026-07-17.md`**
  (A4, A5, A8, F1–F10), in the order the audit's "Summary for Sonnet"
  specified:
  1. **F3+A5** — unified poll rate (`joycon_stream.POLL_INTERVAL_S`, 200Hz)
     across all 3 call sites, consecutive-duplicate-read skip in
     `stream()`, `RISE_RATE_THRESHOLD` re-expressed as units/**second**
     (was units/sample) computed from actual inter-sample dt in
     `ictus_detector.py`. Verified: replaying real `src/swings.csv` through
     the fixed pipeline now yields 10/10 events (was 15 raw / 5 real
     swings — the ~50% inflation A5 flagged).
  2. **F2** — `IctusEvent.max_derivative` field, computed during RISING in
     `ictus_detector.py`, threaded through `game.py::_judge` and
     `settings_menu.py`'s calibration seed (falls back to peak/rise_duration
     only when max_derivative is unset, e.g. hand-built synthetic events in
     tests). Verified: a synthetic `rise_duration==0` violent swing now
     buckets "High" instead of the old "Low"/Bunt bug.
  3. **F1** — `FALLBACK_SHARPNESS_LOW_HIGH` changed from placeholder
     (150–600) to (120000–330000), derived by reprocessing the existing
     `swings.csv` through the fixed pipeline (**not** a fresh capture — no
     hardware available; flagged as unverified-until-recaptured in the code
     comment and must be redone per Build Plan v2 Phase 4).
  4. **A4** — `game.py::run_game()` now checks `judge.measure_index <
     config.max_measures` before calling `judge.tick()`, avoiding the
     `IndexError` past 500 measures.
  5. **F4** — replaced blocking `pygame.time.wait(600)` after each judgment
     with a non-blocking "popup visible until t" flag inside the normal
     sampling loop, so beat-2 wind-up ictuses and QUIT events are no longer
     lost during the popup.
  6. **F5+F6** — `settings_menu.run_calibration()` now leaves
     `calibration.json` untouched when calibration fails/under-matches
     (`from_default` or <3 matches), and seeds the sharpness reference only
     from swings matched to a practice measure, not every detected event.
  7. **F7–F10+A8** — `MeasureJudge`'s window now centers on
     `beat1 + calibration_offset_s`; `run_calibration()`'s return type is
     now consistently `None`; `joycon_stream.py`'s `ImportError` message
     surfaces the real underlying exception + correct Windows package names
     (`hidapi`, `PyGLM`); install notes added to `Swinger_Build_Plan_v1.md`
     Phase 0; added `.gitignore` (`calibration.json`, `__pycache__`);
     `primer.md.lnk` stale-shortcut issue resolved by copying the real
     `primer.md` into the project root (`.lnk` left in place, unused).
- **New test**: `tests/test_full_loop_probe.py` — synthetic 500-measure
  MeasureJudge run (Build Plan v2's Phase 3.5 exit criterion): confirms no
  IndexError at the max_measures boundary and a non-empty
  `windup_interval_variance()` (proves F4's fix actually reaches
  session_log). All 5 offline tests green after the fixes.

- **Also completed (Build Plan v2 Phase 6 closeout, non-hardware parts
  only):** added a dated addendum to `Swinger_Build_Plan_v1.md` Section 6
  noting F3's real-device findings (native rate ~66Hz, ~10,000-unit resting
  magnitude bias) as a lead for the deferred per-axis-calibration item;
  added a "Unity port readiness" note to `CLAUDE.md`'s Platform roadmap
  confirming the pure-logic/presentation split still holds post-bugfix
  (`beat_schedule.py`/`ictus_detector.py`/`scoring.py`/`session_log.py`/
  `calibration.py` are pygame-free; `game.py`/`metronome.py`/
  `settings_menu.py` are the presentation layer Unity replaces).
  `primer.md` restore and `.gitignore` (Phase 6's other two items) were
  already done as part of the F7-F10+A8 cleanup pass.

## Exact next step
Everything reachable without physical hardware or a second human is done.
What's left is entirely gated on those two things, per
`Swinger_Build_Plan_v2.md`'s phase order (do not skip ahead):
1. **Phase 3.5's last item** — a *fresh* Phase 0 CSV recapture on real
   hardware to confirm F3's raw-vs-deduplicated counts now match 1:1 (the
   synthetic/reprocessed verification is done; this specific check needs a
   live device).
2. **Phase 4** — real threshold tuning against soft/med/hard captures under
   the fixed pipeline; re-derive `FALLBACK_SHARPNESS_LOW_HIGH` from that
   fresh data instead of the reprocessed old `swings.csv`.
3. **Phase 5** — the full manual playtest of `game.py` (stream → detector →
   judge → render → summary → settings), including handing the controller
   to a second person per v1 Section 0's actual "v0 prototype-ready" bar.

## Open blockers
- No physical Joy-Con available in this environment — blocks all three
  remaining Phase 3.5/4/5 items above.
- `game.py` end-to-end still has never been run once, simulated or
  otherwise, with real pygame rendering — only `MeasureJudge` (pure logic)
  has been exercised via synthetic probes.
- Repo still not committed to git — user hasn't asked for a commit this
  session either; `.gitignore` was added but nothing staged/committed.
- `FALLBACK_SHARPNESS_LOW_HIGH` (scoring.py) is explicitly flagged
  unverified — reprocessed old capture, not a fresh one; don't trust it past
  "better than the old 3-orders-of-magnitude-off placeholder."
- Phase 5 additionally needs a second human tester, not just hardware —
  flag this separately from the hardware blocker when picking this back up.
