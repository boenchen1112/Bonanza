/**
 * Bounce Brigade — the level IS the melody.
 *
 * This file is the whole idea of the game, written down as data.
 *
 * A platform is a note:
 *   - its ONSET BEAT is where it sits along the track (x = beat * UNITS_PER_BEAT,
 *     because the bouncer travels at a constant speed, so time and distance are
 *     the same axis and the chart is a piano roll you can run across);
 *   - its PITCH is its HEIGHT (`degToY`) and its HUE (`COLOR_FOR_DEGREE`), so a
 *     player can literally see the tune coming toward them;
 *   - its DURATION is its WIDTH — a long note is a long ramp you roll along.
 *
 * That last one is what turns "hold across two beats and release on the
 * downbeat" from a UI gesture into a place in the world: the charge platforms
 * are the melody's half notes, they are visibly four times as long as the
 * eighth-note platforms, and the chasm after them is visibly four times as
 * wide. Nothing on screen has to be read, only looked at.
 *
 * Harmony: the backing track "Rubber Band Stand" is D major, I-vi-IV-V, and it
 * is started at beat -8 so the two lead-in bars are its bars 0-1. Every degree
 * below is a D-major scale degree, so the melody cannot leave the key, and
 * every downbeat lands on a tone of the chord the track is actually playing at
 * that bar (see `chordDegForBar`). `chart.test.mjs` asserts both.
 *
 * Pure data + pure functions: no THREE, no DOM, no clock. The test suite and
 * the verification script both import this directly.
 */

import { note, SCALES, degree } from '../../audio/theory.js';

// ---------------------------------------------------------------- constants

export const BPM = 118;
export const BEATS_PER_BAR = 4;

/** World units the bouncer travels per beat. Time axis == X axis. */
export const UNITS_PER_BEAT = 3.1;

/** Height mapping. Scale degree -> world Y of the platform's top surface. */
export const BASE_Y = 1.15;
export const Y_PER_DEGREE = 0.40;
export const MIN_DEGREE = 3;

/** Pitch mapping. Track root is D2; the melody sings two octaves above it. */
export const ROOT_MIDI = note('D', 2);
export const SCALE = SCALES.major;
export const MELODY_OCTAVE = 24;

/** Where the water is. Falling short means getting wet, never dying. */
export const WATER_Y = -3.4;

/** Beats a charge ramp is rolled along before the release. */
export const CHARGE_BEATS = 2;
/** Beats the button must have been held for the release to count as charged. */
export const CHARGE_MIN_HOLD_BEATS = 1.35;
/** Upward rake of a charge ramp, radians. */
export const RAMP_TILT = 0.092;

/** Slab sizes. An eighth-note run of these reads as a xylophone. */
export const TAP_WIDTH = 1.42;
export const SLAB_DEPTH = 2.25;
export const SLAB_THICK = 0.34;
export const FINISH_WIDTH = 11.0;

export const LEAD_IN_BARS = 2;
export const RUNWAY_BEAT = -LEAD_IN_BARS * BEATS_PER_BAR;   // -8
export const RUNWAY_ROLL = 7;                                // depart at beat -1

// ------------------------------------------------------------------ mapping

export const degToY = (deg) => BASE_Y + (deg - MIN_DEGREE) * Y_PER_DEGREE;
/** Inverse of `degToY`. The tune is readable off the level geometry alone. */
export const yToDeg = (y) => Math.round((y - BASE_Y) / Y_PER_DEGREE) + MIN_DEGREE;
export const midiForDeg = (deg) => degree(ROOT_MIDI, SCALE, deg) + MELODY_OCTAVE;
export const xAtBeat = (beat) => beat * UNITS_PER_BEAT;

/**
 * Hue per scale degree, boomwhacker-style: one colour per step of the scale,
 * lifted toward white an octave at a time. Pitch is therefore encoded twice —
 * height AND colour — which is what makes the layout readable at a glance and
 * still readable to a player who cannot separate those seven hues.
 */
export const COLOR_FOR_DEGREE = [
  0xff5d73, // D  red
  0xff9a3c, // E  orange
  0xffd93d, // F# yellow
  0x9ee87a, // G  green
  0x4dd6ff, // A  cyan
  0x6f8bff, // B  blue
  0xc08bff, // C# violet
];

/** sRGB hex lightened toward white by `t`. */
export function lighten(hex, t) {
  const r = (hex >> 16) & 255, g = (hex >> 8) & 255, b = hex & 255;
  return (Math.round(r + (255 - r) * t) << 16)
    | (Math.round(g + (255 - g) * t) << 8)
    | Math.round(b + (255 - b) * t);
}

export function colorForDeg(deg) {
  const pc = ((deg % 7) + 7) % 7;
  const oct = Math.floor(deg / 7);
  return lighten(COLOR_FOR_DEGREE[pc], Math.max(0, Math.min(0.42, (oct - 1) * 0.19)));
}

// ------------------------------------------------------------------ harmony

/** The backing track's progression, and where our bar 0 sits inside it. */
export const PROG = [0, 0, 5, 5, 3, 3, 4, 4, 0, 0, 5, 5, 3, 4, 0, 4];
export const TRACK_BAR_OFFSET = LEAD_IN_BARS;

/** Scale degree of the chord root the track is on during game bar `bar`. */
export function chordDegForBar(bar) {
  const i = ((bar + TRACK_BAR_OFFSET) % PROG.length + PROG.length) % PROG.length;
  return PROG[i];
}

/** Pitch classes (0..6, as scale degrees) of that bar's diatonic triad. */
export function chordToneClasses(bar) {
  const root = chordDegForBar(bar);
  return [root, root + 2, root + 4].map((d) => ((d % 7) + 7) % 7);
}

// -------------------------------------------------------------------- score
//
// [beat, degree, kind]
//   't' tap     — press down on the beat
//   'c' charge  — press down on the beat, HOLD two beats up the ramp, release
//                 on the downbeat to clear the chasm
//   'F' finale  — the same, but the chasm is four beats wide
//   'X' finish  — the last landing, worth double
//
// Sections: teach (bars 0-7) even quarters + the first charge; play (bars 8-23)
// syncopation, offbeats and three more charges; escalate (bars 24-29) eighth
// runs at a sprint; finale (bars 29-31) one enormous charged jump.

export const SCORE_LINE = [
  // ---- TEACH: even spacing, one platform per beat -------------------------
  [0, 5, 't'], [1, 7, 't'], [2, 9, 't'], [3, 7, 't'],            // bar 0  Bm
  [4, 12, 't'], [5, 9, 't'], [6, 7, 't'], [7, 5, 't'],           // bar 1  Bm
  [8, 3, 't'], [9, 5, 't'], [10, 7, 't'], [11, 10, 't'],         // bar 2  G
  [12, 12, 't'], [13, 10, 't'], [14, 7, 't'], [15, 5, 't'],      // bar 3  G
  [16, 11, 'c'],                                                 // bar 4  A  CHARGE
  [20, 8, 't'], [21, 6, 't'], [22, 4, 't'], [23, 6, 't'],        // bar 5  A
  [24, 7, 't'], [25, 9, 't'], [26, 11, 't'], [27, 9, 't'],       // bar 6  D
  [28, 11, 't'], [29, 9, 't'], [30, 7, 't'], [31, 4, 't'],       // bar 7  D

  // ---- PLAY: syncopation, offbeats, more charges --------------------------
  [32, 12, 't'], [32.5, 11, 't'], [33.5, 9, 't'], [35, 7, 't'],  // bar 8  Bm
  [36, 5, 't'], [37.5, 7, 't'], [38.5, 9, 't'], [39.5, 11, 't'], // bar 9  Bm
  [40, 12, 't'], [41, 10, 't'], [42.5, 12, 't'], [43, 10, 't'],  // bar 10 G
  [44, 11, 'c'],                                                 // bar 11 A  CHARGE
  [48, 11, 't'], [49, 9, 't'], [50, 7, 't'], [51, 9, 't'],       // bar 12 D
  [52, 11, 't'], [52.5, 13, 't'], [53.5, 11, 't'], [55, 8, 't'], // bar 13 A
  [56, 7, 't'], [57, 9, 't'], [58, 11, 't'], [59, 12, 't'],      // bar 14 D
  [60, 9, 'c'],                                                  // bar 15 D  CHARGE
  [64, 12, 't'], [65, 9, 't'], [66, 7, 't'], [67, 5, 't'],       // bar 16 Bm
  [68, 7, 't'], [68.5, 9, 't'], [69.5, 12, 't'], [71, 9, 't'],   // bar 17 Bm
  [72, 10, 't'], [73, 12, 't'], [74, 10, 't'], [74.5, 7, 't'], [75.5, 5, 't'], // bar 18 G
  [76, 3, 't'], [77, 5, 't'], [78, 7, 't'], [79, 10, 't'],       // bar 19 G
  [80, 11, 't'], [80.5, 13, 't'], [81.5, 11, 't'], [82.5, 8, 't'], [83.5, 6, 't'], // bar 20 A
  [84, 11, 'c'],                                                 // bar 21 A  CHARGE
  [88, 14, 't'], [89, 12, 't'], [90, 11, 't'], [91, 9, 't'],     // bar 22 D
  [92, 7, 't'], [93, 9, 't'], [94, 11, 't'], [95, 12, 't'],      // bar 23 D

  // ---- ESCALATE: eighths, at a sprint -------------------------------------
  [96, 12, 't'], [96.5, 11, 't'], [97, 9, 't'], [97.5, 7, 't'],
  [98, 9, 't'], [99, 12, 't'],                                   // bar 24 Bm
  [100, 14, 't'], [100.5, 12, 't'], [101, 11, 't'], [101.5, 9, 't'],
  [102, 7, 't'], [102.5, 9, 't'], [103, 11, 't'], [103.5, 12, 't'], // bar 25 Bm
  [104, 14, 't'], [104.5, 12, 't'], [105, 10, 't'], [105.5, 12, 't'],
  [106, 14, 't'], [106.5, 12, 't'], [107, 10, 't'], [107.5, 7, 't'], // bar 26 G
  [108, 11, 't'], [108.5, 13, 't'], [109, 11, 't'], [109.5, 8, 't'],
  [110, 6, 't'], [110.5, 8, 't'], [111, 11, 't'], [111.5, 13, 't'], // bar 27 A
  [112, 14, 't'], [112.5, 11, 't'], [113, 9, 't'], [113.5, 11, 't'],
  [114, 12, 't'], [114.5, 14, 't'], [115, 12, 't'], [115.5, 11, 't'], // bar 28 D
  [116, 13, 't'], [116.5, 11, 't'], [117, 8, 't'], [117.5, 6, 't'], // bar 29 A

  // ---- FINALE -------------------------------------------------------------
  [118, 11, 'F'],   // two-beat ramp, release on beat 120, four beats of air
  [124, 7, 'X'],    // the landing. Worth double.
];

/** Beat the round is over (the finish platform is ridden for four bars). */
export const END_BEAT = 132;

// ------------------------------------------------------------------- build

/**
 * Expand `SCORE_LINE` into platforms with geometry, plus the judged note list.
 *
 * @returns {{platforms: object[], notes: object[], finishIndex: number,
 *            endBeat: number, maxPoints: number}}
 */
export function buildChart() {
  const rows = SCORE_LINE.slice().sort((a, b) => a[0] - b[0]);

  /** @type {object[]} */
  const platforms = [{
    index: 0, beat: RUNWAY_BEAT, deg: rows[0][1], kind: 'R', roll: RUNWAY_ROLL, scored: false,
  }];
  for (let i = 0; i < rows.length; i++) {
    const [beat, deg, kind] = rows[i];
    platforms.push({
      index: i + 1,
      beat,
      deg,
      kind,
      roll: kind === 'c' || kind === 'F' ? CHARGE_BEATS : kind === 'X' ? 8 : 0,
      scored: true,
    });
  }

  for (let i = 0; i < platforms.length; i++) {
    const p = platforms[i];
    const next = platforms[i + 1] || null;
    p.midi = midiForDeg(p.deg);
    p.y = degToY(p.deg);
    p.color = colorForDeg(p.deg);
    p.charge = p.kind === 'c' || p.kind === 'F';
    p.tilt = p.charge ? RAMP_TILT : 0;
    p.departBeat = p.beat + p.roll;
    p.contactX = xAtBeat(p.beat);
    p.departX = xAtBeat(p.departBeat);
    p.nextBeat = next ? next.beat : p.beat + p.roll;
    p.flightBeats = next ? p.nextBeat - p.departBeat : 0;
    p.weight = p.kind === 'X' || p.kind === 'F' ? 2 : 1;

    // Top surface: `anchor` is its left end, `width` runs up the rake.
    if (p.kind === 'R') {
      p.anchorX = p.contactX;
      p.width = p.roll * UNITS_PER_BEAT + 1.0;
    } else if (p.charge) {
      p.anchorX = p.contactX - 0.6;
      p.width = p.roll * UNITS_PER_BEAT + 1.5;
    } else if (p.kind === 'X') {
      p.anchorX = p.contactX - 1.6;
      p.width = FINISH_WIDTH;
    } else {
      p.anchorX = p.contactX - TAP_WIDTH / 2;
      p.width = TAP_WIDTH;
    }
    p.anchorY = p.y;
    p.slope = Math.tan(p.tilt);
    p.endX = p.anchorX + Math.cos(p.tilt) * p.width;
    p.departY = p.y + p.slope * (p.departX - p.contactX);
    p.centerX = p.anchorX + Math.cos(p.tilt) * p.width * 0.5;
    p.centerY = p.anchorY + Math.sin(p.tilt) * p.width * 0.5;
  }

  // ----- judged notes -------------------------------------------------------
  // Contacts are DOWN presses; charge releases are UP presses. Two distinct
  // actions so a press can never be claimed by a release note and vice versa.
  const notes = [];
  for (const p of platforms) {
    if (!p.scored) continue;
    notes.push({
      beat: p.beat, action: 'a', kind: 'contact', platform: p.index, weight: p.weight,
    });
    if (p.charge) {
      notes.push({
        beat: p.departBeat, action: 'a-up', kind: 'release', platform: p.index, weight: p.weight,
      });
    }
  }
  notes.sort((a, b) => a.beat - b.beat);

  let maxPoints = 0;
  for (const n of notes) maxPoints += 1000 * n.weight;

  const finishIndex = platforms.findIndex((p) => p.kind === 'X');
  return { platforms, notes, finishIndex, endBeat: END_BEAT, maxPoints };
}

/** The tune, as MIDI, in the order a clean run performs it. */
export function melodyMidi() {
  return buildChart().platforms.filter((p) => p.scored).map((p) => p.midi);
}
