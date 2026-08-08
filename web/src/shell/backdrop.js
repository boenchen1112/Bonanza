/**
 * The shell's shared world.  [shell agent owns this file]
 *
 * Every screen outside a minigame stands in the same room: a lit stage floor,
 * a crowd on the horizon that bobs on the beat, drifting confetti shapes and
 * two sweeping spotlights. It is deliberately the SAME room everywhere, because
 * that continuity is most of what makes a menu feel like part of a game rather
 * than a document.
 *
 * Budget: 6 draw calls, all instanced or textured-once, no per-frame allocation.
 */

import * as THREE from 'three';
import { damp } from '../core/util.js';
import { PAL, num } from './theme.js';

const SHAPES = 44;
const CROWD = 34;

export function createBackdrop(ctx, {
  accent = num(PAL.cyan),
  crowd = true,
  spots = true,
  floorY = -1.6,
  density = 1,
} = {}) {
  const g = new THREE.Group();
  const disposables = [];
  const rng = ctx.rng || Math.random;

  ctx.scene.background = new THREE.Color(0x0b0a1a);
  ctx.scene.fog = new THREE.Fog(0x0b0a1a, 26, 74);

  // ------------------------------------------------------------------ light
  const key = new THREE.DirectionalLight(0xffffff, 1.9);
  key.position.set(5, 9, 7);
  g.add(key);
  const rim = new THREE.DirectionalLight(accent, 1.1);
  rim.position.set(-6, 3, -4);
  g.add(rim);
  g.add(new THREE.HemisphereLight(0x8fa8ff, 0x1a1030, 1.25));

  // -------------------------------------------------------------------- sky
  const skyTex = makeSkyTexture(accent);
  disposables.push(skyTex);
  const skyMat = new THREE.MeshBasicMaterial({ map: skyTex, depthWrite: false, fog: false });
  const sky = new THREE.Mesh(new THREE.PlaneGeometry(160, 90), skyMat);
  sky.position.set(0, 8, -46);
  sky.renderOrder = -10;
  g.add(sky);
  disposables.push(sky.geometry, skyMat);

  // ------------------------------------------------------------------ floor
  const floorTex = makeGridTexture(accent);
  floorTex.wrapS = floorTex.wrapT = THREE.RepeatWrapping;
  floorTex.repeat.set(18, 18);
  disposables.push(floorTex);
  const floorMat = new THREE.MeshStandardMaterial({
    map: floorTex, color: 0x2a2560, roughness: 0.62, metalness: 0.22,
    emissive: new THREE.Color(accent).multiplyScalar(0.10), emissiveMap: floorTex,
  });
  const floor = new THREE.Mesh(new THREE.PlaneGeometry(120, 120), floorMat);
  floor.rotation.x = -Math.PI / 2;
  floor.position.y = floorY;
  g.add(floor);
  disposables.push(floor.geometry, floorMat);

  // ----------------------------------------------------------- floating bits
  const shapeCount = Math.round(SHAPES * density);
  const shapeGeo = whiteVertexColors(new THREE.OctahedronGeometry(0.32, 0));
  const shapeMat = new THREE.MeshStandardMaterial({ roughness: 0.3, metalness: 0.15, flatShading: true, vertexColors: true });
  const shapes = new THREE.InstancedMesh(shapeGeo, shapeMat, shapeCount);
  shapes.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
  shapes.frustumCulled = false;
  const palette = [num(PAL.yellow), num(PAL.cyan), num(PAL.coral), num(PAL.green), num(PAL.violet)];
  const c = new THREE.Color();
  const bits = [];
  for (let i = 0; i < shapeCount; i++) {
    bits.push({
      x: (rng() - 0.5) * 40, y: rng() * 16 - 3, z: -6 - rng() * 26,
      spin: (rng() - 0.5) * 1.5, phase: rng() * Math.PI * 2,
      rise: 0.25 + rng() * 0.55, s: 0.5 + rng() * 1.1,
    });
    c.set(palette[i % palette.length]);
    shapes.setColorAt(i, c);
  }
  if (shapes.instanceColor) shapes.instanceColor.needsUpdate = true;
  g.add(shapes);
  disposables.push(shapeGeo, shapeMat);

  // ------------------------------------------------------------------ crowd
  let crowdMesh = null;
  const heads = [];
  if (crowd) {
    const cg = whiteVertexColors(new THREE.CapsuleGeometry(0.34, 0.5, 3, 6));
    const cm = new THREE.MeshStandardMaterial({ roughness: 0.85, flatShading: true, vertexColors: true });
    crowdMesh = new THREE.InstancedMesh(cg, cm, CROWD);
    crowdMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
    crowdMesh.frustumCulled = false;
    for (let i = 0; i < CROWD; i++) {
      const row = i % 3;
      heads.push({
        x: ((i * 7919) % 100) / 100 * 46 - 23 + (rng() - 0.5) * 1.2,
        z: -13 - row * 2.6,
        y: floorY + 0.6 + row * 0.55,
        phase: rng() * Math.PI * 2,
        amp: 0.14 + rng() * 0.16,
      });
      c.set(palette[i % palette.length]).multiplyScalar(0.42);
      crowdMesh.setColorAt(i, c);
    }
    if (crowdMesh.instanceColor) crowdMesh.instanceColor.needsUpdate = true;
    g.add(crowdMesh);
    disposables.push(cg, cm);
  }

  // -------------------------------------------------------------- spotlights
  const beams = [];
  if (spots) {
    const bg = new THREE.ConeGeometry(2.6, 22, 14, 1, true);
    bg.translate(0, -11, 0);
    for (let i = 0; i < 2; i++) {
      const bm = new THREE.MeshBasicMaterial({
        color: i ? accent : 0xffffff, transparent: true, opacity: 0.06,
        blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide, fog: false,
      });
      const beam = new THREE.Mesh(bg, bm);
      beam.position.set(i ? 9 : -9, 13, -12);
      beam.renderOrder = 5;
      g.add(beam);
      beams.push(beam);
      disposables.push(bm);
    }
    disposables.push(bg);
  }

  ctx.scene.add(g);

  // ------------------------------------------------------------------ update
  const m4 = new THREE.Matrix4();
  const q = new THREE.Quaternion();
  const v = new THREE.Vector3();
  const s = new THREE.Vector3();
  const axis = new THREE.Vector3(0.4, 1, 0.2).normalize();
  let pulse = 0;
  let accentCol = new THREE.Color(accent);

  function update(dt, beat, t) {
    const bf = beat - Math.floor(beat);
    pulse = damp(pulse, 0, 7, dt);
    if (bf < 0.12 && pulse < 0.2) pulse = 1;

    for (let i = 0; i < bits.length; i++) {
      const b = bits[i];
      b.y += b.rise * dt;
      if (b.y > 15) b.y = -5;
      const wob = Math.sin(t * 0.7 + b.phase) * 0.6;
      v.set(b.x + wob, b.y, b.z);
      q.setFromAxisAngle(axis, t * b.spin + b.phase);
      const sc = b.s * (1 + pulse * 0.16);
      s.set(sc, sc, sc);
      m4.compose(v, q, s);
      shapes.setMatrixAt(i, m4);
    }
    shapes.instanceMatrix.needsUpdate = true;

    if (crowdMesh) {
      for (let i = 0; i < heads.length; i++) {
        const h = heads[i];
        const b2 = Math.abs(Math.sin((beat + h.phase) * Math.PI * 0.5));
        v.set(h.x, h.y + b2 * h.amp * 2.2, h.z);
        q.setFromAxisAngle(AXIS_Z, Math.sin(beat * Math.PI * 0.5 + h.phase) * 0.13);
        const sq = 1 - b2 * 0.10;
        s.set(1 / sq, sq, 1 / sq);
        m4.compose(v, q, s);
        crowdMesh.setMatrixAt(i, m4);
      }
      crowdMesh.instanceMatrix.needsUpdate = true;
    }

    for (let i = 0; i < beams.length; i++) {
      const b = beams[i];
      b.rotation.z = Math.sin(t * 0.42 + i * 2.1) * 0.34 + (i ? 0.2 : -0.2);
      b.rotation.x = Math.cos(t * 0.31 + i) * 0.12;
      b.material.opacity = 0.05 + pulse * 0.055;
    }

    floorMat.emissive.copy(accentCol).multiplyScalar(0.07 + pulse * 0.13);
    floorTex.offset.y = -t * 0.012;
  }

  function setAccent(hexColor) {
    accentCol = new THREE.Color(hexColor);
    rim.color.copy(accentCol);
    if (beams[1]) beams[1].material.color.copy(accentCol);
  }

  function dispose() {
    ctx.scene.remove(g);
    for (const d of disposables) d?.dispose?.();
    if (crowdMesh) crowdMesh.dispose?.();
    shapes.dispose?.();
  }

  return { group: g, update, setAccent, dispose, get pulse() { return pulse; } };
}

const AXIS_Z = new THREE.Vector3(0, 0, 1);

/**
 * three's `color_fragment` only applies vColor when USE_COLOR is defined, so an
 * InstancedMesh that relies on `setColorAt` alone renders untinted. Enabling
 * `vertexColors` fixes that but then multiplies by a `color` attribute the
 * geometry does not have — which WebGL defaults to black. A white attribute is
 * the one-line fix, and it costs 12 bytes per vertex on shared geometry.
 */
export function whiteVertexColors(geo) {
  const n = geo.attributes.position.count;
  const arr = new Float32Array(n * 3).fill(1);
  geo.setAttribute('color', new THREE.BufferAttribute(arr, 3));
  return geo;
}

// ---------------------------------------------------------------- textures

function makeSkyTexture(accent) {
  const cv = document.createElement('canvas');
  cv.width = 256; cv.height = 256;
  const g = cv.getContext('2d');
  const grd = g.createLinearGradient(0, 0, 0, 256);
  grd.addColorStop(0, '#070613');
  grd.addColorStop(0.55, '#141034');
  grd.addColorStop(0.8, '#241a4d');
  grd.addColorStop(1, '#0d0a22');
  g.fillStyle = grd;
  g.fillRect(0, 0, 256, 256);

  // arena glow behind the crowd
  const ac = '#' + (accent >>> 0).toString(16).padStart(6, '0');
  const rg = g.createRadialGradient(128, 190, 6, 128, 190, 150);
  rg.addColorStop(0, hexA(ac, 0.30));
  rg.addColorStop(1, hexA(ac, 0));
  g.fillStyle = rg;
  g.fillRect(0, 0, 256, 256);

  // sparse stars — deterministic, no rng dependency
  g.fillStyle = 'rgba(255,255,255,.7)';
  for (let i = 0; i < 120; i++) {
    const x = (i * 9301 % 256);
    const y = ((i * 49297) % 140);
    const r = ((i * 7) % 3) * 0.4 + 0.4;
    g.globalAlpha = 0.25 + ((i * 13) % 10) / 14;
    g.beginPath(); g.arc(x, y, r, 0, Math.PI * 2); g.fill();
  }
  g.globalAlpha = 1;

  const tex = new THREE.CanvasTexture(cv);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

function makeGridTexture(accent) {
  const cv = document.createElement('canvas');
  cv.width = 128; cv.height = 128;
  const g = cv.getContext('2d');
  g.fillStyle = '#0e0c26';
  g.fillRect(0, 0, 128, 128);
  const ac = '#' + (accent >>> 0).toString(16).padStart(6, '0');
  g.strokeStyle = hexA(ac, 0.55);
  g.lineWidth = 3;
  g.strokeRect(0, 0, 128, 128);
  g.strokeStyle = 'rgba(255,255,255,.06)';
  g.lineWidth = 1;
  g.beginPath();
  g.moveTo(64, 0); g.lineTo(64, 128); g.moveTo(0, 64); g.lineTo(128, 64);
  g.stroke();
  const tex = new THREE.CanvasTexture(cv);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.anisotropy = 4;
  return tex;
}

function hexA(hexStr, a) {
  const n = parseInt(hexStr.replace('#', ''), 16);
  return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${a})`;
}
