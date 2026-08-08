/**
 * Results screen.  [shell agent owns this file]
 * PLACEHOLDER — replace wholesale.
 */
import * as THREE from 'three';

export default {
  id: 'results', name: 'Results',
  load(ctx) {
    ctx.scene.background = new THREE.Color(0x0b0a1a);
    ctx.scene.add(new THREE.HemisphereLight(0x88aaff, 0x221133, 1.4));
    ctx.fx.attach(ctx.scene);
    const r = ctx.opts?.result || {};
    ctx.ui.banner(r.rank ? 'RANK ' + r.rank : 'RESULTS', {
      sub: `score ${Math.round(r.score || 0)}  ·  ${((r.accuracy || 0) * 100).toFixed(1)}%`,
      life: 1e9,
    });
  },
  start(ctx) { ctx.audio.sfx('fanfare'); },
  update() {},
  input(ctx, events) { for (const e of events) if (e.down && e.action === 'a') ctx.go('select'); },
  dispose() {},
};
