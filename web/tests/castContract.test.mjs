/**
 * The committed designed cast bodies meet the asset contract the game relies
 * on (role materials, one skinned mesh, face morph targets bound only to the
 * head, all clips, triangle budget). See tools/assets/blender/cast-contract.mjs.
 */
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { checkCastGlb } from '../../tools/assets/blender/cast-contract.mjs';

/** Characters rebuilt with build-character.py so far. */
const DESIGNED = ['tuff'];

for (const id of DESIGNED) {
  test(`cast-${id}.glb meets the designed-body contract`, () => {
    const file = fileURLToPath(new URL(`../src/assets/chars/cast-${id}.glb`, import.meta.url));
    const r = checkCastGlb(readFileSync(file));
    assert.deepEqual(r.problems, []);
  });
}
