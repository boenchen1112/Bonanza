/**
 * Verdict composition — the layering table.  [render agent owns this directory]
 *
 * `fx.verdict()` is the one call a minigame makes when a note is judged. This
 * file is the score it plays.
 *
 * ─────────────────────────────────────────────────────────────────────────
 * WHY LAYERS ARE STAGGERED
 * ─────────────────────────────────────────────────────────────────────────
 * Fire every element at t=0, at the same scale, with the same duration, and
 * the eye integrates them into a single white blob: a flash. A flash carries
 * exactly one bit — "something happened" — and it is over before the player's
 * saccade lands on it. Perceptually, a saccade to a sudden peripheral event
 * takes roughly 150-250ms. If the whole effect is finished in 200ms, most
 * players never actually LOOK at it; they only see the afterimage.
 *
 * So the response is written as three acts across ~350ms, with each act
 * roughly triple the spatial scale and triple the duration of the one before.
 * The eye always has a next thing to track, and by the time it arrives, the
 * slow, big, readable layer (the callout) is the thing waiting for it.
 *
 *   ACT 1 — CONTACT      t = 0ms       scale ~0.5u   duration 60-180ms
 *     Core flare, directional shard cone, thin fast inner ring.
 *     Must be on the SAME FRAME as the press. This is the entire
 *     responsiveness budget; nothing here is allowed a delay.
 *
 *   ACT 2 — RELEASE      t = 55-110ms  scale ~2-5u   duration 300-500ms
 *     Radial spark spray, the big wobbling outer shockwave, ground bloom.
 *     55ms ≈ 3 frames: long enough that the eye reads act 2 as *caused by*
 *     act 1 rather than simultaneous with it, short enough that it still
 *     feels like one event. This is also where the hitstop ends, so the
 *     spray is the first thing that moves when time restarts — which is the
 *     oldest trick in the impact-feel book and still the best one.
 *
 *   ACT 3 — VERDICT      t = 130-220ms scale ~1u     duration 600-800ms
 *     World-space callout, confetti, the faint echo ring.
 *     130ms ≈ 8 frames: this is when the saccade arrives. The callout is
 *     therefore the first thing in focus, which is what makes the feedback
 *     *legible* rather than merely loud.
 *
 * Speed lines are the exception: they are emitted in act 1 but travel INWARD
 * and are timed to collapse onto the hit point at ~120ms, so they visually
 * hand off from act 1 to act 3.
 *
 * ─────────────────────────────────────────────────────────────────────────
 * WHY EACH VERDICT IS A DIFFERENT SHAPE, NOT A DIFFERENT SIZE
 * ─────────────────────────────────────────────────────────────────────────
 * If PERFECT is just a bigger GREAT, the player has to compare against a
 * memory to tell them apart. Instead each verdict removes or replaces whole
 * acts, so they differ in silhouette and can be told apart from a single
 * glance at a single frame:
 *
 *   PERFECT  all three acts + echo ring + confetti + ground scorch.
 *            Directional: the shard cone fires along the hit direction, so a
 *            perfect hit visibly *sends* something.
 *   GREAT    acts 1-2 and the callout. No confetti, no echo, no scorch.
 *            Same silhouette family as PERFECT, visibly one step down.
 *   GOOD     act 1 only, small, plus the callout. One thin ring. No shards.
 *            Reads as "contact was made" and nothing more.
 *   MISS     inverted. No outward burst at all: a slow grey puff with high
 *            drag that sags downward, a ring that CONTRACTS instead of
 *            expanding, and a callout that drops rather than rises. Failure
 *            is funny, not punishing — the whiff is a deflating balloon, and
 *            it is worth watching.
 */

/** Layer ids. Integers because the deferred queue is a preallocated struct. */
export const L = {
  CORE: 0,
  SHARDS: 1,
  RING_IN: 2,
  STREAKS: 3,
  SPRAY: 4,
  RING_OUT: 5,
  DECAL: 6,
  TEXT: 7,
  CONFETTI: 8,
  ECHO: 9,
  PUFF: 10,
  SAG_RING: 11,
  MILESTONE_WAVE: 12,
};

/**
 * [layer, delaySeconds] per verdict, in emission order.
 * Delays are quoted in 60Hz frames in the comments because that is the unit
 * the feel was tuned in.
 */
export const RECIPE = {
  perfect: [
    [L.CORE, 0.000],      // frame 0 — the responsiveness contract
    [L.SHARDS, 0.000],    // frame 0
    [L.RING_IN, 0.000],   // frame 0
    [L.STREAKS, 0.012],   // frame 1 — offset only so it doesn't share the core's peak
    [L.SPRAY, 0.055],     // frame 3 — "caused by" the contact
    [L.DECAL, 0.070],     // frame 4 — the ground registers it a beat later
    [L.RING_OUT, 0.090],  // frame 5 — the big wave leaves the impact behind
    [L.TEXT, 0.130],      // frame 8 — the saccade lands here
    [L.CONFETTI, 0.160],  // frame 10 — celebration trails the verdict
    [L.ECHO, 0.220],      // frame 13 — one faint late ring so it doesn't end flat
  ],
  great: [
    [L.CORE, 0.000],
    [L.SHARDS, 0.000],
    [L.RING_IN, 0.000],
    [L.STREAKS, 0.020],
    [L.SPRAY, 0.060],
    [L.RING_OUT, 0.110],
    [L.TEXT, 0.140],
  ],
  good: [
    [L.CORE, 0.000],
    [L.RING_IN, 0.000],
    [L.SPRAY, 0.070],
    [L.TEXT, 0.150],
  ],
  miss: [
    [L.PUFF, 0.000],
    [L.SAG_RING, 0.050],
    [L.TEXT, 0.180],      // slowest callout of the four: the whiff hangs
  ],
};

/** Vocabulary baked into the word atlas at boot. Order = atlas row order. */
export const VOCAB = [
  'PERFECT!', 'GREAT!', 'OKAY', 'WHIFF',
  'x5', 'x10', 'x20', 'x35', 'x50', 'x75', 'x100',
  'COMBO!', 'NICE!', 'ON FIRE!', 'UNREAL!',
];
