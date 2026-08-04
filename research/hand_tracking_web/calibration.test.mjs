// v6 Phase 1.1 regression harness, calibration half -- see scoring.test.mjs
// for the scoring half. Run with: node --test research/hand_tracking_web/calibration.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { averageSamples, prunedStabilityBuffer, isStable } from "./calibration.mjs";

test("averageSamples: averages x and y independently", () => {
  const [ox, oy] = averageSamples([[0, 0], [1, 2], [2, 4]]);
  assert.equal(ox, 1);
  assert.equal(oy, 2);
});

test("averageSamples: single sample returns itself", () => {
  const [ox, oy] = averageSamples([[0.3, 0.7]]);
  assert.equal(ox, 0.3);
  assert.equal(oy, 0.7);
});

test("prunedStabilityBuffer: drops samples older than windowMs", () => {
  const buffer = [{ t: 0, x: 0, y: 0 }, { t: 300, x: 0, y: 0 }, { t: 900, x: 0, y: 0 }];
  const pruned = prunedStabilityBuffer(buffer, 1000, 400);
  assert.deepEqual(pruned.map(s => s.t), [900]);
});

test("prunedStabilityBuffer: does not mutate the input array", () => {
  const buffer = [{ t: 0, x: 0, y: 0 }, { t: 900, x: 0, y: 0 }];
  prunedStabilityBuffer(buffer, 1000, 50);
  assert.equal(buffer.length, 2);
});

test("isStable: false with fewer than 3 samples even if perfectly still", () => {
  const buffer = [{ t: 600, x: 0.1, y: 0.1 }, { t: 900, x: 0.1, y: 0.1 }];
  const stable = isStable(buffer, 1000, { windowMs: 400, threshold: 0.02, videoAspect: 4 / 3 });
  assert.equal(stable, false);
});

test("isStable: true when tightly clustered and window is covered", () => {
  const buffer = [
    { t: 600, x: 0.10, y: 0.10 },
    { t: 750, x: 0.101, y: 0.099 },
    { t: 900, x: 0.099, y: 0.101 },
  ];
  const stable = isStable(buffer, 1000, { windowMs: 400, threshold: 0.02, videoAspect: 4 / 3 });
  assert.equal(stable, true);
});

test("isStable: false when spread exceeds threshold", () => {
  const buffer = [
    { t: 600, x: 0.05, y: 0.10 },
    { t: 750, x: 0.20, y: 0.10 }, // large x jump
    { t: 900, x: 0.10, y: 0.10 },
  ];
  const stable = isStable(buffer, 1000, { windowMs: 400, threshold: 0.02, videoAspect: 4 / 3 });
  assert.equal(stable, false);
});

test("isStable: false when the buffer hasn't covered enough of the window yet", () => {
  const buffer = [
    { t: 950, x: 0.10, y: 0.10 },
    { t: 970, x: 0.10, y: 0.10 },
    { t: 990, x: 0.10, y: 0.10 },
  ]; // tightly clustered but only spans 40ms of a 400ms window
  const stable = isStable(buffer, 1000, { windowMs: 400, threshold: 0.02, videoAspect: 4 / 3 });
  assert.equal(stable, false);
});

test("isStable: videoAspect scales the x spread (non-square frame correction)", () => {
  // x spread of 0.03 * aspect(2.0) = 0.06 > threshold 0.05 -- fails with a
  // wide aspect even though the raw x spread alone looks small.
  const buffer = [
    { t: 600, x: 0.10, y: 0.10 },
    { t: 750, x: 0.13, y: 0.10 },
    { t: 900, x: 0.10, y: 0.10 },
  ];
  const stableWide = isStable(buffer, 1000, { windowMs: 400, threshold: 0.05, videoAspect: 2.0 });
  const stableSquare = isStable(buffer, 1000, { windowMs: 400, threshold: 0.05, videoAspect: 1.0 });
  assert.equal(stableWide, false);
  assert.equal(stableSquare, true);
});
