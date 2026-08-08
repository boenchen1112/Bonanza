/**
 * Ground / platform.  [render agent owns this dir]
 *
 * A raised disc with visible thickness, a glowing edge ring, and confetti
 * strewn across it. Thickness matters: a flat plane reads as a texture, a slab
 * with an edge reads as a *place*, and the shadow the edge casts against the
 * backdrop is most of the depth in the frame.
 *
 * The edge ring is the beat instrument here — it is the largest continuous
 * shape on screen, so lifting it 20% on the downbeat pulses the whole image
 * without moving anything the player is trying to read.
 */

import * as THREE from 'three';
import { arenaTexture } from '../look/textures.js';
import { damp } from '../../core/util.js';

export function makeGround({ look, rng }, {
  radius = 13, thickness = 0.9, y = 0, confetti = 90, rings = true,
} = {}) {
  const mats = look.materials;
  const group = new THREE.Group();
  group.name = 'env:ground';
  group.position.y = y;

  const top = new THREE.Mesh(
    new THREE.CircleGeometry(radius, 72),
    mats.toon({ color: 0xffffff, map: arenaTexture(), bands: 3, rim: 0.25, pulse: 0.05, name: 'envGroundTop' })
  );
  top.rotation.x = -Math.PI / 2;
  top.receiveShadow = false;
  group.add(top);

  const side = new THREE.Mesh(
    new THREE.CylinderGeometry(radius, radius * 0.965, thickness, 72, 1, true),
    mats.toon({ color: 0xffffff, bands: 2, rim: 0.7, pulse: 0.08, side: THREE.DoubleSide, name: 'envGroundSide' })
  );
  side.position.y = -thickness / 2;
  group.add(side);

  const edge = new THREE.Mesh(
    new THREE.TorusGeometry(radius * 1.002, 0.075, 8, 96),
    mats.glow({ color: 0xffd93d })
  );
  edge.rotation.x = Math.PI / 2;
  edge.position.y = 0.02;
  group.add(edge);

  let inner = null;
  if (rings) {
    inner = new THREE.Mesh(
      new THREE.TorusGeometry(radius * 0.55, 0.035, 6, 72),
      mats.glow({ color: 0x4dd6ff, transparent: true, opacity: 0.75 })
    );
    inner.rotation.x = Math.PI / 2;
    inner.position.y = 0.015;
    group.add(inner);
  }

  // --- confetti strewn on the floor ----------------------------------------
  let bits = null;
  if (confetti > 0) {
    const g = new THREE.PlaneGeometry(0.26, 0.16);
    g.rotateX(-Math.PI / 2);
    bits = new THREE.InstancedMesh(g, mats.toon({
      color: 0xffffff, bands: 2, rim: 0, pulse: 0.25, name: 'envConfetti',
    }), confetti);
    bits.frustumCulled = false;
    const m4 = new THREE.Matrix4();
    const q = new THREE.Quaternion();
    const p = new THREE.Vector3();
    const s = new THREE.Vector3(1, 1, 1);
    const c = new THREE.Color();
    for (let i = 0; i < confetti; i++) {
      const a = rng() * Math.PI * 2;
      const r = Math.sqrt(rng()) * radius * 0.93;
      p.set(Math.cos(a) * r, 0.02, Math.sin(a) * r);
      q.setFromAxisAngle(UP, rng() * Math.PI);
      const sc = 0.7 + rng() * 0.9;
      s.set(sc, sc, sc);
      m4.compose(p, q, s);
      bits.setMatrixAt(i, m4);
      bits.setColorAt(i, c.setHex(0xffffff));
    }
    bits.instanceMatrix.needsUpdate = true;
    group.add(bits);
  }

  let pulseV = 0;
  const colA = new THREE.Color();

  function pulse(strength) { pulseV = Math.max(pulseV, strength); }

  function update(dt, beat, time, pal) {
    pulseV = damp(pulseV, 0, 7, dt);
    top.material.color.copy(pal.col.ground);
    side.material.color.copy(pal.col.groundAlt);
    edge.material.color.copy(colA.copy(pal.col.groundRim).multiplyScalar(1 + pulseV * 1.5));
    edge.scale.setScalar(1 + pulseV * 0.012);
    if (inner) {
      inner.material.color.copy(colA.copy(pal.col.accent2).multiplyScalar(0.55 + pulseV * 0.9));
      inner.scale.setScalar(1 + pulseV * 0.03);
    }
    if (bits) {
      bits.material.color.copy(colA.copy(pal.col.accent).lerp(pal.col.accent2, 0.35));
    }
  }

  function dispose() {
    group.traverse((o) => { o.geometry?.dispose?.(); });
  }

  return { group, update, pulse, dispose, top, edge, radius };
}

const UP = new THREE.Vector3(0, 1, 0);
