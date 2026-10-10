# Beat Bash Bonanza — architecture contract

**Read this before writing a line.** Many agents work on this codebase in
parallel. The contracts below are what keep that from turning into mush.

## The product

An original browser rhythm-party game series. Five minigames plus a party
shell, built in Three.js, aimed squarely at Nintendo first-party polish:
tight timing, loud kinetic feedback, readable at a glance, funny, generous
to beginners without being boring to experts.

Nothing is copied from Mario Party — no characters, no assets, no music, no
minigame designs lifted one-for-one. It competes with it; it does not
borrow from it.

## Non-negotiable rules

1. **All time is audio time.** Seconds, in the `AudioContext.currentTime`
   domain, latency-compensated via `Clock.now()`. Never judge input against
   `performance.now()` or a rAF timestamp. `Clock.toAudioTime(perfMs)`
   converts at the boundary; `Input` already does this for you.
2. **Never `lerp(a, b, 0.1)` in an update loop.** Use `damp(a, b, lambda, dt)`
   from `core/util.js`, or the feel changes with frame rate.
3. **No fetches outside the build** (ADR 0003, `docs/adr/`). Authored assets
   (`.glb`, textures, wasm, models) may be committed and imported through
   Vite — they ship inside `dist/` and load same-origin via
   `src/assets/index.js`. Nothing may be requested from any other origin
   (CDN, API, font service); the harness aborts and flags it. The build must
   run offline from any static server. (`file://` is not a target: module
   scripts are CORS-blocked there and it never worked.) Free sources only —
   CC0 or Mixamo — with a provenance README beside every asset.
   **One sanctioned exception** (ADR 0005): the cross-player leaderboard,
   through `web/src/net/index.js` only, called from `load()`/`result()`
   never `update()`/`input()`, fails soft, mocked in the harness build.
4. **Every minigame implements the same interface** (below). No exceptions,
   because the shell, the pause menu, the results screen and the automated
   critic harness all drive them generically.
5. **Own your directory.** Do not edit files outside the ones assigned to
   you. If you need something from another module, it goes through the
   documented interface; if the interface is missing something, say so in
   your report rather than reaching across.
6. **60fps is the floor, not the target.** Budget: <8ms CPU per frame on a
   2019 laptop iGPU. No per-frame allocation in hot paths. Pool everything.
7. **Determinism.** Use `makeRng(seed)` from `core/util.js`, never
   `Math.random()`, anywhere that affects gameplay. The harness replays runs.

## Module map and ownership

```
web/src/
  main.js            boot + frame loop + scene routing        [INTEGRATOR only]
  core/
    clock.js         master rhythm clock, beat<->time         [INTEGRATOR only]
    input.js         audio-timestamped abstract input         [INTEGRATOR only]
    judge.js         timing windows, note matching, ranks     [INTEGRATOR only]
    util.js          easing, damp, rng, bus, save             [INTEGRATOR only]
  audio/             synth engine, music, SFX, mixer          [audio agent]
  render/            renderer, camera, lighting, post, VFX    [render agent]
  ui/                HUD, popups, menus, transitions          [ui agent]
  chars/             procedural characters + animation        [chars agent]
  games/<id>/        one directory per minigame               [one agent each]
  shell/             title, select, results, flow             [shell agent]
```

## The minigame interface

```js
export default {
  id: 'swing-kings',
  name: 'Swing Kings',
  blurb: 'One swing. One beat. Send it.',   // shown on the rules card
  bpm: 124,
  durationBars: 32,
  controls: 'a',                            // which actions the rules card shows

  /**
   * Build everything. Called once, off the critical path — the shell shows a
   * rules card while this runs. Must not start the clock.
   * @param {GameContext} ctx
   * @returns {Promise<void>|void}
   */
  async load(ctx) {},

  /** Transport has started; beat 0 is `ctx.clock.timeAt(0)`. */
  start(ctx) {},

  /** Per frame. `dt` is real seconds, clamped. `beat` is the float beat. */
  update(ctx, dt, beat) {},

  /** Drained input events, already in audio time, in order. */
  input(ctx, events) {},

  /**
   * Return a result once the game is over, else null:
   *   {score, accuracy, rank, stats, highlights, field?}
   * `accuracy` is HIT QUALITY in every game — verdict-weighted, 0..1, the
   * number the results card prints beside PERFECT/GREAT/GOOD/MISS. A game
   * whose score is something else (Drumline's race points) keeps that in
   * `score`, never in `accuracy`.
   * `field` only from a game that actually raced the lineup: one
   * `{id, place}` per competitor (`id` = ctx.players id, null for a house
   * extra). A party scores that order as-is; without it, CPU rounds are
   * simulated and the recap says so.
   */
  result(ctx) { return null; },

  /**
   * The notes a bot should press, for the automated critic — `null` if this
   * game has none. Each entry is `{action}` plus EITHER `time` (audio time)
   * or `beat`; prefer `beat`, which main.js re-derives each frame, so a game
   * that ramps tempo stays exact.
   *
   * Without it the bot presses 'a' on eighths, which in a four-lane game
   * misses every lane and in a call-and-response game answers calls it was
   * never given. Two games forked the whole harness over this.
   */
  testChart(ctx) { return null; },

  /** Free GPU resources. Called always, even on abort. */
  dispose(ctx) {},
};
```

### `GameContext`

```js
{
  clock,      // Clock
  input,      // Input
  scene,      // THREE.Scene owned by this minigame
  camera,     // THREE.PerspectiveCamera, minigame may animate it
  renderer,   // THREE.WebGLRenderer (shared — do not dispose)
  stage,      // render/stage.js facade: shake(), flash(), chroma(), setPalette()
  audio,      // audio/index.js facade: music, sfx, mixer
  ui,         // ui/index.js facade: hud, popup(), banner(), countdown()
  fx,         // render/fx/index.js: burst(), ring(), confetti(), trail()
  bus,        // Bus for cross-module events
  onBeat,     // (fn) => unsubscribe. Use this, never clock.onBeat: the clock
              // outlives the scene, and registrations made here are released
              // on the next scene swap even if dispose() never ran.
  rng,        // seeded rng
  players,    // [{id, name, char, palette, build, isCpu, cpuSkill, dress(char)}]
  offsetMs,   // player's timing calibration (Options); pass to new NoteJudge({offsetMs})
  size,       // {w, h, dpr} — updated on resize
  opts,       // the activation options — {seed, game, from, ...}
  hitstop(s), // freeze gameplay time (NOT the clock) for s seconds
  go(id, o),  // route to another scene. Inside the `play` host, go('results',
              // result) means "this round is over", not "show that screen".
}
```

The list above is the whole context. It is written out in full because it
was not: `hitstop`, `opts`, `go` and `onBeat` were used by every minigame
while being documented nowhere, so a new game's author read a 13-key
contract and then had to read four existing games to find the rest. The tell
was `drumlineDash` calling `ctx.env?.crowd?.cheer?.()` — a member that has
never existed, with optional chaining swallowing the mistake.

Facades reached through the context carry their own surfaces; the ones the
minigames actually use, beyond what `GameContext` names:

```
ctx.ui.el(cls, text)        build a DOM node in the UI layer
ctx.ui.layer                the overlay element, for a game's private HUD
ctx.ui.countdown(text)      the count-in presenter (core/round.js drives it)
ctx.stage.createEnv(scene)  an environment disposed with the scene
ctx.stage.pulse(k)          beat agreement; punchZoom(k) for a one-off
ctx.stage.beatPulse         0..1, the current beat's envelope
ctx.stage.rig.{frame,snap,release,setPushGain}
ctx.stage.look.{materials,setShadowFocus}
ctx.audio.sfxBus            the SFX bus node, for a game's own voices
ctx.audio.ctx               the raw AudioContext — prefer clock.rawNow()
```

`players` is the session lineup, filled by the shell's `play` host
(`shell/chars.js` `gamePlayers`); `[0]` is the one human. `palette` is a rig
palette object and `build` a rig build id, both ready for `makeCharacter` /
`makeCast`; `dress(char)` adds the character's crest to a rig the game built.
A scene booted directly (the harness) gets `[]` — always keep a house
default.

## Shared feel constants

`src/core/feel.js` holds the numbers that must agree across every minigame:
countdown length, hitstop durations, shake magnitudes per verdict, popup
lifetimes, palette per verdict. Change them there, not locally, or the games
stop feeling like one product.

## The automated critic harness

`tools/harness/` boots the built game in headless Chromium, drives scripted
input at exact audio times, and dumps screenshots plus a telemetry JSON.
The game exposes `window.__BBB__` in dev (`vite`) and harness
(`vite build --mode harness`) builds only — a plain production build strips
it, so nothing in game code may depend on it at runtime (pass what you need
through `ctx`/`stage` instead):

```js
window.__BBB__ = {
  ready: Promise,           // resolves once boot completes
  goto(sceneId, opts),      // jump straight to a scene/minigame
  press(action, atBeat),    // schedule a synthetic press at an exact beat
  telemetry(),              // {fps, frameMs[], judgements[], stats}
  screenshotReady(),        // resolves when the frame is settled
  setSeed(n),
}
```

If you add a scene or a minigame, register it so `goto` can reach it, or the
critic cannot inspect your work and it will be judged as broken.
