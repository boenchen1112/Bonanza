/**
 * Music player tests: the cursor must follow the transport, including when a
 * scene restarts it. Every shell scene calls `clock.start(now, 0)`; the menu
 * track keeps playing across that, and a cursor still counting the old
 * transport's beats went silent until the new clock caught up (16s of dead
 * air on the party hub).
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import { Clock } from '../src/core/clock.js';
import { createMusicPlayer } from '../src/audio/player.js';

function rig() {
  const param = () => ({
    value: 0,
    cancelScheduledValues() {}, setValueAtTime() {}, linearRampToValueAtTime() {},
    exponentialRampToValueAtTime() {},
  });
  const ctx = { currentTime: 0, createGain: () => ({ gain: param(), connect() {} }) };
  const clock = new Clock(ctx);
  const kicks = [];
  const track = {
    id: 'menu', bpm: 120, loop: true, bars: 4, root: 60, scale: 'major',
    layers: [{ voice: 'kick', grid: 'x.x.x.x.', gain: 0.5 }],   // a kick on every beat
  };
  const player = createMusicPlayer({
    ctx, clock, voices: { kick: (t) => kicks.push(t) },
    buses: { music: {} }, sends: null, tracks: { menu: track },
  });
  /** Advance audio time, pumping at ~60fps like the frame loop. */
  const run = (secs) => {
    const end = ctx.currentTime + secs;
    while (ctx.currentTime < end) { ctx.currentTime += 1 / 60; player.pump(); }
  };
  return { ctx, clock, player, kicks, run };
}

const onGrid = (clock, t) => Math.abs(clock.beatAt(t) - Math.round(clock.beatAt(t))) < 1e-6;

test('a transport restart keeps the music going on the new grid', () => {
  const { ctx, clock, player, kicks, run } = rig();
  clock.setBpm(120);
  clock.start(0.1, 0);
  player.play('menu');
  run(20);                                   // ~40 beats on the first screen

  // The next scene starts the transport over at beat 0.
  clock.stop();
  const restartAt = ctx.currentTime;
  clock.start(restartAt + 0.12, 0);
  kicks.length = 0;
  run(3);

  const after = kicks.filter((t) => t > restartAt + 0.05);
  assert.ok(after.length >= 5, `music must keep playing after a restart (got ${after.length} kicks in 3s)`);
  assert.ok(after[0] - restartAt < 0.5, `first kick after the restart ${(after[0] - restartAt).toFixed(2)}s late`);
  for (const t of after) assert.ok(onGrid(clock, t), `kick at beat ${clock.beatAt(t)} is off the new grid`);
});

test('a restart at beat 0 after a short screen still plays at once', () => {
  // Title → roster inside ~1.5s: the old cursor sat only a few beats ahead,
  // close enough in time to look like a continuation — a bar of dead air.
  const { ctx, clock, player, kicks, run } = rig();
  clock.setBpm(120);
  clock.start(0.1, 0);
  player.play('menu');
  run(1.5);
  clock.stop();
  const restartAt = ctx.currentTime;
  clock.start(restartAt + 0.12, 0);
  kicks.length = 0;
  run(2);
  const after = kicks.filter((t) => t > restartAt + 0.05);
  assert.ok(after.length >= 3, `got ${after.length} kicks in 2s`);
  assert.ok(after[0] - restartAt < 0.7, `first kick after the restart ${(after[0] - restartAt).toFixed(2)}s late`);
});

test('pause() / resume() across a pause-menu restart picks the track up where it stopped', () => {
  const { ctx, clock, player, kicks, run } = rig();
  clock.setBpm(120);
  clock.start(0.1, 0);
  player.play('menu');
  run(6);
  // pause: the transport stops at a beat; resume counts in 3 beats and restarts at that beat
  const pauseBeat = clock.beat;
  player.pause();
  clock.stop();
  run(2);
  const resumeAt = ctx.currentTime + 3 * 0.5;
  clock.start(resumeAt, pauseBeat);
  player.resume();
  kicks.length = 0;
  run(4);
  const beats = kicks.filter((t) => t >= resumeAt - 1e-6).map((t) => clock.beatAt(t));
  assert.ok(beats.length >= 5, `music is back after resume (got ${beats.length} kicks)`);
  assert.ok(beats[0] - pauseBeat < 1.01, `first kick at beat ${beats[0].toFixed(2)}, paused at ${pauseBeat.toFixed(2)}: it skipped ahead`);
  for (const b of beats) assert.ok(Math.abs(b - Math.round(b)) < 1e-6, `kick at beat ${b} is off the grid`);
});

test('a restart that continues the beat count (pause/resume) keeps the cursor', () => {
  const { ctx, clock, player, kicks, run } = rig();
  clock.setBpm(120);
  clock.start(0.1, 0);
  player.play('menu');
  run(10);
  const beat = clock.beat;
  const shiftAt = ctx.currentTime + 0.2;
  clock.start(shiftAt, beat + 0.4);          // same beat count, origin moved
  kicks.length = 0;
  run(3);
  const beats = kicks.filter((t) => t >= shiftAt).map((t) => Math.round(clock.beatAt(t)));
  assert.ok(beats.length >= 5, 'music keeps playing');
  for (let i = 1; i < beats.length; i++) assert.equal(beats[i] - beats[i - 1], 1, `beats ${beats.join(',')} skip or repeat`);
});
