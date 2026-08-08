/**
 * PLACEHOLDER minigame factory.  DELETE THIS once every game is real.
 *
 * Exists only so the app boots, the registry resolves, and the critic harness
 * has something to route to before the real games land. It is a metronome with
 * a cube. It is not a game. Replace it.
 */

import * as THREE from 'three';
import { NoteJudge, rankFor } from '../core/judge.js';
import { FEEL, feelFor } from '../core/feel.js';
import { damp, backOut, clamp01 } from '../core/util.js';

export function makeStub({ id, name, blurb, bpm = 120, bars = 16, color = 0xffd93d }) {
  let cube, judge, root, done = false, pop = 0;

  return {
    id, name, blurb, bpm, durationBars: bars, controls: 'a', placeholder: true,

    load(ctx) {
      root = new THREE.Group();
      ctx.scene.add(root);
      ctx.scene.background = new THREE.Color(0x0b0a1a);
      ctx.scene.add(new THREE.HemisphereLight(0x88aaff, 0x221133, 1.4));
      const key = new THREE.DirectionalLight(0xffffff, 2.0);
      key.position.set(3, 6, 5);
      ctx.scene.add(key);

      cube = new THREE.Mesh(
        new THREE.BoxGeometry(1.4, 1.4, 1.4),
        new THREE.MeshStandardMaterial({ color, roughness: 0.4, flatShading: true })
      );
      root.add(cube);

      const floor = new THREE.Mesh(
        new THREE.PlaneGeometry(40, 40),
        new THREE.MeshStandardMaterial({ color: 0x171634, roughness: 1 })
      );
      floor.rotation.x = -Math.PI / 2;
      floor.position.y = -1.2;
      root.add(floor);

      ctx.fx.attach(ctx.scene);
      ctx.camera.position.set(0, 1.4, 6.5);
      ctx.camera.lookAt(0, 0.2, 0);
      ctx.ui.hud.mount();

      judge = new NoteJudge();
      done = false;
    },

    start(ctx) {
      ctx.clock.setBpm(bpm);
      ctx.clock.start(ctx.clock.now() + 0.4, -FEEL.leadInBars * 4);

      const notes = [];
      for (let b = 0; b < bars * 4; b += 2) notes.push({ time: ctx.clock.timeAt(b), action: 'a' });
      judge.load(notes);
      judge.onJudged = (note, verdict, errMs) => {
        const f = feelFor(verdict);
        ctx.audio.sfx(verdict);
        ctx.stage.shake(f.shake, [1, 0.3, 0]);
        ctx.stage.flash(f.flash, '#' + f.color.toString(16).padStart(6, '0'));
        ctx.hitstop(f.hitstop);
        // No ui.popup() here. fx bridges the `judge` bus event into a full
        // layered response including an in-world callout at the hit position,
        // and a second DOM callout dead-centre both duplicated the word and
        // covered the thing the player is trying to watch. One callout, and it
        // lives where the eye already is.
        if (verdict !== 'miss') ctx.fx.burst([0, 0.4, 0], { color: f.color, count: 16 });
        ctx.bus.emit('judge', { verdict, errMs, beat: ctx.clock.beatAt(note.time) });
        ctx.ui.hud.setScore(judge.stats.score);
        ctx.ui.hud.setCombo(judge.stats.combo);
        ctx.ui.hud.setAccuracy(judge.accuracy);
        pop = 1;
      };

      ctx.clock.onBeat((b, t) => {
        if (b < 0) ctx.audio.sfx('count', t);
        else ctx.audio.voices.kick(t);
      });
      ctx.ui.banner(name, { sub: blurb, life: 1.4 });
    },

    update(ctx, dt, beat) {
      judge.update(ctx.clock.now());
      pop = damp(pop, 0, 9, dt);
      const bounce = Math.abs(Math.sin(beat * Math.PI)) * 0.18;
      cube.position.y = bounce + pop * 0.5;
      cube.rotation.y += dt * 0.8;
      cube.scale.setScalar(1 + pop * 0.35);
      if (!done && judge.finished && beat > bars * 4) { done = true; }
    },

    input(ctx, events) {
      for (const e of events) {
        if (!e.down || e.action !== 'a') continue;
        const r = judge.press('a', e.time);
        if (!r) ctx.audio.sfx('tick');
      }
    },

    result() {
      if (!done) return null;
      const s = judge.stats;
      return { score: s.score, accuracy: judge.accuracy, rank: rankFor(judge.accuracy, s.miss), stats: s };
    },

    dispose(ctx) {
      root?.traverse((o) => { o.geometry?.dispose?.(); o.material?.dispose?.(); });
      root = null;
      ctx.ui.hud.unmount();
    },
  };
}
