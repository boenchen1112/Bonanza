// Pure scoring/calibration math, deliberately free of DOM/rAF/getUserMedia/
// AudioContext state so it can run under `node --test` (scoring.test.mjs)
// as well as in the browser (index.html imports this directly). Extracted
// from index.html verbatim -- values/thresholds/signs unchanged, see git
// history for the pre-extraction inline versions.

// v6 decision 2 + direct feedback (2026-07-30): score ICTUS TIMING (does
// the lowest point of the stroke land on the beat?), not gesture shape.
// Reuses the exact tier vocabulary the parked baseball minigame already
// established (src/scoring.py) for consistency across the project.
export const PERFECT_MS = 50;
export const GREAT_MS = 100;
export const GOOD_MS = 150;

export function timingTier(offsetMs) {
  if (offsetMs === null) return "Miss";
  const a = Math.abs(offsetMs);
  if (a <= PERFECT_MS) return "Perfect";
  if (a <= GREAT_MS) return "Great";
  if (a <= GOOD_MS) return "Good";
  return "Miss";
}

// Searches `traceXY` ([nx, ny, t] records, t in audioCtx.currentTime
// seconds) for the deepest ny minimum within `windowS` of `beatTime`, and
// compares its timestamp to `heardBeatTime` (the beat time already
// adjusted for audio output latency by the caller). ny is most NEGATIVE at
// the lowest physical point (image y-down negated to logical y-up by the
// caller), so argmin, not argmax. Returns {offsetMs: null, ictusNy: null}
// if no sample falls in the window.
export function findIctus(traceXY, beatTime, heardBeatTime, windowS) {
  const windowSamples = traceXY.filter(([, , t]) => t >= beatTime - windowS && t <= beatTime + windowS);
  if (windowSamples.length === 0) return { offsetMs: null, ictusNy: null };
  let best = windowSamples[0];
  for (const s of windowSamples) if (s[1] < best[1]) best = s;
  return { ictusNy: best[1], offsetMs: (best[2] - heardBeatTime) * 1000 };
}

// Derives calibScaleFactor: the ratio of the user's own real TOP-to-BOTTOM
// range to the reference pattern's prep-to-beat1 distance (REFERENCE's
// numbers are an abstract shape scale; raw camera-frame fractions are a
// different unit system -- see index.html's calibration comment). `ref` is
// prep-first ([[prepX,prepY],[beat1X,beat1Y],...]). Returns null if
// TOP/BOTTOM were too close together to derive a meaningful scale (the
// caller should treat that as a failed calibration).
export function deriveCalibScaleFactor({ ref, origin, bottom, videoAspect }) {
  const refDelta = Math.hypot(ref[1][0] - ref[0][0], ref[1][1] - ref[0][1]);
  const physDx = (bottom[0] - origin[0]) * videoAspect;
  const physDy = -(bottom[1] - origin[1]);
  const physDelta = Math.hypot(physDx, physDy);
  if (physDelta < 0.01) return null;
  return refDelta / physDelta;
}
