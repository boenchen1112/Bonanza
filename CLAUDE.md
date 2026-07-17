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

1. **v1 (current):** Python rule-based prototype. `pygame` for game
   loop/visuals/audio, `joycon-python`/`hid` for right Joy-Con gyro input.
   Flat 2D placeholder visuals — the goal is validating detection,
   calibration, and scoring, not final look.
2. **Next:** 2.5D port to Unity — 3D characters/environment on a fixed
   camera, no free player movement (Super Mario Party style). Detection/
   calibration/scoring logic ports to C# largely as-is.
3. **Long-term:** VR, converging with the existing VR chord-game project.

**Unity port readiness (confirmed post-bugfix, `Swinger_Build_Plan_v2.md`
Phase 6):** the module split needed for a "logic ports largely as-is" claim
still holds. Pure logic, no `pygame`/hardware import: `beat_schedule.py`,
`ictus_detector.py`, `scoring.py`, `session_log.py`, `calibration.py`
(file I/O only — reading/writing `calibration.json`, no rendering).
Presentation/input, `pygame`-dependent: `game.py` (render loop, audio
playback via `metronome.py`), `metronome.py` (click playback), and
`settings_menu.py` (menu rendering/keyboard nav) — these are what the Unity
port actually replaces (pygame draws → Unity scene, keyboard menu → Unity
UI). `joycon_stream.py` is the hardware input layer, replaced by Unity's
input system regardless of pygame. Nothing in the detection/scoring/
calibration path (the part validated by `tests/`) depends on pygame or on
`joycon_stream.py`'s specific device API — only on the `(timestamp,
magnitude)` sample shape those modules already consume.

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
- `src/` — game source (`joycon_stream.py`, `metronome.py`,
  `beat_schedule.py`, `ictus_detector.py`, `game.py`, `settings_menu.py`,
  `session_log.py`)
- `tests/` — validation scripts (e.g. offline ictus-detection replay/plots)
- `tools/` — one-off/dev utilities

## Definition of "v0 prototype-ready"

A person with a paired right Joy-Con runs one command, hears a metronome,
swings once per measure, and sees: a timing judgment (Perfect/Great/Good/
Miss), a sharpness tier (Bunt/Line Drive/Home Run), an out count, and an
end-of-round summary of timing offsets and sharpness — with calibration
available from Settings after the round. Sections 1–5 of the build plan must
be functional, not stubbed.
