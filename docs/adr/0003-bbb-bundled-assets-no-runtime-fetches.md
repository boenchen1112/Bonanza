# Beat Bash Bonanza may bundle authored assets; the rule is "no fetches outside the build"

`web/ARCHITECTURE.md` rule 3 said everything — geometry, textures, music, SFX — must be generated in code or embedded, and that the build must run from `file://` offline. That rule is the largest single cause of the series looking like a prototype: every character and prop is `BoxGeometry`/`CapsuleGeometry` primitives on a segmented joint rig, with no skinned meshes, no authored animation, and no textures. The portfolio-polish pass (`docs/BBB_Portfolio_Polish_Spec.md`) needs real skinned characters and a prop kit.

It also turned out the `file://` half of the rule was never true. A 2026-09-11 check loaded the built `index.html` from disk in Chromium: the module entry script is blocked by CORS (`origin 'null'`), and nothing runs. Every build since Vite was adopted has needed a static server.

## Decision

Rule 3 becomes: **no fetches outside the build.**

- Authored assets (`.glb` models and animation clips, textures, fonts, wasm, ML models) may be committed and imported through Vite, which emits them into `dist/` with hashed names. At runtime the game requests only its own files, same-origin.
- The game must run **offline from any static server** (`npm --prefix web run preview`, the harness's `serve.mjs`, itch.io, GitHub Pages) with zero network beyond that server. `file://` is dropped as a requirement — it never worked.
- **No request to any other origin, ever:** no CDN (jsDelivr, unpkg, Google Storage), no font service, no API. This explicitly covers the MediaPipe hand-tracking work in `research/hand_tracking_web/`, which currently pulls `@mediapipe/tasks-vision` from jsDelivr and `hand_landmarker.task` from Google Storage — when it moves into the game, the wasm and the model file are installed from npm / committed and bundled.
- The harness enforces this: it aborts every off-origin request and logs it as `[external-fetch]`, which makes `consoleClean` false and fails `tools/harness/sweep.mjs`.
- Sources must be free: CC0 (Kenney, Quaternius, Poly Haven) or Mixamo (royalty-free inside a game; the raw downloads must not be redistributed, so only converted files are committed and the raw FBX stay in a gitignored `Mixamo/`). Every asset directory carries a README with source, licence and how it was built; conversion scripts live in `tools/assets/`.
- Load through `web/src/assets/index.js` (`loadGLB(key)`, cached per session). Scene `load()` is already allowed to be async, so asset loading happens there.

Unchanged: all time is audio time (`Clock.now()`), `makeRng(seed)` for anything gameplay-affecting, and harness replay determinism — imported clips are data, as deterministic as the procedural animation they sit under.

## Consequences

The first load is heavier (the Y Bot character + 8 clips is ~2 MB) and scene loads become asynchronous. The "runs from a double-clicked file" story is gone, but it was never real. Procedural generation stays welcome where it is the better tool (the crowd, VFX, the synth music); this ADR removes a prohibition, it does not mandate replacing working procedural systems.
