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
 * TWO WAYS TO SWING (ADR 0004)
 * ─────────────────────────────────────────────────────────────────────────
 * TAP (default, what the harness plays): press as the ball reaches the
 * plate. Timing is everything — PERFECT sends it, GREAT is a line drive,
 * GOOD a bunt (powerFromTiming).
 *
 * CONDUCT (opt-in, mouse or webcam): the original design. Hold to wind up,
 * swing on the ictus — the downstroke's stop (gesture.js) or the release.
 * Two axes that never talk to each other:
 *
 *   release timing  -> perfect / great / good / whiff   (core/judge.js)
 *   windup length   -> bunt / line drive / home run     (rules.js)
 *
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
import { Save } from '../../core/util.js';
import { createIctusDetector } from './gesture.js';
import { createHud } from './hud.js';
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

/** Stance/coil head turn toward camera: the mocap batter watches the pitcher,
 *  which put the helmet brim and gloves over his face in the most common pose. */
const HEAD_TURN = 0.55;

/** Webcam frames arrive ~this late (capture + inference); taken off each sample. */
const CAMERA_LATENCY = 0.07;

/**
 * Tap, mouse or camera (ADR 0004). A launch option (`?swingInput=` / the
 * play wrapper's opts) wins; otherwise the player's saved Options choice.
 * Read through core Save so the game never imports shell modules.
 */
function resolveInputMode(ctx) {
  const m = ctx.opts?.swingInput ?? Save.get('options', null)?.swingInput ?? 'tap';
  return m === 'mouse' || m === 'camera' ? m : 'tap';
}

// scratch
const _v = new THREE.Vector3();
const _dir = new THREE.Vector3();

/**
 * Timing -> power, stepped by verdict so the three hit tiers MEAN the three
 * timing grades: PERFECT sends it (home run), GREAT is a line drive, GOOD a
 * bunt. Within each band power still slides a little, so it never feels like
 * a lookup table. (A smooth curve over the whole window put a ±60ms GREAT at
 * 0.9 power — a home run too — and nobody ever saw a line drive or a bunt.)
 */
export function powerFromTiming(time, pitch, clock) {
  // Pitches carry their beat; the audio time lives on the judged note. (This
  // read `pitch.time`, which does not exist: power was NaN on every press.)
  const target = pitch.note ? pitch.note.time : clock.timeAt(pitch.targetBeat);
  const err = Math.abs(time - target) * 1000;
  const { perfect, great, good } = WINDOWS_MS;
  if (err <= perfect) return 1;
  if (err <= great) return 0.62 - 0.2 * ((err - perfect) / (great - perfect));   // liner
  if (err <= good) return 0.24 - 0.14 * ((err - great) / (good - great));        // bunt
  return 0;
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
    // Where pitches cross the plate; starts at the calibrated sweet spot and
    // adapts to the live bat after each hit (flushContact).
    this.contactPoint = LAYOUT.contact.slice();
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

    // Input mode (ADR 0004): 'tap' is the default and the harness path;
    // 'mouse' and 'camera' are opt-in conducting modes from Options.
    this.mode = resolveInputMode(ctx);
    this.detector = createIctusDetector();
    this.camera = null;

    // Debug/verification hook. The shared harness bot can only emit key-DOWN
    // events, so it can never perform a hold-and-release; this lets a script
    // drive the real gesture at exact audio times, in any mode. See verify.mjs.
    if (typeof window !== 'undefined' && window.__BBB__) {
      const self = this;
      window.__BBB__.swing = {
        hold: (atBeat) => self.beginWindup(ctx, ctx.clock.timeAt(atBeat)),
        release: (atBeat) => self.releaseSwing(ctx, ctx.clock.timeAt(atBeat)),
        /** Conduct-mode state, for smoke-conduct.mjs. */
        conduct: () => ({ mode: self.mode, holding: self.holding, detector: self.detector.state, pointer: self._pointer || null }),
        stats: () => ({
          swings: self.swings.slice(-40),
          contacts: self.contacts.slice(),
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
    this.contacts = [];
    this.over = false;
    this.finaleDone = false;
    this.lastBeat = -1e9;
    this.camPush = 0;
    this.strikeUntil = -Infinity;
    this.pendingContact = null;
    this.nightOn = false;
    this.camLift = 0;
    this.curtain = false;
    this.curtainBeat = null;
    this.finishShown = false;
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
    // Lead-in, 8 beats: title card + the demo swing (lands at -5), then a
    // 4-3-2 count and "PLAY BALL!" on the last beat — short, and up top, so
    // it is gone before the first scored pitch reaches the plate at beat 0.
    // (It used to be a centre banner AT beat 0, printed over the first hit.)
    this._offBeat = clock.onBeat((b, t) => {
      if (b < 0) {
        ctx.audio.sfx('count', t, ((b % 4) + 4) % 4);
        const n = -b;
        if (n <= 4 && n >= 2) ctx.ui.popup(String(n - 1), { y: 0.26, scale: 1.4, color: '#ffe58a', life: 0.42 });
        if (n === 1) ctx.ui.popup('PLAY BALL!', { y: 0.26, scale: 1.2, color: '#ffe58a', life: 0.44 });
      }
    });

    ctx.audio.music.play('swing-kings');
    // No subtitle: the instruction lives in the HUD bar for the whole teach
    // section, and a 1.5s line of small text over the grass was unreadable.
    ctx.ui.banner('SWING KINGS', { life: this.mode === 'tap' ? 1.5 : 2.2, color: '#ffe58a' });
    this.hud = createHud(ctx, this.inputHintHtml());
    this.hintHidden = false;
    this.startGestureSource(ctx);
    ctx.ui.hud.setScore(0);
    ctx.ui.hud.setAccuracy(1);
  },

  /** The one instruction the player reads (HUD bar) — it must describe the real input. */
  inputHintHtml() {
    if (this.mode === 'mouse') return '<b>HOLD</b> the mouse as the ball flies · <b>SWING DOWN</b> as it lands';
    if (this.mode === 'camera') return '<b>RAISE</b> your hand as the ball flies · <b>CONDUCT DOWN</b> as it lands';
    return '<b>SPACE</b> / tap as the ball reaches the plate · on the beat = <b>HOME RUN</b>';
  },

  /**
   * Gesture sources (ADR 0004). Both feed the same ictus detector: the mouse
   * while a button is held (press = raise, the downstroke's stop = swing),
   * the camera from the tracked palm (raise = windup, ictus = swing). Neither
   * runs in tap mode, and the harness never drives them.
   */
  startGestureSource(ctx) {
    if (this.mode === 'mouse') this.startPointerConduct(ctx);
    if (this.mode === 'camera') {
      import('./camera.js')
        .then(({ startCamera }) => startCamera({
          clock: ctx.clock,
          onSample: (t, y) => this.onConductSample(ctx, t - CAMERA_LATENCY, y, true),
          onLost: () => this.detector.reset(),
        }))
        .then((cam) => {
          if (!this.w) { cam.stop(); return; }   // left the scene while loading
          this.camera = cam;
        })
        .catch((e) => {
          // No camera, refused, or no GPU for the model: conduct with the mouse
          // instead. The mode is opt-in and low-stakes by design (spec §5).
          console.info('camera conduct unavailable, using mouse:', e?.message || e);
          if (!this.w) return;
          this.mode = 'mouse';
          ctx.ui.banner('NO CAMERA', { sub: 'conduct with the mouse instead', life: 1.8, color: '#ffe58a' });
          this.startPointerConduct(ctx);
        });
    }
  },

  startPointerConduct(ctx) {
    if (this._onPointerMove) return;
    this._onPointerMove = (e) => {
      if (!this.holding || !(e.buttons & 1)) return;
      const t = ctx.clock.toAudioTime(e.timeStamp);
      const y = e.clientY / Math.max(1, innerHeight);
      this._pointer = { t, y };
      this.onConductSample(ctx, t, y, false);
    };
    window.addEventListener('pointermove', this._onPointerMove, { passive: true });
  },

  /** One gesture position; `raises` = this source starts windups itself. */
  onConductSample(ctx, time, y, raises) {
    const ev = this.detector.feed(time, y);
    if (!ev) return;
    if (ev.type === 'raise' && raises && !this.holding) this.beginWindup(ctx, ev.time);
    else if (ev.type === 'ictus' && this.holding) this.releaseSwing(ctx, ev.time);
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
      if (e.action !== 'a') continue;
      if (this.mode !== 'tap') {
        // CONDUCT MODES: hold to wind up (the trace draws the gesture),
        // release — or finish a downstroke, see onConductSample — to swing.
        // Power is the windup length again, the original design.
        if (e.down && !e.repeat) { this.detector.reset(); this.beginWindup(ctx, e.time); }
        else if (!e.down) this.releaseSwing(ctx, e.time);
        continue;
      }
      if (!e.down) continue;
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
      to: LOAD, dur, hold: true, face: 'focus', beat: 0.12, blend: 0.14, headTurn: HEAD_TURN,
    });
    this.pendingReact = null;   // the next pitch outranks the last verdict
  },

  /** Contact -> follow-through, from the loaded frame. */
  batterStrike(ctx, power) {
    power = Number.isFinite(power) ? clamp01(power) : 0.5;
    const rate = 1.15 + power * 0.55;
    const anim = this.w.batter.anim;
    // No squash impulse here: under the twisted mocap contact pose a root
    // squash sheared the batter into an egg on every home run. The swing's
    // own mechanics carry the impact; the world (hitstop, punch, ring) sells it.
    anim.play('swing', {
      from: LOAD, rate, face: 'fierce', beat: 0.05, blend: 0.03, power, next: 'idle',
    });
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

    // `press` calls onJudged synchronously when it claims a note; the flag
    // tells a swing-and-miss from a pitch the player never swung at.
    this._swinging = true;
    const r = this.judge.press('a', time);
    this._swinging = false;

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
    this.queueContact(ctx, [d, 'perfect', power, TIERS.homer, false, true]);
    d.done = true;
    return true;
  },

  /**
   * The bat meets the ball when the BAT gets there. Judgement happens on the
   * press (audio time, exact), but the strike starts a few clip-frames before
   * the contact frame, so the visuals — ball launch, flash, hitstop, words —
   * wait until the live swing reaches its contact frame (~2 frames) and fire
   * from the bat's actual sweet spot. Firing them at a fixed point on the
   * press put the flash in empty air while the bat swung through elsewhere.
   */
  queueContact(ctx, args) {
    if (this.pendingContact) this.flushContact(ctx);
    this.pendingContact = { args, t0: ctx.clock.now() };
  },

  flushContact(ctx) {
    const pc = this.pendingContact;
    if (!pc) return;
    this.pendingContact = null;
    this.w.batSweetSpot(_v);
    // Pitches aim at `contactPoint`; pull it toward where the bat really was
    // (load-time calibration can't see the animator's smoothing), so the
    // next ball arrives on the bat, not beside it.
    const cp = this.contactPoint;
    cp[0] += (_v.x - cp[0]) * 0.7; cp[1] += (_v.y - cp[1]) * 0.7; cp[2] += (_v.z - cp[2]) * 0.7;
    // How far the waiting ball was from the bat when the bat got there, and
    // how long the visuals waited for it — verify.mjs holds both to a bound.
    const ball = pc.args[0].ball?.mesh.position;
    this.contacts.push({
      gap: ball ? Math.round(ball.distanceTo(_v) * 1000) / 1000 : null,
      delayMs: Math.round((ctx.clock.now() - pc.t0) * 1000),
    });
    if (this.contacts.length > 40) this.contacts.shift();
    this.connect(ctx, ...pc.args, [_v.x, _v.y, _v.z]);
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

    const swung = hit || this._swinging;
    if (swung) {
      this.batterStrike(ctx, 0.4 + power * 0.6);
      // The verdict pose lands once the follow-through has read, not over it.
      this.pendingReact = { verdict, t: 0.3 };
    } else {
      // Never swung: the batter watches it go by and slumps — no lunge, so
      // "didn't swing" and "swung and missed" no longer look the same.
      this.w.batter.anim.react('miss');
      this.pendingReact = null;
    }

    if (hit) { p.contactPending = true; this.queueContact(ctx, [p, verdict, power, tier, foul, false]); }
    else this.whiff(ctx, p, swung);

    ctx.bus.emit('judge', { verdict, errMs, beat: p.targetBeat });
    ctx.ui.hud.setScore(Math.round(this.gotPts));
    ctx.ui.hud.setCombo(this.judge.stats.combo);
    ctx.ui.hud.setAccuracy(this.judge.accuracy);
    if (p.finale) this.finaleDone = true;
  },

  /** Contact. Ball leaves the bat; the whole stadium says the same thing. */
  connect(ctx, p, verdict, power, tier, foul, isDemo, at = null) {
    const c = at || this.contactPoint;
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
    // How long until the next pitch lands: in the dense sections (a pitch
    // every 0.97s) full-size rings and 1.25s words stacked up and caged the
    // batter, hiding the next ball. Feedback scales to the space it has.
    const gap = this.gapAfter(p, ctx.clock.spb);
    const roomy = gap > 1.4;
    const dirArr = [dir.x * 0.65, 0.72 + tier.lift * 0.3, dir.z * 0.4];
    const scale = tier.id === 'slam' ? 2.0 : big ? 1.35 : tier.id === 'liner' ? 1.0 : 0.78;
    // One word per hit: the result badge (below) carries the timing grade as
    // its second line, so the fx verdict text is off — PERFECT!, HOME RUN!,
    // the combo and OUTS used to pile into one patch of screen. The fx
    // system's own full-frame flash is off too (it washed the frame milky);
    // Swing Kings shakes the camera itself.
    ctx.fx.verdict(verdict, c, { dir: dirArr, combo, scale, groundY: 0, text: false, stage: false, rings: !!p.finale });
    ctx.fx.impact(c, { dir: dirArr, color: tier.color, count: big ? 24 : 12, speed: 13, scale });
    if (big && !isDemo) {
      ctx.fx.speedLines(c, { color: tier.color, count: roomy ? 16 : 8, radius: 3.0 });
      if (p.finale) ctx.fx.ring(c, { color: tier.color, from: 0.4, to: 4.2 * scale, life: 0.5, thick0: 0.2 });
    }
    if (!isDemo) ctx.stage.shake(big ? 0.12 : 0.05, [dir.x, 0.5, 0]);

    const f = feelForCombo(verdict, combo);
    if (!isDemo) {
      ctx.hitstop(Math.min(FEEL.hitstopMax * 1.6, f.hitstop * (big ? 1.5 : 1) * (p.finale ? 2.6 : 1)));
      ctx.stage.punchZoom(1 + (big ? 0.07 : 0.03) * (p.finale ? 2.4 : 1));
    }
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
    const grade = { perfect: 'PERFECT', great: 'GREAT', good: 'GOOD' }[verdict] || '';
    // Home runs carry their distance, so forty homers aren't forty identical
    // badges (quantised to 5ft: the badge textures are cached per string).
    const feet = tier.id === 'homer' && !foul
      ? ` · ${Math.round((365 + power * 70 + (verdict === 'perfect' ? 15 : 0) + (combo % 7) * 3) / 5) * 5} FT` : '';
    const word = isDemo ? 'LIKE THIS!' : `${foul ? 'FOUL!' : tier.label}|${grade}${feet}`;
    // Above the verdict word (fx.verdict pops at the contact point), so the two
    // channels stack instead of overprinting; its life never outlasts the gap
    // to the next pitch, so two tier words are never up at once.
    // The grand slam is the one word that gets to stay: bigger and longer.
    // The demo's LIKE THIS! clears before the count-in's "3" appears.
    const life = p.finale ? 2.4 : isDemo ? 0.85 : Math.min(big ? 1.25 : 0.95, Math.max(0.55, gap * 0.8));
    this.w.callout(word, [c[0] - 0.9, c[1] + 1.6, c[2]], {
      scale: tier.id === 'slam' ? 1.75 : big ? 1.05 : 0.8,
      life,
      rise: (big ? 1.5 : 0.9) * (life / (big ? 1.25 : 0.95)),
    });

    if (p.finale) {
      ctx.stage.flash(FEEL.flash.finale, '#fff3cf');
      ctx.stage.shake(FEEL.shake.finale, [-1, 0.5, 0]);
      ctx.fx.confetti([c[0] - 2, c[1] + 2, c[2]], { count: 140, speed: 9, up: 1 });
    }
  },

  /**
   * A miss. Funny, never punishing: the ball thuds into the backstop. WHIFF!
   * for a swing and a miss, STRIKE! for a pitch the player never swung at.
   */
  whiff(ctx, p, swung = true) {
    const c = this.contactPoint;
    // The badge's life is capped by the gap to the next pitch (the fx
    // verdict word lived ~1.26s and sat over the next HOME RUN!).
    ctx.fx.verdict('miss', c, { combo: 0, scale: 1, groundY: 0, text: false, stage: false });
    const life = Math.min(0.9, Math.max(0.5, this.gapAfter(p, ctx.clock.spb) * 0.7));
    this.outs += 1;
    const third = this.outs >= 3;
    if (!third) this.w.callout(swung ? 'WHIFF!' : 'STRIKE!', [c[0] - 0.6, c[1] + 1.2, c[2]], { scale: 0.8, life, rise: 0.4 });
    ctx.audio.sfx('miss', ctx.clock.rawNow());
    this.w.crowd.deflate(0.9);
    // Outs are a scoring tier, not an ejection: the third one retires the
    // side — all three lamps hold, the stadium says so — then the count
    // resets and play goes on. The count lives in the HUD (hud.js).
    this.hud.setOuts(this.outs);
    if (third) {
      this.outs = 0;
      this._outsClear = Math.max(1.2, Math.min(1.8, this.gapAfter(p, ctx.clock.spb) * 0.9));
      this.w.callout('SIDE RETIRED!|3 OUTS', [c[0] - 0.4, c[1] + 1.5, c[2]], { scale: 1.05, life: this._outsClear, rise: 0.3 });
      this.w.crowd.deflate(1.4);
      this.w.crowd.wave(0.55, 1.4);
      ctx.stage.shake?.(0.08, [0, -1, 0]);
    }
    this.camPush = 0.25;
  },

  // ----------------------------------------------------------------- update

  update(ctx, dt, beat) {
    const clock = ctx.clock;
    const now = clock.now();
    const w = this.w;

    // A mouse that stops moving stops sending events, so the detector would
    // never see the deceleration that IS the ictus. Once the pointer has been
    // still for 40ms (longer than any gap between moves mid-stroke), feed it
    // the resting position so the stop registers — stamped one frame after
    // the last real move, which is when the hand actually stopped, not 40ms
    // later when we noticed.
    if (this.mode === 'mouse' && this.holding && this._pointer && now - this._pointer.t > 0.04) {
      const tStill = this._pointer.t + 1 / 60;
      this._pointer.t = tStill;
      this.onConductSample(ctx, tStill, this._pointer.y, false);
    }

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
    // The ribbon draws the player's conducting gesture. A tap has no gesture,
    // so in tap mode it was just a smear behind the bat that meant nothing.
    this.trace.mesh.visible = this.mode !== 'tap';

    // --- delayed verdict pose --------------------------------------------
    if (this.pendingReact) {
      this.pendingReact.t -= dt;
      if (this.pendingReact.t <= 0) {
        // With a long pitch already in the air (the grand slam flies for
        // eight beats), square up for it instead of celebrating facing away.
        const lp = this.livePitch();
        if (lp && clock.timeAt(lp.targetBeat) - now > 1.6) {
          w.batter.anim.play('ready', { loop: true, face: 'focus', beat: 0.3, blend: 0.25, headTurn: HEAD_TURN });
        } else {
          w.batter.anim.react(this.pendingReact.verdict);
        }
        this.pendingReact = null;
      }
    }

    if (this._outsClear > 0) {
      this._outsClear -= dt;
      if (this._outsClear <= 0) this.hud.setOuts(0);
    }

    this.updatePitches(ctx, dt, beat);
    this.updateHitBalls(ctx, dt);
    this.updateShow(ctx, dt, beat);

    w.update(dt, beat);

    // Contact fires once the posed bat has actually reached the contact frame
    // (poses were just applied above), or after 120ms whatever happens.
    if (this.pendingContact) {
      const a = w.batter.anim;
      const atContact = a.state === 'clip' && a.variant?.name === 'swing'
        && a.variant.from >= LOAD - 1e-6 && a.clipTime >= SWING.contact;
      if (atContact || now - this.pendingContact.t0 > 0.12) this.flushContact(ctx);
    }

    if (!this.over && beat > END_BEAT && this.judge.finished) this.over = true;
    this.lastBeat = beat;
  },

  /** Seconds until the next pitch after `p` lands (Infinity for the last). */
  gapAfter(p, spb) {
    let next = Infinity;
    for (const q of this.pitches) {
      if (q.targetBeat > p.targetBeat && q.targetBeat < next) next = q.targetBeat;
    }
    return (next - p.targetBeat) * spb;
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
    const c = this.contactPoint;
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
          w.batter.anim.play('ready', { loop: true, face: 'focus', beat: 0.3, blend: 0.25, headTurn: HEAD_TURN });
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
        // A pitch in the air outranks the last verdict: the batter squares up
        // at the launch (the previous hit's celebration used to play on
        // through the whole grand-slam flight, facing away from the ball).
        const a = w.batter.anim;
        const inSwing = a.state === 'clip' && a.variant?.name === 'swing';
        const inReady = a.state === 'clip' && a.variant?.name === 'ready';
        if (p.kind !== 'demo' && !inSwing && !inReady) {
          this.pendingReact = null;
          a.play('ready', { loop: true, face: 'focus', beat: 0.3, blend: 0.2, headTurn: HEAD_TURN });
        }
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
      // A struck ball waits on the plate for the ~2 frames until the bat's
      // contact frame fires (flushContact) instead of flying through the bat.
      const uu = p.contactPending ? Math.min(u, 1) : u;
      this.sample(p, Math.min(uu, 1.6), _v);
      if (uu > 1) _v.y = Math.max(0.16, _v.y - (uu - 1) * 1.4);
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

    // The instruction bar stays up through the teach section, then fades.
    if (!this.hintHidden && beat >= 16) { this.hintHidden = true; this.hud?.hideHint(); }

    // crowd energy climbs with the round and with the player's streak
    const base = { lead: 0.28, teach: 0.42, play: 0.58, escalate: 0.8, finale: 1 }[section] ?? 0.5;
    const streak = clamp01(this.judge.stats.combo / 14) * 0.25;
    w.crowd.setEnergy(clamp01(base + streak));

    // The stage escalates with the chart: dusk turns to stadium-lit night
    // when the fast off-beat section starts, and stays there for the finale.
    const night = section === 'escalate' || section === 'finale';
    if (night !== this.nightOn) {
      this.nightOn = night;
      ctx.stage.setPalette(night ? 'swing-kings-night' : 'swing-kings', night ? 2.4 : 0.8);
    }

    // The grand-slam windup: the ball goes up for eight beats, and the camera
    // goes with it — it used to stay put and the ball left the frame for the
    // whole windup. After contact it follows the hit out, then settles.
    const slam = this.pitches[this.pitches.length - 1];
    const slamHold = beat >= FINALE_BEAT - 8 && beat < FINALE_BEAT;
    let lift = 0;
    const flying = slam?.ball?.mesh?.visible ? slam.ball.mesh.position.y
      : this.hitBalls.find((h) => h.tier.id === 'slam')?.y;
    if ((slamHold || beat < FINALE_BEAT + 4) && flying !== undefined && beat >= FINALE_BEAT - 8) {
      // Capped: a grand slam leaves the park, and a camera that chases it
      // all the way ends up framing empty sky with no batter in shot.
      lift = Math.min(3.2, Math.max(0, flying - 2.6));
    }
    this.camLift = damp(this.camLift || 0, lift, 3.4, dt);
    this.camPush = damp(this.camPush, 0, 3.2, dt);
    ctx.stage.rig.frame({
      target: [-1.9 + this.camPush * 0.9, 1.95 + this.camPush * 0.15 + this.camLift * 0.42, 0],
      distance: (slamHold ? 12.2 : night ? 12.0 : 12.6) - this.camPush * 0.5 + this.camLift * 0.7,
      height: 2.15,
      yaw: 0.055 + Math.sin(beat * 0.11) * 0.012,
      lambda: 2.4,
    });
    if (slamHold) w.crowd.setEnergy(0.95);

    // Curtain call: after the grand slam the batter dances the mocap routine
    // on the beat, the crowd waves every bar and confetti fires every two
    // beats — the run ends on a party, not on four seconds of idle.
    if (this.finaleDone && beat >= FINALE_BEAT + 2) {
      if (!this.curtain) {
        this.curtain = true;
        w.batter.anim.play('dance', { beatLock: true, bpm: ctx.clock.bpm, face: 'joy', beat: 0.3, blend: 0.3 });
      }
      const b = Math.floor(beat);
      if (b !== this.curtainBeat) {
        this.curtainBeat = b;
        if (b % 4 === 0) w.crowd.wave(0.8, 1.6);
        if (b % 2 === 0) {
          ctx.fx.confetti([LAYOUT.contact[0] - 3 + (b % 3), 5.5, -1], { count: 60, speed: 6, up: 0.6 });
        }
      }
      w.crowd.setEnergy(1);
    }

    // The song ends on a word, on the last bar's downbeat, not on a crouch.
    if (!this.finishShown && beat >= END_BEAT - 4) {
      this.finishShown = true;
      ctx.ui.banner('GAME!', { life: 2.2, color: '#ffe58a' });
      ctx.audio.sfx('fanfare', ctx.clock.rawNow());
      w.crowd.wave(1, 2);
    }
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
    if (this._onPointerMove) { window.removeEventListener('pointermove', this._onPointerMove); this._onPointerMove = null; }
    if (this.camera) { this.camera.stop(); this.camera = null; }
    this.hud?.dispose(); this.hud = null;
    for (const h of this.hitBalls) this.w?.freeBall(h.b);
    this.hitBalls = [];
    this.trace?.dispose();
    this.w?.dispose();
    this.trace = null;
    this.w = null;
    ctx.ui.hud.unmount();
  },
};
