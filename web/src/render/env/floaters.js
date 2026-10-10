/**
 * Floating shapes.  [render agent owns this dir]
 *
 * Chunky low-poly solids orbiting the arena, well outside the play space and
 * well above or below the eye line. One instanced draw.
 *
 * They exist for three reasons: parallax (the frame has depth even when
 * nothing is happening), colour (they carry the palette into the empty upper
 * third of the screen), and rhythm (they punch on the beat, so the tempo is
 * visible in peripheral vision while the player's fovea is on the gameplay).
 */

import * as THREE from 'three';
import { damp } from '../../core/util.js';

export function makeFloaters({ look, rng }, {
  count = 22, innerR = 16, outerR = 30, minY = -2, maxY = 15, size = 1.0,
} = {}) {
  const mats = look.materials;
  const group = new THREE.Group();
  group.name = 'env:floaters';

  // One geometry for all of them keeps this to a single draw; variety comes
  // from scale, spin and colour instead of from shape count.
  const geo = new THREE.OctahedronGeometry(1, 0);
  const mat = mats.toon({ color: 0xffffff, bands: 3, rim: 1.5, pulse: 0.35, flat: true, name: 'envFloater' });
  const mesh = new THREE.InstancedMesh(geo, mat, count);
  mesh.frustumCulled = false;
  group.add(mesh);

  const items = [];
  for (let i = 0; i < count; i++) {
    items.push({
      a: rng() * Math.PI * 2,
      r: innerR + rng() * (outerR - innerR),
      y: minY + rng() * (maxY - minY),
      s: size * (0.5 + rng() * 1.5),
      spin: (rng() - 0.5) * 0.9,
      orbit: (rng() - 0.5) * 0.05,
      bob: 0.4 + rng() * 0.9,
      phase: rng() * Math.PI * 2,
      hue: rng(),
      pop: 0,
    });
  }

  const m4 = new THREE.Matrix4();
  const q = new THREE.Quaternion();
  const e = new THREE.Euler();
  const p = new THREE.Vector3();
  const s = new THREE.Vector3();
  const col = new THREE.Color();

  function pulse(strength) {
    for (let i = 0; i < items.length; i++) {
      // Alternate which half of the ring punches, so the beat has a direction.
      items[i].pop = Math.max(items[i].pop, strength * (0.5 + 0.5 * Math.abs(Math.sin(items[i].a))));
    }
  }

  function update(dt, beat, time, pal) {
    for (let i = 0; i < items.length; i++) {
      const it = items[i];
      it.a += it.orbit * dt;
      it.pop = damp(it.pop, 0, 6, dt);
      const y = it.y + Math.sin(time * it.bob + it.phase) * 0.7;
      p.set(Math.cos(it.a) * it.r, y, Math.sin(it.a) * it.r - 4);
      e.set(time * it.spin, time * it.spin * 0.7 + it.phase, it.phase);
      q.setFromEuler(e);
      const sc = it.s * (1 + it.pop * 0.22);
      s.set(sc, sc, sc);
      m4.compose(p, q, s);
      mesh.setMatrixAt(i, m4);
      col.copy(it.hue < 0.4 ? pal.col.accent : it.hue < 0.75 ? pal.col.accent2 : pal.col.accent3)
        .multiplyScalar(0.62 + it.pop * 0.75);
      mesh.setColorAt(i, col);
    }
    mesh.instanceMatrix.needsUpdate = true;
    if (mesh.instanceColor) mesh.instanceColor.needsUpdate = true;
  }

  function dispose() { geo.dispose(); }

  return { group, update, pulse, dispose };
}
