/**
 * Swing Kings gesture mode — the conducting stroke.  [swingKings owns this]
 *
 * One pure detector for every gesture source (mouse drag, tracked hand). Feed
 * it (time, y) with y growing DOWNWARD in screen heights; it reports:
 *
 *   raise   the hand lifted clear of where it rested — the windup begins
 *   ictus   the sharp DECELERATION that ends a fast downward stroke — the
 *           downbeat. Not the fastest moment of the stroke and not its
 *           start: the instant it stops, which is the event a conductor's
 *           downbeat reads as and the one every timing measurement in this
 *           repo is anchored to (see CONTEXT.md, "Ictus").
 *
 * Trigger-only by design (spec §5): the ictus fires the swing and its time is
 * what the judge scores. Stroke shape, sharpness and size are not scored.
 *
 * Pure: no DOM, no clock — times are whatever the caller passes (audio time
 * in the game, synthetic in the tests).
 */

/**
 * @param {object} [o]
 * @param {number} [o.riseMin]    lift (screen heights) that counts as a raise
 * @param {number} [o.vDown]      downward speed (heights/s) that starts a stroke
 * @param {number} [o.minStroke]  stroke length that counts as a swing
 * @param {number} [o.decel]      ictus when speed falls below this × the stroke's peak
 * @param {number} [o.refractory] seconds after an ictus before another can fire
 */
export function createIctusDetector({
  riseMin = 0.06, vDown = 1.0, minStroke = 0.08, decel = 0.3, refractory = 0.15,
} = {}) {
  let state = 'rest';
  let anchor = null;     // lowest point (largest y) the hand rested at
  let top = 0;           // highest point of the current windup
  let peakV = 0;
  let lastT = null, lastY = 0, vy = 0;
  let quietUntil = -Infinity;

  function reset() {
    state = 'rest'; anchor = null; lastT = null; vy = 0; peakV = 0;
  }

  /** @returns {{type:'raise'|'ictus', time:number, y:number}|null} */
  function feed(t, y) {
    if (lastT === null) { lastT = t; lastY = y; anchor = y; return null; }
    const dt = t - lastT;
    if (dt <= 0) return null;
    const raw = (y - lastY) / dt;
    // Light smoothing: kills pixel jitter without delaying the stop by more
    // than a frame (the ictus is a sudden change, and must stay sudden).
    vy = vy * 0.35 + raw * 0.65;
    lastT = t; lastY = y;

    switch (state) {
      case 'rest':
        anchor = Math.max(anchor, y);
        if (t >= quietUntil && y < anchor - riseMin) {
          state = 'raised';
          top = y;
          return { type: 'raise', time: t, y };
        }
        return null;

      case 'raised':
        top = Math.min(top, y);
        if (vy > vDown && y > top + 0.01) { state = 'striking'; peakV = vy; return null; }
        // Lowered slowly back to rest: that was not a windup after all.
        if (y > anchor - riseMin * 0.25 && vy < vDown) { state = 'rest'; anchor = y; }
        return null;

      case 'striking':
        peakV = Math.max(peakV, vy);
        if (vy < peakV * decel) {
          const stroke = y - top;
          state = 'rest';
          anchor = y;
          if (stroke < minStroke) { state = 'raised'; top = Math.min(top, y); return null; }
          quietUntil = t + refractory;
          return { type: 'ictus', time: t, y };
        }
        return null;

      default:
        return null;
    }
  }

  return {
    feed, reset,
    get state() { return state; },
  };
}
