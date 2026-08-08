# Beat Bash Bonanza — handoff

**Written 2026-08-08.** Branch `claude/rhythm-party-game-713xsk`, all pushed.

Read this first, then `web/ARCHITECTURE.md`. This document is the state of
play and the plan; ARCHITECTURE.md is the contract.

---

## 1. What this is

An original browser rhythm-party game series in Three.js — five minigames
plus a party shell — built to a Nintendo first-party bar. Nothing is copied
from Mario Party: no characters, no assets, no music, no minigame designs.

The build method is deliberate and should continue: **one builder agent per
piece, then a separate critic agent with fresh context that inspects the
running game via the harness and never reads the builder's summary.** The
briefs for both roles are `docs/agents/build-brief.md` and
`docs/agents/critic-brief.md`. They are written to be handed to an agent
verbatim.

## 2. Current status, honestly

| | |
|---|---|
| Builds | yes, clean |
| Runs | yes — all 8 registered scenes load with a clean console |
| Tests | 27/27 green (`npm --prefix web test`) |
| Critic rounds completed | **zero** |
| Minigames built | **zero** — all five are placeholder spinning cubes |

Wave 1 (six parallel engine builders) was terminated part-way by a monthly
API spend limit. Their partial work was recovered and integrated, so the
engine is real but **nothing has passed the quality bar you set, because no
critic has run.** Treat every "shipped" claim below as "exists and runs",
not as "reviewed".

### What genuinely exists

- **Timing core** (`web/src/core/`) — the strongest part of the codebase, and
  the only part that is test-pinned. Audio-time clock, abstract timestamped
  input, nearest-note judge, shared feel constants.
- **Audio** (`web/src/audio/`) — synthesis voices, reverb/delay sends, a
  music player with lookahead scheduling, music theory helpers, and seven
  original tracks as data. Verdict SFX are pitched into the current chord.
  **Never heard by anyone** — the harness mutes audio. This is the single
  least-verified area in the project.
- **Look** (`web/src/render/look/`, `env/`) — house toon style, lighting rig,
  palettes, gradient sky, blob shadows, bloom/vignette/chroma post, and a
  beat-reactive environment kit.
- **VFX** (`web/src/render/fx/`) — pooled sparks, shards, streaks, rings,
  trails, confetti, decals, in-world callouts, plus a `fx.verdict()` that
  composes a layered response and a combo-escalation system.
- **Characters** (`web/src/chars/`) — procedural rig, beat-driven animation,
  instanced crowd. **Not registered as a scene and never rendered by the
  harness** (see §5).
- **Shell** (`web/src/shell/`) — animated title with cast, crowd and attract
  mode; roster; free-play carousel with live canvas previews; a play wrapper
  with pause; save/profile state.

### What is missing

- **The five minigames.** `web/src/games/*/index.js` all re-export
  `makeStub()` — a metronome with a cube. This is the top priority and it
  blocks every critic round, because a critic cannot judge Swing Kings by
  looking at a cube.
- **UI (P5) is half-wired.** `web/src/ui/font.js` (procedural typeface) and
  `styles.js` landed, but `ui/index.js` was never rewritten to use them. The
  HUD is still the original debug overlay.
- **`select.js` and `results.js`** are still integrator placeholders.
- **`party` and `options` views** are referenced by `nav.js` but have no
  scene files; they currently fall through to `select`.
- **No calibration, no accessibility pass, no coherence pass.**

## 3. The harness — read this before trusting any measurement

```bash
node tools/harness/inspect.mjs --scene <id> --dist dist-<yours> \
  --out runs/<yours> --seconds 12 --shots 10 --play auto|perfect|sloppy|none
```

Boots the **built** game in headless Chromium, plays it, dumps screenshots +
`telemetry.json` + `console.log` + `summary.json`. Use your own `--dist` and
`--out` so parallel agents never collide.

**It renders through SwiftShader at ~2-3fps at 720p.** Three consequences,
each of which already cost a debugging cycle once:

1. **`fps`/`frameMs` are meaningless.** Judge perf on `cpuMs` (our JS per
   frame, budget < 4ms) and `render.drawCalls` (budget < 120).
2. **Timed effects pile up on screen.** At 2fps three callouts are alive
   where at 60fps there would be one. Before reporting stacked or overlapping
   effects, re-run at `--width 480 --height 270` (~5x the frame rate). If it
   resolves, it is the harness.
3. **Autoplay is frame-rate independent by construction.** Presses are
   emitted inside the frame loop carrying the audio time they were *aimed*
   at. `--play perfect` must report `meanAbsErrMs: 0`. Anything else is a
   real defect. (The original setTimeout-driven bot delivered presses
   hundreds of ms late and scored 0 hits / 20 misses — that was very nearly
   filed as a broken game.)

Scenes reachable: `title roster freeplay play results select` and the five
game ids. Anything not in `web/src/shell/registry.js` is invisible to review.

## 4. Next actions, in order

1. **Wave 2 — build the five minigames.** Five parallel builders, one per
   game, against `docs/design/minigames.md`, which specifies each game's
   verb, twist, escalation and why it is in the set. Each owns exactly
   `web/src/games/<dir>/`. Delete `web/src/games/_stub.js` when the last
   one lands.
2. **Wave 2 critics.** One fresh critic per game, `critic-brief.md` verbatim,
   blind A/B against the Mario Party rhythm minigames. Loop until PASS.
3. **Finish P5 (UI).** Wire `font.js`/`styles.js` into `ui/index.js`; build
   the real HUD, timing bar, rules card and transitions.
4. **Register a `chars-demo` scene** so the character work becomes
   reviewable. It exists at `web/src/chars/demo.js`; add
   `{ id: 'chars-demo', kind: 'shell', load: () => import('../chars/demo.js') }`
   to the registry and verify it loads.
5. **Audio verification.** Nobody has heard this game. Render a track through
   `OfflineAudioContext` in the browser and assert peak <= 1.0 and non-silence
   per layer, or the whole soundtrack remains unvalidated.
6. **Real-hardware perf pass.** `cpuMs` currently reads ~11ms against a 4ms
   budget, but SwiftShader contaminates it. Needs a real GPU to separate.
7. **Coherence pass**, then `party`/`options`, calibration, accessibility.

## 5. Traps already paid for — do not re-introduce

- **`x % 1` for beat phase.** JS modulo keeps the dividend's sign and the
  transport runs *negative* beats through the lead-in. Use
  `x - Math.floor(x)`. This bug crashed every free-play preview and had
  already been guarded against in `clock.test.mjs` for `Beats.barFrac`.
- **Zeroing `dt` for hitstop.** Subtract only the frozen portion, or a 78ms
  hitstop eats a whole frame on a slow device.
- **Sampling `renderer.info` after post.** The fullscreen quads overwrite it;
  every scene reports 1 draw call. `stage.stats` captures it before post.
- **Two verdict callouts.** `fx` auto-bridges the `judge` bus event into a
  full layered response including an in-world callout. Do not also fire a
  DOM popup for the same verdict.
- **Fixed-size text atlases.** `wordAtlas` silently drops words past its row
  count — ten of fifteen callouts were unrenderable and nothing errored.
- **Parallel agents and git.** Builders must never run git commands in a
  shared tree. The integrator commits.

## 6. Conventions

- All time is audio time (`Clock.now()`), never `performance.now()`.
- `damp(a, b, lambda, dt)`, never `lerp(a, b, 0.1)` in an update loop.
- `makeRng(seed)`, never `Math.random()`, in anything affecting gameplay.
- No external fetches: every asset is generated in code.
- Minigames implement the interface in ARCHITECTURE.md and are registered.
