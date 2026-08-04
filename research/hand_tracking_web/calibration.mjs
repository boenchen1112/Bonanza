// Pure calibration math, extracted for the same reason as scoring.mjs's
// functions (see that file's header): the call sites (onResults(),
// calibratePoint()) are entangled with async control flow and a watchdog
// timer that has hung before -- deliberately NOT touched here, only the
// math they use. Values/thresholds unchanged from the pre-extraction
// inline versions.

// Averages a run of [x, y] calibration samples into a single origin point.
export function averageSamples(samples) {
  const ox = samples.reduce((s, p) => s + p[0], 0) / samples.length;
  const oy = samples.reduce((s, p) => s + p[1], 0) / samples.length;
  return [ox, oy];
}

// Drops buffered samples older than windowMs relative to `now`. Pure --
// returns a new array, doesn't mutate `buffer`.
export function prunedStabilityBuffer(buffer, now, windowMs) {
  return buffer.filter(s => now - s.t <= windowMs);
}

// True once the buffer covers at least windowMs*0.8 and every sample in it
// is within `threshold` of every other (x scaled by videoAspect -- on a
// non-square frame the same physical wobble reads larger in whichever axis
// is foreshortened relative to the other, same correction as index.html's
// nx/VIDEO_ASPECT elsewhere). Requires at least 3 samples so a single
// lucky frame can't count as "stable."
export function isStable(buffer, now, { windowMs, threshold, videoAspect }) {
  if (buffer.length < 3) return false;
  const xs = buffer.map(s => s.x), ys = buffer.map(s => s.y);
  const spread = Math.max(
    (Math.max(...xs) - Math.min(...xs)) * videoAspect,
    Math.max(...ys) - Math.min(...ys),
  );
  const covered = now - buffer[0].t;
  return spread < threshold && covered >= windowMs * 0.8;
}
