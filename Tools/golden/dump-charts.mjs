// Dump the web build's charts (and seeded CPU rolls) so the C# ports can be
// test-pinned against them exactly.
//   node Tools/golden/dump-charts.mjs
// Source: the web prototype in Reference/web/src (dependency-free chart modules).
import { writeFileSync, mkdirSync } from 'node:fs';

const OUT = new URL('../../Assets/Tests/EditMode/Golden/', import.meta.url);
mkdirSync(OUT, { recursive: true });
const load = (p) => import(new URL(`../../Reference/web/src/${p}`, import.meta.url).href);

// Plain text, one record per line, space-separated: EditMode tests read it
// without a JSON library. `cols` names the fields; a missing field is "-".
const cell = (v) => (v === undefined || v === null ? '-' : typeof v === 'boolean' ? (v ? 1 : 0) : v);
const save = (name, rows, cols) => {
  const lines = [`# ${cols.join(' ')}`];
  for (const r of rows) lines.push(cols.map((c) => cell(r[c])).join(' '));
  writeFileSync(new URL(name, OUT), lines.join('\n') + '\n');
  console.log(name, rows.length);
};

const util = await load('core/util.js');
const judge = await load('core/judge.js');

// ---- Chomp Chorus
{
  const c = await load('games/chompChorus/chart.js');
  const CHART = ['beat', 'lane', 'intro', 'run', 'runStep', 'finale', 'lead', 'holdBeats'];
  const charts = [];
  for (let h = 0; h < 4; h++) {
    charts.push(c.buildChart({ humanLane: h }));
    save(`chomp-chorus-h${h}.txt`, charts[h], CHART);
  }

  // CPU rolls exactly as index.js start(): chart order, non-human lanes, skill 0.8.
  const rnd = util.makeRng(0xc40f);
  const skill = 0.8;
  const rolls = charts[0].filter((n) => n.lane !== 0).map((n) => {
    const miss = rnd() < 0.02 + (1 - skill) * 0.22;
    const g = Math.sqrt(-2 * Math.log(1 - rnd())) * Math.cos(2 * Math.PI * rnd());
    const errMs = miss ? judge.WINDOWS_MS.claim : g * (10 + (1 - skill) * 50);
    return { beat: n.beat, lane: n.lane, verdict: miss ? 'miss' : judge.verdictFor(errMs), errMs };
  });
  save('chomp-chorus-cpu.txt', rolls, ['beat', 'lane', 'verdict', 'errMs']);
}

// ---- Drumline Dash
{
  const c = await load('games/drumlineDash/chart.js');
  const phrases = c.buildChart();
  const list = (a) => a.join(',');
  save('drumline-dash-chart.txt', phrases.map((p) => ({
    ...p, callBeats: list(p.callBeats), noteBeats: list(p.noteBeats),
  })), ['index', 'section', 'bars', 'callBeat', 'respBeat', 'endBeat', 'finale', 'callBeats', 'noteBeats']);

  // The house field's race, exactly as index.js closePhrase(): per phrase, per
  // CPU in lane order, one jitter draw then one stumble draw. Seed 0xd12b + 0.
  const rng = util.makeRng(0xd12b);
  const CPUS = [[0.70, 0.16], [0.80, 0.12], [0.90, 0.08]];
  const race = [];
  for (const p of phrases) {
    const mul = p.finale ? 2 : 1;
    CPUS.forEach(([skill, stumble], k) => {
      let a = skill + (rng() - 0.5) * 0.18;
      const stumbled = rng() < stumble;
      if (stumbled) a = 0.22;
      race.push({ phrase: p.index, racer: k + 1, advance: a * mul, stumbled });
    });
  }
  save('drumline-dash-race.txt', race, ['phrase', 'racer', 'advance', 'stumbled']);
  console.log('maxPoints', c.maxPoints(phrases), 'endBeat', c.chartEndBeat(phrases), 'bars', c.chartDurationBars(phrases));
}

// ---- Finale Fever
{
  const c = await load('games/finaleFever/chart.js');
  const { notes, cues, phrases } = c.buildChart();
  save('finale-fever-notes.txt', notes, ['beat', 'action', 'kind', 'lane', 'telegraph', 'section', 'weight', 'patName']);
  save('finale-fever-cues.txt', cues, ['beat', 'type', 'kind', 'accent', 'tone', 'of']);
  save('finale-fever-phrases.txt', phrases, ['idx0', 'idx1', 'callBeat0', 'ansBeat0', 'endBeat', 'section', 'patName', 'solo', 'finale']);
  const probes = [-8, -1, 0, 0.5, 7, 40, 99.25, 150, 183.5, 199, 200, 230];
  save('finale-fever-tempo.txt', probes.map((b) => ({ beat: b, bpm: c.bpmAt(b), section: c.sectionAt(b), progress: c.progressAt(b) })),
    ['beat', 'bpm', 'section', 'progress']);
}

// ---- Bounce Brigade
{
  const c = await load('games/bounceBrigade/chart.js');
  const { platforms, notes, finishIndex, maxPoints } = c.buildChart();
  save('bounce-brigade-platforms.txt', platforms, ['index', 'beat', 'deg', 'kind', 'roll', 'scored', 'charge', 'midi', 'color', 'weight',
    'y', 'tilt', 'departBeat', 'contactX', 'departX', 'nextBeat', 'flightBeats', 'anchorX', 'width', 'slope', 'endX', 'departY', 'centerX', 'centerY']);
  save('bounce-brigade-notes.txt', notes, ['beat', 'action', 'kind', 'platform', 'weight']);
  console.log('finishIndex', finishIndex, 'maxPoints', maxPoints);
}

// ---- Party (shell/state.js)
{
  const st = await load('shell/state.js');
  const IDS = ['swing-kings', 'chomp-chorus', 'drumline-dash', 'bounce-brigade', 'finale-fever'];
  const lists = [];
  for (const seed of [1, 2, 3, 0xbeef, 0x5eed]) {
    for (const len of [1, 3, 4, 5]) {
      const rng = util.makeRng(seed);
      lists.push({ seed, len, games: st.partyPlaylist(IDS, len, rng).join(',') });
    }
  }
  save('party-playlist.txt', lists, ['seed', 'len', 'games']);

  const cpu = [];
  for (const skill of [0, 0.4, 0.63, 0.86]) {
    for (const seed of [7, 99, 0xabcdef]) {
      for (const [score, accuracy] of [[0, 0], [1000, 1], [640, 0.8]]) {
        const r = st.cpuRound(skill, util.makeRng(seed), { score, accuracy });
        cpu.push({ skill, seed, refScore: score, refAcc: accuracy, ...r });
      }
    }
  }
  save('party-cpu.txt', cpu, ['skill', 'seed', 'refScore', 'refAcc', 'score', 'accuracy', 'rank']);

  // A scripted party: human + easy/normal/hard CPUs, four rounds, Drumline
  // raced (field), the rest simulated.
  const s = st.session;
  s.setPlayers([
    { name: 'YOU', isCpu: false, cpuSkill: 0 },
    { name: 'ZIZZ', isCpu: true, cpuSkill: 0.40 },
    { name: 'KWARK', isCpu: true, cpuSkill: 0.63 },
    { name: 'TUFF', isCpu: true, cpuSkill: 0.86 },
  ]);
  s.startParty(['chomp-chorus', 'drumline-dash', 'bounce-brigade', 'finale-fever'], 4, 0x1234abcd);
  const human = [
    { score: 820, rank: 'B', accuracy: 0.86 },
    { score: 700, rank: 'A', accuracy: 0.93, field: [{ id: 0, place: 1 }, { id: 1, place: 3 }, { id: 2, place: 2 }, { id: 3, place: 0 }] },
    { score: 1000, rank: 'S', accuracy: 1 },
    { score: 450, rank: 'D', accuracy: 0.6 },
  ];
  const rows = [];
  human.forEach((h) => {
    const g = s.currentGame;
    s.recordPartyRound(g, h);
    const round = s.party.scores[s.party.scores.length - 1];
    for (const r of round.players) rows.push({ game: g, sim: round.sim, id: r.id, score: r.score, rank: r.rank, place: r.place, points: r.points });
  });
  save('party-session.txt', rows, ['game', 'sim', 'id', 'score', 'rank', 'place', 'points']);
  console.log('party totals', s.players.map((p) => `${p.id}:${p.points}/${p.wins}`).join(' '), 'winners', s.party.winners.join(','));
}
