/**
 * Pooled instanced particle system.  [render agent owns this directory]
 *
 * One InstancedMesh per family => one draw call per family. Every particle
 * attribute lives in a preallocated Float32Array, slot allocation is an O(1)
 * free-stack pop, and the per-frame walk visits only living particles. There
 * is no allocation on emit and none per frame — a rhythm game fires effects on
 * the beat, and a GC pause on a downbeat is a missed note (ARCHITECTURE.md #6).
 *
 * Emission parameters go through a single reused `params` struct rather than an
 * options object, so `burst 40 sparks` costs zero garbage:
 *
 *     const e = pool.begin();     // resets to family defaults
 *     e.x = 0; e.y = 1; e.speed = 7; e.setColor(0xffd93d);
 *     pool.emit(24);
 *
 * Orientation modes:
 *   billboard — camera-facing quad, spun by `rot`
 *   stretch   — camera-facing quad, aligned to screen-space velocity and
 *               elongated by speed (the only honest way to draw a fast spark)
 *   tumble    — full 3D rotation, double-sided (confetti flipping edge-on)
 *   ground    — flat on the XZ plane, spun about Y (decals, scorch)
 */

import * as THREE from 'three';
import { easeOutCubic, clamp01 } from '../../core/util.js';
import { bakeUv } from './textures.js';

export const MODE = { BILLBOARD: 0, STRETCH: 1, TUMBLE: 2, GROUND: 3 };

/** Alpha-over-life shapes. Different families need different exits. */
export const FADE = {
  QUAD_OUT: 0,  // (1-t)^2 — the default: bright immediately, gone quietly
  LINEAR: 1,    // steady drain — confetti, which must stay readable while it falls
  POP: 2,       // sin(pi t) — fades IN then out; for anything that must not
                //             appear at full brightness on frame one
  LATE: 3,      // holds, then drops off a cliff — impact cores
  HOLD: 4,      // constant, then a short tail — decals
};

function fadeAt(kind, t) {
  switch (kind) {
    case FADE.LINEAR: return 1 - t;
    case FADE.POP: return Math.sin(Math.PI * clamp01(t));
    case FADE.LATE: return t < 0.55 ? 1 : 1 - (t - 0.55) / 0.45;
    case FADE.HOLD: return t < 0.35 ? 1 : Math.pow(1 - (t - 0.35) / 0.65, 1.6);
    default: { const u = 1 - t; return u * u; }
  }
}

/** Per-instance rgba on a MeshBasicMaterial, without cloning three's shaders. */
export function patchInstanceRGBA(mat) {
  mat.onBeforeCompile = (sh) => {
    sh.vertexShader = 'attribute vec4 iCData;\nvarying vec4 vCD;\n' +
      sh.vertexShader.replace('void main() {', 'void main() {\n  vCD = iCData;');
    sh.fragmentShader = 'varying vec4 vCD;\n' +
      sh.fragmentShader.replace(
        '#include <color_fragment>',
        '#include <color_fragment>\n  diffuseColor.rgb *= vCD.rgb;\n  diffuseColor.a *= vCD.a;'
      );
  };
  mat.customProgramCacheKey = () => 'bbb-fx-rgba';
  return mat;
}

export class ParticlePool {
  /**
   * @param {object} o
   * @param {number} o.max         capacity (hard cap; emitting past it recycles the oldest)
   * @param {THREE.Texture} o.texture
   * @param {number[]} o.uvRect    atlas cell, baked into the geometry
   * @param {number} o.mode        MODE.*
   * @param {() => number} o.rng   seeded — replays must be identical
   */
  constructor({
    max, texture, uvRect, mode = MODE.BILLBOARD,
    blending = THREE.AdditiveBlending, rng, renderOrder = 10, depthTest = true,
  }) {
    this.max = max;
    this.mode = mode;
    this.rng = rng;

    const geo = bakeUv(new THREE.PlaneGeometry(1, 1), uvRect);
    // forceSinglePass: three draws a transparent DoubleSide material twice
    // (back faces, then front) to order the two sides of one closed mesh. A
    // flat quad has no second side to order and depthWrite is off, so that
    // second pass bought nothing but a draw call per live family — up to 9 a
    // frame in a busy verdict, enough to take Drumline Dash past 120 draws.
    // Both faces still render (confetti flips edge-on), in one draw.
    const mat = new THREE.MeshBasicMaterial({
      map: texture, transparent: true, depthWrite: false, depthTest,
      blending, side: THREE.DoubleSide, toneMapped: false, forceSinglePass: true,
    });
    patchInstanceRGBA(mat);

    const mesh = new THREE.InstancedMesh(geo, mat, max);
    mesh.frustumCulled = false;
    mesh.renderOrder = renderOrder;
    mesh.visible = false;
    mesh.count = 0;
    mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
    this.mesh = mesh;
    this.matArr = mesh.instanceMatrix.array;

    const cdata = new THREE.InstancedBufferAttribute(new Float32Array(max * 4), 4);
    cdata.setUsage(THREE.DynamicDrawUsage);
    geo.setAttribute('iCData', cdata);
    this.cdata = cdata;
    this.cArr = cdata.array;

    const F = (n) => new Float32Array(n);
    this.a = {
      x: F(max), y: F(max), z: F(max),
      vx: F(max), vy: F(max), vz: F(max),
      life: F(max), maxLife: F(max),
      size: F(max), s0: F(max), s1: F(max), aspect: F(max),
      rot: F(max), spin: F(max),
      grav: F(max), drag: F(max), turb: F(max),
      r: F(max), g: F(max), b: F(max), alpha: F(max),
      ax: F(max), ay: F(max), az: F(max), wx: F(max), wy: F(max), wz: F(max),
      stretch: F(max), phase: F(max),
    };
    this.fade = new Uint8Array(max);

    // O(1) allocation: free stack + swap-remove alive list.
    this.free = new Int32Array(max);
    for (let i = 0; i < max; i++) this.free[i] = max - 1 - i;
    this.freeCount = max;
    this.aliveIdx = new Int32Array(max);
    this.aliveCount = 0;

    this.params = {
      x: 0, y: 0, z: 0,
      dx: 0, dy: 1, dz: 0, cone: 1,
      speed: 6, speedVar: 0.45,
      size: 0.15, sizeVar: 0.35, aspect: 1,
      s0: 1, s1: 0.05,
      life: 0.5, lifeVar: 0.3,
      grav: -9, drag: 2.2, turb: 0,
      r: 1, g: 1, b: 1, alpha: 1,
      spin: 0, spinVar: 8,
      stretch: 0, fade: FADE.QUAD_OUT,
      tumble: 0,
      jitter: 0, // positional scatter at birth, world units
      setColor: (hex, mul = 1) => {
        const p = this.params;
        p.r = (((hex >> 16) & 255) / 255) * mul;
        p.g = (((hex >> 8) & 255) / 255) * mul;
        p.b = ((hex & 255) / 255) * mul;
        return p;
      },
      setPos: (v) => {
        const p = this.params;
        if (Array.isArray(v)) { p.x = v[0]; p.y = v[1]; p.z = v[2]; }
        else { p.x = v.x; p.y = v.y; p.z = v.z; }
        return p;
      },
      setDir: (v) => {
        const p = this.params;
        let dx, dy, dz;
        if (Array.isArray(v)) { dx = v[0]; dy = v[1]; dz = v[2]; }
        else { dx = v.x; dy = v.y; dz = v.z; }
        const l = Math.hypot(dx, dy, dz) || 1;
        p.dx = dx / l; p.dy = dy / l; p.dz = dz / l;
        return p;
      },
    };
    this._defaults = { ...this.params };
  }

  get object3d() { return this.mesh; }

  /** Reset the emit params to family defaults and return them. No allocation. */
  begin() {
    const p = this.params;
    const d = this._defaults;
    p.x = d.x; p.y = d.y; p.z = d.z;
    p.dx = d.dx; p.dy = d.dy; p.dz = d.dz; p.cone = d.cone;
    p.speed = d.speed; p.speedVar = d.speedVar;
    p.size = d.size; p.sizeVar = d.sizeVar; p.aspect = d.aspect;
    p.s0 = d.s0; p.s1 = d.s1;
    p.life = d.life; p.lifeVar = d.lifeVar;
    p.grav = d.grav; p.drag = d.drag; p.turb = d.turb;
    p.r = d.r; p.g = d.g; p.b = d.b; p.alpha = d.alpha;
    p.spin = d.spin; p.spinVar = d.spinVar;
    p.stretch = d.stretch; p.fade = d.fade; p.tumble = d.tumble;
    p.jitter = d.jitter;
    return p;
  }

  /** Override the family defaults once, at construction. */
  defaults(o) {
    Object.assign(this._defaults, o);
    return this;
  }

  _alloc() {
    let s;
    if (this.freeCount > 0) {
      s = this.free[--this.freeCount];
    } else {
      // Full: recycle the least-recently allocated slot rather than dropping the
      // emit. Dropping makes a big hit silently smaller, which is worse.
      s = this.aliveIdx[0];
      this.aliveIdx[0] = this.aliveIdx[--this.aliveCount];
    }
    this.aliveIdx[this.aliveCount++] = s;
    return s;
  }

  /** Emit `n` particles from the current params. */
  emit(n) {
    const p = this.params;
    const rng = this.rng;
    const a = this.a;
    for (let k = 0; k < n; k++) {
      const i = this._alloc();

      // direction: blend the aimed axis with a uniform sphere sample by `cone`
      const th = rng() * Math.PI * 2;
      const ph = Math.acos(2 * rng() - 1);
      const sph = Math.sin(ph);
      let dx = p.dx + (sph * Math.cos(th) - p.dx) * p.cone;
      let dy = p.dy + (sph * Math.sin(th) - p.dy) * p.cone;
      let dz = p.dz + (Math.cos(ph) - p.dz) * p.cone;
      const dl = Math.hypot(dx, dy, dz) || 1;
      const sp = p.speed * (1 + (rng() * 2 - 1) * p.speedVar);
      dx = (dx / dl) * sp; dy = (dy / dl) * sp; dz = (dz / dl) * sp;

      a.x[i] = p.x + (rng() * 2 - 1) * p.jitter;
      a.y[i] = p.y + (rng() * 2 - 1) * p.jitter;
      a.z[i] = p.z + (rng() * 2 - 1) * p.jitter;
      a.vx[i] = dx; a.vy[i] = dy; a.vz[i] = dz;
      a.life[i] = 0;
      a.maxLife[i] = Math.max(0.02, p.life * (1 + (rng() * 2 - 1) * p.lifeVar));
      a.size[i] = p.size * (1 + (rng() * 2 - 1) * p.sizeVar);
      a.s0[i] = p.s0; a.s1[i] = p.s1; a.aspect[i] = p.aspect;
      a.rot[i] = rng() * Math.PI * 2;
      a.spin[i] = (rng() * 2 - 1) * p.spinVar + p.spin;
      a.grav[i] = p.grav; a.drag[i] = p.drag; a.turb[i] = p.turb;
      a.r[i] = p.r; a.g[i] = p.g; a.b[i] = p.b; a.alpha[i] = p.alpha;
      a.stretch[i] = p.stretch;
      a.phase[i] = rng() * Math.PI * 2;
      if (this.mode === MODE.TUMBLE) {
        a.ax[i] = rng() * Math.PI * 2; a.ay[i] = rng() * Math.PI * 2; a.az[i] = rng() * Math.PI * 2;
        const w = p.tumble || 9;
        a.wx[i] = (rng() * 2 - 1) * w;
        a.wy[i] = (rng() * 2 - 1) * w;
        a.wz[i] = (rng() * 2 - 1) * w * 0.6;
      }
      this.fade[i] = p.fade;
    }
  }

  killAll() {
    const M = this.matArr;
    for (let k = 0; k < this.aliveCount; k++) {
      const o = this.aliveIdx[k] * 16;
      for (let j = 0; j < 16; j++) M[o + j] = 0;
      M[o + 15] = 1;
    }
    this.aliveCount = 0;
    this.freeCount = this.max;
    for (let i = 0; i < this.max; i++) this.free[i] = this.max - 1 - i;
    this.mesh.count = 0;
    this.mesh.visible = false;
    this.mesh.instanceMatrix.needsUpdate = true;
  }

  /**
   * Integrate and rewrite instance data.
   *
   * Called EVERY frame including dt===0 (hitstop): the impact frame is a
   * hitstop frame, and a particle that waits for the freeze to end before it
   * first draws is a particle that arrived late.
   */
  update(dt, cam, time) {
    const a = this.a;
    const M = this.matArr;
    const C = this.cArr;
    const mode = this.mode;

    // Camera basis, computed once per frame rather than a quaternion per quad.
    let rx = 1, ry = 0, rz = 0, ux = 0, uy = 1, uz = 0, fx = 0, fy = 0, fz = 1;
    if (cam) {
      const e = cam.matrixWorld.elements;
      rx = e[0]; ry = e[1]; rz = e[2];
      ux = e[4]; uy = e[5]; uz = e[6];
      fx = e[8]; fy = e[9]; fz = e[10];
    }

    let hi = -1;
    for (let k = 0; k < this.aliveCount; k++) {
      const i = this.aliveIdx[k];

      if (dt > 0) {
        const nl = a.life[i] + dt;
        if (nl >= a.maxLife[i]) {
          // die: zero the matrix once, return the slot, swap-remove
          const o = i * 16;
          for (let j = 0; j < 16; j++) M[o + j] = 0;
          M[o + 15] = 1;
          this.aliveIdx[k] = this.aliveIdx[--this.aliveCount];
          this.free[this.freeCount++] = i;
          k--;
          continue;
        }
        a.life[i] = nl;
        a.vy[i] += a.grav[i] * dt;
        const d = 1 - Math.min(0.95, a.drag[i] * dt);
        a.vx[i] *= d; a.vy[i] *= d; a.vz[i] *= d;
        if (a.turb[i] !== 0) {
          const ph = a.phase[i] + time * 3.1;
          a.vx[i] += Math.sin(ph) * a.turb[i] * dt;
          a.vz[i] += Math.cos(ph * 1.37) * a.turb[i] * dt;
        }
        a.x[i] += a.vx[i] * dt; a.y[i] += a.vy[i] * dt; a.z[i] += a.vz[i] * dt;
        a.rot[i] += a.spin[i] * dt;
        if (mode === MODE.TUMBLE) {
          a.ax[i] += a.wx[i] * dt; a.ay[i] += a.wy[i] * dt; a.az[i] += a.wz[i] * dt;
        }
      }

      if (i > hi) hi = i;

      const t = a.life[i] / a.maxLife[i];
      const sc = a.size[i] * (a.s0[i] + (a.s1[i] - a.s0[i]) * easeOutCubic(t));
      const alpha = a.alpha[i] * fadeAt(this.fade[i], t);

      const c4 = i * 4;
      C[c4] = a.r[i]; C[c4 + 1] = a.g[i]; C[c4 + 2] = a.b[i]; C[c4 + 3] = alpha;

      const o = i * 16;
      const px = a.x[i], py = a.y[i], pz = a.z[i];

      if (mode === MODE.TUMBLE) {
        // Rz * Ry * Rx, expanded. Confetti must show its edge, so this is a
        // real 3D rotation, not a spun billboard.
        const cx = Math.cos(a.ax[i]), sx = Math.sin(a.ax[i]);
        const cy = Math.cos(a.ay[i]), sy = Math.sin(a.ay[i]);
        const cz = Math.cos(a.az[i]), sz = Math.sin(a.az[i]);
        const w = sc * a.aspect[i], h = sc;
        M[o] = (cz * cy) * w; M[o + 1] = (sz * cy) * w; M[o + 2] = (-sy) * w; M[o + 3] = 0;
        M[o + 4] = (cz * sy * sx - sz * cx) * h; M[o + 5] = (sz * sy * sx + cz * cx) * h; M[o + 6] = (cy * sx) * h; M[o + 7] = 0;
        M[o + 8] = (cz * sy * cx + sz * sx) * sc; M[o + 9] = (sz * sy * cx - cz * sx) * sc; M[o + 10] = (cy * cx) * sc; M[o + 11] = 0;
      } else if (mode === MODE.GROUND) {
        const c = Math.cos(a.rot[i]), s = Math.sin(a.rot[i]);
        const w = sc * a.aspect[i];
        M[o] = c * w; M[o + 1] = 0; M[o + 2] = s * w; M[o + 3] = 0;
        M[o + 4] = -s * sc; M[o + 5] = 0; M[o + 6] = c * sc; M[o + 7] = 0;
        M[o + 8] = 0; M[o + 9] = sc; M[o + 10] = 0; M[o + 11] = 0;
      } else {
        let c, s, w = sc * a.aspect[i], h = sc;
        if (mode === MODE.STRETCH) {
          // align to screen-space velocity and elongate by speed
          const vx = a.vx[i], vy = a.vy[i], vz = a.vz[i];
          const sr = vx * rx + vy * ry + vz * rz;
          const su = vx * ux + vy * uy + vz * uz;
          const m = Math.hypot(sr, su);
          if (m > 1e-4) { c = sr / m; s = su / m; } else { c = 1; s = 0; }
          const speed = Math.hypot(vx, vy, vz);
          w = sc * a.aspect[i] * (1 + speed * a.stretch[i]);
          h = sc;
        } else {
          c = Math.cos(a.rot[i]); s = Math.sin(a.rot[i]);
        }
        M[o] = (rx * c + ux * s) * w; M[o + 1] = (ry * c + uy * s) * w; M[o + 2] = (rz * c + uz * s) * w; M[o + 3] = 0;
        M[o + 4] = (-rx * s + ux * c) * h; M[o + 5] = (-ry * s + uy * c) * h; M[o + 6] = (-rz * s + uz * c) * h; M[o + 7] = 0;
        M[o + 8] = fx * sc; M[o + 9] = fy * sc; M[o + 10] = fz * sc; M[o + 11] = 0;
      }
      M[o + 12] = px; M[o + 13] = py; M[o + 14] = pz; M[o + 15] = 1;
    }

    const live = this.aliveCount > 0;
    this.mesh.visible = live;
    // Only submit instances up to the highest live slot; the free stack is LIFO
    // so slots stay packed and this is usually close to aliveCount.
    this.mesh.count = live ? hi + 1 : 0;
    if (live || this._wasLive) {
      this.mesh.instanceMatrix.needsUpdate = true;
      this.cdata.needsUpdate = true;
    }
    this._wasLive = live;
  }

  dispose() {
    this.mesh.geometry.dispose();
    this.mesh.material.dispose();
  }
}
