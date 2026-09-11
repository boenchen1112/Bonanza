/**
 * Mocap -> toy rig retargeting.  [chars agent owns this directory]
 *
 * The toy rig (rig.js) is posed through `anim.js`'s pose channels, not bone
 * rotations: an arm is `swing` (forward), `lift` (outward), `twist` and
 * `bend` (elbow), applied as
 *
 *     upper.rotation = Euler(-swing, twist * sx, sx * lift, 'XYZ')
 *     fore.rotation.x = -bend           // forearm flexes toward +Z
 *     thigh.rotation = Euler(-swing, 0, sx * spread, 'XYZ')
 *     shin.rotation.x = bend            // knee folds toward -Z
 *
 * with every segment hanging along -Y at rest. Mocap skeletons have other
 * proportions and a T-pose rest, so copying rotations would put the toy's
 * short arms in the wrong places. Instead the bake measures where each mocap
 * limb segment POINTS (in the torso / hips frame) and this module solves for
 * the channels that make the toy's segments point the same way. Proportions
 * stop mattering; the silhouette of the motion survives.
 *
 * Pure (three.js math only) so it runs in the Node bake and in the tests.
 */

import * as THREE from 'three';

const DOWN = new THREE.Vector3(0, -1, 0);
const _m = new THREE.Matrix4();
const _e = new THREE.Euler();
const _x = new THREE.Vector3();
const _y = new THREE.Vector3();
const _z = new THREE.Vector3();

/** Euler XYZ angles of a quaternion, as [x, y, z]. */
export function eulerXYZ(q) {
  _e.setFromQuaternion(q, 'XYZ');
  return [_e.x, _e.y, _e.z];
}

const TAU = Math.PI * 2;
const nearAngle = (a, ref) => a + TAU * Math.round((ref - a) / TAU);

/**
 * Euler angles [x, y, z] of a quaternion in `order`, choosing — of the two
 * solutions (first+π, π-middle, last+π) and the canonical one, each shifted
 * by whole turns — the one nearest `prev`. The canonical decomposition keeps
 * the middle angle within ±π/2, so a body turning past a quarter turn flipped
 * the other two by π in a single frame; a sample between those frames drew
 * the dancer face-down.
 */
export function eulerNear(q, prev, order = 'XYZ') {
  _e.setFromQuaternion(q, order);
  const e = [_e.x, _e.y, _e.z];
  const idx = { X: 0, Y: 1, Z: 2 };
  const [f, m, l] = [idx[order[0]], idx[order[1]], idx[order[2]]];
  const alt = e.slice();
  alt[f] += Math.PI; alt[m] = Math.PI - alt[m]; alt[l] += Math.PI;
  const a = e.map((v, c) => nearAngle(v, prev[c]));
  const b = alt.map((v, c) => nearAngle(v, prev[c]));
  const dist = (s) => Math.abs(s[0] - prev[0]) + Math.abs(s[1] - prev[1]) + Math.abs(s[2] - prev[2]);
  return dist(b) < dist(a) ? b : a;
}

/** Forward kinematics of one toy arm: unit directions of upper arm + forearm. */
export function armFK({ swing, lift, twist, bend }, sx) {
  const R = new THREE.Quaternion().setFromEuler(new THREE.Euler(-swing, twist * sx, sx * lift, 'XYZ'));
  const u = DOWN.clone().applyQuaternion(R);
  const local = new THREE.Vector3(0, -Math.cos(bend), Math.sin(bend));
  const f = local.applyQuaternion(R);
  return { u, f };
}

/** Forward kinematics of one toy leg: unit directions of thigh + shin. */
export function legFK({ swing, spread, bend }, sx) {
  const R = new THREE.Quaternion().setFromEuler(new THREE.Euler(-swing, 0, sx * spread, 'XYZ'));
  const u = DOWN.clone().applyQuaternion(R);
  const f = new THREE.Vector3(0, -Math.cos(bend), -Math.sin(bend)).applyQuaternion(R);
  return { u, f };
}

/** Bend below this and the elbow plane is undefined — keep the last twist. */
const STRAIGHT = 0.02;

/**
 * Channels that point a toy arm's upper arm along `u` and forearm along `f`
 * (unit vectors in the torso frame, the side's `sx` = -1 left / +1 right).
 * `prev` ({twist}) resolves the straight-arm case without a twist flip.
 */
export function solveArm(u, f, sx, prev = null) {
  const cosB = THREE.MathUtils.clamp(u.dot(f), -1, 1);
  const bend = Math.acos(cosB);
  // Local basis of the upper arm in the torso frame: -Y along the segment,
  // +Z the side the forearm folds toward, X completing a right-handed frame.
  _y.copy(u).negate();
  if (Math.sin(bend) > STRAIGHT) {
    _z.copy(f).addScaledVector(u, -cosB).normalize();
  } else {
    // Straight arm: rebuild the frame from the previous twist so the limb
    // doesn't spin when the elbow passes through straight.
    const twist = prev?.twist ?? 0;
    const guess = armFromDirection(u, sx, twist);
    _z.set(0, 0, 1).applyQuaternion(guess);
    _z.addScaledVector(_y, -_z.dot(_y)).normalize();
  }
  _x.crossVectors(_y, _z);
  _m.makeBasis(_x, _y, _z);
  _e.setFromRotationMatrix(_m, 'XYZ');
  return {
    swing: -_e.x,
    twist: _e.y * sx,
    lift: _e.z * sx,
    bend: Math.sin(bend) > STRAIGHT ? bend : 0,
  };
}

/** Rotation that points -Y along `u` with a given twist (straight-arm helper). */
function armFromDirection(u, sx, twist) {
  // Solve the swing/lift pair for this twist by a few Newton steps on the
  // direction error; converges in 2-3 for any reachable direction.
  let swing = 0, lift = 0;
  const q = new THREE.Quaternion();
  for (let it = 0; it < 12; it++) {
    const cur = armFK({ swing, lift, twist, bend: 0 }, sx).u;
    const err = u.clone().sub(cur);
    if (err.lengthSq() < 1e-14) break;
    const h = 1e-5;
    const ds = armFK({ swing: swing + h, lift, twist, bend: 0 }, sx).u.sub(cur).divideScalar(h);
    const dl = armFK({ swing, lift: lift + h, twist, bend: 0 }, sx).u.sub(cur).divideScalar(h);
    // 2x2 least squares on the 3-vector error.
    const a = ds.dot(ds), b = ds.dot(dl), c = dl.dot(dl);
    const r1 = ds.dot(err), r2 = dl.dot(err);
    const det = a * c - b * b || 1e-12;
    swing += (c * r1 - b * r2) / det;
    lift += (a * r2 - b * r1) / det;
  }
  return q.setFromEuler(new THREE.Euler(-swing, twist * sx, sx * lift, 'XYZ'));
}

/**
 * Channels that point a toy leg's thigh along `u` and shin along `f` (unit
 * vectors in the hips frame). Legs have no twist channel, so the knee folds
 * in the thigh's own sagittal plane; `bend` keeps the true knee angle.
 */
export function solveLeg(u, f, sx) {
  const dx = THREE.MathUtils.clamp(u.x, -1, 1);
  const tz = Math.asin(dx);
  const tx = Math.atan2(-u.z, -u.y);
  const bend = Math.acos(THREE.MathUtils.clamp(u.dot(f), -1, 1));
  return { swing: -tx, spread: tz * sx, bend };
}

/** Remove ±2π jumps from an angle track in place (for smooth lerping). */
export function unwrapInPlace(a) {
  for (let i = 1; i < a.length; i++) {
    let d = a[i] - a[i - 1];
    while (d > Math.PI) { a[i] -= 2 * Math.PI; d -= 2 * Math.PI; }
    while (d < -Math.PI) { a[i] += 2 * Math.PI; d += 2 * Math.PI; }
  }
  return a;
}
