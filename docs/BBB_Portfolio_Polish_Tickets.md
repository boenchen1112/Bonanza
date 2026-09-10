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

## [ ] 10 — Swing Kings stage to "B": shadows, unified toon shading, env kit, lighting
**Blocked by:** 04, 06

## [ ] 11 — Swing Kings callouts: pool never clobbers, legible, no pip curtain
**Blocked by:** 01

## [ ] 12 — Swing Kings music verified in-engine
**Blocked by:** 03

## [ ] 13 — Critic loop: Swing Kings to PASS
**Blocked by:** 05, 08, 09, 10, 11, 12

## [ ] 14 — Swing Kings gesture mode: mouse/pointer-drag source (+ ADR 0004)
**Blocked by:** 01
- [ ] Selectable input mode; drag-down + release fires the swing; release timing feeds accuracy → power
- [ ] Keyboard hold-and-release works again in that mode; `verify.mjs` hold checks pass
- [ ] Tap mode unchanged and still harness-verified

## [ ] 15 — Swing Kings gesture mode: webcam hand-tracking source
**Blocked by:** 14, 06
- [ ] MediaPipe bundled (offline); no camera → falls back to mouse

## [ ] 16 — Shell lineups use the real cast + animator (finishes #14's chars items)
**Blocked by:** none

## [ ] 17 — No placeholder game-select path (`select.js` real or removed)
**Blocked by:** none

## [ ] 18 — Shell to "B": mocap-animated cast, consistent art, shell music verified
**Blocked by:** 03, 07, 10, 16, 17

## [ ] 19 — Critic loop: shell to PASS
**Blocked by:** 05, 08, 18

## [ ] 20 — Baseline pass: Drumline Dash — *release valve*
**Blocked by:** 04, 07, 10

## [ ] 21 — Baseline pass: Bounce Brigade — *release valve*
**Blocked by:** 04, 07, 10

## [ ] 22 — Baseline pass: Chomp Chorus (+ stuck beam) — *release valve*
**Blocked by:** 04, 07, 10

## [ ] 23 — Baseline pass: Finale Fever — *release valve*
**Blocked by:** 04, 07, 10

## [ ] 24 — Hand-back status report in the draft PR
**Blocked by:** 13, 15, 19 (deliberately **not** 20–23)
