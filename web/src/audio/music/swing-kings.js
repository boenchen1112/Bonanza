/**
 * "SEVENTH INNING SWAGGER" — Swing Kings, 124bpm.  [original composition]
 *
 * Brief: swaggering, brassy, big-hit stadium energy.
 *
 * Harmony: F mixolydian. The flat seventh is doing all the work — it turns the
 * V chord minor (Cm) and puts an Eb in the melody, which is the difference
 * between "marching band" and "swagger". A light shuffle (swing 0.11) sits the
 * eighths just behind the grid so the groove leans back.
 *
 * Form (16 bars, an 8-bar melody stated twice):
 *   bars  1-2  F      hook, call
 *   bars  3-4  Dm     answer
 *   bars  5-6  Bb     lift
 *   bars  7-8  Cm     descent + tom fill  <- telegraphs the repeat
 *   bars  9-16 as above, turnaround harmony (Bb Cm Bb Cm) under bars 13-16
 *
 * You can hear bar 1 without counting: crash + orchestral hit on 1, 5, 9, 13,
 * a tom fill on the last beat of 4, 8, 12, 16, and the bass slams the root on
 * every downbeat.
 */

import { note } from '../theory.js';
import { defaultRender } from '../player.js';

/** Descending tom fill — pitch falls with the 16th index inside the bar. */
function tomFill(V, ev, t, m) {
  const inBar = ev.beat - Math.floor(ev.beat / 4) * 4;
  const i = Math.round(inBar * 4);           // 0..15
  const k = Math.max(0, Math.min(4, i - 11));
  const freq = [210, 186, 162, 138, 116][k];
  V.tom(t, { freq, gain: 0.66 * ev.vel, decay: 0.26, pan: (k - 2) * 0.22, rev: 0.2 });
}

// --- the hook. 2-bar phrases; degrees are F-mixolydian scale degrees. -------
const P1 = [ // over F — the call
  [0, 11, 0.75, 1.0], [0.75, 10, 0.25, 0.8], [1, 7, 1.0, 0.95],
  [2.5, 9, 0.25, 0.75], [2.75, 10, 0.25, 0.8], [3, 11, 1.0, 0.95],
  [5, 12, 0.5, 0.9], [5.5, 11, 0.5, 0.85], [6, 9, 1.75, 0.9],
];
const P2 = [ // over Dm — the answer
  [0, 12, 0.75, 1.0], [0.75, 11, 0.25, 0.8], [1, 9, 1.0, 0.95],
  [2.5, 7, 0.5, 0.8], [3, 9, 0.5, 0.85], [3.5, 10, 0.5, 0.85],
  [4, 11, 1.5, 0.95], [6, 9, 0.5, 0.8], [6.5, 7, 1.5, 0.9],
];
const P3 = [ // over Bb — the lift, peaks on F5
  [0, 10, 0.5, 1.0], [0.5, 11, 0.5, 0.9], [1, 12, 1.0, 0.95],
  [2.5, 11, 0.25, 0.8], [2.75, 10, 0.25, 0.8], [3, 9, 1.0, 0.9],
  [4.5, 12, 0.5, 0.9], [5, 13, 0.5, 0.95], [5.5, 14, 2.0, 1.0],
];
const P4 = [ // over Cm — comes home, leaves a hole for the fill
  [0, 13, 0.5, 0.95], [0.5, 12, 0.5, 0.9], [1, 11, 0.5, 0.9], [1.5, 9, 1.0, 0.85],
  [3, 7, 1.0, 0.85], [4.5, 10, 0.25, 0.8], [4.75, 11, 0.25, 0.8],
  [5, 12, 0.5, 0.9], [5.5, 11, 0.5, 0.85], [6, 7, 1.75, 0.95],
];
const HOOK = [P1, P2, P3, P4];

export default {
  id: 'swing-kings',
  title: 'Seventh Inning Swagger',
  bpm: 124,
  beatsPerBar: 4,
  bars: 16,
  loop: true,
  swing: 0.11,
  root: note('F', 2),
  scale: 'mixolydian',
  shape: [0, 2, 4, 6],
  prog: [0, 0, 5, 5, 3, 3, 4, 4, 0, 0, 5, 5, 3, 4, 3, 4],
  mix: 0.95,
  render: defaultRender,

  layers: [
    // ---------------------------------------------------------------- drums
    {
      id: 'kick', voice: 'kick', min: 0, gain: 0.95,
      grid: ['9.....7.9.....6.', '9.....7.9.....6.', '9.....7.9.....6.', '9.....7.9...7.6.'],
      opts: { decay: 0.21, click: 0.55 },
    },
    {
      id: 'snare', voice: 'snare', min: 0, gain: 0.6,
      grid: ['....9.......9...', '....9.......9...', '....9.......9...', '....9.......9.7.'],
      opts: { decay: 0.17, snap: 1.05, rev: 0.2 },
    },
    {
      id: 'ghosts', voice: 'snare', min: 2, gain: 0.26,
      grid: '..s...s.s.s..s..',
      opts: { decay: 0.07, snap: 0.55, rev: 0.08 },
    },
    {
      id: 'hats', voice: 'hat', min: 0, gain: 0.26,
      grid: '7.5.7.5.7.5.7.6.',
      opts: { pan: 0.18 },
    },
    {
      id: 'ride', voice: 'ride', min: 2, gain: 0.2,
      grid: '9.5.7.5.9.5.7.5.',
      opts: { bell: 0.35, pan: 0.3 },
    },
    {
      id: 'tambourine', voice: 'shaker', min: 3, gain: 0.2,
      grid: '..x...x...x...x.', opts: { pan: -0.35 },
    },
    {
      id: 'crash', voice: 'crash', min: 0, gain: 0.34,
      bars: 'x...x...x...x...', grid: 'x...............',
      opts: { decay: 1.5, rev: 0.45 },
    },
    {
      id: 'tomfill', voice: 'tom', min: 0, gain: 1,
      bars: '...x...x...x...x', grid: '............9797',
      render: tomFill,
    },

    // ---------------------------------------------------------------- bass
    {
      id: 'bass', voice: 'bass', min: 0, gain: 0.44, rel: 'chord', span: 1,
      seq: [
        [[0, 0, 0.7, 1], [1.5, 4, 0.35, 0.7], [2, 0, 0.6, 0.9], [3, 2, 0.35, 0.7], [3.5, -1, 0.4, 0.65]],
        [[0, 0, 0.7, 1], [1, 7, 0.35, 0.7], [2, 4, 0.5, 0.85], [3, 2, 0.4, 0.7], [3.5, 0, 0.4, 0.75]],
      ],
      opts: { cutoff: 3.4, res: 7, sub: 0.65, drive: 0.4 },
    },

    // ------------------------------------------------------------ harmony
    {
      id: 'organ', voice: 'organ', min: 1, gain: 0.11, octave: 1,
      rhythm: [[0, 3.7, 0.9]],
      opts: { rev: 0.34, drawbars: [1, 0.5, 0.42, 0.18, 0.1] },
    },
    {
      id: 'stabs', voice: 'stab', min: 1, gain: 0.17, octave: 1,
      rhythm: [
        [[1.5, 0.22, 0.9], [2.5, 0.2, 0.72]],
        [[1.5, 0.22, 0.9], [2.5, 0.2, 0.72]],
        [[0.5, 0.18, 0.7], [1.5, 0.22, 0.9], [3, 0.3, 0.8]],
        [[1.5, 0.22, 0.9], [2.5, 0.2, 0.72], [3.5, 0.25, 0.85]],
      ],
      opts: { wah: 1.5, dly: 0.14, rev: 0.2 },
    },

    // -------------------------------------------------------------- melody
    {
      id: 'lead', voice: 'brass', min: 2, gain: 0.21, octave: 1, span: 2, seq: HOOK,
      opts: { bite: 1.1, rev: 0.24, dly: 0.12, pan: -0.08 },
    },
    {
      id: 'harmony', voice: 'brass', min: 3, gain: 0.13, octave: 1, span: 2, seq: HOOK,
      degShift: -2,
      opts: { bite: 0.7, rev: 0.24, pan: 0.22 },
    },

    // --------------------------------------------------------- section hits
    {
      id: 'bighit', voice: 'orchHit', min: 0, gain: 0.26, octave: 1,
      bars: [0, 8], rhythm: [[0, 0.5, 1]],
      opts: { rev: 0.55 },
    },
  ],
};
