/**
 * "BONANZA!" — title / menu theme, 112bpm.  [original composition]
 *
 * Brief: the first eight seconds of the product. It has to say "this is going
 * to be fun and you are not going to be bad at it" before a single word of UI
 * is read.
 *
 * So: C major, no borrowed chords, no minor sevenths, an eight-bar loop that
 * resolves cleanly every time, and a bell hook wide enough to leave room for
 * menu SFX (which are pitched into this key — see audio/index.js).
 *
 * It stays at intensity 1 by default; the extra layers exist so the select
 * screen can push it to 2 when the player is scrolling, which makes the menu
 * itself feel like part of the show.
 */

import { note } from '../theory.js';
import { defaultRender } from '../player.js';

const HOOK = [
  [
    [0, 7, 0.5, 0.9], [0.5, 9, 0.5, 0.85], [1, 11, 1, 1],
    [2.5, 9, 0.5, 0.85], [3, 7, 1, 0.9],
    [4, 9, 0.5, 0.9], [4.5, 11, 0.5, 0.85], [5, 12, 1.5, 1],
    [7, 11, 0.75, 0.9],
  ],
  [
    [0, 12, 0.5, 0.95], [0.5, 11, 0.5, 0.85], [1, 9, 1, 0.95],
    [2.5, 11, 0.5, 0.85], [3, 12, 1, 0.95],
    [4, 14, 1, 1], [5.5, 12, 0.5, 0.9], [6, 11, 0.5, 0.9], [6.5, 7, 1.5, 0.95],
  ],
];

export default {
  id: 'title',
  title: 'Bonanza!',
  bpm: 112,
  beatsPerBar: 4,
  bars: 8,
  loop: true,
  swing: 0.08,
  root: note('C', 2),
  scale: 'major',
  shape: [0, 2, 4],
  prog: [0, 3, 5, 4, 0, 3, 1, 4],
  mix: 0.85,
  render: defaultRender,

  layers: [
    {
      id: 'kick', voice: 'kick', min: 0, gain: 0.85,
      grid: ['9.......9.......', '9.......9.....7.'],
      opts: { decay: 0.23, click: 0.4 },
    },
    {
      id: 'clap', voice: 'clap', min: 0, gain: 0.4, grid: '....9.......9...', opts: { rev: 0.32 } },
    {
      id: 'hats', voice: 'hat', min: 0, gain: 0.22, grid: '..6...6...6...7.', opts: { open: 0.1 } },
    {
      id: 'shaker', voice: 'shaker', min: 2, gain: 0.14, grid: 'x.x.x.x.x.x.x.x.' },
    {
      id: 'crash', voice: 'crash', min: 0, gain: 0.28,
      bars: [0, 4], grid: 'x...............', opts: { decay: 1.2, rev: 0.45 },
    },
    {
      id: 'bass', voice: 'bass', min: 0, gain: 0.4, rel: 'chord', span: 1,
      seq: [
        [[0, 0, 0.7, 1], [1.5, 7, 0.35, 0.7], [2, 0, 0.6, 0.9], [3.5, 4, 0.35, 0.7]],
        [[0, 0, 0.7, 1], [1.5, 4, 0.35, 0.7], [2, 2, 0.6, 0.85], [3, 0, 0.8, 0.8]],
      ],
      opts: { cutoff: 3.6, res: 6, sub: 0.6, drive: 0.3 },
    },
    {
      id: 'pad', voice: 'pad', min: 0, gain: 0.14, octave: 1,
      rhythm: [[0, 3.9, 0.9]], opts: { rev: 0.6, cutoff: 2200 },
    },
    {
      id: 'upstrokes', voice: 'stab', min: 1, gain: 0.13, octave: 1,
      rhythm: [[1.5, 0.16, 0.8], [3.5, 0.16, 0.75]],
      opts: { wah: 1.2, dly: 0.2, rev: 0.24, pan: 0.2 },
    },
    {
      id: 'hook', voice: 'bell', min: 1, gain: 0.19, octave: 2, span: 2, seq: HOOK,
      opts: { ratio: 2.01, index: 4.5, rev: 0.42, dly: 0.26 },
    },
    {
      id: 'hookDouble', voice: 'pluck', min: 2, gain: 0.17, octave: 2, span: 2, seq: HOOK,
      opts: { bright: 3.6, rev: 0.22, dly: 0.18, pan: -0.2 },
    },
  ],
};
