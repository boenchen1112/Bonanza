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
| Minigames built | **five, all playable and passing autoplay at 100%** — none reviewed by a critic |

Wave 1 (six engine builders) and Wave 2 (five minigame builders) were BOTH
terminated part-way by a monthly API spend limit. Their partial work was recovered and integrated, so the
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
  instanced crowd. Reviewable via the registered `chars-demo` scene: four
  distinct builds plus the crowd, 105 draw calls, clean console.
- **Shell** (`web/src/shell/`) — animated title with cast, crowd and attract
  mode; roster; free-play carousel with live canvas previews; a play wrapper
  with pause; save/profile state.

### What is missing

- **Critic review.** Still zero rounds. Every game is mechanically verified
  but none has been judged for feel, readability or personality.
- **The conducting gesture is deferred by decision, not oversight.** Swing
  Kings and Bounce Brigade were built around hold-and-release; both are now
  plain taps (see §6). Swing Kings derives power from timing accuracy instead
  of hold length; Bounce Brigade's two-beat charge is a double-tap, on the
  ramp and off it. Restoring the gesture later is a localised change to each
  game's `input()` plus its power source.
- **UI (P5) is half-wired.** `web/src/ui/font.js` (procedural typeface) and
  `styles.js` landed, but `ui/index.js` was never rewritten to use them. The
  HUD is still the original debug overlay. This is now the biggest visible
  quality gap.
- **`select.js` and `results.js`** are still integrator placeholders.
- **`party` and `options` views** are referenced by `nav.js` but have no
  scene files; they currently fall through to `select`.
- **Audio has still never been heard by anyone** — the harness mutes it.
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

1. **Wave 2 critics.** Every game now plays under the standard harness, so a
   critic can finally judge one. One fresh critic per game,
   `critic-brief.md` verbatim, blind A/B against the Mario Party rhythm
   minigames. Loop until PASS. Known cosmetic defect for triage: Swing Kings'
   hit-tier callout is dark-on-dark against the crowd.
2. **Finish P5 (UI)** — the biggest visible gap now that the games exist.
3. **Audio verification** via `OfflineAudioContext`: assert peak <= 1.0 and
   non-silence per layer. Nobody has heard this game.
4. **Real-hardware perf pass.** Draw calls are trustworthy and fine: 73
   (swing-kings) to 109 (chomp-chorus) against a 120 budget. `cpuMs` is
   contaminated by SwiftShader and needs a real GPU.
5. **`select.js` / `results.js`**, then `party`/`options`, coherence,
   calibration, accessibility.

### Verified, so stop worrying about it

The Finale Fever tempo ramp — previously called out as the most likely real
bug — is **correct**. 60 seconds spanning the full ramp: 108 hits, 0 misses
attributable to timing, **0.00ms mean absolute error**. The chart stays
locked to the transport across every `setBpm`.

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
- **A scene throwing in `load()` used to kill the whole app** — `ready` never
  resolved and the harness reported a 20s timeout pointing nowhere near the
  cause. `activate()` now contains scene failures and records them on
  `window.__BBB__.lastError`. If a scene seems to hang, read that field.

## 6. Input: taps only, for now

The hold-and-release conducting gesture is deliberately deferred. Every
scored input in every game is a discrete button press. Two consequences:

- The standard harness bot exercises all five games. Pass
  `--actions a,left,down,up,right` for lane games; `--division 2` (eighths)
  is the right grid — finer grids make accuracy look worse, see the critic
  brief.
- No per-game verification scripts are needed. `swingKings/verify.mjs` and
  `chompChorus/verify.mjs` are leftovers from the gesture design and are not
  part of the verification path.

## 7. Conventions

- All time is audio time (`Clock.now()`), never `performance.now()`.
- `damp(a, b, lambda, dt)`, never `lerp(a, b, 0.1)` in an update loop.
- `makeRng(seed)`, never `Math.random()`, in anything affecting gameplay.
- No external fetches: every asset is generated in code.
- Minigames implement the interface in ARCHITECTURE.md and are registered.
