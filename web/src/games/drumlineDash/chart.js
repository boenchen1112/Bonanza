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
 *   4. SYNCOPATION    — eighths off the beat, still no sixteenths: the grid
 *                       never gets finer than the one you were taught.
 *   5. THE FINALE     — one bar, called twice, returned whole.
 *
 * Every pattern is authored, not generated. A random rhythm is a quiz; a
 * written one has a shape you can hum back, and humming it back is the game.
 */

/** Beats per slot. 16 slots to a 4/4 bar. */
import { FEEL } from '../../core/feel.js';

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
 * ESCALATE — 10 bars. Rests and syncopation on the eighth grid, capped at 5
 * notes. Playtesting twice found the end of the round too hard: first the
 * density (was 6-7 notes), then the memory load — sixteenth pickups plus bars
 * with no downbeat, back to back, right before a two-bar finale. So: no odd
 * (sixteenth) slots, only two bars that start on a rest, never adjacent.
 */
export const ESCALATE = [
  [0, 2, 6, 8, 12],         // rest where beat 2 should be
  [2, 4, 8, 10, 12],        // no downbeat: the bar starts on a rest
  [0, 6, 8, 10, 12],        // hold the downbeat, then a late eighth run
  [0, 2, 4, 10, 12],        // one hole in the middle
  [2, 6, 8, 12, 14],        // syncopated, no downbeat
];

/** FINALE — a two-bar call returned whole, worth double. The second bar
 *  repeats the first, so it is one bar to remember played twice: the length
 *  sells the ending, not a second rhythm to hold in memory. */
export const FINALE = [
  [0, 4, 6, 12],
  [0, 4, 6, 12],
];

/** How many beats of lead-in the transport runs before beat 0. */
export const LEAD_BEATS = FEEL.leadInBars * 4;

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
