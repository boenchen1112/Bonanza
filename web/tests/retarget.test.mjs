/**
 * Retarget solver tests. The solver maps mocap limb directions onto the toy
 * rig's pose channels (swing/lift/twist/bend), and the bake bakes whatever
 * it returns into every clip — so the check that matters is geometric: pose
 * the rig with the solved channels and the limbs must point where the mocap
 * limbs pointed.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import { solveArm, solveLeg, armFK, legFK, eulerXYZ, eulerNear, unwrapInPlace } from '../src/chars/retarget.js';

function rng(seed) {
  let s = seed >>> 0;
  return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296);
}
const near = (a, b, eps, msg) => assert.ok(a.distanceTo(b) < eps, `${msg}: ${a.toArray().map((v) => v.toFixed(3))} vs ${b.toArray().map((v) => v.toFixed(3))}`);

test('arm FK at the zero pose hangs straight down', () => {
  const { u, f } = armFK({ swing: 0, lift: 0, twist: 0, bend: 0 }, 1);
  near(u, new THREE.Vector3(0, -1, 0), 1e-9, 'upper');
  near(f, new THREE.Vector3(0, -1, 0), 1e-9, 'fore');
});

test('arm channels keep their documented meaning (forward / outward / elbow)', () => {
  // swing is forward-positive: the upper arm moves toward +Z (the face).
  assert.ok(armFK({ swing: 0.8, lift: 0, twist: 0, bend: 0 }, 1).u.z > 0.5);
  // lift is outward-positive on BOTH sides (sx mirrors it).
  assert.ok(armFK({ swing: 0, lift: 0.8, twist: 0, bend: 0 }, 1).u.x > 0.5);
  assert.ok(armFK({ swing: 0, lift: 0.8, twist: 0, bend: 0 }, -1).u.x < -0.5);
  // bend flexes the forearm forward of a hanging upper arm.
  assert.ok(armFK({ swing: 0, lift: 0, twist: 0, bend: 1.2 }, 1).f.z > 0.5);
});

test('solveArm round-trips random bent poses on both sides', () => {
  const r = rng(11);
  for (let i = 0; i < 300; i++) {
    const sx = i % 2 ? 1 : -1;
    const pose = {
      swing: (r() * 2 - 1) * 2.2, lift: (r() * 2 - 1) * 1.6,
      twist: (r() * 2 - 1) * 1.2, bend: 0.15 + r() * 2.3,
    };
    const { u, f } = armFK(pose, sx);
    const got = solveArm(u, f, sx);
    const back = armFK(got, sx);
    near(back.u, u, 1e-6, `upper #${i}`);
    near(back.f, f, 1e-6, `fore #${i}`);
    assert.ok(Math.abs(got.bend - pose.bend) < 1e-6, `bend #${i}`);
  }
});

test('solveArm with a straight arm keeps the previous twist instead of flipping', () => {
  const { u, f } = armFK({ swing: 0.4, lift: 0.3, twist: 0.7, bend: 0 }, 1);
  const got = solveArm(u, f, 1, { twist: 0.7 });
  near(armFK(got, 1).u, u, 1e-6, 'upper');
  assert.ok(Math.abs(got.twist - 0.7) < 1e-6, `twist ${got.twist}`);
  assert.equal(got.bend, 0);
});

test('solveLeg round-trips random poses (no twist DOF on legs)', () => {
  const r = rng(5);
  for (let i = 0; i < 300; i++) {
    const sx = i % 2 ? 1 : -1;
    const pose = { swing: (r() * 2 - 1) * 1.4, spread: (r() * 2 - 1) * 0.8, bend: r() * 2.2 };
    const { u, f } = legFK(pose, sx);
    const got = solveLeg(u, f, sx);
    const back = legFK(got, sx);
    near(back.u, u, 1e-6, `thigh #${i}`);
    near(back.f, f, 1e-6, `shin #${i}`);
  }
});

test('eulerXYZ matches three.js Euler(XYZ) decomposition', () => {
  const r = rng(3);
  for (let i = 0; i < 50; i++) {
    const e = new THREE.Euler((r() - 0.5) * 2, (r() - 0.5) * 2, (r() - 0.5) * 2, 'XYZ');
    const q = new THREE.Quaternion().setFromEuler(e);
    const [x, y, z] = eulerXYZ(q);
    const q2 = new THREE.Quaternion().setFromEuler(new THREE.Euler(x, y, z, 'XYZ'));
    assert.ok(Math.abs(Math.abs(q.dot(q2)) - 1) < 1e-9, `#${i}`);
  }
});

test('eulerNear follows two full spins (yaw-first hips order) with no flips', () => {
  // A dancer turning round on the spot with a little lean. The canonical
  // decomposition keeps the middle angle in ±π/2, so past a quarter turn it
  // flipped the other two by π in one frame — a linear sample across that
  // frame drew the body face-down. The hips use YXZ (yaw outermost, so a spin
  // is one channel and never nears gimbal lock) and must stay continuous.
  let prev = [0, 0, 0];
  for (let i = 1; i <= 240; i++) {
    const yaw = (i / 240) * Math.PI * 4;
    const q = new THREE.Quaternion().setFromEuler(new THREE.Euler(0.25 * Math.sin(i * 0.1), yaw, 0.15, 'YXZ'));
    const e = eulerNear(q, prev, 'YXZ');
    for (let c = 0; c < 3; c++) assert.ok(Math.abs(e[c] - prev[c]) < 0.3, `frame ${i} channel ${c}: ${prev[c].toFixed(2)} -> ${e[c].toFixed(2)}`);
    const q2 = new THREE.Quaternion().setFromEuler(new THREE.Euler(e[0], e[1], e[2], 'YXZ'));
    assert.ok(Math.abs(Math.abs(q.dot(q2)) - 1) < 1e-9, `frame ${i} reconstructs`);
    prev = e;
  }
  assert.ok(Math.abs(prev[1] - Math.PI * 4) < 1e-6, 'the spin accumulates on yaw');
});

test('eulerNear picks the alternate XYZ solution when it is the continuous one', () => {
  const q = new THREE.Quaternion().setFromEuler(new THREE.Euler(0.1, 2.0, 0.2, 'XYZ'));
  const e = eulerNear(q, [0.1, 1.9, 0.2], 'XYZ');   // canonical would be y = π-2 with x, z flipped
  assert.ok(Math.abs(e[0] - 0.1) < 1e-9 && Math.abs(e[1] - 2.0) < 1e-9 && Math.abs(e[2] - 0.2) < 1e-9, e.join(','));
});

test('unwrapInPlace removes 2π jumps so channels interpolate smoothly', () => {
  const a = Float32Array.from([3.0, 3.1, -3.1, -3.0, 3.1]);
  unwrapInPlace(a);
  for (let i = 1; i < a.length; i++) assert.ok(Math.abs(a[i] - a[i - 1]) < 0.5, `step ${i}: ${a[i - 1]} -> ${a[i]}`);
});
