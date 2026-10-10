/**
 * "STREET BEAT PARADE" — Drumline Dash, 132bpm.  [original composition]
 *
 * Brief: percussive, marching, call-and-response built into the arrangement.
 *
 * The call-and-response is not decoration, it is the *form*: the snare line
 * owns bars 1-2 of every four, the tenor/bass drum line answers on bars 3-4,
 * and the horn section only ever punctuates the gap. Because the layers are
 * masked by bar, the conversation happens whether or not the melody layers are
 * unlocked — a player at intensity 0 still hears the question and the answer.
 *
 * Harmony: C natural minor, static for four bars at a time. Marching music
 * moves harmonically about as often as a parade turns a corner.
 */

import { note } from '../theory.js';
import { defaultRender } from '../player.js';

/** Tenor-drum sweep: five tuned drums, pitch chosen by position in the bar. */
function tenors(V, ev, t) {
  const inBar = ev.beat - Math.floor(ev.beat / 4) * 4;
  const i = Math.round(inBar * 4);
  const freq = [232, 232, 196, 196, 165, 165, 139, 139, 232, 196, 165, 139, 117, 117, 139, 165][i] || 180;
  V.tom(t, { freq, gain: 0.6 * ev.vel, decay: 0.24, pan: ((i % 5) - 2) * 0.24, rev: 0.18 });
}

const CALL = 'xx..xx..xx..xx..';   // snare speaks
const RESP = '..xx..xx..xx..xx';   // drums answer

export default {
  id: 'drumline-dash',
  title: 'Street Beat Parade',
  bpm: 132,
  beatsPerBar: 4,
  bars: 16,
  loop: true,
  swing: 0,
  root: note('C', 2),
  scale: 'minor',
  shape: [0, 2, 4],
  prog: [0, 0, 0, 0, 5, 5, 5, 5, 3, 3, 3, 3, 4, 4, 6, 6],
  mix: 0.95,
  render: defaultRender,

  layers: [
    // ------------------------------------------------- the marching engine
    {
      id: 'bassdrums', voice: 'kick', min: 0, gain: 0.92,
      grid: ['9...9..79...9...', '9...9..79...9.7.', '9...9..79...9...', '9.7.9..79.7.9797'],
      opts: { decay: 0.19, tune: 0.95, click: 0.7 },
    },
    {
      id: 'snareCall', voice: 'snare', min: 0, gain: 0.56, bars: CALL,
      grid: ['9ss57ss59ss57ss5', '9s5s9s5s7s5s9797'],
      opts: { decay: 0.11, snap: 1.2, tone: 1.15, rev: 0.16, pan: -0.18 },
    },
    {
      id: 'tenorResp', voice: 'tom', min: 0, gain: 1, bars: RESP,
      grid: ['9.5.7.5.9.7.5.9.', '9.7.5.9.7.5.9797'],
      render: tenors,
    },
    {
      id: 'rimClicks', voice: 'rim', min: 1, gain: 0.34, bars: RESP,
      grid: '..x...x...x...x.',
    },
    {
      id: 'cymbals', voice: 'hat', min: 1, gain: 0.22,
      grid: 'x.x.x.x.x.x.x.x.', opts: { pan: 0.28 },
    },
    {
      id: 'crash', voice: 'crash', min: 0, gain: 0.36,
      bars: 'x...x...x...x...', grid: 'x...............',
      opts: { decay: 1.3, rev: 0.4 },
    },
    {
      id: 'rollout', voice: 'snare', min: 0, gain: 0.5, bars: [15],
      grid: '3344556677889999',
      opts: { decay: 0.075, snap: 0.9, tone: 1.1, rev: 0.14 },
    },

    // ----------------------------------------------------------- low brass
    {
      id: 'sousa', voice: 'bass', min: 0, gain: 0.42, rel: 'chord', span: 1,
      seq: [
        [[0, 0, 0.6, 1], [1, 4, 0.4, 0.7], [2, 0, 0.6, 0.95], [3, 4, 0.4, 0.7]],
        [[0, 0, 0.6, 1], [1, 4, 0.4, 0.7], [2, 7, 0.5, 0.9], [3, 2, 0.4, 0.72]],
      ],
      opts: { cutoff: 2.6, res: 5, sub: 0.7, drive: 0.45 },
    },

    // ---------------------------------------------- horns answer the drums
    {
      id: 'hornHits', voice: 'stab', min: 1, gain: 0.19, octave: 1, bars: CALL,
      rhythm: [[[3, 0.4, 0.9]], [[2.5, 0.3, 0.8], [3, 0.5, 0.95]]],
      opts: { wah: 1.3, rev: 0.26, dly: 0.1 },
    },
    {
      id: 'hornPad', voice: 'organ', min: 2, gain: 0.1, octave: 1,
      rhythm: [[0, 3.8, 0.85]],
      opts: { rev: 0.36, drawbars: [1, 0.45, 0.5, 0.2, 0.14] },
    },

    // ------------------------------------------------------- the top line
    {
      id: 'mellophone', voice: 'brass', min: 2, gain: 0.2, octave: 2, span: 4,
      seq: [
        [ // bars 1-4: a fanfare shout, answered by the drums in the gaps
          [0, 7, 0.5, 1], [0.5, 7, 0.5, 0.85], [1, 9, 1, 0.95], [2.5, 7, 0.5, 0.8],
          [3, 4, 1.5, 0.9],
          [8, 7, 0.5, 1], [8.5, 9, 0.5, 0.9], [9, 11, 1.5, 1],
          [11, 9, 0.5, 0.85], [11.5, 7, 0.5, 0.85],
        ],
        [ // bars 5-8: same shape a third up, over Ab
          [0, 9, 0.5, 1], [0.5, 9, 0.5, 0.85], [1, 11, 1, 0.95], [2.5, 9, 0.5, 0.8],
          [3, 7, 1.5, 0.9],
          [8, 11, 0.5, 1], [8.5, 12, 0.5, 0.9], [9, 14, 1.5, 1],
          [11, 12, 0.5, 0.9], [11.5, 11, 0.5, 0.85],
        ],
        [ // bars 9-12: the long note, over Fm — space for the drum answer
          [0, 11, 1.5, 1], [2, 9, 1, 0.9],
          [4, 7, 2, 0.95],
          [8, 9, 0.5, 0.9], [8.5, 11, 0.5, 0.95], [9, 12, 1, 1],
          [10.5, 11, 0.5, 0.85], [11, 9, 1, 0.9],
        ],
        [ // bars 13-16: climb to the turnaround
          [0, 7, 0.5, 0.9], [0.5, 9, 0.5, 0.9], [1, 11, 0.5, 0.95], [1.5, 12, 1.5, 1],
          [4, 12, 0.5, 0.95], [4.5, 11, 0.5, 0.9], [5, 9, 0.5, 0.9], [5.5, 7, 1.5, 0.95],
          [8, 14, 2, 1], [10.5, 12, 0.5, 0.9], [11, 11, 0.75, 0.9],
        ],
      ],
      opts: { bite: 1.2, rev: 0.26, dly: 0.1 },
    },
    {
      id: 'piccolo', voice: 'bell', min: 3, gain: 0.13, octave: 3, span: 4,
      seq: [
        [[0, 7, 0.4, 0.8], [1, 9, 0.4, 0.8], [2, 11, 0.4, 0.9], [3, 9, 0.4, 0.7],
          [8, 11, 0.4, 0.9], [9, 12, 0.4, 0.85], [10, 14, 0.8, 1]],
        [[0, 11, 0.4, 0.85], [1, 12, 0.4, 0.85], [2, 14, 0.8, 1],
          [8, 12, 0.4, 0.85], [9, 11, 0.4, 0.8], [10, 9, 0.8, 0.9]],
      ],
      opts: { ratio: 3.02, index: 5, rev: 0.4, dly: 0.24 },
    },
  ],
};
