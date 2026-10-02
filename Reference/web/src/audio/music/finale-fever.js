/**
 * "LAST ONE STANDING" — Finale Fever, 150bpm, ramps to ~1.35x.
 * [original composition]
 *
 * Brief: escalating, tense, must survive a tempo ramp.
 *
 * Surviving the ramp is a *composition* constraint, not an engine one. At
 * 202bpm a sixteenth is 74ms, which is under the ear's ability to separate
 * two attacks on the same timbre — so nothing here is written in continuous
 * sixteenths. The engine is eighth-note based, sixteenths appear only as
 * two-note flams and fills, and the density INCREASES with intensity rather
 * than with tempo. The result speeds up cleanly instead of turning to mush.
 *
 * Harmony: E harmonic minor. The raised seventh gives a real B major dominant
 * (that D# is the tension), and the augmented second between C and D# is the
 * most anxious interval available inside a diatonic scale.
 *
 * Escalation is built into the 16 bars: the loop starts on a bare pedal, the
 * toms enter, the lead climbs a scale degree each 4-bar phrase, and bars 15-16
 * are a riser + snare accelerando that telegraphs the next lap.
 */

import { note } from '../theory.js';
import { defaultRender } from '../player.js';

/** Riser: one long noise sweep per bar, tuned to the bar length. */
function riseFx(V, ev, t, m) {
  V.riser(t, {
    dur: m.spb * 3.9, gain: 0.14 * ev.vel, from: 320, to: 7200, rev: 0.35,
  });
}

/** Taiko: two drums, low on the downbeat, high on the answer. */
function taiko(V, ev, t) {
  const inBar = ev.beat - Math.floor(ev.beat / 4) * 4;
  const low = inBar < 2;
  V.tom(t, {
    freq: low ? 96 : 132, gain: 0.8 * ev.vel, decay: low ? 0.4 : 0.3,
    pan: low ? -0.18 : 0.18, rev: 0.28,
  });
}

const CLIMB = [
  [ // phrase 1 — the motif: up the chord, back down, sit on the fifth
    [0, 7, 0.5, 0.9], [0.5, 9, 0.5, 0.85], [1, 11, 0.5, 0.9], [1.5, 9, 0.5, 0.85],
    [2, 11, 1.5, 1],
    [4, 7, 0.5, 0.9], [4.5, 9, 0.5, 0.85], [5, 11, 0.5, 0.9], [5.5, 12, 0.5, 0.9],
    [6, 11, 1.75, 1],
  ],
  [ // phrase 2 — same motif a third up
    [0, 9, 0.5, 0.9], [0.5, 11, 0.5, 0.85], [1, 13, 0.5, 0.9], [1.5, 11, 0.5, 0.85],
    [2, 13, 1.5, 1],
    [4, 9, 0.5, 0.9], [4.5, 11, 0.5, 0.85], [5, 13, 0.5, 0.9], [5.5, 14, 0.5, 0.9],
    [6, 13, 1.75, 1],
  ],
  [ // phrase 3 — off the beat, higher, losing its footing
    [0.5, 11, 0.5, 0.95], [1, 13, 0.5, 0.9], [1.5, 14, 0.5, 0.95], [2, 16, 1.5, 1],
    [4.5, 13, 0.5, 0.9], [5, 14, 0.5, 0.9], [5.5, 16, 0.5, 0.95], [6, 18, 1.75, 1],
  ],
  [ // phrase 4 — the descent onto the dominant, then a held D# screaming for E
    [0, 18, 0.5, 1], [0.5, 16, 0.5, 0.95], [1, 14, 0.5, 0.9], [1.5, 13, 0.5, 0.9],
    [2, 11, 0.5, 0.9], [2.5, 9, 0.5, 0.9], [3, 13, 1, 1],
    [4, 14, 0.5, 1], [4.5, 13, 0.5, 0.95], [5, 11, 0.5, 0.9],
    [5.5, 13, 2.4, 1],
  ],
];

export default {
  id: 'finale-fever',
  title: 'Last One Standing',
  bpm: 150,
  beatsPerBar: 4,
  bars: 16,
  loop: true,
  swing: 0,
  root: note('E', 2),
  scale: 'harmonicMinor',
  shape: [0, 2, 4],
  prog: [0, 0, 0, 0, 5, 5, 4, 4, 0, 0, 3, 3, 5, 5, 4, 4],
  mix: 0.95,
  render: defaultRender,

  layers: [
    // Eighth-note engine: dense enough to drive, sparse enough to survive 202bpm
    {
      id: 'kick', voice: 'kick', min: 0, gain: 0.95,
      grid: ['9...9...9...9...', '9...9...9...9...', '9...9...9...9...', '9...9...9...9.7.'],
      opts: { decay: 0.17, tune: 1.02, click: 0.65 },
    },
    {
      id: 'snare', voice: 'snare', min: 0, gain: 0.6,
      grid: ['....9.......9...', '....9.......9...', '....9.......9...', '....9.....7.9.7.'],
      opts: { decay: 0.13, snap: 1.1, tone: 1.08, rev: 0.2 },
    },
    {
      id: 'taiko', voice: 'tom', min: 1, gain: 1,
      grid: ['x.......x.......', 'x.....x.x.......', 'x.......x.......', 'x.....x.x...x...'],
      render: taiko,
    },
    {
      id: 'hats', voice: 'hat', min: 1, gain: 0.2,
      grid: 'x.x.x.x.x.x.x.x.', opts: { pan: 0.24 },
    },
    {
      id: 'ride', voice: 'ride', min: 3, gain: 0.16,
      grid: '9...7...9...7...', opts: { bell: 0.5, pan: 0.3 },
    },
    {
      id: 'crash', voice: 'crash', min: 0, gain: 0.36,
      bars: 'x...x...x...x...', grid: 'x...............',
      opts: { decay: 1.4, rev: 0.45 },
    },
    // the accelerando that says "the loop is about to come round again"
    {
      id: 'rollout', voice: 'snare', min: 0, gain: 0.44, bars: [15],
      grid: '4.5.6.7.88999999', opts: { decay: 0.06, snap: 0.85, rev: 0.14 },
    },
    {
      id: 'riser', voice: 'riser', min: 1, gain: 1, bars: [7, 15],
      notes: [[0, 0, 4, 1]], render: riseFx,
    },

    // pedal bass: relentless eighths on the chord root
    {
      id: 'bass', voice: 'bass', min: 0, gain: 0.44, rel: 'chord', span: 1,
      seq: [
        [[0, 0, 0.45, 1], [0.5, 0, 0.45, 0.7], [1, 0, 0.45, 0.85], [1.5, 0, 0.45, 0.7],
          [2, 0, 0.45, 0.95], [2.5, 0, 0.45, 0.7], [3, 0, 0.45, 0.85], [3.5, 4, 0.45, 0.8]],
        [[0, 0, 0.45, 1], [0.5, 0, 0.45, 0.7], [1, 0, 0.45, 0.85], [1.5, 4, 0.45, 0.75],
          [2, 0, 0.45, 0.95], [2.5, 0, 0.45, 0.7], [3, 2, 0.45, 0.8], [3.5, -1, 0.45, 0.75]],
      ],
      opts: { cutoff: 3.2, res: 8, sub: 0.7, drive: 0.55 },
    },

    {
      id: 'choir', voice: 'pad', min: 1, gain: 0.14, octave: 1,
      rhythm: [[0, 3.9, 0.9]], opts: { rev: 0.65, cutoff: 1500 },
    },
    {
      id: 'stabs', voice: 'stab', min: 2, gain: 0.16, octave: 1,
      rhythm: [
        [[0, 0.2, 1], [1.5, 0.16, 0.7], [3, 0.2, 0.85]],
        [[0, 0.2, 1], [2, 0.16, 0.75], [3.5, 0.2, 0.9]],
      ],
      opts: { wah: 1.6, rev: 0.24, dly: 0.12 },
    },

    // the motif that climbs a phrase at a time
    {
      id: 'lead', voice: 'lead', min: 2, gain: 0.2, octave: 2, span: 4, seq: CLIMB,
      opts: { detune: 13, cutoff: 5, res: 4, rev: 0.2, dly: 0.2, pan: -0.12, vib: 0.8 },
    },
    {
      id: 'leadOct', voice: 'bell', min: 3, gain: 0.1, octave: 3, span: 4, seq: CLIMB,
      opts: { ratio: 2.0, index: 4, rev: 0.4, dly: 0.28 },
    },
    {
      id: 'bighit', voice: 'orchHit', min: 0, gain: 0.28, octave: 1,
      bars: [0, 8], rhythm: [[0, 0.5, 1]], opts: { rev: 0.55 },
    },
  ],
};
