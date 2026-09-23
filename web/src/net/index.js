/**
 * The single import path every other module uses for the leaderboard —
 * never import leaderboardClient.js or mockLeaderboardClient.js directly.
 * Picks the real client normally, and the canned mock when the harness
 * builds with VITE_MOCK_NETWORK=1 (see tools/harness/inspect.mjs), so
 * critic runs stay deterministic without every call site needing to know
 * mocking exists.
 */
const impl = import.meta.env.VITE_MOCK_NETWORK === '1'
  ? await import('./mockLeaderboardClient.js')
  : await import('./leaderboardClient.js');

export const submitScore = impl.submitScore;
export const fetchTopScores = impl.fetchTopScores;
