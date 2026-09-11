# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Two products in this repo

This branch (`Mario-Party`) holds two independently-developed products that
share a repo but not a codebase:

1. **Swinger — All-Star Swingers**: a single conducting-rhythm baseball
   minigame, Python-prototyped then ported to Unity (C#). See "Swinger" below.
2. **Beat Bash Bonanza**: an original browser rhythm-party game series
   (Three.js) — five minigames plus a party shell, built as a from-scratch
   competitor to Mario Party (no assets/characters/designs borrowed). Lives
   entirely under `web/`. See "Beat Bash Bonanza" below.

They do not import from each other. When working in one, you generally don't
need to touch the other.

---

## Beat Bash Bonanza (`web/`)

### Commands

```bash
npm --prefix web run dev       # vite dev server, http://127.0.0.1:5178
npm --prefix web run build     # production build -> web/dist
npm --prefix web run preview   # preview the build, http://127.0.0.1:5179
npm --prefix web test          # node --test "tests/**/*.test.mjs" (currently clock.test.mjs, judge.test.mjs)
```

Run a single test file directly: `node --test web/tests/clock.test.mjs`.

The automated critic harness (boots the **built** game in headless Chromium,
plays it, dumps screenshots + telemetry — this is how gameplay/feel/perf are
actually verified, not by reading code):

```bash
node tools/harness/inspect.mjs --scene <id> --dist dist-<yours> \
  --out runs/<yours> --seconds 12 --shots 10 --play auto|perfect|sloppy|none
```

Use your own `--dist`/`--out` when working alongside other agents so builds
don't collide. Read `docs/HANDOFF.md` §3 before trusting harness numbers.
It renders headless on the real GPU (bundled Chromium, ANGLE/D3D11) at
display rate with `--quality high` by default; `summary.json` → `gpu` /
`softwareRendered` says what drew the frames. `--swiftshader` forces the old
~2-3fps software path, where `fps`/`frameMs` are meaningless and timed
effects pile up. Budget perf on `cpuMs` and `render.drawCalls` either way.
The harness builds with `vite build --mode harness`; run `npm --prefix web
install` first in a fresh worktree (Playwright resolves from `web/`).

### Architecture

Read `web/ARCHITECTURE.md` in full before editing — it's the binding
contract, not background reading. Read `docs/HANDOFF.md` first for current
state (what's built vs. stubbed vs. never verified).

- **All time is audio time.** Everything is seconds in the
  `AudioContext.currentTime` domain via `Clock.now()` — never
  `performance.now()` or a raw rAF timestamp.
- **Determinism.** `makeRng(seed)` from `core/util.js`, never `Math.random()`,
  anywhere that affects gameplay — the harness replays runs.
- **No fetches outside the build** (ADR 0003). Authored assets (`.glb`,
  textures, wasm) may be bundled via Vite and loaded same-origin through
  `web/src/assets/index.js`; nothing is ever requested from another origin
  (the harness aborts and flags it). Runs offline from any static server —
  `file://` was never a working target. Free sources only (CC0 / Mixamo),
  provenance README beside each asset; raw Mixamo FBX stay in gitignored
  `Mixamo/`, converted by `tools/assets/convert-mixamo.mjs`.
- **Every minigame implements the same interface** (`load/start/update/
  input/result/dispose`, documented in full in `web/ARCHITECTURE.md`) so the
  shell, pause menu, results screen, and harness can drive them generically.
  A game not registered in `web/src/shell/registry.js` is invisible to the
  harness/critic.
- **Directory ownership** (`web/src/`):
  - `main.js`, `core/` (`clock.js`, `input.js`, `judge.js`, `util.js`) —
    integrator-only; this is the shared timing/input/judging core, the
    strongest and only test-pinned part of the codebase.
  - `audio/` — synth engine, music player (lookahead scheduling), theory
    helpers, seven original tracks as data. Muted in the harness — least
    verified area in the project.
  - `render/` — renderer, camera, lighting/post (`look/`, `env/`), pooled
    VFX (`fx/`).
  - `ui/` — HUD, popups, menus. Currently half-wired: `font.js`/`styles.js`
    exist but `ui/index.js` hasn't been rewritten to use them yet.
  - `chars/` — procedural character rig, beat-driven animation, instanced
    crowd. Reviewable standalone via the `chars-demo` scene.
  - `games/<id>/` — one directory per minigame (`swingKings`,
    `chompChorus`, `drumlineDash`, `finaleFever`, `bounceBrigade`).
  - `shell/` — title, roster, freeplay, select, results, pause, nav, save
    state.
- `src/core/feel.js` (referenced from `web/src/`) holds cross-game feel
  constants (countdown length, hitstop, shake magnitudes, popup lifetimes) —
  change them there, not per-game.
- **Input is taps-first.** Every game plays on taps and the harness drives
  taps. Two scoped exceptions: Baton Brawl (ADR 0002) and Swing Kings'
  opt-in conducting modes — mouse hold/drag and webcam hand tracking,
  chosen in Options (ADR 0004). Those modes are harness-exempt and covered
  by `swingKings/verify.mjs` + `swingKings/smoke-conduct.mjs` instead.
  Don't add gesture input elsewhere without reading `docs/HANDOFF.md` §6.
- Build process notes: `docs/agents/build-brief.md` and
  `docs/agents/critic-brief.md` define the builder/critic agent workflow
  this codebase is developed under (one builder per module, then a separate
  critic with fresh context that inspects the running game via the harness
  and never reads the builder's summary). `docs/HANDOFF.md` §5 lists traps
  already paid for — read before re-deriving them (e.g. use
  `x - Math.floor(x)` for beat phase, never `x % 1`, since the transport
  runs negative beats through the lead-in).

---

## Swinger — All-Star Swingers

Conducting-rhythm baseball minigame. Player swings a right Joy-Con on the
beat; the game scores timing accuracy (against a metronome) and gesture
sharpness (how clean the "ictus" direction-change is), then resolves it as a
baseball hit tier (Bunt / Line Drive / Home Run).

This is one minigame in a larger Mario-Party-style set that doubles as a
conducting-technique trainer, and the first place two long-running threads
meet: the VR chord-game project (Unity) and a Joy-Con-based conducting
analysis tool.

### Platform roadmap

1. **v1 (superseded as a product):** Python rule-based prototype. `pygame`
   for game loop/visuals/audio, `joycon-python`/`hid` for right Joy-Con
   gyro input. Validated detection, calibration, and scoring before the
   Unity port existed.
2. **v3 (current product):** 2.5D Unity port — 3D characters/environment
   on a fixed camera, no free player movement (Super Mario Party style).
   Detection/calibration/scoring logic ported to C# largely as-is.
   **Unity is the sole game as of `Swinger_Build_Plan_v4.md` Phase A** —
   `src/game.py`'s pygame loop is retired as a product and is not
   developed in parallel with the Unity build; see "`src/` is tooling, not
   a product" below.
3. **v4 (current focus):** pivot from a timing-verdict minigame to a
   conducting-trace trainer — see `Swinger_Build_Plan_v4.md`.
4. **Long-term:** VR, converging with the existing VR chord-game project.

**`src/` is tooling, not a product (Build Plan v4 Phase A):** with Unity as
the sole game, `src/` exists only to support it: real-hardware capture
(`joycon_stream.py`, `joycon_udp_bridge.py` — the latter is what Unity's
input path actually depends on for the UDP-bridge fallback), offline
replay/tuning (`ictus_detector.py`, `scoring.py`, `calibration.py`,
`beat_schedule.py`, `session_log.py`), and the frozen `tests/` suite that
validates that logic. `game.py` and `settings_menu.py` (the pygame render
loop and its menu) are **not maintained as a product going forward** — do
not add gameplay features there; fix only what capture/tuning work
actually needs. Unity's C# logic (`UnitySwinger/Assets/Scripts/Logic/`) is
the canonical port target and must be kept in sync with fixes made to the
Python logic modules (see `reviews/Bug_Audit_2026-07-28.md` — Unity's
`MeasureJudge.cs` had forked out of sync with Python's fixes before v4
Phase A closed that gap).

### v1 scope

- Time signature: 2/4 only. Beat 1 (downbeat/ictus) is scored; beat 2 is not.
- Input: right Joy-Con only, Bluetooth, raw combined gyro magnitude
  (no per-axis/dominant-axis calibration in v1).
- Tempo source: self-generated metronome click (fixed BPM), not real audio.
- Ictus detection: not the gyro peak, but the sharp deceleration right after
  it (direction-change event).
- Calibration: automatic on run, offered via Settings after a round (not
  forced every session); offset persists to `calibration.json`.
- 3 misses = out.

Full spec, phase-by-phase build plan, algorithms, thresholds, and deferred
scope: see `Swinger_Build_Plan_v1.md` in this folder (Phases 0–4, plus
Section 6 — explicitly deferred items). Later `Swinger_Build_Plan_v2.md`
through `v6.md` in the same folder supersede/extend it phase by phase.

### Commands

No package manager — run Python tooling scripts directly, e.g.:

```bash
python src/game.py            # retired pygame prototype; fix only what tooling needs
python -m pytest tests/       # frozen validation suite (offline, no hardware/pygame needed except where noted)
python -m pytest tests/test_ictus_detector_smoke.py   # single test file
```

Unity: open `UnitySwinger/` in the Unity Editor; `Assets/Scripts/Logic/` is
plain C# (no UnityEngine dependency) and is the canonical port target.

### Definition of "v0 prototype-ready"

A person with a paired right Joy-Con runs one command, hears a metronome,
swings once per measure, and sees: a timing judgment (Perfect/Great/Good/
Miss), a sharpness tier (Bunt/Line Drive/Home Run), an out count, and an
end-of-round summary of timing offsets and sharpness — with calibration
available from Settings after the round. Sections 1–5 of the build plan must
be functional, not stubbed.

---

## Folder structure

- `.claude/` — Claude Code project config
- `web/` — Beat Bash Bonanza (Three.js); see that section above
- `src/` — Swinger capture/tuning tooling only, not a maintained product
  (see "Platform roadmap" above): `joycon_stream.py`,
  `joycon_udp_bridge.py`, `metronome.py`, `beat_schedule.py`,
  `ictus_detector.py`, `calibration.py`, `scoring.py`, `session_log.py`,
  plus the retired `game.py`/`settings_menu.py` pygame prototype
- `archive/` (under `src/`) — stale/contaminated captures kept for
  provenance, not for re-deriving anything from
- `tests/` — frozen Python validation suite backing the Swinger logic
  modules above
- `tools/` — `tools/harness/` is the Beat Bash Bonanza critic harness
  (`inspect.mjs`, `serve.mjs`); `tools/generate_golden_traces.py` and
  `tools/golden/` feed the Unity port's golden-trace parity tests
- `UnitySwinger/` — the Unity project; `Assets/Scripts/Logic/` is the
  canonical, UnityEngine-free ported Swinger logic (keep in sync with
  `src/`'s logic modules); `Assets/Scripts/Presentation/` and
  `Assets/Scripts/Input/` are Unity-specific (rendering, UDP receive,
  calibration flow)
- `research/` — hand-tracking and gesture-trace exploration (`research/
  hand_tracking_web/`, `extract_pattern_from_video.py`,
  `live_trace_view.py`, etc.), separate from both products' shipped code
- `docs/agents/` — agent-facing process docs: `issue-tracker.md` (GitHub
  issues in `boenchen1112/Swinger`), `domain.md` (Swinger's `CONTEXT.md` +
  `docs/adr/`), `build-brief.md`/`critic-brief.md` (Beat Bash Bonanza
  builder/critic workflow)
- `docs/design/minigames.md` — Beat Bash Bonanza minigame design docs
- `docs/HANDOFF.md` — Beat Bash Bonanza current status and next actions
- `reviews/` — playtest notes, design reviews
- `progress/` — Beat Bash Bonanza live progress page (`index.html`,
  `state.json`)

## Agent skills

### Issue tracker

Issues live as GitHub issues in boenchen1112/Swinger (private repo). See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context — `CONTEXT.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.
