/**
 * The conducting trace.
 *
 * The windup is invisible otherwise: a button held down is not a gesture. So
 * the bat tip leaves a ribbon behind it for the WHOLE coil — not a rolling
 * 300ms tail like `fx.trail()`, which is tuned for fast motion and is far too
 * short to show a two-beat arc.
 *
 * Two things make the shape read as *conducting* rather than as a smear:
 *
 *  1. It keeps the entire path from the moment of the press. As the hold gets
 *     longer the rig's coil gets deeper, so the ribbon grows from a stub into
 *     a long sweeping curve. The player can literally see their gesture get
 *     better, which is the design's whole reason for existing.
 *  2. Colour and width track POWER, not time. Cold and thin is a bunt; hot,
 *     wide and haloed is a home run. Colour tells you the tier before the ball
 *     is even hit, so the two axes are separable by eye during the swing.
 *
 * One draw call, one geometry, zero per-frame allocation.
 */

import * as THREE from 'three';
import { clamp01, lerp } from '../../core/util.js';

const MAX_SAMPLES = 96;
const BASE_RATE = 1 / 45; // seconds between samples before decimation

/** Cold -> warm -> white-hot, matching the tier thresholds in rules.js. */
const RAMP = [
  [0.00, 0x2fb8ff],
  [0.28, 0x5ce1ff],
  [0.55, 0xffd93d],
  [0.72, 0xff9a3a],
  [1.00, 0xfff3cf],
];

function rampColor(out, t) {
  t = clamp01(t);
  for (let i = 1; i < RAMP.length; i++) {
    if (t <= RAMP[i][0] || i === RAMP.length - 1) {
      const a = RAMP[i - 1];
      const b = RAMP[i];
      const u = b[0] === a[0] ? 0 : (t - a[0]) / (b[0] - a[0]);
      out.setHex(a[1]).lerp(_TMPC.setHex(b[1]), clamp01(u));
      return out;
    }
  }
  return out.setHex(RAMP[0][1]);
}
const _TMPC = new THREE.Color();

export function createTrace() {
  const positions = new Float32Array(MAX_SAMPLES * 2 * 3);
  const colors = new Float32Array(MAX_SAMPLES * 2 * 4);
  const index = new Uint16Array((MAX_SAMPLES - 1) * 6);
  for (let s = 0; s < MAX_SAMPLES - 1; s++) {
    const a = s * 2, b = a + 1, c = a + 2, d = a + 3;
    const o = s * 6;
    index[o] = a; index[o + 1] = b; index[o + 2] = c;
    index[o + 3] = b; index[o + 4] = d; index[o + 5] = c;
  }

  const geo = new THREE.BufferGeometry();
  const pAttr = new THREE.BufferAttribute(positions, 3).setUsage(THREE.DynamicDrawUsage);
  const cAttr = new THREE.BufferAttribute(colors, 4).setUsage(THREE.DynamicDrawUsage);
  geo.setAttribute('position', pAttr);
  geo.setAttribute('color', cAttr);
  geo.setIndex(new THREE.BufferAttribute(index, 1));
  geo.setDrawRange(0, 0);

  const mat = new THREE.MeshBasicMaterial({
    vertexColors: true, transparent: true, depthWrite: false,
    blending: THREE.AdditiveBlending, side: THREE.DoubleSide, toneMapped: false,
  });

  const mesh = new THREE.Mesh(geo, mat);
  mesh.name = 'swing:trace';
  mesh.frustumCulled = false;
  mesh.renderOrder = 12;
  mesh.visible = false;
  mesh.userData.keepMaterial = true;

  // sample ring (linear, not circular — we keep the whole gesture)
  const sx = new Float32Array(MAX_SAMPLES);
  const sy = new Float32Array(MAX_SAMPLES);
  const sz = new Float32Array(MAX_SAMPLES);
  let n = 0;
  let acc = 0;
  let rate = BASE_RATE;

  let live = false;
  let fade = 0;          // 1 while alive, drains after release
  let power = 0;
  let width = 0.07;
  const col = new THREE.Color();

  const camF = new THREE.Vector3();
  const seg = new THREE.Vector3();
  const perp = new THREE.Vector3();

  /** Halve the sample density in place when the buffer fills. */
  function decimate() {
    let w = 0;
    for (let i = 0; i < n; i += 2) { sx[w] = sx[i]; sy[w] = sy[i]; sz[w] = sz[i]; w++; }
    n = w;
    rate *= 2;
  }

  function begin(x, y, z) {
    n = 0; acc = 0; rate = BASE_RATE;
    live = true; fade = 1; power = 0;
    sx[n] = x; sy[n] = y; sz[n] = z; n++;
    mesh.visible = true;
  }

  /** Feed the head position. `p` is current power 0..1. */
  function push(dt, x, y, z, p) {
    if (!live) return;
    power = p;
    acc += dt;
    if (acc >= rate) {
      acc = 0;
      if (n >= MAX_SAMPLES) decimate();
      sx[n] = x; sy[n] = y; sz[n] = z; n++;
    } else if (n > 0) {
      // keep the head glued to the object between samples
      sx[n - 1] = x; sy[n - 1] = y; sz[n - 1] = z;
    }
  }

  /** Stop feeding; the ribbon holds its shape and dissolves. */
  function release() { live = false; }

  function clear() { live = false; fade = 0; n = 0; mesh.visible = false; geo.setDrawRange(0, 0); }

  function update(dt, camera) {
    if (!live) {
      if (fade > 0) fade = Math.max(0, fade - dt * 2.6);
      if (fade <= 0) { if (mesh.visible) { mesh.visible = false; geo.setDrawRange(0, 0); } return; }
    }
    if (n < 2) { geo.setDrawRange(0, 0); return; }

    if (camera) camF.set(0, 0, -1).applyQuaternion(camera.quaternion);
    else camF.set(0, 0, -1);

    rampColor(col, power);
    const w = width * (0.55 + power * 1.25) * (0.55 + fade * 0.45);

    for (let i = 0; i < n; i++) {
      const i0 = Math.max(0, i - 1);
      const i1 = Math.min(n - 1, i + 1);
      seg.set(sx[i1] - sx[i0], sy[i1] - sy[i0], sz[i1] - sz[i0]);
      if (seg.lengthSq() < 1e-9) seg.set(0, 1, 0);
      perp.copy(seg).cross(camF);
      if (perp.lengthSq() < 1e-9) perp.set(0, 1, 0);
      perp.normalize();

      // Taper: the tail of the gesture is thin and faint, the head is full.
      const u = n > 1 ? i / (n - 1) : 1;
      const taper = 0.20 + 0.80 * u * u;
      const hw = w * taper;

      const o = i * 6;
      positions[o] = sx[i] + perp.x * hw;
      positions[o + 1] = sy[i] + perp.y * hw;
      positions[o + 2] = sz[i] + perp.z * hw;
      positions[o + 3] = sx[i] - perp.x * hw;
      positions[o + 4] = sy[i] - perp.y * hw;
      positions[o + 5] = sz[i] - perp.z * hw;

      const a = (0.10 + 0.90 * u) * fade * (0.45 + power * 0.75);
      const c = i * 8;
      colors[c] = col.r; colors[c + 1] = col.g; colors[c + 2] = col.b; colors[c + 3] = a;
      colors[c + 4] = col.r; colors[c + 5] = col.g; colors[c + 6] = col.b; colors[c + 7] = a;
    }

    pAttr.needsUpdate = true;
    cAttr.needsUpdate = true;
    geo.setDrawRange(0, (n - 1) * 6);
    mesh.visible = true;
  }

  return {
    mesh,
    begin, push, release, clear, update,
    get sampleCount() { return n; },
    setWidth(v) { width = v; },
    dispose() { geo.dispose(); mat.dispose(); mesh.removeFromParent(); },
  };
}
