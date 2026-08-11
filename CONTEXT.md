# Swinger repo — domain glossary

This repo holds two independently-developed products (Swinger, a
conducting-rhythm baseball minigame; Beat Bash Bonanza, a browser
rhythm-party game series) that share a repo but not a codebase — see
`CLAUDE.md`. Terms below are grouped by which product they belong to.
Baton Brawl is the one deliberate bridge between them.

## Language

### Shared / cross-product

**Ictus**:
The scored instant of a conducting swing: the sharp deceleration
immediately *after* the gyro-magnitude peak — not the peak itself. This is
the direction-change event a conductor's downbeat reads as, and every
timing measurement in this repo (Python `ictus_detector.py`, the Unity
port, and Baton Brawl's WebHID port) is anchored to it.
_Avoid_: peak, apex, swing top

### Beat Bash Bonanza (`web/`)

**Baton Brawl**:
The sixth Beat Bash Bonanza minigame (`id: 'baton-brawl'`). A boss-battle
conducting game: the player fights three monsters using a real Joy-Con,
read directly in-browser via the WebHID API (no Python bridge, no Unity).
It is the only Beat Bash Bonanza game with hardware gyro input — every
other game stays taps-only. Scoring is timing-only: whether each swing's
**ictus** lands on the beat, judged through the same `core/judge.js`
`NoteJudge` every other minigame uses (a `'swing'` action feeding the same
verdict/score/combo/rank pipeline). Gesture *shape* is deliberately never
scored — camera-free shape capture was tried and found unworkable in
`research/hand_tracking_web/` (see `reviews/Bug_Audit_2026-07-30_hand_tracking_web.md`),
so shape is used only as each monster's visual identity, not as a scored
dimension.
_Avoid_: Bunzano, bandana game (voice-transcription errors from the design
conversation, not real names)

**Monster** (Baton Brawl):
One of the three bosses the player fights in a Baton Brawl playthrough,
each keyed to one of the three established conducting time signatures —
line ↔ 2/4, triangle ↔ 3/4, square ↔ 4/4 — and appearing in a random
order per playthrough (seeded via `makeRng`, never `Math.random()`, per
this repo's determinism rule). Before conducting, the player picks which
signature they believe the shape calls for via the normal tap menu
controls (a verifiable button press, unlike the swing itself) — the fight
is always judged against the monster's *real* signature regardless of
that pick, so a wrong guess has no bespoke penalty; it just makes the
player's own swings more likely to land off the real beat chart, which
the ordinary `NoteJudge` verdicts already punish. One measure (i.e. one
full cycle of that signature's beat count) defeats a monster.
_Avoid_: shape-matching, gesture scoring (Baton Brawl never does this —
see the `Baton Brawl` entry)
