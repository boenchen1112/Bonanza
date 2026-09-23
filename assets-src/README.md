# assets-src/

Raw source files for externally-authored Beat Bash Bonanza assets: `.blend`
projects, Meshy exports, reference mockup images used for art direction.

Kept for provenance only. Nothing here is imported by game code — the game
imports the base64-embedded module that `tools/embed-asset.mjs` generates
under `web/src/render/assets/embedded/`. See `docs/agents/asset-pipeline.md`
for the full workflow and why the embed step exists.

Suggested layout: one directory per asset, e.g. `assets-src/refs/` for
mockup images that never ship, `assets-src/<piece>/` for that piece's
`.blend`/export files.
