/**
 * Talks to the one Cloudflare Worker endpoint this game is allowed to talk
 * to (see web/ARCHITECTURE.md rules 4-6). Every call here must be fired
 * from load()/result()/a shell screen, never from update()/input(), and
 * every call must fail soft — a dead Worker degrades the leaderboard panel,
 * never the game.
 *
 * VITE_LEADERBOARD_URL is the deployed Worker's base URL, e.g.
 * https://bonanza-leaderboard.<you>.workers.dev — set in web/.env.local for
 * local dev, and as a build-time env var for the deployed build.
 */
const BASE_URL = import.meta.env.VITE_LEADERBOARD_URL || '';

export async function submitScore(gameId, playerName, score) {
  if (!BASE_URL) return { ok: false, reason: 'leaderboard not configured' };
  try {
    const res = await fetch(`${BASE_URL}/score`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ gameId, playerName, score }),
    });
    if (!res.ok) return { ok: false, reason: `http ${res.status}` };
    return { ok: true };
  } catch (e) {
    return { ok: false, reason: e.message };
  }
}

export async function fetchTopScores(gameId) {
  if (!BASE_URL) return [];
  try {
    const res = await fetch(`${BASE_URL}/scores?game=${encodeURIComponent(gameId)}`);
    if (!res.ok) return [];
    const body = await res.json();
    return body.scores || [];
  } catch {
    return [];
  }
}
