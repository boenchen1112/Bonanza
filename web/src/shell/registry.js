/**
 * Scene registry.  [INTEGRATOR-owned]
 *
 * Every scene and minigame MUST be listed here. `window.__BBB__.goto(id)`
 * routes through it, which is how the automated critic reaches your work —
 * an unregistered scene is, as far as review is concerned, not built.
 *
 * Imports are lazy so the title screen paints before the rest is parsed.
 */

export const SCENES = [
  { id: 'title', kind: 'shell', load: () => import('./title.js') },
  { id: 'select', kind: 'shell', load: () => import('./select.js') },
  { id: 'results', kind: 'shell', load: () => import('./results.js') },

  // Registered per the contract in nav.js: a view with its own entry routes
  // straight to it, otherwise it is hosted inside `select`. Registering these
  // is the pure upgrade that note describes — deep links and __BBB__.goto()
  // start working and nothing else changes.
  { id: 'roster', kind: 'shell', load: () => import('./roster.js') },
  { id: 'freeplay', kind: 'shell', load: () => import('./freeplay.js') },
  { id: 'play', kind: 'shell', load: () => import('./play.js') },

  // Not reachable in normal play — it exists so the character rig, animation
  // states and crowd can be reviewed at all. Work no critic can load is, as
  // far as review is concerned, work that was never done.
  { id: 'chars-demo', kind: 'debug', load: () => import('../chars/demo.js') },

  { id: 'swing-kings', kind: 'game', name: 'Swing Kings', load: () => import('../games/swingKings/index.js') },
  { id: 'drumline-dash', kind: 'game', name: 'Drumline Dash', load: () => import('../games/drumlineDash/index.js') },
  { id: 'bounce-brigade', kind: 'game', name: 'Bounce Brigade', load: () => import('../games/bounceBrigade/index.js') },
  { id: 'chomp-chorus', kind: 'game', name: 'Chomp Chorus', load: () => import('../games/chompChorus/index.js') },
  { id: 'finale-fever', kind: 'game', name: 'Finale Fever', load: () => import('../games/finaleFever/index.js') },
];

export const GAMES = SCENES.filter((s) => s.kind === 'game');

const cache = new Map();

export async function getScene(id) {
  if (cache.has(id)) return cache.get(id);
  const entry = SCENES.find((s) => s.id === id);
  if (!entry) return null;
  try {
    const mod = await entry.load();
    const scene = mod.default || mod;
    cache.set(id, scene);
    return scene;
  } catch (e) {
    console.error('scene load failed:', id, e);
    return null;
  }
}
