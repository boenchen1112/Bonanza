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

## [ ] 03 — Harness records video + the game's audio mix, with an automatic audio check
**Blocked by:** 01
- [ ] `--video` gives a usable real-rate webm
- [ ] Run dumps a WAV of the in-page audio mix
- [ ] Pure, unit-tested checker: non-silent + onsets aligned to the beat grid (reported in summary)

## [ ] 04 — Console-clean on every registered scene
**Blocked by:** 01
- [ ] No `flatShading`-on-toon warnings anywhere
- [ ] `CONTEXT_LOST_WEBGL` re-checked on the GPU path; fixed or filtered with a reason
- [ ] `consoleClean: true` on every scene

## [ ] 05 — `__BBB__` out of the production build; harness builds opt in
**Blocked by:** 01
- [ ] Production bundle contains no test API
- [ ] Harness build mode exposes it; harness runs still green

## [ ] 06 — ADR 0003 + first bundled asset renders offline
**Blocked by:** 01
- [ ] ADR 0003: "no external asset fetches **at runtime**"; ARCHITECTURE.md + CLAUDE.md updated
- [ ] ADR states MediaPipe wasm/model must be bundled (no CDN)
- [ ] One bundled CC0/Mixamo `.glb` renders in Swing Kings from the built game, no network

## [ ] 07 — Skinned character behind the existing character contract (clip-only)
**Blocked by:** 02, 06
- [ ] `makeCharacter` keeps its contract (`.joints/.dims/.palette/.build/.attach/.dispose`); `attach('handR')` resolves to the hand bone
- [ ] Toon material + inverted-hull outline wired for skinned meshes; palettes tint per player
- [ ] Animator states crossfade Mixamo clips on the beat; visible in `chars-demo`

## [ ] 08 — Additive beat-phase layer on skinned bones (or documented fallback)
**Blocked by:** 07
- [ ] Anticipation / overshoot / squash / secondary motion reads on the skinned rig in `chars-demo`, OR a written, evidenced clip-only fallback decision

## [ ] 09 — Swing Kings cast is skinned; bat visible in the batter's hand
**Blocked by:** 07

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
- [ ] Tap mode unchanged and still harness-verified

## [ ] 15 — Swing Kings gesture mode: webcam hand-tracking source
**Blocked by:** 14, 06
- [ ] MediaPipe bundled (offline); no camera → falls back to mouse

## [ ] 16 — Shell lineups use the real cast + animator (finishes #14's chars items)
**Blocked by:** none

## [ ] 17 — No placeholder game-select path (`select.js` real or removed)
**Blocked by:** none

## [ ] 18 — Shell to "B": skinned cast, consistent art, shell music verified
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
