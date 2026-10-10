/**
 * The round result contract.  [INTEGRATOR-owned]
 *
 * `ARCHITECTURE.md` describes this shape in prose. Prose did not hold: one
 * game fed race points into `setAccuracy()` and showed the player a number
 * the results card then contradicted, and `field` — the thing a party scores
 * a real race by — had one implementation and no shape check anywhere.
 *
 * Two entry points, both pure:
 *   - `roundResult(...)`  minigames return through this. It is the contract.
 *   - `normaliseResult(r)` the shell reads every result through this. It
 *     tolerates a half-built minigame: a broken game must not take the
 *     results screen down with it.
 *
 * `accuracy` is HIT QUALITY in every game — verdict-weighted, 0..1, the
 * number printed beside PERFECT/GREAT/GOOD/MISS. A game whose score is
 * something else (Drumline's race points) keeps that in `score`, never here.
 */

/** @typedef {{perfect:number, great:number, good:number, miss:number, maxCombo:number, errors:number[]}} RoundStats */

const isNum = (v) => typeof v === 'number' && Number.isFinite(v);
const clamp01 = (v) => (v < 0 ? 0 : v > 1 ? 1 : v);

/** Warn once per message — a per-frame result() must not flood the console. */
const warned = new Set();
function warnOnce(msg) {
  if (warned.has(msg)) return;
  warned.add(msg);
  console.warn(`[result] ${msg}`);
}

/** Reset the warn-once ledger. Tests only. */
export function _resetWarnings() { warned.clear(); }

export function emptyStats() {
  return { perfect: 0, great: 0, good: 0, miss: 0, maxCombo: 0, errors: [] };
}

function normStats(s) {
  const o = s || {};
  return {
    // Games carry their own extras here (place, standings, notes, fumbles…)
    // and the results screen reads them; only the six shared keys are
    // guaranteed, never enforced as the whole set.
    ...o,
    perfect: isNum(o.perfect) ? o.perfect : 0,
    great: isNum(o.great) ? o.great : 0,
    good: isNum(o.good) ? o.good : 0,
    miss: isNum(o.miss) ? o.miss : 0,
    maxCombo: isNum(o.maxCombo) ? o.maxCombo : 0,
    errors: Array.isArray(o.errors) ? o.errors : [],
  };
}

/**
 * Is this a well-formed `field`? One `{id, place}` per competitor, places
 * starting at 0 and dense. `null` (the game did not race the lineup) is
 * valid and means "simulate the CPU rounds"; a malformed one is not, because
 * the party would score a race that never happened.
 * @returns {{ok: boolean, why?: string}}
 */
export function checkField(field) {
  if (field == null) return { ok: true };
  if (!Array.isArray(field) || field.length === 0) return { ok: false, why: 'field must be a non-empty array or null' };
  const places = [];
  for (const row of field) {
    if (!row || typeof row !== 'object') return { ok: false, why: 'field rows must be objects' };
    if (!isNum(row.place) || row.place < 0) return { ok: false, why: 'every field row needs a place >= 0' };
    if (!('id' in row)) return { ok: false, why: 'every field row needs an id (null for a house extra)' };
    places.push(row.place);
  }
  // Shared places are legal (a dead heat), so only the floor and the span are
  // checked: places must start at 0 and never skip past the field size.
  if (Math.min(...places) !== 0) return { ok: false, why: 'places must start at 0' };
  if (Math.max(...places) >= field.length) return { ok: false, why: 'a place is beyond the size of the field' };
  return { ok: true };
}

/**
 * Build a round result. Minigames return this from `result(ctx)`.
 *
 * @param {object} r
 * @param {number} r.score      the game's own scale — points, not a percentage
 * @param {number} r.accuracy   hit quality, 0..1. Clamped, and flagged if out of range.
 * @param {string|null} r.rank
 * @param {Partial<RoundStats>} [r.stats]
 * @param {object|null} [r.highlights]
 * @param {Array<{id: *, place: number}>|null} [r.field]  only from a game that raced the lineup
 */
export function roundResult({ score = 0, accuracy = 0, rank = null, stats, highlights = null, field = null } = {}) {
  if (!isNum(score)) { warnOnce('score is not a finite number; using 0'); score = 0; }
  if (!isNum(accuracy)) { warnOnce('accuracy is not a finite number; using 0'); accuracy = 0; }
  if (accuracy < 0 || accuracy > 1) {
    warnOnce(`accuracy must be hit quality in 0..1, got ${accuracy}. Race points and score scales belong in \`score\`.`);
    accuracy = clamp01(accuracy);
  }
  const f = checkField(field);
  if (!f.ok) { warnOnce(`${f.why} — dropping field, the party will simulate this round`); field = null; }

  return {
    score: Math.round(score),
    accuracy,
    rank: rank || null,
    stats: normStats(stats),
    highlights: highlights || null,
    field: field || null,
  };
}

/**
 * Tolerant read of whatever a minigame returned. Never throws, never yields
 * a partial shape — the results screen can rely on every key being present.
 */
export function normaliseResult(r) {
  const field = Array.isArray(r?.field) && checkField(r.field).ok ? r.field : null;
  return {
    score: isNum(r?.score) ? Math.round(r.score) : 0,
    accuracy: isNum(r?.accuracy) ? clamp01(r.accuracy) : 0,
    rank: r?.rank || null,
    stats: normStats(r?.stats),
    highlights: r?.highlights || null,
    field,
  };
}
