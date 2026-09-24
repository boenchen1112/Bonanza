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

## [x] 13 — Critic loop: Swing Kings to PASS — *closed at round 7 without a PASS (see loop decision under 19)*
**Blocked by:** 05, 08, 09, 10, 11, 12
- [x] Round 1 FAIL (MP 7/7), round 2 FAIL (ours won Timing + Escalation), fixes landed for both
- [x] Round 3 FAIL (6–1, ours won Escalation; gap: bat never meets the ball). Fixed: contact visuals wait for the live swing's contact frame and fire from the bat's sweet spot; the pitch aim adapts to it (verify.mjs: gap < 0.3 after the first hit, delay < 80ms); rounder/smaller beat pips; trace hidden in tap mode; earlier verdict pose; no title-card subtitle; opaque callout plaques popping from 60%; callouts age on real time
- [x] Round 4 FAIL (ours won Escalation; contact now reads "ring lands on the ball at the bat tip", Timing 8/10). Send-back: (1) grand-slam ball off-screen its whole ~4s flight (apex 7.4 vs camera lift cap 3.2) + batter still celebrating while the slam pitch flies; (2) third out never shown (lamps 2 -> 0, no callout); (3) home-run payoff leaves frame instantly, same badge every hit; (4) popped pips read as stray lights; (5) night palette milky; (6) contact freeze-frame looks like an overhead chop; (7) whiff plants bat in dirt / batter lunges with no press; (8) no finish word; (9) clutter (octahedra, big confetti on field); (10) countdown "3" + LIKE THIS! overlap
- [x] Round 4 fixes landed (slam in frame, batter squares up, STRIKE!/SIDE RETIRED!, GAME!, homer distances, night palette, floaters)
- [x] Round 5 FAIL, close (ours won Escalation; Timing 8, Readability 7). Send-back: grand-slam ring cut flat by the field. Fixed: overlay ring pool; badges off the pitch arc; homers land in the stands in frame; bat dropped for the curtain call; gold pips; bunt/foul split; misses stop at the net; NEW INNING; contact fires within half a frame
- [x] Round 6 FAIL, narrowly (Timing 8, Impact 7, Escalation 7; ours won 3 of 7). Send-back: the HOME RUN!/GRAND SLAM! badge rose under the OUTS pill. Also: NEW INNING beside a lit lamp, LIKE THIS! overlapping the "3", see-through badge plates, lumpy finale/combo rings. All fixed in 6009ff1 (badges clamp below the HUD band in screen space). Left as known gaps (design, not bugs): the pitch arcs through the crowd band, the strike heap hides the face, the set edge beyond the stands and the skyline are blockout, the night switch reads as a grade, hits look alike
- [x] Round 7 FAIL (Impact 8, Escalation 8, Timing 8 even; the R6 badge fix held). Send-back: in the two-a-bar section the incoming pitch lost its beat dots — resolving pitch N cleared the dots pitch N+1 had just laid. Fixed in f304bf9 (dots owned by their pitch), with homers moved centre-right so they no longer cross the incoming lob, and the results score now equal to the HUD score. Verified on runs/r9-sk-late, verify.mjs ALL PASS
- **Known gaps at hand-back** (not re-reviewed): ~~flat wedge at the left edge of the set and blockout skyline~~ (fixed, ticket 27); ~~the title card's beige text over the crowd~~ (fixed, ticket 33); home-run badges repeat (only the distance varies); the pitch arcs through the busy crowd band; the strike heap hides the face; the night switch reads as a colour grade; the pitching machine's face never changes; ~~faceless crowd~~ (fixed, commit `2b35e27`, predates this session)

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

## [x] 19 — Critic loop: shell to PASS — *closed at round 3 without a PASS (see loop decision below)*
**Blocked by:** 05, 08, 18
- [x] Round 1 FAIL (MP 7/7; gap: menu music died on every transition). Fixed: player re-anchors on transport restart (player.test.mjs); party has CPUs, points, standings hub, podium + winner, finale last; results/party card transforms; options reset in-row + keeps settings; roster stamp/legend/hints/see-through; freeplay side cards; menu ping + confetti at the item; portraits rendered from the 3D rig; dance no longer flips face-down (YXZ hips, re-baked, wrap-aware blends)
- [ ] Open from round 1: title cpu p95 ~13ms (steady state; not DOM, env, chars or post — suspect per-beat work), attract reel is still stills, ~~crowd faceless (art note)~~ (fixed, ticket 32)
- [x] Round 2 FAIL (ours won Timing). Send-back: the lineup never exists in play (picked character, rival CPUs), CPU standings are rolls, ties shown as ranks. Also: results accuracy contradicts stats (Drumline), READY! clips slot text, 2nd human cursor on a taken character, no results input lockout, no party->game wipe, load hitches, fonts
- [x] Round 2 fixes (6009ff1): ctx.players carries the lineup into every game (hero palette/build/crest; Drumline's rival lanes are the roster CPUs and the party scores that race's real places); competition ranking with shared places and co-champions; one human seat; results accuracy = hit quality; scene warm-up before the clock starts + title card over the swap
- [x] Round 3 FAIL (ours won Timing; the lineup pass-through was seen working). Send-back: results → standings don't pay off (static card, same sting for D as B, rivals never shown), plus carousel placeholder thumbnails, truncated seat plates, unexplained tie places. Fixed in f304bf9: results reveal in order (count-up, staggered stats, rank stamp with a per-rank sting, every player's round placing), carousel repaints on decode, READY! on the portrait, wins shown on points ties, Drumline races only the lineup, centred game title card, title stats plate/plural
- **Known gaps at hand-back** (not re-reviewed): 3 of 4 party rounds are solo games whose CPU results are simulated (disclosed on the recap); roster menu music fails the beat-grid check (suspect: each shell scene restarts the transport at beat 0 — not investigated); countdown lands on the Drumline intro title and on the fading PAUSED panel; ~~bloom bleaches white heads/limbs~~; ~~TUFF lies flat and small cast figures float on roster lock-in~~; ~~results frame is empty below the card~~; Drumline taglines differ across attract/carousel/intro; attract stills washed out; title p95 cpu spikes (~12ms)
  - **Fixed 2026-09-13** (visual-polish pass, bloom+floor+pose): bloom bleaching (Swing Kings' key light is stronger/steeper than baseline and nothing compensated the bloom threshold for it — raised `bloomThreshold` for `swing-kings`/`swing-kings-night`, verified via before/after screenshots); results empty floor (groundY copied from title's void preset, which has its own separate deck the character never needed — results' own character stands directly on the env's ground piece, so groundY had to match its stand height; fixed, verified); roster lock-in pose (`makeCast()` never ticked a fresh animator before handing it back, so a new character would visibly settle from its zero rest pose toward "idle" over several frames — the portrait-bust code already worked around this per-character; the fix now runs at the shared construction point every caller goes through). Two screenshot repro attempts for the pose bug (free-play and full party lineup) both showed already-correct figures, so that one ships on the pose-math evidence, not a caught before/after — flagging in case it resurfaces.

**Loop decision (2026-09-11):** the blind critic always finds a next gap against a first-party bar, so "loop until PASS" has no fixed point. Plan: Swing Kings round 6 and shell round 3 are the last rounds; whatever they still find goes into ticket 24 as known gaps. Scope for the shell: the minigames are single-player by design, so the party is presented honestly as one player vs CPU rivals (no 2nd-human slots, CPU columns marked) rather than building 4-player minigames. Cut, with reason: Free Play thumbnails before focus, shell/game font unification, title cpu p95 and scene-load hitches (no bounded fix found; see verify cold-run note), bloom on the roster preview, MIMO's build.

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

## [x] 24 — Hand-back status report in the draft PR
**Blocked by:** 13, 15, 19 (deliberately **not** 20–23)
- [x] PR #22 body rewritten as the hand-back: every ticket done/partial/cut, both critic loops' final scores and known gaps, and the human checklist (listen to the captured WAVs, real-webcam test, confirm the toy-rig + retargeted-mocap direction)

---

## [x] 25 — Cross-player leaderboard backend (Cloudflare Worker + D1), ADR 0005
**Blocked by:** none — orthogonal to the visual pass
- [x] Scoped overnight, 2026-09-24: no multiplayer (latency), no runtime AI (dev-time-only,
  per `docs/agents/asset-pipeline.md`), Worker exists solely so players see each other's scores;
  everything else must keep working with it unreachable.
- [x] `worker/` (Worker: `GET /scores`, `POST /score`, D1 schema, CORS via `ALLOWED_ORIGIN`),
  real D1 database created and schema applied (`database_id` in `worker/wrangler.toml` is real,
  not a placeholder). `ALLOWED_ORIGIN` is still `"*"` — needs the real GitHub Pages origin once
  Pages is live (morning list).
- [x] `web/src/net/` (`leaderboardClient.js`, canned `mockLeaderboardClient.js`, `index.js`
  switch on `VITE_MOCK_NETWORK`). `tools/harness/inspect.mjs` now builds with
  `VITE_MOCK_NETWORK=1` unless `--live-network` is passed — smoke-tested clean
  (`runs/envprops-smoke`: `consoleClean: true`, no `[external-fetch]`).
- [x] `docs/adr/0005-leaderboard-is-the-one-sanctioned-network-call.md` — the one exception to
  ADR 0003, confined to `load()`/`result()`, fails soft. `web/ARCHITECTURE.md` rule 3 cross-refs
  it. `docs/agents/backend-brief.md` documents the Worker/client conventions.
- Not done, deliberately: nothing in `web/src/` calls `submitScore`/`fetchTopScores` yet — see 26.
- Verified deployed and reachable from a browser is still open: `wrangler deploy` succeeded but
  the `*.workers.dev` URL had not resolved yet as of this writing (subdomain/DNS propagation);
  morning list.

## [ ] 26 — Wire the leaderboard into results.js
**Blocked by:** 25
- Call `submitScore`/`fetchTopScores` (`web/src/net/index.js`) from `results.js`'s `load()`,
  next to the existing `profile.submit()` call — the one other place a round already becomes a
  permanent record (ADR 0005 confines network calls to `load()`/`result()`).
- Needs a player-name decision first (none exists today — party lineup names like "BOPP" are
  local seat labels, not identities to put on a shared leaderboard): where a name is entered,
  how it persists, and what shows against CPU/party rounds where "the player" isn't well-defined.
  That's a UI/UX call, not one to guess overnight — flagged for Laya / the user.
- Display: a compact top-scores strip on the results card (fits the existing reveal timeline
  around `STAMP_AT`/`PARTY_AT`) — exact placement needs the same design pass results.js already
  went through in tickets 18-19, not a blind insert.

## [x] 27 — Swing Kings: authored skyline + set edge (round-6 known gap, not accepted as final)
**Blocked by:** none (Swing Kings only — `world.js:63` already passes its own backdrop config,
so `render/env/backdrop.js`'s shared defaults for every other scene stay untouched)
- Gap this closes (critic round 6, `docs/BBB_Portfolio_Polish_Tickets.md` ticket 19): "the set
  edge beyond the stands and the skyline are blockout" — logged as a design gap when the loop
  was deliberately capped at round 7, not accepted as finished.
- Scope: a Blender-authored skyline silhouette + set-edge piece for Swing Kings' backdrop only.
  `render/env/backdrop.js`'s uniform-box skyline stays as the shared default for every scene that
  doesn't opt out of it.
- Owned files: `tools/assets/blender/build-sk-set.py` (new), `web/src/assets/env/` (new, +
  provenance README matching `chars/README.md`'s table), `web/src/games/swingKings/world.js`
  (pass `skyline: 0` to `makeBackdrop`, add the authored piece).
- Gates: `render.drawCalls` < 120 (baseline before this ticket: 102, `runs/envprops-smoke`),
  budget CPU on the 2019-iGPU floor, `consoleClean`, `swingKings/verify.mjs` ALL PASS.
- [x] Baseline recorded: `runs/envprops-smoke` — drawCalls 102, gpu ANGLE D3D11 (not software).
- [x] **Round 1 PASS** (fresh critic, `runs/sk27-critic` day + `runs/sk27-critic-night`, blind to
  the builder's report): "the skyline reads as a real city now... not blockout any more"; night
  re-tint lands clean between frames; set edge, parapet, outfield wall and tunnel mouth all read
  finished; nothing floats or clips; no HUD collisions. drawCalls 101 day / 99 night (was 102,
  net cost ~0 draw calls, +7k triangles), consoleClean, real GPU, `verify.mjs` ALL PASS, 165/165
  unit tests. Nit (not a send-back): the near stand-end block reads as stacked boxes from the
  main camera; its tunnel/lintel detail only shows in wide shots — optional follow-up, not required
  at the "B" bar. Closed at round 1 (spec's DoD needs ≥1 PASS round; got it on the first).
- **Found by the same critic run, NOT this ticket's regression — logged here, own ticket needed:**
  Drumline Dash now measures 125 draw calls (over the 120 budget; ticket 20 recorded 90–118).
  Nothing outside `swingKings/` imports the new asset/loader. Console clean, ~55fps. See ticket 28.
- **Also found, pre-existing:** night-run audio check `pass: false` (49.8% onsets on-grid, same
  borderline figure ticket 12 already recorded machine-wide at 51%) — not caused by this ticket.

## [x] 28 — Drumline Dash: draw-call regression over budget (125 > 120)
**Blocked by:** none
- Found incidentally by ticket 27's critic pass (`runs/sk27-critic-drumline`,
  `runs/sk27-critic-drumline12`), not caused by it — nothing outside `swingKings/` imports
  ticket 27's new asset or loader (checked: two unrelated code comments only).
- Ticket 20 recorded 90–118 draw calls for Drumline Dash; now measuring 125–127 in repeat runs.
  Console stays clean, ~55fps — this is a budget regression, not a crash/visual bug.
- [x] **Cause found: no code regression, a measurement gap.** A build of 6c22caf (Drumline's
  original baseline pass) gives the same draw-call histogram as this branch's HEAD — ticket 20's
  118 was a lucky frame. The scene's real full-round peak was always 131–133 on a busy verdict
  frame: `ParticlePool`'s transparent `DoubleSide` material drew twice per live family (no
  `forceSinglePass`, up to 9 extra draws), risers cost one draw per tier, the track cost one per
  stripe family + one per yard line, the finish gate cost ~30 draws for separate pieces, five
  rigs cost one draw each for their contact-blob shadow.
- [x] **Fixed by merging, nothing removed:** `render/fx/pool.js` (`forceSinglePass`, shared),
  `render/env/crowd.js` (risers merged to one geometry, shared), `chars/rig.js`
  (`batchBlobShadows()`, new, opt-in — only Drumline calls it, nothing else changes),
  `games/drumlineDash/props.js` (track + finish gate merged), `index.js` (wires it in).
- [x] Verified: harness snapshot 127 → 109; full-round peak (every frame, ~75s, auto/perfect/
  sloppy) 131–133 → 117. Regression check on the shared changes: Swing Kings 92–111 → 92–104,
  Bounce Brigade 36–54 → 34–44, Finale Fever 78–98 → 76–88, all clean. Chomp Chorus (109–127,
  already over budget before this ticket, unrelated) is now 107–117 — fixed as a side effect.
  165/165 unit tests, `swingKings/verify.mjs` ALL PASS.
- **Found by the same fix pass, NOT fixed here — see ticket 29:** the harness gate measures a
  direct scene launch; the real player path (Free Play → Play) still measures Drumline Dash at
  144–150 draw calls even after this fix, and likely affects every game launched that way, not
  just Drumline. Also found, unrelated: Drumline's party-round placings are silently dropped.

## [x] 29 — Free Play → Play launches a second environment; Drumline's party placings drop
**Blocked by:** none — found incidentally by ticket 28, more player-facing than either 27 or 28
since it's on the actual path players use to reach any minigame, not a direct scene launch
- [x] **Double env build, fixed at the source.** Cause: `render/stage.js`'s per-frame `render()`
  called `ensureDefaultEnv()` on every frame — a leftover from before `warm()` existed.
  `main.js`'s `activateNow` already awaits `stage.warm()`, which calls `ensureDefaultEnv()` once
  after `load()` has said what the scene wants; the per-frame call only ever mattered on frames
  drawn *during* an async `load()` (e.g. `play.js` awaiting the chosen game's module), where it
  built a default `'arena'` under the game's own real set and nothing ever tore the extra one
  down. Fix: removed the per-frame call; `warm()`'s single call covers every real case. Verified
  with a probe that drives the actual Free Play → Play route (not a direct scene launch) for all
  5 minigames: env roots 2 → 1 every time; draw calls (that path) Drumline 133–144 → 117–128,
  Chomp Chorus 124–133 → 108–117, Swing Kings 94–102 → 78–86, Finale Fever 96–106 → 80–90, Bounce
  Brigade 53–63 → 38–47. Direct scene launches (every ticket 01–28 gate) measure unchanged.
- [x] **Drumline's dropped party placings, fixed.** `drumlineDash/index.js` numbered places from
  1 but `core/result.js`'s `checkField` requires 0-based places, silently dropping the field
  (console warning, party simulates the round) on every Drumline party round. Fixed at the one
  call site building `field` for `result.js` (`r.place - 1`); the 1-based `r.place` is untouched
  everywhere it's actually displayed (banner, HUD, standings). Verified as a real party round via
  the same probe: `sim:false`, real race order, no console warning. 165/165 unit tests.
- **Still open, own ticket (30):** Drumline via Free Play → Play still measures 117–128 (median
  122, still over 120) even after the env fix — the remaining ~11 draws are character-crest
  meshes (shadow pass + main pass) on the lineup's toy rigs, outside `render/stage.js`'s and
  `drumlineDash/`'s scope.
- **Noted, not a regression from this ticket:** `swingKings/verify.mjs`'s "ball on the bat" check
  was flaky under this machine's load during this session (borderline contact-gap timing failed
  on both the pre- and post-fix build, roughly half the runs) — Swing Kings already sets
  `ctx.scene.userData.env = false`, so the removed call was a no-op there either way. 5/5 clean
  re-runs with no other agents running at the same time point at system-load jitter rather than a
  real regression, but flagging since ticket 27 was closed on a critic PASS using this same check.
  Worth a quiet re-run before fully trusting ticket 27's PASS if anyone doubts it later.
- Scratch probes from the discovery + fix passes: `runs/t28-probe/`, `runs/t29-probe/` (gitignored).

## [x] 30 — Drumline via Free Play → Play still ~10 draw calls over budget (character crests)
**Blocked by:** none
- After ticket 29's env fix, Drumline Dash measured through the real Free Play → Play path was
  117–128 (median 122), still over the 120 ceiling half the time. Ticket 28 already got the
  direct-launch number comfortably under budget (109); this was specifically the crest cost that
  only shows up with a full lineup of characters, which a direct scene launch doesn't build.
- Cause: each crest piece (TUFF's horns 2 pieces, ZIZZ's bolt 1, MIMO's cap 2, NIBB's antenna 1)
  was its own `Mesh` with its own shadow draw — one draw in the main pass + one in the shadow pass
  per piece, scaling with lineup size.
- Fix: `shell/chars.js`'s `addCrest` now bakes each crest's pieces into one cached geometry
  (`mergeGeometries`), so a crest is one `Mesh` (tagged `userData.crest`) regardless of piece
  count. `chars/rig.js` gained `batchCrests(chars)`, opt-in like ticket 28's `batchBlobShadows` —
  combines a whole lineup's crests into one `SkinnedMesh` (one bone per crest, riding the same
  joint the original crest sat on; colour baked to vertex colours; a back-facing copy for the one
  double-sided crest, `fin`), so the batch draws once in the main pass + once in the shadow pass no
  matter how many characters or crest pieces are in the lineup. Original crest meshes are hidden,
  not removed — `dispose()` restores them. `drumlineDash/index.js` wires it in alongside the
  existing blob-shadow batch.
- Verified (independently, not just builder-reported): `npm test` 165/165. Direct harness launch
  (`--scene drumline-dash --play auto`) — 113 draw calls, real GPU (Iris Xe/D3D11), console clean.
  Free Play → Play, default lineup (tuff/zizz/mimo/nibb): 117–128 → 107–118. Free Play → Play,
  crest-heavy lineup (glub/kwark/fizz/bopp): 106–117 — confirms the batch cost doesn't scale with
  crest piece count, which was the whole point. Close-up screenshots of all 4 racers' heads in
  BOTH lineups (including GLUB's `fin` — the one double-sided crest, the riskiest part of the
  batch), taken mid-autoplay so the rig is actively animating, confirm crests sit correctly with
  no displacement, no flipped normals, no missing pieces — this is the real evidence for "moves
  with the rig", not the bone/crest `matrixWorld` diff also run at the time. **That diff check is
  circular and was wrongly cited as proof here**: the batch bone is parented to the same object the
  original crest is (`e.crest.parent.add(bone)`) with a local transform copied from it once at
  construction, so `bone.matrixWorld` is *structurally* identical to `crest.matrixWorld` no matter
  how the joint animates or whether the `SkinnedMesh` renders correctly off those bones — it can
  never read nonzero, so it proves nothing about motion. Bounce Brigade (that game doesn't call
  `batchCrests`) checked via the same Free Play → Play probe and confirmed via its own
  `crestCheck`: `visibleCrests: 1`, a real, un-batched, un-hidden `addCrest` mesh actually
  rendering — genuine evidence the merge didn't break that path. **The title/roster/Finale Fever
  screenshots do NOT verify `addCrest`'s wider blast radius, despite what this entry originally claimed**:
  re-checked directly and title + roster render Blender bodies for every character shown
  (`userData.isBlenderBody: true` for bopp/zizz/tuff/fizz on title, bopp on roster) — Blender
  bodies skip `addCrest` entirely (crest baked in at Blender build time), so those screenshots
  were never exercising this ticket's change. Finale Fever's scene showed no `userData.crest`
  meshes at all at the point checked — inconclusive, not confirmed either way. The real,
  confirmed-toy-rig coverage for this ticket is Drumline Dash (both lineups, via `batchCrests`,
  `crestCheck` found real hidden crests + bones every time) and Bounce Brigade (one real, visible,
  un-batched crest). Menu portraits (`drawPortrait`) are a separate 2D canvas path (`chars.js`'s
  own `crest()` function, line ~515) and never touch `addCrest`, so they were never at risk.
  Checked the new merged cache keys (`horn:${b.id}`, `cap:${b.id}`, `plume:${b.id}`) for a
  load-order race with async Blender-body loading: `addCrest` is only ever called when
  `!obj.userData.isBlenderBody`, and `b` is the toy rig's static build-shape table, unaffected by
  Blender loading state — same keying discipline the pre-existing `ant:${b.id}` key already relied
  on, so no new risk (this part holds regardless of the screenshot mix-up above). `git show`'d the
  `drumlineDash/index.js` diff after the fact to confirm it was reviewed, not just the other three
  files.
- Note, corrected after ticket 31 fixed a gate bug (see 31): 118 was measured over only a 10s
  window. Re-run over a full ~80s round (default lineup) after ticket 31's fix reads **119/120** —
  worse than first measured, essentially no margin left. Re-checked across two more lineups on the
  same fixed gate to make sure one lineup wasn't a fluke: GLUB as hero with bopp/zizz/tuff as
  rivals (GLUB carries the one double-sided crest, the riskiest case) reads **117/120**; a third
  mix (mimo hero, nibb/fizz/glub rivals) also reads **117/120**. All three lineups stay under
  budget, with a peak seen so far of 119. This ticket stays closed, but there is at most 1 draw
  call of headroom over a real full round, and nothing else should be added to Drumline Dash's
  scene without another merge pass first.

## [x] 31 — Promote the Free Play → Play draw-call/env-root check into a tracked tool
**Blocked by:** none
- Tickets 29 and 30 were both found by a scratch, gitignored probe (`runs/t29-probe/`,
  `runs/t30-probe/`) that boots through the real `goto('freeplay')` → `goto('play', {from:
  'freeplay'})` route instead of `inspect.mjs`'s direct scene launch. Every prior critic round's
  draw-call/env gating used the direct launch, which never exercises the async `load()`-vs-render()
  window a real nav takes — that's why both bugs went unnoticed for 28+ tickets. Without promoting
  this into something tracked, the next visual ticket's critic measures the wrong path again.
- `tools/harness/lineup-probe.mjs` already existed, already tracked, and already boots through
  this exact route (`--race` was already using it to verify ticket 29's party-placings fix) — it
  just wasn't reporting draw calls or env-root count, only load-hitch timing. Extended it instead
  of writing a new duplicate script: each game's report row now includes a ~2s-sampled draw-call
  `min/p50/max` (sampled, not a single snapshot — ticket 28's history is exactly a peak hiding on
  an unlucky frame) and an `envRoots` count with a console warning if it's ever >1.
- Verified: ran it against the ticket-30 build (`--dist dist-t30 --hero tuff --rivals
  zizz,mimo,nibb --games drumline-dash,swing-kings`) — reports `envRoots: 1` for both and draw
  ranges matching the numbers already independently confirmed for ticket 30
  (drumline-dash 104–113, swing-kings 79–84).
- **Bug found and fixed after closing (see ticket 30's corrected note)**: the draw-call sampling
  above never actually drove gameplay — no `autoplay()` call meant it sampled idle/lead-in frames
  only, missing the busy VERDICT/combo frames that push a scene toward budget. Silently under-read
  Drumline Dash's peak by ~5 (113 vs. the real 118+). Fixed in `6049d99`: samples now run under
  real `autoplay({chart: true})` for `--drawsecs` (default 10s) and stop it after. Also added
  `--budget` and a non-zero exit code on any violation so this gates instead of relying on someone
  reading warnings. Caveat: the env-root check runs right after the draw sample, so at a
  `--drawsecs` longer than a round it can end up inspecting the results scene instead of the
  game's — use the short default for env-root correctness, a longer value only for the draw-call
  budget.
- Re-verified with the fix on all 5 games (fresh HEAD build, `dist-verify-full`): short pass
  (`--drawsecs 10`, correct env-root timing) — all 5 report `envRoots: 1`, max draws
  swing-kings 86, drumline-dash 118, bounce-brigade 45, finale-fever 89, chomp-chorus 116, all
  under the 120 budget, exit 0. Long pass (`--drawsecs 80`, full-round draw-call peak) — max draws
  swing-kings 90, drumline-dash 118, bounce-brigade 46, finale-fever 89, chomp-chorus 117, still
  all under budget, exit 0. Nothing here was over budget before the fix either — the fix mattered
  for Drumline Dash's margin specifically, not for catching a hidden failure in the other 4.
- Scratch probes (`runs/t28-probe/`, `runs/t29-probe/`, `runs/t30-probe/`, gitignored) are now
  superseded for this specific check but left in place — they still hold the ticket-30-specific
  close-up/crest-motion checks that aren't general enough to promote.

## [x] 32 — Faceless crowd (known gap, tickets 13 + 19)
**Blocked by:** none
- Named as a known gap in both Swing Kings' round-6/7 hand-back (ticket 13) and the shell's
  hand-back (ticket 19) — the audience in `render/env/crowd.js` is 132 instanced blobs
  (`SphereGeometry` scaled into a teardrop, one `InstancedMesh`, one draw call for the whole
  audience) with zero facial detail, unlike every named cast character. Picking this one from the
  known-gaps list (the same pattern ticket 27 used for the skyline/set-edge gap) since it's
  cross-cutting (every scene with a crowd), purely visual, and matches the overnight mandate
  ("redesign the whole visual of Bonanza").
- **Ticket 13's copy of this gap turned out to already be stale**: Swing Kings uses its own,
  separate `chars/crowd.js`, which got eyes in an earlier commit (2b35e27, 2026-09-17) — after
  ticket 13's hand-back note was written but before tonight. Only `render/env/crowd.js` (ticket
  19's copy — the shared arena crowd used by Finale Fever, Drumline Dash, Chomp Chorus, Bounce
  Brigade, and the shell screens on the default set: freeplay, options, roster) still lacked eyes.
- Fix: `crowdFigureGeometry()` merges the body sphere with two small flattened eye lenses into one
  geometry, so the crowd stays one `InstancedMesh`/one draw call. Vertex colours (white body,
  near-black eyes) multiply with the existing per-instance tint (`setColorAt`) automatically —
  `toon()` (`render/look/materials.js`) already supports `vertexColors`, no shader patch needed.
  Each eye's vertices are reassigned the body's surface normal at that point so the eye shades
  like a decal (the toon terminator doesn't cut through it, no rim-light artifact). `update()`'s
  bounce/hop/lean logic untouched.
- Verified independently: `npm test` 165/165. `lineup-probe.mjs` (extended in ticket 31) through
  the real Free Play → Play route, before vs. a fresh after-build — draw-call ranges identical
  (drumline-dash 107–113, bounce-brigade 36–40, finale-fever 80–84, chomp-chorus 108–112), single
  env root throughout. Screenshots across all four games plus the shell options screen show
  clearly legible eyes at normal resolution and at the user's real DPR-2 display scaling, with no
  toon-shading banding artifacts. Instances never yaw (pre-existing, unchanged), so every face
  looks toward the same side of the set rather than at the seat's own sightline — reads as a
  stylised choice at this distance/scale, not a visible bug, and the ticket explicitly ruled out
  touching `update()`'s rotation to fix it.

## [x] 33 — Swing Kings title card: beige text over the crowd (known gap, ticket 13)
**Blocked by:** none
- Named as a known gap in Swing Kings' round-6/7 hand-back: the opening "SWING KINGS" title-card
  banner is hard to read over the crowd behind it.
- `web/src/games/swingKings/index.js:268` was the only title-card banner in the whole codebase
  passing an explicit `color: '#ffe58a'` (a warm beige) — every other game's title banner
  (`DRUMLINE DASH`, `BOUNCE BRIGADE`, `FINALE FEVER`, Chomp Chorus's own name) calls `ui.banner()`
  with no `color`, which defaults to white (`ui/index.js:136`). Beige is close in luminance to the
  crowd's warm stadium-light tones behind it (`render/env/crowd.js`), which is exactly the kind of
  case the outline/shadow the glyph renderer already applies (`ui/font.js`) isn't enough for on its
  own — the same failure mode already fixed once for the in-world "HOME RUN!" badge
  (`swingKings/world.js:1089`) by giving it an opaque backing plate instead of relying on outline
  alone.
- Fix: drop the explicit beige, let the title-card banner fall back to the shared default white,
  matching every other game. Scoped to just that one call (not `NO CAMERA` or `GAME!`, which also
  passed the same beige but aren't the title card and weren't named in the known gap).
- Verified: a direct scene launch (`?scene=swing-kings`) never shows the banner in a screenshot —
  it fires during `start()`, before the harness's own `ready` flag resolves, so a screenshot taken
  after `ready` always misses it (the same direct-launch-vs-real-route gap ticket 29/30/31 already
  found elsewhere). Went through the real Free Play → Play route instead
  (`goto('freeplay')` → `goto('play', {game:'swing-kings', from:'freeplay'})`, 150ms after) and
  screenshotted the card live: "SWING KINGS" now reads in solid white with a clean dark outline
  over the busy multicoloured crowd, clearly legible — the old beige would have sat close to the
  crowd's warm stadium-light tones right behind it. `npm test` still 165/165 after the change.

## Morning list (2026-09-24 overnight session — items needing a human, not re-derivable from code)
- **The Laya moderation daemon** (`C:\Users\user\.claude\laya-moderation\daemon.py`, PID 27404 as
  of tonight) grew to ~24.5 GB private memory mid-session, drove free system RAM down to ~2.7 GB,
  and caused real tool failures (Bash/node fork-OOM, a full session crash+restart). Claude Code's
  own permission classifier denied every attempt this session to inspect or kill it (twice, under
  two different reasons), so it was never touched directly. After the crash, a permitted `tasklist`
  check showed only ~1.96 GB *working set* for that PID — a different metric than the ~24.5 GB
  *private/committed* figure that actually spiked, which was never re-measured (the classifier
  denied the memory-detail check needed to get it), so **this is not confirmed resolved** — treat
  the working-set number as reassuring but not proof the private-memory growth stopped or reversed.
  Worth checking on directly and possibly restarting it manually before running anything else
  memory-heavy (Blender, concurrent Opus builders + Chromium) tonight's way again.
- **The repo has moved**: pushes to `origin` (`github.com/boenchen1112/Swinger.git`) succeeded
  tonight via GitHub's redirect, but the canonical remote is now `github.com/boenchen1112/Bonanza`.
  Nobody ran `git remote set-url` (out of scope for an autonomous session). `CLAUDE.md` and
  `docs/agents/issue-tracker.md` still name `boenchen1112/Swinger` — worth updating once the move
  is confirmed intentional and settled.
- `ALLOWED_ORIGIN` in `worker/wrangler.toml` is still the `"*"` placeholder (ticket 25); needs the
  real GitHub Pages origin once Pages is live.
- `*.workers.dev` reachability was never reconfirmed from a browser after the original
  OOM-interrupted check (ticket 25).
- Ticket 26 (wiring the leaderboard into `results.js`) stays open — needs a player-name UX
  decision (party lineup names like "BOPP" are local seat labels, not shareable identities) that's
  a real product call, not something to guess overnight.
- Drumline Dash has only ~1 draw call of headroom left under the 120 ceiling across the lineups
  checked (ticket 30, re-confirmed under ticket 31's fixed gate) — anything else added to that
  scene will likely need another merge pass first.
- `docs/HANDOFF.md` §2's critic-round-count section is stale (says rounds are "running" when the
  tickets file shows those loops were deliberately closed) — a five-minute fix whenever it fits.
