/**
 * In-world callouts.  [render agent owns this directory]
 *
 * The DOM HUD owns the screen-space popup; this is the 3D variant, for text
 * that must belong to a PLACE — the callout that erupts out of the ball you
 * just hit, at the depth you hit it, occluded by nothing and parallaxing with
 * the camera. A screen-space popup says "the game scored you". A world-space
 * one says "that thing, right there, just happened."
 *
 * The vocabulary is fixed and baked into one atlas at boot, so a callout costs
 * a matrix write and nothing else. One draw call for every word on screen.
 */

import * as THREE from 'three';
import { backOut, clamp01 } from '../../core/util.js';
import { wordAtlas } from './textures.js';

const VERT = /* glsl */`
attribute vec4 iCData;
attribute vec4 iUv;
varying vec4 vCD;
varying vec2 vAUv;
void main() {
  vCD = iCData;
  vAUv = uv * iUv.zw + iUv.xy;
  gl_Position = projectionMatrix * modelViewMatrix * instanceMatrix * vec4(position, 1.0);
}`;

const FRAG = /* glsl */`
precision mediump float;
uniform sampler2D uMap;
varying vec4 vCD;
varying vec2 vAUv;
void main() {
  vec4 t = texture2D(uMap, vAUv);
  float a = t.a * vCD.a;
  if (a < 0.01) discard;
  // t.rgb is 1 in the fill and 0 in the outline, so the tint colours the fill
  // and leaves the outline black. See textures.js.
  gl_FragColor = vec4(t.rgb * vCD.rgb, a);
  #include <colorspace_fragment>
}`;

export class PopTextPool {
  constructor({ max = 24, words }) {
    this.max = max;
    const { texture, words: map } = wordAtlas(words);
    this.words = map;

    const geo = new THREE.PlaneGeometry(1, 1);
    const mat = new THREE.ShaderMaterial({
      vertexShader: VERT,
      fragmentShader: FRAG,
      uniforms: { uMap: { value: texture } },
      transparent: true,
      depthWrite: false,
      depthTest: false, // callouts are information; never let geometry eat them
      side: THREE.DoubleSide,
    });
    const mesh = new THREE.InstancedMesh(geo, mat, max);
    mesh.frustumCulled = false;
    mesh.renderOrder = 30;
    mesh.visible = false;
    mesh.count = 0;
    mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
    this.mesh = mesh;
    this.matArr = mesh.instanceMatrix.array;

    const c = new THREE.InstancedBufferAttribute(new Float32Array(max * 4), 4);
    const u = new THREE.InstancedBufferAttribute(new Float32Array(max * 4), 4);
    c.setUsage(THREE.DynamicDrawUsage);
    geo.setAttribute('iCData', c);
    geo.setAttribute('iUv', u);
    this.cAttr = c; this.uAttr = u;
    this.cArr = c.array; this.uArr = u.array;

    const F = (n) => new Float32Array(n);
    this.a = {
      x: F(max), y: F(max), z: F(max),
      vy: F(max), life: F(max), maxLife: F(max),
      size: F(max), aspect: F(max), tilt: F(max), spin: F(max),
      r: F(max), g: F(max), b: F(max),
      rise: F(max),
    };

    this.free = new Int32Array(max);
    for (let i = 0; i < max; i++) this.free[i] = max - 1 - i;
    this.freeCount = max;
    this.aliveIdx = new Int32Array(max);
    this.aliveCount = 0;
  }

  get object3d() { return this.mesh; }
  has(word) { return this.words.has(word); }

  _alloc() {
    let s;
    if (this.freeCount > 0) s = this.free[--this.freeCount];
    else { s = this.aliveIdx[0]; this.aliveIdx[0] = this.aliveIdx[--this.aliveCount]; }
    this.aliveIdx[this.aliveCount++] = s;
    return s;
  }

  /**
   * @param {string} word must be in the boot vocabulary; unknown words no-op
   * @param {number[]|THREE.Vector3} pos
   */
  spawn(word, pos, {
    color = 0xffffff, size = 0.62, life = 0.72, rise = 1.5, tilt = 0, spin = 0,
  } = {}) {
    const w = this.words.get(word);
    if (!w) return -1;
    const i = this._alloc();
    const a = this.a;
    if (Array.isArray(pos)) { a.x[i] = pos[0]; a.y[i] = pos[1]; a.z[i] = pos[2]; }
    else { a.x[i] = pos.x; a.y[i] = pos.y; a.z[i] = pos.z; }
    a.life[i] = 0; a.maxLife[i] = life;
    a.size[i] = size; a.aspect[i] = w.aspect;
    a.rise[i] = rise;
    a.tilt[i] = tilt; a.spin[i] = spin;
    a.r[i] = ((color >> 16) & 255) / 255;
    a.g[i] = ((color >> 8) & 255) / 255;
    a.b[i] = (color & 255) / 255;
    const u4 = i * 4;
    this.uArr[u4] = w.rect[0]; this.uArr[u4 + 1] = w.rect[1];
    this.uArr[u4 + 2] = w.rect[2]; this.uArr[u4 + 3] = w.rect[3];
    this.uAttr.needsUpdate = true;
    return i;
  }

  update(dt, cam) {
    const a = this.a;
    const M = this.matArr;
    const C = this.cArr;

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
      // overshoot in, hold, then shrink+fade: the classic pop, and the reason
      // it reads at speed is that the scale peaks BEFORE the eye arrives.
      const grow = t < 0.34 ? backOut(t / 0.34, 2.6) : 1 + 0.10 * Math.sin((t - 0.34) * 7.5);
      const shrink = t > 0.78 ? 1 - (t - 0.78) / 0.22 * 0.45 : 1;
      const sc = a.size[i] * grow * shrink;
      // decelerating rise — energy is spent early, like everything thrown
      const y = a.y[i] + a.rise[i] * (1 - (1 - t) * (1 - t)) * 0.55;
      const alpha = t < 0.08 ? t / 0.08 : t > 0.7 ? 1 - (t - 0.7) / 0.3 : 1;

      const c4 = i * 4;
      C[c4] = a.r[i]; C[c4 + 1] = a.g[i]; C[c4 + 2] = a.b[i]; C[c4 + 3] = alpha;

      const rot = a.tilt[i] + a.spin[i] * a.life[i];
      const c = Math.cos(rot), s = Math.sin(rot);
      const w = sc * a.aspect[i];
      const o = i * 16;
      M[o] = (rx * c + ux * s) * w; M[o + 1] = (ry * c + uy * s) * w; M[o + 2] = (rz * c + uz * s) * w; M[o + 3] = 0;
      M[o + 4] = (-rx * s + ux * c) * sc; M[o + 5] = (-ry * s + uy * c) * sc; M[o + 6] = (-rz * s + uz * c) * sc; M[o + 7] = 0;
      M[o + 8] = fx * sc; M[o + 9] = fy * sc; M[o + 10] = fz * sc; M[o + 11] = 0;
      M[o + 12] = a.x[i]; M[o + 13] = y; M[o + 14] = a.z[i]; M[o + 15] = 1;
    }

    const live = this.aliveCount > 0;
    this.mesh.visible = live;
    this.mesh.count = live ? hi + 1 : 0;
    if (live || this._wasLive) {
      this.mesh.instanceMatrix.needsUpdate = true;
      this.cAttr.needsUpdate = true;
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
