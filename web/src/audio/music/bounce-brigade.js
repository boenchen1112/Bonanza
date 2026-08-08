/**
 * "RUBBER BAND STAND" — Bounce Brigade, 118bpm.  [original composition]
 *
 * Brief: bouncy, springy, playful.
 *
 * Springiness comes from where the notes AREN'T: the kick holds down 1 and 3,
 * and almost everything else lands on an off-beat. Chord stabs play the "and"
 * of every beat (an upstroke pattern), the hats are pure off-beats, and the
 * bass leaps a full octave and drops back inside every bar. The ear keeps
 * getting pulled off the floor and let go.
 *
 * Harmony: D major, I-vi-IV-V — the friendliest progression there is, which is
 * the point. The hook is written in D major pentatonic so every note is safe
 * to sing along to on first listen.
 */

import { note } from '../theory.js';
import { defaultRender } from '../player.js';

/** Boing: a pluck whose pitch springs up an octave and settles. */
function boing(V, ev, t, m) {
  const midi = m.midiFor(ev.layer, ev.deg);
  V.pluck(t, { midi: midi + 12, dur: 0.1, gain: 0.2 * ev.vel, bright: 5, rev: 0.16, dly: 0.2 });
  V.pluck(t + 0.055, { midi, dur: 0.22, gain: 0.26 * ev.vel, bright: 3.4, rev: 0.2, dly: 0.24 });
}

const HOOK = [
  [ // A — over D, pentatonic, hops off the beat
    [0.5, 7, 0.4, 0.95], [1, 9, 0.4, 0.85], [1.5, 11, 0.5, 1],
    [2.5, 9, 0.4, 0.85], [3, 7, 0.4, 0.9], [3.5, 4, 0.9, 0.85],
    [5.5, 7, 0.4, 0.9], [6, 9, 0.4, 0.85], [6.5, 7, 1, 0.95],
  ],
  [ // A' — over Bm, same rhythm a step down: instantly recognisable as "the same"
    [0.5, 9, 0.4, 0.95], [1, 11, 0.4, 0.85], [1.5, 12, 0.5, 1],
    [2.5, 11, 0.4, 0.85], [3, 9, 0.4, 0.9], [3.5, 7, 0.9, 0.85],
    [5.5, 11, 0.4, 0.9], [6, 9, 0.4, 0.85], [6.5, 11, 1, 0.95],
  ],
  [ // B — over G, the lift
    [0, 11, 0.4, 1], [0.5, 12, 0.4, 0.9], [1, 14, 1, 1],
    [2.5, 12, 0.4, 0.85], [3, 11, 0.9, 0.9],
    [4.5, 9, 0.4, 0.85], [5, 11, 0.4, 0.9], [5.5, 12, 0.4, 0.95], [6, 14, 1.5, 1],
  ],
  [ // B' — over A, tumbles home and leaves the last beat empty for the fill
    [0, 12, 0.4, 0.95], [0.5, 11, 0.4, 0.9], [1, 9, 0.4, 0.9], [1.5, 7, 0.9, 0.9],
    [3.5, 4, 0.4, 0.8],
    [4, 7, 0.5, 0.95], [4.5, 9, 0.5, 0.9], [5, 11, 0.5, 0.95], [5.5, 12, 1.5, 1],
  ],
];

export default {
  id: 'bounce-brigade',
  title: 'Rubber Band Stand',
  bpm: 118,
  beatsPerBar: 4,
  bars: 16,
  loop: true,
  swing: 0.055,
  root: note('D', 2),
  scale: 'major',
  shape: [0, 2, 4],
  prog: [0, 0, 5, 5, 3, 3, 4, 4, 0, 0, 5, 5, 3, 4, 0, 4],
  mix: 0.95,
  render: defaultRender,

  layers: [
    {
      id: 'kick', voice: 'kick', min: 0, gain: 0.9,
      grid: ['9.......9.......', '9.......9.....7.', '9.......9.......', '9.....7.9...7...'],
      opts: { decay: 0.24, tune: 1.04, click: 0.4 },
    },
    {
      id: 'clap', voice: 'clap', min: 0, gain: 0.5,
      grid: '....9.......9...', opts: { rev: 0.3 },
    },
    {
      id: 'offhats', voice: 'hat', min: 0, gain: 0.28,
      grid: '..7...7...7...8.', opts: { open: 0.12, pan: 0.2 },
    },
    {
      id: 'ticks', voice: 'rim', min: 2, gain: 0.24,
      grid: '...s..s..s..s..s', opts: { pan: -0.3 },
    },
    {
      id: 'shaker', voice: 'shaker', min: 1, gain: 0.16,
      grid: 'x.x.x.x.x.x.x.x.',
    },
    {
      id: 'crash', voice: 'crash', min: 0, gain: 0.3,
      bars: 'x...x...x...x...', grid: 'x...............',
      opts: { decay: 1.1, rev: 0.42 },
    },
    {
      id: 'fill', voice: 'tom', min: 0, gain: 0.55, bars: '...x...x...x...x',
      grid: '............7.99', opts: { freq: 150, decay: 0.22, rev: 0.2 },
    },

    // rubbery octave-jumping bass — the actual bounce
    {
      id: 'bass', voice: 'bass', min: 0, gain: 0.42, rel: 'chord', span: 1,
      seq: [
        [[0, 0, 0.4, 1], [0.5, 7, 0.25, 0.7], [1, 0, 0.3, 0.75],
          [2, 0, 0.4, 0.95], [2.5, 7, 0.25, 0.7], [3, 4, 0.3, 0.8], [3.5, 2, 0.25, 0.7]],
        [[0, 0, 0.4, 1], [0.5, 4, 0.25, 0.7], [1, 7, 0.3, 0.8],
          [2, 0, 0.4, 0.95], [2.5, 7, 0.25, 0.7], [3, 2, 0.3, 0.8], [3.5, -1, 0.3, 0.7]],
      ],
      opts: { cutoff: 4.2, res: 8, sub: 0.55, drive: 0.3 },
    },

    // upstroke chords: every "and", never a downbeat
    {
      id: 'upstrokes', voice: 'stab', min: 0, gain: 0.15, octave: 1,
      rhythm: [[0.5, 0.16, 0.85], [1.5, 0.16, 0.75], [2.5, 0.16, 0.85], [3.5, 0.16, 0.8]],
      opts: { wah: 1.1, dly: 0.2, rev: 0.2, pan: 0.18 },
    },
    {
      id: 'pad', voice: 'pad', min: 1, gain: 0.13, octave: 1,
      rhythm: [[0, 3.9, 0.9]], opts: { rev: 0.6, cutoff: 2000 },
    },

    // the singalong
    {
      id: 'hook', voice: 'pluck', min: 2, gain: 0.34, octave: 2, span: 2, seq: HOOK,
      opts: { bright: 3.8, rev: 0.24, dly: 0.16 },
    },
    {
      id: 'sparkle', voice: 'bell', min: 3, gain: 0.12, octave: 3, span: 2, seq: HOOK,
      opts: { ratio: 2.01, index: 3.5, rev: 0.45, dly: 0.3 },
    },
    {
      id: 'boing', voice: 'pluck', min: 1, gain: 1, octave: 1, rel: 'chord',
      bars: '...x...x...x...x', notes: [[3.5, 0, 0.2, 1]],
      render: boing,
    },
  ],
};
