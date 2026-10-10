# Bonanza

A party rhythm game that runs in any browser, no install. Five minigames, played
alone or as a party against CPU players:

| Id | Game | Play |
| --- | --- | --- |
| `swing-kings` | Swing Kings | Time the swing to the pitch. Tap, hold and release, or conduct with your hand on the webcam. |
| `chomp-chorus` | Chomp Chorus | Sing your lane in a four-voice row. |
| `drumline-dash` | Drumline Dash | Answer the drum call and race the field. |
| `bounce-brigade` | Bounce Brigade | Bounce from platform to platform on the beat. |
| `finale-fever` | Finale Fever | Call-and-answer finale with a rising tempo. |

Built with Three.js and Vite. All timing runs on the audio clock, and every
game plays on taps alone. Swing Kings also has an optional hand-tracking mode
(MediaPipe) that uses the webcam on your device and sends nothing anywhere.

## Run it

Node.js 20 or later.

```powershell
npm --prefix web install
npm run dev              # http://127.0.0.1:5178
npm --prefix web test    # unit tests
npm --prefix web run build
```

Main button: Space, Enter, J, a click or a tap. Esc or P pauses.

## Checks

A harness boots the built game in headless Chromium, plays it, and writes
screenshots and timing telemetry:

```powershell
node tools/harness/inspect.mjs --scene swing-kings --dist dist-check --out runs/check --seconds 12 --shots 4 --play auto
```

## Layout

```
web/       The game: src/ (core, audio, render, chars, games, shell, ui), tests/, ARCHITECTURE.md
tools/     harness/ (headless play and checks), assets/ (Blender and Mixamo pipelines)
worker/    Optional leaderboard (Cloudflare Worker + D1)
docs/      Design notes and decision records
```

## Branches

- `main`: this browser version, playable.
- `dev`: the Unity rebuild, still in development.

## Credits

See `CREDITS.md`.
