/**
 * Mocap clip playback through the procedural animator. The baked tracks are
 * data; these pin how the animator samples them, times them to the beat and
 * hands off afterwards — the parts that decide whether a mocap swing lands
 * on the note.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import { CharacterAnimator, clipTimeAt, sampleClip, beatLockRatio, makePose, HIPS_ORDER } from '../src/chars/anim.js';
import { CLIPS, CLIP_FPS, CLIP_CHANNELS } from '../src/chars/clips.gen.js';

/** The joints and dims CharacterAnimator touches — no meshes, no DOM. */
function stubChar() {
  const g = () => new THREE.Object3D();
  const arm = () => ({ upper: g(), fore: g(), hand: g() });
  const leg = () => ({ thigh: g(), shin: g(), foot: g() });
  return {
    joints: {
      root: g(), hips: g(), torso: g(), head: g(), face: g(), torsoMesh: g(),
      armL: arm(), armR: arm(), legL: leg(), legR: leg(),
    },
    dims: { height: 1.35, hipY: 0.4, legLen: 0.36 },
    build: { torso: { h: 0.48 }, neck: 0.045, head: { w: 0.48, h: 0.44 } },
    variation: { phase: 0, bounce: 1, blinkOffset: 0, swagger: 0.5 },
    seed: 1,
  };
}

test('every baked clip has every channel at full length', () => {
  for (const [name, c] of Object.entries(CLIPS)) {
    for (const k of CLIP_CHANNELS) assert.equal(c.ch[k].length, c.n, `${name}.${k}`);
    assert.ok(c.contact >= 0 && c.contact <= c.duration, `${name} contact`);
  }
});

test('sampleClip returns the baked value exactly on a frame', () => {
  const c = CLIPS.swing;
  const p = makePose();
  const i = 17;
  sampleClip(p, c, i / CLIP_FPS, 1);
  for (const k of CLIP_CHANNELS) assert.ok(Math.abs(p[k] - c.ch[k][i] / 1000) < 1e-9, k);
});

test('beatLockRatio keeps playback rate nearest natural', () => {
  assert.equal(beatLockRatio(195.6, 124), 2);   // 1.27x beats 0.63x
  assert.equal(beatLockRatio(120, 120), 1);
  assert.equal(beatLockRatio(100, 200), 0.5);
});

test('beat-locked loops sit on their downbeat at every integer beat', () => {
  const spec = { from: 0, to: 20, beatLock: true, secPerBeat: 0.6, beat0: 8, phase: 0.3 };
  for (const b of [8, 9, 12, 31]) {
    const t = clipTimeAt(spec, 0, 0, b);
    const k = (t - 0.3) / 0.6;
    assert.ok(Math.abs(k - Math.round(k)) < 1e-9, `beat ${b} -> clip ${t}`);
  }
});

test('a played clip drives the arm from its baked track, then hands off to idle', () => {
  const char = stubChar();
  const anim = new CharacterAnimator(char, { seed: 1 });
  const c = CLIPS.swing;
  anim.play('swing', { beat: 0, blend: 0.001 });
  // Advance to just past the contact frame, no beat layer noise.
  let beat = 0;
  const dt = 1 / 120;
  for (let t = 0; t < c.contact; t += dt) { beat += dt * 2; anim.update(dt, beat); }
  assert.equal(anim.state, 'clip');
  const expect = c.ch.armRSwing[Math.round(c.contact * CLIP_FPS)] / 1000;
  assert.ok(Math.abs(anim.pose.armRSwing - expect) < 0.25, `armRSwing ${anim.pose.armRSwing} vs ${expect}`);
  assert.ok(Math.abs(anim.clipTime - c.contact) < 0.05, `clipTime ${anim.clipTime}`);
  for (let t = 0; t < c.duration; t += dt) { beat += dt * 2; anim.update(dt, beat); }
  assert.equal(anim.state, 'idle');
});

const ROT_KEYS = ['hipsRotX', 'hipsRotY', 'hipsRotZ', 'torsoRotX', 'torsoRotY', 'torsoRotZ', 'headRotX', 'headRotY', 'headRotZ'];

test('baked rotation tracks have no frame-to-frame flips', () => {
  for (const [name, c] of Object.entries(CLIPS)) {
    for (const k of ROT_KEYS) {
      const tr = c.ch[k];
      for (let i = 1; i < c.n; i++) {
        assert.ok(Math.abs(tr[i] - tr[i - 1]) < 700, `${name}.${k} jumps ${tr[i - 1]} -> ${tr[i]} at frame ${i}`);
      }
    }
  }
});

/** How upright the hips are: world-up of the hips frame, y component. */
function hipsUp(p) {
  const q = new THREE.Quaternion().setFromEuler(new THREE.Euler(p.hipsRotX, p.hipsRotY, p.hipsRotZ, HIPS_ORDER));
  return new THREE.Vector3(0, 1, 0).applyQuaternion(q).y;
}

test('the dance never tips the body over — through its loop and the blends in and out', () => {
  const c = CLIPS.dance;
  let rawMin = 1;
  const p = makePose();
  for (let i = 0; i < c.n; i++) { sampleClip(p, c, i / CLIP_FPS); rawMin = Math.min(rawMin, hipsUp(p)); }
  const anim = new CharacterAnimator(stubChar(), { seed: 1 });
  const dt = 1 / 60;
  let beat = 0;
  let min = 1;
  anim.play('dance', { beatLock: true, bpm: 124, beat0: -3, blend: 0.3 });
  const run = (secs) => {
    for (let t = 0; t < secs; t += dt) { beat += dt * (124 / 60); anim.update(dt, beat); min = Math.min(min, hipsUp(anim.pose)); }
  };
  run(c.duration * 2.2);                    // past the loop wrap, twice
  anim.react('great');                      // blend out of a spun-round frame
  run(1.5);
  assert.ok(min > rawMin - 0.15, `hips tipped to up.y=${min.toFixed(2)} (the mocap itself never goes below ${rawMin.toFixed(2)})`);
});

test('a retimed windup holds at its end until the strike', () => {
  const anim = new CharacterAnimator(stubChar(), { seed: 1 });
  anim.play('swing', { to: CLIPS.swing.contact, dur: 0.3, hold: true });
  for (let i = 0; i < 120; i++) anim.update(1 / 60, i / 30);
  assert.equal(anim.state, 'clip', 'still holding after 2s');
  assert.ok(Math.abs(anim.clipTime - CLIPS.swing.contact) < 1e-6);
});
