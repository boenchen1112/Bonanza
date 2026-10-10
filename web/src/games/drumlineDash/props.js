/**
 * Drumline Dash — the set.
 *
 * Everything here is a prop the game owns and disposes. The *world* (sky,
 * ground disc, stands, bunting, floaters, lighting, grade) comes from the
 * stage's environment kit; this file only adds the things that carry gameplay
 * meaning and therefore cannot be generic:
 *
 *   TRACK    the field scrolls, the band marches in place. Absolute speed is
 *            in the ground, relative standing is in the marchers' X. That way
 *            the camera never has to chase anyone and the whole race stays in
 *            one framing for 75 seconds.
 *   RACK     five tuned drums in front of the drum major. Heads light as he
 *            plays — but see `lightHead`: the light is deliberately smeared in
 *            time so that watching it is worse than listening.
 *   BEAMS    two spotlights, magenta over the drum major, gold over you.
 *            Exactly one is ever lit. This is the primary "whose turn is it"
 *            signal and it needs no text to read.
 *   FINISH   the gate, invisible for seventy seconds, arriving exactly as the
 *            finale resolves.
 *
 * Draw-call budget for the set (track, rack, beams, finish): 6. Each maker disposes its own
 * geometry and materials; the game only has to call `dispose()`.
 */

import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { damp, easeOutCubic } from '../../core/util.js';

const _m4 = new THREE.Matrix4();
const _q = new THREE.Quaternion();
const _p = new THREE.Vector3();
const _s = new THREE.Vector3();
const _c = new THREE.Color();
const _c2 = new THREE.Color();

/** Non-indexed normalisation, so indexed and non-indexed prims can merge. */
function merge(parts) {
  if (!parts.length) return new THREE.BufferGeometry();
  if (parts.length === 1) return parts[0];
  const anyIndexed = parts.some((g) => g.index !== null);
  const anyDirect = parts.some((g) => g.index === null);
  const list = anyIndexed && anyDirect ? parts.map((g) => (g.index ? g.toNonIndexed() : g)) : parts;
  return mergeGeometries(list, false) || parts[0];
}

// ---------------------------------------------------------------- the track

/**
 * Yard lines + lane stripes.
 *
 * The lines recycle through a fixed window, so the field is infinite for zero
 * extra geometry, and `speed` is the only thing that says "we are moving". It
 * never reaches zero: a marching band that stops marching has stopped, and
 * this race is not over until it is over.
 */
export function makeTrack({ mats, lanes, laneWidth = 1.45, span = 30 }) {
  const group = new THREE.Group();
  group.name = 'dd:track';

  // Lane stripes and yard lines are one instanced draw: a unit ground quad
  // sized per instance, with the two families' opacities (0.18 / 0.5) carried
  // per instance. Stripes come first in the instance order, so the lines
  // still land on top of them, as they did as two draws.
  const L = lanes.length;
  const LINES = 11;
  const GAP = span / LINES;
  const geo = new THREE.PlaneGeometry(1, 1);
  geo.rotateX(-Math.PI / 2);
  const alpha = new THREE.InstancedBufferAttribute(new Float32Array(L + LINES), 1);
  geo.setAttribute('iAlpha', alpha);
  const mat = patchInstanceAlpha(mats.glow({
    color: 0xffffff, transparent: true, opacity: 1, depthWrite: false,
  }), 'dd-track-alpha');
  const mesh = new THREE.InstancedMesh(geo, mat, L + LINES);
  mesh.frustumCulled = false;
  mesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array((L + LINES) * 3), 3);
  mesh.renderOrder = 1;
  group.add(mesh);

  // --- lane stripes: instances 0..L-1 ---------------------------------------
  const laneGlow = new Float32Array(L);
  for (let i = 0; i < L; i++) {
    _p.set(0, 0.02 + i * 0.001, lanes[i]);
    _s.set(span, 1, laneWidth);
    mesh.setMatrixAt(i, _m4.compose(_p, _q.identity(), _s));
    alpha.setX(i, 0.18);
  }

  // --- yard lines: instances L..L+LINES-1 -----------------------------------
  const lineW = 0.17;
  const lineD = laneWidth * L + 2.4;
  const lineX = new Float32Array(LINES);
  const midZ = (lanes[0] + lanes[L - 1]) / 2;
  for (let i = 0; i < LINES; i++) {
    lineX[i] = -span / 2 + i * GAP;
    alpha.setX(L + i, 0.5);
    mesh.instanceColor.setXYZ(L + i, 1, 1, 1);
  }

  let scroll = 0;

  function update(dt, speed, beatPulse) {
    scroll += speed * dt;
    for (let i = 0; i < LINES; i++) {
      let x = lineX[i] - scroll;
      x = ((x + span / 2) % span + span) % span - span / 2;
      // Lines swell on the beat: the field itself keeps time in the periphery.
      _p.set(x, 0.03, midZ);
      _s.set(lineW * (1 + beatPulse * 0.55), 1, lineD);
      mesh.setMatrixAt(L + i, _m4.compose(_p, _q.identity(), _s));
    }
    mesh.instanceMatrix.needsUpdate = true;
    for (let i = 0; i < L; i++) laneGlow[i] = damp(laneGlow[i], 0, 4.5, dt);
  }

  /** Brighten one lane — whose turn it is, said on the ground itself. */
  function setLaneColors(colors, hot) {
    for (let i = 0; i < L; i++) {
      const k = 0.32 + laneGlow[i] * 1.5 + (i === hot ? 0.85 : 0);
      _c.setHex(colors[i]).multiplyScalar(k);
      mesh.instanceColor.setXYZ(i, _c.r, _c.g, _c.b);
    }
    mesh.instanceColor.needsUpdate = true;
  }

  function flashLane(i, amount = 1) {
    if (i >= 0 && i < L) laneGlow[i] = Math.max(laneGlow[i], amount);
  }

  function dispose() {
    group.removeFromParent();
    geo.dispose();
    mat.dispose();
    mesh.dispose();
  }

  return { group, update, setLaneColors, flashLane, dispose, materials: [mat] };
}

/** Per-instance opacity (`iAlpha`, a float per instance) on a basic material. */
function patchInstanceAlpha(mat, key) {
  mat.onBeforeCompile = (sh) => {
    sh.vertexShader = 'attribute float iAlpha;\nvarying float vIAlpha;\n'
      + sh.vertexShader.replace('void main() {', 'void main() {\n  vIAlpha = iAlpha;');
    sh.fragmentShader = 'varying float vIAlpha;\n'
      + sh.fragmentShader.replace('#include <color_fragment>', '#include <color_fragment>\n  diffuseColor.a *= vIAlpha;');
  };
  mat.customProgramCacheKey = () => key;
  return mat;
}

// ------------------------------------------------------------------ the rack

/**
 * The drum major's podium and his five tuned drums.
 *
 * ── Why the light is worse than the sound, on purpose ──────────────────────
 * `lightHead` is called ~70ms BEFORE the note and ramps up over ~90ms, so the
 * visual onset is a smear straddling the beat while the audio transient is
 * exact. Heads also light in a fixed left-to-right sweep rather than by pitch,
 * so *which* drum lit tells you nothing about *what* was played. A player who
 * only watches gets the count and the density and then drifts; a player who
 * listens locks in. That asymmetry is the reason this game exists in the set,
 * so it is deliberate, not a limitation.
 */
export function makeRack({ mats, count = 5, radius = 2.2, y = 1.95 }) {
  const group = new THREE.Group();
  group.name = 'dd:rack';

  // --- podium + rack frame, merged: one call for all the static metal -------
  const parts = [];
  const post = new THREE.CylinderGeometry(0.58, 0.78, 1.02, 14);
  post.translate(0, 0.51, 0);
  parts.push(post);
  const plate = new THREE.CylinderGeometry(0.94, 0.94, 0.14, 18);
  plate.translate(0, 1.07, 0);
  parts.push(plate);

  const angles = [];
  for (let i = 0; i < count; i++) {
    const a = -0.80 + (i / (count - 1)) * 1.60;
    angles.push(a);
    const x = Math.sin(a) * radius;
    const z = -Math.cos(a) * radius;
    const leg = new THREE.CylinderGeometry(0.055, 0.085, y, 6);
    leg.translate(x, y / 2, z);
    parts.push(leg);
  }
  const rail = new THREE.TorusGeometry(radius, 0.05, 5, 44, 1.76);
  rail.rotateX(Math.PI / 2);
  rail.rotateY(Math.PI - 0.88);
  rail.translate(0, y * 0.55, 0);
  parts.push(rail);

  const frameGeo = merge(parts);
  const frameMat = mats.toon({ color: 0x45134f, bands: 2, rim: 1.3, pulse: 0.18, name: 'ddRack' });
  const frame = new THREE.Mesh(frameGeo, frameMat);
  group.add(frame);

  // --- shells ---------------------------------------------------------------
  const shellGeo = new THREE.CylinderGeometry(0.46, 0.42, 0.42, 16);
  const shellMat = mats.toon({ color: 0xf3ecff, bands: 2, rim: 1.4, pulse: 0.2, name: 'ddShell' });
  const shells = new THREE.InstancedMesh(shellGeo, shellMat, count);
  shells.frustumCulled = false;
  group.add(shells);

  // --- heads: the light -----------------------------------------------------
  const headGeo = new THREE.CylinderGeometry(0.475, 0.475, 0.10, 18);
  const headMat = mats.glow({ color: 0xffffff, pulse: 0.4 });
  const heads = new THREE.InstancedMesh(headGeo, headMat, count);
  heads.frustumCulled = false;
  heads.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(count * 3), 3);
  group.add(heads);

  const pos = [];
  for (let i = 0; i < count; i++) {
    pos.push([Math.sin(angles[i]) * radius, y, -Math.cos(angles[i]) * radius]);
  }

  const lit = new Float32Array(count);
  const rise = new Float32Array(count);
  const hit = new Float32Array(count);

  function lightHead(i, power = 1) {
    const k = ((i % count) + count) % count;
    rise[k] = 0;
    lit[k] = Math.max(lit[k], 0.001);
    hit[k] = Math.max(hit[k], power);
  }

  function update(dt, dimColor, hotColor, active) {
    _c2.setHex(hotColor);
    for (let i = 0; i < count; i++) {
      if (lit[i] > 0) {
        rise[i] = Math.min(1, rise[i] + dt / 0.09);
        lit[i] = rise[i] < 1 ? easeOutCubic(rise[i]) : damp(lit[i], 0, 5.0, dt);
        if (lit[i] < 0.004 && rise[i] >= 1) lit[i] = 0;
      }
      hit[i] = damp(hit[i], 0, 13, dt);

      const b = lit[i];
      _c.setHex(dimColor).lerp(_c2, b);
      _c.multiplyScalar((active ? 0.6 : 0.18) + b * 2.6);
      heads.instanceColor.setXYZ(i, _c.r, _c.g, _c.b);

      const drop = hit[i] * 0.08;
      const wide = 1 + hit[i] * 0.16;
      _p.set(pos[i][0], pos[i][1] - drop, pos[i][2]);
      _s.set(wide, 1 - hit[i] * 0.3, wide);
      heads.setMatrixAt(i, _m4.compose(_p, _q.identity(), _s));
      _p.y = pos[i][1] - 0.26 - drop * 0.5;
      _s.set(1, 1, 1);
      shells.setMatrixAt(i, _m4.compose(_p, _q.identity(), _s));
    }
    heads.instanceColor.needsUpdate = true;
    heads.instanceMatrix.needsUpdate = true;
    shells.instanceMatrix.needsUpdate = true;
  }

  /** World position of head `i` — where a call flourish should live. */
  function headWorld(i, out = new THREE.Vector3()) {
    const k = ((i % count) + count) % count;
    return out.set(pos[k][0], pos[k][1], pos[k][2])
      .applyEuler(group.rotation).add(group.position);
  }

  function dispose() {
    group.removeFromParent();
    frameGeo.dispose(); shellGeo.dispose(); headGeo.dispose();
    frameMat.dispose(); shellMat.dispose(); headMat.dispose();
    shells.dispose(); heads.dispose();
  }

  return {
    group, lightHead, update, headWorld, dispose, count,
    materials: [frameMat, shellMat, headMat],
  };
}

// -------------------------------------------------------------------- beams

/** Vertical alpha ramp for a fake volumetric beam. Generated, never fetched. */
function beamTexture() {
  const c = document.createElement('canvas');
  c.width = 4; c.height = 64;
  const g = c.getContext('2d');
  const grad = g.createLinearGradient(0, 64, 0, 0); // canvas y=64 is uv.y = 0
  grad.addColorStop(0.00, 'rgba(255,255,255,0.66)');
  grad.addColorStop(0.26, 'rgba(255,255,255,0.26)');
  grad.addColorStop(0.74, 'rgba(255,255,255,0.16)');
  grad.addColorStop(1.00, 'rgba(255,255,255,0.60)');
  g.fillStyle = grad;
  g.fillRect(0, 0, 4, 64);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

/**
 * Two beams, one instanced draw. Exactly one is ever bright: `set(i, 1)` for
 * whoever's turn it is. Light is the loudest turn-taking cue available and it
 * costs the player nothing to read.
 */
export function makeBeams({ height = 8.6, radius = 2.5, count = 2 }) {
  const geo = new THREE.ConeGeometry(radius, height, 20, 1, true);
  geo.translate(0, height / 2, 0);
  const tex = beamTexture();
  // forceSinglePass: additive and depthWrite-off, so drawing the back faces in
  // their own pass first (three's default for transparent DoubleSide) changes
  // nothing on screen and cost a second draw every frame.
  const mat = new THREE.MeshBasicMaterial({
    map: tex, transparent: true, depthWrite: false, side: THREE.DoubleSide,
    blending: THREE.AdditiveBlending, fog: false, toneMapped: false, forceSinglePass: true,
  });
  const mesh = new THREE.InstancedMesh(geo, mat, count);
  mesh.frustumCulled = false;
  mesh.renderOrder = 6;
  mesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(count * 3), 3);

  const at = [];
  const amp = new Float32Array(count);
  const target = new Float32Array(count);
  const col = new Uint32Array(count);
  for (let i = 0; i < count; i++) { at.push(new THREE.Vector3()); col[i] = 0xffffff; }

  const place = (i, x, y, z) => at[i].set(x, y, z);
  const setColor = (i, hex) => { col[i] = hex >>> 0; };
  const set = (i, v) => { target[i] = v; };

  function update(dt, beatPulse) {
    for (let i = 0; i < count; i++) {
      amp[i] = damp(amp[i], target[i], 11, dt);
      const a = amp[i];
      _c.setHex(col[i]).multiplyScalar(a * (0.85 + beatPulse * 0.55) * 1.15);
      mesh.instanceColor.setXYZ(i, _c.r, _c.g, _c.b);
      const w = 0.55 + a * 0.55;
      _p.copy(at[i]);
      _s.set(w, 1, w);
      mesh.setMatrixAt(i, _m4.compose(_p, _q.identity(), _s));
    }
    mesh.instanceColor.needsUpdate = true;
    mesh.instanceMatrix.needsUpdate = true;
  }

  function dispose() {
    mesh.removeFromParent();
    geo.dispose(); mat.dispose(); tex.dispose(); mesh.dispose();
  }

  return { mesh, place, setColor, set, update, dispose };
}

// ------------------------------------------------------------------- finish

/** The gate. Off-screen for seventy seconds, then it arrives, and that is it. */
export function makeFinish({ mats, width = 11.5, height = 5.4 }) {
  // Posts, crossbar and checker tape are one static mesh, coloured per
  // vertex: one draw (it was two, and the gate arrives in the busiest seconds
  // of the round).
  const tinted = (g, hex) => {
    const c = _c.set(hex);   // linear, as a material colour would be
    const n = g.getAttribute('position').count;
    const a = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) { a[i * 3] = c.r; a[i * 3 + 1] = c.g; a[i * 3 + 2] = c.b; }
    g.setAttribute('color', new THREE.BufferAttribute(a, 3));
    return g;
  };
  const parts = [];
  for (const sx of [-1, 1]) {
    const p = new THREE.CylinderGeometry(0.22, 0.30, height, 10);
    p.translate(0, height / 2, sx * width / 2);
    parts.push(tinted(p, 0xffe9a8));
  }
  const bar = new THREE.BoxGeometry(0.5, 0.95, width);
  bar.translate(0, height - 0.48, 0);
  parts.push(tinted(bar, 0xffe9a8));

  // Checker tape on the crossbar.
  const TILES = 26;
  for (let i = 0; i < TILES; i++) {
    const z = -width / 2 + 0.32 + (i / (TILES - 1)) * (width - 0.64);
    const t = new THREE.PlaneGeometry(0.66, 0.44);
    t.translate(0.3, height - 0.95 - (i % 2) * 0.46, z);
    const k = i % 2 ? 0.05 : 1.0;
    const n = t.getAttribute('position').count;
    t.setAttribute('color', new THREE.BufferAttribute(new Float32Array(n * 3).fill(k), 3));
    parts.push(t);
  }
  const geo = merge(parts);
  // DoubleSide for the tape (seen from behind as the gate passes); the posts
  // and bar are closed and opaque, so their back faces never show.
  const mat = mats.glow({ color: 0xffffff, side: THREE.DoubleSide });
  mat.vertexColors = true;
  const mesh = new THREE.Mesh(geo, mat);

  const group = new THREE.Group();
  group.name = 'dd:finish';
  group.add(mesh);
  group.position.x = 220;
  group.visible = false;

  const show = (v) => { group.visible = v; };

  function dispose() {
    group.removeFromParent();
    geo.dispose(); mat.dispose();
  }

  return { group, show, dispose, materials: [mat] };
}

// --------------------------------------------------------------- attachments

/** The player's snare, slung at the hip. Reads instantly as "the drummer". */
export function makeSnare({ mats }) {
  const shell = new THREE.CylinderGeometry(0.30, 0.30, 0.26, 16);
  shell.rotateZ(Math.PI / 2);
  shell.rotateY(0.30);
  const rimG = new THREE.TorusGeometry(0.305, 0.035, 5, 18);
  rimG.rotateY(Math.PI / 2);
  rimG.rotateZ(0);
  rimG.translate(0.13, 0, 0);
  const geo = merge([shell, rimG]);
  const mat = mats.toon({ color: 0xfff4d8, bands: 2, rim: 1.5, pulse: 0.3, name: 'ddSnare' });
  return { mesh: new THREE.Mesh(geo, mat), geo, mat };
}

/** The drum major's shako: plume up top, so his silhouette is unmistakable. */
export function makeMajorGear({ mats }) {
  const cap = new THREE.CylinderGeometry(0.24, 0.26, 0.44, 12);
  cap.translate(0, 0.44, 0);
  const brim = new THREE.CylinderGeometry(0.31, 0.31, 0.05, 12);
  brim.translate(0, 0.22, 0);
  const plume = new THREE.ConeGeometry(0.13, 0.66, 8);
  plume.translate(0, 0.99, 0);
  const geo = merge([cap, brim, plume]);
  const mat = mats.toon({
    color: 0xffd93d, bands: 2, rim: 1.6, pulse: 0.35, emissive: 0.22, name: 'ddShako',
  });
  return { mesh: new THREE.Mesh(geo, mat), geo, mat };
}

/** The mace. Long limb + long prop = a long, legible arc on every beat. */
export function makeMace({ mats }) {
  const shaft = new THREE.CylinderGeometry(0.035, 0.035, 1.55, 8);
  shaft.translate(0, -0.55, 0);
  const ball = new THREE.SphereGeometry(0.135, 12, 9);
  ball.translate(0, 0.26, 0);
  const collar = new THREE.TorusGeometry(0.07, 0.026, 5, 12);
  collar.rotateX(Math.PI / 2);
  collar.translate(0, 0.04, 0);
  const geo = merge([shaft, ball, collar]);
  const mat = mats.glow({ color: 0xffe9a8, pulse: 0.6 });
  return { mesh: new THREE.Mesh(geo, mat), geo, mat };
}
