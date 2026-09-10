/**
 * Procedural character animation.  [chars agent owns this directory]
 *
 * THE RULE HERE: motion is a function of BEAT PHASE, not of wall time. A
 * character in this game is never "animating while music happens" — the body
 * is a second instrument. Every idle bob, every step, every breath is derived
 * from `beat`, so a tempo change or a tempo ramp re-times the whole cast for
 * free and nothing ever drifts against the kick drum.
 *
 * The system is three layers, applied in this order:
 *
 *   1. **State pose** — a target pose from the state machine (idle / ready /
 *      windup / action / recover / celebrate / fail / taunt), crossfaded
 *      between the outgoing and incoming state with a smootherstep weight.
 *   2. **Damping** — a light frame-rate-independent `damp()` pass so that a
 *      pose change never produces a hard visual seam. Light, because the state
 *      curves already carry the timing; heavy damping would eat the snap.
 *   3. **Beat layer, additive** — bounce, weight-shift step, arm swing, breath,
 *      head lag, bobble spring, blinks. Computed in closed form from the beat
 *      phase and added AFTER damping, so the downbeat is always exactly on the
 *      downbeat no matter what the state machine is doing.
 *
 * Animation principles are cashed out, not gestured at:
 *   - **Anticipation** before every action (`anticipate()` from core/util).
 *   - **Overshoot and settle** after it (`backOut`, `elasticOut`).
 *   - **Squash on landing, stretch on launch**, volume-compensated, pivoting
 *     at the feet — the only pivot where squash reads as weight.
 *   - **Secondary motion**: head, bobble and arms lag the body through real
 *     springs, so nothing in the rig stops moving at the same instant.
 *   - **Silhouette-first reactions**: the four verdict poses are a tall V, an
 *     asymmetric punch, a low wide shrug and a folded comma. Fill them black
 *     and you can still name the verdict.
 */

import {
  clamp, clamp01, lerp, smootherstep, damp,
  backOut, elasticOut, anticipate, easeOutCubic, easeOutQuint, makeRng,
} from '../core/util.js';
import { CLIPS, CLIP_FPS, CLIP_CHANNELS } from './clips.gen.js';

export const STATES = [
  'idle', 'ready', 'windup', 'action', 'recover', 'celebrate', 'fail', 'taunt', 'clip',
];

/**
 * Per-state timing.
 *  dur   — nominal seconds; drives `u` (0..1) and auto-advance.
 *  blend — crossfade seconds into this state.
 *  beat  — how much of the beat layer shows through (0 = frozen, >1 = amped).
 *  next  — auto-advance target when `dur` elapses.
 *  hold  — stay at u=1 instead of advancing (windup waits for the player).
 */
export const STATE_DEF = {
  idle: { dur: Infinity, blend: 0.20, beat: 1.00 },
  ready: { dur: Infinity, blend: 0.13, beat: 0.72 },
  windup: { dur: 0.26, blend: 0.055, beat: 0.30, hold: true, next: 'action' },
  action: { dur: 0.30, blend: 0.028, beat: 0.18, next: 'recover' },
  recover: { dur: 0.42, blend: 0.09, beat: 0.80, next: 'idle' },
  celebrate: { dur: 2.40, blend: 0.10, beat: 1.35, next: 'idle' },
  fail: { dur: 1.70, blend: 0.07, beat: 0.22, next: 'idle' },
  taunt: { dur: 2.00, blend: 0.16, beat: 1.10, next: 'idle' },
  // Mocap-driven body (see `play()`); dur/next/beat are set per call.
  clip: { dur: 1, blend: 0.10, beat: 0.25, next: 'idle' },
};

/** Which state + variant a verdict maps to. Silhouettes, in order: Y, K, W, comma. */
export const VERDICT_POSE = {
  perfect: { state: 'celebrate', variant: 'perfect' },
  great: { state: 'celebrate', variant: 'great' },
  good: { state: 'recover', variant: 'good' },
  miss: { state: 'fail', variant: 'miss' },
};

// ------------------------------------------------------------------- poses

/**
 * The pose vector. Everything is in *normalised* units:
 *   - translations are fractions of the character's height, so a pose reads
 *     identically on the 1.05-unit rookie and the 1.98-unit lanky one;
 *   - rotations are radians;
 *   - `swing` is forward-positive, `lift` is outward-positive, and `bend` is
 *     elbow/knee-positive. The rig's sign conventions are applied in
 *     `applyPose` so no caller ever has to think about them.
 */
const POSE_KEYS = [
  'rootY', 'squash', 'rootRotY', 'rootRotZ',
  'hipsY', 'hipsRotX', 'hipsRotY', 'hipsRotZ',
  'torsoRotX', 'torsoRotY', 'torsoRotZ',
  'headRotX', 'headRotY', 'headRotZ',
  'armLSwing', 'armLLift', 'armLTwist', 'armLBend',
  'armRSwing', 'armRLift', 'armRTwist', 'armRBend',
  'legLSwing', 'legLSpread', 'legLBend',
  'legRSwing', 'legRSpread', 'legRBend',
  'eyeOpen', 'pupilX', 'pupilY', 'browY', 'browAngle',
  'mouthCurve', 'mouthOpen', 'mouthW',
];

/** Neutral standing pose — arms hang slightly clear of the body, small smile. */
const REST = {
  armLLift: 0.19, armRLift: 0.19,
  eyeOpen: 1, mouthCurve: 0.45, mouthW: 1,
};

export function makePose() {
  const p = {};
  for (const k of POSE_KEYS) p[k] = REST[k] ?? 0;
  return p;
}

function resetPose(p) {
  for (let i = 0; i < POSE_KEYS.length; i++) {
    const k = POSE_KEYS[i];
    p[k] = REST[k] ?? 0;
  }
  return p;
}

function blendPose(out, a, b, t) {
  for (let i = 0; i < POSE_KEYS.length; i++) {
    const k = POSE_KEYS[i];
    out[k] = a[k] + (b[k] - a[k]) * t;
  }
  return out;
}

// ------------------------------------------------------- curves & springs

/**
 * Windup shape. Starts at 0, feints *forward* (negative) for the first third,
 * then drives to a full coil at 1. This is `anticipate()` used for exactly
 * what its name says.
 */
export function windup(t) {
  return anticipate(t, 0.22);
}

/**
 * Strike shape. Overshoots past the target and settles — `backOut` for the
 * body's throw, `elasticOut` for the tail that keeps ringing after it.
 * Returns >1 briefly, on purpose; that overshoot is the impact.
 */
export function strike(t) {
  t = clamp01(t);
  return backOut(Math.min(1, t / 0.55), 2.4) * 0.72 + elasticOut(t, 1, 0.42) * 0.28;
}

/** Damped spring step. Substepped so a 20fps frame can't make it explode. */
function springStep(s, target, freq, zeta, dt) {
  const w = freq * Math.PI * 2;
  const steps = dt > 1 / 45 ? Math.min(4, Math.ceil(dt * 45)) : 1;
  const h = dt / steps;
  for (let i = 0; i < steps; i++) {
    const a = -w * w * (s.x - target) - 2 * zeta * w * s.v;
    s.v += a * h;
    s.x += s.v * h;
  }
  return s.x;
}

/**
 * The gravity-true hop. 0 at the beat, 1 at the halfway point. Using a
 * parabola rather than a sine matters: a sine spends too long near the top and
 * reads floaty, a parabola reads like a body with mass.
 */
function hop(p) { return 4 * p * (1 - p); }

/** Compression right after contact — how squash gets its timing. */
function compress(p) { return Math.exp(-p * 11); }

/** Crouch just before the next contact. This is the offbeat anticipation. */
function crouch(p) { return smootherstep((p - 0.78) / 0.22); }

// -------------------------------------------------------------- beat layer

/**
 * `idle(beat)` — the beat-synced idle, returned as a pose.
 *
 * Exported standalone (and pure) so tests and tuning tools can sample it at
 * arbitrary beats without a rig, a renderer or a clock.
 *
 * @param {number} beat  float beat
 * @param {object} [o]   {amp, phase, swagger, out} — `out` avoids allocating
 */
export function idle(beat, o = {}) {
  const p = o.out ? resetPose(o.out) : makePose();
  const amp = o.amp ?? 1;
  const ph = o.phase ?? 0;
  const b = beat + ph;
  const f = b - Math.floor(b);

  // Vertical: hop up through the beat, compress on the beat, crouch before it.
  const y = hop(f) * 0.030 - compress(f) * 0.024 - crouch(f) * 0.020;
  p.rootY = y * amp;

  // Squash follows the vertical derivative in spirit: flattened at contact,
  // stretched through the launch.
  p.squash = (-compress(f) * 0.85 + Math.exp(-(((f - 0.17) / 0.11) ** 2)) * 0.45
    - crouch(f) * 0.30) * amp;

  // Weight shift: a two-beat cycle, so the character steps left-right-left.
  const sway = Math.sin(b * Math.PI);
  p.hipsRotZ = sway * 0.055 * amp;
  p.torsoRotZ = -sway * 0.075 * amp;
  p.rootRotY = sway * 0.055 * amp;
  p.headRotZ = sway * 0.05 * amp;

  // The free leg lifts on alternate beats — a step in place, not a pogo.
  const stepL = Math.max(0, sway);
  const stepR = Math.max(0, -sway);
  p.legLSwing = stepL * 0.24 * amp;
  p.legLBend = stepL * 0.40 * amp;
  p.legRSwing = stepR * 0.24 * amp;
  p.legRBend = stepR * 0.40 * amp;
  p.legLSpread = 0.03 + stepR * 0.03;
  p.legRSpread = 0.03 + stepL * 0.03;

  // Arms swing counter to the legs, with a quarter-beat lag so they trail.
  const swingLag = Math.sin((b - 0.16) * Math.PI);
  p.armLSwing = -swingLag * 0.30 * amp;
  p.armRSwing = swingLag * 0.30 * amp;
  p.armLBend = 0.24 + Math.max(0, swingLag) * 0.28;
  p.armRBend = 0.24 + Math.max(0, -swingLag) * 0.28;
  p.armLLift = REST.armLLift + hop(f) * 0.10 * amp;
  p.armRLift = REST.armRLift + hop(f) * 0.10 * amp;

  // Breath: a four-beat cycle. Slow enough to never fight the bounce, present
  // enough that a paused character is still alive.
  const breath = Math.sin(b * Math.PI * 0.5);
  p.torsoRotX = -0.02 + breath * 0.018;
  p.headRotX = -breath * 0.022;

  return p;
}

// ------------------------------------------------------------ state poses
//
// Each writes a full pose for its state. `s` carries {t,u,beat,variation,
// variant,power}. They are pure functions of that — no hidden state — which is
// what makes crossfading them safe.

function poseIdle(p, s) {
  return idle(s.beat, { out: p, phase: s.variation.phase * 0.0, amp: 1 });
}

/**
 * READY — the telegraph pose. A player who has never seen the game before
 * must be able to tell from this alone that something is about to be asked of
 * them, a full beat early: knees bent, weight forward, guard up, eyes wide.
 */
function poseReady(p, s) {
  resetPose(p);
  const b = s.beat - Math.floor(s.beat);
  const pulse = hop(b);
  p.rootY = -0.045 + pulse * 0.012;
  p.squash = -0.18 - compress(b) * 0.25;
  p.torsoRotX = 0.20;
  p.hipsRotX = -0.10;
  p.headRotX = -0.14;
  p.legLBend = 0.44; p.legRBend = 0.44;
  p.legLSwing = 0.20; p.legRSwing = 0.20;
  p.legLSpread = 0.13; p.legRSpread = 0.13;
  p.armLSwing = 0.62; p.armRSwing = 0.62;
  p.armLBend = 1.15; p.armRBend = 1.15;
  p.armLLift = 0.34; p.armRLift = 0.34;
  p.eyeOpen = 1.18 + pulse * 0.06;
  p.browY = 0.55;
  p.mouthCurve = 0.05;
  p.mouthW = 0.85;
  p.mouthOpen = 0.12;
  return p;
}

/** WINDUP — coil away from the target. Anticipation, in one channel: `k`. */
function poseWindup(p, s) {
  resetPose(p);
  const k = windup(s.u);
  p.rootY = -0.055 * k;
  p.squash = -0.30 * k;
  p.rootRotY = -0.30 * k;
  p.torsoRotY = -0.60 * k;
  p.torsoRotX = 0.16 * k;
  p.torsoRotZ = 0.10 * k;
  p.hipsRotY = -0.22 * k;
  // Head turns TO the target while the body coils away — the classic
  // "look where you're going to hit" read.
  p.headRotY = 0.34 * k;
  p.headRotX = -0.06 * k;

  p.armRSwing = -1.15 * k;
  p.armRLift = 0.19 + 0.55 * k;
  p.armRBend = 1.30 * k;
  p.armRTwist = -0.5 * k;
  p.armLSwing = 0.55 * k;
  p.armLBend = 1.05 * k;
  p.armLLift = 0.19 + 0.22 * k;

  p.legLBend = 0.36 * k; p.legRBend = 0.52 * k;
  p.legLSwing = 0.26 * k; p.legRSwing = -0.12 * k;
  p.legLSpread = 0.06 + 0.10 * k; p.legRSpread = 0.06 + 0.14 * k;

  p.eyeOpen = 1 - 0.30 * k;
  p.browY = -0.5 * k;
  p.browAngle = 0.55 * k;
  p.pupilX = 0.5 * k;
  p.mouthCurve = -0.15 * k;
  p.mouthW = 0.8;
  return p;
}

/** ACTION — the strike. Overshoot through the target, then ring down. */
function poseAction(p, s) {
  resetPose(p);
  const k = strike(s.u);           // 0 -> ~1.15 -> 1
  const early = Math.exp(-s.u * 16); // the first two frames: launch stretch
  const pw = s.power ?? 1;

  p.rootY = (0.055 * easeOutCubic(Math.min(1, s.u / 0.35)) - 0.02 * (1 - k)) * pw;
  p.squash = (early * 0.75 - 0.20 * (1 - easeOutQuint(s.u))) * pw;
  p.rootRotY = lerp(-0.30, 0.42, k);
  p.torsoRotY = lerp(-0.60, 0.95, k) * pw;
  p.torsoRotX = lerp(0.16, -0.14, k);
  p.torsoRotZ = lerp(0.10, -0.16, k);
  p.hipsRotY = lerp(-0.22, 0.48, k);

  // The head whips last and settles with an elastic tail — secondary motion
  // written into the pose rather than left to the damper.
  p.headRotY = lerp(0.34, -0.28, elasticOut(clamp01(s.u / 0.8), 1, 0.5));
  p.headRotX = -0.12 * k;

  p.armRSwing = lerp(-1.15, 1.55, k) * pw;
  p.armRLift = lerp(0.74, 0.30, k);
  p.armRBend = lerp(1.30, 0.10, easeOutQuint(clamp01(s.u / 0.5)));
  p.armRTwist = lerp(-0.5, 0.35, k);
  p.armLSwing = lerp(0.55, -0.85, k);
  p.armLBend = lerp(1.05, 0.35, k);
  p.armLLift = lerp(0.41, 0.55, k);

  p.legLSwing = lerp(0.26, -0.18, k);
  p.legRSwing = lerp(-0.12, 0.34, k);
  p.legLBend = lerp(0.36, 0.16, k);
  p.legRBend = lerp(0.52, 0.30, k);
  p.legLSpread = 0.16; p.legRSpread = 0.20;

  p.eyeOpen = 0.55 + 0.5 * easeOutCubic(s.u);
  p.browAngle = 0.75 * (1 - s.u * 0.5);
  p.browY = -0.35;
  p.mouthCurve = -0.1;
  p.mouthOpen = Math.exp(-s.u * 5) * 0.95;
  p.mouthW = 1.15;
  return p;
}

/**
 * RECOVER — settle. The 'good' variant doubles as the OKAY verdict pose: a
 * low, wide, palms-up shrug. In silhouette it is a flat W, which is nothing
 * like the tall V of a PERFECT or the fold of a WHIFF.
 */
function poseRecover(p, s) {
  resetPose(p);
  const e = easeOutCubic(s.u);
  const wob = Math.sin(s.t * 13) * Math.exp(-s.t * 5);
  if (s.variant === 'good') {
    const k = backOut(clamp01(s.u / 0.4), 1.9);
    p.rootY = -0.020 * k;
    p.squash = -0.22 * k + wob * 0.1;
    p.torsoRotX = 0.06 * k;
    p.headRotZ = 0.22 * k;
    p.headRotX = 0.05 * k;
    p.armLSwing = 0.20 * k; p.armRSwing = 0.20 * k;
    p.armLLift = 0.19 + 1.02 * k; p.armRLift = 0.19 + 1.02 * k;
    p.armLBend = 0.62 * k; p.armRBend = 0.62 * k;
    p.armLTwist = 0.7 * k; p.armRTwist = -0.7 * k;
    p.legLSpread = 0.05 + 0.24 * k; p.legRSpread = 0.05 + 0.24 * k;
    p.legLBend = 0.22 * k; p.legRBend = 0.22 * k;
    p.eyeOpen = 0.72;
    p.browY = 0.45; p.browAngle = -0.30;
    p.mouthCurve = 0.12; p.mouthW = 1.25; p.mouthOpen = 0.10;
    return p;
  }
  // Plain settle: residual follow-through bleeding off into the idle.
  const r = 1 - e;
  p.rootY = -0.012 * r;
  p.squash = -0.16 * r + wob * 0.08;
  p.torsoRotY = 0.42 * r;
  p.rootRotY = 0.18 * r;
  p.headRotY = -0.16 * r;
  p.armRSwing = 1.10 * r; p.armRBend = 0.18 + 0.5 * r; p.armRLift = 0.19 + 0.16 * r;
  p.armLSwing = -0.55 * r; p.armLBend = 0.30 + 0.3 * r; p.armLLift = 0.19 + 0.30 * r;
  p.legRSwing = 0.24 * r; p.legLSwing = -0.12 * r;
  p.legLBend = 0.14 * r; p.legRBend = 0.22 * r;
  p.mouthCurve = 0.35;
  p.eyeOpen = 1;
  return p;
}

/**
 * CELEBRATE — beat-synced jumping. Two variants, deliberately different in
 * silhouette: 'perfect' is a symmetric tall V with the legs together;
 * 'great' is an asymmetric single-fist punch with one knee up.
 */
function poseCelebrate(p, s) {
  resetPose(p);
  const b = s.beat * 2; // celebrate at double time — joy is faster than idle
  const f = b - Math.floor(b);
  const j = hop(f);
  const entry = backOut(clamp01(s.t / 0.22), 2.6);
  const air = j * entry;

  p.rootY = 0.075 * air - compress(f) * 0.030 * entry;
  p.squash = (-compress(f) * 0.95 + Math.exp(-(((f - 0.20) / 0.12) ** 2)) * 0.70) * entry;
  p.headRotX = -0.30 * entry + j * 0.08;
  p.eyeOpen = 0.30;           // squeezed-shut happy eyes
  p.browY = 0.85;
  p.browAngle = -0.35;
  p.mouthCurve = 1.0;
  p.mouthOpen = 0.55 + j * 0.45;
  p.mouthW = 1.35;

  if (s.variant === 'great') {
    // Asymmetric: right fist punches the sky on every beat, left hand on hip.
    const punch = backOut(clamp01(f / 0.32), 3.0);
    p.rootRotY = 0.26 * entry;
    p.torsoRotZ = -0.14 * entry;
    p.armRSwing = -0.35 * entry;
    p.armRLift = 0.19 + (2.45 * punch - 0.35) * entry;
    p.armRBend = (0.30 + (1 - punch) * 0.9) * entry;
    p.armLSwing = -0.55 * entry;
    p.armLLift = 0.19 + 0.55 * entry;
    p.armLBend = 1.75 * entry;
    p.legRSwing = 0.85 * air + 0.1 * entry;
    p.legRBend = 1.25 * air;
    p.legLSwing = -0.20 * air;
    p.legLBend = 0.30 * air;
    p.legLSpread = 0.06; p.legRSpread = 0.20;
    p.headRotZ = -0.16 * entry;
    return p;
  }
  // 'perfect' (default): both arms straight up and out — a tall, wide V.
  p.armLSwing = -0.30 * entry; p.armRSwing = -0.30 * entry;
  p.armLLift = 0.19 + 2.30 * entry; p.armRLift = 0.19 + 2.30 * entry;
  p.armLBend = 0.10 * entry; p.armRBend = 0.10 * entry;
  p.armLTwist = 0.35 * entry; p.armRTwist = -0.35 * entry;
  p.legLSpread = -0.02; p.legRSpread = -0.02;
  p.legLSwing = -0.18 * air; p.legRSwing = -0.18 * air;
  p.legLBend = 0.55 * air; p.legRBend = 0.55 * air;
  p.torsoRotX = -0.16 * entry;
  return p;
}

/**
 * FAIL — the whiff. Failure has to be funny, so it is a full-body fold: knees
 * buckle inward, torso doubles over, both arms fling out BEHIND, head drops.
 * The silhouette is a comma. Then it wobbles back up, dizzily.
 */
function poseFail(p, s) {
  resetPose(p);
  const fold = backOut(clamp01(s.t / 0.20), 2.2);
  const rise = smootherstep((s.t - 0.85) / 0.7);
  const k = fold * (1 - rise * 0.85);
  const woozy = Math.sin(s.t * 7.5) * Math.exp(-Math.max(0, s.t - 0.4) * 1.4);

  p.rootY = -0.085 * k;
  p.squash = -0.62 * k;
  p.rootRotZ = woozy * 0.11 * k;
  p.rootRotY = woozy * 0.22 * k;
  p.torsoRotX = 0.78 * k;
  p.torsoRotZ = woozy * 0.10;
  p.hipsRotX = -0.30 * k;
  p.headRotX = 0.52 * k;
  p.headRotZ = woozy * 0.20;

  // Arms fling backwards and stay there — the pose that says "I have no idea
  // what just happened".
  p.armLSwing = -1.55 * k; p.armRSwing = -1.45 * k;
  p.armLLift = 0.19 + 0.85 * k; p.armRLift = 0.19 + 0.95 * k;
  p.armLBend = 0.20 * k; p.armRBend = 0.15 * k;
  p.armLTwist = -0.6 * k; p.armRTwist = 0.6 * k;

  // Knees buckle IN — pigeon-toed collapse is the funny version.
  p.legLSpread = -0.30 * k; p.legRSpread = -0.30 * k;
  p.legLBend = 0.85 * k; p.legRBend = 0.72 * k;
  p.legLSwing = 0.30 * k; p.legRSwing = 0.22 * k;

  p.eyeOpen = 0.18 + 0.6 * rise;
  p.pupilY = -0.5 * k;
  p.pupilX = woozy * 0.6;
  p.browY = 0.55 * k;
  p.browAngle = -0.85 * k;
  p.mouthCurve = -1.0 * k + 0.2 * rise;
  p.mouthOpen = 0.55 * k;
  p.mouthW = 0.75;
  return p;
}

/** TAUNT — hands on hips, exaggerated hip sway, head cocked, one brow up. */
function poseTaunt(p, s) {
  resetPose(p);
  const b = s.beat;
  const sway = Math.sin(b * Math.PI);
  const f = b - Math.floor(b);
  const e = smootherstep(clamp01(s.t / 0.25));
  p.rootY = (-0.018 + hop(f) * 0.014) * e;
  p.squash = (-0.14 - compress(f) * 0.35) * e;
  p.hipsRotZ = sway * 0.20 * e;
  p.rootRotY = sway * 0.16 * e;
  p.torsoRotZ = -sway * 0.16 * e;
  p.headRotZ = 0.20 * e + sway * 0.08;
  p.headRotY = -0.20 * e;
  p.headRotX = -0.10 * e;
  p.armLSwing = 0.10 * e; p.armRSwing = 0.10 * e;
  p.armLLift = 0.19 + 0.70 * e; p.armRLift = 0.19 + 0.70 * e;
  p.armLBend = 1.85 * e; p.armRBend = 1.85 * e;
  p.armLTwist = 0.4 * e; p.armRTwist = -0.4 * e;
  p.legLSpread = 0.05 + 0.16 * e; p.legRSpread = 0.05 + 0.16 * e;
  p.legLBend = 0.12 * e + Math.max(0, sway) * 0.16;
  p.legRBend = 0.12 * e + Math.max(0, -sway) * 0.16;
  p.eyeOpen = 0.62;
  p.pupilX = -0.55; p.pupilY = 0.1;
  p.browY = 0.3; p.browAngle = 0.35;
  p.mouthCurve = 0.75; p.mouthW = 0.85;
  return p;
}

// ------------------------------------------------------------- mocap clips
//
// Mixamo motion, retargeted offline onto THIS rig's pose channels
// (tools/assets/bake-clips.mjs + retarget.js). A clip only ever supplies the
// body channels; the face stays ours (a preset per call), and everything
// downstream — crossfade, damping, the additive beat layer, springs,
// impulses, blinks — treats a clip exactly like a hand-written state.

/** Face presets for clip states. Keys are pose channels; unset = REST. */
export const CLIP_FACE = {
  neutral: {},
  focus: { eyeOpen: 0.78, browY: -0.45, browAngle: 0.5, pupilX: 0.45, mouthCurve: -0.12, mouthW: 0.8 },
  fierce: { eyeOpen: 1.1, browY: -0.35, browAngle: 0.75, mouthCurve: -0.1, mouthOpen: 0.55, mouthW: 1.15 },
  joy: { eyeOpen: 0.3, browY: 0.85, browAngle: -0.35, mouthCurve: 1.0, mouthOpen: 0.6, mouthW: 1.35 },
  smug: { eyeOpen: 0.62, pupilX: -0.55, pupilY: 0.1, browY: 0.3, browAngle: 0.35, mouthCurve: 0.75, mouthW: 0.85 },
  groove: { eyeOpen: 0.55, browY: 0.4, browAngle: -0.2, mouthCurve: 0.9, mouthOpen: 0.25, mouthW: 1.2 },
};

const mod = (a, n) => ((a % n) + n) % n;

/** Clip-local time (seconds) a clip spec shows at state time `t` / beat. */
export function clipTimeAt(spec, t, u, beat) {
  const len = spec.to - spec.from;
  if (spec.beatLock) return spec.from + mod(spec.phase + (beat - spec.beat0) * spec.secPerBeat - spec.from, len);
  if (spec.loop) return spec.from + mod(t * spec.rate, len);
  return spec.from + clamp01(u) * len;
}

/** Linear sample of every baked body channel at clip time `time`. */
export function sampleClip(p, clip, time, legFrac = 0.3) {
  const x = clamp(time, 0, clip.duration) * CLIP_FPS;
  const i0 = Math.min(clip.n - 1, Math.floor(x));
  const i1 = Math.min(clip.n - 1, i0 + 1);
  const a = x - i0;
  for (let c = 0; c < CLIP_CHANNELS.length; c++) {
    const k = CLIP_CHANNELS[c];
    const tr = clip.ch[k];
    p[k] = (tr[i0] + (tr[i1] - tr[i0]) * a) * 0.001;
  }
  // Baked in leg-lengths; the pose wants fractions of this rig's height.
  p.hipsY *= legFrac;
  return p;
}

function poseClip(p, s) {
  resetPose(p);
  const spec = s.variant;
  const clip = spec && CLIPS[spec.name];
  if (!clip) return poseIdle(p, s);
  sampleClip(p, clip, clipTimeAt(spec, s.t, s.u, s.beat), s.legFrac);
  if (spec.face) Object.assign(p, spec.face);
  return p;
}

/**
 * Pick how many clip beats fit one game beat (½, 1, 2 or 4) so the playback
 * rate — clipBeatsPerGameBeat · bpm / clipTempo — is as close to 1 as it can
 * be. A 196bpm swing-dance loop under a 124bpm song plays at 1.27x with two
 * of its steps per beat, rather than at 0.63x looking drugged.
 */
export function beatLockRatio(clipTempo, bpm) {
  let best = 1, bestErr = Infinity;
  for (const m of [0.5, 1, 2, 4]) {
    const err = Math.abs(Math.log((m * bpm) / clipTempo));
    if (err < bestErr) { bestErr = err; best = m; }
  }
  return best;
}

const POSE_FN = {
  clip: poseClip,
  idle: poseIdle,
  ready: poseReady,
  windup: poseWindup,
  action: poseAction,
  recover: poseRecover,
  celebrate: poseCelebrate,
  fail: poseFail,
  taunt: poseTaunt,
};

// ------------------------------------------------------------- the animator

/**
 * Drives one character rig.
 *
 * ```js
 * const anim = makeAnimator(char, { seed: 3 });
 * anim.setState('ready');
 * anim.windup();            // on the offbeat before the note
 * anim.strike({ power: 1 }); // on the note
 * anim.react('perfect');     // on the verdict
 * anim.update(dt, clock.beat);
 * ```
 */
export class CharacterAnimator {
  /**
   * @param {THREE.Group} char  from `makeCharacter`
   * @param {object} [opts] {seed, beatScale, style}
   */
  constructor(char, opts = {}) {
    this.char = char;
    this.j = char.joints;
    this.dims = char.dims;
    this.variation = char.variation || { phase: 0, bounce: 1, blinkOffset: 0, swagger: 0.5 };
    this.rng = makeRng(opts.seed ?? char.seed ?? 1);

    this.state = 'idle';
    this.prevState = 'idle';
    this.variant = null;
    this.prevVariant = null;
    this.t = 0;            // seconds in current state
    this.prevT = 0;        // frozen local time of the outgoing state
    this.blend = 1;        // 0 = fully previous, 1 = fully current
    this.blendDur = 0.2;
    this.power = 1;
    this.hold = false;
    this.dur = Infinity;

    /** Amplitude of the additive beat layer, 0..1.4. Owned by the state. */
    this.beatAmt = 1;
    this.beatAmtTarget = 1;

    // Pose buffers — allocated once, mutated forever. Nothing here allocates
    // per frame, because a GC pause on a downbeat is a missed note.
    this._pA = makePose();
    this._pB = makePose();
    this._target = makePose();
    this._cur = makePose();
    this._layer = makePose();
    this._s = {
      t: 0, u: 0, beat: 0, variation: this.variation, variant: null, power: 1,
      legFrac: this.dims ? this.dims.legLen / this.dims.height : 0.3,
    };
    /** Where a finished clip hands off to (per `play()` call). */
    this._clipNext = 'idle';

    // Secondary-motion springs.
    this._headLag = { x: 0, v: 0 };
    this._headTilt = { x: 0, v: 0 };
    this._bobY = { x: 0, v: 0 };
    this._bobZ = { x: 0, v: 0 };
    this._prevRootY = 0;
    this._prevVelY = 0;

    // One-shot impulses layered on top of everything (hit reactions).
    this._impulseSquash = 0;
    this._impulseLift = 0;

    this._blinkT = this.variation.blinkOffset ?? 0;
    this._blink = 0;
    this._beat = 0;
    this._faceBase = null;
    this._cacheFaceBase();

    resetPose(this._cur);
  }

  _cacheFaceBase() {
    const j = this.j;
    this._faceBase = {
      pupilX: j.pupils ? j.pupils.position.x : 0,
      pupilY: j.pupils ? j.pupils.position.y : 0,
      browLY: j.browL ? j.browL.position.y : 0,
      browRY: j.browR ? j.browR.position.y : 0,
      mouthY: j.mouth ? j.mouth.position.y : 0,
      bobbleY: j.bobble ? j.bobble.position.y : 0,
      shadow: j.shadow ? j.shadow.scale.x : 1,
      eyeR: this.char.build ? this.char.build.head.w * 0.155 : 0.06,
      headH: this.char.build ? this.char.build.head.h : 0.4,
    };
  }

  // ------------------------------------------------------------- transitions

  /**
   * Enter a state, crossfading from whatever is playing.
   * @param {string} name
   * @param {object} [opts] {variant, dur, blend, power, hold, force}
   */
  setState(name, opts = {}) {
    const def = STATE_DEF[name] || STATE_DEF.idle;
    if (name === this.state && !opts.force && opts.variant === this.variant) {
      // Re-entering the same state restarts it only if asked; otherwise the
      // caller spamming setState('idle') every frame must not reset the timer.
      return this;
    }
    // Freeze the outgoing pose by remembering where it was.
    this.prevState = this.state;
    this.prevVariant = this.variant;
    this.prevT = this.t;
    // The outgoing state's real duration: a retimed windup or a clip must
    // keep sampling where it was, not at t / (its STATE_DEF default).
    this.prevDur = this.dur;
    this.state = name;
    this.variant = opts.variant ?? null;
    this.t = 0;
    this.dur = opts.dur ?? def.dur;
    this.hold = opts.hold ?? def.hold ?? false;
    this.power = opts.power ?? 1;
    this.blendDur = Math.max(0.001, opts.blend ?? def.blend);
    this.blend = 0;
    this.beatAmtTarget = def.beat;
    return this;
  }

  /** Coil. Call on the offbeat before the note — anticipation needs lead time. */
  windup(opts = {}) { return this.setState('windup', { force: true, ...opts }); }

  /** Release. Call exactly on the note. */
  strike(opts = {}) {
    this.setState('action', { force: true, ...opts });
    this.impulse(0.55 * (opts.power ?? 1), 0.04 * (opts.power ?? 1));
    return this;
  }

  /** Braced/telegraph pose. */
  ready(opts = {}) { return this.setState('ready', opts); }

  /** Back to the beat-synced idle. */
  relax(opts = {}) { return this.setState('idle', opts); }

  taunt(opts = {}) { return this.setState('taunt', { force: true, ...opts }); }

  /**
   * Play a baked mocap clip through the normal state machine.
   *
   * ```js
   * anim.play('swing', { to: CLIPS.swing.contact, dur: lead, hold: true, face: 'focus' });
   * anim.play('swing', { from: CLIPS.swing.contact, face: 'fierce' }); // on the note
   * anim.play('dance', { beatLock: true, bpm: clock.bpm, face: 'groove' });
   * ```
   * @param {string} name  clip id in clips.gen.js
   * @param {object} [o]   {from, to, dur, rate, loop, beatLock, bpm, beat0,
   *                        hold, next, blend, beat, face, power}
   *   dur      seconds to spend on [from, to] (retimes the clip; default natural)
   *   beatLock loop the clip with its own downbeats on the game's beats
   *   beat     how much of the procedural beat layer rides on top (0..1.4)
   *   face     a CLIP_FACE preset name or a {channel: value} object
   */
  play(name, o = {}) {
    const clip = CLIPS[name];
    if (!clip) return this;
    const from = o.from ?? 0;
    const to = o.to ?? clip.duration;
    const rate = o.rate ?? 1;
    const spec = {
      name, from, to, rate,
      loop: !!(o.loop || o.beatLock),
      beatLock: !!(o.beatLock && clip.tempo),
      face: typeof o.face === 'string' ? CLIP_FACE[o.face] : (o.face || null),
    };
    if (spec.beatLock) {
      const m = beatLockRatio(clip.tempo, o.bpm ?? 120);
      spec.secPerBeat = (m * 60) / clip.tempo;
      spec.beat0 = o.beat0 ?? Math.floor(this._beat);
      spec.phase = clip.downbeat ?? 0;
    }
    const dur = o.dur ?? (spec.loop ? Infinity : (to - from) / rate);
    this.setState('clip', {
      variant: spec, force: true, dur, hold: o.hold ?? false,
      blend: o.blend ?? STATE_DEF.clip.blend, power: o.power,
    });
    this.beatAmtTarget = o.beat ?? STATE_DEF.clip.beat;
    this._clipNext = o.next ?? 'idle';
    return this;
  }

  /** Clip-local time currently shown (null outside a clip) — for mirroring a source rig. */
  get clipTime() {
    if (this.state !== 'clip' || !this.variant) return null;
    return clipTimeAt(this.variant, this.t, clamp01(this.t / this.dur), this._beat);
  }

  /**
   * Verdict reaction. Maps to a state+variant whose silhouettes are mutually
   * unmistakable. Returns the state it chose.
   */
  react(verdict, opts = {}) {
    const m = VERDICT_POSE[verdict] || VERDICT_POSE.good;
    this.setState(m.state, { variant: m.variant, force: true, ...opts });
    if (verdict === 'perfect') this.impulse(0.9, 0.05);
    else if (verdict === 'great') this.impulse(0.6, 0.035);
    else if (verdict === 'miss') this.impulse(-0.75, 0);
    else this.impulse(0.25, 0.01);
    return m.state;
  }

  /** Squash/stretch impulse. Positive = stretch (launch), negative = squash. */
  impulse(squash = 0.5, lift = 0) {
    this._impulseSquash = squash;
    this._impulseLift = lift;
    return this;
  }

  /** Landing: squash, hard, and shrink the shadow's gap. */
  land(power = 1) { return this.impulse(-0.8 * power, 0); }

  /** Launch: stretch. */
  launch(power = 1) { return this.impulse(0.8 * power, 0.03 * power); }

  get finished() { return this.t >= this.dur; }

  // ------------------------------------------------------------------ update

  /**
   * @param {number} dt   real seconds (already clamped by the shell)
   * @param {number} beat float beat from `clock.beat`
   */
  update(dt, beat) {
    this._beat = beat;
    this.t += dt;

    // auto-advance
    const def = STATE_DEF[this.state] || STATE_DEF.idle;
    if (!this.hold && isFinite(this.dur) && this.t >= this.dur && def.next) {
      const next = this.state === 'clip' ? this._clipNext : def.next;
      this.setState(next, { variant: this.variant && next === 'recover' && this.state !== 'clip' ? this.variant : null });
    }

    if (this.blend < 1) this.blend = Math.min(1, this.blend + dt / this.blendDur);
    this.prevT += dt;

    const s = this._s;
    s.beat = beat;
    s.variation = this.variation;

    // --- 1. state pose, crossfaded --------------------------------------
    const wIn = smootherstep(this.blend);
    let target;
    if (wIn >= 1) {
      s.t = this.t; s.u = clamp01(this.t / this.dur); s.variant = this.variant; s.power = this.power;
      target = (POSE_FN[this.state] || poseIdle)(this._pB, s);
    } else {
      const defPrev = STATE_DEF[this.prevState] || STATE_DEF.idle;
      s.t = this.prevT; s.u = clamp01(this.prevT / (this.prevDur ?? defPrev.dur)); s.variant = this.prevVariant; s.power = 1;
      const a = (POSE_FN[this.prevState] || poseIdle)(this._pA, s);
      s.t = this.t; s.u = clamp01(this.t / this.dur); s.variant = this.variant; s.power = this.power;
      const b = (POSE_FN[this.state] || poseIdle)(this._pB, s);
      target = blendPose(this._target, a, b, wIn);
    }

    // --- 2. light damping ------------------------------------------------
    // 34 e-folds/sec ≈ 30ms to close 63% of a gap: enough to kill seams,
    // not enough to blunt a snap. Frame-rate independent by construction.
    const cur = this._cur;
    for (let i = 0; i < POSE_KEYS.length; i++) {
      const k = POSE_KEYS[i];
      cur[k] = damp(cur[k], target[k], 34, dt);
    }

    // --- 3. additive beat layer -----------------------------------------
    this.beatAmt = damp(this.beatAmt, this.beatAmtTarget, 9, dt);
    const amt = this.beatAmt * (this.variation.bounce ?? 1);
    if (amt > 0.003) {
      const L = idle(beat + this.variation.phase * 0.0, { out: this._layer, amp: amt });
      // Only the channels the state machine does not fully own get the layer.
      cur.rootY += L.rootY;
      cur.squash += L.squash * 0.65;
      cur.hipsRotZ += L.hipsRotZ;
      cur.torsoRotZ += L.torsoRotZ * 0.6;
      cur.rootRotY += L.rootRotY * 0.5;
      cur.headRotZ += L.headRotZ * 0.6;
      cur.headRotX += L.headRotX;
      cur.torsoRotX += L.torsoRotX * 0.5;
      const legAmt = amt * 0.85;
      cur.legLSwing += (L.legLSwing - 0) * legAmt * 0.6;
      cur.legRSwing += (L.legRSwing - 0) * legAmt * 0.6;
      cur.legLBend += L.legLBend * legAmt * 0.6;
      cur.legRBend += L.legRBend * legAmt * 0.6;
      cur.armLSwing += L.armLSwing * 0.55;
      cur.armRSwing += L.armRSwing * 0.55;
    }

    // --- impulses (decay fast; they are punctuation, not motion) ----------
    if (Math.abs(this._impulseSquash) > 0.002) {
      cur.squash += this._impulseSquash;
      cur.rootY += this._impulseLift;
      this._impulseSquash = damp(this._impulseSquash, 0, 11, dt);
      this._impulseLift = damp(this._impulseLift, 0, 11, dt);
    }

    // --- blink ------------------------------------------------------------
    this._blinkT += dt;
    // Blink roughly every 3.4s with a deterministic wobble; 110ms closed.
    if (this._blinkT > 3.4) {
      this._blinkT = -this.rng() * 1.6;
      this._blink = 1;
    }
    if (this._blink > 0) {
      this._blink = Math.max(0, this._blink - dt / 0.11);
      cur.eyeOpen *= 1 - Math.sin(clamp01(1 - this._blink) * Math.PI) * 0.95;
    }

    // --- secondary motion: springs driven by the body ---------------------
    const velY = dt > 0 ? (cur.rootY - this._prevRootY) / dt : 0;
    const accY = dt > 0 ? (velY - this._prevVelY) / dt : 0;
    this._prevRootY = cur.rootY;
    this._prevVelY = velY;

    // Head lags the body vertically and lags the torso's turn.
    springStep(this._headLag, clamp(-accY * 0.0016, -0.28, 0.28), 3.6, 0.42, dt);
    springStep(this._headTilt, clamp(-cur.torsoRotY * 0.42 - cur.torsoRotZ * 0.5, -0.6, 0.6), 3.0, 0.45, dt);
    // Bobble lags harder and rings longer — it is the last thing to stop.
    springStep(this._bobY, clamp(-accY * 0.0032, -0.5, 0.5), 2.5, 0.16, dt);
    springStep(this._bobZ, clamp(-(cur.rootRotY + cur.headRotZ) * 1.1, -1.2, 1.2), 2.1, 0.14, dt);

    this._applyPose(cur);
  }

  // ------------------------------------------------------------------ apply

  _applyPose(p) {
    const j = this.j;
    const H = this.dims.height;
    const fb = this._faceBase;

    // root: translation then squash about the feet, volume-compensated.
    const sy = 1 + p.squash * 0.32;
    const sxz = 1 / Math.sqrt(Math.max(0.25, sy));
    j.root.position.y = p.rootY * H;
    j.root.scale.set(sxz, sy, sxz);
    j.root.rotation.set(0, p.rootRotY, p.rootRotZ);

    j.hips.position.y = this.dims.hipY + p.hipsY * H;
    j.hips.rotation.set(p.hipsRotX, p.hipsRotY, p.hipsRotZ);
    j.torso.rotation.set(p.torsoRotX, p.torsoRotY, p.torsoRotZ);

    // Breathing lives on the torso MESH, so it never moves the head or arms.
    const breath = 1 + Math.sin(this._beat * Math.PI * 0.5) * 0.022 * this.beatAmt;
    j.torsoMesh.scale.set(breath, 2 - breath, breath);

    j.head.rotation.set(
      p.headRotX + this._headLag.x * 0.55,
      p.headRotY + this._headTilt.x * 0.30,
      p.headRotZ + this._headTilt.x * 0.22
    );
    j.head.position.y = (this.char.build.torso.h + this.char.build.neck) + this._headLag.x * 0.05 * H;

    // arms: swing forward-positive, lift outward-positive, bend elbow-positive
    this._arm(j.armL, -1, p.armLSwing, p.armLLift, p.armLTwist, p.armLBend);
    this._arm(j.armR, 1, p.armRSwing, p.armRLift, p.armRTwist, p.armRBend);
    this._leg(j.legL, -1, p.legLSwing, p.legLSpread, p.legLBend);
    this._leg(j.legR, 1, p.legRSwing, p.legRSpread, p.legRBend);

    // bobble: lag + ring
    if (j.bobble) {
      j.bobble.position.y = fb.bobbleY + this._bobY.x * 0.06 * H;
      j.bobble.rotation.z = this._bobZ.x * 0.5;
      j.bobble.rotation.x = this._bobY.x * 0.6;
    }

    // --- face -------------------------------------------------------------
    const open = clamp(p.eyeOpen, 0.02, 1.6);
    if (j.eyes) j.eyes.scale.set(1, open, 1);
    if (j.pupils) {
      j.pupils.scale.set(1, clamp(open, 0.02, 1.1), 1);
      j.pupils.position.x = fb.pupilX + p.pupilX * fb.eyeR * 0.42;
      j.pupils.position.y = fb.pupilY + p.pupilY * fb.eyeR * 0.42;
    }
    if (j.browL) {
      const by = p.browY * fb.eyeR * 0.55;
      j.browL.position.y = fb.browLY + by;
      j.browR.position.y = fb.browRY + by;
      j.browL.rotation.z = -p.browAngle;
      j.browR.rotation.z = p.browAngle;
    }
    if (j.mouthL) {
      const c = clamp(p.mouthCurve, -1.3, 1.3);
      j.mouthL.rotation.z = -c * 0.62;
      j.mouthR.rotation.z = c * 0.62;
      const o = clamp01(p.mouthOpen);
      j.mouth.scale.set(clamp(p.mouthW, 0.3, 2) * (1 + o * 0.15), 1 + o * 0.35, 1);
      j.mouth.position.y = fb.mouthY + o * fb.headH * 0.055;
      if (j.gape) {
        const g = Math.max(0.0001, o);
        j.gape.scale.set(g * 1.15 * clamp(p.mouthW, 0.3, 2), g, g * 0.6);
        j.gape.position.y = fb.mouthY - o * fb.headH * 0.055;
      }
    }

    // --- shadow: it is the only cue for height off the ground -------------
    if (j.shadow) {
      const air = clamp01(p.rootY / 0.13);
      const spread = clamp01(-p.squash * 0.5);
      const sc = fb.shadow * (1 - air * 0.34 + spread * 0.12);
      j.shadow.scale.set(sc, sc, 1);
      j.shadow.material.opacity = 0.58 * (1 - air * 0.55) + spread * 0.08;
    }
  }

  _arm(a, sx, swing, lift, twist, bend) {
    a.upper.rotation.set(-swing, twist * sx, sx * lift);
    a.fore.rotation.x = -bend;
  }

  _leg(l, sx, swing, spread, bend) {
    l.thigh.rotation.set(-swing, 0, sx * spread);
    l.shin.rotation.x = bend;
  }

  /** Current pose, for tests and tooling. Do not mutate. */
  get pose() { return this._cur; }
}

export function makeAnimator(char, opts) {
  return new CharacterAnimator(char, opts);
}
