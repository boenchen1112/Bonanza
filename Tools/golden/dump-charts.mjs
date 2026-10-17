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
  // The web registry order (shell/registry.js GAMES); GameHost.Games and the shuffle follow it.
  const IDS = ['swing-kings', 'drumline-dash', 'bounce-brigade', 'chomp-chorus', 'finale-fever'];
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

// ---- Shell flow (shell/nav.js, shell/title.js bestOverall, shell/theme.js fmtScore, shell/party.js headline)
{
  const nav = await load('shell/nav.js');
  const st = await load('shell/state.js');
  const enc = (v) => String(v).replaceAll(' ', '~');   // cells are space-separated
  const o = (opts) => ['mode', 'game', 'from'].map((k) => (opts && opts[k] !== undefined ? `${k}:${opts[k]}` : null)).filter(Boolean).join(',') || '-';
  const routes = [];
  const add = (fn, args, r) => routes.push({ fn, args: args.map((a) => (a === undefined ? '-' : a)).join(','), view: r.view, opts: o(r.opts) });
  for (const id of ['party', 'free', 'options']) add('titleMenuRoute', [id], nav.titleMenuRoute(id));
  for (const m of ['party', 'free']) add('rosterExitRoute', [m], nav.rosterExitRoute(m));
  for (const inParty of [true, false]) {
    for (const from of ['freeplay', 'select', 'party', undefined]) {
      for (const gameId of ['swing-kings', undefined]) {
        add('exitRoute', [inParty ? 1 : 0, from, gameId], nav.exitRoute({ inParty, from, gameId }));
      }
    }
  }
  for (const done of [false, true]) add('partyConfirmRoute', [done ? 1 : 0, done ? undefined : 'chomp-chorus'], nav.partyConfirmRoute({ done, gameId: done ? undefined : 'chomp-chorus' }));
  save('shell-routes.txt', routes, ['fn', 'args', 'view', 'opts']);

  // title.js bestOverall, verbatim, over scripted records (CATALOG order = registry order).
  const bestOverall = (records, ids) => {
    const order = ['S', 'A', 'B', 'C', 'D'];
    const rankIdx = (r) => (order.includes(r) ? order.indexOf(r) : order.length);
    let best = null;
    for (const id of ids) {
      const r = records[id] || { score: 0, rank: null, accuracy: 0, plays: 0 };
      if (!r.plays) continue;
      const cand = { name: id, score: r.score, rank: r.rank || '-', acc: r.accuracy || 0 };
      if (!best || rankIdx(cand.rank) < rankIdx(best.rank)
        || (rankIdx(cand.rank) === rankIdx(best.rank) && cand.acc > best.acc)) best = cand;
    }
    return best;
  };
  const IDS2 = ['swing-kings', 'drumline-dash', 'bounce-brigade', 'chomp-chorus', 'finale-fever'];
  const R = (score, rank, accuracy, plays) => ({ score, rank, accuracy, plays });
  const cases = [
    {},
    { 'swing-kings': R(1234567, 'D', 0.5, 3) },
    { 'swing-kings': R(99000, 'D', 0.9, 2), 'chomp-chorus': R(400, 'B', 0.7, 1) },
    { 'swing-kings': R(5000, 'A', 0.91, 1), 'bounce-brigade': R(700, 'A', 0.95, 4), 'finale-fever': R(300, 'A', 0.95, 2) },
    { 'drumline-dash': R(0, null, 0, 1), 'finale-fever': R(10, 'C', 0.4, 1) },
    { 'swing-kings': R(12, 'S', 1, 1), 'drumline-dash': R(10, 'S', 1, 1) },
    { 'chomp-chorus': R(999, 'S', 0.99, 9), 'drumline-dash': R(5, 'S', 0.999, 1) },
    { 'swing-kings': R(500, 'B', 0.8, 0), 'finale-fever': R(800, 'C', 0.6, 2) },
  ];
  // fmtScore from theme.js (imports three, so copied verbatim).
  const fmtScore = (n) => { n = Math.max(0, Math.round(n || 0)); const s = String(n); let out = ''; for (let i = 0; i < s.length; i++) { if (i > 0 && (s.length - i) % 3 === 0) out += ','; out += s[i]; } return out; };
  const best = cases.map((c, i) => {
    const b = bestOverall(c, IDS2);
    const n = Object.values(c).reduce((a, r) => a + r.plays, 0);
    return { case: i, rounds: n, best: b ? b.name : '-', rank: b ? b.rank : '-', line1: enc(`${n} ROUND${n === 1 ? '' : 'S'} PLAYED`),
      line2: enc(b ? `BEST: ${b.name} - ${b.rank} - ${fmtScore(b.score)}` : 'NO RECORDS YET') };
  });
  save('shell-best.txt', best, ['case', 'rounds', 'best', 'rank', 'line1', 'line2']);
  save('shell-score-format.txt', [0, 7, 999, 1000, 12345, 1234567, 100000].map((n) => ({ n, text: fmtScore(n) })), ['n', 'text']);

  // party.js wrap-up headline over scripted standings, through the web session's own standings code.
  const s = st.session;
  const heads = [];
  const lines = [
    [[8, 1], [0, 0], [5, 0], [11, 3]],
    [[6, 2], [6, 1], [3, 0], [1, 0]],
    [[6, 2], [6, 2], [3, 0], [1, 0]],
    [[4, 1], [4, 1], [4, 1], [0, 0]],
    [[9, 3], [9, 3], [9, 3], [9, 3]],
    [[3, 1], [2, 0]],
    [[5, 2], [5, 1]],
  ];
  const names = ['YOU', 'ZIZZ', 'KWARK', 'TUFF'];
  lines.forEach((pl, i) => {
    s.setPlayers(pl.map((_, k) => ({ name: names[k], isCpu: k > 0, cpuSkill: 0.5 })));
    s.players.forEach((p, k) => { p.points = pl[k][0]; p.wins = pl[k][1]; });
    const standings = s.standings();
    const places = s.standingPlaces();
    const wins = standings.filter((p) => places.get(p.id) === 1);
    const win = wins[0] || standings[0];
    const title = wins.length > 1 ? `${wins.map((w) => w.name).join(' & ')} SHARE THE CROWN!` : `${win?.name || '???'} WINS!`;
    const second = standings[1];
    const note = wins.length === 1 && second && second.points === win.points
      ? `Level on ${win.points} points - ${win.wins} round wins to ${second.wins} breaks the tie` : '-';
    heads.push({ case: i, seats: pl.map((x) => x.join('/')).join(','), title: enc(title), note: enc(note) });
  });
  save('party-headline.txt', heads, ['case', 'seats', 'title', 'note']);
}

// ---- Swing Kings ictus detector (games/swingKings/gesture.js): every feed of a set of traces, with the event
// and the detector state after it. `op` is feed or reset. Trace shapes mirror web/tests/gesture.test.mjs, plus
// irregular pointer timestamps and a conduct with a slow stroke.
{
  const g = await load('games/swingKings/gesture.js');
  const FPS = 60;
  const conduct = ({ lift = 0.25, strike = 0.12, strikeAt = 1.0, hold = 0.3 } = {}) => {
    const low = 0.7, high = low - lift;
    return (t) => {
      if (t < 0.3) return low;
      if (t < 0.7) return low - lift * ((t - 0.3) / 0.4);
      if (t < strikeAt) return high;
      if (t < strikeAt + strike) { const u = (t - strikeAt) / strike; return high + lift * u * u; }
      return low;
    };
  };
  let ls = 7;
  const rnd = () => ((ls = (ls * 1664525 + 1013904223) >>> 0) / 4294967296);
  const grid = (secs) => Array.from({ length: Math.floor(secs * FPS) + 1 }, (_, i) => i / FPS);
  // A mouse reports at uneven intervals (4-20 ms): times from a seeded generator.
  const irregular = (secs) => { const ts = []; let t = 0; while (t <= secs) { ts.push(t); t += 0.004 + rnd() * 0.016; } return ts; };
  const a = conduct(), b = conduct({ strikeAt: 1.0 });
  const scenarios = [
    { name: 'conduct', ts: grid(1.6), y: conduct() },
    { name: 'conduct-slow', ts: grid(2.2), y: conduct({ strike: 0.3 }) },
    { name: 'conduct-small-lift', ts: grid(1.6), y: conduct({ lift: 0.07 }) },
    { name: 'drift', ts: grid(6), y: (t) => 0.6 + 0.1 * Math.sin(t * 1.2) },
    { name: 'jitter', ts: grid(4), y: () => 0.5 + (rnd() - 0.5) * 0.006 },
    { name: 'no-lift', ts: grid(1.5), y: (t) => (t < 1 ? 0.3 : t < 1.12 ? 0.3 + 0.3 * ((t - 1) / 0.12) ** 2 : 0.6) },
    { name: 'two-strokes', ts: grid(3.2), y: (t) => (t < 1.5 ? a(t) : b(t - 1.5)) },
    { name: 'quick-second', ts: grid(2.6), y: (t) => (t < 1.3 ? a(t) : b(t - 1.3)) },
    { name: 'irregular', ts: irregular(1.6), y: conduct() },
    { name: 'irregular-two', ts: irregular(3.2), y: (t) => (t < 1.5 ? a(t) : b(t - 1.5)) },
    { name: 'reset-midway', ts: grid(1.6), y: conduct(), resetAt: Math.round(0.9 * FPS) },
  ];
  const rows = [];
  for (const sc of scenarios) {
    const d = g.createIctusDetector();
    sc.ts.forEach((t, i) => {
      if (sc.resetAt === i) { d.reset(); rows.push({ scenario: sc.name, op: 'reset', t: 0, y: 0, ev: '-', evT: '-', evY: '-', state: d.state }); }
      const y = sc.y(t);
      const e = d.feed(t, y);
      rows.push({ scenario: sc.name, op: 'feed', t, y, ev: e ? e.type : '-', evT: e ? e.time : '-', evY: e ? e.y : '-', state: d.state });
    });
  }
  // Tunable parameters: a stricter detector on the plain conduct.
  {
    const d = g.createIctusDetector({ riseMin: 0.2, vDown: 2, minStroke: 0.2, decel: 0.5, refractory: 0.4 });
    const y = conduct();
    for (const t of grid(1.6)) {
      const e = d.feed(t, y(t));
      rows.push({ scenario: 'strict', op: 'feed', t, y: y(t), ev: e ? e.type : '-', evT: e ? e.time : '-', evY: e ? e.y : '-', state: d.state });
    }
  }
  save('swing-ictus.txt', rows, ['scenario', 'op', 't', 'y', 'ev', 'evT', 'evY', 'state']);

  // The pointer wrapper in index.js: a still pointer is fed its resting position one frame after its last move
  // once 40 ms have passed. Steps: m = pointer move at (t,y), u = update at `now`; result = detector events.
  const mouseRows = [];
  {
    const d = g.createIctusDetector();
    let pointer = null;
    const sample = (t, y) => d.feed(t, y);
    const fmt = (e) => (e ? e : null);
    const log = (op, now, t, y, e) => mouseRows.push({ op, now, t, y, ev: e ? e.type : '-', evT: e ? e.time : '-' });
    d.reset();
    // a conduct played as sparse mouse moves: the pointer stops dead at the bottom, no more events
    const y = conduct();
    const ts = []; { let t = 0.3; while (t < 1.2) { ts.push(t); t += 0.008 + rnd() * 0.01; } }
    let now = 0;
    for (const t of ts) {
      if (t > 1.0 + 0.12) break;
      const yy = y(t);
      pointer = { t, y: yy };
      const e = sample(t, yy);
      log('m', t, t, yy, e);
    }
    // ...then the frames go by without events
    for (let n = 0; n < 12; n++) {
      now = pointer.t + (n + 1) / 60;
      let e = null;
      if (pointer && now - pointer.t > 0.04) {
        const tStill = pointer.t + 1 / 60;
        pointer.t = tStill;
        e = sample(tStill, pointer.y);
      }
      log('u', now, pointer.t, pointer.y, e);
    }
  }
  save('swing-ictus-pointer.txt', mouseRows, ['op', 'now', 't', 'y', 'ev', 'evT']);
}
