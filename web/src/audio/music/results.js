/**
 * "TAKE A BOW" — results fanfare, 128bpm, ONE SHOT (does not loop).
 * [original composition]
 *
 * Four bars. Bars 1-3 are the fanfare proper — a rising brass figure over a
 * IV-V-I cadence, which is the most reliable "you did it" gesture in Western
 * music and there is no reason to be clever about it. Bar 4 lands on the tonic
 * with the whole band and lets the reverb tail carry into the score readout,
 * so the screen doesn't go silent while the numbers are still counting up.
 *
 * `loop: false` — the player stops itself at the end and fades out, which is
 * why this is a track and not an SFX: it has to be in the same key system as
 * everything else so a rank stinger can be pitched against it.
 */

import { note } from '../theory.js';
import { defaultRender } from '../player.js';

export default {
  id: 'results',
  title: 'Take a Bow',
  bpm: 128,
  beatsPerBar: 4,
  bars: 4,
  loop: false,
  swing: 0,
  root: note('C', 2),
  scale: 'major',
  shape: [0, 2, 4],
  prog: [3, 4, 0, 0],
  mix: 1.0,
  render: defaultRender,

  layers: [
    {
      id: 'kick', voice: 'kick', min: 0, gain: 0.95,
      grid: ['9...9...9...9...', '9...9...9...9.9.', '9.......9.......', '9...............'],
      opts: { decay: 0.22, click: 0.5 },
    },
    {
      id: 'snare', voice: 'snare', min: 0, gain: 0.55,
      grid: ['....9...9.9.9.9.', '....9...9.9.9797', '....9.......9...', '9...............'],
      opts: { decay: 0.12, snap: 1.1, rev: 0.24 },
    },
    {
      id: 'crash', voice: 'crash', min: 0, gain: 0.4,
      grid: 'x...............', opts: { decay: 1.8, rev: 0.5 },
    },
    {
      id: 'bass', voice: 'bass', min: 0, gain: 0.46, rel: 'chord', span: 1,
      seq: [
        [[0, 0, 0.9, 1], [2, 4, 0.9, 0.85]],
        [[0, 0, 0.9, 1], [2, 4, 0.5, 0.85], [3, 7, 0.5, 0.9]],
        [[0, 0, 1.9, 1], [2, 0, 1.9, 0.9]],
        [[0, 0, 3.5, 1]],
      ],
      opts: { cutoff: 3.4, res: 6, sub: 0.72, drive: 0.45 },
    },
    {
      id: 'chords', voice: 'brass', min: 0, gain: 0.2, octave: 1, rel: 'chord', span: 4,
      seq: [[
        // bar 1 (IV): rising triad
        [0, 0, 0.45, 0.9], [0.5, 2, 0.45, 0.9], [1, 4, 1, 0.95], [2.5, 4, 0.4, 0.85], [3, 7, 0.9, 1],
        // bar 2 (V): the push
        [4, 0, 0.45, 0.95], [4.5, 2, 0.45, 0.9], [5, 4, 1, 0.95], [6.5, 7, 1.4, 1],
        // bar 3 (I): arrival
        [8, 7, 1.4, 1], [10, 4, 0.9, 0.9], [11, 7, 0.9, 0.95],
        // bar 4: hold
        [12, 9, 3.6, 1],
      ]],
      opts: { bite: 1.3, rev: 0.35, dly: 0.1 },
    },
    {
      id: 'sparkle', voice: 'bell', min: 0, gain: 0.16, octave: 3, span: 4,
      seq: [[
        [3, 11, 0.5, 0.9], [3.5, 12, 0.5, 0.9],
        [7, 12, 0.5, 0.9], [7.5, 14, 0.5, 0.95],
        [11, 14, 0.5, 0.95], [11.5, 16, 0.5, 1],
        [12, 18, 2.5, 1],
      ]],
      opts: { ratio: 2.01, index: 5, rev: 0.5, dly: 0.3 },
    },
    {
      id: 'organ', voice: 'organ', min: 0, gain: 0.13, octave: 1,
      rhythm: [[0, 3.9, 0.9]], opts: { rev: 0.45 },
    },
    {
      id: 'hits', voice: 'orchHit', min: 0, gain: 0.3, octave: 1,
      bars: [0, 2, 3], rhythm: [[0, 0.5, 1]], opts: { rev: 0.6 },
    },
  ],
};
