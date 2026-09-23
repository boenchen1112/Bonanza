# Backend brief — Beat Bash Bonanza leaderboard

You are working on `worker/` (the Cloudflare Worker + D1 leaderboard) or
`web/src/net/` (its client). This is the *only* server-side code in either
product in this repo. Read `web/ARCHITECTURE.md` rules 3-6 first — this
brief is how those rules get implemented, not an exception to them.

## Scope — read this before adding anything

This backend does exactly one thing: store and return scores, per game, so
players can see how they rank against each other. That's it.

- **No multiplayer.** Explicitly ruled out — real-time play needs
  low-latency transport (WebSockets/Durable Objects), which is a different
  and much larger build. Don't add a socket, a room, or a "live players"
  feature to this Worker.
- **No AI-generation calls at runtime.** Image/model generation tools are
  dev-time-only (see `docs/agents/asset-pipeline.md`) — they help *author*
  an asset before it ships, they never run inside the shipped game.
- **No save-sync yet.** The pattern here (Worker endpoint + D1 table +
  `web/src/net/` client) is the template for save-sync if that gets
  greenlit later, but don't build it speculatively — this brief covers the
  leaderboard only.

## The pieces

- `worker/src/index.js` — the Worker. Two routes: `GET /scores?game=<id>`,
  `POST /score`. No auth, no anti-cheat beyond input clamping — a public
  write endpoint accepting garbage gracefully is the accepted v1 shape;
  don't add a signing/anti-cheat scheme unless asked.
- `worker/schema.sql` — the D1 table (`scores`: game_id, player_name,
  score, created_at) and its index. Apply changes with
  `wrangler d1 execute bonanza-leaderboard --file=worker/schema.sql`.
- `worker/wrangler.toml` — Worker config. `database_id` and
  `ALLOWED_ORIGIN` are placeholders; whoever actually deploys this needs
  their own Cloudflare account (`wrangler login`), their own
  `wrangler d1 create bonanza-leaderboard` (paste the resulting id in),
  and to set `ALLOWED_ORIGIN` to the real GitHub Pages URL once the repo
  is public. **None of that can happen from here — it needs your
  Cloudflare credentials, not an agent's.**
- `web/src/net/leaderboardClient.js` — the real client. Reads the Worker's
  base URL from `VITE_LEADERBOARD_URL` (set in `web/.env.local` for local
  dev, or as a build-time env var for the deployed build).
- `web/src/net/mockLeaderboardClient.js` — canned responses, no network.
- `web/src/net/index.js` — the only import path anything else should use;
  picks real vs. mock based on `VITE_MOCK_NETWORK`. Never import the real
  or mock client directly from game/shell code.

## Rules specific to this piece

- **Every call fails soft.** `submitScore`/`fetchTopScores` never throw —
  they catch and return `{ok: false, ...}` or `[]`. A dead Worker must
  degrade the leaderboard panel to "unavailable", never break a results
  screen or a round.
- **Only from `load()`/`result()`/shell screens.** Same rule as
  `ARCHITECTURE.md` 5 — if you're about to call `submitScore` from inside
  a minigame's `update()`, you're calling it from the wrong place.
- **The harness must never hit the real Worker.** `tools/harness/
  inspect.mjs` builds with `VITE_MOCK_NETWORK=1` by default specifically so
  critic runs stay deterministic — don't change that default, and don't
  make a piece behave differently when mocked vs. real beyond the response
  content itself.
- **CORS is the Worker's job, not the client's.** If you're adding a new
  route, add its CORS headers in `corsHeaders()`; don't work around a CORS
  failure by relaxing something on the `web/` side.

## Verifying your work

The Worker has no headless-browser story — test it directly:

```bash
cd worker && npm install && npm run dev   # wrangler dev, local D1
curl -X POST http://127.0.0.1:8787/score \
  -H 'content-type: application/json' \
  -d '{"gameId":"swingKings","playerName":"test","score":1234}'
curl 'http://127.0.0.1:8787/scores?game=swingKings'
```

For the client side, run the harness as usual — it verifies the mocked
path, which is the only path it can verify deterministically. A real
end-to-end check (client against the real deployed Worker) is a manual
step for whoever has the Cloudflare account, not something an agent's
report should claim to have done.

## Reporting back

End with: what you built, whether you ran the `curl` checks above and what
they returned, and — if you touched `web/src/net/` — confirmation that you
imported it only from `net/index.js`'s consumers, never the real/mock
clients directly.
