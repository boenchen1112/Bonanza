import test from 'node:test';
import assert from 'node:assert/strict';
import { makeCrowd } from '../src/chars/crowd.js';

// A smooth hop at the fastest layer (eighths, 124bpm, full energy) moves an
// instance at most ~0.2 units per 60Hz frame, a passing wave crest ~0.15 more.
// The bugs this pins measured 0.63 (hop phase aliasing) and 0.81 (a miss
// snapping the stands down) — well past anything smooth.
test('crowd hop stays smooth while energy swings late in a round', () => {
  const crowd = makeCrowd({ rows: 3, perRow: 20, seed: 7 });
  const arr = crowd.mesh.instanceMatrix.array;
  const n = crowd.count;
  const dt = 1 / 60;
  const bps = 124 / 60;
  let beat = 118;
  crowd.setEnergy(0.6);
  crowd.update(dt, beat);
  let prev = Array.from({ length: n }, (_, i) => arr[i * 16 + 13]);
  let worst = 0;
  for (let f = 0; f < 600; f++) {
    beat += dt * bps;
    if (f % 45 === 0) crowd.hype(0.5);        // a hit: energy spikes, then damps
    if (f % 100 === 30) crowd.hype(0.9);      // a big hit: also sends a wave
    if (f % 25 === 10) crowd.wave(0.8, 1.6);  // curtain call spams waves
    if (f === 200) crowd.setEnergy(0.95);     // section change
    if (f % 150 === 70) crowd.deflate(0.9);   // a miss
    crowd.update(dt, beat);
    for (let i = 0; i < n; i++) {
      const y = arr[i * 16 + 13];
      worst = Math.max(worst, Math.abs(y - prev[i]));
      prev[i] = y;
    }
  }
  crowd.dispose();
  assert.ok(worst < 0.4,`crowd jumped ${worst.toFixed(3)} units in one frame`);
});
