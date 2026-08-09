/**
 * Shell state tests. `node --test web/tests/`
 *
 * `Save` (localStorage) throws in Node and falls back to in-memory defaults,
 * which is what makes `profile`/`session` importable here at all — no DOM
 * needed for the bookkeeping these two objects do.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import { profile, session } from '../src/shell/state.js';

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
  assert.deepEqual(session.party.scores[0], { game: 'swing-kings', score: 900, rank: 'B' });
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
