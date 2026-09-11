/**
 * Swing Kings — the rules, with no rendering in them.
 *
 * Two independent axes, which is the whole design:
 *
 *   RELEASE TIMING  -> accuracy   (perfect / great / good / whiff)  [NoteJudge]
 *   WINDUP LENGTH   -> power      (bunt / line drive / home run)    [here]
 *
 * The power curve is the interesting half. Its shape is chosen so that a
 * perfectly-timed bare tap is a BUNT — correct, scored, and unrewarding. That
 * single fact teaches the mechanic with no tutorial screen: the player gets a
 * "PERFECT" and a dribbler, looks at the ball still in flight next time, and
 * works out on their own that the hold is the point.
 *
 * Concretely: `ideal` is the pitch's flight length in beats, so the ideal
 * gesture is "start the coil when the machine fires, release when the ball
 * arrives". The player's windup literally conducts the ball's arc.
 */

import { clamp, clamp01, smoothstep } from '../../core/util.js';
import { SCORE } from '../../core/judge.js';

export const BEATS_PER_BAR = 4;
export const LEAD_IN_BEATS = 8;      // FEEL.leadInBars * 4
export const SCORED_BARS = 32;       // teach 8 + play 16 + escalate 8
export const FINALE_BEAT = 128;      // the grand slam lands here
export const END_BEAT = FINALE_BEAT + 12; // three bars of curtain call

/** Presentation for each hit tier. `dist` is how far the ball actually goes. */
export const TIERS = {
  bunt: { id: 'bunt', label: 'BUNT', color: 0x5ce1ff, dist: 5.5, lift: 0.55 },
  liner: { id: 'liner', label: 'LINE DRIVE', color: 0xffd93d, dist: 22, lift: 0.34 },
  homer: { id: 'homer', label: 'HOME RUN!', color: 0xff9a3a, dist: 52, lift: 0.72 },
  slam: { id: 'slam', label: 'GRAND SLAM!', color: 0xfff6d8, dist: 120, lift: 0.85 },
};

/**
 * Windup length (in beats) -> power 0..1.
 *
 * - `DEAD` swallows the few ms between a keydown and its keyup on a tap, so a
 *   tap is unambiguously zero power rather than "a very small windup".
 * - `smoothstep` rather than an ease-out: an ease-out would hand a half-length
 *   hold ~70% power and the tiers would stop meaning anything.
 * - Over-holding sags. Coiling for four bars is not more power, it is a tired
 *   arm — and without this the optimal strategy is "hold the button forever",
 *   which deletes the gesture.
 */
export function powerFor(holdBeats, ideal = 2) {
  const DEAD = 0.1;
  if (!(holdBeats > DEAD)) return 0;
  const t = clamp01((holdBeats - DEAD) / Math.max(0.25, ideal - DEAD));
  const overStart = ideal * 1.8;
  const over = holdBeats > overStart
    ? clamp01((holdBeats - overStart) / (ideal * 2.2))
    : 0;
  return clamp01(smoothstep(t) - over * 0.55);
}

export function tierFor(power, finale = false) {
  if (power >= 0.72) return finale ? TIERS.slam : TIERS.homer;
  if (power >= 0.28) return TIERS.liner;
  return TIERS.bunt;
}

/** Power's contribution to score. A bunt still pays; it just never wins. */
export const powerMul = (p) => 0.42 + 0.58 * clamp01(p);

/** Points for one judged pitch, and the most it could have been worth. */
export function scoreFor(verdict, power, finale) {
  const mul = finale ? 2 : 1;
  return {
    got: (SCORE[verdict] ?? 0) * powerMul(power) * mul,
    max: SCORE.perfect * mul,
  };
}

/**
 * The chart.
 *
 * Every entry is a ball leaving the machine at `launchBeat` and crossing the
 * plate at `targetBeat`. Nothing is ever asked of the player that is not
 * already visible as a moving object at least `lead` beats early — that is
 * enforced structurally here rather than remembered per-cue.
 */
export function buildSchedule() {
  const out = [];
  let id = 0;

  const add = (targetBeat, lead, o = {}) => {
    out.push({
      id: id++,
      targetBeat,
      lead,
      launchBeat: targetBeat - lead,
      ideal: o.ideal ?? clamp(lead, 1.2, 2.2),
      apex: o.apex ?? (lead >= 6 ? 7.0 : lead >= 2 ? 2.75 : 1.75),
      kind: o.kind || 'normal',
      section: o.section || 'teach',
      scored: o.scored !== false,
      finale: !!o.finale,
      // runtime
      live: false, done: false, ball: null, note: null,
    });
  };

  // --- lead-in: one full demonstration, swung by the batter, not scored ----
  add(-5, 2, { kind: 'demo', section: 'demo', scored: false });

  // --- teach (bars 0-7): one pitch a bar, always on the downbeat ----------
  for (let b = 0; b < 8; b++) add(b * 4, 2, { section: 'teach' });

  // --- play (bars 8-23) ---------------------------------------------------
  // 8-15: still one a bar, but it moves between the downbeat and beat 3, so
  // the player has to read the BALL rather than count bars.
  for (let b = 8; b < 16; b++) add(b * 4 + (b % 2 ? 2 : 0), 2, { section: 'play' });
  // 16-23: two a bar. Density, not speed — speed is the escalation's job.
  for (let b = 16; b < 24; b++) {
    add(b * 4, 2, { section: 'play' });
    add(b * 4 + 2, 2, { section: 'play' });
  }

  // --- escalate (bars 24-29): faster pitches, landing on the offbeats -----
  for (let b = 24; b < 30; b++) {
    add(b * 4 + 1.5, 1.5, { section: 'escalate', kind: 'fast' });
    add(b * 4 + 3.5, 1.5, { section: 'escalate', kind: 'fast' });
  }

  // --- the grand slam: launched on bar 30, arrives two bars later ---------
  add(FINALE_BEAT, 8, {
    // Apex 4.6, not 7.4: at 7.4 the ball spent its whole 4s flight above the
    // top of the frame — the song's climax read as dots in the sky.
    section: 'finale', kind: 'slam', ideal: 6, apex: 4.6, finale: true,
  });

  return out;
}

/** Which section a beat is in — drives crowd energy and music intensity. */
export function sectionAt(beat) {
  if (beat < 0) return 'lead';
  if (beat < 32) return 'teach';
  if (beat < 96) return 'play';
  if (beat < FINALE_BEAT) return 'escalate';
  return 'finale';
}
