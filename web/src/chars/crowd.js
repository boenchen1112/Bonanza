/**
 * The crowd.  [chars agent owns this directory]
 *
 * Hundreds of figures in the stands, bouncing on the beat, in **one draw
 * call**. A single InstancedMesh, one merged low-poly figure geometry, and a
 * per-frame matrix write that goes straight into the instance buffer's
 * Float32Array — no Object3D, no Matrix4.compose(), no allocation.
 *
 * Why a crowd matters more than it looks: it is the cheapest possible way to
 * make the *whole screen* agree with the music. The player watches the middle
 * of the frame; the crowd fills the periphery with unambiguous, low-frequency
 * beat information, so the beat is legible even to someone who is not looking
 * at the beat. And it is the only element that can react to a combo without
 * ever competing with the thing the player must read, because it lives behind
 * the action and never changes colour suddenly.
 *
 * It reacts:
 *   - `hype(x)`   — a combo milestone. Everyone jumps higher, faster, longer.
 *   - `deflate()` — a miss. The stands sag for a beat, then recover.
 *   - `wave()`    — a Mexican wave that travels along the arc.
 */

import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { clamp, clamp01, damp, makeRng, smootherstep, beatPhase } from '../core/util.js';

/** Crowd palette — desaturated relative to the cast, so it never competes. */
const CROWD_COLORS = [
  0xff7a5c, 0x6fc9e8, 0xd98ae0, 0xa9d98a, 0xffc86a,
  0x8a95e0, 0xff9ab5, 0x9aa6bd, 0xffe08a, 0x7fe0c8,
];

/** Tint every vertex of `geo` a flat color — merged geometries must all carry
 * the same attributes, so the body/head/arms need a (white, i.e. no-op)
 * `color` attribute too once the eyes below introduce one. */
function tintGeometry(geo, r, g, b) {
  const n = geo.attributes.position.count;
  const arr = new Float32Array(n * 3);
  for (let i = 0; i < n; i++) { arr[i * 3] = r; arr[i * 3 + 1] = g; arr[i * 3 + 2] = b; }
  geo.setAttribute('color', new THREE.BufferAttribute(arr, 3));
  return geo;
}

/**
 * One spectator: body, head, two stubby arms held out in a low V, and two
 * eye-bumps. ~190 triangles. The arms and eyes are baked, not articulated —
 * at this size the silhouette does the work and articulation would cost the
 * single draw call. The eyes are dark regardless of this spectator's body
 * color: a per-vertex `color` attribute (white everywhere except the eye
 * bumps) multiplies with the per-instance tint in the shader, so two tiny
 * geometric bumps read as a face instead of just two same-colored lumps —
 * from the stands' typical viewing distance that's enough to stop reading
 * as a blank silhouette, without a texture, a second material, or a second
 * draw call.
 */
function figureGeometry() {
  const parts = [];
  const body = new THREE.CapsuleGeometry(0.17, 0.30, 2, 7);
  body.translate(0, 0.30, 0);
  parts.push(tintGeometry(body, 1, 1, 1));

  const head = new THREE.SphereGeometry(0.155, 8, 6);
  head.translate(0, 0.62, 0);
  parts.push(tintGeometry(head, 1, 1, 1));

  for (const sx of [-1, 1]) {
    const arm = new THREE.CapsuleGeometry(0.055, 0.26, 2, 5);
    arm.rotateZ(sx * 0.72);
    arm.translate(sx * 0.20, 0.42, 0);
    parts.push(tintGeometry(arm, 1, 1, 1));
  }

  // Eyes: local +Z is "forward" (facing=0 leaves the per-instance rotation
  // near-identity, matching this codebase's "character faces +Z" convention
  // — see chars/rig.js), just above head centre, either side of the seam.
  for (const sx of [-1, 1]) {
    const eye = new THREE.SphereGeometry(0.026, 6, 5);
    eye.scale(1, 1, 0.6);
    eye.translate(sx * 0.052, 0.655, 0.148);
    parts.push(tintGeometry(eye, 0.12, 0.12, 0.15));
  }

  return mergeGeometries(parts, false) || body;
}

/**
 * @param {object} [opts]
 * @param {number} [opts.count]     spectators (default 352)
 * @param {number} [opts.rows]      tiers
 * @param {number} [opts.radius]    inner tier radius
 * @param {number} [opts.arc]       angular span, radians
 * @param {number} [opts.seed]
 * @param {[number,number,number]} [opts.center]
 */
export function makeCrowd({
  rows = 7, perRow = 44, radius = 8.5, rowDepth = 1.15, rowRise = 0.78,
  arc = Math.PI * 1.05, seed = 0xc0ffee, center = [0, 0, 0], scale = 1,
  colors = CROWD_COLORS,
} = {}) {
  const rng = makeRng(seed);

  // ---- layout ------------------------------------------------------------
  let n = 0;
  for (let r = 0; r < rows; r++) n += perRow + r * 3;

  const geo = figureGeometry();
  const mat = new THREE.MeshLambertMaterial({ color: 0xffffff, vertexColors: true });
  const mesh = new THREE.InstancedMesh(geo, mat, n);
  mesh.name = 'crowd';
  mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
  mesh.frustumCulled = false; // one call either way; culling a 350-strong arc
  mesh.castShadow = false;    // by its bounding box only ever costs us pops
  mesh.receiveShadow = false;

  const colAttr = new THREE.InstancedBufferAttribute(new Float32Array(n * 3), 3);
  mesh.instanceColor = colAttr;

  // Per-instance static data, in flat typed arrays: cache-friendly and, more
  // importantly, allocation-free at update time.
  const px = new Float32Array(n);
  const py = new Float32Array(n);
  const pz = new Float32Array(n);
  const cosF = new Float32Array(n);   // facing (toward the stage)
  const sinF = new Float32Array(n);
  const phase = new Float32Array(n);  // beat offset, so nobody is in lockstep
  const uPos = new Float32Array(n);   // 0..1 along the arc — drives the wave
  const size = new Float32Array(n);
  const wob = new Float32Array(n);    // per-instance bounce amplitude

  const c = new THREE.Color();
  let k = 0;
  for (let r = 0; r < rows; r++) {
    const cnt = perRow + r * 3;
    const rad = radius + r * rowDepth;
    const y = center[1] + r * rowRise;
    for (let i = 0; i < cnt; i++) {
      const u = cnt > 1 ? i / (cnt - 1) : 0.5;
      // Jitter the angle so rows don't read as a grid, but keep it seeded.
      const a = (u - 0.5) * arc + (rng() - 0.5) * (arc / cnt) * 0.8;
      const rr = rad + (rng() - 0.5) * 0.3;
      px[k] = center[0] + Math.sin(a) * rr;
      pz[k] = center[2] - Math.cos(a) * rr;
      py[k] = y + (rng() - 0.5) * 0.06;
      const facing = -a + (rng() - 0.5) * 0.25;
      cosF[k] = Math.cos(facing);
      sinF[k] = Math.sin(facing);
      // Phase: mostly on the beat (a crowd claps together) with enough spread
      // that it looks like people rather than a single animated texture.
      phase[k] = (rng() - 0.5) * 0.17 + (rng() < 0.08 ? rng() * 0.6 : 0);
      uPos[k] = u;
      size[k] = (0.86 + rng() * 0.34) * scale;
      wob[k] = 0.7 + rng() * 0.6;
      c.setHex(colors[(rng() * colors.length) | 0]);
      // Back rows read darker: cheap aerial perspective, and it keeps the
      // stands from flattening into one bright wall behind the cast.
      const dim = 1 - r * 0.055;
      colAttr.setXYZ(k, c.r * dim, c.g * dim, c.b * dim);
      k++;
    }
  }
  colAttr.needsUpdate = true;

  // ---- reactive state ----------------------------------------------------
  const st = {
    energy: 0.45, energyTarget: 0.45,
    hype: 0, sag: 0,
    waveT: -1, waveSpeed: 1.6, waveAmt: 0,
    beat: 0,
  };

  const arr = mesh.instanceMatrix.array;

  function hop(p) { return 4 * p * (1 - p); }

  /**
   * @param {number} dt
   * @param {number} beat float beat
   */
  function update(dt, beat) {
    st.beat = beat;
    st.energy = damp(st.energy, clamp01(st.energyTarget + st.hype), 3.2, dt);
    st.hype = damp(st.hype, 0, 0.9, dt);
    st.sag = damp(st.sag, 0, 2.6, dt);
    if (st.waveT >= 0) {
      st.waveT += dt * st.waveSpeed;
      if (st.waveT > 2.2) { st.waveT = -1; st.waveAmt = 0; }
    }

    const e = st.energy;
    const sag = st.sag;
    // Energy raises the jump AND the rate: a hyped crowd bounces on eighths.
    const rate = 1 + (e > 0.72 ? 1 : 0) * smootherstep((e - 0.72) / 0.28);
    const jumpH = (0.10 + e * 0.42) * (1 - sag * 0.9);
    const leanA = 0.10 + e * 0.16;
    const waveOn = st.waveT >= 0;
    const wt = st.waveT;
    const wAmt = st.waveAmt;

    for (let i = 0; i < n; i++) {
      const b = (beat + phase[i]) * rate;
      let f = beatPhase(b);
      let j = hop(f) * wob[i];

      // Travelling wave: a moving gaussian in arc position.
      if (waveOn) {
        const d = uPos[i] - (wt * 0.85 - 0.15);
        const g = Math.exp(-(d * d) / 0.006);
        j += g * wAmt * 1.9;
      }

      const lift = j * jumpH;
      // Squash/stretch: flatter at the bottom, taller at the top. Even at 350
      // instances this is what makes them read as bodies and not as capsules.
      const sq = 1 + (j - 0.42) * 0.30 - sag * 0.35;
      const sxz = 1 / Math.sqrt(Math.max(0.3, sq));
      const s = size[i];
      const sy = sq * s;
      const sh = sxz * s;

      // sway: a small Z-lean, alternating on a two-beat cycle
      const lean = Math.sin(b * Math.PI) * leanA;
      const cz = Math.cos(lean), sz = Math.sin(lean);
      const cy = cosF[i], sy2 = sinF[i];

      // M = T * Ry(facing) * Rz(lean) * S, written straight into the buffer.
      const o = i * 16;
      arr[o + 0] = cy * cz * sh;
      arr[o + 1] = sz * sh;
      arr[o + 2] = -sy2 * cz * sh;
      arr[o + 3] = 0;
      arr[o + 4] = -cy * sz * sy;
      arr[o + 5] = cz * sy;
      arr[o + 6] = sy2 * sz * sy;
      arr[o + 7] = 0;
      arr[o + 8] = sy2 * sh;
      arr[o + 9] = 0;
      arr[o + 10] = cy * sh;
      arr[o + 11] = 0;
      arr[o + 12] = px[i];
      arr[o + 13] = py[i] + lift - sag * 0.22;
      arr[o + 14] = pz[i];
      arr[o + 15] = 1;
    }
    mesh.instanceMatrix.needsUpdate = true;
  }

  return {
    mesh,
    get count() { return n; },
    /** Always 1. If this ever isn't 1, something upstream broke. */
    get drawCalls() { return 1; },

    /** Baseline excitement, 0..1. Raise it as a round escalates. */
    setEnergy(v) { st.energyTarget = clamp01(v); },
    get energy() { return st.energy; },

    /** Combo milestone: go wild, and send a wave for the big ones. */
    hype(amount = 0.6) {
      st.hype = Math.max(st.hype, clamp(amount, 0, 1.2));
      st.sag = 0;
      if (amount >= 0.55) this.wave(Math.min(1, amount));
    },

    /** A miss. The stands sag — silence you can see. */
    deflate(amount = 1) {
      st.sag = Math.max(st.sag, clamp01(amount));
      st.hype = 0;
      st.energy = Math.min(st.energy, 0.30);
    },

    /** Travelling wave through the stands. */
    wave(amount = 1, speed = 1.6) {
      st.waveT = 0;
      st.waveAmt = clamp01(amount);
      st.waveSpeed = speed;
    },

    update,

    dispose() {
      mesh.removeFromParent();
      geo.dispose();
      mat.dispose();
      mesh.dispose();
    },
  };
}
