# Unity batch mode

The `unity` CLI and a live Editor can hang (Hub sign-in loop, Pipeline server waiting on
modal dialogs). `Tools/unity-batch.sh` drives Unity with `-batchmode` instead. No Editor,
no Hub, no `unity` CLI. Editor entry points live in `Assets/Game/Editor/BonanzaBuild.cs`.

```sh
Tools/unity-batch.sh tests [EditMode|PlayMode]      # prints "N/M passed" + failing names; XML in Logs/test-results.xml
Tools/unity-batch.sh build-webgl                    # -> Builds/WebGL, verifies .wasm.br/.data.br/.framework.js.br/loader
Tools/unity-batch.sh exec Ns.Class.Method [args]    # generic -executeMethod (UNITY_BATCH_SCRIPT etc. pass through the env)
Tools/unity-batch.sh import                         # refresh/import assets and quit
Tools/unity-batch.sh screenshot <scene> <out.png> [w h]          # needs graphics (no -nographics)
Tools/unity-batch.sh play-capture <scene> <outDir> [secs] [every] [w h]   # Play Mode frames
```

Logs: `Logs/batch-{tests,build,exec,import,screenshot,play}.log`.
Env: `UNITY` (binary), `UNITY_TIMEOUT` (seconds, overrides), or `TEST_TIMEOUT` / `BUILD_TIMEOUT`
/ `SHOT_TIMEOUT` / `PLAY_TIMEOUT`. Defaults: tests 10 min, build 30 min, others 15-20 min.
Exit code is non-zero on any failure or timeout; on timeout the whole Unity process tree is killed.

## Rules

- One Unity process at a time. The script refuses to start if another Unity has this
  project open (`pgrep` on `-projectPath` and `lsof Temp/UnityLockfile`). Close the Editor first.
- Never run `unity` (CLI) or open Unity Hub for this project.
- The gates (autoplay, pause, input, party) run against `Builds/WebGL` with node as before.

## Pitfalls

- Unity does not rewrite byte-identical outputs on an incremental build, so `.wasm.br` and
  friends can keep an old mtime after a successful build. The script treats a file as fresh
  if it was rewritten or is newer than every runtime source (Editor/ and Tests/ excluded).
- This project disables the Play Mode domain reload, so `play-capture` arms its watcher directly
  (`PlayCaptureRunner.Arm`) instead of relying on an `[InitializeOnLoad]` re-run.
- A background watchdog that inherits stdout keeps `cmd | tail` waiting until its `sleep`
  ends. The script kills the watchdog's whole tree; keep that if you edit it.
- The first run after a script change recompiles (extra ~20 s); an incremental WebGL build
  takes about 40 s, a cold one much longer.
- `build-webgl` uses the PlayerSettings already stored in `ProjectSettings` (Brotli, no
  decompression fallback) and throws if they drift. It writes nothing to them. After any
  batch run check `git status ProjectSettings Packages` and `git checkout` unrelated rewrites.
- zsh: `echo ===` fails ("== not found"); quote it.

## What batch mode can and cannot do

- `screenshot` opens (edit mode: SwingKings.unity holds only a camera and the host, the stage is built at
  runtime, so the PNG is black; use `play-capture` for the real stage) the scene in edit mode and renders the main camera into a half-float
  target (like `hdr-shot.cs`). Screen-space-overlay uGUI (HUD, results card) is not part of
  a camera render and will not appear. Needs a GPU/window server session (macOS Metal works).
- `play-capture` enters Play Mode (domain reload; state survives via `SessionState`), waits
  N real seconds, and renders the camera every S seconds. Set `BONANZA_AUTOPLAY=1`,
  `BONANZA_GAME=<id>`, `BONANZA_PARTY=1` in the environment. No audio output in batch mode
  and no real input, so use autoplay. Game-dependent timing runs against `AudioSettings.dspTime`,
  which may behave differently without an audio device: use the WebGL gates as the truth.
- Cannot answer modal dialogs, run a persistent Editor, or use `unity command` (eval,
  console, hierarchy edits). For one-off scene/asset edits write a static method and use `exec`.
