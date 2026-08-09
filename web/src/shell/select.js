/**
 * Minigame select.  [shell agent owns this file]
 * PLACEHOLDER — functional routing only. Replace wholesale.
 */
import * as THREE from 'three';
import { GAMES } from './registry.js';
import { goPlay } from './nav.js';

let idx = 0, cards = [], root;

function render(ctx) {
  ctx.ui.clear();
  const el = ctx.ui.el('bbb-banner');
  const m = ctx.ui.el('bbb-banner__main', GAMES[idx].name);
  el.appendChild(m);
  el.appendChild(ctx.ui.el('bbb-banner__sub', '← → to browse  ·  SPACE to play'));
  el.style.cssText += 'left:50%;top:50%;transform:translate(-50%,-50%);';
  ctx.ui.layer.appendChild(el);
}

export default {
  id: 'select', name: 'Select',
  load(ctx) {
    root = new THREE.Group();
    ctx.scene.add(root);
    ctx.scene.background = new THREE.Color(0x0b0a1a);
    ctx.scene.add(new THREE.HemisphereLight(0x88aaff, 0x221133, 1.4));
    ctx.fx.attach(ctx.scene);
    ctx.camera.position.set(0, 0, 9);
    render(ctx);
  },
  start(ctx) { ctx.clock.setBpm(124); ctx.clock.start(ctx.clock.now() + 0.1, 0); },
  update() {},
  input(ctx, events) {
    for (const e of events) {
      if (!e.down) continue;
      if (e.action === 'left') { idx = (idx + GAMES.length - 1) % GAMES.length; ctx.audio.sfx('ui'); render(ctx); }
      else if (e.action === 'right') { idx = (idx + 1) % GAMES.length; ctx.audio.sfx('ui'); render(ctx); }
      else if (e.action === 'a') { ctx.audio.sfx('ui'); goPlay({ go: ctx.go }, GAMES[idx].id, { from: 'select' }); }
      else if (e.action === 'b' || e.action === 'pause') { ctx.audio.sfx('uiBack'); ctx.go('title'); }
    }
  },
  dispose() { root = null; cards = []; },
};
