# Beat Bash Bonanza — Portfolio Polish Tickets

Source: `docs/BBB_Portfolio_Polish_Spec.md`. Tracked here (not GitHub issues — user choice,
2026-09-11). Work the **frontier**: the lowest-numbered ticket whose blockers are all done.
Numbering carries the preferred order; "Blocked by" carries only genuine gates.

Decisions taken at approval (2026-09-11):
- Characters/animation come from **Mixamo** (user downloaded Y Bot + 8 clips as FBX into
  `<main checkout>/Mixamo/`, gitignored). Only converted, game-ready `.glb` is committed.
- Audio "verified" = machine check (non-silent, onsets on the beat grid). A human listen is a
  non-blocking follow-up in the hand-back checklist.
- Webcam gesture closes on "builds, runs offline, falls back to mouse without a camera". The
  user's webcam smoke test is a hand-back checklist item.

Status key: `[ ]` open · `[~]` in progress · `[x]` done · `[-]` cut (with reason)

---

## [x] 01 — Harness renders on the real GPU
**Blocked by:** none
**Delivers:** Swing Kings in the harness at real frame rate on the Iris Xe, post chain visible.
- [x] SwiftShader flags gated behind an explicit opt-in (`--swiftshader`)
- [x] Windows GPU-capable browser launch (system Chrome / bundled Chromium), headless where GPU survives
- [x] `QUALITY` defaults to `high` on the GPU path
- [x] Capture driven by `__BBB__.screenshotReady()`; the `SECONDS*4` padding and wall-clock sleeps gone
- [x] Summary reports GPU vs software honestly (renderer string), not a hardcoded `softwareRendered`
- [x] HANDOFF §3, critic-brief, CLAUDE.md harness notes rewritten
- [x] Draft PR into `Mario-Party` opened

## [x] 02 — Character + clips converted and in the repo
**Blocked by:** none
- [x] FBX → GLB conversion is a repeatable script (not a one-off)
- [x] Y Bot mesh + skeleton and the 8 clips land as bundled `.glb` with provenance notes
- [x] Raw FBX never committed

## [x] 03 — Harness records video + the game's audio mix, with an automatic audio check
**Blocked by:** 01
- [x] `--video` gives a usable webm (Playwright: fixed 25fps, silent — fine for review; portfolio footage via real browser/OBS later)
- [x] Run dumps a WAV of the in-page audio mix (AudioWorklet on master, context-clock stamped)
- [x] Pure, unit-tested checker: non-silent + onsets aligned to the beat grid (reported in summary)

## [x] 04 — Console-clean on every registered scene
**Blocked by:** 01
- [x] No `flatShading`-on-toon warnings anywhere (flag now set post-construction, so the intended faceting renders)
- [x] `CONTEXT_LOST_WEBGL` re-checked: never occurs on the GPU path; only under `--swiftshader` (browser GPU-process reset, no `loseContext` in source) — documented, not filtered
- [x] `consoleClean: true` on all 14 registered scenes (`node tools/harness/sweep.mjs`)

## [x] 05 — `__BBB__` out of the production build; harness builds opt in
**Blocked by:** 01
- [x] Production bundle contains no test API (verified by grepping a plain `vite build`)
- [x] Harness build mode exposes it; harness runs still green

## [x] 06 — ADR 0003 + first bundled asset renders offline
**Blocked by:** 01
- [x] ADR 0003: "no fetches outside the build" (file:// dropped — verified it never worked); ARCHITECTURE.md + CLAUDE.md updated
- [x] ADR states MediaPipe wasm/model must be bundled (no CDN); harness aborts + flags any off-origin request
- [x] Bundled Mixamo `ybot.glb` renders + animates in the built game (chars-demo tracer; Swing Kings gets it in 09) with all off-origin requests blocked

> **Re-plan of 07–09 (2026-09-11, evidence-driven).** The spec assumed the
> segmented toy rig is where the "prototype" read comes from ("limbs telescope
> and clip"). That diagnosis was made from SwiftShader-era frames. A 60fps GPU
> run of `chars-demo` through every state (`runs/t07-poses`: windup, strike,
> all four verdicts, fail) shows the toy cast holding up in its extreme poses,
> and its faces + four builds are the series' identity; the bundled Y Bot
> mannequin next to them reads as generic. So the cast is **kept**, and the
> Mixamo motion is **retargeted onto it** instead: an offline bake turns each
> clip into the animator's own pose-channel tracks, so the existing crossfade,
> damping, additive beat layer, springs, impulses and faces apply unchanged.
> This is still the spec's §4 hybrid (clip base layer + procedural additive
> layer) — minus the geometry swap. The four verdict reactions stay procedural
> (their silhouettes are the design contract; the Victory/Sitting clips don't
> meet it). Flag this in the hand-back.

## [x] 07 — Mixamo clips retargeted onto the toy rig as pose-channel tracks
**Blocked by:** 02, 06
- [x] Pure, unit-tested retarget solver: Mixamo bone directions/rotations → the rig's swing/lift/twist/bend + hips/torso/head channels (FK round-trip tests)
- [x] Deterministic bake script (`ybot.glb` → committed clip-track module), no raw FBX needed
- [x] Animator plays clip-backed states (`play(name, {from,to,dur})`) through the same crossfade/damping/beat layer; face channels still procedural
- [x] Visible in `chars-demo` (mocap source figure beside the toy cast) — runs/t07-mocap

## [x] 08 — Beat layer + face tuned on clip-driven states
**Blocked by:** 07
- [x] Per-clip beat-layer amounts and face expressions tuned (per-call `beat` + CLIP_FACE presets; dance beat-locked to its own downbeats; further tuning rides on 09/13) so mocap states stay on the beat and in character; verified in `chars-demo` frames

## [x] 09 — Swing Kings batter performs the mocap swing on the beat; bat visible
**Blocked by:** 07
- [x] Batter: batting stance when a pitch is armed, mocap coil retimed across the ball's flight (lands loaded just before contact), strike from the contact frame on the press, procedural verdict pose after the follow-through — runs/t09-c
- [x] Bat visible in every frame of the swing
- [-] Mocap pitcher: cut by design — pitches come every 2 beats in dense sections, faster than any pitching windup; the machine (built for rapid fire) stays. The pitch clip is used in chars-demo
- [x] Found + fixed on the way: tap-mode power was NaN (`pitch.time` does not exist) and judgement recomputed it from hold length, so every keyboard hit was a BUNT — perfect taps are now HOME RUNs
- Note for 14: verify.mjs hold/release checks fail because tap-mode `input()` ignores key-up (pre-existing since the tap-mode change); restoring hold-and-release is ticket 14

## [x] 10 — Swing Kings stage to "B": shadows, unified toon shading, env kit, lighting
**Blocked by:** 04, 06
- [x] Real shadows: house-wide opt-in — one PCF shadow map from the key light, cast only inside a scene-declared focus box (`look.setShadowFocus`); low tier and scenes without a focus pay nothing (verified: no shadow pass in title). Rig bodies cast (faces don't); SK batter + machine cast onto grass/dirt/mound (runs/t10-c)
- [x] Unified toon shading: everything lit goes through the house dress pass; no MeshStandard leaks in frame
- [x] Ballpark set (procedural, consistent with the house kit rather than imported CC0): mowed-stripe grass, speckled dirt cut-out + mound, merged chalk foul lines + batter's boxes (1 draw), bleacher rows on the stands, no confetti/rings
- [x] PITCH-O: the machine rebuilt as a toy character — cart, capsule body, stacked wheels, ball hopper, eyes that squint when it arms and blink when it fires
- [x] Budget: SK 96-103 draw calls (< 120), ~54fps, cpu ~3ms; all 14 scenes console-clean (runs/t10-sweep)

## [x] 11 — Swing Kings callouts: pool never clobbers, legible, no pip curtain
**Blocked by:** 01
- [x] Callout pool 3 -> 6, and a full pool recycles the word closest to done (never slot 0 mid-pop)
- [x] Tier word stacks above the verdict word instead of overprinting; legible over the crowd at 60fps (runs/t11-a)
- [x] The "pip curtain" was the wireframe backstop: its arc sat on the pitcher's side of the plate, drawing vertical lines over the batter. Now a chain-link net + rail behind the plate, closing the right edge
- [x] Bat re-seated in a fist grip (across the forearm) so the mocap follow-through doesn't read as a cane (runs/t11-b)

## [x] 12 — Swing Kings music verified in-engine
**Blocked by:** 03
- [x] Whole track (68.7s, chart-played, 45/45 perfect): audio pass, -15.8 dB RMS, peak 0.955 (no clipping), beat grid on 51% / off 0.8%, bias -4.2ms (runs/t12-full). Human listen: hand-back checklist

## [ ] 13 — Critic loop: Swing Kings to PASS
**Blocked by:** 05, 08, 09, 10, 11, 12

## [x] 14 — Swing Kings gesture mode: mouse/pointer-drag source (+ ADR 0004)
**Blocked by:** 01
- [x] Selectable input mode (Options: TAP / MOUSE CONDUCT / CAMERA CONDUCT); hold to wind up, a downstroke ictus (or release) swings; hold length = power
- [x] Keyboard/pointer hold-and-release works again; `verify.mjs` ALL PASS (first time since tap mode); `smoke-conduct.mjs` drives real mouse strokes -> PERFECT/GREAT released by the ictus
- [x] Tap mode unchanged and still harness-verified (default); ADR 0004 + CLAUDE.md updated

## [x] 15 — Swing Kings gesture mode: webcam hand-tracking source
**Blocked by:** 14, 06
- [x] MediaPipe bundled (npm wasm + committed Apache-2.0 model, lazy chunk); raise = windup, ictus = swing; no camera -> falls back to mouse with a notice; smoke: pipeline boots offline on Chromium's fake camera, console clean. Real-webcam check = hand-back checklist

## [x] 16 — Shell lineups use the real cast + animator (finishes #14's chars items)
**Blocked by:** none
- [x] Already landed before this pass (0f3ba11: construction collapsed onto makeCast(), adapter deleted; charBeat only runs the real animator). Remaining cleanup done: dead baseY/baseScale/phase fields + a stale comment removed

## [x] 17 — No placeholder game-select path (`select.js` real or removed)
**Blocked by:** none
- [x] Removed: nothing in the flow ever routed into `select` (title -> party / freeplay / options; freeplay is the real game picker). Scene, registry entry, nav fallback and stage reference gone; nav test updated first (red -> green); HANDOFF updated

## [x] 18 — Shell to "B": mocap-animated cast, consistent art, shell music verified
**Blocked by:** 03, 07, 10, 16, 17
- [x] Cast identity: 3D palettes derived from each portrait + portrait crests; fixed makeCast coercing palette objects to one orange
- [x] Mocap: title cast dances the beat-locked swing-dance; roster preview introduces itself with the taunt
- [x] Framing: roster/freeplay floors no longer slice through the cast; title cast clear of the menu; results shows a character
- [x] Freeplay cards + title attract reel use real captured game frames
- [x] Title logo bright again (text-shadow over background-clipped text bug)
- [x] Music: options mapped to the menu theme; title/roster/freeplay/options/party pass the beat-sync check; results fanfare reported no-grid (no transport)
- Note for 19: title cpu ~4-5ms/frame (budget 4) — judge in the shell critic round

## [ ] 19 — Critic loop: shell to PASS
**Blocked by:** 05, 08, 18

## [x] 20 — Baseline pass: Drumline Dash — *release valve*
**Blocked by:** 04, 07, 10
- [x] Unified shading (dress pass + flatShading fix), real shadows following the camera between call/response, cast variety (4 palettes x builds + drum major), callouts legible, console clean — runs/t20c (sweep: all clean, draws 90-118 < 120, ~55fps, audio pass on 12s runs)

## [x] 21 — Baseline pass: Bounce Brigade — *release valve*
**Blocked by:** 04, 07, 10
- [x] Same bar; shadow focus rides the platform-following camera; single bouncer is the design — runs/t20c (sweep: all clean, draws 90-118 < 120, ~55fps, audio pass on 12s runs)

## [x] 22 — Baseline pass: Chomp Chorus (+ stuck beam) — *release valve*
**Blocked by:** 04, 07, 10
- [x] Same bar — runs/t20c (sweep: all clean, draws 90-118 < 120, ~55fps, audio pass on 12s runs)
- [x] "Stuck beam": not stuck at 60fps — it is the air-pipe telegraph / voice beam of whichever lane is cued, moving between lanes (runs/t22-a); the report came from SwiftShader-era frames

## [x] 23 — Baseline pass: Finale Fever — *release valve*
**Blocked by:** 04, 07, 10
- [x] Same bar; boss + player cast shadows — runs/t20c (sweep: all clean, draws 90-118 < 120, ~55fps, audio pass on 12s runs)

## [ ] 24 — Hand-back status report in the draft PR
**Blocked by:** 13, 15, 19 (deliberately **not** 20–23)
