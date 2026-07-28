# Phase B Spike — Status, 2026-07-28

**This is a status note, not a verdict.** `Swinger_Build_Plan_v4.md` Phase B
requires a written verdict on whether gyro-only orientation integration
(reset per detected downbeat) can recover a usable conducting-gesture trace,
backed by per-repetition shape-distance numbers from **real** captured
conducting gestures. That capture step needs a human physically swinging a
paired right Joy-Con through real 2/4, 3/4, and 4/4 patterns — it cannot be
done from an autonomous session with no hardware access. **Do not read
anything below as satisfying Phase B's exit criterion, and do not start
Phase D on the strength of this note.**

## What's actually done

`research/gesture_trace_spike.py`:

- `integrate_orientation()` — gyro-only Euler integration, reset to (0,0) at
  each downbeat timestamp, per Phase B's plan. Which two gyro axes map to
  the 2D trace's x/y is a parameter (`--axis-x`/`--axis-y`, default
  `gx`/`gy`), not hardcoded — Phase B's own text flags "pitch/yaw, or
  roll/pitch depending on grip" as an open question that needs real data,
  not an assumption to bake in here.
- `shape_distance()` — Phase B's specified scoring method: arc-length
  resample both the trace and the canonical reference (`resample_path()`,
  shared with `src/conducting_patterns.py` so Phase D's eventual live
  comparison uses the identical implementation) to 50 points, then average
  point-to-nearest-reference-point distance normalized by the reference's
  own bounding size.
- Self-test (`--self-test`, also the default with no `--capture`): builds a
  synthetic gyro trace whose integral traces the 4/4 reference pattern
  near-exactly, and asserts `shape_distance < 0.05`. **This only proves the
  integration/scoring arithmetic is internally self-consistent** — it says
  nothing about whether the representation survives real gyro noise, real
  drift, or a real human's swing dynamics. Result: `0.0216`, plot at
  `research/self_test_plots/synthetic_4beat.png`.

`src/conducting_patterns.py` (Phase C, done for real): canonical 2/4, 3/4,
4/4 reference patterns as normalized keyframe polylines, plus the shared
`resample_path()` helper. Static reference shapes only — these don't depend
on Phase B's verdict, since they're standard conducting pedagogy, not
something recovered from a live trace.

## What's blocked, and on whom

Actually running Phase B needs:

1. Real capture sessions: a person swinging real 2/4, 3/4, and 4/4
   conducting patterns with a paired right Joy-Con, a few repetitions each,
   via the existing `joycon_stream.py` capture path (already logs
   `gx, gy, gz` — no new capture code needed per the build plan).
2. Downbeat timestamps for each repetition, from `IctusDetector` events
   (the same detection already validated for the baseball minigame).
3. Running `research/gesture_trace_spike.py --capture <csv> --downbeats
   <timestamps> --signature <2|3|4>` per repetition, collecting the
   per-repetition `shape_distance` numbers.
4. A judgment call — from the *numbers*, not the plots alone, per Phase B's
   own instruction — on whether (a) gyro-only with per-beat reset is good
   enough, (b) it needs a complementary accelerometer-tilt filter, or (c)
   the representation should be abandoned/constrained.

This needs the user (or another human with the hardware) to run the capture
sessions; it cannot proceed further autonomously.
