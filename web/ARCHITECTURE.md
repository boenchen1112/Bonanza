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
3. **The game deploys to a real hosted URL (GitHub Pages) — it no longer
   has to survive `file://`.** Asset fetches by plain URL are fine now;
   `GLTFLoader.load(url)` works normally. Embedding via
   `tools/embed-asset.mjs` (see `docs/agents/asset-pipeline.md`) still
   exists and is still fine to use, but it is no longer mandatory — use it
   only when you specifically want an asset inlined (e.g. to guarantee it
   survives a flaky connection), not by default.
4. **The only sanctioned server-talking code is the leaderboard client**
   (`web/src/net/`), and it talks to exactly one Cloudflare Worker
   endpoint. No other runtime network call exists in this game — no live
   AI-generation calls, no multiplayer, no arbitrary third-party fetches.
   If a piece needs the leaderboard, it imports from `web/src/net/`; it
   never rolls its own `fetch()` to some other service.
5. **Every network call is confined to `load()`, `result()`, or a shell
   screen — never `update()`/`input()`.** A `fetch()` has latency the audio
   clock does not; putting one in the timing-critical loop breaks the exact
   thing rule 1 protects. Fire it, don't block on it inside a scored round.
6. **The game is fully playable with the Cloudflare Worker unreachable.**
   The leaderboard panel degrades to "unavailable" or cached data; nothing
   else about a minigame depends on the network at all. See
   `docs/agents/backend-brief.md` for the Worker/D1 side of this and
   `web/src/net/mockLeaderboardClient.js` for how the harness verifies both
   pieces stay decoupled.
7. **Every minigame implements the same interface** (below). No exceptions,
   because the shell, the pause menu, the results screen and the automated
   critic harness all drive them generically.
8. **Own your directory.** Do not edit files outside the ones assigned to
   you. If you need something from another module, it goes through the
   documented interface; if the interface is missing something, say so in
   your report rather than reaching across.
9. **60fps is the floor, not the target.** Budget: <8ms CPU per frame on a
   2019 laptop iGPU. No per-frame allocation in hot paths. Pool everything.
10. **Determinism.** Use `makeRng(seed)` from `core/util.js`, never
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

  /** Return a result once the game is over, else null. */
  result(ctx) { return null; },  // {score, accuracy, rank, stats, highlights}

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
  rng,        // seeded rng
  players,    // [{id, name, palette, isCpu, cpuSkill}]
  size,       // {w, h, dpr} — updated on resize
}
```

## Shared feel constants

`src/core/feel.js` holds the numbers that must agree across every minigame:
countdown length, hitstop durations, shake magnitudes per verdict, popup
lifetimes, palette per verdict. Change them there, not locally, or the games
stop feeling like one product.

## The automated critic harness

`tools/harness/` boots the built game in headless Chromium, drives scripted
input at exact audio times, and dumps screenshots plus a telemetry JSON.
The game exposes `window.__BBB__` in dev/test builds:

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
