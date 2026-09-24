# Beat Bash Bonanza — handoff

**Updated 2026-09-18 (performance + designed cast).** Branch `bbb-portfolio-polish`,
draft PR [#22](https://github.com/boenchen1112/Bonanza/pull/22) into
`Mario-Party` (never `master`). The pass is specified in
`docs/BBB_Portfolio_Polish_Spec.md` and tracked ticket by ticket in
`docs/BBB_Portfolio_Polish_Tickets.md` — read that file for what changed and
why; this document is the standing state of play.

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
| Builds | yes, clean (production build strips the `__BBB__` test API; the harness builds `--mode harness`) |
| Runs | yes — every registered scene loads console-clean on the real GPU (`tools/harness/sweep.mjs`) |
| Tests | 165/165 green (`npm --prefix web test`) + `swingKings/verify.mjs` (now on the real GPU, incl. bat-meets-ball contact checks) and `swingKings/smoke-conduct.mjs` ALL PASS |
| Critic rounds completed | Swing Kings: capped at round 7 without a PASS (deliberate — "loop until PASS" has no fixed point against a blind first-party bar; remaining gaps logged as known-gaps in ticket 19/24, one closed in ticket 27). Shell: capped at round 3 without a PASS, same reason, gaps in ticket 19. See the tickets file (13, 19). |
| Minigames | five, all playable; Swing Kings is the polished hero, the other four had a baseline pass |
| Cast | all eight rebuilt to the approved design sheet (faces, shape-matched shells, five colour roles, outline), 6 draw calls each |
| Performance | Graphics Auto holds 60fps on the DPR-2 Iris Xe laptop; see §3b |

The earlier waves' builders were cut short by a spend limit and no critic
ever ran; the portfolio-polish pass is the first time the builder/critic
loop has actually been exercised.

### What genuinely exists

- **Timing core** (`web/src/core/`) — the strongest part of the codebase, and
  the only part that is test-pinned. Audio-time clock, abstract timestamped
  input, nearest-note judge, shared feel constants.
- **Audio** (`web/src/audio/`) — synthesis voices, reverb/delay sends, a
  music player with lookahead scheduling, music theory helpers, and seven
  original tracks as data. Verdict SFX are pitched into the current chord.
  The harness now captures the mix and checks level + beat sync on every
  run; no person has listened to it yet.
- **Look** (`web/src/render/look/`, `env/`) — house toon style, lighting rig,
  palettes, gradient sky, blob shadows plus one opt-in real shadow map
  (`look.setShadowFocus`), bloom/vignette/chroma post, and a beat-reactive
  environment kit.
- **Mocap on the toy rig** (`web/src/chars/retarget.js`, `clips.gen.js`) —
  Mixamo clips baked offline into the procedural rig's pose channels and
  played through the same animator (`anim.play()`); the why is in the
  tickets file (re-plan note above ticket 07).
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

- **Critic PASS.** In progress for Swing Kings and the shell (tickets 13,
  19); the other four games had a baseline pass only, no critic round.
- **Conducting gesture: Swing Kings only, opt-in** (ADR 0004) — mouse drag
  and webcam hand tracking, chosen in Options; taps stay the default.
  Bounce Brigade's charge is still a double-tap.
- **`select.js` was removed** (2026-09-11): a placeholder nothing in the
  flow routed to. Title → PARTY / FREE PLAY / OPTIONS; the game picker is
  `freeplay`. `results`, `party` and `options` are real scenes now.
- **Audio has still never been heard by a person.** Since 2026-09-11 the
  harness captures the mix and machine-checks level + beat sync (§3 item 5);
  Swing Kings passes (on the 8th grid, -4ms bias). Taste is still unjudged.
- **No calibration, no accessibility pass, no coherence pass.**

## 3. The harness — read this before trusting any measurement

```bash
node tools/harness/inspect.mjs --scene <id> --dist dist-<yours> \
  --out runs/<yours> --seconds 12 --shots 10 --play auto|perfect|sloppy|none
```

Boots the **built** game in headless Chromium, plays it, dumps screenshots +
`telemetry.json` + `console.log` + `summary.json`. Use your own `--dist` and
`--out` so parallel agents never collide.

**It renders on the machine's real GPU** (since 2026-09-11, portfolio-polish
ticket 01). Playwright's full Chromium runs in new-headless mode — no window
opens — with ANGLE/D3D11 on Windows, so frames arrive at display rate and
`--quality` defaults to `high` (the full post chain). `summary.json` names the
adapter in `gpu` and sets `softwareRendered` from it; check both before
trusting a number. Consequences:

1. **`fps`/`frameMs` mean something again** on the GPU path (~55-60fps for
   Swing Kings on the dev laptop's Iris Xe). Still budget on `cpuMs` (our JS
   per frame, < 4ms) and `render.drawCalls` (< 120) — those are the numbers
   that transfer to other hardware. `drawCalls` now counts the whole frame
   (shadow + world passes); `postPasses` counts the post chain separately.
2. **Shots are paced in audio time.** Frame *i* is taken when
   `Clock.now()` crosses its slot, then after `screenshotReady()` — never
   after a wall-clock sleep — so what a shot shows cannot drift from the
   music even if a screenshot is slow.
3. **`--swiftshader` restores the old software path** for a machine with no
   usable GPU. On that path (and only there) the old warnings apply: frames
   run at ~2-3fps, `fps`/`frameMs` are meaningless, and timed effects pile up
   on screen (three callouts alive where 60fps would show one). Old runs and
   notes from before 2026-09-11 were all taken this way.
4. **Autoplay is frame-rate independent by construction.** Presses are
   emitted inside the frame loop carrying the audio time they were *aimed*
   at. `--play perfect` must report `meanAbsErrMs: 0`. Anything else is a
   real defect. (The original setTimeout-driven bot delivered presses
   hundreds of ms late and scored 0 hits / 20 misses — that was very nearly
   filed as a broken game.)
5. **Audio is captured and checked.** An AudioWorklet taps the master bus
   (on the context clock — `--mute-audio` silences speakers, not the graph)
   and writes `audio.wav`; `summary.audio` reports level, clipping, and
   whether onsets sit on the game's beat grid (`pass`, `bestGrid`, `biasMs`
   — see `tools/harness/audiocheck.mjs`, unit-tested in
   `web/tests/audiocheck.test.mjs`). That proves sync, not taste: a person
   still has to listen to the WAV. `--no-audio` skips it.
6. **`--video`** writes Playwright's screen recording (`video/*.webm`,
   fixed 25fps, no audio track). Fine for review; portfolio footage should
   come from a real browser/OBS capture.

Scenes reachable: `title roster freeplay party options play results` (+ `chars-demo`) and the five
game ids. Anything not in `web/src/shell/registry.js` is invisible to review.

## 3b. Performance and the cast (2026-09-18)

Spec: issue [#32](https://github.com/boenchen1112/Bonanza/issues/32); the
nine tickets it was broken into are drafted in `.scratch/perf-and-cast/`
(not yet published as issues).

**Measure at the player's pixel ratio.** The dev laptop (Iris Xe, 2560x1440
at 200% scaling) renders four times the pixels the old harness default did,
which is why lag fixes passed here and not there. `inspect.mjs --dpr 2
--budget` measures a screenshot-free window with vsync and the frame-rate
limit off (at display rate, headless Chromium's own pacing puts p95 near
18.7ms whatever the game does) and reports PASS/FAIL against p95 <= 16.7ms
plus at most one frame over 50ms a minute.

**Graphics setting.** Options has Auto (default) / High / Medium / Low.
`render/quality.js` holds the (render scale, tier) ladder and start-level
policy; `render/governor.js` moves Auto between rounds — one severe step
mid-round at most, never into or out of the low tier (that recompiles every
lit shader, ~1.2s), never back into a level that failed a round, and it
remembers the level per device. On the Iris Xe it settles at
(1.5, medium): Swing Kings p95 10.9ms, Finale Fever 11.8ms, party hub with
four characters 8.7ms.

**The cast.** All eight are built by
`tools/assets/blender/build-character.mjs` to the approved sheet
(`docs/design/cast-sheet.html`): one skinned mesh, five role materials,
head shell with a real face, face morph targets, 6 draw calls each.
`cast-contract.mjs` checks a built file and runs inside `npm test`;
`contact-sheet.mjs` renders the review sheet.

## 4. Next actions, in order

See `docs/BBB_Portfolio_Polish_Tickets.md` for the live list. Beyond it:

1. **Publish the nine perf/cast tickets** in `.scratch/perf-and-cast/` as
   GitHub issues (blocked on permission when they were drafted).
2. **Mid-round shader compiles.** With the count-in text stalls fixed, one
   or two ~330ms frames per Swing Kings round remain: runtime-created
   effect materials compiling on first use, which `stage.warm()` never saw
   because they do not exist at load. Overlay now attributes them
   (update / render / other).
3. **Critic rounds for the other four games** (they only had a baseline
   pass). Same brief, same loop.
4. **A person listens** to every track (`runs/*/audio.wav` from any harness
   run) and plays the webcam mode once.
5. Accessibility. (Timing calibration now exists: Options > TIMING OFFSET
   reaches every game's judge, +/-300ms.)

### Verified, so stop worrying about it

The Finale Fever tempo ramp — previously called out as the most likely real
bug — is **correct**. 60 seconds spanning the full ramp: 108 hits, 0 misses
attributable to timing, **0.00ms mean absolute error**. The chart stays
locked to the transport across every `setBpm`.

## 5. Traps already paid for — do not re-introduce

**Canvas text is a GPU readback.** A canvas big enough for Chrome to
accelerate (a banner-sized string) costs 60-235ms to read back with
`toDataURL`, on the frame it happens — every count-in beat of a fresh
session. `ui/font.js` paints with `willReadFrequently` and encodes with
`toBlob`. Displaying a copy of the canvas instead is worse: a new
GPU-backed canvas per popup measured 300-450ms.

**`#include` must start its line** in a three.js shader chunk. The house
`materials.outline()` never compiled because it did not.

**Never scale an absolute beat count by a rate that drifts.**
`frac(beat * rate)` sweeps through whole cycles when `rate` moves and
`beat` is large — it made the crowd buzz. Blend two fixed-rate layers
instead.

**An impulse multiplies, never sets.** `react()` used to write an absolute
root scale, so a character a scene had sized popped to unit size on every
verdict.

- **`x % 1` for beat phase.** JS modulo keeps the dividend's sign and the
  transport runs *negative* beats through the lead-in. Use `beatPhase(x)`
  from `core/util.js` — it is one function now, after being re-derived in a
  dozen files. This bug crashed every free-play preview.
- **`clock.onBeat` from a scene.** The clock outlives every scene, so a
  discarded unsubscribe keeps firing inside whatever loads next. Use
  `ctx.onBeat`, which main.js releases on the swap. Two call sites had
  already leaked: the title screen stacked a kick voice per visit and Bounce
  Brigade's count SFX played inside the following minigame.
- **`stage.attach()` without the scene id.** Palette inference keys on it; a
  scene attached without one is graded in the *previous* scene's palette for
  its first frames and then cross-fades out of it.
- **`clock.stop()` to pause.** It drops the one-shot schedule. `suspend()`
  keeps it and returns the beat to resume from.
- **Returning a raw object from `result()`.** Go through `roundResult()` in
  `core/result.js`: it is the only thing enforcing that `accuracy` is hit
  quality 0..1 and that a `field` is a real race order.
- **A new minigame without `testChart()`.** Without it the critic bot presses
  'a' on eighths, which in a four-lane game misses every lane and proves
  nothing. Measured on Chomp Chorus: 16/16 MISS before, 13/13 PERFECT after.
- **Mutating shared fx/stage state in `load()` and unwinding it in
  `dispose()`.** Don't — `fx.reset()` and `stage.detach()` restore their own
  defaults on every swap. Half the mutations used to be one-way, so whatever
  the last minigame left behind was what the next one inherited.
- **A blanket `catch {}` around a harness read.** `sweep.mjs` reported
  "HARNESS FAILURE" on all thirteen scenes for a missing import in itself.
- **Zeroing `dt` for hitstop.** Subtract only the frozen portion, or a 78ms
  hitstop eats a whole frame on a slow device.
- **`renderer.info` resetting per render() call.** The post chain calls
  render() several times per frame; with the default `autoReset` every
  scene reported 1 draw call. The stage turns autoReset off and resets once
  per frame.
- **Trusting SwiftShader frames.** Many old "bugs" (callout pile-ups, a
  stuck Chomp beam, CONTEXT_LOST spam) were 2fps software-render artifacts.
  Re-check on the GPU path before fixing anything seen in an old run.
- **Two verdict callouts.** `fx` auto-bridges the `judge` bus event into a
  full layered response including an in-world callout. Do not also fire a
  DOM popup for the same verdict.
- **Fixed-size text atlases.** `wordAtlas` silently drops words past its row
  count — ten of fifteen callouts were unrenderable and nothing errored.
- **Parallel agents and git.** Builders must never run git commands in a
  shared tree. The integrator commits.
- **Restarting the transport under a playing track.** Every shell scene
  calls `clock.start(now, 0)`; the music player now notices
  (`clock.generation`) and re-anchors. Anything else that caches a beat
  cursor must do the same, or it goes silent until the new clock catches up.
- **Lerping raw Euler angles.** Mocap hips spin many turns; blends and
  damping must take the short way round (anim.js `IS_ANGLE`/`wrapPi`), and
  the bake must decompose on the continuous branch (`retarget.eulerNear`,
  hips in yaw-first `HIPS_ORDER`). Re-bake with `node tools/assets/bake-clips.mjs`.
- **The default arena floor sits at y=0.** A scene that stands its cast on
  the house floor (-1.6) must set `scene.userData.groundY`, or the arena
  floor hides everything below 0.
- **Compiling on the first frame.** On ANGLE/D3D11 a fresh scene's shader
  compiles blocked the first frame ~1.2s (Swing Kings) with the count-in
  already running. `activate()` now awaits `stage.warm()` (default set,
  dress pass, `compileAsync`) before `start()`, so the stall sits under the
  `play` host's title card instead of inside the game's clock — but the
  stall itself was still one blocking JS call: `renderer.compile()` (which
  `compileAsync` calls first) is a plain synchronous pass, confirmed
  against three's own source, and texture upload is exactly as synchronous
  when it happens lazily. `warm()` now compiles per unique material (a rig
  is ~20 segment meshes sharing far fewer real shaders) in small batches
  via a fake root that forwards `traverse`/`traverseVisible` without
  reparenting anything, yielding a real animation frame once real time has
  elapsed, and pre-uploads textures the same way via `renderer.initTexture`
  (which `compileAsync` never touched). Measured on title/roster/party:
  compile+upload time roughly halved to a third (2274ms → 866ms on title).
  **Swing Kings still shows one ~800-900ms frame regardless of batch
  size** — isolated by per-batch timing to ONE shader whose link time is
  that expensive on this GPU/driver; splitting it further needs
  shader-level work (simplify or precompile that specific material), not a
  scheduling change. `tools/harness/lineup-probe.mjs` measures both
  halves; `tools/harness/freeze-probe.mjs` / `freeze-auto-probe.mjs` add a
  CPU-profile + frame-timing breakdown for real in-page navigation and for
  the harness's own `--play auto` path.
- **Building N throwaway 3D renders synchronously.** `shell/chars.js`'s
  portrait cache used to build all character busts (mesh + 20 animator
  ticks + a WebGL render + a GPU readback, per character) in one tight
  loop on first use — the single worst frame anywhere in the shell
  (1216ms cold-boot on roster). `pumpBusts(budgetMs)` now builds a few ms
  per call; `title.js` pumps it during idle frames, `roster.js` carries
  the same pump as a fallback plus a repaint sweep for any card/slot face
  that already drew the cheap placeholder. Cold-boot roster worst frame:
  1216ms → 66.9ms.
- **Calling a disposed scene during the next load.** `activate()` clears
  `current` before awaiting the next `load()`. It used not to, so the loop
  kept updating the disposed scene — harmless for most, but `play → play`
  (pause-menu restart) reached the new instance's half-built state.
- **Only testing games booted directly.** The harness loads a minigame under
  its own scene id; players reach it through the shell's `play` host. That
  gap hid a silent soundtrack for every menu-launched game (`play` mapped to
  "silent" in `trackForScene`; now `HOST_SCENES`). Check anything touching
  scene activation through `tools/harness/lineup-probe.mjs` too.
- **`music.stop()` for a pause.** It drops the track; nothing restarts it.
  Use `music.pause()` / `music.resume()` (cursor kept, lookahead rewound).
- **Identity through `ctx.players`, not shell imports.** Games read palette,
  build and `dress()` from `ctx.players` (ARCHITECTURE.md) and must keep a
  house default for `[]` — the harness boots games directly.
- **A scene throwing in `load()` used to kill the whole app** — `ready` never
  resolved and the harness reported a 20s timeout pointing nowhere near the
  cause. `activate()` now contains scene failures and records them on
  `window.__BBB__.lastError`. If a scene seems to hang, read that field.
- **`warm()` skipped every pooled fx material.** Fixed (see the batching
  wrapper's `traverseVisible` in `render/stage.js`) — was silently
  re-applying a visibility filter to owners `materialOwner` had already
  decided to compile, and every `render/fx` particle system rests at
  `visible = false` until something fires. Meant the FIRST use of any
  effect type in a session compiled its shader synchronously, mid-gameplay.
- **`textImage()`'s `canvas.toDataURL()` (`ui/font.js`) is synchronous and
  gets paid fresh for every new (style, text) pair** — cached after that,
  but a score/combo HUD churns through many unique strings a session, and
  each first render blocks the frame it lands on (measured: 100-270ms on
  `chompChorus`/`drumlineDash`/`finaleFever`/`bounceBrigade`, all of which
  use the standard HUD counter). Not fixed: the honest fix is `canvas.
  toBlob()` + `URL.createObjectURL()` instead of `toDataURL()`, which
  makes `textImage()` async and ripples into every caller in `ui/index.js`
  — real scope for whoever finishes wiring `ui/` (see this file's own
  "Currently half-wired" note), not a drive-by fix.

## 6. Input: taps first

Every scored input in every game can be a discrete button press, so the
standard harness bot exercises all five games. Pass
`--actions a,left,down,up,right` for lane games; `--division 2` (eighths) is
the right grid — finer grids make accuracy look worse, see the critic brief.
`--chart` makes the bot press a scene's own notes (Swing Kings implements
`testChart()`), one press per note like a person.

Exceptions (harness-exempt, own scripts): Baton Brawl (ADR 0002) and Swing
Kings' opt-in conduct modes (ADR 0004) — `swingKings/verify.mjs` drives
hold/release, `swingKings/smoke-conduct.mjs` drives real mouse strokes and
boots the webcam pipeline on a fake camera.

## 7. Conventions

- All time is audio time (`Clock.now()`), never `performance.now()`.
- `damp(a, b, lambda, dt)`, never `lerp(a, b, 0.1)` in an update loop.
- `makeRng(seed)`, never `Math.random()`, in anything affecting gameplay.
- No fetches outside the build (ADR 0003): bundled assets only, loaded
  same-origin through `web/src/assets/index.js`.
- Minigames implement the interface in ARCHITECTURE.md and are registered.
