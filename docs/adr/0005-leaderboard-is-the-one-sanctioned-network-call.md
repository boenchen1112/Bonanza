# The cross-player leaderboard is the one sanctioned exception to "no fetches outside the build"

ADR 0003 made rule 3 "no request to any other origin, ever," enforced by the harness aborting
and flagging any off-origin fetch. That rule is still right for assets and for gameplay — but
players want to see how they stack up against other players, which needs a request to a server
this game doesn't ship.

Scope was deliberately narrowed before any code was written: no real-time multiplayer (latency
rules it out for a rhythm game), no runtime AI generation (asset generation is dev-time-only,
per `docs/agents/asset-pipeline.md`). The only thing Cloudflare exists for is letting players see
each other's scores.

## Decision

Rule 3's "no request to any other origin, ever" gets exactly one exception:

- **One endpoint:** a single Cloudflare Worker (`worker/`, backed by D1), reachable only through
  `web/src/net/index.js`. No other network call is sanctioned anywhere in `web/src/`.
- **Confined to shell timing:** the leaderboard client is only ever called from `load()` or
  `result()` — never from `update()` or `input()`, so a slow or unreachable network can't touch a
  frame budget or a beat judgment.
- **Fails soft, always:** `leaderboardClient.js`'s `submitScore`/`fetchTopScores` catch every
  network error and resolve anyway (`fetchTopScores` resolves `[]`). The game must be fully
  playable, including seeing its own results screen, with the Worker unreachable, unregistered,
  or DNS-unresolved. Nothing awaits the network in a way that can block gameplay.
- **Mocked in the harness:** `tools/harness/inspect.mjs` builds with `VITE_MOCK_NETWORK=1` by
  default, which swaps in `mockLeaderboardClient.js` (canned scores, no network) via
  `web/src/net/index.js`'s top-level-await switch — so the harness's own "abort any off-origin
  request" enforcement from ADR 0003 keeps applying to everything else, and critic runs stay
  deterministic. `--live-network` opts a harness run into hitting the real Worker.
- **No secrets in the client:** the Worker takes no auth (an accepted v1 gap — scores are
  unauthenticated, clamped server-side); nothing sensitive is bundled into `web/`. CORS is
  enforced by the Worker (`ALLOWED_ORIGIN`), not worked around in the client.

## Consequences

Every other line of ADR 0003 is unchanged: assets still ship inside the build, nothing else
reaches another origin, and the harness still aborts and flags any fetch that isn't this one
endpoint. `docs/agents/backend-brief.md` documents the Worker/client conventions in full.
