/**
 * Finale Fever — the chart, and the tempo curve.
 *
 * Two ideas live in this file and nothing else does.
 *
 * 1. **Notes are stored as BEATS, never as times.** The transport is ramping
 *    continuously, so any time computed more than a frame in advance is a lie.
 *    `index.js` re-derives `note.time = clock.timeAt(note.beat)` every frame for
 *    the notes still in play, which makes the chart exact at every tempo instead
 *    of merely close. This is the single thing most likely to break a game that
 *    ramps, so it is fixed structurally rather than tuned around.
 *
 * 2. **The phrase vocabulary quotes the other four games.** Each verb the series
 *    taught gets a shape here, and the boss states it before you answer it:
 *
 *      swing   G1 Swing Kings   one enormous commit on the downbeat
 *      echo    G2 Drumline Dash a call you must play back
 *      groove  G3 Bounce Brigade an unbroken run of eighths
 *      chord   G4 Chomp Chorus  two lanes at once (left + right together)
 *
 *    `hook` is the game's own signature — the six-note figure the boss keeps
 *    coming back to. It is stated in the lead-in and answered thirteen times
 *    before the solo, which is the whole point: the last four bars have no
 *    visual telegraph, and the only reason a player can survive them is that
 *    they have had this rhythm in their hands for a minute.
 */

import { clamp01 } from '../../core/util.js';

export const BPM0 = 150;
/** Top of the ramp, as a multiple of BPM0. 150 -> 202.5. */
export const RAMP = 1.35;
export const BEATS_PER_BAR = 4;
/** Two bars of lead-in, per the universal rules. */
export const LEAD_BEATS = 8;

/** Bar layout. 23 call/answer phrases, a four-bar solo, one finale note. */
export const DUEL_PHRASES = 23;
export const SOLO_BAR = DUEL_PHRASES * 2;         // 46
export const SOLO_BARS = 4;
export const END_BAR = SOLO_BAR + SOLO_BARS;      // 50
export const END_BEAT = END_BAR * BEATS_PER_BAR;  // 200 — the finale note

/**
 * The tempo curve.
 *
 * Deliberately NOT linear. A linear ramp spends its first third being
 * imperceptible and its last third being brutal; this one eases in (the teach
 * section barely moves, so the mechanic is learnable) and then leans, reaching
 * ~1.30x as the solo starts and the full 1.35x on the finale note. The player
 * should be able to feel it accelerating under them the whole way and never be
 * able to point at a step.
 */
export function bpmAt(beat) {
  const s = clamp01(beat / END_BEAT);
  return BPM0 * (1 + (RAMP - 1) * (0.25 * s + 0.75 * s * s));
}

/** Sections, and how much warning the player gets in each. */
export const SECTIONS = {
  teach: { telegraph: 4, label: 'WARM-UP' },
  play: { telegraph: 3, label: 'DUEL' },
  escalate: { telegraph: 2, label: 'FEVER' },
  solo: { telegraph: 0, label: 'SOLO' },
};

/**
 * Patterns, in beats from the top of the bar.
 * A bare number is a tap; `{ b, chord: true }` is a two-lane chord;
 * `{ b, kind }` overrides the read of the note.
 */
const P = {
  swing: [{ b: 0, kind: 'swing' }, { b: 2, kind: 'swing' }],
  hook: [0, 1, 1.5, 2, 3, 3.5],
  hookEnd: [0, 1, 1.5, 2, 3],
  echoA: [0, 1, 2, 2.5],
  echoB: [0, 0.5, 1.5, 3],
  echoC: [0.5, 1, 2, 2.5, 3],
  groove: [0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5],
  groove2: [0, 0.5, 1, 2, 2.5, 3, 3.5],
  chord: [{ b: 0, chord: true }, 1.5, { b: 2, chord: true }, 3],
  chordMix: [0, { b: 1, chord: true }, 2, 2.5, { b: 3, chord: true }],
  rush: [0, 0.5, 1, 1.5, { b: 2, chord: true }, 2.75, 3, 3.5],
  rush2: [0, 0.75, 1, 1.5, 2, 2.5, { b: 3, chord: true }, 3.5],
};

/**
 * The running order. Twenty-three call/answer phrases: six to teach, fourteen
 * to play, three to panic. `hook` recurs on a four-phrase cycle so that by the
 * solo it is muscle memory.
 */
const RUN = [
  // teach — one verb at a time, widest telegraph
  ['swing', 'teach'], ['echoA', 'teach'], ['groove', 'teach'],
  ['hook', 'teach'], ['echoB', 'teach'], ['groove2', 'teach'],
  // play — the verbs start mixing, chords arrive
  ['hook', 'play'], ['echoC', 'play'], ['groove', 'play'], ['chord', 'play'],
  ['echoA', 'play'], ['hook', 'play'], ['groove2', 'play'], ['chordMix', 'play'],
  ['echoB', 'play'], ['hook', 'play'], ['groove', 'play'], ['echoC', 'play'],
  ['chordMix', 'play'], ['hook', 'play'],
  // escalate — everything, at once, with two beats of warning
  ['rush', 'escalate'], ['chordMix', 'escalate'], ['rush2', 'escalate'],
];

const entryBeat = (e) => (typeof e === 'number' ? e : e.b);
const entryKind = (e) => (typeof e === 'number' ? 'tap' : (e.kind || (e.chord ? 'chord' : 'tap')));

/**
 * Build the whole chart as data. No times, no audio, no THREE — just beats,
 * which is why the tempo ramp cannot corrupt it.
 *
 * @returns {{notes:Note[], cues:Cue[], phrases:Phrase[]}}
 */
export function buildChart() {
  /** @type {any[]} */ const notes = [];
  /** @type {any[]} */ const cues = [];
  /** @type {any[]} */ const phrases = [];

  const push = (n) => { notes.push(n); return notes.length - 1; };

  for (let i = 0; i < RUN.length; i++) {
    const [patName, section] = RUN[i];
    const pat = P[patName];
    const tele = SECTIONS[section].telegraph;
    const callBeat0 = i * 2 * BEATS_PER_BAR;
    const ansBeat0 = callBeat0 + BEATS_PER_BAR;
    const idx0 = notes.length;
    let prevOff = -99;

    for (let k = 0; k < pat.length; k++) {
      const off = entryBeat(pat[k]);
      const kind = entryKind(pat[k]);
      // The boss only takes a full swing on notes with room around them;
      // otherwise a run of eighths turns the baton into a hummingbird.
      const accent = off - prevOff >= 0.75;
      prevOff = off;

      cues.push({
        beat: callBeat0 + off, type: 'call', kind, accent, tone: k, of: pat.length,
      });

      if (kind === 'chord') {
        push({ beat: ansBeat0 + off, action: 'left', kind, lane: -1, telegraph: tele, section, weight: 1, patName });
        push({ beat: ansBeat0 + off, action: 'right', kind, lane: 1, telegraph: tele, section, weight: 1, patName });
      } else {
        push({ beat: ansBeat0 + off, action: 'a', kind, lane: 0, telegraph: tele, section, weight: 1, patName });
      }
    }

    phrases.push({
      idx0, idx1: notes.length, callBeat0, ansBeat0,
      endBeat: ansBeat0 + BEATS_PER_BAR, section, patName, solo: false,
    });
  }

  // ---- the solo -----------------------------------------------------------
  // Four bars of the hook with no call, no orbs, no baton: only the music.
  // The last bar drops its final eighth so that the phrase resolves onto the
  // finale note instead of ending beside it.
  for (let b = 0; b < SOLO_BARS; b++) {
    const bar0 = (SOLO_BAR + b) * BEATS_PER_BAR;
    const pat = b === SOLO_BARS - 1 ? P.hookEnd : P.hook;
    const idx0 = notes.length;
    for (const off of pat) {
      push({ beat: bar0 + off, action: 'a', kind: 'solo', lane: 0, telegraph: 0, section: 'solo', weight: 2, patName: 'hook' });
      // The only cue the solo gets, and it is a musical one: a rim accent that
      // sits inside the arrangement. "Only the music tells you when."
      cues.push({ beat: bar0 + off, type: 'pulse', kind: 'solo', accent: off === 0, tone: 0, of: 1 });
    }
    phrases.push({
      idx0, idx1: notes.length, callBeat0: bar0, ansBeat0: bar0,
      endBeat: bar0 + BEATS_PER_BAR, section: 'solo', patName: 'hook', solo: true,
    });
  }

  // ---- the finale note ----------------------------------------------------
  const fIdx = push({
    beat: END_BEAT, action: 'a', kind: 'finale', lane: 0,
    telegraph: 0, section: 'solo', weight: 4, patName: 'finale',
  });
  phrases.push({
    idx0: fIdx, idx1: notes.length, callBeat0: END_BEAT, ansBeat0: END_BEAT,
    endBeat: END_BEAT + 1, section: 'solo', patName: 'finale', solo: true, finale: true,
  });

  cues.sort((a, b) => a.beat - b.beat);
  notes.sort((a, b) => a.beat - b.beat);
  return { notes, cues, phrases };
}

/** Which section a beat belongs to. Used by the stage escalation. */
export function sectionAt(beat) {
  if (beat < 0) return 'teach';
  const bar = Math.floor(beat / BEATS_PER_BAR);
  if (bar >= SOLO_BAR) return 'solo';
  const phrase = Math.floor(bar / 2);
  return RUN[Math.min(phrase, RUN.length - 1)][1];
}

/** 0..1 through the whole round. Drives every escalation curve in the game. */
export function progressAt(beat) {
  return clamp01(beat / END_BEAT);
}
