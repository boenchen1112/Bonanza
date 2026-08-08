/**
 * Chomp Chorus — the lane furniture.  [G4 builder owns this dir]
 *
 * Everything here exists to solve one problem: **four simultaneous telegraphs
 * have to be readable.** A single approaching cue is easy. Four of them, some
 * of which fire together as a chord and some of which are a sixteenth apart, is
 * the hardest readability problem in the set.
 *
 * The answer is four channels that say the same thing in four different ways,
 * plus one that chunks a chord into a single object:
 *
 *  1. **Breath ring** — a hoop that CONVERGES on the singer's head over the
 *     telegraph beat. Radius encodes time-to-note directly, so four rings at
 *     four radii are read as four distances without comparing anything. Four
 *     rings at the SAME radius reads instantly as "these are together", which
 *     is exactly the fact a chord needs to communicate.
 *  2. **Air pipe** — a column in front of the pedestal that FILLS from the
 *     floor over the same beat. A row of four filling bars is a bar chart; the
 *     eye reads relative heights pre-attentively, with no colour matching.
 *  3. **The singer itself** — inflates as it gulps. Motion of the thing you
 *     are actually watching, not a symbol next to it.
 *  4. **Colour + position + key glyph** on the pedestal, constant, so the
 *     mapping never has to be recalled under pressure.
 *  5. **Chord bridge** — a bar that physically links the pedestals of lanes
 *     that fire on the same beat. Two lanes joined by a bar are ONE object to
 *     track, which is why chord density can rise without the read collapsing.
 *
 * Cost: six draw calls for the whole set (one merged pedestal mesh, one merged
 * glyph mesh, and four InstancedMeshes of four instances each). Instanced
 * colour is modulated toward black to fade, because every glow here is
 * additive and black is free transparency.
 */

import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { LANES } from './chart.js';

const _m4 = new THREE.Matrix4();
const _q = new THREE.Quaternion();
const _pos = new THREE.Vector3();
const _scl = new THREE.Vector3();
const _col = new THREE.Color();
const X_AXIS = new THREE.Vector3(1, 0, 0);

/** Chunky procedural arrow glyphs — drawn as paths, never as font glyphs, so
 *  the key hint cannot silently vanish on a machine without an arrow font. */
function glyphAtlas() {
  const CELL = 128;
  const c = document.createElement('canvas');
  c.width = CELL * 4;
  c.height = CELL;
  const g = c.getContext('2d');
  g.clearRect(0, 0, c.width, c.height);
  g.fillStyle = '#ffffff';
  for (let i = 0; i < 4; i++) {
    g.save();
    g.translate(i * CELL + CELL / 2, CELL / 2);
    // 0 left, 1 down, 2 up, 3 right
    g.rotate([Math.PI, Math.PI * 0.5, -Math.PI * 0.5, 0][i]);
    // arrow pointing +x
    const s = CELL * 0.34;
    g.beginPath();
    g.moveTo(s, 0);
    g.lineTo(s * 0.1, -s * 0.85);
    g.lineTo(s * 0.1, -s * 0.34);
    g.lineTo(-s, -s * 0.34);
    g.lineTo(-s, s * 0.34);
    g.lineTo(s * 0.1, s * 0.34);
    g.lineTo(s * 0.1, s * 0.85);
    g.closePath();
    g.fill();
    g.restore();
  }
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.generateMipmaps = true;
  tex.anisotropy = 4;
  return tex;
}

/**
 * @param {object} o
 * @param {number[]} o.pedTop   pedestal top height per lane
 * @param {number[]} o.headY    world head height per lane
 * @param {THREE.Color[]} o.colors lane colours
 */
export function makeLaneProps({ pedTop, headY, colors }) {
  const group = new THREE.Group();
  group.name = 'chomp:props';
  const owned = [];
  const track = (x) => { owned.push(x); return x; };

  const N = LANES.length;

  // ---------------------------------------------------------------- pedestals
  // One merged mesh with vertex colours: four risers, four rims, one back
  // riser bench. The house dress pass upgrades the material in place, which is
  // why this is a plain MeshStandardMaterial rather than a reach into render/.
  {
    const parts = [];
    const paint = (geo, hex, mul = 1) => {
      const n = geo.attributes.position.count;
      const arr = new Float32Array(n * 3);
      _col.setHex(hex, THREE.SRGBColorSpace).multiplyScalar(mul);
      for (let i = 0; i < n; i++) { arr[i * 3] = _col.r; arr[i * 3 + 1] = _col.g; arr[i * 3 + 2] = _col.b; }
      geo.setAttribute('color', new THREE.BufferAttribute(arr, 3));
      return geo;
    };
    for (let i = 0; i < N; i++) {
      const h = Math.max(0.14, pedTop[i]);
      const body = new THREE.CylinderGeometry(1.02, 1.16, h, 20, 1);
      body.translate(LANES[i].x, h / 2, 0);
      parts.push(paint(body, colors[i].getHex(THREE.SRGBColorSpace), 0.32));
      const cap = new THREE.CylinderGeometry(1.1, 1.1, 0.11, 20, 1);
      cap.translate(LANES[i].x, h + 0.02, 0);
      parts.push(paint(cap, colors[i].getHex(THREE.SRGBColorSpace), 1.0));
    }
    // A low bench joining the four, so the row reads as one stage rather than
    // four floating discs.
    const bench = new THREE.BoxGeometry(9.6, 0.22, 2.4);
    bench.translate(0, 0.06, -0.15);
    parts.push(paint(bench, 0x2a1240, 1));

    const geo = mergeGeometries(parts, false);
    for (const p of parts) p.dispose();
    const mat = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.75, metalness: 0.0 });
    const mesh = new THREE.Mesh(geo, mat);
    mesh.name = 'chomp:pedestals';
    group.add(mesh);
    track(geo); track(mat);
  }

  // ------------------------------------------------------------- key glyphs
  let glyphTex = null;
  {
    glyphTex = glyphAtlas();
    const parts = [];
    for (let i = 0; i < N; i++) {
      const q = new THREE.PlaneGeometry(0.86, 0.86);
      const uv = q.attributes.uv;
      for (let k = 0; k < uv.count; k++) uv.setX(k, (uv.getX(k) + i) / 4);
      const n = q.attributes.position.count;
      const arr = new Float32Array(n * 3);
      _col.copy(colors[i]);
      for (let k = 0; k < n; k++) { arr[k * 3] = _col.r; arr[k * 3 + 1] = _col.g; arr[k * 3 + 2] = _col.b; }
      q.setAttribute('color', new THREE.BufferAttribute(arr, 3));
      q.translate(LANES[i].x, Math.max(0.42, pedTop[i] * 0.55), 1.18);
      parts.push(q);
    }
    const geo = mergeGeometries(parts, false);
    for (const p of parts) p.dispose();
    const mat = new THREE.MeshBasicMaterial({
      map: glyphTex, transparent: true, vertexColors: true, depthWrite: false,
      toneMapped: false, blending: THREE.AdditiveBlending,
    });
    const mesh = new THREE.Mesh(geo, mat);
    mesh.name = 'chomp:glyphs';
    mesh.renderOrder = 2;
    group.add(mesh);
    track(geo); track(mat); track(glyphTex);
  }

  // ------------------------------------------------------------- breath rings
  const ringGeo = track(new THREE.TorusGeometry(1, 0.055, 8, 44));
  const ringMat = track(new THREE.MeshBasicMaterial({
    transparent: true, blending: THREE.AdditiveBlending, depthWrite: false,
    toneMapped: false, fog: false,
  }));
  const rings = new THREE.InstancedMesh(ringGeo, ringMat, N);
  rings.frustumCulled = false;
  rings.renderOrder = 3;
  rings.name = 'chomp:rings';
  group.add(rings);

  // ---------------------------------------------------------------- air pipes
  const pipeGeo = track(new THREE.CylinderGeometry(0.15, 0.19, 1, 12, 1, true));
  pipeGeo.translate(0, 0.5, 0);
  const pipeMat = track(new THREE.MeshBasicMaterial({
    transparent: true, blending: THREE.AdditiveBlending, depthWrite: false,
    side: THREE.DoubleSide, toneMapped: false, fog: false,
  }));
  const pipes = new THREE.InstancedMesh(pipeGeo, pipeMat, N);
  pipes.frustumCulled = false;
  pipes.renderOrder = 3;
  pipes.name = 'chomp:pipes';
  group.add(pipes);

  // ------------------------------------------------------------- voice beams
  const beamGeo = track(new THREE.CylinderGeometry(0.06, 0.42, 1, 12, 1, true));
  beamGeo.translate(0, 0.5, 0);
  const beamMat = track(new THREE.MeshBasicMaterial({
    transparent: true, blending: THREE.AdditiveBlending, depthWrite: false,
    side: THREE.DoubleSide, toneMapped: false, fog: false,
  }));
  const beams = new THREE.InstancedMesh(beamGeo, beamMat, N);
  beams.frustumCulled = false;
  beams.renderOrder = 3;
  beams.name = 'chomp:beams';
  group.add(beams);

  // ------------------------------------------------------------ chord bridges
  const BR = N - 1;
  const brGeo = track(new THREE.BoxGeometry(1, 0.10, 0.10));
  const brMat = track(new THREE.MeshBasicMaterial({
    transparent: true, blending: THREE.AdditiveBlending, depthWrite: false,
    toneMapped: false, fog: false,
  }));
  const bridges = new THREE.InstancedMesh(brGeo, brMat, BR);
  bridges.frustumCulled = false;
  bridges.renderOrder = 3;
  bridges.name = 'chomp:bridges';
  group.add(bridges);

  for (const m of [rings, pipes, beams, bridges]) {
    for (let i = 0; i < m.count; i++) m.setColorAt(i, _col.setHex(0x000000));
  }

  const HIDDEN = new THREE.Vector3(0, -999, 0);

  function place(mesh, i, x, y, z, sx, sy, sz, rx = 0) {
    _pos.set(x, y, z);
    _scl.set(sx, sy, sz);
    _q.setFromAxisAngle(X_AXIS, rx);
    _m4.compose(_pos, _q, _scl);
    mesh.setMatrixAt(i, _m4);
  }
  function hide(mesh, i) {
    _m4.compose(HIDDEN, _q.identity(), _scl.set(0.001, 0.001, 0.001));
    mesh.setMatrixAt(i, _m4);
  }
  function tint(mesh, i, color, amount) {
    _col.copy(color).multiplyScalar(Math.max(0, amount));
    mesh.setColorAt(i, _col);
  }

  /**
   * @param {object} s per-frame lane state
   * @param {number[]} s.gulp     0..1 telegraph progress (1 = the note is NOW)
   * @param {number[]} s.armed    0..1 whether a telegraph is running at all
   * @param {number[]} s.sing     0..1 decaying "just sang" energy
   * @param {number[]} s.alive    0..1 is this voice in the harmony
   * @param {number[]} s.chord    1 when this lane is part of the pending chord
   * @param {number}   s.chordN   how many lanes are in the pending chord
   * @param {number}   s.beatPhase 0..1
   */
  function update(s) {
    const bp = s.beatPhase;
    for (let i = 0; i < N; i++) {
      const g = s.gulp[i];
      const armed = s.armed[i];
      const sing = s.sing[i];
      const alive = s.alive[i];
      const c = colors[i];

      // --- ring: converges from R_FAR to R_NEAR, brightening as it arrives ---
      if (armed > 0.001) {
        const r = 2.15 - 1.55 * g;
        const pop = 1 + Math.max(0, g - 0.92) * 3.2;
        place(rings, i, LANES[i].x, headY[i] - 0.08, 0.15, r * pop, r * pop, 1, -0.1);
        tint(rings, i, c, (0.22 + 0.95 * g * g) * armed);
      } else if (sing > 0.01) {
        // the ring blows outward on release — the breath leaving
        const r = 0.6 + (1 - sing) * 2.6;
        place(rings, i, LANES[i].x, headY[i] - 0.08, 0.15, r, r, 1, -0.1);
        tint(rings, i, c, sing * 0.8);
      } else {
        hide(rings, i);
        tint(rings, i, c, 0);
      }

      // --- pipe: fills over the telegraph, empties on release --------------
      const fill = Math.max(g * armed, sing * 0.55);
      if (fill > 0.004) {
        const h = 0.25 + (headY[i] - pedTop[i] - 0.2) * fill;
        place(pipes, i, LANES[i].x, pedTop[i] + 0.06, 1.02, 1, h, 1);
        tint(pipes, i, c, 0.30 + 0.85 * fill);
      } else {
        hide(pipes, i);
      }

      // --- beam: the sung note leaving, plus a resting halo when alive -----
      const rest = alive * (0.16 + 0.06 * Math.sin(bp * Math.PI * 2));
      const b = Math.max(sing, rest * 0.5);
      if (b > 0.006) {
        const h = 0.5 + 4.2 * sing * sing + rest * 1.4;
        place(beams, i, LANES[i].x, headY[i] + 0.16, 0, 0.8 + sing * 0.7, h, 0.8 + sing * 0.7);
        tint(beams, i, c, (0.16 + 1.35 * sing) * (0.35 + 0.65 * alive));
      } else {
        hide(beams, i);
      }
    }

    // --- chord bridges: link consecutive lanes that fire together ----------
    let prev = -1;
    let seg = 0;
    if (s.chordN >= 2) {
      for (let i = 0; i < N; i++) {
        if (!s.chord[i]) continue;
        if (prev >= 0 && seg < BR) {
          const x0 = LANES[prev].x;
          const x1 = LANES[i].x;
          const g = Math.max(s.gulp[prev], s.gulp[i]);
          const y = Math.max(pedTop[prev], pedTop[i]) + 0.30;
          place(bridges, seg, (x0 + x1) / 2, y, 0.55, Math.abs(x1 - x0), 1, 1);
          _col.copy(colors[prev]).lerp(colors[i], 0.5).multiplyScalar(0.30 + 1.05 * g * g);
          bridges.setColorAt(seg, _col);
          seg++;
        }
        prev = i;
      }
    }
    for (; seg < BR; seg++) { hide(bridges, seg); bridges.setColorAt(seg, _col.setHex(0x000000)); }

    for (const m of [rings, pipes, beams, bridges]) {
      m.instanceMatrix.needsUpdate = true;
      if (m.instanceColor) m.instanceColor.needsUpdate = true;
    }
  }

  function dispose() {
    for (const o of owned) o.dispose?.();
    owned.length = 0;
    rings.dispose(); pipes.dispose(); beams.dispose(); bridges.dispose();
    group.removeFromParent();
  }

  return { group, update, dispose };
}
