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
5. **Place it, or embed it — the game is hosted online now, so both work.**
   Default: drop the `.glb` under `web/public/assets/<piece>.glb` and load
   it by URL — the normal, standard three.js way:
   ```js
   import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
   const { scene } = await new GLTFLoader().loadAsync('assets/<piece>.glb');
   ```
   Only reach for the embed script if you specifically want the asset
   inlined into the JS (e.g. to guarantee it survives a flaky connection on
   first paint):
   ```bash
   node tools/embed-asset.mjs assets-src/<piece>/model.glb \
     --out web/src/render/assets/embedded/<piece>.embed.js \
     --name <piece>
   ```
   That writes a module exporting `<piece>Data` (base64), `<piece>Mime`,
   `<piece>Bytes`, consumed via `render/assets/loadModel.js`'s
   `loadEmbeddedModel(<piece>Data)`. It warns if the bundle cost looks
   large — read the warning, don't ignore it.
6. **Hand the consuming agent the import** — either the plain asset path or
   the embedded module, whichever you chose in step 5 — not the raw
   `assets-src/` file.
7. **Verify exactly like any other piece** — run the harness, read the
   screenshots, check `cpuMs`/`drawCalls` in `summary.json`. An asset that
   looks right in Blender but blows the draw-call budget in the harness is
   not done; go back to step 3.

## Rules specific to this pipeline

- **A plain URL reference is fine now — it wasn't when the game had to
  survive `file://`.** `GLTFLoader.load(url)`/`loadAsync(url)` works
  normally against a real hosted build. (For the record: it never worked
  reliably from a double-clicked `dist/index.html`, since Chrome blocks
  `fetch()`/XHR from one `file://` resource to another — that's the whole
  reason the embed script exists as an option, not why URL loading is
  banned. It isn't banned anymore.)
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
