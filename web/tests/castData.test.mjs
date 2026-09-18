import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import { CAST, colourRoles, mixHex } from '../src/shell/castData.js';

// The shell has always derived portrait/rig colours through THREE.Color
// (sRGB hex in, linear-light lerp, sRGB hex out). The data module must agree
// with it exactly, or the Blender bodies and the menus drift apart.
const threeMix = (a, b, t) => new THREE.Color().setHex(a).lerp(new THREE.Color(b), t).getHex();

test('mixHex matches THREE.Color lerp for every cast colour role', () => {
  for (const def of CAST) {
    for (const [target, t] of [[0x100818, 0.62], [0xffffff, 0.8], [0x06030c, 0.88], [0xffffff, 0.18], [0xffffff, 0.35]]) {
      assert.equal(mixHex(def.color, target, t), threeMix(def.color, target, t), `${def.id} -> ${target.toString(16)} @ ${t}`);
    }
  }
});

test('every cast member has the five build roles', () => {
  for (const def of CAST) {
    const r = colourRoles(def);
    for (const k of ['body', 'trim', 'skin', 'accent', 'eye']) assert.equal(typeof r[k], 'number', `${def.id}.${k}`);
    assert.equal(r.body, def.color);
    assert.equal(r.accent, def.accent);
  }
});

test('the cast is the eight approved characters with distinct crests per look-alike pair', () => {
  assert.deepEqual(CAST.map((c) => c.id), ['bopp', 'zizz', 'kwark', 'tuff', 'mimo', 'nibb', 'glub', 'fizz']);
});
