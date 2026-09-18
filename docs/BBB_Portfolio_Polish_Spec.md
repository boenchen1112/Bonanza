# Beat Bash Bonanza — Portfolio Polish Spec

**Status:** Approved plan, not yet started. Produced from a grilling session on 2026-09-10.
**Deadline:** end of October 2026 (~7 weeks).
**Owner:** solo, agent-loop-driven, unattended (no phase checkpoints — run to completion or until usage depleted).
**Deliverable:** a playable WebGL build. Video capture comes later (not this pass).
**Portfolio context:** undergraduate university application. Program is games / CS / interactive
media, but the visual bar being applied is a design/art-school "B" standard. Malfunctioning
edge features are acceptable for an undergrad portfolio.

---

## 1. Decision — this is NOT a Unity rebuild

The originating request was "rebuild the whole game in Unity." It was grilled and **rejected**:

- The driver was "the Three.js version looks like a prototype, not showable." Diagnosis (from
  harness screenshots + render-pipeline read): ~half the prototype look comes from the
  self-imposed **"no external asset fetches"** rule (no models, no textures, no skinned meshes —
  everything is `BoxGeometry`/`CapsuleGeometry` primitives with a segmented-joint rig whose limbs
  telescope and clip), and the other half is **unfinished integration** (shadow maps disabled,
  three shading models coexisting, a duplicate render kit in the shell, four identical characters
  on the title screen, `select.js` still a placeholder). **The critic/polish loop the whole
  project process is built around has never run once** (`progress/state.json`: every piece
  `"rounds": 0`).
- No engine gets you to a "B" for free. A Unity rebuild = pay ~23,500 lines to re-earn a systems
  layer (the test-pinned audio-time timing/judging core) that already works, then still do the
  polish pass. Unity's "Claude plugin" is editor automation (MCP), not an animation upgrade, and
  it failed to connect this session anyway.
- Swinger's own build plans v4→v6 already pivoted *away* from Unity and explicitly state "the
  browser tool is the product, not a prototype for an eventual Unity port." Reversing that for
  BBB would be the seventh pivot in ~two months.

**The two highest-leverage fixes — skinned meshes + real bundled assets, and actually running
the polish loop — are both available in Three.js today.** That is this plan.

---

## 2. Scope

### In scope

| Item | Bar |
| --- | --- |
| **Swing Kings** (the mandatory hero — swing-on-the-beat, origin of the conducting thread) | Full "B" standard |
| **Party shell** (title, roster, select, results, pause) | Consistent art, `select.js` de-stubbed |
| **Other 4 minigames** (Drumline Dash, Bounce Brigade, Chomp Chorus, Finale Fever) | **Baseline pass only** — *release valve, first to be cut if time runs short* |
| **Audio** | Swing Kings track + shell/title music: verified, mixed, synced in-engine. Nothing more. |
| **GPU harness + video-capture pipeline** | Prerequisite for everything — must land first |
| **Verification** | ≥1 completed critic round at PASS on Swing Kings + shell |

### Explicitly out of scope

- Any Unity work. `UnitySwinger/` is untouched.
- The Swinger **ictus + sharpness scoring model** — the gesture is **trigger-only** (see §5).
- Baton Brawl (6th game) — stays unbuilt.
- Full audio pass (all 7 tracks, synth-engine review).
- Party mode CPU-scoring / multiplayer standings model.
- Accessibility pass, calibration, real-hardware perf pass beyond "runs at 60fps on the dev machine."
- Video *production* (scripted edited walkthrough) — pipeline yes, cut clips later.

### "Baseline pass" definition (the other 4 games)

- One consistent shading model (no raw `MeshStandardMaterial` + `flatShading` leaks).
- Shadow maps on.
- Cast variety wired (use the 8 palettes × 4 body types the rig already ships).
- Hit/verdict callouts legible against their background.
- Zero console errors/warnings on a harness run.
- No new gameplay, no new assets beyond what the shared character/prop kit provides.

---

## 3. Constraint change (needs an ADR)

**`web/ARCHITECTURE.md` "no external asset fetches" is relaxed to "no external asset fetches at
runtime."** Authored assets (`.glb` skinned models, textures, normal maps, imported animation
clips) are **bundled by Vite into the build** — the game still runs offline from `file://`, the
harness still replays deterministically. Write this as an ADR under `docs/adr/` and update
`web/ARCHITECTURE.md` §3.

- **Determinism preserved:** Mixamo/imported clips are deterministic. `makeRng(seed)` rules
  unchanged. Audio-time (`Clock.now()`) rules unchanged.
- **Asset sources (free / CC0 only — no Synty, no paid packs):**
  - Characters + animation: **Mixamo** (free, auto-rigged humanoid, large clip library).
  - Props / environment: **Kenney**, **Quaternius**, **Poly Haven** (all CC0).
- **Realistic quality ceiling:** clean cohesive *stylized* look — "polished student game," not
  "Synty-tier shipped indie." Sufficient for the portfolio bar.

---

## 4. Animation approach — hybrid (Q14 = B)

Keep what is genuinely portfolio-worthy in `web/src/chars/anim.js` (the state-pose machine plus
the **additive beat-phase layer**: anticipation, `backOut`/`elasticOut` overshoot,
volume-compensated squash, spring secondary motion, deterministic blinks, per-character phase
jitter). Replace the geometry it drives.

- Base layers = Mixamo skinned clips (idle / ready / windup / action / recover / celebrate /
  fail / taunt) crossfaded on beat via `AnimationMixer`.
- **The existing additive procedural layer is retargeted onto the skinned humanoid skeleton's
  bones** (bone-local rotation space, applied on top of the sampled clip pose).
- `web/src/render/look/materials.js` already ships unused `skinning_pars_vertex` /
  `skinning_vertex` GLSL and unused inverted-hull toon `outline()` — the material system was
  *built expecting skinned characters*. Wire both.
- **This retarget is the single biggest technical risk of the visual work.** If it does not
  converge, fall back to clip-only animation on the skinned characters (lose the additive nuance,
  keep the skinned geometry — still a large visual improvement over the segmented rig).

---

## 5. Conducting gesture — Swing Kings only, additive, trigger-only

Carries the conducting-trainer narrative thread (Swinger → Joy-Con analysis → this). The
hold-and-release gesture *was* in Swing Kings and Bounce Brigade and was removed per ADR 0001/0002;
`docs/HANDOFF.md` §6 says read why before restoring. Restoration is localized to the game's
`input()` + power source. `web/src/games/swingKings/verify.mjs` still has a `window.__BBB__.swing`
hold/release hook.

- **Taps stay the default and primary input.** Keyboard is always available and is "the real path."
- Gesture is an **optional second input mode**, selectable in Swing Kings.
- **Trigger-only (Q23 = A):** the gesture just fires the swing; release timing feeds the
  **existing accuracy → power model** (ADR 0001). The Swinger ictus-timing + sharpness scoring
  model is **NOT** ported in.
- **Devices:** webcam hand-tracking (primary target) with **mouse/pointer-drag as fallback**.
  Reuse `research/hand_tracking_web/` MediaPipe HandLandmarker work: detect a hand, detect a
  downward ictus motion, trigger a swing.
- **Accuracy bar: none.** "The function just needs to be there." Jank is acceptable; when it
  flakes the player switches back to keyboard. No week-1 spike gate, no precision playtest.
- **Verification:** gesture mode is **harness-exempt** (same rationale as Baton Brawl / ADR 0002 —
  a webcam can't be driven headless). The harness keeps covering **tap mode** via
  `window.__BBB__.swing`. Gesture mode gets a manual smoke check only.
- **Determinism:** webcam input is non-deterministic and not replayable — acceptable because
  gesture mode is out of the harness replay path.

---

## 6. Verification pipeline (prerequisite #1 — build this first)

The harness renders at 2–3 fps because `tools/harness/inspect.mjs:105-106` hardcodes
`--use-gl=swiftshader` / `--enable-unsafe-swiftshader`. This machine has an **Intel Iris Xe GPU**
and a full GPU-capable Chromium already installed (`~/AppData/Local/ms-playwright/chromium-1234`),
plus system Chrome. Required changes:

1. Gate/remove the two SwiftShader flags.
2. Launch a GPU-capable binary: `chromium.launch({ channel: 'chrome' })` or the bundled full
   Chromium, with `headless: false` (or new-headless, which keeps ANGLE-D3D11). Fix the
   Linux-only `executablePath` probe (`inspect.mjs:97-99`) for Windows.
3. Default `QUALITY` to `high` on the real-GPU path so the post chain is actually shown
   (`inspect.mjs:40`).
4. Use the already-wired `--video` path (Playwright `recordVideo` → webm); at true 60fps it
   becomes usable. Consider CDP `Page.startScreencast` for smoother capture.
5. Remove the slow-capture compensations that would now corrupt timing: `secs: SECONDS * 4`
   autoplay padding (`inspect.mjs:147`), the wall-clock `waitForTimeout` screenshot loop
   (`inspect.mjs:153`) — switch to the already-defined `window.__BBB__.screenshotReady()`
   (`web/src/main.js:437`, never currently called).
6. Drop `--mute-audio` (or add an `OfflineAudioContext` capture) so the critic can judge
   animation-to-music sync.
7. Update `docs/HANDOFF.md` §3 and `docs/agents/critic-brief.md`, which currently codify the
   stills-only SwiftShader-artifact workflow.

Then run the **never-run critic loop** (`docs/HANDOFF.md` §1 / `critic-brief.md`: one builder,
then a fresh blind critic, loop until PASS) on Swing Kings and the shell.

---

## 7. Known bugs to fix (Swing Kings + shell)

`docs/HANDOFF.md` and `progress/state.json` are stale (predate 7 commits — the UI rewrite,
`results.js`, `party.js`/`options.js`, nav reducers, ictus-detector port all already landed).
Actual open items:

| Bug | Location | Notes |
| --- | --- | --- |
| `flatShading` set on `MeshToonMaterial` — silently ignored, ~30 warnings/run | material setup across `render/env/*`, games | intended faceting never applied; `consoleClean: false` every run |
| Callout sprite pool is 3 sprites; a 4th within ~1s clobbers an animating slot | `web/src/games/swingKings/world.js:322` — `callouts.find(c => c.life <= 0) \|\| callouts[0]` | reachable at 60 fps in dense sections; enlarge pool or queue |
| Swing Kings hit-tier callout dark-on-dark against the crowd, illegible | `swingKings/world.js` callout render | already flagged `state.json` G1 + `HANDOFF` §4 |
| `select.js` still a literal `PLACEHOLDER` (bare banner, "replace wholesale") | `web/src/shell/select.js` | the one genuinely stubbed shell scene |
| Audio never heard or verified by anyone | `web/src/audio/` | scope here = Swing Kings track + shell music only |
| `window.__BBB__` test API ships in the production build (no `import.meta.env.DEV` guard) | `web/src/main.js:380` | dev-gate it |
| No visible bat on the Swing Kings batter in harness frames (bat parented to `handR`) | `swingKings/world.js:269` | unconfirmed — verify on the GPU path |
| Dense "curtain" of vertical white lines at the plate | half-beat pips `world.js:298` / `trace.js` ribbon | verify vs SwiftShader accumulation on the GPU path |
| Persistent vertical light beam stuck on one Chomp Chorus singer | Chomp lane FX not clearing | Chomp is baseline-tier; fix if cheap |
| `CONTEXT_LOST_WEBGL` spam at startup | every run | re-check once on the GPU path; filter or fix |

Not bugs (do not "fix"): the `x5` callout repeat-counter is designed; `--division 4` harness
misuse reads false 92 ms error on Finale Fever; the "BUNT BUNT" text stacking is mostly a
SwiftShader framerate artifact (one clean callout at 60 fps in `runs/ui-p5-play/shot-002.png`).

---

## 8. Git workflow

1. **First (this session, DONE):** committed CLAUDE.md restructure + deletions to `Mario-Party`
   (commit `3405e4a`). Deleted `email_to_dr_zoe.md` and the stray
   `research/hand_tracking_web/unity_build/`.
2. Branch **`bbb-portfolio-polish`** off `Mario-Party` (this spec lives on it).
3. **Never** push to or merge into `master`. Never force-push.
4. Commit per phase / per verified bug fix (durable progress — no review checkpoints).
5. Open a **draft PR** into `Mario-Party` early; keep its body updated as a live status report.
6. Work in a git worktree so the user's checkout stays free.

### Phase order

1. GPU harness + video-capture pipeline; update `HANDOFF.md` / `critic-brief.md`.
2. Constraint-relaxation ADR; asset pipeline (Vite bundling of `.glb` / textures); import the
   Mixamo character + a shared prop/env kit.
3. Skinned character rig + hybrid animation retarget (`chars/`), wire the unused skinning +
   toon-outline GLSL. Reviewable via the `chars-demo` scene.
4. Swing Kings to "B": art, lighting/shadows, unified shading, the §7 bug fixes, legible
   callouts, its music track synced. Run the critic loop to PASS.
5. Conducting gesture: mouse-drag fallback first, then webcam via the `research/hand_tracking_web/`
   MediaPipe path. Trigger-only. Manual smoke check.
6. Shell to "B": consistent art, de-stub `select.js`, shell music synced. Critic loop to PASS.
7. **Baseline pass on the other 4 games (release valve — cut here first if short on time).**

### Definition of done (hand-back)

Draft PR into `Mario-Party` + a written status report marking every §2 item done / partial /
untouched, with:

1. GPU harness + video capture working; docs updated.
2. Swing Kings at "B": skinned chars, hybrid animation, shadows, unified shading, legible
   callouts, §7 bugs fixed, keyboard + mouse + webcam input, music synced, ≥1 critic round PASS.
3. Shell at "B": consistent art, `select.js` real, shell music synced.
4. Other 4 games: baseline pass (or explicitly listed as cut).
5. `ARCHITECTURE.md` ADR for the relaxed asset rule; `__BBB__` dev-gated.

---

## 9. Standing risks

- **7 weeks is a full plate:** SK-to-B + animation retarget + webcam + first-ever critic loop +
  GPU harness + audio + 4 baseline passes. The baseline-4 is the release valve; drop it without
  hesitation.
- **Animation retarget** (§4) is the top technical risk — clip-only fallback defined.
- **This project's documented failure mode is "plans outrunning shipped, verified code"** (stated
  in build plans v5 and v6). The antidote baked into this plan: the working GPU harness lands
  first, and nothing is called done without a critic round at PASS.
- Webcam/gesture is deliberately low-stakes (§5) — not a risk, by design.
