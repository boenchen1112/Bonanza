# Swinger — All-Star Swingers

Conducting-rhythm baseball minigame. Player swings a right Joy-Con on the beat;
the game scores timing accuracy (against a metronome) and gesture sharpness
(how clean the "ictus" direction-change is), then resolves it as a baseball
hit tier (Bunt / Line Drive / Home Run).

This is one minigame in a larger Mario-Party-style set that doubles as a
conducting-technique trainer, and the first place two long-running threads
meet: the VR chord-game project (Unity) and a Joy-Con-based conducting
analysis tool.

## Platform roadmap

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

## v1 scope

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
Section 6 — explicitly deferred items).

## Folder structure

- `.claude/` — Claude Code project config
- `.git/` — version control
- `.VS/` — VS/VS Code workspace settings
- `Backup/` — manual backups
- `media/` — art, audio, and other binary assets
- `reviews/` — playtest notes, design reviews
- `src/` — capture/tuning tooling only, not a maintained product (see
  "Platform roadmap" above): `joycon_stream.py`, `joycon_udp_bridge.py`,
  `metronome.py`, `beat_schedule.py`, `ictus_detector.py`, `calibration.py`,
  `scoring.py`, `session_log.py`, plus the retired `game.py`/
  `settings_menu.py` pygame prototype (fix only what tooling needs)
- `archive/` (under `src/`) — stale/contaminated captures kept for
  provenance, not for re-deriving anything from
- `tests/` — frozen Python validation suite (offline, no hardware/pygame
  required except where noted) backing the logic modules above
- `tools/` — one-off/dev utilities (e.g. `generate_golden_traces.py`,
  which the Unity port's golden-trace parity tests consume)
- `UnitySwinger/` — the Unity project; `Assets/Scripts/Logic/` is the
  canonical, UnityEngine-free ported logic (keep in sync with `src/`'s
  logic modules); `Assets/Scripts/Presentation/` and `Assets/Scripts/
  Input/` are Unity-specific (rendering, UDP receive, calibration flow)

## Definition of "v0 prototype-ready"

A person with a paired right Joy-Con runs one command, hears a metronome,
swings once per measure, and sees: a timing judgment (Perfect/Great/Good/
Miss), a sharpness tier (Bunt/Line Drive/Home Run), an out count, and an
end-of-round summary of timing offsets and sharpness — with calibration
available from Settings after the round. Sections 1–5 of the build plan must
be functional, not stubbed.

## Agent skills

### Issue tracker

Issues live as GitHub issues in boenchen1112/Swinger (private repo). See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context — `CONTEXT.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.
