// v6 Phase 1.1: regression harness for the pure scoring/calibration math,
// checked in before any further scoring tuning (per primer.md's standing
// plan). Run with: node --test research/hand_tracking_web/scoring.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { timingTier, findIctus, deriveCalibScaleFactor, PERFECT_MS, GREAT_MS, GOOD_MS } from "./scoring.mjs";

test("timingTier: null offset is a Miss", () => {
  assert.equal(timingTier(null), "Miss");
});

test("timingTier: tier boundaries are inclusive", () => {
  assert.equal(timingTier(PERFECT_MS), "Perfect");
  assert.equal(timingTier(PERFECT_MS + 1), "Great");
  assert.equal(timingTier(GREAT_MS), "Great");
  assert.equal(timingTier(GREAT_MS + 1), "Good");
  assert.equal(timingTier(GOOD_MS), "Good");
  assert.equal(timingTier(GOOD_MS + 1), "Miss");
});

test("timingTier: sign doesn't matter, only magnitude", () => {
  assert.equal(timingTier(-30), timingTier(30));
  assert.equal(timingTier(-120), timingTier(120));
});

test("findIctus: empty window returns nulls", () => {
  const result = findIctus([], 1.0, 1.0, 0.5);
  assert.equal(result.offsetMs, null);
  assert.equal(result.ictusNy, null);
});

test("findIctus: picks the deepest (most negative ny) sample in window, not the closest in time", () => {
  // Downbeat is the BOTTOM of the stroke (most negative ny), never the
  // closest-in-time sample -- feedback_conducting_downbeat_is_bottom.
  const traceXY = [
    [0, -0.2, 0.9], // shallower, closer to beatTime
    [0, -0.9, 1.05], // deepest -- this is the ictus
    [0, -0.5, 1.1],
  ];
  const result = findIctus(traceXY, 1.0, 1.0, 0.5);
  assert.equal(result.ictusNy, -0.9);
  assert.ok(Math.abs(result.offsetMs - 50) < 1e-9); // 1.05 - 1.0 = 0.05s = 50ms
});

test("findIctus: excludes samples outside the window (doesn't leak into a neighboring beat)", () => {
  const traceXY = [
    [0, -5.0, 0.2], // far outside the window, deepest overall -- must be excluded
    [0, -0.3, 1.0],
  ];
  const result = findIctus(traceXY, 1.0, 1.0, 0.3);
  assert.equal(result.ictusNy, -0.3);
});

test("findIctus: offsetMs is relative to heardBeatTime, not raw beatTime", () => {
  const traceXY = [[0, -1.0, 1.0]];
  const result = findIctus(traceXY, 0.9, 1.0, 0.5); // heard 100ms after scheduled
  assert.equal(result.offsetMs, 0); // sample landed exactly on the heard time
});

test("deriveCalibScaleFactor: returns null when TOP and BOTTOM are too close", () => {
  const scale = deriveCalibScaleFactor({
    ref: [[0, 0], [0.5, -1.0]],
    origin: [0.5, 0.5],
    bottom: [0.5, 0.500001],
    videoAspect: 4 / 3,
  });
  assert.equal(scale, null);
});

test("deriveCalibScaleFactor: matches the pre-extraction inline formula on a known input", () => {
  const scale = deriveCalibScaleFactor({
    ref: [[0.0, 0.0], [0.5299, -1.0]],
    origin: [0.5, 0.3],
    bottom: [0.5, 0.5],
    videoAspect: 4 / 3,
  });
  // refDelta = hypot(0.5299, -1.0); physDx = 0*aspect = 0; physDy = -(0.5-0.3) = -0.2
  const refDelta = Math.hypot(0.5299, -1.0);
  const physDelta = 0.2;
  assert.ok(Math.abs(scale - refDelta / physDelta) < 1e-9);
});
