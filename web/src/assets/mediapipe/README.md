# MediaPipe hand-landmarker model

| | |
| --- | --- |
| File | `hand_landmarker.task` (float16, v1) |
| Source | Google MediaPipe — `https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task`, downloaded once at development time (2026-09-11) |
| Licence | Apache License 2.0 (MediaPipe models and `@mediapipe/tasks-vision`) |
| Used by | `web/src/games/swingKings/camera.js` (Swing Kings "camera conduct" input) |

Bundled per ADR 0003: the game never fetches it (or the tasks-vision wasm,
which comes from the npm package) from a CDN at runtime. Lazy-loaded only
when a player picks camera conducting.
