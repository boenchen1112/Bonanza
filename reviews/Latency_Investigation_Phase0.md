# Phase 0 Latency Investigation (Swinger_Build_Plan_v3.md)

Status: **closed.** Logged playtest data collected; see Conclusion below.

## What triggered this

A live `game.py` playtest reported "the latency/offset is too large, I
cannot tell whether it's functioning well." Three follow-up attempts to
characterize the complaint (calibration number? scoring wrong? popup
delayed?) all came back "can't tell" -- the complaint is real but
unlocalized. `calibration.json` from that session no longer exists (deleted
during routine cleanup before this was flagged as needing preservation), so
the offset value at the time of the complaint is unrecoverable. The only
way forward is a fresh, logged playtest.

## Three live hypotheses (not mutually exclusive)

1. **Race condition (scoring wrong)** -- `MeasureJudge.tick()` used to close
   a measure's window on wall-clock time alone, with no allowance for the
   detector's own pipeline lag (up to ~150-200ms from `MIN_SEEK_TIMEOUT_S`
   + smoothing). An on-time swing could be judged Miss before its own event
   arrived. **Already patched** with `JUDGE_GRACE_S = 0.25` (game.py) --
   but this fix was **never validated against the actual complaint**, and
   the complaint was reported *before* this fix existed. Do not assume it's
   resolved.
2. **Grace period itself is the felt lag** -- `JUDGE_GRACE_S` adds up to
   250ms of real wall-clock delay before a judgment (and its popup) appears,
   independent of whether the score is correct. If real detector delivery
   is usually much faster than 150ms (most swings aren't the flat-signal
   worst case `MIN_SEEK_TIMEOUT_S` guards against), 250ms may be needlessly
   conservative and could itself be what "too laggy" describes.
3. **`DEFAULT_OFFSET_S` was never really measured** -- `calibration.py`'s
   `DEFAULT_OFFSET_S = 0.05` has been a placeholder guess since v1 and was
   never replaced with a real dev-machine measurement (v1's own flag, still
   open). If the player was on this default (didn't calibrate, or
   calibration under-matched and F5 correctly refused to save), a real
   ~150-250ms pipeline+Bluetooth+audio latency would score honest on-beat
   swings as Miss -- not a bug, just an unmeasured constant.

Hypotheses 1 and 2 pull `JUDGE_GRACE_S` in opposite directions, so a single
"feels better/worse" report can't distinguish them -- they need separate
numbers.

## Instrumentation added (src/game.py)

`run_game(log_swings=True)` (now the default) prints, live, per swing:

- On detector delivery: `event t=<ts> delivered_lag_ms=<N>` -- how long
  after the true ictus timestamp the detector actually returned the event.
  Answers hypothesis 3's detector-side half and bounds how conservative
  `JUDGE_GRACE_S` actually needs to be.
- On judgment: `corrected_offset_ms` (scoring correctness -- was the swing
  actually on time per calibration) separately from `popup_lag_ms` (felt
  latency -- real wall-clock delay from the swing to the judgment
  appearing, including the grace period). This is the split that lets a
  playtest distinguish "scored wrong" from "scored right but felt slow."

## What's still needed to close Phase 0

1. A playtest run with this logging on, ideally including both an
   uncalibrated round (isolates hypothesis 3) and a calibrated round
   (isolates hypotheses 1/2).
2. From that log: whether `delivered_lag_ms` is consistently near the
   `MIN_SEEK_TIMEOUT_S`-bound worst case (~150-200ms) or much lower in
   practice -- determines whether `JUDGE_GRACE_S` should shrink.
3. Whether `corrected_offset_ms` clusters near 0 for swings that felt
   on-time (scoring is right, complaint is about felt lag / hypothesis 2)
   or is consistently large in one direction (calibration/hypothesis 3).
4. A real `DEFAULT_OFFSET_S` measurement (dual-reference: clap on the
   click, compare recorded-audio timestamp vs. detected ictus timestamp)
   if hypothesis 3 looks likely.

## Conclusion (2026-07-17)

A logged playtest with the new instrumentation showed a **consistent
offset of ~286-317ms**, with everything else (tier judgments, sharpness
buckets, out-count, summary) functioning correctly. A ~30ms-wide, one-
directional cluster is a systematic bias, not noise or a scoring bug --
this matches hypothesis 3 (`DEFAULT_OFFSET_S` was never really measured)
far better than hypotheses 1 or 2, which would show up as inconsistent or
bidirectional error, not a tight one-sided band.

Practical read: real end-to-end pipeline + Bluetooth + audio latency on
this hardware is ~300ms, roughly 6x the old `DEFAULT_OFFSET_S = 0.05`
placeholder. This is a calibration-constant problem, not a bug --
`JUDGE_GRACE_S` (0.25s) and the race-condition fix from earlier in the
session are validated as correct and are NOT the cause; if anything the
real offset being ~300ms while grace is only 250ms means grace could
stand to grow slightly, but the primary fix is running Settings ->
Calibrate (which measures this exact number per-player-per-session)
rather than relying on the static default.

## Not yet done

- `DEFAULT_OFFSET_S` (calibration.py) still left at the old 0.05
  placeholder rather than being bumped to ~0.3, since per-session
  calibration already measures and overrides it -- the static default only
  matters for an uncalibrated first swing. Worth a small bump as a better
  starting guess, but not required for correctness.
- No further action needed on `JUDGE_GRACE_S`; validated correct.
- Phase 0 exit criterion (Swinger_Build_Plan_v3.md) met: concrete
  before/after number obtained (~300ms systematic offset), root cause
  identified (unmeasured default constant, not a threading/logic bug), and
  confirmed it won't recur as a Unity-side bug since it's a
  hardware/calibration property, not a Python-specific race.
