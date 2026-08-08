/**
 * Shared feel constants.
 *
 * These are the numbers that must agree across every minigame, or the series
 * stops reading as one product. A PERFECT has to punch exactly as hard in
 * game five as it did in game one.
 *
 * Everything here was chosen to a principle, not by taste alone:
 *  - Feedback latency is budgeted. Visual response to a press must begin on
 *    the SAME frame as the press. Audio response must be scheduled at the
 *    press time, not the frame time.
 *  - Hitstop sells impact more cheaply than any particle system, but it eats
 *    rhythm. So it is measured in milliseconds, capped well under a 16th note
 *    at our fastest tempo (180bpm 16th = 83ms), and never applied to the
 *    audio clock — only to gameplay/animation time.
 *  - Screen shake is directional and decays fast. Omnidirectional shake reads
 *    as noise; a short directional kick reads as force.
 */

export const FEEL = {
  /** Countdown before a round: bars of lead-in the player gets to lock on. */
  leadInBars: 2,

  /** Hitstop, seconds. Applied to gameplay time only, never to the clock. */
  hitstop: {
    perfect: 0.075,
    great: 0.045,
    good: 0.022,
    miss: 0.0,
    finale: 0.16,
  },

  /** Directional camera kick magnitude in world units. */
  shake: {
    perfect: 0.16,
    great: 0.10,
    good: 0.045,
    miss: 0.055, // a miss shakes too — it just feels bad instead of good
    finale: 0.5,
  },

  /** Full-screen flash alpha. Sparingly: this is the loudest tool we have. */
  flash: {
    perfect: 0.22,
    great: 0.10,
    good: 0.0,
    miss: 0.0,
    finale: 0.6,
  },

  /** Verdict popup lifetime, seconds. Short. Rhythm games clutter fast. */
  popupLife: 0.62,

  /** Colours. One palette for the whole series. */
  color: {
    perfect: 0xffd93d,
    great: 0x4dd6ff,
    good: 0x9ee87a,
    miss: 0xff5d73,
    combo: 0xffffff,
    bgDeep: 0x0b0a1a,
  },

  /** Verdict copy. Playful, short, readable at a glance in motion. */
  label: {
    perfect: 'PERFECT!',
    great: 'GREAT!',
    good: 'OKAY',
    miss: 'WHIFF',
  },

  /**
   * Combo milestones that earn an escalation (bigger popup, music layer,
   * crowd reaction). Spaced so a good player gets one every few seconds.
   */
  comboMilestones: [5, 10, 20, 35, 50, 75, 100],

  /**
   * Input feedback the game gives for a press that hit NOTHING. Silence here
   * feels broken; a full verdict feels punishing. A small tick is right.
   */
  ghostPressTick: 0.12, // sfx gain

  /** Max frame dt fed to gameplay — a tab-switch must not teleport anything. */
  maxDt: 1 / 20,
};

/** Per-verdict bundle, so callers do one lookup instead of five. */
export function feelFor(verdict) {
  return {
    hitstop: FEEL.hitstop[verdict] ?? 0,
    shake: FEEL.shake[verdict] ?? 0,
    flash: FEEL.flash[verdict] ?? 0,
    color: FEEL.color[verdict] ?? 0xffffff,
    label: FEEL.label[verdict] ?? '',
  };
}
