#!/usr/bin/env node
/**
 * Bake Mixamo clips into the toy rig's pose channels.
 *
 *   node tools/assets/bake-clips.mjs      (or npm --prefix web run assets:clips)
 *
 * Input: web/src/assets/chars/ybot.glb (committed — see its README).
 * Output: web/src/chars/clips.gen.js, a plain data module `anim.js` imports.
 *
 * For every frame at 30 fps the mocap skeleton is posed and measured:
 *  - hips / torso / head orientation as the rotation AWAY FROM REST, in the
 *    parent's frame (toy rest = identity, mocap rest = its T-pose, so
 *    "rotation from rest" is the quantity both rigs agree on);
 *  - each limb segment's DIRECTION in the torso (arms) or hips (legs) frame,
 *    solved into swing/lift/twist/bend by `web/src/chars/retarget.js`;
 *  - hip height change in units of leg length (a crouch on a lanky mocap
 *    body is a crouch on a stubby toy body).
 * Horizontal root motion was already pinned by convert-mixamo.
 *
 * Also written per clip: the hand-speed peak (`contact` — bat meets ball,
 * ball leaves hand) and, for loops, the motion's own tempo from the
 * autocorrelation of hip height, so the runtime can lock a dance to the beat.
 */

import { readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import path from 'node:path';

const ROOT = path.resolve(import.meta.dirname, '..', '..');
const WEB = path.join(ROOT, 'web');
const req = createRequire(path.join(WEB, 'package.json'));
const imp = (id) => import(pathToFileURL(req.resolve(id)).href);
const THREE = await imp('three');
const { GLTFLoader } = await imp('three/examples/jsm/loaders/GLTFLoader.js');
const { solveArm, solveLeg, eulerNear, unwrapInPlace } = await import(pathToFileURL(path.join(WEB, 'src/chars/retarget.js')).href);
const { HIPS_ORDER } = await import(pathToFileURL(path.join(WEB, 'src/chars/anim.js')).href);

const FPS = 30;
const OUT = path.join(WEB, 'src', 'chars', 'clips.gen.js');

/** Which clips ship, and how. `loop` clips get a tempo estimate. */
const BAKE = [
  { name: 'idle', loop: true },
  { name: 'ready', loop: true },
  { name: 'swing' },
  { name: 'pitch' },
  { name: 'taunt' },
  { name: 'dance', loop: true },
];

export const CHANNELS = [
  'hipsRotX', 'hipsRotY', 'hipsRotZ', 'hipsY',
  'torsoRotX', 'torsoRotY', 'torsoRotZ',
  'headRotX', 'headRotY', 'headRotZ',
  'armLSwing', 'armLLift', 'armLTwist', 'armLBend',
  'armRSwing', 'armRLift', 'armRTwist', 'armRBend',
  'legLSwing', 'legLSpread', 'legLBend',
  'legRSwing', 'legRSpread', 'legRBend',
];

const buf = readFileSync(path.join(WEB, 'src/assets/chars/ybot.glb'));
const gltf = await new Promise((res, rej) => new GLTFLoader().parse(
  buf.buffer.slice(buf.byteOffset, buf.byteOffset + buf.byteLength), '', res, rej));
const scene = gltf.scene;
scene.updateMatrixWorld(true);
const bone = (n) => {
  const b = scene.getObjectByName(`mixamorig${n}`);
  if (!b) throw new Error(`bone ${n} missing`);
  return b;
};

const B = {
  hips: bone('Hips'), spine: bone('Spine2'), head: bone('Head'),
  lArm: bone('LeftArm'), lFore: bone('LeftForeArm'), lHand: bone('LeftHand'),
  rArm: bone('RightArm'), rFore: bone('RightForeArm'), rHand: bone('RightHand'),
  lUp: bone('LeftUpLeg'), lLeg: bone('LeftLeg'), lFoot: bone('LeftFoot'),
  rUp: bone('RightUpLeg'), rLeg: bone('RightLeg'), rFoot: bone('RightFoot'),
};

const wp = (o) => new THREE.Vector3().setFromMatrixPosition(o.matrixWorld);
const wq = (o) => o.getWorldQuaternion(new THREE.Quaternion());

// ---- rest measurements (bind pose = T-pose) --------------------------------
const rest = {
  hipsQ: wq(B.hips), spineQ: wq(B.spine), headQ: wq(B.head),
  hipsY: wp(B.hips).y,
};
const legLen = wp(B.lUp).distanceTo(wp(B.lLeg)) + wp(B.lLeg).distanceTo(wp(B.lFoot));
// The toy's `R` side sits at +X (rig.js: sx = +1). Map mocap sides by where
// they actually are, not by their names.
const leftIsPlusX = wp(B.lHand).x > 0;
const side = leftIsPlusX
  ? { R: { arm: B.lArm, fore: B.lFore, hand: B.lHand, up: B.lUp, leg: B.lLeg, foot: B.lFoot },
    L: { arm: B.rArm, fore: B.rFore, hand: B.rHand, up: B.rUp, leg: B.rLeg, foot: B.rFoot } }
  : { L: { arm: B.lArm, fore: B.lFore, hand: B.lHand, up: B.lUp, leg: B.lLeg, foot: B.lFoot },
    R: { arm: B.rArm, fore: B.rFore, hand: B.rHand, up: B.rUp, leg: B.rLeg, foot: B.rFoot } };
console.log(`rest: hips ${rest.hipsY.toFixed(3)}m, leg ${legLen.toFixed(3)}m, mocap Left -> toy ${leftIsPlusX ? 'R' : 'L'}`);

const invRest = {
  hips: rest.hipsQ.clone().invert(), spine: rest.spineQ.clone().invert(), head: rest.headQ.clone().invert(),
};

const mixer = new THREE.AnimationMixer(scene);
const out = {};

for (const spec of BAKE) {
  const clip = gltf.animations.find((a) => a.name === spec.name);
  if (!clip) throw new Error(`clip ${spec.name} missing from ybot.glb`);
  mixer.stopAllAction();
  const act = mixer.clipAction(clip);
  act.reset().play();
  const n = Math.floor(clip.duration * FPS) + 1;
  const ch = Object.fromEntries(CHANNELS.map((k) => [k, new Float32Array(n)]));
  const handSpeed = { L: new Float32Array(n), R: new Float32Array(n) };
  const prevHand = { L: null, R: null };
  const prevTwist = { L: null, R: null };

  for (let i = 0; i < n; i++) {
    act.time = Math.min(clip.duration, i / FPS);
    mixer.update(0);
    scene.updateMatrixWorld(true);

    const Qh = wq(B.hips).multiply(invRest.hips);          // world rotation from rest
    const Qs = wq(B.spine).multiply(invRest.spine);
    const Qhd = wq(B.head).multiply(invRest.head);
    const iQh = Qh.clone().invert();
    const iQs = Qs.clone().invert();

    // Continuous with the previous frame (not the canonical ±π/2 branch):
    // a spin past a quarter turn must not flip x and z by π between frames.
    // Hips in the animator's HIPS_ORDER (yaw outermost), torso/head in XYZ.
    const near = (q, k, order = 'XYZ') => eulerNear(q, i ? [ch[k + 'X'][i - 1], ch[k + 'Y'][i - 1], ch[k + 'Z'][i - 1]] : [0, 0, 0], order);
    const [hx, hy, hz] = near(Qh, 'hipsRot', HIPS_ORDER);
    const [tx, ty, tz] = near(iQh.clone().multiply(Qs), 'torsoRot');
    const [ex, ey, ez] = near(iQs.clone().multiply(Qhd), 'headRot');
    ch.hipsRotX[i] = hx; ch.hipsRotY[i] = hy; ch.hipsRotZ[i] = hz;
    ch.torsoRotX[i] = tx; ch.torsoRotY[i] = ty; ch.torsoRotZ[i] = tz;
    ch.headRotX[i] = ex; ch.headRotY[i] = ey; ch.headRotZ[i] = ez;
    ch.hipsY[i] = (wp(B.hips).y - rest.hipsY) / legLen;

    for (const s of ['L', 'R']) {
      const sx = s === 'L' ? -1 : 1;
      const b = side[s];
      const shoulder = wp(b.arm), elbow = wp(b.fore), wrist = wp(b.hand);
      const u = elbow.clone().sub(shoulder).normalize().applyQuaternion(iQs);
      const f = wrist.clone().sub(elbow).normalize().applyQuaternion(iQs);
      const a = solveArm(u, f, sx, prevTwist[s] !== null ? { twist: prevTwist[s] } : null);
      prevTwist[s] = a.twist;
      ch[`arm${s}Swing`][i] = a.swing; ch[`arm${s}Lift`][i] = a.lift;
      ch[`arm${s}Twist`][i] = a.twist; ch[`arm${s}Bend`][i] = a.bend;

      const hip = wp(b.up), knee = wp(b.leg), ankle = wp(b.foot);
      const lu = knee.clone().sub(hip).normalize().applyQuaternion(iQh);
      const lf = ankle.clone().sub(knee).normalize().applyQuaternion(iQh);
      const l = solveLeg(lu, lf, sx);
      ch[`leg${s}Swing`][i] = l.swing; ch[`leg${s}Spread`][i] = l.spread; ch[`leg${s}Bend`][i] = l.bend;

      if (prevHand[s]) handSpeed[s][i] = wrist.distanceTo(prevHand[s]) * FPS;
      prevHand[s] = wrist;
    }
  }
  for (const k of CHANNELS) if (k !== 'hipsY' && !k.endsWith('Bend')) unwrapInPlace(ch[k]);

  // Contact: the fastest hand's speed peak.
  const peakOf = (a) => a.reduce((best, v, i) => (v > a[best] ? i : best), 0);
  const pR = peakOf(handSpeed.R), pL = peakOf(handSpeed.L);
  const fastSide = handSpeed.R[pR] >= handSpeed.L[pL] ? 'R' : 'L';
  const contact = (fastSide === 'R' ? pR : pL) / FPS;

  // Tempo of a loop: the hip bob happens once per beat, so the first
  // autocorrelation peak of hip height AFTER its first trough is one beat.
  // (The global max is always the smallest lag — a smooth signal looks most
  // like itself shifted by one frame.)
  let tempo = null;
  if (spec.loop) {
    const y = ch.hipsY;
    const mean = y.reduce((s, v) => s + v, 0) / n;
    const maxLag = Math.min(Math.round(1.5 * FPS), Math.floor(n / 2));
    const r = new Float32Array(maxLag + 1);
    for (let lag = 0; lag <= maxLag; lag++) {
      let s = 0;
      for (let i = 0; i + lag < n; i++) s += (y[i] - mean) * (y[i + lag] - mean);
      r[lag] = s / (n - lag);
    }
    let trough = 1;
    while (trough < maxLag && r[trough + 1] < r[trough]) trough++;
    let peak = trough;
    for (let lag = trough; lag <= maxLag; lag++) if (r[lag] > r[peak]) peak = lag;
    // Only trust a real periodicity: the peak must recover well above zero.
    if (peak > trough && peak < maxLag && r[peak] > 0.25 * r[0]) {
      // Parabolic sub-frame refinement of the peak lag.
      const a = r[peak - 1], b = r[peak], c = r[peak + 1];
      const lag = peak + (a - c) / (2 * (a - 2 * b + c) || 1);
      tempo = Math.round(((60 * FPS) / lag) * 10) / 10;
    }
  }

  // Downbeat phase of a loop: the first hip LOW (the body lands on the beat).
  let downbeat = null;
  if (tempo) {
    const period = Math.round((60 * FPS) / tempo);
    let lo = 0;
    for (let i = 1; i < Math.min(n, period + 1); i++) if (ch.hipsY[i] < ch.hipsY[lo]) lo = i;
    downbeat = lo / FPS;
  }

  out[spec.name] = {
    n, duration: (n - 1) / FPS, contact, contactHand: fastSide, tempo, downbeat,
    ch: Object.fromEntries(CHANNELS.map((k) => [k, Array.from(ch[k], (v) => Math.round(v * 1000))])),
  };
  const r = (k) => { const a = ch[k]; let lo = Infinity, hi = -Infinity; for (const v of a) { lo = Math.min(lo, v); hi = Math.max(hi, v); } return `${lo.toFixed(2)}..${hi.toFixed(2)}`; };
  console.log(`${spec.name.padEnd(6)} ${n} frames  contact ${contact.toFixed(2)}s (${fastSide})  tempo ${tempo ?? '-'}  `
    + `armRSwing ${r('armRSwing')}  torsoRotY ${r('torsoRotY')}  hipsY ${r('hipsY')}`);
}

const header = `/**
 * GENERATED by tools/assets/bake-clips.mjs from web/src/assets/chars/ybot.glb
 * (Mixamo — see web/src/assets/chars/README.md). Do not edit by hand.
 *
 * Pose-channel tracks for the toy rig at ${FPS} fps, values x1000 (radians;
 * hipsY in leg-lengths). \`contact\` = hand-speed peak (s), \`tempo\` = the
 * loop's own beats per minute, for beat-locking.
 */
`;
writeFileSync(OUT, `${header}export const CLIP_FPS = ${FPS};\nexport const CLIP_CHANNELS = ${JSON.stringify(CHANNELS)};\nexport const CLIPS = ${JSON.stringify(out)};\n`);
console.log(`wrote ${path.relative(ROOT, OUT)} (${(readFileSync(OUT).length / 1024).toFixed(0)} KB)`);
