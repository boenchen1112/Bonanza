/**
 * Face states on a designed Blender body, driven through the animator's
 * public surface (react / setFace / update), observed through the shared
 * morph influences a renderer would draw.
 */
import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import { BlenderCharacterAnimator } from '../src/chars/blenderAnim.js';

const MORPHS = ['mouthOpen', 'smile', 'frown', 'lidsDown', 'browsUp', 'browsPinch'];

function designedBody() {
  const root = new THREE.Object3D();
  const influences = MORPHS.map(() => 0);
  const dictionary = Object.fromEntries(MORPHS.map((m, i) => [m, i]));
  root.userData.designed = { influences, dictionary };
  const anim = new BlenderCharacterAnimator(root, new THREE.AnimationMixer(root), {});
  const w = (name) => influences[dictionary[name]];
  const run = (seconds) => { for (let t = 0; t < seconds; t += 1 / 60) anim.update(1 / 60, 0); };
  return { root, anim, w, run };
}

test('a miss shows the miss face, then relaxes back to idle', () => {
  const { anim, w, run } = designedBody();
  anim.react('miss');
  run(0.3);
  assert.ok(w('lidsDown') > 0.5 && w('frown') > 0.6 && w('browsPinch') > 0.6, 'miss face showing');
  assert.ok(w('smile') < 0.05);
  run(2.5);
  for (const m of MORPHS) assert.ok(w(m) < 0.05, `${m} relaxed back to idle`);
});

test('a perfect smiles with an open mouth; a good is a milder version', () => {
  const a = designedBody();
  a.anim.react('perfect');
  a.run(0.3);
  const b = designedBody();
  b.anim.react('good');
  b.run(0.3);
  assert.ok(a.w('smile') > 0.7 && a.w('mouthOpen') > 0.3, 'perfect face');
  assert.ok(b.w('smile') > 0.1 && b.w('smile') < a.w('smile'), 'good is milder');
  assert.ok(a.w('frown') < 0.05 && b.w('frown') < 0.05);
});

test('setFace holds until released when asked to', () => {
  const { anim, w, run } = designedBody();
  anim.setFace('gulp', 1, Infinity);
  run(3);
  assert.ok(w('browsUp') > 0.9 && w('mouthOpen') > 0.4, 'gulp held');
  anim.setFace('idle');
  run(1);
  assert.ok(w('browsUp') < 0.05);
});

test('a body without face morphs ignores faces without throwing', () => {
  const root = new THREE.Object3D();
  const anim = new BlenderCharacterAnimator(root, new THREE.AnimationMixer(root), {});
  anim.react('miss');
  anim.setFace('perfect');
  anim.update(1 / 60, 0);
});

test('a reaction impulse never loses the scale the scene gave the character', () => {
  const { root, anim, run } = designedBody();
  root.scale.setScalar(1.5);
  anim.react('perfect');
  run(0.05);
  assert.ok(Math.abs(root.scale.x - 1.5) < 0.1, `scale ${root.scale.x} stays near 1.5 mid-impulse`);
  run(2);
  assert.ok(Math.abs(root.scale.y - 1.5) < 1e-3 && Math.abs(root.scale.x - 1.5) < 1e-3, 'back to exactly 1.5');
});
