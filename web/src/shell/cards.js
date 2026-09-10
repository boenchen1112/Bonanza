/**
 * Game-card thumbnails.  [shell agent owns this file]
 *
 * Real frames of each minigame, captured by tools/assets/capture-cards.mjs
 * and bundled (ADR 0003). `drawPreview` paints these when present and falls
 * back to its procedural sketch when a card is missing or still decoding.
 */

const URLS = import.meta.glob('../assets/shell/cards/*.webp', { eager: true, query: '?url', import: 'default' });
const cache = new Map();

/** A decoded <img> for the game, or null (none bundled / not loaded yet). */
export function cardImage(id) {
  let img = cache.get(id);
  if (img === undefined) {
    const key = Object.keys(URLS).find((k) => k.endsWith(`/${id}.webp`));
    img = null;
    if (key && typeof Image !== 'undefined') {
      img = new Image();
      img.decoding = 'async';
      img.src = URLS[key];
    }
    cache.set(id, img);
  }
  return img && img.complete && img.naturalWidth > 0 ? img : null;
}
