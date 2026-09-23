# Asset pipeline brief — Beat Bash Bonanza

You are authoring an asset externally (Blender, Meshy, an image model) for a
piece someone else owns (a `games/<id>/`, `chars/`, or `render/env/` agent).
You do not touch their code. You hand them an embedded module; they import
it. Read `web/ARCHITECTURE.md` rule 3 first — this brief is how that rule
gets satisfied in practice, not an exception to it.

## Why this exists

Every prior Bonanza asset was generated in code. That's still the default
and still preferred for anything simple enough to hand-author cheaply — a
procedural rig, a primitive prop, a synthesized texture has zero pipeline
risk and zero file size. Reach for this pipeline only when something is
genuinely cheaper or better made externally (a detailed character mesh, a
hand-sculpted prop) than it would be built from THREE primitives in code.

## The workflow

1. **Reference images are dev-time only, never shipped.** Generate mockups
   (any image model) for art direction. They live in `assets-src/refs/` for
   provenance, are never imported by game code, and are never styled tight
   enough to a specific Mario Party asset to be recognizable as it — see
   rule 3 in `docs/agents/build-brief.md`.
2. **Model/rig/animate externally** (Blender, Meshy) as you normally would.
   Keep the working files (`.blend`, Meshy exports) in `assets-src/<piece>/`
   — tracked for provenance, never imported directly.
3. **Decimate and atlas before export.** The harness perf budget (`cpuMs`
   < 4ms, `render.drawCalls` < 120 — see `build-brief.md`) did not move for
   imported meshes. Merge materials into one texture atlas per asset where
   you can; a character that needs 8 draw calls on its own has already
   spent most of the game's budget on one piece.
4. **Export a single `.glb`.** One file, embedded textures, baked
   animations (no external animation-library dependency at runtime).
5. **Embed it:**
   ```bash
   node tools/embed-asset.mjs assets-src/<piece>/model.glb \
     --out web/src/render/assets/embedded/<piece>.embed.js \
     --name <piece>
   ```
   This writes a module exporting `<piece>Data` (base64), `<piece>Mime`,
   `<piece>Bytes`. It warns if the bundle cost looks large — read the
   warning, don't ignore it.
6. **Hand the consuming agent the import**, not the raw file:
   ```js
   import { <piece>Data } from '../render/assets/embedded/<piece>.embed.js';
   import { loadEmbeddedModel } from '../render/assets/loadModel.js';
   const model = await loadEmbeddedModel(<piece>Data);
   ```
7. **Verify exactly like any other piece** — run the harness, read the
   screenshots, check `cpuMs`/`drawCalls` in `summary.json`. An embedded
   asset that looks right in Blender but blows the draw-call budget in the
   harness is not done; go back to step 3.

## Rules specific to this pipeline

- **Never reference an asset by relative URL or a bare `fetch()`.** GLTF
  loaded via `GLTFLoader.load(url)` will work in `npm run dev` and silently
  fail in the offline `dist/` build — this has already cost a debugging
  cycle once elsewhere in this project (see the autoplay-bot incident in
  `docs/HANDOFF.md` §3 for what "worked in dev, broken in the artifact that
  actually gets judged" looks like). Always go through
  `tools/embed-asset.mjs` and `loadEmbeddedModel()`.
- **Determinism still applies to anything the asset drives at runtime.** A
  baked animation clip is fine (it's just data). A runtime pose/variation
  system built on top of the import must still use `makeRng(seed)`, never
  `Math.random()`, if it affects what the player sees during play the
  harness replays.
- **Original work only, including AI-generated work.** A Meshy character
  prompted from "the Mario Party toad-like guy" is a Nintendo character with
  extra steps. Prompt from your own mockups, not screenshots of the
  competition.
- **You do not commit.** Same as every other builder role — the integrator
  commits `assets-src/`, the generated `.embed.js`, and the consuming code
  together, so they never land out of sync.

## Reporting back

End with: what you built, the `.glb` triangle/material count before and
after decimation, the harness `cpuMs`/`drawCalls` delta versus the piece
without it, and the screenshot you actually looked at. If the budget didn't
fit, say what you cut, not just that you shipped it anyway.
