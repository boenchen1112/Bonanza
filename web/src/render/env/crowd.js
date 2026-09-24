/**
 * Crowd stands.  [render agent owns this dir]
 *
 * Tiered risers behind the arena plus an instanced crowd that bounces on the
 * beat. Two draws for the whole audience.
 *
 * The crowd is the cheapest legibility win in the project. It is a metronome
 * the player can see without looking at it: rows bounce on the beat with a
 * per-column phase offset, so the pulse sweeps sideways and the tempo is
 * readable from peripheral vision alone. On a big moment `cheer()` sends
 * everyone up at once — success is a change in the room, not just a popup.
 */

import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { damp, easeOutCubic, clamp01 } from '../../core/util.js';

export function makeCrowd({ look, rng }, {
  count = 132, arc = Math.PI * 1.05, radius = 17.5, tiers = 3, y = -0.9, z = -3,
} = {}) {
  const mats = look.materials;
  const group = new THREE.Group();
  group.name = 'env:crowd';
  group.position.z = z;

  // --- risers ---------------------------------------------------------------
  const riserMat = mats.toon({ color: 0xffffff, bands: 2, rim: 0.55, pulse: 0.05, side: THREE.DoubleSide, name: 'envRiser' });
  // One material and static, so every tier is baked into one geometry: one
  // draw for all the risers (it was one per tier).
  const tierGeos = [];
  for (let t = 0; t < tiers; t++) {
    const r = radius + t * 2.2;
    const h = 1.5 + t * 1.5;
    const g = new THREE.CylinderGeometry(r, r, h, 56, 1, true, -arc / 2 - Math.PI / 2, arc);
    g.translate(0, y + h / 2 - 0.4 + t * 0.9, 0);
    tierGeos.push(g);
  }
  const riserGeo = tierGeos.length > 1 ? mergeGeometries(tierGeos, false) : tierGeos[0];
  if (riserGeo !== tierGeos[0]) for (const g of tierGeos) g.dispose();
  const risers = new THREE.Mesh(riserGeo, riserMat);
  group.add(risers);

  // --- the crowd ------------------------------------------------------------
  const geo = new THREE.SphereGeometry(0.42, 8, 6);
  geo.scale(1, 1.25, 1);
  const mat = mats.toon({ color: 0xffffff, bands: 2, rim: 1.6, pulse: 0.2, name: 'envCrowd' });
  const mesh = new THREE.InstancedMesh(geo, mat, count);
  mesh.frustumCulled = false;
  group.add(mesh);

  const people = [];
  const perTier = Math.ceil(count / tiers);
  for (let i = 0; i < count; i++) {
    const tier = Math.floor(i / perTier);
    const k = (i % perTier) / perTier;
    const a = -arc / 2 + arc * (k + (rng() - 0.5) * 0.012) + Math.PI / 2;
    const r = radius + tier * 2.2 + 0.35 + rng() * 0.5;
    people.push({
      x: Math.cos(a) * r,
      z: -Math.sin(a) * r,
      base: y + 1.5 + tier * 2.4 + rng() * 0.1,
      col: rng(),
      // column phase — the bounce sweeps across the stands instead of the
      // whole crowd moving as one slab, which looks like a bug.
      phase: k * Math.PI * 2 * 1.5 + tier * 0.6,
      s: 0.85 + rng() * 0.35,
      pop: 0,
      lean: (rng() - 0.5) * 0.3,
    });
  }

  const m4 = new THREE.Matrix4();
  const q = new THREE.Quaternion();
  const e = new THREE.Euler();
  const p = new THREE.Vector3();
  const s = new THREE.Vector3();
  const col = new THREE.Color();
  let cheer = 0;

  function pulse(strength) {
    for (let i = 0; i < people.length; i++) people[i].pop = Math.max(people[i].pop, strength);
  }

  /** Everyone up, at once, hard. For finishes and combo milestones. */
  function bigCheer(strength = 1.4) {
    cheer = Math.max(cheer, strength);
    pulse(strength);
  }

  function update(dt, beat, time, pal) {
    cheer = damp(cheer, 0, 3.2, dt);
    const wave = Math.sin(beat * Math.PI * 2) * 0.5 + 0.5;
    for (let i = 0; i < people.length; i++) {
      const it = people[i];
      it.pop = damp(it.pop, 0, 7, dt);
      // idle: nobody is ever still
      const idle = Math.sin(time * 2.1 + it.phase) * 0.06;
      const hop = easeOutCubic(clamp01(it.pop)) * (0.55 + cheer * 0.5)
        * (0.55 + 0.45 * Math.sin(it.phase + wave * 1.4));
      p.set(it.x, it.base + idle + hop, it.z);
      e.set(0, 0, it.lean * (0.4 + hop));
      q.setFromEuler(e);
      const sc = it.s * (1 + hop * 0.12);
      s.set(sc, sc * (1 - hop * 0.06), sc);
      m4.compose(p, q, s);
      mesh.setMatrixAt(i, m4);
      const c = pal.crowd;
      col.setHex(c[(i * 7) % c.length], THREE.SRGBColorSpace)
        .multiplyScalar(0.5 + it.col * 0.35 + hop * 0.5 + cheer * 0.25);
      mesh.setColorAt(i, col);
    }
    mesh.instanceMatrix.needsUpdate = true;
    if (mesh.instanceColor) mesh.instanceColor.needsUpdate = true;
    riserMat.color.copy(pal.col.band).lerp(pal.col.fog, 0.2);
  }

  function dispose() {
    geo.dispose();
    riserGeo.dispose();
  }

  return { group, update, pulse, cheer: bigCheer, dispose };
}
