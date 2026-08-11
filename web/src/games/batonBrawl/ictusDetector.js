/**
 * Turns a raw gyro-magnitude stream into discrete, timestamped ictus events
 * — the direction-change/deceleration point right after a swing's peak, not
 * the peak itself (see CONTEXT.md's `Ictus` entry). Pure port of
 * `src/ictus_detector.py`'s `IctusDetector`; no DOM, no WebHID, no
 * rendering dependency, so it can be imported and unit tested standalone
 * (see ticket #16 / ADR-0001, ADR-0002).
 *
 * Keep this in sync with the Python original when either is retuned —
 * `web/tests/ictusDetector.test.mjs` pins exact timestamps against the
 * Python reference to catch silent divergence.
 */

// --- Tunable constants (same values/units as ictus_detector.py) ---
export const SMOOTHING_WINDOW = 4; // samples; kills single-sample jitter without flattening the spike.
// mag units/SECOND (not per-sample): processSample() divides by the actual
// inter-sample dt so this threshold means the same thing regardless of poll rate.
export const RISE_RATE_THRESHOLD = 7500.0;
export const PEAK_MIN_THRESHOLD = 200.0; // mag units; filters small hand jitter from counting as a swing.
export const DROP_FRACTION = 0.6; // qualifying drop: mag falls below this fraction of tracked peak
export const DROP_WINDOW_S = 0.08; // drop must happen within this long of the peak
export const REFRACTORY_S = 0.175; // after an ictus, ignore new rises for this long
export const RISING_TIMEOUT_S = 0.5; // abandon a RISING candidate that never qualifies
export const MIN_SEEK_TIMEOUT_S = 0.15; // finalize the local min if mag hasn't risen again by this long

const State = Object.freeze({
  ARMED: 'ARMED',
  RISING: 'RISING',
  POST_DROP_SEEK_MIN: 'POST_DROP_SEEK_MIN',
  REFRACTORY: 'REFRACTORY',
});

/**
 * @typedef {{
 *   timestamp: number,
 *   peakMagnitude: number,
 *   riseDuration: number,
 *   maxDerivative: number,
 * }} IctusEvent
 */

/** Streaming state machine. Feed samples one at a time via processSample(). */
export class IctusDetector {
  constructor({
    smoothingWindow = SMOOTHING_WINDOW,
    riseRateThreshold = RISE_RATE_THRESHOLD,
    peakMinThreshold = PEAK_MIN_THRESHOLD,
    dropFraction = DROP_FRACTION,
    dropWindowS = DROP_WINDOW_S,
    refractoryS = REFRACTORY_S,
    risingTimeoutS = RISING_TIMEOUT_S,
    minSeekTimeoutS = MIN_SEEK_TIMEOUT_S,
  } = {}) {
    this.smoothingWindow = smoothingWindow;
    this.riseRateThreshold = riseRateThreshold;
    this.peakMinThreshold = peakMinThreshold;
    this.dropFraction = dropFraction;
    this.dropWindowS = dropWindowS;
    this.refractoryS = refractoryS;
    this.risingTimeoutS = risingTimeoutS;
    this.minSeekTimeoutS = minSeekTimeoutS;

    this._smoothBuf = [];
    this._prevSmoothed = null;
    this._prevT = null;
    this._state = State.ARMED;

    this._riseStartT = null;
    this._peakMag = 0.0;
    this._peakT = 0.0;
    this._maxDerivative = 0.0;
    this._seekingMinVal = null;
    this._seekingMinT = null;
    this._seekEnteredT = 0.0;
    this._refractoryUntil = 0.0;
  }

  _smooth(rawMag) {
    this._smoothBuf.push(rawMag);
    if (this._smoothBuf.length > this.smoothingWindow) this._smoothBuf.shift();
    let sum = 0;
    for (const v of this._smoothBuf) sum += v;
    return sum / this._smoothBuf.length;
  }

  /**
   * @param {number} t timestamp, seconds
   * @param {number} rawMag raw gyro magnitude
   * @returns {IctusEvent | null}
   */
  processSample(t, rawMag) {
    const mag = this._smooth(rawMag);
    let event = null;

    if (this._state === State.REFRACTORY) {
      if (t >= this._refractoryUntil) {
        // Fall through to ARMED handling below instead of returning early
        // (H6, Bug_Audit_2026-07-28.md): an early return here means the
        // very sample that ends the refractory period never gets
        // rise-tested, costing up to one sample (~15ms) of detection
        // latency on rapid repeat swings.
        this._state = State.ARMED;
      } else {
        this._prevSmoothed = mag;
        this._prevT = t;
        return null;
      }
    }

    const dt = this._prevT !== null ? t - this._prevT : null;
    let rate = null;
    if (this._prevSmoothed !== null && dt !== null && dt > 0) {
      rate = (mag - this._prevSmoothed) / dt;
    }

    if (this._state === State.ARMED) {
      if (rate !== null && rate > this.riseRateThreshold) {
        this._state = State.RISING;
        this._riseStartT = t;
        this._peakMag = mag;
        this._peakT = t;
        this._maxDerivative = rate;
      }
    } else if (this._state === State.RISING) {
      if (t - this._riseStartT > this.risingTimeoutS) {
        this._state = State.ARMED;
      } else {
        if (rate !== null && rate > this._maxDerivative) this._maxDerivative = rate;
        if (mag > this._peakMag) {
          this._peakMag = mag;
          this._peakT = t;
        } else if (this._peakMag >= this.peakMinThreshold && mag <= this._peakMag * this.dropFraction) {
          if (t - this._peakT <= this.dropWindowS) {
            this._state = State.POST_DROP_SEEK_MIN;
            this._seekingMinVal = mag;
            this._seekingMinT = t;
            this._seekEnteredT = t;
          } else {
            this._state = State.ARMED;
          }
        }
      }
    } else if (this._state === State.POST_DROP_SEEK_MIN) {
      // Finalize on either a rise (true local min found) or a timeout
      // (signal went flat/idle without rising -- use the min seen so far
      // rather than stalling until the *next* swing's rise, which would
      // let the refractory window swallow it).
      const timedOut = (t - this._seekEnteredT) > this.minSeekTimeoutS;
      // Strict '<' (D1, Bug_Audit_2026-07-28.md): '<=' let a flat bottom
      // keep advancing _seekingMinT on every tying sample, so the reported
      // ictus timestamp slid to the *end* of the plateau instead of its
      // start -- a bias that varies with swing style rather than a
      // constant calibration can absorb.
      if (mag < this._seekingMinVal && !timedOut) {
        this._seekingMinVal = mag;
        this._seekingMinT = t;
      } else {
        event = {
          timestamp: this._seekingMinT,
          peakMagnitude: this._peakMag,
          riseDuration: this._peakT - this._riseStartT,
          maxDerivative: this._maxDerivative,
        };
        this._refractoryUntil = t + this.refractoryS;
        this._state = State.REFRACTORY;
      }
    }

    this._prevSmoothed = mag;
    this._prevT = t;
    return event;
  }
}

/**
 * Offline replay helper: samples is an array of [timestamp, magnitude] pairs.
 * @param {[number, number][]} samples
 * @param {ConstructorParameters<typeof IctusDetector>[0]} [detectorOpts]
 * @returns {IctusEvent[]}
 */
export function detectIctuses(samples, detectorOpts) {
  const detector = new IctusDetector(detectorOpts);
  const events = [];
  for (const [t, mag] of samples) {
    const ev = detector.processSample(t, mag);
    if (ev !== null) events.push(ev);
  }
  return events;
}
