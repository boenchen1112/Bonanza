# Shell assets

## `cards/<game-id>.webp`

Real frames of each minigame, used by the free-play carousel and the title's
attract reel (`web/src/shell/cards.js` → `drawPreview`). Captured from this
repo's own build by `node tools/assets/capture-cards.mjs` (harness, real GPU,
perfect autoplay, UI hidden, cropped to 640x300). Original work — no external
source or licence involved. Re-run after a game's look changes.
