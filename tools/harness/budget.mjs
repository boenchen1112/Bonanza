/**
 * Frame-budget verdict for a harness run: consecutive frame durations (ms)
 * in, pass/fail out. The budget is what a 60Hz player needs to see smooth
 * play: p95 frame work within one refresh, and at most one visible stall a
 * minute. The first seconds after a scene starts are excluded — shader
 * compiles and asset uploads land there by design.
 *
 * Feed it frames measured with vsync and the frame-rate limit OFF (inspect.mjs
 * does this under --budget). At display rate, headless Chromium's own frame
 * pacing puts p95 near 18.7ms even with the game canvas hidden, which says
 * nothing about the game; uncapped, frame time is the work itself.
 */

export const BUDGET = {
  p95Ms: 1000 / 60,
  longMs: 50,
  longPerMinute: 1,
  warmupMs: 3000,
  minSeconds: 5,
};

/**
 * @param {number[]} frames consecutive frame durations, ms, oldest first
 * @param {Partial<typeof BUDGET>} [opts]
 */
export function budgetVerdict(frames, opts = {}) {
  const b = { ...BUDGET, ...opts };
  let skipped = 0;
  let i = 0;
  while (i < frames.length && skipped < b.warmupMs) skipped += frames[i++];
  const kept = frames.slice(i);
  const totalMs = kept.reduce((a, x) => a + x, 0);
  const seconds = totalMs / 1000;

  const sorted = kept.slice().sort((x, y) => x - y);
  const p95 = sorted.length ? sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * 0.95))] : 0;
  const longFrames = kept.filter((x) => x > b.longMs).length;
  const allowedLong = Math.max(1, Math.round((seconds / 60) * b.longPerMinute));

  let reason = 'within budget';
  let pass = true;
  if (seconds < b.minSeconds) {
    pass = false;
    reason = `not enough data (${seconds.toFixed(1)}s after warm-up, need ${b.minSeconds}s)`;
  } else if (p95 > b.p95Ms) {
    pass = false;
    reason = `frame p95 ${p95.toFixed(1)}ms > ${b.p95Ms}ms`;
  } else if (longFrames > allowedLong) {
    pass = false;
    reason = `${longFrames} frames over ${b.longMs}ms in ${seconds.toFixed(0)}s (allowed ${allowedLong})`;
  }
  return { pass, reason, p95, longFrames, allowedLong, seconds: Math.round(seconds * 10) / 10, budget: b };
}
