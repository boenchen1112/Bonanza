# Rhythm Game

A party rhythm game for the browser, built in Unity. Five minigames, played
alone or as a party against CPU players:

| Id | Game | Play |
| --- | --- | --- |
| `swing-kings` | Swing Kings | Time the swing to the pitch. |
| `chomp-chorus` | Chomp Chorus | Sing your lane in a four-voice row. |
| `drumline-dash` | Drumline Dash | Answer the drum call and race the field. |
| `bounce-brigade` | Bounce Brigade | Bounce from platform to platform on the beat. |
| `finale-fever` | Finale Fever | Call-and-answer finale with a rising tempo. |

All timing runs on the audio clock (`AudioSettings.dspTime`), never on frame
time, and every game plays on taps alone.

## Requirements

- Unity **6000.6.2f1** (Unity 6.6) with WebGL Build Support. The project uses URP.
- Node.js 20 or later, for the browser checks and the golden data.
- Blender 4.2 LTS, only to rebuild the character.

## Open and play

Open the project folder in Unity Hub. The only scene is
`Assets/Scenes/SwingKings.unity`, and it starts on the title screen.
The main button is Space, Enter, J, a click or a tap. Some games also use the
arrow keys (or WASD). Esc or P pauses. Gamepads work too.

## Layout

```
Assets/Game/Core/      Rules for every game and the party mode. Pure C#, no UnityEngine.
Assets/Game/Runtime/   GameHost (clock, input, count-in, pause, results), one
                       Minigame subclass per game, and the menu screens.
Assets/Tests/EditMode/ Rule tests. Golden/ holds chart dumps the C# must match.
Assets/Art/Character/  The batter: FBX with clips, textures, import settings.
Art/blender/           Blender sources of the batter.
Tools/blender/         Headless Blender scripts that build the batter (see its README).
Tools/                 Browser checks and the golden-data dumper.
Reference/web/         The original web prototype's rules, charts and music, which the
                       C# rules are tested against.
```

## Tests

Rule tests (EditMode), from the command line:

```powershell
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults Captures/editmode.xml
```

or in the Editor under Window > General > Test Runner.

The rules of four games and the party mode are pinned to golden dumps of the
web prototype. To dump them again (the result must not change):

```powershell
node Tools/golden/dump-charts.mjs
```

The prototype's own tests: `npm --prefix Reference/web test`.

## WebGL build and browser check

Build WebGL into `Builds/WebGL` (File > Build Profiles, or batch mode). Then,
once:

```powershell
npm --prefix Tools install
```

Each game must reach rank S with no misses on autoplay in a real browser:

```powershell
node Tools/webgl-check.mjs Builds/WebGL Captures/swing-kings.png "?autoplay&game=swing-kings" "[RESULT]" 200
```

It prints `game=<id> rank=S ... miss=0` and `errors: none`. Check that the
`game=` value matches; an unknown id falls back to `swing-kings`. A full party:
`LIVE=1 node Tools/webgl-check.mjs Builds/WebGL Captures/party.png "?autoplay&party=2" "[PARTY-DONE]" 900`
(about 6 minutes). Pause and resume: `node Tools/pause-check.mjs Builds/WebGL Captures/pause swing-kings`.

## Credits

See `Art/CREDITS.md`. The batter's mesh was generated with Meshy (CC BY 4.0).
