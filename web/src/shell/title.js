/**
 * Title screen.  [shell agent owns this file]
 *
 * PLACEHOLDER — functional so the app boots and the harness can route, but
 * nowhere near the bar. Replace wholesale.
 */

import * as THREE from 'three';
import { damp } from '../core/util.js';

let root, tRuntime = 0;

export default {
  id: 'title',
  name: 'Title',

  load(ctx) {
    root = new THREE.Group();
    ctx.scene.add(root);
    ctx.scene.background = new THREE.Color(0x0b0a1a);
    ctx.scene.fog = new THREE.Fog(0x0b0a1a, 12, 40);

    const key = new THREE.DirectionalLight(0xffffff, 2.2);
    key.position.set(4, 8, 6);
    ctx.scene.add(key);
    ctx.scene.add(new THREE.HemisphereLight(0x88aaff, 0x221133, 1.1));

    const geo = new THREE.IcosahedronGeometry(1.1, 0);
    for (let i = 0; i < 12; i++) {
      const m = new THREE.Mesh(geo, new THREE.MeshStandardMaterial({
        color: [0xffd93d, 0x4dd6ff, 0xff5d73, 0x9ee87a][i % 4],
        roughness: 0.35, metalness: 0.1, flatShading: true,
      }));
      m.position.set((ctx.rng() - 0.5) * 14, (ctx.rng() - 0.5) * 6, -4 - ctx.rng() * 12);
      m.userData.spin = (ctx.rng() - 0.5) * 1.4;
      root.add(m);
    }

    ctx.fx.attach(ctx.scene);
    ctx.camera.position.set(0, 0, 9);
    ctx.camera.lookAt(0, 0, 0);

    ctx.ui.banner('BEAT BASH BONANZA', { sub: 'press SPACE to start', life: 1e9 });
    tRuntime = 0;
  },

  start(ctx) {
    ctx.clock.setBpm(124);
    ctx.clock.start(ctx.clock.now() + 0.15, 0);
  },

  update(ctx, dt) {
    tRuntime += dt;
    for (const m of root.children) {
      m.rotation.y += m.userData.spin * dt;
      m.rotation.x += m.userData.spin * 0.6 * dt;
    }
    ctx.camera.position.x = damp(ctx.camera.position.x, Math.sin(tRuntime * 0.4) * 0.6, 2, dt);
  },

  input(ctx, events) {
    for (const e of events) {
      if (e.down && (e.action === 'a')) {
        ctx.audio.sfx('ui');
        ctx.go('select');
      }
    }
  },

  dispose(ctx) {
    root?.traverse((o) => { o.geometry?.dispose?.(); o.material?.dispose?.(); });
    root = null;
  },
};
