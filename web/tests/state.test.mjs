/**
 * Shell state tests. `node --test web/tests/`
 *
 * `Save` (localStorage) throws in Node and falls back to in-memory defaults,
 * which is what makes `profile`/`session` importable here at all — no DOM
 * needed for the bookkeeping these two objects do.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import { profile, session, partyPlaylist, cpuRound, PLACE_POINTS } from '../src/shell/state.js';
import { makeRng } from '../src/core/util.js';

const IDS = ['swing-kings', 'chomp-chorus', 'drumline-dash', 'bounce-brigade', 'finale-fever'];

test('partyPlaylist() always ends a party on the finale, no repeats, seeded', () => {
  for (let seed = 1; seed < 40; seed++) {
    const list = partyPlaylist(IDS, 4, makeRng(seed));
    assert.equal(list.length, 4);
    assert.equal(list[3], 'finale-fever', `seed ${seed}: ${list.join(',')}`);
    assert.equal(new Set(list).size, 4, 'no game twice');
    assert.deepEqual(partyPlaylist(IDS, 4, makeRng(seed)), list, 'same seed, same party');
  }
  assert.deepEqual(partyPlaylist(IDS, 1, makeRng(3)), ['finale-fever']);
});

test('cpuRound(): skill decides accuracy, the human run sets the score scale', () => {
  const ref = { score: 10000, accuracy: 1 };
  const mean = (skill) => {
    let a = 0;
    for (let s = 1; s <= 200; s++) a += cpuRound(skill, makeRng(s), ref).accuracy;
    return a / 200;
  };
  assert.ok(mean(0.86) > mean(0.63) + 0.05 && mean(0.63) > mean(0.4) + 0.05, 'hard > normal > easy');
  const r = cpuRound(0.63, makeRng(7), ref);
  assert.ok(r.score > 0 && r.score < 10000, `score ${r.score} sits under a perfect human run`);
  assert.ok(['S', 'A', 'B', 'C', 'D'].includes(r.rank));
  assert.deepEqual(cpuRound(0.63, makeRng(7), ref), r, 'deterministic');
  // A zero-score human run still gives the CPUs a real score to post.
  assert.ok(cpuRound(0.63, makeRng(7), { score: 0, accuracy: 0 }).score > 0);
});

test('a party round places every player and hands out points; the party has a winner', () => {
  session.setPlayers([
    { name: 'P1', char: 'bopp', isCpu: false, cpuSkill: 0 },
    { name: 'ZIZZ', char: 'zizz', isCpu: true, cpuSkill: 0.4 },
    { name: 'KWARK', char: 'kwark', isCpu: true, cpuSkill: 0.4 },
  ]);
  session.startParty(['swing-kings', 'finale-fever'], 2, 12345);
  session.recordPartyRound('swing-kings', { score: 10000, accuracy: 1, rank: 'S' });
  const round = session.party.scores[0];
  assert.equal(round.players.length, 3);
  assert.deepEqual(round.players.map((p) => p.place).sort(), [1, 2, 3]);
  const human = round.players.find((p) => p.id === 0);
  assert.equal(human.score, 10000, 'the human posts their real score');
  assert.equal(human.place, 1, 'a perfect run beats two easy CPUs');
  assert.equal(session.players[0].points, PLACE_POINTS[0]);
  const total = session.players.reduce((a, p) => a + p.points, 0);
  assert.equal(total, PLACE_POINTS[0] + PLACE_POINTS[1] + PLACE_POINTS[2]);

  session.recordPartyRound('finale-fever', { score: 8000, accuracy: 0.95, rank: 'A' });
  assert.equal(session.partyDone, true);
  assert.equal(session.standings()[0].id, session.party.winner, 'the winner is the standings leader');
  session.endParty();
});

test('profile.submit() keeps the better score and reports newScore/newRank', () => {
  profile.reset();
  const r1 = profile.submit('swing-kings', { score: 100, rank: 'C', accuracy: 0.5, stats: { maxCombo: 3 } });
  assert.equal(r1.newScore, true);
  assert.equal(r1.newRank, true);
  assert.equal(profile.record('swing-kings').score, 100);

  const r2 = profile.submit('swing-kings', { score: 50, rank: 'D', accuracy: 0.2, stats: { maxCombo: 1 } });
  assert.equal(r2.newScore, false, 'a worse run must not overwrite the best score');
  assert.equal(profile.record('swing-kings').score, 100, 'best score is retained across a worse run');
  assert.equal(profile.record('swing-kings').rank, 'C', 'best rank is retained across a worse run');

  const r3 = profile.submit('swing-kings', { score: 200, rank: 'A', accuracy: 0.95, stats: { maxCombo: 30 } });
  assert.equal(r3.newScore, true);
  assert.equal(r3.newRank, true);
  assert.equal(profile.record('swing-kings').plays, 3);
});

test('session.recordPartyRound() folds a result into party.scores and advances the index', () => {
  session.startParty(['swing-kings', 'chomp-chorus'], 2);
  assert.equal(session.currentGame, 'swing-kings');
  assert.equal(session.partyDone, false);

  session.recordPartyRound('swing-kings', { score: 900, rank: 'B' });
  assert.equal(session.party.scores.length, 1);
  const r0 = session.party.scores[0];
  assert.deepEqual([r0.game, r0.score, r0.rank], ['swing-kings', 900, 'B']);
  assert.equal(session.currentGame, 'chomp-chorus');
  assert.equal(session.partyDone, false);

  session.recordPartyRound('chomp-chorus', { score: 700, rank: 'C' });
  assert.equal(session.party.scores.length, 2);
  assert.equal(session.partyDone, true);

  session.endParty();
});

test('recordPartyRound() is a no-op outside a party', () => {
  session.endParty();
  assert.equal(session.party, null);
  session.recordPartyRound('swing-kings', { score: 1, rank: 'D' });
  assert.equal(session.party, null, 'no party in progress, nothing to record');
});
