/**
 * Bunting and banners.  [render agent owns this dir]
 *
 * A garland of triangular flags strung in an arc above the arena, waving out
 * of phase. One instanced draw for the flags, one thin torus for the line.
 *
 * Framing device first, decoration second: the garland closes the top of the
 * frame, which stops the composition leaking into empty sky and pushes the
 * eye down onto the stage. It also gives the camera something to parallax
 * against on a push-in.
 */

import * as THREE from 'three';
import { damp } from '../../core/util.js';

export function makeBanners({ look, rng }, {
  count = 26, radius = 12.5, y = 7.2, sag = 1.6, size = 1.05, z = -2,
} = {}) {
  const mats = look.materials;
  const group = new THREE.Group();
  group.name = 'env:banners';
  group.position.z = z;

  // Triangle flag, pivot at the top edge so it swings from the line.
  const g = new THREE.BufferGeometry();
  const w = 0.45 * size, h = 0.85 * size;
  g.setAttribute('position', new THREE.BufferAttribute(new Float32Array([
    -w, 0, 0, w, 0, 0, 0, -h, 0,
  ]), 3));
  g.setAttribute('normal', new THREE.BufferAttribute(new Float32Array([
    0, 0, 1, 0, 0, 1, 0, 0, 1,
  ]), 3));
  g.setAttribute('uv', new THREE.BufferAttribute(new Float32Array([0, 1, 1, 1, 0.5, 0]), 2));
  g.setIndex([0, 1, 2]);

  const mat = mats.toon({
    color: 0xffffff, bands: 2, rim: 1.1, pulse: 0.3,
    side: THREE.DoubleSide, name: 'envBanner',
  });
  const mesh = new THREE.InstancedMesh(g, mat, count);
  mesh.frustumCulled = false;
  group.add(mesh);

  const line = new THREE.Mesh(
    new THREE.TorusGeometry(radius, 0.03, 5, 72, Math.PI * 1.35),
    mats.glow({ color: 0x000000 })
  );
  line.rotation.set(Math.PI / 2, 0, 0);
  line.position.y = y;
  group.add(line);

  const flags = [];
  const spanA = Math.PI * 1.3;
  for (let i = 0; i < count; i++) {
    const k = i / (count - 1);
    const a = -spanA / 2 + spanA * k + Math.PI / 2;
    flags.push({
      x: Math.cos(a) * radius,
      z: -Math.sin(a) * radius,
      y: y - Math.sin(k * Math.PI) * sag,
      phase: rng() * Math.PI * 2,
      speed: 1.4 + rng() * 0.8,
      hue: i % 3,
      face: a,
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
    for (let i = 0; i < flags.length; i++) flags[i].pop = Math.max(flags[i].pop, strength * (0.4 + 0.6 * (i % 2)));
  }

  function update(dt, beat, time, pal) {
    for (let i = 0; i < flags.length; i++) {
      const f = flags[i];
      f.pop = damp(f.pop, 0, 7, dt);
      const swing = Math.sin(time * f.speed + f.phase) * 0.28 + f.pop * 0.35;
      p.set(f.x, f.y, f.z);
      e.set(swing * 0.5, -f.face + Math.PI / 2, swing);
      q.setFromEuler(e);
      const sc = 1 + f.pop * 0.25;
      s.set(sc, sc, sc);
      m4.compose(p, q, s);
      mesh.setMatrixAt(i, m4);
      const base = f.hue === 0 ? pal.col.accent : f.hue === 1 ? pal.col.accent2 : pal.col.accent3;
      col.copy(base).multiplyScalar(0.8 + f.pop * 0.8);
      mesh.setColorAt(i, col);
    }
    mesh.instanceMatrix.needsUpdate = true;
    if (mesh.instanceColor) mesh.instanceColor.needsUpdate = true;
    line.material.color.copy(pal.col.band).multiplyScalar(0.5);
  }

  function dispose() { g.dispose(); line.geometry.dispose(); }

  return { group, update, pulse, dispose };
}
