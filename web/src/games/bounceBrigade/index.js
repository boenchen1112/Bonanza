/**
 * G3 · BOUNCE BRIGADE — "Land every landing."
 * 118bpm · 4/4 · one button (taps + charges)
 *
 * ────────────────────────────────────────────────────────────────────────────
 * THE IDEA
 *
 * The platforms are instruments. Landing on one plays its note; the chain of
 * platforms IS the melody, laid out along the direction you are already
 * travelling. Height is pitch, colour is pitch class, width is note length, and
 * distance is time — because the bouncer moves at a constant speed, so the X
 * axis and the beat axis are literally the same axis (`x = beat * UPB`). The
 * level is a piano roll you run across.
 *
 * A clean run performs "Rubber Band Stand"'s counter-melody over the backing
 * track. A sloppy run plays it late, early, or not at all, and you hear every
 * one of those. There is no verdict jingle on a successful landing on purpose:
 * the note IS the feedback, and accuracy changes its *timbre* (a PERFECT adds
 * an octave and a bell, an OKAY is dull and woody), so the tune audibly gets
 * better as you get better. The player is making the music, not scoring
 * against it.
 *
 * ────────────────────────────────────────────────────────────────────────────
 * THE VERBS
 *
 *   TAP     — press down on the beat. Bounce. One note.
 *   CHARGE  — the melody's half notes are long, raked RAMPS. Press down on the
 *             beat you meet the ramp, ROLL along it for two beats while the
 *             note sustains and the bouncer coils, and RELEASE on the downbeat
 *             at the far end to rocket over a chasm four times as wide as a
 *             normal gap. Release early, or just tap, and you belly-flop into
 *             the water, skip off it, and scramble back up onto the next
 *             platform, having lost exactly one note and no dignity you were
 *             using.
 *
 * There is no fail-out and no death spiral: every trajectory, however badly it
 * started, is aimed at the next platform. The most a single mistake can cost
 * is the note it was attached to.
 *
 * ────────────────────────────────────────────────────────────────────────────
 * The chart, the pitch/height/colour mapping and the harmony live in
 * `chart.js` (pure data, no THREE) so the tests and the verification script can
 * import them. `verify.mjs` drives real hold/release input through a headless
 * browser and proves a charged release clears the chasm while a bare tap does
 * not.
 */

import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/examples/jsm/geometries/RoundedBoxGeometry.js';

import { NoteJudge, rankFor, SCORE } from '../../core/judge.js';
import { FEEL, feelForCombo } from '../../core/feel.js';
import {
  damp, clamp, clamp01, lerp, smoothstep, smootherstep,
  easeOutCubic, easeOutQuint, easeInCubic, backOut, elasticOut, makeRng,
} from '../../core/util.js';
import { makeCharacter, makeAnimator } from '../../chars/index.js';

import {
  buildChart, UNITS_PER_BEAT, WATER_Y, BASE_Y, SLAB_DEPTH, SLAB_THICK,
  CHARGE_MIN_HOLD_BEATS, LEAD_IN_BARS, BPM, END_BEAT,
  degToY, yToDeg, colorForDeg, lighten,
} from './chart.js';

// ---------------------------------------------------------------- constants

const CAM_DISTANCE = 15.6;
const CAM_HEIGHT = 2.35;
const CAM_YAW = -0.17;          // camera sits slightly behind-left: you see ahead
const CAM_LEAD = 5.4;           // world units of runway kept in front of the bouncer
const CAM_LAMBDA = 6.0;

const BOUNCER_SCALE = 0.98;
const BOUNCER_FACING = 1.12;    // rad; faces screen-right but keeps its face on us

const SPLASH_Y = WATER_Y + 0.35;
const HIGHLIGHT_WINDOW = 26;    // platforms around the cursor whose matrices animate

const beatFrac = (x) => x - Math.floor(x);       // NEVER x % 1: beats go negative
const hopShape = (u) => 4 * u * (1 - u);

// ------------------------------------------------------------------- state

let ctxRef = null;
let root = null;
let env = null;
let rng = null;
let unsubBeat = null;

let chart = null;
let plats = null;
let judge = null;

// platform instancing
let slabMesh = null, capMesh = null, postMesh = null, buoyMesh = null;
let slabGeo = null, capGeo = null, postGeo = null, buoyGeo = null;
let slabMat = null, capMat = null, postMat = null, buoyMat = null;
let popArr = null, glowArr = null, litArr = null;
let waterMesh = null, waterGeo = null, waterMat = null, waterTex = null;
let gateGroup = null, gateGeos = null, gateMats = null;

// character
let bouncer = null, bouncerRoot = null, anim = null, trail = null;

// run state
let started = false, done = false, result = null;
let cursor = 0;
let held = false, holdStartBeat = -1e9, holdStartTime = 0;
let contactVerdict = null, releaseVerdict = null, splashed = null, landedPop = null;
let points = 0, judged = 0, missCount = 0, bestCombo = 0;
let chargeGlow = 0, launchFlash = 0, camAnchorY = 2, camY = 2, stumble = 0;
let lastUiBeat = -1e9, finished = false, finishTime = 0;
let noteMap = null;             // judge note -> chart note
let motionStopBeat = Infinity;
let lastBeat = -1e9;
let telemetry = null;           // dev hook payload for verify.mjs

const M = { x: 0, y: 0, spin: 0, lean: 0, phase: 'surface', u: 0, k: 0, outcome: 'clean' };
const dummy = new THREE.Object3D();
const tmpColor = new THREE.Color();
const camTarget = new THREE.Vector3();

// ------------------------------------------------------------------ helpers

/** InstancedMesh colour needs USE_COLOR, which needs a vertex colour attribute. */
function whiteVertexColors(geo) {
  const n = geo.attributes.position.count;
  const a = new Float32Array(n * 3);
  a.fill(1);
  geo.setAttribute('color', new THREE.BufferAttribute(a, 3));
  return geo;
}

/** Procedural water: soft caustic bands, scrolled by the camera for parallax. */
function makeWaterTexture() {
  const S = 256;
  const c = document.createElement('canvas');
  c.width = c.height = S;
  const g = c.getContext('2d');
  g.fillStyle = '#ffffff';
  g.fillRect(0, 0, S, S);
  const r = makeRng(0x77a7e2);
  g.globalAlpha = 1;
  for (let i = 0; i < 26; i++) {
    const y = r() * S;
    const h = 2 + r() * 7;
    const a = 0.10 + r() * 0.16;
    g.fillStyle = `rgba(0,0,0,${a})`;
    g.beginPath();
    for (let x = 0; x <= S; x += 8) {
      const wob = Math.sin((x / S) * Math.PI * (2 + i % 3) + i) * 5;
      if (x === 0) g.moveTo(x, y + wob); else g.lineTo(x, y + wob);
    }
    for (let x = S; x >= 0; x -= 8) {
      const wob = Math.sin((x / S) * Math.PI * (2 + i % 3) + i) * 5;
      g.lineTo(x, y + wob + h);
    }
    g.closePath();
    g.fill();
  }
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.repeat.set(26, 10);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

function platformIndexAt(beat) {
  while (cursor + 1 < plats.length && plats[cursor + 1].beat <= beat) cursor++;
  while (cursor > 0 && plats[cursor].beat > beat) cursor--;
  return cursor;
}

/** What happened on the way OFF platform k. Unjudged reads as optimistic. */
function outcomeFor(k) {
  const p = plats[k];
  if (!p.scored) return 'clean';
  if (p.charge) return releaseVerdict[k] === 'miss' ? 'plunge' : 'clean';
  return contactVerdict[k] === 'miss' ? 'stumble' : 'clean';
}

/**
 * Where the bouncer is at `beat`. Pure, deterministic, recomputed each frame —
 * so a judgement that arrives 100ms late simply changes the shape of the arc
 * from that frame on, rather than needing its own state machine.
 */
function motionAt(beat) {
  const k = platformIndexAt(beat);
  const p = plats[k];
  const bx = Math.min(beat, motionStopBeat) * UNITS_PER_BEAT;
  M.k = k;
  M.x = bx;
  M.spin = 0;
  M.lean = 0;
  M.outcome = 'clean';

  if (beat < p.departBeat || p.flightBeats <= 0) {
    // --- riding a surface --------------------------------------------------
    const u = p.roll > 0 ? clamp01((beat - p.beat) / p.roll) : 0;
    let y = p.y + p.slope * (bx - p.contactX);
    if (p.kind === 'R') y += 0.24 * hopShape(beatFrac(beat));            // jogging in
    if (p.charge) y -= 0.46 * smootherstep(clamp01(u * 1.25));           // coiling
    if (p.kind === 'X') y += 0.30 * hopShape(beatFrac(beat));            // victory hops
    M.y = y;
    M.phase = 'surface';
    M.u = u;
    M.lean = p.charge ? -0.22 * smootherstep(u) : 0;
    return M;
  }

  // --- airborne ------------------------------------------------------------
  const t0 = p.departBeat;
  const t1 = p.nextBeat;
  const T = Math.max(0.05, t1 - t0);
  const u = clamp01((beat - t0) / T);
  const y0 = p.departY;
  const y1 = plats[k + 1].y;
  const outcome = outcomeFor(k);
  M.phase = 'air';
  M.u = u;
  M.outcome = outcome;

  if (outcome === 'plunge') {
    const s = 0.48;
    if (u < s) {
      M.y = lerp(y0, SPLASH_Y, easeInCubic(u / s));
    } else {
      const w = (u - s) / (1 - s);
      M.y = lerp(SPLASH_Y, y1, 1 - (1 - w) * (1 - w)) + 0.55 * hopShape(w);
    }
    M.spin = -Math.PI * 2.6 * u;
    return M;
  }

  if (outcome === 'stumble') {
    const apex = (0.5 + 0.75 * T) * 0.5;
    M.y = lerp(y0, y1, u) + apex * hopShape(u) - 1.15 * Math.exp(-7 * u) * (1 - u);
    M.spin = -Math.PI * 1.2 * easeOutCubic(u);
    return M;
  }

  const apex = 0.55 + 0.95 * T;
  M.y = lerp(y0, y1, u) + apex * hopShape(u);
  // Charged launches somersault; ordinary hops only lean into the arc.
  if (p.charge) M.spin = -Math.PI * 2 * (T >= 3 ? 2 : 1) * easeOutQuint(u * 0.92);
  else M.lean = -0.30 * (1 - 2 * u);
  return M;
}

// ------------------------------------------------------------------- audio

function noteGainFor(v) {
  return v === 'perfect' ? 1 : v === 'great' ? 0.86 : 0.66;
}

/** Play a platform's note. Scheduled at the PRESS time, so mistiming is heard. */
function playPlatformNote(p, verdict, at, durBeats) {
  const V = ctxRef.audio.voices;
  const spb = 60 / BPM;
  const dur = clamp((durBeats || 1) * spb * 0.9, 0.16, 1.6);
  const g = noteGainFor(verdict);
  const bright = verdict === 'perfect' ? 4.8 : verdict === 'great' ? 3.9 : 2.5;
  V.pluck(at, { midi: p.midi, dur, gain: 0.33 * g, bright, rev: 0.22, dly: 0.14, pan: 0.06 });
  if (verdict !== 'good') {
    V.pluck(at + 0.014, {
      midi: p.midi + 12, dur: dur * 0.45, gain: 0.11 * g, bright: 5.4, rev: 0.22, dly: 0.1,
    });
  }
  if (verdict === 'perfect') {
    V.bell(at + 0.02, {
      midi: p.midi + 12, dur: 0.55, gain: 0.10, ratio: 2.01, index: 3.2, rev: 0.42, dly: 0.26,
    });
  }
  // The springboard itself.
  V.hit(at, { gain: 0.085 * g, d: 0.028, hp: 900, lp: 5600, rev: 0.06 });
}

function playSustain(p, verdict, at) {
  const V = ctxRef.audio.voices;
  const spb = 60 / BPM;
  const g = noteGainFor(verdict);
  V.lead(at, {
    midi: p.midi, dur: p.roll * spb, gain: 0.20 * g, cutoff: 4.2, res: 2.4,
    rev: 0.24, dly: 0.16, vib: 0.9,
  });
  V.pluck(at, { midi: p.midi, dur: 0.2, gain: 0.2 * g, bright: 4, rev: 0.2 });
  V.riser(at, { dur: p.roll * spb, gain: 0.075, from: 500, to: 5200, rev: 0.25 });
  V.hit(at, { gain: 0.09, d: 0.03, hp: 800, lp: 5200 });
}

function playRelease(p, verdict, at, huge) {
  const V = ctxRef.audio.voices;
  const g = noteGainFor(verdict);
  V.bell(at, {
    midi: p.midi + 12, dur: huge ? 0.9 : 0.5, gain: 0.15 * g, ratio: 2.01,
    index: 4.5, rev: 0.45, dly: 0.28,
  });
  V.pluck(at, { midi: p.midi + 19, dur: 0.22, gain: 0.13 * g, bright: 6, rev: 0.3 });
  V.kick(at, { gain: 0.55, decay: 0.26, tune: 1.1, dest: ctxRef.audio.sfxBus });
  if (huge) V.crash(at, { gain: 0.24, decay: 1.3, rev: 0.5, dest: ctxRef.audio.sfxBus });
}

function playThud(at) {
  const V = ctxRef.audio.voices;
  V.hit(at, { gain: 0.16, d: 0.11, hp: 110, lp: 850, rev: 0.05 });
}

// ---------------------------------------------------------------- judgement

function onJudged(note, verdict, errMs) {
  const meta = noteMap.get(note);
  if (!meta) return;
  const p = plats[meta.platform];
  const t = ctxRef.clock.rawNow();
  const pressTime = Math.max(note.time + errMs / 1000, t + 0.002);
  const hit = verdict !== 'miss';

  judged++;
  points += SCORE[verdict] * meta.weight;
  if (!hit) missCount++;
  bestCombo = Math.max(bestCombo, judge.stats.combo);

  if (meta.kind === 'contact') {
    contactVerdict[p.index] = verdict;
    if (hit) {
      if (p.charge) playSustain(p, verdict, pressTime);
      else playPlatformNote(p, verdict, pressTime, Math.max(0.5, p.flightBeats));
      popPlatform(p.index, verdict === 'perfect' ? 1 : verdict === 'great' ? 0.8 : 0.55);
    } else {
      playThud(pressTime);
      ctxRef.audio.sfx('miss', pressTime);
      stumble = 1;
    }
  } else {
    releaseVerdict[p.index] = verdict;
    if (hit) {
      playRelease(p, verdict, pressTime, p.kind === 'F');
      launchFlash = 1;
      anim?.strike({ power: p.kind === 'F' ? 1.35 : 1.05 });
    } else {
      playThud(pressTime);
      ctxRef.audio.sfx('miss', pressTime);
    }
  }

  reactVisually(p, verdict, meta);
  ctxRef.bus.emit('judge', {
    verdict, errMs, beat: ctxRef.clock.beatAt(note.time), kind: meta.kind,
  });
  pushHud();
}

function reactVisually(p, verdict, meta) {
  const f = feelForCombo(verdict, judge.stats.combo);
  const big = p.kind === 'X' || (p.kind === 'F' && meta.kind === 'release');
  const scale = big ? 1.7 : meta.kind === 'release' ? 1.25 : 1;
  const pos = [M.x, Math.max(M.y, WATER_Y + 0.6) + 0.55, 0];

  ctxRef.fx.verdict(verdict, pos, {
    dir: meta.kind === 'release' ? [0.72, 0.68, 0] : [0.18, 0.96, 0.1],
    combo: judge.stats.combo,
    scale,
    groundY: WATER_Y,
    color: verdict === 'miss' ? undefined : lighten(p.color, 0.18),
  });
  ctxRef.hitstop(Math.min(FEEL.hitstopMax, f.hitstop * (big ? 2.1 : 1)));
  if (verdict === 'miss') {
    anim?.react('miss', { dur: 0.5 });
  } else {
    anim?.impulse(meta.kind === 'release' ? 0.85 * scale : -0.85, 0);
    if (big) {
      ctxRef.stage.punchZoom(1.13);
      env?.crowd?.cheer?.(1.5);
    }
  }
}

function popPlatform(i, amount) {
  if (i >= 0 && i < popArr.length) popArr[i] = Math.max(popArr[i], amount);
}

function pushHud() {
  const hud = ctxRef.ui.hud;
  hud.setScore(Math.round(1000 * points / chart.maxPoints));
  hud.setCombo(judge.stats.combo);
  hud.setAccuracy(judged ? points / (judged ? maxPointsJudged() : 1) : 1);
}

let judgedWeight = 0;
function maxPointsJudged() { return Math.max(1, judgedWeight * 1000); }

// ------------------------------------------------------------------- build

function buildPlatforms(scene, mats) {
  const n = plats.length;
  popArr = new Float32Array(n);
  glowArr = new Float32Array(n);
  litArr = new Float32Array(n);

  slabGeo = whiteVertexColors(new RoundedBoxGeometry(1, 1, 1, 3, 0.14));
  slabGeo.translate(0, -0.5, 0);                 // top face at local y = 0
  slabMat = mats.toon({
    color: 0xffffff, bands: 3, rim: 1.35, pulse: 0.30, emissive: 0.06,
    vertexColors: true, name: 'bbPlatform',
  });
  slabMesh = new THREE.InstancedMesh(slabGeo, slabMat, n);
  slabMesh.frustumCulled = false;
  slabMesh.name = 'bb:platforms';

  // The glowing key-top: unlit, so it holds its pitch colour in shadow and
  // reads as the note face of an instrument rather than a painted stripe.
  capGeo = whiteVertexColors(new THREE.BoxGeometry(1, 1, 1));
  capGeo.translate(0, -0.5, 0);
  capMat = new THREE.MeshBasicMaterial({ vertexColors: true, toneMapped: false, fog: true });
  capMat.name = 'bbPlatformCap';
  capMesh = new THREE.InstancedMesh(capGeo, capMat, n);
  capMesh.frustumCulled = false;

  postGeo = whiteVertexColors(new THREE.CylinderGeometry(0.13, 0.17, 1, 8, 1));
  postGeo.translate(0, -0.5, 0);
  postMat = mats.toon({
    color: 0xffffff, bands: 2, rim: 0.8, pulse: 0.10, vertexColors: true, name: 'bbPost',
  });
  postMesh = new THREE.InstancedMesh(postGeo, postMat, n);
  postMesh.frustumCulled = false;

  for (let i = 0; i < n; i++) {
    const p = plats[i];
    writeSlab(i, 0);
    // stilt down into the water
    dummy.position.set(p.centerX, p.centerY - SLAB_THICK, p.kind === 'R' ? -0.15 : 0.05);
    dummy.rotation.set(0, 0, 0);
    dummy.scale.set(1, Math.max(0.4, p.centerY - SLAB_THICK - WATER_Y + 0.5), 1);
    dummy.updateMatrix();
    postMesh.setMatrixAt(i, dummy.matrix);
    tmpColor.setHex(lighten(p.color, -0.0)).multiplyScalar(0.45);
    postMesh.setColorAt(i, tmpColor);
  }
  postMesh.instanceMatrix.needsUpdate = true;
  if (postMesh.instanceColor) postMesh.instanceColor.needsUpdate = true;

  scene.add(slabMesh, capMesh, postMesh);
}

function writeSlab(i, pop) {
  const p = plats[i];
  const squash = pop * 0.55;
  const drop = pop * 0.20;
  const cs = Math.cos(p.tilt), sn = Math.sin(p.tilt);
  const cx = p.anchorX + cs * p.width * 0.5;
  const cy = p.anchorY + sn * p.width * 0.5 - drop;

  dummy.position.set(cx, cy, 0);
  dummy.rotation.set(0, 0, p.tilt);
  dummy.scale.set(p.width, SLAB_THICK * (1 - squash) + 0.02, SLAB_DEPTH * (1 + pop * 0.16));
  dummy.updateMatrix();
  slabMesh.setMatrixAt(i, dummy.matrix);

  dummy.position.set(cx, cy + 0.012, 0);
  dummy.scale.set(p.width * 0.94, 0.075 + pop * 0.05, SLAB_DEPTH * 0.86 * (1 + pop * 0.16));
  dummy.updateMatrix();
  capMesh.setMatrixAt(i, dummy.matrix);
}

function buildWater(scene, mats) {
  waterTex = makeWaterTexture();
  waterGeo = new THREE.PlaneGeometry(900, 340);
  waterMat = mats.toon({
    color: 0x1aa6c8, bands: 2, rim: 0.45, pulse: 0.24, map: waterTex, name: 'bbWater',
  });
  waterMesh = new THREE.Mesh(waterGeo, waterMat);
  waterMesh.rotation.x = -Math.PI / 2;
  waterMesh.position.set(0, WATER_Y, -20);
  waterMesh.userData.keepMaterial = true;
  scene.add(waterMesh);

  // Lane buoys: world-space markers that stream past, recycled around the
  // camera. They are the speed cue — without something at ground level moving,
  // constant motion reads as a still image with a bobbing character on it.
  buoyGeo = whiteVertexColors(new THREE.TorusGeometry(0.55, 0.2, 6, 12));
  buoyGeo.rotateX(Math.PI / 2);
  buoyMat = mats.toon({
    color: 0xffffff, bands: 2, rim: 1.2, pulse: 0.4, vertexColors: true, flat: true, name: 'bbBuoy',
  });
  buoyMesh = new THREE.InstancedMesh(buoyGeo, buoyMat, 28);
  buoyMesh.frustumCulled = false;
  scene.add(buoyMesh);
}

function buildFinishGate(scene, mats) {
  const p = plats[chart.finishIndex];
  gateGroup = new THREE.Group();
  gateGroup.position.set(p.contactX + 1.2, 0, 0);
  gateGeos = [];
  gateMats = [];

  const archGeo = new THREE.TorusGeometry(4.6, 0.30, 8, 40, Math.PI);
  const archMat = mats.toon({
    color: 0xffd93d, bands: 2, rim: 1.6, pulse: 0.7, emissive: 0.5, name: 'bbGateArch',
  });
  const arch = new THREE.Mesh(archGeo, archMat);
  arch.position.set(0, p.y - 0.1, 0);
  gateGroup.add(arch);
  gateGeos.push(archGeo); gateMats.push(archMat);

  const banGeo = new RoundedBoxGeometry(7.0, 1.3, 0.5, 2, 0.18);
  const banMat = mats.toon({
    color: 0xff5d73, bands: 2, rim: 1.2, pulse: 0.5, emissive: 0.3, name: 'bbGateBanner',
  });
  const banner = new THREE.Mesh(banGeo, banMat);
  banner.position.set(0, p.y + 4.5, -0.35);
  gateGroup.add(banner);
  gateGeos.push(banGeo); gateMats.push(banMat);

  scene.add(gateGroup);
}

// ------------------------------------------------------------------- module

export default {
  id: 'bounce-brigade',
  name: 'Bounce Brigade',
  blurb: 'The platforms are the tune. Land every landing.',
  bpm: BPM,
  durationBars: 32,
  controls: 'a',

  load(ctx) {
    ctxRef = ctx;
    rng = makeRng(0xb0c3);
    chart = buildChart();
    plats = chart.platforms;

    started = false; done = false; result = null; finished = false; finishTime = 0;
    cursor = 0; held = false; holdStartBeat = -1e9; holdStartTime = 0;
    points = 0; judged = 0; judgedWeight = 0; missCount = 0; bestCombo = 0;
    chargeGlow = 0; launchFlash = 0; stumble = 0; lastUiBeat = -1e9; lastBeat = -1e9;
    motionStopBeat = Infinity;
    contactVerdict = new Array(plats.length).fill(null);
    releaseVerdict = new Array(plats.length).fill(null);
    splashed = new Array(plats.length).fill(false);
    landedPop = new Array(plats.length).fill(false);

    root = new THREE.Group();
    root.name = 'bounce-brigade';
    ctx.scene.add(root);
    ctx.scene.userData.palette = 'bounce-brigade';
    ctx.stage.setPalette('bounce-brigade');

    const mats = ctx.stage.look.materials;
    buildWater(root, mats);
    buildPlatforms(root, mats);
    buildFinishGate(root, mats);

    // Compose the set from the env kit, minus its ground: we have water.
    // Everything in it is radially symmetric behind the action, so it can ride
    // along with the camera and read as "the far side of the pool".
    env = ctx.stage.createEnv(ctx.scene);
    env.addBackdrop({ arches: 5, radius: 18, spacing: 6, z: -16, skyline: 24, skylineZ: -52 });
    env.addCrowd({ count: 120, radius: 21, tiers: 3, y: -3.0, z: -12 });
    env.addFloaters({ count: 18, innerR: 15, outerR: 34, minY: -2, maxY: 16, size: 1.1 });
    env.root.position.y = 0;

    // --- the bouncer ---------------------------------------------------------
    const hero = ctx.players?.[0];
    bouncerRoot = new THREE.Group();
    bouncerRoot.name = 'bb:bouncer';
    bouncer = makeCharacter({
      palette: hero?.palette ?? 'lagoon', build: hero?.build || 'round', seed: 0xb0c3, detail: 'full',
      scale: BOUNCER_SCALE, name: 'bb:hero',
    });
    hero?.dress?.(bouncer);
    bouncer.rotation.y = BOUNCER_FACING;
    bouncerRoot.add(bouncer);
    root.add(bouncerRoot);
    anim = makeAnimator(bouncer, { seed: 0xb0c3 });

    // --- fx ------------------------------------------------------------------
    ctx.fx.attach(ctx.scene);
    // We compose our own verdict response, so the legacy bus bridge must not
    // fire a second one on top of it.
    ctx.fx.setAutoVerdict(false);
    ctx.fx.setGroundY(WATER_Y);
    trail = ctx.fx.trail({ color: 0xbdfff2, width: 0.2 });

    // --- camera --------------------------------------------------------------
    camAnchorY = plats[0].y;
    camY = camAnchorY;
    ctx.camera.fov = 50;
    ctx.camera.updateProjectionMatrix();
    ctx.stage.rig.frame({
      target: [plats[0].contactX + CAM_LEAD, camY + 1.0, 0],
      distance: CAM_DISTANCE, height: CAM_HEIGHT, yaw: CAM_YAW,
      lambda: CAM_LAMBDA, immediate: true,
    });
    // Real shadows ride along with the camera as it follows the platforms.
    ctx.stage.look.setShadowFocus('rig', 8);

    ctx.ui.hud.mount();

    // Judge before the first pushHud(): pushHud reads judge.stats, and building
    // the HUD one line too early threw during load(), which took the whole boot
    // down rather than just this scene.
    judge = new NoteJudge();
    noteMap = new Map();
    pushHud();

    // Dev/test hook. `verify.mjs` drives real hold/release through this so a
    // two-beat charge can be delivered at exact audio times under a software
    // renderer that only manages 3fps.
    if (typeof window !== 'undefined') {
      telemetry = {
        chargeRuns: [],
        minAirY: {},
        pressed: 0,
        released: 0,
      };
      window.__BOUNCE__ = {
        chart: () => ({
          platforms: plats.map((p) => ({
            index: p.index, beat: p.beat, deg: p.deg, midi: p.midi, y: p.y,
            kind: p.kind, departBeat: p.departBeat, nextBeat: p.nextBeat,
            flightBeats: p.flightBeats, x: p.contactX,
          })),
          waterY: WATER_Y,
        }),
        /** Inject a press with an exact audio time, exactly like the harness bot. */
        press: (down, atTime) => {
          if (!ctxRef) return;
          handleEvents(ctxRef, [{ action: 'a', time: atTime, down: !!down, source: 'test' }]);
        },
        state: () => ({
          beat: ctxRef.clock.beat,
          x: M.x, y: M.y, phase: M.phase, outcome: M.outcome, platform: M.k,
          held, points, judged, missCount, combo: judge.stats.combo,
          contactVerdict: contactVerdict.slice(),
          releaseVerdict: releaseVerdict.slice(),
          minAirY: telemetry.minAirY,
        }),
        reset: () => { telemetry.minAirY = {}; },
      };
    }
  },

  start(ctx) {
    ctx.clock.setBpm(BPM);
    ctx.clock.start(ctx.clock.now() + 0.45, -LEAD_IN_BARS * 4);

    // Notes carry absolute audio times, derived from the chart's beats.
    const notes = chart.notes.map((n) => {
      const note = { time: ctx.clock.timeAt(n.beat), action: n.action };
      return { note, meta: n };
    });
    judge.load(notes.map((x) => x.note));
    // `load()` clones, so map by identity on the judge's own copies.
    for (let i = 0; i < judge.notes.length; i++) {
      const jn = judge.notes[i];
      const src = chart.notes.find(
        (n) => Math.abs(ctx.clock.timeAt(n.beat) - jn.time) < 1e-6 && n.action === jn.action
      );
      if (src) noteMap.set(jn, src);
    }
    judge.onJudged = (n, v, e) => {
      const meta = noteMap.get(n);
      judgedWeight += meta ? meta.weight : 1;
      onJudged(n, v, e);
    };

    // The backing track starts on the lead-in, so the player hears two bars of
    // groove before the first scored platform.
    ctx.audio.music.play('bounce-brigade', { atBeat: -LEAD_IN_BARS * 4 });

    unsubBeat = ctx.onBeat((b, t) => {
      if (b < 0) ctx.audio.sfx('count', t, ((b % 4) + 4) % 4);
    });

    ctx.ui.banner('BOUNCE BRIGADE', { sub: 'The platforms are the tune.', life: 1.5 });
    started = true;
  },

  update(ctx, dt, beat) {
    if (!root) return;
    judge.update(ctx.clock.now());

    const m = motionAt(beat);
    const p = plats[m.k];

    // ---- countdown + section banners ---------------------------------------
    const wholeBeat = Math.floor(beat);
    if (wholeBeat !== lastUiBeat) {
      lastUiBeat = wholeBeat;
      if (wholeBeat >= -3 && wholeBeat <= -1) {
        ctx.ui.banner(String(-wholeBeat), { life: 0.5, color: '#bdfff2' });
      } else if (wholeBeat === 0) {
        ctx.ui.banner('GO!', { life: 0.75, color: '#9ee87a' });
      } else if (wholeBeat === 96) {
        ctx.ui.banner('SPRINT!', { sub: 'eighths — do not stop', life: 1.0, color: '#ffd93d' });
      }
    }

    // ---- the free demonstration bounce off the runway -----------------------
    if (lastBeat < -1 && beat >= -1) {
      playPlatformNote(plats[0], 'great', ctx.clock.rawNow() + 0.01, 1);
      anim?.impulse(-0.8, 0);
      popPlatform(0, 0.7);
      ctx.fx.ring([m.x, plats[0].y + 0.1, 0], {
        color: 0xbdfff2, life: 0.4, from: 0.2, to: 1.6, thick0: 0.16, thick1: 0.02,
      });
    }
    lastBeat = beat;

    // ---- charge state -------------------------------------------------------
    const charging = p.charge && m.phase === 'surface' && held
      && contactVerdict[p.index] && contactVerdict[p.index] !== 'miss';
    const chargeFill = charging ? clamp01((beat - holdStartBeat) / p.roll) : 0;
    chargeGlow = damp(chargeGlow, charging ? chargeFill : 0, 12, dt);
    launchFlash = damp(launchFlash, 0, 7, dt);
    stumble = damp(stumble, 0, 3.2, dt);

    if (charging) {
      // one ring per beat of the coil, so the hold has a visible metronome
      const f = beatFrac(beat);
      if (f < dt * (BPM / 60) * 1.05) {
        ctx.fx.ring([m.x, m.y + 0.4, 0], {
          color: lighten(p.color, 0.35), life: 0.34,
          from: 1.5, to: 0.35, thick0: 0.06, thick1: 0.22, alpha: 0.7,
        });
      }
    }

    // ---- splash on a failed charge -----------------------------------------
    if (m.phase === 'air' && m.outcome === 'plunge' && !splashed[m.k] && m.y <= SPLASH_Y + 0.25) {
      splashed[m.k] = true;
      ctx.fx.burst([m.x, WATER_Y + 0.1, 0], {
        count: 30, color: 0xbdfff2, speed: 8, size: 0.18, life: 0.7, gravity: -13,
      });
      ctx.fx.ring([m.x, WATER_Y + 0.05, 0], {
        color: 0xd8ffff, life: 0.55, from: 0.3, to: 4.2, thick0: 0.14, thick1: 0.02,
        billboard: false, normal: [0, 1, 0],
      });
      ctx.stage.shake(0.12, [0, -1, 0]);
      ctx.audio.voices.hit(ctx.clock.rawNow() + 0.01, {
        gain: 0.22, d: 0.24, hp: 260, lp: 2600, rev: 0.3,
      });
      anim?.react('miss', { dur: 0.6 });
    }

    // ---- landing pop --------------------------------------------------------
    if (m.phase === 'surface' && !landedPop[m.k] && m.k > 0) {
      landedPop[m.k] = true;
      if (contactVerdict[m.k] === null) popPlatform(m.k, 0.35);
    }

    // ---- the bouncer --------------------------------------------------------
    bouncerRoot.position.set(m.x, m.y, 0);
    bouncerRoot.rotation.z = m.spin + m.lean;
    anim.update(dt, beat);
    if (trail) {
      trail.set(m.x - 0.15, m.y + 0.55, 0.05);
      trail.setWidth(0.14 + 0.16 * chargeGlow + 0.2 * launchFlash);
      trail.setColor(chargeGlow > 0.15 ? lighten(p.color, 0.5) : 0xbdfff2);
    }

    // ---- platforms ----------------------------------------------------------
    const lo = Math.max(0, m.k - 4);
    const hi = Math.min(plats.length - 1, m.k + HIGHLIGHT_WINDOW);
    let dirtyMatrix = false;
    for (let i = lo; i <= hi; i++) {
      const q = plats[i];
      const prev = popArr[i];
      popArr[i] = damp(popArr[i], 0, 9, dt);
      if (prev > 0.002 || popArr[i] > 0.002) { writeSlab(i, popArr[i]); dirtyMatrix = true; }

      // Telegraph: the platform you must hit next brightens over the beat
      // before contact. Motion, not a symbol — it swells toward you.
      const lead = q.beat - beat;
      let want = 0;
      if (lead > 0 && lead < 1.15) want = smoothstep(1 - lead / 1.15);
      else if (lead <= 0 && beat < q.departBeat) want = 1;
      if (i === m.k && q.charge) want = Math.max(want, 0.35 + 0.65 * chargeGlow);
      glowArr[i] = damp(glowArr[i], want, 14, dt);

      const heat = clamp01(glowArr[i] * 0.55 + popArr[i] * 0.6);
      tmpColor.setHex(lighten(q.color, heat * 0.55));
      capMesh.setColorAt(i, tmpColor);
      tmpColor.setHex(lighten(q.color, 0.05 + heat * 0.3)).multiplyScalar(0.9);
      slabMesh.setColorAt(i, tmpColor);
    }
    if (dirtyMatrix) {
      slabMesh.instanceMatrix.needsUpdate = true;
      capMesh.instanceMatrix.needsUpdate = true;
    }
    if (slabMesh.instanceColor) slabMesh.instanceColor.needsUpdate = true;
    if (capMesh.instanceColor) capMesh.instanceColor.needsUpdate = true;

    // ---- camera -------------------------------------------------------------
    // Follow the MELODY CONTOUR, not the ballistic arc: anchoring the camera to
    // a bouncing body makes the whole frame bounce, which is unreadable.
    const nextP = plats[Math.min(plats.length - 1, m.k + 1)];
    const aheadP = plats[Math.min(plats.length - 1, m.k + 3)];
    const anchor = (p.y + nextP.y + aheadP.y) / 3;
    camAnchorY = damp(camAnchorY, anchor, 2.4, dt);
    camY = damp(camY, camAnchorY * 0.82 + m.y * 0.18, 5.5, dt);
    camTarget.set(m.x + CAM_LEAD, camY + 1.05, 0);
    ctx.stage.rig.frame({
      target: camTarget,
      distance: CAM_DISTANCE + 1.9 * chargeGlow + (finished ? 3.4 : 0),
      height: CAM_HEIGHT, yaw: CAM_YAW, lambda: CAM_LAMBDA,
    });

    // The set and the ambient sparkle travel with us.
    env.root.position.x = damp(env.root.position.x, m.x, 8, dt);
    ctx.fx.setFocus([m.x, m.y + 0.4, 0]);

    // ---- water + buoys ------------------------------------------------------
    waterMesh.position.x = m.x;
    waterTex.offset.x = m.x * 0.012;
    updateBuoys(m.x, beat);

    // ---- finale -------------------------------------------------------------
    const finishP = plats[chart.finishIndex];
    if (!finished && beat >= finishP.beat) {
      finished = true;
      finishTime = ctx.clock.now();
      motionStopBeat = finishP.beat + 1.3;
      const hitIt = contactVerdict[finishP.index] && contactVerdict[finishP.index] !== 'miss';
      ctx.ui.banner(hitIt ? 'FINISH!' : 'FINISH', {
        sub: hitIt ? 'the tune is yours' : 'you got there',
        life: 2.0, color: hitIt ? '#ffd93d' : '#cfd3ff',
      });
      ctx.audio.sfx('fanfare', ctx.clock.rawNow() + 0.02);
      ctx.fx.confetti([finishP.contactX + 1.5, finishP.y + 2.4, 0], { count: 150, speed: 9, up: 1.1 });
      ctx.stage.flash(0.3, '#ffd93d');
      ctx.stage.shake(0.42, [0, 1, 0]);
      env?.crowd?.cheer?.(2);
      anim?.react(hitIt ? 'perfect' : 'good', { dur: 2.4 });
    }
    if (finished) {
      if ((Math.floor(beat) !== Math.floor(beat - dt * BPM / 60))) {
        ctx.fx.confetti([m.x + rng.range(-3, 3), finishP.y + 3.2, rng.range(-1, 1)], {
          count: 26, speed: 6.5, up: 1,
        });
      }
      if (!done && ctx.clock.now() - finishTime > 2.6 && judge.finished) finalise();
    }
    if (!done && beat > END_BEAT + 4) finalise();
  },

  input(ctx, events) {
    handleEvents(ctx, events);
  },

  result() {
    return result;
  },

  dispose(ctx) {
    unsubBeat?.(); unsubBeat = null;
    try { trail?.release(); } catch { /* ignore */ }
    trail = null;

    const mats = ctx?.stage?.look?.materials;
    const kill = (m) => {
      if (!m) return;
      mats?.owned?.delete?.(m);
      m.dispose();
    };

    slabMesh?.dispose?.(); capMesh?.dispose?.(); postMesh?.dispose?.(); buoyMesh?.dispose?.();
    slabGeo?.dispose(); capGeo?.dispose(); postGeo?.dispose(); buoyGeo?.dispose();
    kill(slabMat); kill(capMat); kill(postMat); kill(buoyMat);
    waterGeo?.dispose(); kill(waterMat); waterTex?.dispose();
    if (gateGeos) for (const g of gateGeos) g.dispose();
    if (gateMats) for (const m of gateMats) kill(m);

    bouncer?.dispose?.();
    env?.dispose?.();
    root?.removeFromParent?.();

    slabMesh = capMesh = postMesh = buoyMesh = null;
    slabGeo = capGeo = postGeo = buoyGeo = null;
    slabMat = capMat = postMat = buoyMat = null;
    waterMesh = waterGeo = waterMat = waterTex = null;
    gateGroup = gateGeos = gateMats = null;
    bouncer = bouncerRoot = anim = null;
    root = null; env = null; plats = null; chart = null; judge = null; noteMap = null;

    ctx?.ui?.hud?.unmount();
    if (typeof window !== 'undefined' && window.__BOUNCE__) delete window.__BOUNCE__;
    ctxRef = null;
  },
};

// ------------------------------------------------------------------- input

function handleEvents(ctx, events) {
  if (!judge) return;
  for (const e of events) {
    if (e.action !== 'a') continue;

    if (e.down) {
      if (!held) { held = true; holdStartTime = e.time; holdStartBeat = ctx.clock.beatAt(e.time); }
      if (telemetry) telemetry.pressed++;
      const r = judge.press('a', e.time);
      if (!r) ctx.audio.sfx('tick', Math.max(e.time, ctx.clock.rawNow() + 0.002), FEEL.ghostPressTick);
      continue;
    }

    // Key-up. In tap mode the release note is an ordinary 'a' claimed by the
    // second tap above, so there is nothing left to judge here — we only track
    // the flag so the coil animation knows the button is no longer down.
    held = false;
    if (telemetry) telemetry.released++;
  }
}

// ------------------------------------------------------------------- buoys

const BUOY_SPACING = UNITS_PER_BEAT * 2;
function updateBuoys(camX, beat) {
  const n = buoyMesh.count;
  const span = BUOY_SPACING * n;
  const base = Math.floor((camX - span * 0.35) / BUOY_SPACING);
  for (let i = 0; i < n; i++) {
    const k = base + i;
    const x = k * BUOY_SPACING;
    const z = -5.5 - ((k % 3) + 3) % 3 * 3.4;
    const bob = Math.sin(beat * Math.PI + k * 0.7) * 0.12;
    dummy.position.set(x, WATER_Y + 0.16 + bob, z);
    dummy.rotation.set(0, 0, 0);
    const s = 1 + 0.14 * Math.sin(beat * Math.PI * 2 - k);
    dummy.scale.set(s, 1, s);
    dummy.updateMatrix();
    buoyMesh.setMatrixAt(i, dummy.matrix);
    tmpColor.setHex(k % 4 === 0 ? 0xffd93d : 0xff7ad0);
    buoyMesh.setColorAt(i, tmpColor);
  }
  buoyMesh.instanceMatrix.needsUpdate = true;
  if (buoyMesh.instanceColor) buoyMesh.instanceColor.needsUpdate = true;
}

// ------------------------------------------------------------------ result

function finalise() {
  done = true;
  const accuracy = clamp01(points / chart.maxPoints);
  result = {
    score: Math.round(1000 * accuracy),
    accuracy,
    rank: rankFor(accuracy, missCount),
    stats: {
      ...judge.stats,
      notes: chart.notes.length,
      maxCombo: Math.max(bestCombo, judge.stats.maxCombo),
      bias: judge.bias,
    },
    highlights: [],
  };
}
