/**
 * Timing judgement.
 *
 * Windows are in milliseconds, not beats. A rhythm game that scales its
 * windows with tempo feels wrong at both ends: forgiving to the point of
 * mush at 80bpm, impossible at 180. Human timing precision is roughly
 * tempo-independent, so the windows are too.
 *
 * The numbers below are deliberately generous at the outer edge and tight at
 * the centre. That is the party-game shape: almost nobody outright misses a
 * note they tried to hit, but PERFECT still has to be earned, so the skill
 * ceiling survives.
 */

export const VERDICT = {
  PERFECT: 'perfect',
  GREAT: 'great',
  GOOD: 'good',
  MISS: 'miss',
};

export const WINDOWS_MS = {
  perfect: 42,
  great: 82,
  good: 128,
  /**
   * Beyond this a press is not "a bad hit on this note", it is a press that
   * belongs to no note at all — it gets swallowed rather than burning the
   * note, so mashing between notes doesn't destroy a run.
   */
  claim: 165,
};

export const SCORE = {
  perfect: 1000,
  great: 600,
  good: 250,
  miss: 0,
};

/** Ordered best->worst, for "did this run improve" comparisons. */
export const VERDICT_RANK = { perfect: 3, great: 2, good: 1, miss: 0 };

/**
 * Classify a signed timing error.
 * @param {number} errMs  hitTime - targetTime, in ms. Negative = early.
 */
export function verdictFor(errMs) {
  const a = Math.abs(errMs);
  if (a <= WINDOWS_MS.perfect) return VERDICT.PERFECT;
  if (a <= WINDOWS_MS.great) return VERDICT.GREAT;
  if (a <= WINDOWS_MS.good) return VERDICT.GOOD;
  return VERDICT.MISS;
}

/**
 * A note the player is expected to hit.
 * @typedef {{
 *   time: number,        // audio time, seconds
 *   action?: string,     // required action, default 'a'
 *   lane?: number,
 *   hold?: number,       // hold duration in seconds, 0 = tap
 *   data?: any,
 *   hit?: boolean,
 *   judged?: boolean,
 *   verdict?: string,
 *   errMs?: number,
 * }} Note
 */

/**
 * Matches presses to notes and expires notes that were never hit.
 *
 * The matching rule matters more than the windows do. We claim the note whose
 * time is *nearest* the press among those still unjudged and within the claim
 * window — not simply the next one in the list. With dense charts the naive
 * rule makes a slightly-late press eat the following note and cascade the
 * whole run into failure, which feels like the game cheated.
 */
export class NoteJudge {
  /**
   * @param {object} [opts]
   * @param {number} [opts.offsetMs] player latency calibration, added to press times
   */
  constructor(opts = {}) {
    /** @type {Note[]} */
    this.notes = [];
    this.offsetMs = opts.offsetMs || 0;
    this.windows = { ...WINDOWS_MS, ...(opts.windows || {}) };
    this._cursor = 0;
    this.onJudged = null; // (note, verdict, errMs) => void
    this.stats = { perfect: 0, great: 0, good: 0, miss: 0, combo: 0, maxCombo: 0, score: 0, errors: [] };
  }

  /** @param {Note[]} notes */
  load(notes) {
    this.notes = notes
      .map((n) => ({ action: 'a', hold: 0, ...n, judged: false, hit: false }))
      .sort((a, b) => a.time - b.time);
    this._cursor = 0;
    this.stats = { perfect: 0, great: 0, good: 0, miss: 0, combo: 0, maxCombo: 0, score: 0, errors: [] };
    return this;
  }

  get remaining() {
    let n = 0;
    for (let i = this._cursor; i < this.notes.length; i++) if (!this.notes[i].judged) n++;
    return n;
  }

  get finished() {
    return this._cursor >= this.notes.length;
  }

  /**
   * Offer a press. Returns the judgement, or null if no note claimed it.
   * @param {string} action
   * @param {number} time audio time of the press
   */
  press(action, time) {
    const t = time + this.offsetMs / 1000;
    const claim = this.windows.claim / 1000;

    let best = -1;
    let bestErr = Infinity;
    for (let i = this._cursor; i < this.notes.length; i++) {
      const n = this.notes[i];
      if (n.time - t > claim) break; // sorted: everything after is further away
      if (n.judged) continue;
      if (n.action !== action) continue;
      const err = t - n.time;
      if (Math.abs(err) > claim) continue;
      if (Math.abs(err) < Math.abs(bestErr)) { best = i; bestErr = err; }
    }
    if (best < 0) return null;

    const note = this.notes[best];
    const errMs = bestErr * 1000;
    const verdict = verdictFor(errMs);
    return this._commit(note, verdict, errMs);
  }

  /** Expire notes whose window has fully passed. Call every frame. */
  update(now) {
    const claim = this.windows.claim / 1000;
    for (let i = this._cursor; i < this.notes.length; i++) {
      const n = this.notes[i];
      if (n.time + claim > now) break;
      if (!n.judged) this._commit(n, VERDICT.MISS, claim * 1000);
    }
    // Advance the cursor past a contiguous run of judged notes.
    while (this._cursor < this.notes.length && this.notes[this._cursor].judged) this._cursor++;
  }

  _commit(note, verdict, errMs) {
    note.judged = true;
    note.verdict = verdict;
    note.errMs = errMs;
    note.hit = verdict !== VERDICT.MISS;

    const s = this.stats;
    s[verdict]++;
    s.score += SCORE[verdict];
    if (note.hit) {
      s.combo++;
      s.maxCombo = Math.max(s.maxCombo, s.combo);
      s.errors.push(errMs);
    } else {
      s.combo = 0;
    }
    if (this.onJudged) this.onJudged(note, verdict, errMs);
    return { note, verdict, errMs };
  }

  /** Accuracy 0..1 weighted by verdict value. */
  get accuracy() {
    const s = this.stats;
    const total = s.perfect + s.great + s.good + s.miss;
    if (!total) return 1;
    return s.score / (total * SCORE.perfect);
  }

  /** Mean signed error in ms — tells a player they are consistently rushing. */
  get bias() {
    const e = this.stats.errors;
    if (!e.length) return 0;
    return e.reduce((a, b) => a + b, 0) / e.length;
  }
}

/**
 * Rank thresholds. Party games hand out generous ranks; the top rank should
 * still be rare enough that hitting it is a moment.
 */
export function rankFor(accuracy, missCount) {
  if (missCount === 0 && accuracy >= 0.98) return 'S';
  if (accuracy >= 0.92) return 'A';
  if (accuracy >= 0.80) return 'B';
  if (accuracy >= 0.65) return 'C';
  return 'D';
}
