/**
 * Render-quality policy: which (render scale, tier) a Graphics setting means
 * on a given device. Pure — renderer name and device ratio are passed in.
 */
import test from 'node:test';
import assert from 'node:assert/strict';
import { ladderFor, isIntegratedGpu, levelForSetting } from '../src/render/quality.js';

const IRIS = 'ANGLE (Intel, Intel(R) Iris(R) Xe Graphics (0x000046A6) Direct3D11 vs_5_0 ps_5_0, D3D11)';
const RTX = 'ANGLE (NVIDIA, NVIDIA GeForce RTX 3060 Laptop GPU Direct3D11 vs_5_0 ps_5_0, D3D11)';

test('the ladder drops render scale before quality tier', () => {
  const l = ladderFor(2);
  assert.deepEqual(l[0], { scale: 2, tier: 'high' });
  assert.deepEqual(l[l.length - 1], { scale: 1, tier: 'low' });
  const firstMedium = l.findIndex((x) => x.tier === 'medium');
  assert.ok(l.slice(0, firstMedium).some((x) => x.scale < 2), 'a high-tier step at reduced scale comes before medium');
});

test('the ladder never supersamples past the device ratio and has no duplicate steps', () => {
  const l = ladderFor(1);
  assert.ok(l.every((x) => x.scale <= 1));
  const keys = l.map((x) => `${x.scale}|${x.tier}`);
  assert.equal(new Set(keys).size, keys.length);
  assert.deepEqual(l, [{ scale: 1, tier: 'high' }, { scale: 1, tier: 'medium' }, { scale: 1, tier: 'low' }]);
});

test('integrated GPUs are recognised from the WebGL renderer string', () => {
  assert.equal(isIntegratedGpu(IRIS), true);
  assert.equal(isIntegratedGpu('ANGLE (Intel, Intel(R) UHD Graphics 620 Direct3D11 vs_5_0 ps_5_0, D3D11)'), true);
  assert.equal(isIntegratedGpu('ANGLE (AMD, AMD Radeon(TM) Graphics Direct3D11 vs_5_0 ps_5_0, D3D11)'), true);
  assert.equal(isIntegratedGpu(RTX), false);
  assert.equal(isIntegratedGpu(''), false);
});

test('Auto starts an integrated GPU on a high-DPI screen at (1.5, medium)', () => {
  const lv = levelForSetting('auto', { renderer: IRIS, deviceRatio: 2 });
  assert.deepEqual({ scale: lv.scale, tier: lv.tier, auto: lv.auto }, { scale: 1.5, tier: 'medium', auto: true });
});

test('Auto starts a discrete GPU, or any GPU at ratio <= 1.5, at full resolution on high', () => {
  assert.deepEqual(pick(levelForSetting('auto', { renderer: RTX, deviceRatio: 2 })), { scale: 2, tier: 'high' });
  assert.deepEqual(pick(levelForSetting('auto', { renderer: IRIS, deviceRatio: 1.25 })), { scale: 1.25, tier: 'high' });
});

test('a remembered level wins over the start heuristic, clamped to this device', () => {
  const lv = levelForSetting('auto', { renderer: IRIS, deviceRatio: 2, remembered: { scale: 1.25, tier: 'medium' } });
  assert.deepEqual(pick(lv), { scale: 1.25, tier: 'medium' });
  const onLowDpi = levelForSetting('auto', { renderer: RTX, deviceRatio: 1, remembered: { scale: 2, tier: 'high' } });
  assert.deepEqual(pick(onLowDpi), { scale: 1, tier: 'high' });
});

test('fixed settings map to fixed levels and are not Auto', () => {
  const env = { renderer: IRIS, deviceRatio: 2 };
  assert.deepEqual(pick(levelForSetting('high', env)), { scale: 2, tier: 'high' });
  assert.deepEqual(pick(levelForSetting('medium', env)), { scale: 1.5, tier: 'medium' });
  assert.deepEqual(pick(levelForSetting('low', env)), { scale: 1, tier: 'low' });
  assert.equal(levelForSetting('medium', env).auto, false);
});

test('an unknown setting falls back to Auto', () => {
  assert.equal(levelForSetting('ultra', { renderer: RTX, deviceRatio: 1 }).auto, true);
});

function pick(lv) { return { scale: lv.scale, tier: lv.tier }; }
