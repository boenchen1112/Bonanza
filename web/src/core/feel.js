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
 *  - Nothing scales alone. When a moment gets bigger, hitstop, shake, flash,
 *    particle count and colour temperature all move together — see
 *    `comboEscalation`. Scaling one channel makes a moment louder; scaling all
 *    of them makes it *bigger*, which is a different and better feeling.
 */

export const FEEL = {
  /** Countdown before a round: bars of lead-in the player gets to lock on. */
  leadInBars: 2,

  /**
   * Hitstop, seconds. Applied to gameplay time only, never to the clock.
   *
   * Retuned from the first-pass guesses. The rule used: hitstop must be long
   * enough to be *seen* (>= 3 frames at 60Hz = 50ms, or nobody registers it)
   * but short enough that a 16th-note follow-up is still hittable at our
   * fastest tempo (83ms). That leaves a usable band of 50-80ms for the top
   * verdict, and PERFECT takes the top of it.
   *
   * `good` sits at ~1.5 frames — deliberately below the threshold of conscious
   * perception. You do not notice it; you notice its absence on a MISS.
   * `miss` gets a real (if tiny) freeze now: a whiff that produces no time
   * distortion at all reads as the game not having noticed. 18ms is a single
   * frame of "ugh" and costs the player nothing rhythmically.
   */
  hitstop: {
    perfect: 0.078,
    great: 0.048,
    good: 0.024,
    miss: 0.018,
    finale: 0.17,
  },

  /** Hard ceiling after combo escalation. Above this the game stops feeling live. */
  hitstopMax: 0.098,

  /**
   * Directional camera kick magnitude in world units.
   *
   * Calibrated against the reference framing (camera ~6.5u out, 50deg fov):
   * 0.18u is ~3% of the frame width — big enough to feel in the gut, small
   * enough that the cube's silhouette never leaves the eye. Anything past ~5%
   * starts costing readability, which is the one thing we never spend.
   *
   * MISS kicks HARDER than GOOD. It is the only place a failure gets more of a
   * channel than a success, and it is deliberate: the whiff should thud.
   */
  shake: {
    perfect: 0.185,
    great: 0.105,
    good: 0.042,
    miss: 0.075,
    finale: 0.55,
  },

  /**
   * Full-screen flash alpha. Sparingly: this is the loudest tool we have, and
   * it is also the one that most easily hides the thing the player must read.
   *
   * Dropped PERFECT from 0.22 to 0.16: with the layered VFX now doing the
   * work, a 0.22 white wash was competing with the burst instead of framing
   * it — the classic "everything at once reads as a flat flash". GOOD stays at
   * zero on purpose. The player should be able to tell a GOOD from a GREAT
   * with their eyes closed to everything but the screen brightness.
   */
  flash: {
    perfect: 0.16,
    great: 0.075,
    good: 0.0,
    miss: 0.05,
    finale: 0.55,
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
    /** The white-hot core every impact shares, whatever the verdict colour. */
    hot: 0xfff6d8,
    /** Confetti set — four hues, none of them a verdict colour by accident. */
    party: [0xffd93d, 0x4dd6ff, 0xff5d73, 0x9ee87a, 0xc08bff, 0xffffff],
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
   * Escalation curves, indexed by TIER = how many milestones you have passed
   * (0..comboMilestones.length). Every channel is a table rather than a
   * formula so the shape is legible and tunable per step.
   *
   * The curves are deliberately NOT the same shape:
   *  - `count` climbs hardest (x2.6 by the top). Particle count is the cheapest
   *    channel to escalate and the one with the most headroom.
   *  - `size` climbs gently (x1.5). Doubling the size of everything just makes
   *    the screen soup; the burst should get denser, not fatter.
   *  - `shake`/`hitstop` climb least (x1.6 / x1.35) and then stop. Past a
   *    point, more screen shake is a worse game, not a bigger moment.
   *  - `hot` is the fraction of the verdict colour blended toward white. A
   *    high combo literally runs hotter, which is a colour cue the player
   *    reads before they read the number.
   *  - `rings` adds physical layers, which is what actually makes tier 6 feel
   *    different in kind rather than just in volume.
   */
  comboEscalation: {
    count: [1.00, 1.18, 1.38, 1.62, 1.90, 2.15, 2.40, 2.60],
    size: [1.00, 1.06, 1.12, 1.20, 1.28, 1.36, 1.44, 1.50],
    shake: [1.00, 1.06, 1.14, 1.24, 1.34, 1.45, 1.54, 1.60],
    hitstop: [1.00, 1.04, 1.09, 1.15, 1.21, 1.27, 1.32, 1.35],
    hot: [0.00, 0.06, 0.13, 0.21, 0.30, 0.40, 0.50, 0.60],
    rings: [0, 0, 1, 1, 2, 2, 3, 3],
  },

  /** Milestone-crossing flourish — the one-frame "you just levelled up". */
  milestone: {
    /** Ring wave onsets, seconds. Three waves so it reads as a pulse, not a pop. */
    waveDelays: [0, 0.075, 0.15],
    confetti: 44,
    shake: 0.24,
    flash: 0.20,
    hitstop: 0.06,
    /** Word shown in-world. Falls back to 'COMBO!' when the number isn't baked. */
    words: { 5: 'x5', 10: 'x10', 20: 'x20', 35: 'x35', 50: 'x50', 75: 'x75', 100: 'x100' },
  },

  /**
   * VFX composition. Owned by render/fx; lives here because a minigame that
   * wants a bespoke moment must be able to reason about the same numbers.
   *
   * `layerDelay` is the heart of the whole system: the seconds after impact at
   * which each layer of the response begins. See render/fx/recipes.js for why
   * these specific offsets, measured in frames at 60Hz.
   */
  fx: {
    quality: 'high',
    qualityScale: { high: 1.0, med: 0.62, low: 0.32 },

    /** Base particle counts at tier 0, high quality, before verdict scaling. */
    burst: {
      perfect: { shards: 22, sparks: 34, streaks: 14, confetti: 30 },
      great: { shards: 13, sparks: 20, streaks: 9, confetti: 0 },
      good: { shards: 0, sparks: 11, streaks: 0, confetti: 0 },
      miss: { shards: 0, sparks: 9, streaks: 0, confetti: 0 },
    },

    /**
     * World-space RADIUS each verdict's outer shockwave reaches.
     * Sized against the reference framing, where the visible frame is ~6 world
     * units tall: a PERFECT wave (2.8 radius = 5.6 across) very nearly fills
     * the frame, which is exactly as far as a shockwave should ever go.
     */
    ringReach: { perfect: 2.8, great: 1.9, good: 1.15, miss: 1.0 },

    /** Ribbon trails. */
    trail: { segments: 28, sampleHz: 90, width: 0.16, maxTrails: 8 },

    /** Beat-synced ambient sparkle, so the screen breathes between hits. */
    ambient: {
      perBeat: 2,        // motes released on an off-beat
      downbeatBonus: 3,  // extra on beat 1 of the bar
      radius: 7.0,
      height: 5.0,
      life: 2.6,
      size: 0.075,
    },

    /** Ground decal (scorch) defaults. */
    decal: { drop: 1.6, life: 0.95, size: 1.5 },
  },

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

/** How many combo milestones `combo` has passed. 0..comboMilestones.length */
export function comboTier(combo) {
  const m = FEEL.comboMilestones;
  let t = 0;
  for (let i = 0; i < m.length; i++) if (combo >= m[i]) t = i + 1;
  return t;
}

/** True on exactly the hit that lands on a milestone. */
export function isMilestone(combo) {
  return FEEL.comboMilestones.indexOf(combo) >= 0;
}

/**
 * Verdict feel with combo escalation folded in. Everything that scales, scales
 * here, so no caller has to remember which channels escalate.
 */
export function feelForCombo(verdict, combo = 0) {
  const t = comboTier(combo);
  const e = FEEL.comboEscalation;
  const i = Math.min(t, e.count.length - 1);
  return {
    tier: t,
    hitstop: Math.min(FEEL.hitstopMax, (FEEL.hitstop[verdict] ?? 0) * e.hitstop[i]),
    shake: (FEEL.shake[verdict] ?? 0) * e.shake[i],
    flash: FEEL.flash[verdict] ?? 0,
    color: FEEL.color[verdict] ?? 0xffffff,
    label: FEEL.label[verdict] ?? '',
    countScale: e.count[i],
    sizeScale: e.size[i],
    hot: e.hot[i],
    extraRings: e.rings[i],
  };
}

/** Blend a hex colour toward white by `t`. Used by the combo heat curve. */
export function heatUp(hex, t) {
  if (t <= 0) return hex;
  const r = Math.round(((hex >> 16) & 255) + (255 - ((hex >> 16) & 255)) * t);
  const g = Math.round(((hex >> 8) & 255) + (255 - ((hex >> 8) & 255)) * t);
  const b = Math.round((hex & 255) + (255 - (hex & 255)) * t);
  return (r << 16) | (g << 8) | b;
}
