/**
 * Music theory primitives.  [audio agent]
 *
 * Everything the synth and the SFX kit need to agree on what "in key" means.
 * The point of this file is that a PERFECT verdict can be *harmonised* against
 * whatever chord the track is currently sitting on, instead of being a beep
 * that happens to be loud. Consonance is free polish: it costs one lookup.
 *
 * Degrees are SCALE degrees, not semitones. Degree 0 is the tonic, degree 7 is
 * the tonic an octave up, degree -1 is the leading tone below. That means a
 * melody written as degrees is diatonic by construction — you cannot
 * accidentally write a wrong note — and the same melody data transposes to any
 * mode by swapping one array.
 */

export const SEMI = {
  C: 0, 'C#': 1, Db: 1, D: 2, 'D#': 3, Eb: 3, E: 4, F: 5, 'F#': 6,
  Gb: 6, G: 7, 'G#': 8, Ab: 8, A: 9, 'A#': 10, Bb: 10, B: 11,
};

export const SCALES = {
  major: [0, 2, 4, 5, 7, 9, 11],
  minor: [0, 2, 3, 5, 7, 8, 10],
  dorian: [0, 2, 3, 5, 7, 9, 10],
  phrygian: [0, 1, 3, 5, 7, 8, 10],
  lydian: [0, 2, 4, 6, 7, 9, 11],
  mixolydian: [0, 2, 4, 5, 7, 9, 10],
  harmonicMinor: [0, 2, 3, 5, 7, 8, 11],
  phrygianDominant: [0, 1, 4, 5, 7, 8, 10],
  majorPent: [0, 2, 4, 7, 9],
  minorPent: [0, 3, 5, 7, 10],
  blues: [0, 3, 5, 6, 7, 10],
};

/** MIDI note number for a name + octave. `note('F', 2)` -> 41. */
export function note(name, octave) {
  return 12 * (octave + 1) + SEMI[name];
}

/** MIDI -> Hz. */
export function mtof(m) {
  return 440 * Math.pow(2, (m - 69) / 12);
}

/**
 * Scale degree -> MIDI. Degrees outside 0..len-1 wrap by octave, so degree
 * math never leaves the key: `degree(root, major, 9)` is the 3rd, an octave up.
 */
export function degree(rootMidi, scale, deg) {
  const n = scale.length;
  const oct = Math.floor(deg / n);
  return rootMidi + oct * 12 + scale[deg - oct * n];
}

/**
 * Diatonic chord built by stacking scale thirds from `chordDeg`.
 * shape [0,2,4] = triad, [0,2,4,6] = seventh, [0,2,4,6,8] = ninth. Quality
 * (major/minor/diminished) falls out of the scale automatically, which is
 * exactly what you want when the same progression data has to work in
 * mixolydian and in harmonic minor.
 */
export function chord(rootMidi, scale, chordDeg, shape = [0, 2, 4], octaves = 0) {
  const out = new Array(shape.length);
  for (let i = 0; i < shape.length; i++) {
    out[i] = degree(rootMidi, scale, chordDeg + shape[i]) + octaves * 12;
  }
  return out;
}

/**
 * Pull an arbitrary MIDI note onto the nearest chord tone (any octave).
 * This is what makes verdict SFX land *inside* the harmony: pick a bright
 * register, snap it to the chord, done.
 */
export function snapToChord(midi, chordMidis) {
  let best = midi;
  let bestD = Infinity;
  for (const c of chordMidis) {
    // consider the chord tone in every octave near `midi`
    const k = Math.round((midi - c) / 12);
    for (let o = k - 1; o <= k + 1; o++) {
      const cand = c + o * 12;
      const d = Math.abs(cand - midi);
      if (d < bestD) { bestD = d; best = cand; }
    }
  }
  return best;
}

/** Ascending chord tones starting at/above `fromMidi`. Used for arpeggio SFX. */
export function arpUp(chordMidis, fromMidi, count) {
  const sorted = chordMidis.slice().sort((a, b) => a - b);
  const out = [];
  let oct = Math.ceil((fromMidi - sorted[0]) / 12);
  let i = 0;
  let guard = 0;
  while (out.length < count && guard++ < 64) {
    const m = sorted[i] + oct * 12;
    if (m >= fromMidi - 0.5) out.push(m);
    i++;
    if (i >= sorted.length) { i = 0; oct++; }
  }
  return out;
}

/** Cents -> ratio, for detune stacks. */
export function cents(c) { return Math.pow(2, c / 1200); }
