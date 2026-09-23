/**
 * Canned, deterministic stand-in for leaderboardClient.js — never touches
 * the network. Used for harness/critic builds (see tools/harness/inspect.mjs
 * and net/index.js) so a run's numbers can't vary with Worker latency or
 * whatever is actually in the D1 table today. If a real defect exists in
 * how a piece *uses* the leaderboard (wrong gameId, ignoring the ok:false
 * case), this stand-in still exercises that code path — it just never
 * exercises the network itself.
 */
const CANNED_SCORES = [
  { player_name: 'AAA', score: 9800, created_at: '2026-01-01T00:00:00Z' },
  { player_name: 'BBB', score: 9200, created_at: '2026-01-01T00:00:00Z' },
  { player_name: 'CCC', score: 8700, created_at: '2026-01-01T00:00:00Z' },
];

export async function submitScore() {
  return { ok: true };
}

export async function fetchTopScores() {
  return CANNED_SCORES;
}
