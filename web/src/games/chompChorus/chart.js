/**
 * Chomp Chorus — lane definitions and the note chart.  [G4 builder owns this dir]
 *
 * Dependency-free on purpose: the verification script (`verify.mjs`) imports
 * this module directly in Node to drive the correct key for every note, and a
 * chart that needed a WebGL context to describe itself could not be checked.
 *
 * ── The lane mapping ───────────────────────────────────────────────────────
 * Four singers stand left to right. The keys are the four arrows (or WASD,
 * which the input layer aliases onto the same actions):
 *
 *     ←  lane 0   far left    round build      lowest chord tone
 *     ↓  lane 1   mid left    WIDE + LOW       3rd
 *     ↑  lane 2   mid right   TALL + HIGH      5th
 *     →  lane 3   far right   tiny             top tone
 *
 * The outer two are trivially spatial. The inner two are the only ones that
 * could be ambiguous, so the *builds* carry the mapping: the ↓ lane is the
 * squat, wide, low-headed singer and the ↑ lane is the lanky one whose head
 * stands a clear head above the row. Up is up. Down is down. You never have to
 * be told twice.
 *
 * ── Pitch ──────────────────────────────────────────────────────────────────
 * Lane i sings chord tone i of whatever the track is currently sitting on
 * (Snack Attack Shuffle is a four-note seventh-chord vamp — exactly four
 * tones for exactly four lanes). Left to right is low to high, like a choir
 * line-up or a keyboard, so the harmony is spatially legible too, and a lane
 * that has dropped out leaves a hole you can hear *and* point at.
 */

export const LANES = [
  { i: 0, action: 'left', dir: 'left', build: 'round', pal: 'lagoon', x: -3.35, pan: -0.62 },
  { i: 1, action: 'down', dir: 'down', build: 'wide', pal: 'ember', x: -1.12, pan: -0.21 },
  { i: 2, action: 'up', dir: 'up', build: 'tall', pal: 'lime', x: 1.12, pan: 0.21 },
  { i: 3, action: 'right', dir: 'right', build: 'small', pal: 'sunburst', x: 3.35, pan: 0.62 },
];

export const LANE_ACTIONS = LANES.map((l) => l.action);
export const ACTION_TO_LANE = Object.fromEntries(LANES.map((l) => [l.action, l.i]));

/** Bars of scored content after beat 0 (lead-in is separate). */
export const PLAY_BARS = 40;
/** Beat of the held four-part finale chord. */
export const FINALE_BEAT = 156;
/** Beat at which the finale's sustain window closes. */
export const FINALE_END = 160;
/** Beat at which the round is over and `result()` starts returning. */
export const END_BEAT = 164;

/**
 * The chart.
 *
 * Escalation, cashed out in the note data rather than in prose:
 *   bars  0-7   TEACH     one lane at a time, two bars each, half notes only
 *   bars  8-11  WALK      every lane, one note per beat, sweeping left/right
 *   bars 12-15  PAIRS     two-note chords appear
 *   bars 16-19  WEAVE     chords plus off-beat eighths
 *   bars 20-23  TRIADS    three-note chords
 *   bars 24-27  RUNS      sixteenth runs sweeping across all four lanes
 *   bars 28-31  PEAK      full four-part chords + runs, alternating with air
 *   bars 32-35  DRIVE     chords on every beat, then a full eighth-note sweep
 *   bars 36-38  BUILD     into the big inhale (bar 38 beats 2-4 are silent)
 *   bar  39     FINALE    all four, held through the last bar. Worth double.
 */
export function buildChart() {
  const notes = [];
  const add = (beat, lanes, extra) => {
    for (const l of lanes) notes.push({ beat, lane: l, ...extra });
  };
  const B = (bar, beat = 0) => bar * 4 + beat;

  // --- TEACH: bars 0-7 ------------------------------------------------------
  // Two bars per lane. The first note of each lane is flagged so the game can
  // pop that lane's key glyph, big, at the moment the lane is introduced.
  for (let bar = 0; bar < 8; bar++) {
    const lane = bar >> 1;
    const first = bar % 2 === 0;
    add(B(bar, 0), [lane], first ? { intro: true } : undefined);
    add(B(bar, 2), [lane]);
    if (!first) add(B(bar, 3), [lane]);
  }

  // --- WALK: bars 8-11 ------------------------------------------------------
  for (let bar = 8; bar < 12; bar++) {
    const rev = bar % 2 === 1;
    for (let k = 0; k < 4; k++) add(B(bar, k), [rev ? 3 - k : k]);
  }

  // --- PAIRS: bars 12-15 ----------------------------------------------------
  const PAIR_BARS = [
    [[0, [0, 2]], [1, [1]], [2, [1, 3]], [3, [2]]],
    [[0, [1, 3]], [1, [2]], [2, [0, 2]], [3, [1]]],
    [[0, [0, 3]], [1, [1]], [2, [1, 2]], [3, [3]]],
    [[0, [1, 2]], [1, [0]], [2, [0, 3]], [3, [3]]],
  ];
  for (let bar = 12; bar < 16; bar++) {
    for (const [beat, lanes] of PAIR_BARS[bar - 12]) add(B(bar, beat), lanes);
  }

  // --- WEAVE: bars 16-19 ----------------------------------------------------
  const WEAVE = [
    [[0, [0, 3]], [1, [1]], [1.5, [2]], [2, [1, 2]], [3, [3]], [3.5, [0]]],
    [[0, [1, 2]], [1, [3]], [1.5, [0]], [2, [0, 3]], [3, [2]], [3.5, [1]]],
    [[0, [0, 1]], [1, [2]], [1.5, [3]], [2, [2, 3]], [3, [1]], [3.5, [0]]],
    [[0, [1, 3]], [1, [0]], [1.5, [2]], [2, [0, 2]], [3, [3]], [3.5, [1]]],
  ];
  for (let bar = 16; bar < 20; bar++) {
    for (const [beat, lanes] of WEAVE[bar - 16]) add(B(bar, beat), lanes);
  }

  // --- TRIADS: bars 20-23 ---------------------------------------------------
  const TRIAD = [
    [[0, [0, 1, 2]], [1.5, [3]], [2, [1, 2, 3]], [3.5, [0]]],
    [[0, [0, 2, 3]], [1.5, [1]], [2, [0, 1, 3]], [3.5, [2]]],
    [[0, [1, 2, 3]], [1.5, [0]], [2, [0, 1, 2]], [3.5, [3]]],
    [[0, [0, 1, 3]], [1.5, [2]], [2, [0, 2, 3]], [3.5, [1]]],
  ];
  for (let bar = 20; bar < 24; bar++) {
    for (const [beat, lanes] of TRIAD[bar - 20]) add(B(bar, beat), lanes);
  }

  // --- RUNS: bars 24-27 -----------------------------------------------------
  // A sixteenth run is four notes that sweep the row. The eye follows a moving
  // thing far better than it reads four separate things, so a run is telegraphed
  // as one travelling wave rather than four independent gulps.
  const run = (bar, beat, order, tag) => {
    for (let k = 0; k < 4; k++) {
      add(B(bar, beat + k * 0.25), [order[k]], { run: tag, runStep: k });
    }
  };
  for (let bar = 24; bar < 28; bar++) {
    const up = bar % 2 === 0;
    add(B(bar, 0), [0, 3]);
    run(bar, 1, up ? [0, 1, 2, 3] : [3, 2, 1, 0], bar * 2);
    add(B(bar, 2), [1, 2]);
    run(bar, 3, up ? [3, 2, 1, 0] : [0, 1, 2, 3], bar * 2 + 1);
  }

  // --- PEAK: bars 28-31 -----------------------------------------------------
  for (let bar = 28; bar < 32; bar++) {
    if (bar % 2 === 0) {
      add(B(bar, 0), [0, 1, 2, 3]);
      run(bar, 1, [0, 1, 2, 3], bar * 2);
      add(B(bar, 2), [0, 2]);
      run(bar, 3, [3, 2, 1, 0], bar * 2 + 1);
    } else {
      add(B(bar, 0), [1, 2]);
      add(B(bar, 1), [0]);
      add(B(bar, 1.5), [3]);
      add(B(bar, 2), [0, 3]);
      add(B(bar, 3), [1]);
      add(B(bar, 3.5), [2]);
    }
  }

  // --- DRIVE: bars 32-35 ----------------------------------------------------
  add(B(32, 0), [0, 1, 2]); add(B(32, 1.5), [3]); add(B(32, 2), [1, 2, 3]); add(B(32, 3.5), [0]);
  run(33, 0, [0, 1, 2, 3], 66); add(B(33, 1), [0, 3]); run(33, 2, [3, 2, 1, 0], 67); add(B(33, 3), [1, 2]);
  add(B(34, 0), [0, 1]); add(B(34, 1), [2, 3]); add(B(34, 2), [0, 2]); add(B(34, 3), [1, 3]);
  const sweep = [0, 1, 2, 3, 3, 2, 1, 0];
  for (let k = 0; k < 8; k++) add(B(35, k * 0.5), [sweep[k]]);

  // --- BUILD: bars 36-38 ----------------------------------------------------
  add(B(36, 0), [0, 1, 2]); add(B(36, 2), [1, 2, 3]);
  add(B(37, 0), [0, 3]); add(B(37, 1), [1, 2]); add(B(37, 2), [0, 3]); add(B(37, 3), [1, 2]);
  // Bar 38 beat 1 lands, and then everything stops: three beats of silence into
  // which the four singers take one enormous shared breath.
  add(B(38, 0), [0, 1, 2, 3]);

  // --- FINALE: bar 39 -------------------------------------------------------
  // Four beats of telegraph (`lead: 4`) rather than one, because it is the only
  // moment in the round that asks for something the player has not done before.
  add(FINALE_BEAT, [0, 1, 2, 3], { finale: true, lead: 4, holdBeats: FINALE_END - FINALE_BEAT });

  notes.sort((a, b) => a.beat - b.beat || a.lane - b.lane);
  return notes;
}

/** Per-lane beat lists, for the telegraph lookahead. */
export function laneSchedules(notes) {
  const out = [[], [], [], []];
  for (const n of notes) out[n.lane].push(n);
  return out;
}
