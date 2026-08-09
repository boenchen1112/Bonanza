/**
 * Drumline Dash — the chart.
 *
 * Pure data + pure functions. No THREE, no audio, no DOM: the verification
 * script imports this directly and the game reads the same numbers, so what is
 * tested is what is played.
 *
 * ── How a rhythm is written ────────────────────────────────────────────────
 * A bar is 16 sixteenth-note SLOTS. A pattern is the sorted list of slots that
 * carry a hit. Slot 0 is the downbeat, 4 is beat 2, 8 is beat 3, 12 is beat 4;
 * even slots are eighths, odd slots are sixteenths.
 *
 * ── Why the difficulty curve is shaped like this ───────────────────────────
 * The order of what gets introduced is the whole design of a memory game:
 *
 *   1. QUARTERS       — 3 notes on the pulse. You cannot get this wrong, which
 *                       is the point: it teaches the call/answer loop, not
 *                       rhythm.
 *   2. EIGHTHS        — density rises to 5 notes but every note still lands on
 *                       a subdivision you are already tapping your foot to.
 *   3. RESTS          — a pattern that does NOT start on the downbeat. Rests
 *                       are harder than notes because there is nothing to
 *                       remember, only an absence, and absences are not
 *                       rehearsable. That is why they land last.
 *   4. SIXTEENTHS     — odd slots, always as a pair with an adjacent even slot
 *                       so they read as an ornament on a beat you know rather
 *                       than a new grid to count.
 *   5. THE FINALE     — two bars held in memory at once, returned whole.
 *
 * Every pattern is authored, not generated. A random rhythm is a quiz; a
 * written one has a shape you can hum back, and humming it back is the game.
 */

/** Beats per slot. 16 slots to a 4/4 bar. */
export const SLOT_BEATS = 0.25;
export const BEATS_PER_BAR = 4;

/** TEACH — 8 bars. Pulse, then one ornament. Nobody should fumble these. */
export const TEACH = [
  [0, 4, 8],          // three quarters, then space. The demonstration.
  [0, 4, 8, 12],      // four on the floor
  [0, 2, 4, 8],       // first eighth, on the strongest beat
  [0, 4, 6, 12],      // eighth in the middle of the bar
];

/** PLAY — 16 bars. Five notes, eighths, the ornament moves around the bar. */
export const PLAY = [
  [0, 4, 6, 8, 12],
  [0, 2, 4, 8, 10],
  [0, 4, 8, 10, 12],
  [0, 2, 6, 8, 12],
  [0, 4, 6, 10, 12],
  [0, 2, 4, 6, 8],
  [0, 4, 8, 12, 14],
  [0, 4, 6, 8, 10],
];

/**
 * ESCALATE — 10 bars. Missing downbeats, sixteenth pickups, capped at 5 notes
 * (was 6-7) — playtesting found the density plus the off-beat starts too much
 * at once, especially when a bar's first hit isn't on the downbeat. Every
 * non-finale bar in the whole chart now tops out at 5 notes; only the finale
 * is allowed to go higher.
 */
export const ESCALATE = [
  [2, 4, 10, 12, 14],       // no downbeat: the bar starts on a rest
  [0, 3, 4, 7, 8],          // sixteenth pickups into 2 and 3
  [0, 2, 6, 8, 10],         // rest where beat 2 should be
  [2, 6, 8, 11, 12],        // syncopation + a sixteenth, no downbeat
  [0, 2, 4, 10, 12],        // one hole in the middle
];

/** FINALE — a two-bar call returned whole. Worth double. The only phrase
 *  allowed past the 5-notes-per-bar cap (9 total here, ceiling is 10). */
export const FINALE = [
  [0, 4, 6, 12],
  [0, 2, 6, 8, 12],
];

/** How many beats of lead-in the transport runs before beat 0. */
export const LEAD_BEATS = 8;

/** Beats of victory-lap after the last response bar closes. */
export const OUTRO_BEATS = 8;

/**
 * Build the phrase list.
 *
 * Phrase k occupies two bars: the drum major calls, then you answer. The FIRST
 * call deliberately lands inside the lead-in (beats -4..-1), so the player has
 * heard and seen a complete call before a single note is scored, and the very
 * first thing they are asked to do is the thing they just watched.
 *
 * @returns {{index:number, section:string, bars:number, callBeat:number,
 *            respBeat:number, endBeat:number, slots:number[][],
 *            noteBeats:number[], callBeats:number[], finale:boolean}[]}
 */
export function buildChart() {
  const singles = [
    ...TEACH.map((s) => ({ slots: [s], section: 'teach' })),
    ...PLAY.map((s) => ({ slots: [s], section: 'play' })),
    ...ESCALATE.map((s) => ({ slots: [s], section: 'escalate' })),
  ];

  const phrases = [];
  const push = (index, section, slots, callBeat, finale) => {
    const bars = slots.length;
    const respBeat = callBeat + bars * BEATS_PER_BAR;
    const spread = (base) => {
      const out = [];
      for (let b = 0; b < bars; b++) {
        for (const s of slots[b]) out.push(base + b * BEATS_PER_BAR + s * SLOT_BEATS);
      }
      return out;
    };
    phrases.push({
      index, section, bars, callBeat, respBeat,
      endBeat: respBeat + bars * BEATS_PER_BAR,
      slots,
      callBeats: spread(callBeat),
      noteBeats: spread(respBeat),
      finale: !!finale,
    });
  };

  singles.forEach((p, k) => push(k, p.section, p.slots, 8 * k - 4, false));
  push(singles.length, 'finale', FINALE, 8 * singles.length - 4, true);
  return phrases;
}

/** Last beat of the round, including the victory lap. */
export function chartEndBeat(phrases) {
  return phrases[phrases.length - 1].endBeat + OUTRO_BEATS;
}

/** Total bars after beat 0 — what `durationBars` has to report. */
export function chartDurationBars(phrases) {
  return Math.ceil(chartEndBeat(phrases) / BEATS_PER_BAR);
}

/**
 * Points a flawless run is worth, before normalisation. The finale counts
 * double, which is what makes the last phrase the biggest hit of the round.
 */
export function maxPoints(phrases, perNote = 1000) {
  let t = 0;
  for (const p of phrases) t += p.noteBeats.length * perNote * (p.finale ? 2 : 1);
  return t;
}

/** Verdict -> the fraction of a note's advance it is worth in the race. */
export const ADVANCE_WEIGHT = { perfect: 1, great: 0.82, good: 0.5, miss: 0 };
