/**
 * Shell state: what persists between runs, and what persists between scenes.
 * [shell agent owns this file]
 *
 * Two tiers, deliberately separate:
 *   - `profile`  — written through `Save` (localStorage). Records, options,
 *                  player names, unlocks. Must survive a refresh and must never
 *                  throw, even in private mode where storage is dead.
 *   - `session`  — module singleton. The roster the player just built, the
 *                  party in progress. Lives only as long as the tab, but has to
 *                  outlive a scene swap, because the party loop IS a sequence
 *                  of scene swaps.
 */

import { Save, makeRng } from '../core/util.js';
import { rankFor } from '../core/judge.js';

const RECORDS_KEY = 'records';
const OPTIONS_KEY = 'options';
const NAMES_KEY = 'names';
const UNLOCK_KEY = 'unlocks';
const STATS_KEY = 'stats';

export const DEFAULT_OPTIONS = {
  music: 0.75,
  sfx: 0.85,
  offsetMs: 0,
  partyLength: 4,
  reduceMotion: false,
  /** Swing Kings: 'tap' (keyboard/tap, default) | 'mouse' (hold + conducting drag) | 'camera' (hand tracking). */
  swingInput: 'tap',
};

/** @typedef {{score:number, rank:string, accuracy:number, plays:number, maxCombo:number}} Record_ */

function loadRecords() {
  const r = Save.get(RECORDS_KEY, null);
  return (r && typeof r === 'object') ? r : {};
}

export const profile = {
  records: loadRecords(),
  options: { ...DEFAULT_OPTIONS, ...(Save.get(OPTIONS_KEY, null) || {}) },
  names: Save.get(NAMES_KEY, null) || ['P1', 'P2', 'P3', 'P4'],
  unlocks: Save.get(UNLOCK_KEY, null) || { games: [], chars: [] },
  stats: Save.get(STATS_KEY, null) || { rounds: 0, parties: 0, wins: 0, notes: 0 },

  /** @returns {Record_} */
  record(gameId) {
    return this.records[gameId] || { score: 0, rank: null, accuracy: 0, plays: 0, maxCombo: 0 };
  },

  /**
   * Fold a finished run into the records.
   * @returns {{newScore:boolean,newRank:boolean,prevScore:number}}
   */
  submit(gameId, result) {
    const prev = this.record(gameId);
    const score = Math.round(result?.score || 0);
    const rank = result?.rank || null;
    const acc = result?.accuracy || 0;
    const combo = result?.stats?.maxCombo || 0;
    const newScore = score > prev.score;
    const newRank = rank ? RANK_ORDER.indexOf(rank) < RANK_ORDER.indexOf(prev.rank || 'D') || !prev.rank : false;
    this.records[gameId] = {
      score: Math.max(prev.score, score),
      rank: newRank || !prev.rank ? (rank || prev.rank) : prev.rank,
      accuracy: Math.max(prev.accuracy, acc),
      plays: (prev.plays || 0) + 1,
      maxCombo: Math.max(prev.maxCombo || 0, combo),
    };
    this.stats.rounds++;
    this.stats.notes += (result?.stats?.perfect || 0) + (result?.stats?.great || 0) + (result?.stats?.good || 0);
    Save.set(RECORDS_KEY, this.records);
    Save.set(STATS_KEY, this.stats);
    return { newScore, newRank, prevScore: prev.score };
  },

  setOption(k, v) {
    this.options[k] = v;
    Save.set(OPTIONS_KEY, this.options);
  },

  setName(i, name) {
    this.names[i] = String(name || '').slice(0, 8) || 'P' + (i + 1);
    Save.set(NAMES_KEY, this.names);
  },

  unlock(kind, id) {
    const list = this.unlocks[kind] || (this.unlocks[kind] = []);
    if (!list.includes(id)) { list.push(id); Save.set(UNLOCK_KEY, this.unlocks); return true; }
    return false;
  },

  reset() {
    this.records = {};
    this.options = { ...DEFAULT_OPTIONS };
    this.names = ['P1', 'P2', 'P3', 'P4'];
    this.unlocks = { games: [], chars: [] };
    this.stats = { rounds: 0, parties: 0, wins: 0, notes: 0 };
    Save.del(RECORDS_KEY); Save.del(OPTIONS_KEY); Save.del(NAMES_KEY);
    Save.del(UNLOCK_KEY); Save.del(STATS_KEY);
  },
};

export const RANK_ORDER = ['S', 'A', 'B', 'C', 'D'];

/** Rank comparison: is `a` better than `b`? */
export function betterRank(a, b) {
  if (!b) return !!a;
  if (!a) return false;
  return RANK_ORDER.indexOf(a) < RANK_ORDER.indexOf(b);
}

// ----------------------------------------------------------------- party

/** The closing game. A party always ends on it, whatever the shuffle says. */
export const FINALE_ID = 'finale-fever';

/** Round points by finishing place (1st, 2nd, 3rd, 4th). */
export const PLACE_POINTS = [3, 2, 1, 0];

/**
 * The party's games: `len` of `ids`, shuffled by the seeded `rng`, with the
 * finale held back for the last round.
 */
export function partyPlaylist(ids, len, rng) {
  const rest = ids.filter((id) => id !== FINALE_ID);
  for (let i = rest.length - 1; i > 0; i--) {
    const j = Math.floor(rng() * (i + 1));
    [rest[i], rest[j]] = [rest[j], rest[i]];
  }
  const hasFinale = ids.includes(FINALE_ID);
  const n = Math.max(1, Math.min(len, ids.length));
  return hasFinale ? [...rest.slice(0, n - 1), FINALE_ID] : rest.slice(0, n);
}

/**
 * A CPU's round. The minigames are single-player, so the CPUs don't really
 * play: each posts an accuracy centred on its skill with a seeded wobble, and
 * a score on the scale the human's own run just set for this game (score
 * grows ~ with accuracy squared in every game), so the numbers sit together
 * in the standings. `ref` is the human's result.
 */
export function cpuRound(skill, rng, ref = {}) {
  // Wide enough that an easy CPU sometimes beats a hard one: at ±0.09 the
  // standings came out in skill order every single round.
  const wobble = (rng() + rng() - 1) * 0.17;
  const accuracy = Math.min(0.995, Math.max(0.2, 0.42 + skill * 0.56 + wobble));
  const refAcc = Math.max(0.2, ref.accuracy || 0);
  const scale = ref.score > 0 ? ref.score / (refAcc * refAcc) : 10000;
  const score = Math.max(10, Math.round((scale * accuracy * accuracy) / 10) * 10);
  const misses = accuracy >= 0.98 && rng() < skill * 0.6 ? 0 : 1;
  return { score, accuracy: Math.round(accuracy * 1000) / 1000, rank: rankFor(accuracy, misses) };
}

// --------------------------------------------------------------- session

/**
 * @typedef {{id:number, name:string, char:string, isCpu:boolean, cpuSkill:number,
 *            palette:number, points:number, wins:number, ready:boolean}} PlayerSlot
 */

export const session = {
  /** @type {PlayerSlot[]} */
  players: [],
  mode: 'free',
  /** @type {null|{games:string[], index:number, scores:object[], length:number, seed:number}} */
  party: null,
  lastResult: null,
  lastGame: null,
  /** Set when the player leaves a minigame through the pause menu. */
  aborted: false,

  setPlayers(list) {
    this.players = list.map((p, i) => ({ points: 0, wins: 0, ...p, id: i }));
  },

  /** @param {number} [seed] from the caller's seeded rng — never Math.random(). */
  startParty(games, length, seed = 1) {
    this.mode = 'party';
    this.party = { games, index: 0, length: games.length, scores: [], seed: seed >>> 0, winner: null };
    for (const p of this.players) { p.points = 0; p.wins = 0; }
    profile.stats.parties++;
    Save.set(STATS_KEY, profile.stats);
  },

  endParty() { this.party = null; this.mode = 'free'; },

  get currentGame() {
    if (!this.party) return null;
    return this.party.games[this.party.index] || null;
  },

  /**
   * Fold a finished round into party bookkeeping and advance to the next
   * game. The human (the first non-CPU slot) posts their real result; every
   * other slot posts a `cpuRound()` seeded by party, round and slot. Places
   * earn PLACE_POINTS; the round winner gets a win. The last round crowns
   * the party winner.
   */
  recordPartyRound(gameId, result) {
    const party = this.party;
    if (!party) return;
    const humanId = this.players.find((p) => !p.isCpu)?.id ?? -1;
    const posted = this.players.map((p) => {
      if (p.id === humanId) {
        return { id: p.id, score: Math.round(result.score || 0), rank: result.rank || 'D', accuracy: result.accuracy || 0 };
      }
      const rng = makeRng((party.seed ^ ((party.index + 1) * 0x9e3779b1) ^ ((p.id + 1) * 0x85ebca6b)) >>> 0);
      return { id: p.id, ...cpuRound(p.isCpu ? p.cpuSkill : 0.63, rng, result) };
    });
    posted.slice().sort((a, b) => (b.score - a.score) || (a.id - b.id)).forEach((r, i) => {
      r.place = i + 1;
      r.points = PLACE_POINTS[i] ?? 0;
      const p = this.players.find((q) => q.id === r.id);
      p.points += r.points;
      if (i === 0) p.wins++;
    });
    party.scores.push({ game: gameId, score: result.score, rank: result.rank, players: posted });
    party.index++;
    if (party.index >= party.length && this.players.length) {
      const top = this.standings()[0];
      party.winner = top.id;
      if (!top.isCpu) { profile.stats.wins++; Save.set(STATS_KEY, profile.stats); }
    }
  },

  get partyDone() {
    return !!this.party && this.party.index >= this.party.length;
  },

  /** Standings, best first, with ties broken by wins then id. */
  standings() {
    return this.players.slice().sort((a, b) => (b.points - a.points) || (b.wins - a.wins) || (a.id - b.id));
  },
};

/** Convenience for scenes that can be entered cold (harness `goto`). */
export function ensurePlayers(rng) {
  if (session.players.length) return session.players;
  session.setPlayers([
    { name: profile.names[0] || 'P1', char: 'bopp', isCpu: false, cpuSkill: 0 },
    { name: 'ZIZZ', char: 'zizz', isCpu: true, cpuSkill: 0.62 },
    { name: 'KWARK', char: 'kwark', isCpu: true, cpuSkill: 0.5 },
  ]);
  return session.players;
}
