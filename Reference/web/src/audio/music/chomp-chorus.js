/**
 * "SNACK ATTACK SHUFFLE" — Chomp Chorus, 140bpm.  [original composition]
 *
 * Brief: fast, funky, four-on-the-floor.
 *
 * Four-on-the-floor is the easy part; the funk is in the sixteenth-note bass
 * and the clav stabs, which are deliberately written to land in the holes the
 * kick leaves. The open hat sits on every off-beat eighth so the pulse reads
 * as double-time without the drums actually playing double-time — the trick
 * that lets a 140bpm track still feel danceable rather than frantic.
 *
 * Harmony: A dorian, a two-bar Am7 / D9 vamp with a lift to G for bars 13-16.
 * Dorian's major sixth is what stops a minor vamp sounding like a funeral.
 */

import { note } from '../theory.js';
import { defaultRender } from '../player.js';

const RIFF = [
  [ // A — over Am7. Short, chewy, ends with a question mark
    [0, 7, 0.25, 1], [0.5, 9, 0.25, 0.8], [0.75, 10, 0.25, 0.85], [1, 11, 0.75, 1],
    [2.25, 10, 0.25, 0.8], [2.5, 9, 0.25, 0.85], [3, 7, 1, 0.95],
    [5, 11, 0.25, 0.9], [5.5, 12, 0.25, 0.9], [6, 14, 1.25, 1],
  ],
  [ // A' — over D9, the answer, one step higher
    [0, 9, 0.25, 1], [0.5, 11, 0.25, 0.8], [0.75, 12, 0.25, 0.85], [1, 14, 0.75, 1],
    [2.25, 12, 0.25, 0.8], [2.5, 11, 0.25, 0.85], [3, 9, 1, 0.95],
    [5, 7, 0.5, 0.9], [5.75, 9, 0.25, 0.85], [6, 11, 1.5, 1],
  ],
  [ // B — the chorus, long notes over the vamp
    [0, 14, 1.5, 1], [1.75, 12, 0.25, 0.85], [2, 11, 1.5, 0.95],
    [4, 12, 0.5, 0.95], [4.5, 11, 0.5, 0.9], [5, 9, 0.5, 0.9], [5.5, 11, 2, 1],
  ],
  [ // B' — tumbling exit into the loop point
    [0, 16, 0.5, 1], [0.5, 14, 0.5, 0.9], [1, 12, 0.5, 0.9], [1.5, 11, 1, 0.95],
    [3, 9, 0.5, 0.85], [3.5, 7, 0.5, 0.85],
    [4, 11, 0.25, 0.9], [4.5, 12, 0.25, 0.9], [5, 14, 0.25, 0.95], [5.5, 16, 2, 1],
  ],
];

export default {
  id: 'chomp-chorus',
  title: 'Snack Attack Shuffle',
  bpm: 140,
  beatsPerBar: 4,
  bars: 16,
  loop: true,
  swing: 0.035,
  root: note('A', 2),
  scale: 'dorian',
  shape: [0, 2, 4, 6],
  prog: [0, 0, 3, 3, 0, 0, 3, 3, 0, 0, 3, 3, 6, 6, 3, 4],
  mix: 0.92,
  render: defaultRender,

  layers: [
    {
      id: 'kick', voice: 'kick', min: 0, gain: 0.9,
      grid: ['9...9...9...9...', '9...9...9...9...', '9...9...9...9...', '9...9...9...9.7.'],
      opts: { decay: 0.18, click: 0.6 },
    },
    {
      id: 'clap', voice: 'clap', min: 0, gain: 0.46,
      grid: '....9.......9...', opts: { rev: 0.26 },
    },
    {
      id: 'openhat', voice: 'hat', min: 0, gain: 0.24,
      grid: '..x...x...x...x.', opts: { open: 0.22, pan: 0.22 },
    },
    {
      id: 'closedhat', voice: 'hat', min: 1, gain: 0.16,
      grid: 'x.x.x.x.x.x.x.x.', opts: { tone: 1.15, pan: -0.24 },
    },
    {
      id: 'shaker', voice: 'shaker', min: 2, gain: 0.13,
      grid: 'sxsxsxsxsxsxsxsx', opts: { pan: -0.34 },
    },
    {
      id: 'snareOff', voice: 'snare', min: 2, gain: 0.24,
      grid: '..........s...s.', opts: { decay: 0.07, snap: 0.6, rev: 0.1 },
    },
    {
      id: 'crash', voice: 'crash', min: 0, gain: 0.3,
      bars: 'x...x...x...x...', grid: 'x...............',
      opts: { decay: 1.15, rev: 0.4 },
    },
    {
      id: 'fill', voice: 'tom', min: 0, gain: 0.5, bars: [7, 15],
      grid: '........9.7.9799', opts: { freq: 165, decay: 0.18, rev: 0.16 },
    },

    // sixteenth-note funk bass, written around the kick
    {
      id: 'bass', voice: 'bass', min: 0, gain: 0.42, rel: 'chord', span: 1,
      seq: [
        [[0, 0, 0.2, 1], [0.75, 0, 0.15, 0.6], [1, 7, 0.2, 0.8], [1.5, 0, 0.15, 0.7],
          [2, 4, 0.2, 0.85], [2.75, 2, 0.2, 0.7], [3, 0, 0.2, 0.9], [3.5, -1, 0.25, 0.65]],
        [[0, 0, 0.2, 1], [0.5, 0, 0.15, 0.65], [0.75, 4, 0.15, 0.7], [1, 7, 0.25, 0.85],
          [2, 0, 0.2, 0.9], [2.5, 2, 0.15, 0.7], [2.75, 4, 0.15, 0.75], [3.25, 7, 0.4, 0.85]],
      ],
      opts: { cutoff: 4.6, res: 9, sub: 0.5, drive: 0.5 },
    },

    // clav: all sixteenth syncopation, never on a beat except 1
    {
      id: 'clav', voice: 'stab', min: 1, gain: 0.16, octave: 1,
      rhythm: [
        [[0, 0.14, 0.95], [0.75, 0.12, 0.7], [1.5, 0.14, 0.85], [2.25, 0.12, 0.7], [3.25, 0.14, 0.8]],
        [[0.25, 0.12, 0.8], [1, 0.14, 0.9], [1.75, 0.12, 0.7], [2.5, 0.14, 0.85], [3.5, 0.16, 0.9]],
      ],
      opts: { wah: 1.8, dly: 0.18, rev: 0.16, pan: 0.26 },
    },
    {
      id: 'pad', voice: 'pad', min: 2, gain: 0.11, octave: 1,
      rhythm: [[0, 3.9, 0.85]], opts: { rev: 0.55, cutoff: 1700 },
    },

    // the riff
    {
      id: 'riff', voice: 'lead', min: 2, gain: 0.2, octave: 2, span: 2, seq: RIFF,
      opts: { detune: 11, cutoff: 4.5, res: 3.5, rev: 0.18, dly: 0.24, pan: -0.1 },
    },
    {
      id: 'horns', voice: 'brass', min: 3, gain: 0.14, octave: 1, span: 2, seq: RIFF,
      degShift: -2, opts: { bite: 0.9, rev: 0.24, pan: 0.2 },
    },
    {
      id: 'bighit', voice: 'orchHit', min: 0, gain: 0.24, octave: 1,
      bars: [0, 12], rhythm: [[0, 0.4, 1]], opts: { rev: 0.5 },
    },
  ],
};
