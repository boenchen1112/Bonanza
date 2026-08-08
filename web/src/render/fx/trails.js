/**
 * Ribbon trails.  [render agent owns this directory]
 *
 * A bat, a ball, a thrown drumstick: the thing the player is tracking must
 * leave a readable arc, or fast motion turns into teleportation. Swing Kings'
 * bat and Bounce Brigade's ball both need this.
 *
 * All trails live in ONE BufferGeometry (one draw call). Each trail owns a
 * fixed span of vertices: SEG samples x 2 vertices, indexed once at boot.
 * Per frame we rewrite only the spans of active trails.
 *
 * Sampling is time-based, not frame-based: history advances at a fixed rate
 * (`sampleHz`) with linear interpolation of the head position, so a 144Hz
 * machine and a 45Hz machine draw the same length of ribbon. Frame-based
 * sampling is the classic reason trails look different on every device.
 */

import * as THREE from 'three';
import { clamp01 } from '../../core/util.js';

const VERT = /* glsl */`
attribute vec4 aCol;
varying vec4 vCol;
void main() {
  vCol = aCol;
  gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
}`;

const FRAG = /* glsl */`
precision mediump float;
varying vec4 vCol;
void main() {
  if (vCol.a < 0.004) discard;
  gl_FragColor = vec4(vCol.rgb, vCol.a);
  #include <colorspace_fragment>
}`;

export class TrailSystem {
  constructor({ max = 8, segs = 28, sampleHz = 90 }) {
    this.max = max;
    this.segs = segs;
    this.sampleDt = 1 / sampleHz;

    const vcount = max * segs * 2;
    const pos = new Float32Array(vcount * 3);
    const col = new Float32Array(vcount * 4);
    const idx = new Uint16Array(max * (segs - 1) * 6);
    for (let t = 0; t < max; t++) {
      const base = t * segs * 2;
      const io = t * (segs - 1) * 6;
      for (let s = 0; s < segs - 1; s++) {
        const a = base + s * 2, b = a + 1, c = a + 2, d = a + 3;
        const o = io + s * 6;
        idx[o] = a; idx[o + 1] = b; idx[o + 2] = c;
        idx[o + 3] = b; idx[o + 4] = d; idx[o + 5] = c;
      }
    }

    const geo = new THREE.BufferGeometry();
    const pAttr = new THREE.BufferAttribute(pos, 3).setUsage(THREE.DynamicDrawUsage);
    const cAttr = new THREE.BufferAttribute(col, 4).setUsage(THREE.DynamicDrawUsage);
    geo.setAttribute('position', pAttr);
    geo.setAttribute('aCol', cAttr);
    geo.setIndex(new THREE.BufferAttribute(idx, 1));

    const mat = new THREE.ShaderMaterial({
      vertexShader: VERT, fragmentShader: FRAG,
      transparent: true, depthWrite: false,
      blending: THREE.AdditiveBlending, side: THREE.DoubleSide,
    });
    const mesh = new THREE.Mesh(geo, mat);
    mesh.frustumCulled = false;
    mesh.renderOrder = 9;
    mesh.visible = false;

    this.mesh = mesh;
    this.pos = pos;
    this.col = col;
    this.pAttr = pAttr;
    this.cAttr = cAttr;

    // per-trail state, all preallocated
    this.hist = new Float32Array(max * segs * 3); // ring of sampled points
    this.headX = new Float32Array(max);
    this.headY = new Float32Array(max);
    this.headZ = new Float32Array(max);
    this.prevX = new Float32Array(max);
    this.prevY = new Float32Array(max);
    this.prevZ = new Float32Array(max);
    this.count = new Int32Array(max);   // how many samples recorded (<= segs)
    this.acc = new Float32Array(max);
    this.width = new Float32Array(max);
    this.r = new Float32Array(max);
    this.g = new Float32Array(max);
    this.b = new Float32Array(max);
    this.fade = new Float32Array(max);  // 1 while held, drains after release
    this.active = new Uint8Array(max);
    this.dying = new Uint8Array(max);
    this.moved = new Uint8Array(max);

    // reusable handles, so `fx.trail()` allocates nothing after boot
    this.handles = [];
    for (let i = 0; i < max; i++) this.handles.push(makeHandle(this, i));

    this._camF = new THREE.Vector3();
  }

  get object3d() { return this.mesh; }

  /** Acquire a trail. Returns null if all are in use (never throws mid-beat). */
  acquire({ color = 0xffffff, width = 0.12 } = {}) {
    for (let i = 0; i < this.max; i++) {
      if (this.active[i]) continue;
      this.active[i] = 1;
      this.dying[i] = 0;
      this.count[i] = 0;
      this.acc[i] = 0;
      this.fade[i] = 1;
      this.moved[i] = 0;
      this.width[i] = width;
      this.r[i] = ((color >> 16) & 255) / 255;
      this.g[i] = ((color >> 8) & 255) / 255;
      this.b[i] = (color & 255) / 255;
      return this.handles[i];
    }
    return null;
  }

  _release(i) {
    if (!this.active[i]) return;
    this.dying[i] = 1;
  }

  _clearSpan(i) {
    const base = i * this.segs * 2;
    const C = this.col;
    for (let v = 0; v < this.segs * 2; v++) C[(base + v) * 4 + 3] = 0;
  }

  update(dt, cam) {
    const segs = this.segs;
    let any = false;

    if (cam) this._camF.set(0, 0, -1).applyQuaternion(cam.quaternion);
    const cfx = this._camF.x, cfy = this._camF.y, cfz = this._camF.z;

    for (let i = 0; i < this.max; i++) {
      if (!this.active[i]) continue;

      if (this.dying[i]) {
        this.fade[i] -= dt * 4.5; // ~220ms tail after release
        if (this.fade[i] <= 0) {
          this.active[i] = 0; this.dying[i] = 0;
          this._clearSpan(i);
          continue;
        }
      }

      // advance history at a fixed rate, interpolating toward the head
      if (dt > 0 && this.moved[i]) {
        this.acc[i] += dt;
        let steps = 0;
        while (this.acc[i] >= this.sampleDt && steps < 4) {
          this.acc[i] -= this.sampleDt;
          steps++;
          const u = 1 - this.acc[i] / Math.max(dt, 1e-5);
          const px = this.prevX[i] + (this.headX[i] - this.prevX[i]) * clamp01(u);
          const py = this.prevY[i] + (this.headY[i] - this.prevY[i]) * clamp01(u);
          const pz = this.prevZ[i] + (this.headZ[i] - this.prevZ[i]) * clamp01(u);
          this._push(i, px, py, pz);
        }
        this.prevX[i] = this.headX[i];
        this.prevY[i] = this.headY[i];
        this.prevZ[i] = this.headZ[i];
      }

      const n = this.count[i];
      if (n < 2) { this._clearSpan(i); continue; }
      any = true;

      const H = this.hist;
      const P = this.pos;
      const C = this.col;
      const hb = i * segs * 3;
      const vb = i * segs * 2;
      const w = this.width[i];
      const rr = this.r[i], gg = this.g[i], bb = this.b[i];
      const glob = this.fade[i];

      for (let s = 0; s < segs; s++) {
        const si = Math.min(s, n - 1);
        const o = hb + si * 3;
        const x = H[o], y = H[o + 1], z = H[o + 2];
        // tangent from the neighbouring sample
        const j = Math.min(si + 1, n - 1);
        const o2 = hb + j * 3;
        let tx = H[o2] - x, ty = H[o2 + 1] - y, tz = H[o2 + 2] - z;
        if (si === j && si > 0) {
          const o3 = hb + (si - 1) * 3;
          tx = x - H[o3]; ty = y - H[o3 + 1]; tz = z - H[o3 + 2];
        }
        // side = tangent x cameraForward -> ribbon always faces the viewer
        let sx = ty * cfz - tz * cfy;
        let sy = tz * cfx - tx * cfz;
        let sz = tx * cfy - ty * cfx;
        const sl = Math.hypot(sx, sy, sz);
        if (sl > 1e-5) { sx /= sl; sy /= sl; sz /= sl; } else { sx = 1; sy = 0; sz = 0; }

        const age = s / (segs - 1);
        // width tapers to a point and alpha falls faster than width, so the
        // tail dissolves instead of stopping with a visible flat end
        const hw = w * (1 - age) * (1 - age * 0.35) * 0.5;
        const a = glob * (1 - age) * (1 - age) * (s < n ? 1 : 0);

        const v0 = (vb + s * 2) * 3;
        P[v0] = x + sx * hw; P[v0 + 1] = y + sy * hw; P[v0 + 2] = z + sz * hw;
        P[v0 + 3] = x - sx * hw; P[v0 + 4] = y - sy * hw; P[v0 + 5] = z - sz * hw;
        const c0 = (vb + s * 2) * 4;
        C[c0] = rr; C[c0 + 1] = gg; C[c0 + 2] = bb; C[c0 + 3] = a;
        C[c0 + 4] = rr; C[c0 + 5] = gg; C[c0 + 6] = bb; C[c0 + 7] = a;
      }
    }

    this.mesh.visible = any;
    if (any || this._wasAny) {
      this.pAttr.needsUpdate = true;
      this.cAttr.needsUpdate = true;
    }
    this._wasAny = any;
  }

  _push(i, x, y, z) {
    const segs = this.segs;
    const H = this.hist;
    const base = i * segs * 3;
    // shift toward the tail; segs is small (28) so memmove beats a ring index
    H.copyWithin(base + 3, base, base + (segs - 1) * 3);
    H[base] = x; H[base + 1] = y; H[base + 2] = z;
    if (this.count[i] < segs) this.count[i]++;
  }

  killAll() {
    for (let i = 0; i < this.max; i++) {
      this.active[i] = 0; this.dying[i] = 0; this.count[i] = 0;
      this._clearSpan(i);
    }
    this.mesh.visible = false;
    this.cAttr.needsUpdate = true;
  }

  dispose() {
    this.mesh.geometry.dispose();
    this.mesh.material.dispose();
  }
}

function makeHandle(sys, i) {
  return {
    get alive() { return !!sys.active[i] && !sys.dying[i]; },
    /** Move the trail head. Call once per frame with the object's position. */
    set(x, y, z) {
      if (!sys.active[i] || sys.dying[i]) return;
      if (typeof x === 'object') { z = x.z; y = x.y; x = x.x; }
      if (!sys.moved[i]) {
        sys.prevX[i] = x; sys.prevY[i] = y; sys.prevZ[i] = z;
        sys._push(i, x, y, z);
        sys.moved[i] = 1;
      }
      sys.headX[i] = x; sys.headY[i] = y; sys.headZ[i] = z;
    },
    setColor(hex) {
      sys.r[i] = ((hex >> 16) & 255) / 255;
      sys.g[i] = ((hex >> 8) & 255) / 255;
      sys.b[i] = (hex & 255) / 255;
    },
    setWidth(w) { sys.width[i] = w; },
    /** Stop feeding it; the tail fades out on its own. */
    release() { sys._release(i); },
  };
}
