/**
 * G1 · Swing Kings — "One swing. One beat. Send it."
 * 124 bpm · 4/4 · one button, held and released.
 *
 * ─────────────────────────────────────────────────────────────────────────
 * WHY THE BALL IS THE METRONOME
 * ─────────────────────────────────────────────────────────────────────────
 * A pitching machine lobs a ball on a parabola timed so it crosses the plate
 * EXACTLY on its target beat, launched two beats early. Its apex is therefore
 * one beat before contact, and half-beat markers are drawn along the flight
 * path itself. The tempo is not something you hear and then map onto the
 * screen — it is the screen. Mute the game and it is still playable, which is
 * what qualifies this as the series' tutorial.
 *
 * ─────────────────────────────────────────────────────────────────────────
 * WHY YOU DON'T TAP
 * ─────────────────────────────────────────────────────────────────────────
 * Hold to wind up, release on the ictus. Two axes that never talk to each
 * other:
 *
 *   release timing  -> perfect / great / good / whiff   (core/judge.js)
 *   windup length   -> bunt / line drive / home run     (rules.js)
 *
 * A perfectly-timed bare tap is a BUNT: full marks for timing, a dribbler for
 * a result. Nobody has to be told the hold matters; they get told by the ball.
 * The windup draws as a conducting trace (trace.js) so the gesture has a
 * visible SHAPE, and the shape gets better as the player does.
 *
 * ─────────────────────────────────────────────────────────────────────────
 * NOTES FOR THE NEXT PERSON
 * ─────────────────────────────────────────────────────────────────────────
 * - A second press while already winding up releases the first swing and
 *   starts the next. That is not a hack for the autoplay bot; it is what
 *   makes rapid alternating pitches playable on a keyboard, and it means a
 *   press ALWAYS does something visible on the frame it arrives.
 * - `fx.verdict()` is called directly, so the `judge`-bus auto-bridge in
 *   render/fx disarms. The bus event is still emitted (telemetry needs it,
 *   and audio's adaptive layers listen to it) but there is exactly ONE
 *   verdict callout, and it lives at the contact point.
 * - Beat phase is `x - Math.floor(x)` everywhere. The transport runs negative
 *   beats through the lead-in and `%` keeps the dividend's sign.
 */

import * as THREE from 'three';
import { NoteJudge, rankFor, WINDOWS_MS } from '../../core/judge.js';
import { FEEL, feelForCombo } from '../../core/feel.js';
import { clamp01, damp, lerp, smoothstep } from '../../core/util.js';
import { createWorld, LAYOUT } from './world.js';
import { createTrace } from './trace.js';
import { CLIPS } from '../../chars/index.js';
import {
  buildSchedule, powerFor, tierFor, scoreFor, sectionAt,
  END_BEAT, FINALE_BEAT, LEAD_IN_BEATS, SCORED_BARS, TIERS,
} from './rules.js';

const BPM = 124;
const MUZZLE = [-6.38, 1.91, -0.25];
const GRAV = 15.5;

/**
 * The batter's body is Mixamo's "Baseball Hit", retargeted onto the toy rig
 * (chars/retarget.js). It is split at the bat-meets-ball frame: the COIL
 * (stance -> stride -> loaded) plays while the ball is in the air, retimed so
 * it finishes just before the ball arrives, and the press fires the STRIKE
 * (contact -> follow-through). So the body telegraphs the beat the way the
 * pips do, and the swing still happens on the frame the press lands.
 */
const SWING = CLIPS.swing;
const LOAD = SWING.contact - 0.06;
/** Most beats of coil — a finale ball 8 beats out should not coil in slow-mo. */
const COIL_BEATS = 2;

// scratch
const _v = new THREE.Vector3();
const _dir = new THREE.Vector3();

/**
 * Timing -> power. Full power inside the PERFECT window, falling to zero at
 * the edge of what the judge will still claim, with a smooth knee so the tier
 * boundary never feels like a cliff.
 */
function powerFromTiming(time, pitch, clock) {
  // Pitches carry their beat; the audio time lives on the judged note. (This
  // read `pitch.time`, which does not exist: power was NaN on every press.)
  const target = pitch.note ? pitch.note.time : clock.timeAt(pitch.targetBeat);
  const err = Math.abs(time - target) * 1000;
  const full = WINDOWS_MS.perfect;
  const zero = WINDOWS_MS.good;
  if (err <= full) return 1;
  if (err >= zero) return 0;
  return smoothstep(1 - (err - full) / (zero - full));
}

export default {
  id: 'swing-kings',
  name: 'Swing Kings',
  blurb: 'One swing. One beat. Send it.',
  bpm: BPM,
  durationBars: SCORED_BARS,
  controls: 'a',

  // -------------------------------------------------------------- lifecycle

  load(ctx) {
    ctx.scene.userData.palette = 'swing-kings';
    ctx.stage.setPalette('swing-kings');

    this.w = createWorld(ctx);
    this.trace = createTrace();
    this.w.root.add(this.trace.mesh);

    ctx.fx.attach(ctx.scene);
    ctx.fx.setAutoVerdict(false);      // we compose our own; no double callout
    ctx.fx.setGroundY(0);
    ctx.fx.setFocus([-1.6, 1.6, 0]);

    ctx.stage.rig.frame({
      target: [-1.9, 1.95, 0], distance: 12.6, height: 2.15, yaw: 0.055,
      fov: 45, lambda: 2.4, immediate: true,
    });

    ctx.ui.hud.mount();

    this.pitches = buildSchedule();
    this.judge = new NoteJudge();
    this.hitBalls = [];
    this.reset();

    // Debug/verification hook. The shared harness bot can only emit key-DOWN
    // events, so it can never perform a hold-and-release; this lets a script
    // drive the real gesture at exact audio times. See verify.mjs.
    if (typeof window !== 'undefined' && window.__BBB__) {
      const self = this;
      window.__BBB__.swing = {
        hold: (atBeat) => self.input(ctx, [{
          action: 'a', time: ctx.clock.timeAt(atBeat), down: true, source: 'test',
        }]),
        release: (atBeat) => self.input(ctx, [{
          action: 'a', time: ctx.clock.timeAt(atBeat), down: false, source: 'test',
        }]),
        stats: () => ({
          swings: self.swings.slice(-40),
          outs: self.outs,
          score: self.gotPts,
          combo: self.judge.stats.combo,
        }),
      };
    }
  },

  reset() {
    this.holding = false;
    this.holdStart = 0;
    this.holdBeats = 0;
    this.pendingHold = 0;
    this.pendingPower = 0;
    this.pendingReact = null;
    this.traceHoldOff = 0;
    this._outsClear = 0;
    this.outs = 0;
    this.gotPts = 0;
    this.maxPts = 0;
    this.tally = { bunt: 0, liner: 0, homer: 0, slam: 0, foul: 0 };
    this.powerSum = 0;
    this.powerN = 0;
    this.swings = [];
    this.over = false;
    this.finaleDone = false;
    this.lastBeat = -1e9;
    this.camPush = 0;
    this.strikeUntil = -Infinity;
  },

  start(ctx) {
    const clock = ctx.clock;
    clock.setBpm(BPM);
    clock.start(clock.now() + 0.5, -LEAD_IN_BEATS);

    // --- notes ------------------------------------------------------------
    const notes = [];
    for (const p of this.pitches) {
      if (!p.scored) continue;
      const n = { time: clock.timeAt(p.targetBeat), action: 'a', data: p };
      p.note = n;
      notes.push(n);
    }
    this.judge.load(notes);
    this.judge.onJudged = (note, verdict, errMs) => this.onJudged(ctx, note, verdict, errMs);

    // --- lead-in click + crowd -------------------------------------------
    this._offBeat = clock.onBeat((b, t) => {
      if (b < 0) {
        ctx.audio.sfx('count', t, ((b % 4) + 4) % 4);
        const n = -b;
        if (n <= 4) ctx.ui.popup(String(n), { y: 0.30, scale: 1.5, color: '#ffe58a' });
      }
      if (b === 0) ctx.ui.banner('PLAY BALL!', { life: 1.0, color: '#ffe58a' });
    });

    ctx.audio.music.play('swing-kings');
    ctx.ui.banner('SWING KINGS', {
      sub: 'HOLD to wind up · RELEASE as the ball lands', life: 2.4, color: '#ffe58a',
    });
    ctx.ui.hud.setScore(0);
    ctx.ui.hud.setAccuracy(1);
  },

  // ------------------------------------------------------------------ input

  /**
   * TAP MODE.
   *
   * The hold-and-release conducting gesture is deferred; for now one press is
   * one swing. That is not a downgrade of the design so much as a different
   * split of it: power is no longer a second axis the player controls
   * separately, it is earned by TIMING. Nail the downbeat and you send it;
   * clip the edge of the window and you bunt it. The tier ladder, the ball
   * physics, the trace and the crowd all survive unchanged — only the source
   * of `power` moves.
   *
   * The practical win is that the standard harness bot, which emits key-down
   * only, can now play this game properly, so it is verifiable by the same
   * path as everything else instead of needing a bespoke script.
   */
  input(ctx, events) {
    for (const e of events) {
      if (e.action !== 'a' || !e.down) continue;
      // Begin and release on the same timestamp: the windup still runs as an
      // animation (the batter must not teleport into the follow-through), but
      // it costs the player no input.
      this.beginWindup(ctx, e.time);
      this.releaseSwing(ctx, e.time);
    }
  },

  // ------------------------------------------------------------ batter body

  /** Coil during the ball's flight, landing loaded just before `untilTime`. */
  batterCoil(ctx, untilTime) {
    const dur = Math.max(0.16, untilTime - ctx.clock.now() - 0.03);
    this.w.batter.anim.play('swing', {
      to: LOAD, dur, hold: true, face: 'focus', beat: 0.12, blend: 0.14,
    });
    this.pendingReact = null;   // the next pitch outranks the last verdict
  },

  /** Contact -> follow-through, from the loaded frame. */
  batterStrike(ctx, power) {
    power = Number.isFinite(power) ? clamp01(power) : 0.5;
    const rate = 1.15 + power * 0.55;
    const anim = this.w.batter.anim;
    anim.play('swing', {
      from: LOAD, rate, face: 'fierce', beat: 0.05, blend: 0.03, power, next: 'idle',
    });
    anim.impulse(0.45 * power, 0.02 * power);
    // A coil for the next pitch must not cut the follow-through off at the
    // knees; it waits for most of it.
    this.strikeUntil = ctx.clock.now() + ((SWING.duration - LOAD) / rate) * 0.72;
  },

  beginWindup(ctx, time) {
    this.holding = true;
    this.holdStart = time;
    this.holdBeats = 0;
    this.w.batTip.getWorldPosition(_v);
    this.trace.begin(_v.x, _v.y, _v.z);
    this.traceHoldOff = 0;
    ctx.audio.sfx('tick', Math.max(time, ctx.clock.rawNow()), 0.05);
  },

  releaseSwing(ctx, time) {
    if (!this.holding) return;
    this.holding = false;
    const live = this.livePitch();
    const holdBeats = Math.max(0, (time - this.holdStart) / ctx.clock.spb);
    this.pendingHold = holdBeats;
    // In tap mode holdBeats is always ~0, so powerFor() would return a flat
    // zero and every hit would be a bunt. Derive power from how close the
    // press is to the pitch's contact beat instead: dead-on is full power,
    // the edge of the claim window is none.
    // A real hold (the conducting gesture) keeps the original design: power
    // is the windup length. A tap has no windup to measure, so its power is
    // earned by timing instead.
    const held = holdBeats > 0.1;
    this.pendingPower = held
      ? powerFor(holdBeats, live ? live.ideal : 2)
      : live ? powerFromTiming(time, live, ctx.clock) : 0;
    // Keep drawing for a quarter-second: the release stroke is part of the
    // gesture, and cutting the ribbon at the button-up loses the follow-through.
    this.traceHoldOff = 0.26;

    // `press` calls onJudged synchronously when it claims a note.
    const r = this.judge.press('a', time);

    if (!r) {
      // Swung at nothing. Still swing — an input with no visible consequence
      // reads as a dropped input — but it costs nothing.
      if (!this.demoHit(ctx, time, holdBeats)) {
        this.batterStrike(ctx, 0.3 + this.pendingPower * 0.4);
        ctx.audio.sfx('swoosh', ctx.clock.rawNow());
      }
    }
  },

  /** The lead-in demonstration swings itself through the real code path. */
  demoHit(ctx, time, holdBeats) {
    const d = this.pitches[0];
    if (!d || d.kind !== 'demo' || d.done || !d.live) return false;
    const beat = ctx.clock.beatAt(time);
    if (Math.abs(beat - d.targetBeat) > 0.45) return false;
    const power = Math.max(0.8, powerFor(holdBeats, d.ideal));
    this.batterStrike(ctx, 1);
    this.connect(ctx, d, 'perfect', power, TIERS.homer, false, true);
    d.done = true;
    return true;
  },

  // -------------------------------------------------------------- judgement

  onJudged(ctx, note, verdict, errMs) {
    const p = note.data;
    const hit = verdict !== 'miss';
    // `pendingPower` is what releaseSwing derived for THIS press (from timing
    // in tap mode). Recomputing from `pendingHold` here — always ~0 on a tap —
    // made every hit a BUNT: the game never showed a home run.
    const power = hit ? this.pendingPower : 0;
    const tier = tierFor(power, p.finale);
    const foul = hit && verdict === 'good';

    const s = scoreFor(verdict, power, p.finale);
    this.gotPts += s.got;
    this.maxPts += s.max;

    if (hit) {
      this.tally[tier.id]++;
      if (foul) this.tally.foul++;
      this.powerSum += power;
      this.powerN++;
    }
    this.swings.push({
      beat: p.targetBeat, verdict, errMs: Math.round(errMs * 10) / 10,
      hold: Math.round(this.pendingHold * 100) / 100,
      power: Math.round(power * 100) / 100, tier: tier.id, finale: !!p.finale,
    });

    this.batterStrike(ctx, 0.4 + power * 0.6);
    // The verdict pose lands once the follow-through has read, not over it.
    this.pendingReact = { verdict, t: 0.42 };

    if (hit) this.connect(ctx, p, verdict, power, tier, foul, false);
    else this.whiff(ctx, p);

    ctx.bus.emit('judge', { verdict, errMs, beat: p.targetBeat });
    ctx.ui.hud.setScore(Math.round(this.gotPts));
    ctx.ui.hud.setCombo(this.judge.stats.combo);
    ctx.ui.hud.setAccuracy(this.judge.accuracy);
    if (p.finale) this.finaleDone = true;
  },

  /** Contact. Ball leaves the bat; the whole stadium says the same thing. */
  connect(ctx, p, verdict, power, tier, foul, isDemo) {
    const c = LAYOUT.contact;
    const combo = this.judge.stats.combo;
    const big = tier.id === 'homer' || tier.id === 'slam';

    // --- the ball -----------------------------------------------------------
    const b = p.ball || this.w.acquireBall();
    p.ball = null;
    p.done = true;
    this.w.clearPips();

    const dist = tier.dist * (foul ? 0.45 : 1);
    const T = tier.id === 'bunt' ? 0.9 : tier.id === 'liner' ? 1.5 : 2.6;
    // Fouls slice toward the camera; fair balls go out over the machine.
    const zAim = foul ? 1.5 : -0.42;
    const dir = _dir.set(-1, 0, zAim).normalize();
    const vx = dir.x * dist / T;
    const vz = dir.z * dist / T;
    const vy = (tier.lift * dist) / T + 0.5 * GRAV * T * 0.42;

    b.mesh.position.set(c[0], c[1], c[2]);
    b.mesh.visible = true;
    b.busy = true;
    if (!b.trail) b.trail = ctx.fx.trail({ color: tier.color, width: big ? 0.15 : 0.09 });
    else { b.trail.setColor(tier.color); b.trail.setWidth(big ? 0.15 : 0.09); }
    this.hitBalls.push({
      b, x: c[0], y: c[1], z: c[2], vx, vy, vz, t: 0,
      life: big ? 3.4 : T + 0.35, tier, spin: (0.5 + power) * 9,
    });

    // --- feedback -----------------------------------------------------------
    const dirArr = [dir.x * 0.65, 0.72 + tier.lift * 0.3, dir.z * 0.4];
    const scale = tier.id === 'slam' ? 2.0 : big ? 1.35 : tier.id === 'liner' ? 1.0 : 0.78;
    ctx.fx.verdict(verdict, c, { dir: dirArr, combo, scale, groundY: 0 });
    ctx.fx.impact(c, { dir: dirArr, color: tier.color, count: big ? 24 : 12, speed: 13, scale });
    if (big) {
      ctx.fx.speedLines(c, { color: tier.color, count: 16, radius: 3.0 });
      ctx.fx.ring(c, { color: tier.color, from: 0.4, to: 4.2 * scale, life: 0.5, thick0: 0.2 });
    }

    const f = feelForCombo(verdict, combo);
    ctx.hitstop(Math.min(FEEL.hitstopMax * 1.6, f.hitstop * (big ? 1.5 : 1) * (p.finale ? 2.6 : 1)));
    ctx.stage.punchZoom(1 + (big ? 0.07 : 0.03) * (p.finale ? 2.4 : 1));
    this.camPush = big ? 1 : 0.45;

    // --- sound --------------------------------------------------------------
    const now = ctx.clock.rawNow();
    ctx.audio.sfx('impact', now);
    if (!isDemo) ctx.audio.sfx(verdict, now + 0.01);
    if (big) ctx.audio.sfx('coin', now + 0.06);
    if (p.finale) ctx.audio.sfx('fanfare', now + 0.1);

    // --- crowd + callout ----------------------------------------------------
    this.w.crowd.hype(big ? 1.0 : tier.id === 'liner' ? 0.5 : 0.25);
    if (big) this.w.crowd.wave(1, 2.1);
    const word = foul ? 'FOUL!' : tier.label;
    // Above the verdict word (fx.verdict pops at the contact point), so the two
    // channels stack instead of overprinting.
    this.w.callout(word, [c[0] - 0.9, c[1] + 1.6, c[2]], {
      scale: tier.id === 'slam' ? 1.35 : big ? 1.05 : 0.8,
      life: big ? 1.25 : 0.95,
      rise: big ? 1.5 : 0.9,
    });

    if (p.finale) {
      ctx.stage.flash(FEEL.flash.finale, '#fff3cf');
      ctx.stage.shake(FEEL.shake.finale, [-1, 0.5, 0]);
      ctx.fx.confetti([c[0] - 2, c[1] + 2, c[2]], { count: 140, speed: 9, up: 1 });
    }
  },

  /** A whiff. Funny, never punishing: the ball thuds into the backstop. */
  whiff(ctx, p) {
    const c = LAYOUT.contact;
    ctx.fx.verdict('miss', c, { combo: 0, scale: 1, groundY: 0 });
    ctx.audio.sfx('miss', ctx.clock.rawNow());
    this.w.crowd.deflate(0.9);
    // Outs are a scoring tier, not an ejection: the third one retires the side
    // and the lamps reset. Nobody ever stops playing.
    this.outs += 1;
    this.w.setOuts(this.outs);
    this.w.callout('OUT!', [c[0] - 0.6, c[1] + 0.9, c[2]], { scale: 0.72, life: 0.85, rise: -0.5 });
    if (this.outs >= 3) {
      this.outs = 0;
      this._outsClear = 0.6;
      this.w.crowd.wave(0.55, 1.4);
    }
    this.camPush = 0.25;
  },

  // ----------------------------------------------------------------- update

  update(ctx, dt, beat) {
    const clock = ctx.clock;
    const now = clock.now();
    const w = this.w;

    this.judge.update(now);

    // --- windup -----------------------------------------------------------
    if (this.holding) {
      this.holdBeats = Math.max(0, (now - this.holdStart) / clock.spb);
      const p = this.livePitch();
      const power = powerFor(this.holdBeats, p ? p.ideal : 2);
      w.batTip.getWorldPosition(_v);
      this.trace.push(dt, _v.x, _v.y, _v.z, power);
    } else if (this.traceHoldOff > 0) {
      this.traceHoldOff -= dt;
      w.batTip.getWorldPosition(_v);
      this.trace.push(dt, _v.x, _v.y, _v.z, this.pendingPower);
      if (this.traceHoldOff <= 0) this.trace.release();
    }
    this.trace.update(dt, ctx.camera);

    // --- delayed verdict pose --------------------------------------------
    if (this.pendingReact) {
      this.pendingReact.t -= dt;
      if (this.pendingReact.t <= 0) {
        w.batter.anim.react(this.pendingReact.verdict);
        this.pendingReact = null;
      }
    }

    if (this._outsClear > 0) {
      this._outsClear -= dt;
      if (this._outsClear <= 0) w.setOuts(0);
    }

    this.updatePitches(ctx, dt, beat);
    this.updateHitBalls(ctx, dt);
    this.updateShow(ctx, dt, beat);

    w.update(dt, beat);

    if (!this.over && beat > END_BEAT && this.judge.finished) this.over = true;
    this.lastBeat = beat;
  },

  /** Harness hook: the scored notes, so autoplay can press like a person. */
  testChart() {
    return this.judge.notes.map((n) => ({ time: n.time, action: 'a' }));
  },

  /** The pitch currently in flight, if any. */
  livePitch() {
    for (const p of this.pitches) if (p.live && !p.done) return p;
    return null;
  },

  /** Sample a pitch's flight path at `u` (0 = muzzle, 1 = the plate). */
  sample(p, u, out) {
    const c = LAYOUT.contact;
    out.x = lerp(MUZZLE[0], c[0], u);
    out.y = lerp(MUZZLE[1], c[1], u) + 4 * p.apex * u * (1 - u);
    out.z = lerp(MUZZLE[2], c[2], u) + Math.sin(Math.PI * clamp01(u)) * 0.45;
    return out;
  },

  updatePitches(ctx, dt, beat) {
    const w = this.w;
    for (let i = 0; i < this.pitches.length; i++) {
      const p = this.pitches[i];
      if (p.done) continue;
      if (beat < p.launchBeat - 1.15) break;   // schedule is launch-ordered

      // one beat out: the machine spins up. Telegraph before the telegraph.
      if (!p.armed && beat >= p.launchBeat - 1) {
        p.armed = true;
        w.armMachine(p.kind === 'slam' ? 1.6 : 1);
        // Step into the box — unless a verdict pose is still playing out.
        if (w.batter.anim.state === 'idle') {
          w.batter.anim.play('ready', { loop: true, face: 'focus', beat: 0.3, blend: 0.25 });
        }
      }

      // The coil: at most COIL_BEATS before contact, and never over the
      // previous swing's follow-through.
      if (p.live && !p.coiled && beat >= Math.max(p.launchBeat, p.targetBeat - COIL_BEATS)
        && ctx.clock.now() >= this.strikeUntil) {
        p.coiled = true;
        this.batterCoil(ctx, ctx.clock.timeAt(p.targetBeat));
      }

      const u = (beat - p.launchBeat) / p.lead;
      if (!p.live) {
        if (u < 0) continue;
        p.live = true;
        p.ball = w.acquireBall();
        p.ball.trail = ctx.fx.trail({ color: 0xbfeaff, width: 0.075 });
        p.pipCursor = 0;
        w.fireMachine();
        w.setPips(p, (uu, out) => this.sample(p, uu, out));
        ctx.audio.sfx('tick', ctx.clock.rawNow(), 0.16);
        if (p.kind === 'demo') this.beginWindup(ctx, ctx.clock.timeAt(p.launchBeat));
      }

      // demo: the batter swings itself, on time, in front of the player
      if (p.kind === 'demo' && this.holding && beat >= p.targetBeat) {
        this.releaseSwing(ctx, ctx.clock.timeAt(p.targetBeat));
        continue;
      }

      const ball = p.ball;
      if (!ball) continue;
      this.sample(p, Math.min(u, 1.6), _v);
      if (u > 1) _v.y = Math.max(0.16, _v.y - (u - 1) * 1.4);
      ball.mesh.position.copy(_v);
      ball.mesh.rotation.x += dt * 11;
      ball.mesh.rotation.z += dt * 7;
      if (ball.trail) ball.trail.set(_v.x, _v.y, _v.z);

      // pop each half-beat marker as the ball reaches it
      const steps = Math.max(2, Math.round(p.lead * 2));
      while (p.pipCursor < steps && u >= (p.pipCursor + 1) / steps - 0.02) {
        w.popPip(p.pipCursor);
        p.pipCursor++;
      }

      if (u >= 1.55) {
        // sailed past — into the backstop, with a puff. Never silent.
        ctx.fx.burst([_v.x, _v.y, _v.z], { count: 8, color: 0x9fd8ff, speed: 3, life: 0.4 });
        w.freeBall(ball);
        p.ball = null;
        p.done = true;
        w.clearPips();
      }
    }
  },

  updateHitBalls(ctx, dt) {
    for (let i = this.hitBalls.length - 1; i >= 0; i--) {
      const h = this.hitBalls[i];
      h.t += dt;
      h.vy -= GRAV * dt * (h.tier.id === 'slam' ? 0.45 : h.tier.id === 'homer' ? 0.6 : 1);
      h.x += h.vx * dt; h.y += h.vy * dt; h.z += h.vz * dt;
      if (h.y < 0.19 && h.tier.id === 'bunt') { h.y = 0.19; h.vy *= -0.35; h.vx *= 0.7; }
      h.b.mesh.position.set(h.x, h.y, h.z);
      h.b.mesh.rotation.y += dt * h.spin;
      h.b.mesh.rotation.x += dt * h.spin * 0.7;
      if (h.b.trail) h.b.trail.set(h.x, h.y, h.z);
      if (h.t >= h.life) {
        this.w.freeBall(h.b);
        this.hitBalls.splice(i, 1);
      }
    }
  },

  /** Everything that makes the room agree with the music. */
  updateShow(ctx, dt, beat) {
    const w = this.w;
    const section = sectionAt(beat);

    // crowd energy climbs with the round and with the player's streak
    const base = { lead: 0.28, teach: 0.42, play: 0.58, escalate: 0.8, finale: 1 }[section] ?? 0.5;
    const streak = clamp01(this.judge.stats.combo / 14) * 0.25;
    w.crowd.setEnergy(clamp01(base + streak));

    // The grand-slam windup: the camera leans in and the stadium holds still.
    const slamHold = beat >= FINALE_BEAT - 8 && beat < FINALE_BEAT;
    this.camPush = damp(this.camPush, 0, 3.2, dt);
    ctx.stage.rig.frame({
      target: [-1.9 + this.camPush * 0.9, 1.95 + this.camPush * 0.15, 0],
      distance: (slamHold ? 11.4 : 12.6) - this.camPush * 0.5,
      height: 2.15,
      yaw: 0.055 + Math.sin(beat * 0.11) * 0.012,
      lambda: 2.4,
    });
    if (slamHold) w.crowd.setEnergy(0.95);
  },

  // ----------------------------------------------------------------- result

  result(ctx) {
    if (!this.over) return null;
    const s = this.judge.stats;
    const acc = this.judge.accuracy;
    const score = this.maxPts > 0 ? Math.round(1000 * clamp01(this.gotPts / this.maxPts)) : 0;
    return {
      score,
      accuracy: acc,
      rank: rankFor(acc, s.miss),
      stats: {
        perfect: s.perfect, great: s.great, good: s.good, miss: s.miss,
        maxCombo: s.maxCombo, biasMs: Math.round(this.judge.bias * 10) / 10,
        homers: this.tally.homer + this.tally.slam,
        liners: this.tally.liner,
        bunts: this.tally.bunt,
        fouls: this.tally.foul,
        grandSlam: this.tally.slam > 0,
        avgPower: this.powerN ? Math.round((this.powerSum / this.powerN) * 100) / 100 : 0,
      },
      highlights: this.swings.filter((x) => x.tier === 'slam' || (x.tier === 'homer' && x.verdict === 'perfect')),
    };
  },

  // ---------------------------------------------------------------- dispose

  dispose(ctx) {
    if (this._offBeat) { this._offBeat(); this._offBeat = null; }
    if (typeof window !== 'undefined' && window.__BBB__) delete window.__BBB__.swing;
    for (const h of this.hitBalls) this.w?.freeBall(h.b);
    this.hitBalls = [];
    this.trace?.dispose();
    this.w?.dispose();
    this.trace = null;
    this.w = null;
    ctx.ui.hud.unmount();
  },
};
