/**
 * Shockwave rings.  [render agent owns this directory]
 *
 * A single InstancedMesh of quads; the annulus is computed in the fragment
 * shader from UV, which buys three things a RingGeometry cannot:
 *
 *  - THICKNESS ANIMATION. A scaled RingGeometry keeps its proportions, so it
 *    reads as "a ring getting closer", not "a wave passing through". Real
 *    shock rings thin out as they expand. Here radius and thickness are
 *    independent per-instance parameters, so the ring can bloom fat and
 *    tighten to a filament.
 *  - DISTORTION. A per-instance angular wobble (two harmonics, seeded per
 *    ring) breaks the perfect circle. A perfect circle reads as UI; a
 *    wobbling one reads as force moving through air.
 *  - ONE DRAW CALL for every ring on screen, at any orientation.
 *
 * Orientation is per-ring: billboard rings sit facing the camera (the loudest,
 * most legible "you hit it" cue), oriented rings lie in the plane normal to the
 * hit direction (which is what actually sells a directional impact), and ground
 * rings lie flat so the wave visibly travels across the floor.
 */

import * as THREE from 'three';
import { easeOutCubic, easeOutQuint, clamp01 } from '../../core/util.js';

const VERT = /* glsl */`
attribute vec4 iCData;   // rgb, alpha
attribute vec4 iParams;  // radius01, thickness01, wobbleAmp, seed
varying vec4 vC;
varying vec4 vP;
varying vec2 vUv;
void main() {
  vC = iCData;
  vP = iParams;
  vUv = uv;
  gl_Position = projectionMatrix * modelViewMatrix * instanceMatrix * vec4(position, 1.0);
}`;

const FRAG = /* glsl */`
precision highp float;
varying vec4 vC;
varying vec4 vP;
varying vec2 vUv;
void main() {
  vec2 p = vUv - 0.5;
  float r = length(p) * 2.0;
  if (r > 1.0) discard;
  float ang = atan(p.y, p.x);
  // two harmonics -> an organic wobble instead of a polygon
  float wob = vP.z * (sin(ang * 3.0 + vP.w) * 0.65 + sin(ang * 7.0 - vP.w * 1.7) * 0.35);
  float d = abs(r + wob - vP.x);
  float th = max(vP.y, 0.004);
  float band = 1.0 - smoothstep(0.0, th, d);
  float core = 1.0 - smoothstep(0.0, th * 0.3, d);
  float a = (band * band * 0.8 + core * 0.85) * vC.a;
  a *= smoothstep(1.0, 0.9, r);          // soft clip at the quad edge
  if (a < 0.004) discard;
  vec3 rgb = vC.rgb * (0.65 + core * 1.6);
  gl_FragColor = vec4(rgb, a);
  #include <colorspace_fragment>
}`;

export class RingPool {
  constructor({ max = 48, rng }) {
    this.max = max;
    this.rng = rng;

    const geo = new THREE.PlaneGeometry(1, 1);
    const mat = new THREE.ShaderMaterial({
      vertexShader: VERT,
      fragmentShader: FRAG,
      transparent: true,
      depthWrite: false,
      blending: THREE.AdditiveBlending,
      side: THREE.DoubleSide,
    });
    const mesh = new THREE.InstancedMesh(geo, mat, max);
    mesh.frustumCulled = false;
    mesh.renderOrder = 11;
    mesh.visible = false;
    mesh.count = 0;
    mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
    this.mesh = mesh;
    this.matArr = mesh.instanceMatrix.array;

    const c = new THREE.InstancedBufferAttribute(new Float32Array(max * 4), 4);
    const p = new THREE.InstancedBufferAttribute(new Float32Array(max * 4), 4);
    c.setUsage(THREE.DynamicDrawUsage);
    p.setUsage(THREE.DynamicDrawUsage);
    geo.setAttribute('iCData', c);
    geo.setAttribute('iParams', p);
    this.cAttr = c; this.pAttr = p;
    this.cArr = c.array; this.pArr = p.array;

    const F = (n) => new Float32Array(n);
    this.a = {
      x: F(max), y: F(max), z: F(max),
      life: F(max), maxLife: F(max),
      from: F(max), to: F(max),
      th0: F(max), th1: F(max),
      wob: F(max), seed: F(max),
      r: F(max), g: F(max), b: F(max), alpha: F(max),
      qx: F(max), qy: F(max), qz: F(max), qw: F(max),
      spin: F(max),
    };
    this.billboard = new Uint8Array(max);

    this.free = new Int32Array(max);
    for (let i = 0; i < max; i++) this.free[i] = max - 1 - i;
    this.freeCount = max;
    this.aliveIdx = new Int32Array(max);
    this.aliveCount = 0;

    this._q = new THREE.Quaternion();
    this._qs = new THREE.Quaternion();
    this._m = new THREE.Matrix4();
    this._v = new THREE.Vector3();
    this._s = new THREE.Vector3();
    this._up = new THREE.Vector3(0, 0, 1);
  }

  get object3d() { return this.mesh; }

  _alloc() {
    let s;
    if (this.freeCount > 0) s = this.free[--this.freeCount];
    else { s = this.aliveIdx[0]; this.aliveIdx[0] = this.aliveIdx[--this.aliveCount]; }
    this.aliveIdx[this.aliveCount++] = s;
    return s;
  }

  /**
   * @param {number[]|THREE.Vector3} pos
   * @param {object} o
   * @param {number} [o.color]
   * @param {number} [o.life]      seconds
   * @param {number} [o.from]      world radius at birth
   * @param {number} [o.to]        world radius at death
   * @param {number} [o.thick0]    thickness as a fraction of radius, at birth
   * @param {number} [o.thick1]    ... at death (thinning = a wave passing)
   * @param {number} [o.wobble]    distortion amplitude, 0 = perfect circle
   * @param {number} [o.alpha]
   * @param {number[]} [o.normal]  plane normal; omit for a billboard
   * @param {number} [o.spin]      rad/s about the ring's own axis
   */
  spawn(pos, {
    color = 0xffffff, life = 0.42, from = 0.4, to = 3.2,
    thick0 = 0.30, thick1 = 0.045, wobble = 0.02, alpha = 1,
    normal = null, spin = 0,
  } = {}) {
    const i = this._alloc();
    const a = this.a;
    if (Array.isArray(pos)) { a.x[i] = pos[0]; a.y[i] = pos[1]; a.z[i] = pos[2]; }
    else { a.x[i] = pos.x; a.y[i] = pos.y; a.z[i] = pos.z; }
    a.life[i] = 0; a.maxLife[i] = life;
    a.from[i] = from; a.to[i] = to;
    a.th0[i] = thick0; a.th1[i] = thick1;
    a.wob[i] = wobble;
    a.seed[i] = this.rng() * 6.283;
    a.r[i] = ((color >> 16) & 255) / 255;
    a.g[i] = ((color >> 8) & 255) / 255;
    a.b[i] = (color & 255) / 255;
    a.alpha[i] = alpha;
    a.spin[i] = spin;
    if (normal) {
      this._v.set(normal[0], normal[1], normal[2]).normalize();
      this._q.setFromUnitVectors(this._up, this._v);
      a.qx[i] = this._q.x; a.qy[i] = this._q.y; a.qz[i] = this._q.z; a.qw[i] = this._q.w;
      this.billboard[i] = 0;
    } else {
      this.billboard[i] = 1;
    }
    return i;
  }

  update(dt, cam) {
    const a = this.a;
    const M = this.matArr;
    const C = this.cArr;
    const P = this.pArr;
    const q = this._q;
    const m = this._m;

    let hi = -1;
    for (let k = 0; k < this.aliveCount; k++) {
      const i = this.aliveIdx[k];
      if (dt > 0) {
        const nl = a.life[i] + dt;
        if (nl >= a.maxLife[i]) {
          const o = i * 16;
          for (let j = 0; j < 16; j++) M[o + j] = 0;
          M[o + 15] = 1;
          this.aliveIdx[k] = this.aliveIdx[--this.aliveCount];
          this.free[this.freeCount++] = i;
          k--;
          continue;
        }
        a.life[i] = nl;
      }
      if (i > hi) hi = i;

      const t = clamp01(a.life[i] / a.maxLife[i]);
      // radius on a quint-out: fast on frame one (the eye needs the change
      // NOW), then coasting so the ring lingers where it can be read.
      const rad = a.from[i] + (a.to[i] - a.from[i]) * easeOutQuint(t);
      const thick = a.th0[i] + (a.th1[i] - a.th0[i]) * easeOutCubic(t);
      const alpha = a.alpha[i] * (1 - t) * (1 - t);

      const c4 = i * 4;
      C[c4] = a.r[i]; C[c4 + 1] = a.g[i]; C[c4 + 2] = a.b[i]; C[c4 + 3] = alpha;
      // radius01 is fixed at 0.42 of the quad; the quad grows instead, which
      // keeps the shader's edge-antialiasing footprint constant.
      P[c4] = 0.42;
      P[c4 + 1] = thick;
      P[c4 + 2] = a.wob[i] * (0.35 + t * 1.2); // distortion grows as it weakens
      P[c4 + 3] = a.seed[i];

      const scale = rad * 2.4;
      if (this.billboard[i] && cam) q.copy(cam.quaternion);
      else q.set(a.qx[i], a.qy[i], a.qz[i], a.qw[i]);
      if (a.spin[i] !== 0) {
        this._s.set(0, 0, 1).applyQuaternion(q);
        this._qs.setFromAxisAngle(this._s, a.spin[i] * a.life[i]);
        q.premultiply(this._qs);
      }
      this._v.set(a.x[i], a.y[i], a.z[i]);
      this._s.set(scale, scale, scale);
      m.compose(this._v, q, this._s);
      M.set(m.elements, i * 16);
    }

    const live = this.aliveCount > 0;
    this.mesh.visible = live;
    this.mesh.count = live ? hi + 1 : 0;
    if (live || this._wasLive) {
      this.mesh.instanceMatrix.needsUpdate = true;
      this.cAttr.needsUpdate = true;
      this.pAttr.needsUpdate = true;
    }
    this._wasLive = live;
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

  dispose() {
    this.mesh.geometry.dispose();
    this.mesh.material.dispose();
  }
}
