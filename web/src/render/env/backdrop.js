/**
 * Backdrop: arches and a far silhouette.  [render agent owns this dir]
 *
 * Two instanced meshes, two draw calls, and they do most of the work of making
 * the frame feel like a built set rather than a camera pointed at a cube:
 *
 *  - arches: big concentric rings standing behind the stage, half-buried in the
 *    ground. They give the eye a repeating depth cue and a natural place to
 *    put the beat (each ring pops one 16th after the one in front, so the beat
 *    reads as travelling *toward* the player).
 *  - skyline: a low band of blocks far back, in the fog colour. Its only job is
 *    to stop the horizon from being an empty gradient, and to sell scale.
 */

import * as THREE from 'three';
import { damp, easeOutCubic, clamp01 } from '../../core/util.js';

export function makeBackdrop({ look, rng }, {
  arches = 5, radius = 17, spacing = 5.5, z = -8, skyline = 26, skylineZ = -46,
} = {}) {
  const mats = look.materials;
  const group = new THREE.Group();
  group.name = 'env:backdrop';

  // --- arches ---------------------------------------------------------------
  const archGeo = new THREE.TorusGeometry(1, 0.055, 8, 80);
  const archMat = mats.toon({ color: 0xffffff, bands: 2, rim: 1.4, pulse: 0.5, emissive: 0.35, name: 'envArch' });
  const arch = new THREE.InstancedMesh(archGeo, archMat, arches);
  arch.frustumCulled = false;
  group.add(arch);

  const archState = [];
  for (let i = 0; i < arches; i++) {
    archState.push({
      r: radius + i * 2.6,
      z: z - i * spacing,
      pop: 0,
      phase: i,
      tilt: (rng() - 0.5) * 0.05,
    });
  }

  // --- far skyline ----------------------------------------------------------
  const boxGeo = new THREE.BoxGeometry(1, 1, 1);
  const boxMat = mats.toon({ color: 0xffffff, bands: 2, rim: 0.5, pulse: 0.06, name: 'envSkyline' });
  const sky = new THREE.InstancedMesh(boxGeo, boxMat, skyline);
  sky.frustumCulled = false;
  group.add(sky);

  {
    const m4 = new THREE.Matrix4();
    const q = new THREE.Quaternion();
    const p = new THREE.Vector3();
    const s = new THREE.Vector3();
    const spread = 120;
    for (let i = 0; i < skyline; i++) {
      const x = (i / (skyline - 1) - 0.5) * spread + (rng() - 0.5) * 3;
      const h = 4 + rng() * 16;
      const w = 3 + rng() * 5;
      const zz = skylineZ - rng() * 22;
      p.set(x, h / 2 - 2.5, zz);
      s.set(w, h, w);
      m4.compose(p, q, s);
      sky.setMatrixAt(i, m4);
    }
    sky.instanceMatrix.needsUpdate = true;
  }

  const m4 = new THREE.Matrix4();
  const q = new THREE.Quaternion();
  const p = new THREE.Vector3();
  const s = new THREE.Vector3();
  const col = new THREE.Color();
  const AX = new THREE.Vector3(0, 0, 1);

  function pulse(strength, beatIndex = 0) {
    for (const a of archState) {
      // Stagger so the pop travels forward through the set.
      a.queue = strength;
      a.delay = ((a.phase + beatIndex) % arches) * 0.045;
      a.t = -a.delay;
    }
  }

  function update(dt, beat, time, pal) {
    for (let i = 0; i < arches; i++) {
      const a = archState[i];
      if (a.queue !== undefined) {
        a.t += dt;
        if (a.t >= 0) { a.pop = Math.max(a.pop, a.queue); a.queue = undefined; }
      }
      a.pop = damp(a.pop, 0, 8, dt);
      const bob = Math.sin(time * 0.5 + i * 0.9) * 0.12;
      const sc = a.r * (1 + a.pop * 0.02);
      p.set(0, -1.4 + bob, a.z);
      q.setFromAxisAngle(AX, a.tilt + Math.sin(time * 0.24 + i) * 0.012);
      s.set(sc, sc, sc);
      m4.compose(p, q, s);
      arch.setMatrixAt(i, m4);
      const t = clamp01(i / Math.max(1, arches - 1));
      col.copy(pal.col.accent).lerp(pal.col.band, 0.35 + t * 0.5)
        .multiplyScalar(0.75 + a.pop * 1.4 + easeOutCubic(1 - t) * 0.35);
      arch.setColorAt(i, col);
    }
    arch.instanceMatrix.needsUpdate = true;
    if (arch.instanceColor) arch.instanceColor.needsUpdate = true;

    // Skyline sits in the fog, one shade above it — present, never competing.
    boxMat.color.copy(pal.col.band).lerp(pal.col.fog, 0.45);
  }

  function dispose() { archGeo.dispose(); boxGeo.dispose(); }

  return { group, update, pulse, dispose };
}
