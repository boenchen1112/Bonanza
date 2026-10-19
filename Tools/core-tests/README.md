# Core tests without Unity

`dotnet test Tools/core-tests` compiles `Assets/Game/Core/**` plus the EditMode tests and runs
them with NUnit, no Unity needed (about 5 s after the first restore). 148 of the project's 230
EditMode tests run this way: all the game rules, judging, scoring, party, roster, input map,
conduct detector, latency slew, URL params and the golden comparisons.

Not covered here (need the Unity player: PlayerPrefs, Resources, assets): `MusicTests`,
`SfxTests`, `VoiceTests`, `SettingsTests`, `ShellTests`, `TitleReelTests`. Run those on a machine
with Unity (`Tools/unity-batch.sh tests` or the Editor Test Runner) before merging.

`UnityShim.cs` only provides `UnityEngine.Application.dataPath` for the golden-file reader.
If you add a test that needs another UnityEngine type, either keep the logic in `Core/` and
test it without Unity, or add the test file to the `Compile Remove` list in the csproj.
Golden files are produced by `node Tools/golden/dump-charts.mjs` (needs the web sources, see
`Reference/web`); regenerating rewrites `chomp-chorus-cpu.txt` with a one-digit float
difference on a newer Node: revert that file.
