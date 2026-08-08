/**
 * Pooled VFX.  [render agent owns this directory]
 *
 * One InstancedMesh per effect family, zero allocation per burst. A rhythm
 * game fires effects on every beat; a GC pause on a downbeat is a missed note.
 */

import * as THREE from 'three';
import { clamp01, easeOutCubic, makeRng } from '../../core/util.js';

const MAX_SPARKS = 512;
const MAX_RINGS = 24;

export function createFX({ stage, clock }) {
  const rng = makeRng(0xfeed);
  let host = null; // current scene

  // --- sparks ---------------------------------------------------------------
  const sparkGeo = new THREE.PlaneGeometry(1, 1);
  const sparkMat = new THREE.MeshBasicMaterial({
    transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
    side: THREE.DoubleSide,
  });
  const sparks = new THREE.InstancedMesh(sparkGeo, sparkMat, MAX_SPARKS);
  sparks.frustumCulled = false;
  sparks.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
  sparks.count = MAX_SPARKS;
  const sparkColor = new THREE.InstancedBufferAttribute(new Float32Array(MAX_SPARKS * 3), 3);
  sparkGeo.setAttribute('instanceColor', sparkColor);

  const state = new Array(MAX_SPARKS).fill(null).map(() => ({
    alive: false, x: 0, y: 0, z: 0, vx: 0, vy: 0, vz: 0,
    life: 0, maxLife: 1, size: 0.1, spin: 0, rot: 0, grav: -9,
  }));
  let cursor = 0;
  let dirty = false;

  const m4 = new THREE.Matrix4();
  const q = new THREE.Quaternion();
  const v3 = new THREE.Vector3();
  const s3 = new THREE.Vector3();
  const col = new THREE.Color();

  // Additive quads with per-instance colour need a tiny shader patch.
  sparkMat.onBeforeCompile = (sh) => {
    sh.vertexShader = 'attribute vec3 instanceColor;\nvarying vec3 vC;\n' +
      sh.vertexShader.replace('void main() {', 'void main() {\n  vC = instanceColor;');
    sh.fragmentShader = 'varying vec3 vC;\n' +
      sh.fragmentShader.replace(
        '#include <color_fragment>',
        '#include <color_fragment>\n  diffuseColor.rgb *= vC;'
      );
  };

  // Park every slot off-screen at zero scale once, so a slot that has never
  // been used doesn't draw a garbage identity-matrix quad at the origin.
  {
    const z = new THREE.Matrix4().compose(
      new THREE.Vector3(0, -9999, 0), new THREE.Quaternion(), new THREE.Vector3(0, 0, 0)
    );
    for (let i = 0; i < MAX_SPARKS; i++) sparks.setMatrixAt(i, z);
    sparks.instanceMatrix.needsUpdate = true;
    sparks.visible = false;
  }

  function attach(scene) {
    host = scene;
    scene.add(sparks);
    scene.add(ringGroup);
  }

  // --- rings ----------------------------------------------------------------
  const ringGroup = new THREE.Group();
  const rings = [];
  for (let i = 0; i < MAX_RINGS; i++) {
    const g = new THREE.RingGeometry(0.45, 0.5, 48);
    const mt = new THREE.MeshBasicMaterial({
      transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide,
    });
    const mesh = new THREE.Mesh(g, mt);
    mesh.visible = false;
    mesh.frustumCulled = false;
    ringGroup.add(mesh);
    rings.push({ mesh, life: 0, maxLife: 0.5, from: 0.4, to: 3, alive: false, billboard: true });
  }

  /** Radial burst of sparks. */
  function burst(pos, {
    count = 18, color = 0xffd93d, speed = 6, spread = 1, size = 0.14,
    life = 0.5, gravity = -9, dir = null, cone = Math.PI,
  } = {}) {
    col.set(color);
    for (let i = 0; i < count; i++) {
      const p = state[cursor];
      const idx = cursor;
      cursor = (cursor + 1) % MAX_SPARKS;

      let dx, dy, dz;
      if (dir) {
        const a = (rng() - 0.5) * cone;
        const b = (rng() - 0.5) * cone;
        dx = dir[0] + Math.sin(a) * spread;
        dy = dir[1] + Math.sin(b) * spread;
        dz = dir[2] + (rng() - 0.5) * spread * 0.5;
      } else {
        const th = rng() * Math.PI * 2;
        const ph = Math.acos(2 * rng() - 1);
        dx = Math.sin(ph) * Math.cos(th);
        dy = Math.sin(ph) * Math.sin(th);
        dz = Math.cos(ph) * 0.5;
      }
      const sp = speed * (0.55 + rng() * 0.9);
      p.alive = true;
      p.x = pos[0]; p.y = pos[1]; p.z = pos[2];
      p.vx = dx * sp; p.vy = dy * sp; p.vz = dz * sp;
      p.life = 0; p.maxLife = life * (0.7 + rng() * 0.6);
      p.size = size * (0.6 + rng() * 0.9);
      p.rot = rng() * Math.PI; p.spin = (rng() - 0.5) * 14;
      p.grav = gravity;
      sparkColor.setXYZ(idx, col.r, col.g, col.b);
    }
    sparkColor.needsUpdate = true;
  }

  /** Expanding shock ring — the single most legible "you hit it" cue. */
  function ring(pos, { color = 0xffffff, life = 0.42, from = 0.35, to = 3.2, billboard = true } = {}) {
    const r = rings.find((x) => !x.alive) || rings[0];
    r.alive = true; r.life = 0; r.maxLife = life; r.from = from; r.to = to;
    r.billboard = billboard;
    r.mesh.visible = true;
    r.mesh.position.set(pos[0], pos[1], pos[2]);
    r.mesh.material.color.set(color);
    r.mesh.material.opacity = 1;
    r.mesh.scale.setScalar(from);
  }

  function confetti(pos, { count = 60, colors = [0xffd93d, 0x4dd6ff, 0xff5d73, 0x9ee87a] } = {}) {
    for (let i = 0; i < count; i++) {
      burst(pos, {
        count: 1, color: colors[i % colors.length], speed: 9, spread: 1.4,
        size: 0.17, life: 1.5, gravity: -7,
      });
    }
  }

  function update(dt) {
    if (!host) return;
    if (dt > 0) {
      // Only walk slots that are alive, and write a dead slot's zero matrix
      // exactly once (on the frame it dies) rather than every frame forever.
      // Walking all 512 slots and recomposing a matrix per slot costs several
      // ms of pure waste when nothing is on screen.
      let anyAlive = false;
      for (let i = 0; i < MAX_SPARKS; i++) {
        const p = state[i];
        if (!p.alive) continue;
        p.life += dt;
        if (p.life >= p.maxLife) {
          p.alive = false;
          s3.set(0, 0, 0); m4.compose(v3.set(0, -9999, 0), q.identity(), s3);
          sparks.setMatrixAt(i, m4);
          dirty = true;
          continue;
        }
        anyAlive = true;
        p.vy += p.grav * dt;
        p.vx *= 1 - 2.2 * dt; p.vz *= 1 - 2.2 * dt;
        p.x += p.vx * dt; p.y += p.vy * dt; p.z += p.vz * dt;
        p.rot += p.spin * dt;
        const t = p.life / p.maxLife;
        const sc = p.size * (1 - easeOutCubic(t)) * 1.6;
        v3.set(p.x, p.y, p.z);
        q.setFromAxisAngle(AXIS_Z, p.rot);
        s3.set(sc, sc, sc);
        m4.compose(v3, q, s3);
        sparks.setMatrixAt(i, m4);
        dirty = true;
      }
      if (dirty) { sparks.instanceMatrix.needsUpdate = true; dirty = false; }
      sparks.visible = anyAlive;

      for (const r of rings) {
        if (!r.alive) continue;
        r.life += dt;
        const t = clamp01(r.life / r.maxLife);
        if (t >= 1) { r.alive = false; r.mesh.visible = false; continue; }
        const sc = r.from + (r.to - r.from) * easeOutCubic(t);
        r.mesh.scale.setScalar(sc);
        r.mesh.material.opacity = (1 - t) * (1 - t);
        if (r.billboard && stage.camera) r.mesh.quaternion.copy(stage.camera.quaternion);
      }
    }
  }

  function reset() {
    for (const p of state) p.alive = false;
    for (const r of rings) { r.alive = false; r.mesh.visible = false; }
    if (host) { host.remove(sparks); host.remove(ringGroup); }
    host = null;
  }

  return { attach, burst, ring, confetti, update, reset, get attached() { return !!host; } };
}

const AXIS_Z = new THREE.Vector3(0, 0, 1);
