/**
 * Beat Bash Bonanza leaderboard — Cloudflare Worker.
 *
 * Two endpoints, both talking to the D1 database bound as `env.DB`
 * (see worker/schema.sql and worker/wrangler.toml):
 *   GET  /scores?game=<gameId>   -> top 10 scores for that game
 *   POST /score  {gameId, playerName, score}  -> record one score
 *
 * This is the ONLY server this game talks to (web/ARCHITECTURE.md rule 4).
 * No auth, no anti-cheat beyond input clamping — this is a public write
 * endpoint by design (anyone can submit a score for their own runs), and
 * that's an accepted v1 gap, not an oversight: don't add a scoring-signing
 * scheme here unless someone actually asks for one.
 */

function json(obj, status, cors) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: { 'Content-Type': 'application/json', ...cors },
  });
}

function corsHeaders(env) {
  return {
    'Access-Control-Allow-Origin': env.ALLOWED_ORIGIN || '*',
    'Access-Control-Allow-Methods': 'GET,POST,OPTIONS',
    'Access-Control-Allow-Headers': 'Content-Type',
  };
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const cors = corsHeaders(env);

    if (request.method === 'OPTIONS') return new Response(null, { headers: cors });

    if (url.pathname === '/scores' && request.method === 'GET') {
      const gameId = url.searchParams.get('game');
      if (!gameId) return json({ error: 'missing game' }, 400, cors);
      const { results } = await env.DB.prepare(
        'SELECT player_name, score, created_at FROM scores WHERE game_id = ?1 ORDER BY score DESC LIMIT 10'
      ).bind(gameId).all();
      return json({ scores: results }, 200, cors);
    }

    if (url.pathname === '/score' && request.method === 'POST') {
      let body;
      try {
        body = await request.json();
      } catch {
        return json({ error: 'body must be json' }, 400, cors);
      }
      const { gameId, playerName, score } = body || {};
      if (!gameId || !playerName || !Number.isFinite(score)) {
        return json({ error: 'gameId, playerName, score are required' }, 400, cors);
      }
      // Clamp, don't trust: a public write endpoint has to survive garbage
      // input, not reject it with a 500.
      const clampedGameId = String(gameId).slice(0, 40);
      const clampedName = String(playerName).slice(0, 24) || 'anon';
      const clampedScore = Math.max(0, Math.min(Math.trunc(score), 999_999));
      await env.DB.prepare(
        'INSERT INTO scores (game_id, player_name, score) VALUES (?1, ?2, ?3)'
      ).bind(clampedGameId, clampedName, clampedScore).run();
      return json({ ok: true }, 200, cors);
    }

    return json({ error: 'not found' }, 404, cors);
  },
};
