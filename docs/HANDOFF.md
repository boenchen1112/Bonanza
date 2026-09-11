# Beat Bash Bonanza — handoff

**Updated 2026-09-11 (portfolio-polish pass).** Branch `bbb-portfolio-polish`,
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
| Tests | 90/90 green (`npm --prefix web test`) + `swingKings/verify.mjs` (now on the real GPU, incl. bat-meets-ball contact checks) and `swingKings/smoke-conduct.mjs` ALL PASS |
| Critic rounds completed | Swing Kings: 4 FAIL rounds, fixes landed for each, round 5 running. Shell: round 1 FAIL, fixes landed, round 2 running. See the tickets file (13, 19) |
| Minigames | five, all playable; Swing Kings is the polished hero, the other four had a baseline pass |

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

## 4. Next actions, in order

See `docs/BBB_Portfolio_Polish_Tickets.md` for the live list. Beyond it:

1. **Critic rounds for the other four games** (they only had a baseline
   pass). Same brief, same loop.
2. **A person listens** to every track (`runs/*/audio.wav` from any harness
   run) and plays the webcam mode once.
3. Calibration, accessibility.

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
  dress pass, `compileAsync`) before `start()`; the stall is still there,
  but it sits under the `play` host's title card instead of inside the
  game's clock. `tools/harness/lineup-probe.mjs` measures both halves.
- **Calling a disposed scene during the next load.** `activate()` clears
  `current` before awaiting the next `load()`. It used not to, so the loop
  kept updating the disposed scene — harmless for most, but `play → play`
  (pause-menu restart) reached the new instance's half-built state.
- **Identity through `ctx.players`, not shell imports.** Games read palette,
  build and `dress()` from `ctx.players` (ARCHITECTURE.md) and must keep a
  house default for `[]` — the harness boots games directly.
- **A scene throwing in `load()` used to kill the whole app** — `ready` never
  resolved and the harness reported a 20s timeout pointing nowhere near the
  cause. `activate()` now contains scene failures and records them on
  `window.__BBB__.lastError`. If a scene seems to hang, read that field.

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
