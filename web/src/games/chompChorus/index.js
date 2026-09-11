/**
 * G4 · CHOMP CHORUS — "Four lanes. One groove."
 * 140bpm · 4/4 · four keys (← ↓ ↑ →, or A S W D)
 *
 * Four singing creatures stand in a row. Each one is a voice in a four-part
 * chord. They gulp air on the offbeat — that gulp is the telegraph — and sing
 * on the beat; you press their lane to let them.
 *
 * ── The twist, and why it is built the way it is ───────────────────────────
 * The consequence of a mistake is AUDIBLE and SPECIFIC. Every bar, each lane
 * that is currently "alive" sustains its own chord tone, panned to its own
 * position in the stereo field, in its own register. Miss a lane and that
 * voice stops being scheduled: the four-part chord becomes a thin, gap-toothed
 * three-part one, with a hole at a specific pitch in a specific ear. Catch the
 * lane again and the voice returns on the next bar. A player learns "I keep
 * dropping the ↑ lane" by hearing the top of the chord go missing, not by
 * reading a number afterwards.
 *
 * The chord tones are not invented: `music.info()` + the track's own
 * progression tell us which seventh chord Snack Attack Shuffle is sitting on
 * right now, and lane i takes tone i of it. So the choir is always consonant
 * with the backing track, through every chord change, for free.
 *
 * ── Four-way telegraph readability ─────────────────────────────────────────
 * See props.js for the full argument. Five channels, deliberately different in
 * kind: a CONVERGING ring (distance), a FILLING pipe (bar chart), the singer
 * INFLATING (the thing you are already watching), constant colour/position/key
 * glyph (no recall under pressure), and a BRIDGE bar that physically links the
 * pedestals of lanes that fire together so a chord is one object rather than
 * four things.
 *
 * ── Escalation ─────────────────────────────────────────────────────────────
 * teach one lane at a time -> all four singly -> two-note chords -> chords
 * woven with eighths -> triads -> sixteenth runs sweeping the row -> full
 * four-part chords -> a three-beat silence for one enormous shared breath ->
 * the finale: all four, together, held through the last bar. Worth double.
 */

import * as THREE from 'three';
import { NoteJudge, rankFor, SCORE } from '../../core/judge.js';
import { FEEL, feelForCombo } from '../../core/feel.js';
import { clamp, clamp01, damp } from '../../core/util.js';
import { makeCast, paletteById } from '../../chars/index.js';
import { SCALES, chord as chordTones, mtof } from '../../audio/theory.js';
import { LANES, LANE_ACTIONS, ACTION_TO_LANE, buildChart, FINALE_BEAT, FINALE_END, END_BEAT } from './chart.js';
import { makeLaneProps } from './props.js';

const N = 4;
const LEAD_BEATS = FEEL.leadInBars * 4;
const CHAR_SCALE = 1.25;
/** Every singer's head is parked near this height so the row reads as a line;
 *  the tall one is deliberately allowed to stand above it (up is up). */
const HEAD_LINE = 1.60;
/** Fallback chord if the track has not reported a key yet: Am7. */
const FALLBACK_CHORD = [45, 48, 52, 55];

export default {
  id: 'chomp-chorus',
  name: 'Chomp Chorus',
  blurb: 'Four singers, four keys: ← ↓ ↑ →. They gulp on the offbeat — let them sing on the beat. Drop a lane and you will hear the hole in the chord.',
  bpm: 140,
  durationBars: 42,
  controls: '← ↓ ↑ →',

  // ------------------------------------------------------------------- state
  _: null,

  load(ctx) {
    const s = {
      root: new THREE.Group(),
      cast: null, props: null, env: null,
      judge: null, notes: null, laneList: null, laneCursor: [0, 0, 0, 0],
      cues: null, cueAt: 0,
      pedTop: [], headY: [], colors: [],
      lanes: null,
      chordFlag: [0, 0, 0, 0], chordN: 0,
      demoFired: 0,
      maxPoints: 0,
      finale: { armed: false, hit: 0, frames: 0, heldFrames: 0, banner: false, done: false },
      lastVerdictAt: -1,
      trackOffsetBeat: null, trackOffsetAt: -999,
      done: false, over: false, resultCache: null,
      voiceDots: null, dotEls: [],
      time: 0,
      unsubBeat: null,
      // Preallocated: `props.update` runs every frame and must not allocate.
      view: {
        gulp: [0, 0, 0, 0], armed: [0, 0, 0, 0], sing: [0, 0, 0, 0],
        alive: [1, 1, 1, 1], chord: null, chordN: 0, beatPhase: 0,
      },
    };
    s.view.chord = s.chordFlag;
    this._ = s;

    ctx.scene.userData.palette = 'chomp-chorus';
    ctx.scene.add(s.root);

    // ------------------------------------------------------------ the cast
    s.colors = LANES.map((l) => new THREE.Color().setHex(paletteById(l.pal).body, THREE.SRGBColorSpace));

    s.cast = makeCast({
      scene: s.root,
      count: N,
      builds: LANES.map((l) => l.build),
      positions: LANES.map((l) => [l.x, 0, 0]),
      players: LANES.map((l, i) => ({ id: i, name: `V${i + 1}`, palette: l.pal })),
      detail: 'full',
      scale: CHAR_SCALE,
      seed: 0xc40f,
    });

    // Pedestals exist to put four very different builds' HEADS on one line.
    // Silhouette carries lane identity; head height must not, or the row reads
    // as a staircase instead of a choir.
    for (let i = 0; i < N; i++) {
      const m = s.cast.get(i);
      const hy = m.char.dims.headY * CHAR_SCALE;
      const ped = clamp(HEAD_LINE - hy, 0.10, 1.05);
      s.pedTop.push(ped);
      s.headY.push(ped + hy);
      m.char.position.set(LANES[i].x, ped, 0);
      m.char.rotation.y = -LANES[i].x * 0.05;
    }

    s.props = makeLaneProps({ pedTop: s.pedTop, headY: s.headY, colors: s.colors });
    s.root.add(s.props.group);

    // ------------------------------------------------------------- the set
    s.env = ctx.stage.createEnv(ctx.scene);
    s.env.stageSet('field', { groundY: 0 });
    ctx.stage.setPalette('chomp-chorus', 0.4);

    ctx.fx.attach(ctx.scene);
    // We compose our own per-lane verdicts, so the bus->fx bridge must not fire
    // a second, centred copy of every one of them.
    ctx.fx.setAutoVerdict(false);
    ctx.fx.setGroundY(0);
    ctx.fx.setFocus([0, 1.5, 0]);

    ctx.stage.rig.frame({
      target: [0, 1.05, 0], distance: 6.9, height: 1.15, fov: 48, lambda: 2.6,
    });
    ctx.stage.look.setShadowFocus([0, 0, 0], 5);   // the four singers and their pads
    ctx.stage.rig.snap();
    ctx.stage.rig.setPushGain(0.75);

    // ------------------------------------------------------------ lane state
    s.lanes = [];
    for (let i = 0; i < N; i++) {
      s.lanes.push({
        alive: 1, aliveT: 1, gulp: 0, armed: 0, armedT: 0, sing: 0, flinch: 0,
        stat: { perfect: 0, great: 0, good: 0, miss: 0, notes: 0 },
      });
    }

    // ---------------------------------------------------------------- judge
    s.judge = new NoteJudge();
    const chart = buildChart();
    s.chart = chart;
    s.maxPoints = chart.reduce((a, n) => a + SCORE.perfect * (n.finale ? 2 : 1), 0);

    ctx.ui.hud.mount();
    this._mountVoiceDots(ctx, s);
  },

  // --------------------------------------------------------------------- UI

  /**
   * Four coloured dots under the HUD, one per voice, that go dark when that
   * voice drops out. The twist is meant to be learned BY EAR — but a player who
   * cannot hear the game at all still deserves to know which lane they lost, so
   * the same fact is available (quietly, peripherally) in vision.
   */
  _mountVoiceDots(ctx, s) {
    const wrap = ctx.ui.el('cc-voices');
    wrap.style.cssText = 'position:absolute;left:50%;bottom:3.2vh;transform:translateX(-50%);'
      + 'display:flex;gap:clamp(8px,1.4vw,18px);pointer-events:none;';
    for (let i = 0; i < N; i++) {
      const d = ctx.ui.el('cc-voice');
      const hex = '#' + s.colors[i].getHexString(THREE.SRGBColorSpace);
      d.style.cssText = 'width:clamp(14px,2.1vw,26px);height:clamp(14px,2.1vw,26px);border-radius:50%;'
        + `background:${hex};box-shadow:0 0 14px ${hex},0 2px 0 rgba(0,0,0,.5);`
        + 'transition:opacity .12s linear,transform .12s linear;';
      wrap.appendChild(d);
      s.dotEls.push(d);
    }
    ctx.ui.layer.appendChild(wrap);
    s.voiceDots = wrap;
  },

  // ------------------------------------------------------------------ start

  start(ctx) {
    const s = this._;
    const clock = ctx.clock;
    clock.setBpm(this.bpm);
    clock.start(clock.now() + 0.55, -LEAD_BEATS);

    // ------------------------------------------------------------- the chart
    const notes = s.chart.map((n) => ({
      time: clock.timeAt(n.beat),
      action: LANES[n.lane].action,
      lane: n.lane,
      beat: n.beat,
      // Doubled from 1 beat: playtesting found the tempo too fast to react to
      // the breath-ring telegraph in time.
      lead: n.lead ?? 2,
      finale: !!n.finale,
      intro: !!n.intro,
      run: n.run ?? null,
    }));
    s.judge.load(notes);
    s.notes = s.judge.notes;

    // Per-lane lookahead lists. The telegraph asks "what is the next thing this
    // lane has to do", which is also exactly what the player asks.
    s.laneList = [[], [], [], []];
    for (const n of s.notes) s.laneList[n.lane].push({ beat: n.beat, lead: n.lead, note: n });
    // Lead-in demonstration: a cascade, one lane per beat, so a player who has
    // never seen this watches a full gulp-and-sing on every lane before a
    // single note is scored.
    for (let i = 0; i < N; i++) {
      s.laneList[i].push({ beat: -LEAD_BEATS + 1 + i, lead: 1, demo: true });
      s.laneList[i].sort((a, b) => a.beat - b.beat);
    }

    // ------------------------------------------------------------ audio cues
    // A flat, sorted cue list drained by a cursor: no per-frame allocation, no
    // `clock.at()` re-sorting a 300-entry array on load.
    const cues = [];
    let lastGulp = [-99, -99, -99, -99];
    for (const n of s.notes) {
      const g = n.beat - n.lead * 0.94;
      if (g - lastGulp[n.lane] > 0.45) {
        cues.push({ beat: g, kind: 'gulp', lane: n.lane, big: n.lead > 1 });
        lastGulp[n.lane] = g;
      }
    }
    for (let i = 0; i < N; i++) cues.push({ beat: -LEAD_BEATS + i + 0.1, kind: 'gulp', lane: i });
    for (let bar = -FEEL.leadInBars; bar * 4 <= FINALE_END; bar++) cues.push({ beat: bar * 4, kind: 'bed' });
    cues.push({ beat: FINALE_END, kind: 'finish' });
    cues.sort((a, b) => a.beat - b.beat);
    for (const c of cues) c.time = clock.timeAt(c.beat);
    s.cues = cues;
    s.cueAt = 0;

    // --------------------------------------------------------------- judging
    s.judge.onJudged = (note, verdict, errMs) => this._onJudged(ctx, note, verdict, errMs);

    // ------------------------------------------------------------ transport
    // Beat listeners live on the clock, which OUTLIVES the scene — unsubscribe
    // in dispose() or this fires forever inside the next minigame.
    s.unsubBeat = clock.onBeat((b, t) => {
      if (b >= 0) return;
      ctx.audio.sfx('count', t, ((b % 4) + 4) % 4);
      if (b >= -4) ctx.ui.banner(String(-b), { life: 0.52, color: '#ffe9a8' });
    });

    ctx.audio.music.play('chomp-chorus');
    ctx.ui.banner(this.name, { sub: 'Four lanes. One groove.', life: 1.5 });
  },

  // -------------------------------------------------------------- harmony

  /**
   * The chord tones the track is on at `beat`, derived from the track's own
   * progression. `music.info()` reports where the loop currently is; from that
   * we recover the beat at which the loop started and can then evaluate ANY
   * beat, including ones that have not happened yet — which is what scheduling
   * a bar of choir a lookahead early requires.
   */
  _chordAt(ctx, beat) {
    const s = this._;
    const music = ctx.audio.music;
    const trk = music.track;
    const info = music.info();
    if (!trk || !info || !trk.prog || !trk.prog.length) return FALLBACK_CHORD;
    const bpb = trk.beatsPerBar || 4;
    const loopBeats = (trk.bars || 16) * bpb;
    const now = ctx.clock.beat;
    if (s.trackOffsetBeat === null || now - s.trackOffsetAt > 1) {
      s.trackOffsetBeat = now - (info.bar + info.barFrac) * bpb;
      s.trackOffsetAt = now;
    }
    let rel = (beat - s.trackOffsetBeat) % loopBeats;
    if (rel < 0) rel += loopBeats;
    const bar = Math.floor(rel / bpb);
    const deg = trk.prog[bar % trk.prog.length];
    return chordTones(trk.root, SCALES[trk.scale] || SCALES.dorian, deg, trk.shape || [0, 2, 4, 6]);
  },

  /** Lane i owns chord tone i, two octaves up: left is low, right is high. */
  _midiFor(ctx, lane, beat) {
    const c = this._chordAt(ctx, beat);
    return (c[lane] ?? c[c.length - 1]) + 24;
  },

  // ---------------------------------------------------------------- audio

  _sing(ctx, lane, verdict, at, finale) {
    const V = ctx.audio.voices;
    const L = LANES[lane];
    const midi = this._midiFor(ctx, lane, ctx.clock.beatAt(at));
    const spb = ctx.clock.spb;
    const gain = verdict === 'perfect' ? 0.30 : verdict === 'great' ? 0.25 : 0.18;
    V.lead(at, {
      midi, dur: finale ? spb * 4 : spb * 0.85, gain: finale ? 0.34 : gain,
      detune: 13, cutoff: 3.6, res: 2.4, pan: L.pan, rev: 0.32, dly: 0.12, vib: 0.9,
    });
    if (verdict === 'perfect' || finale) {
      V.bell(at + 0.012, {
        midi: midi + 12, dur: 0.55, gain: 0.09, ratio: 2.01, index: 4,
        rev: 0.4, dly: 0.2, pan: L.pan,
      });
    }
  },

  /** The hole: a lane that just died gets a deflating raspberry in its ear. */
  _sour(ctx, lane, at) {
    const V = ctx.audio.voices;
    const L = LANES[lane];
    const f = mtof(this._midiFor(ctx, lane, ctx.clock.beatAt(at)) - 12);
    V.tone(at, { freq: f, type: 'sawtooth', bend: 0.45, d: 0.30, gain: 0.13, pan: L.pan, rev: 0.12 });
    V.hit(at, { gain: 0.05, d: 0.09, hp: 240, lp: 1400, pan: L.pan });
  },

  _gulp(ctx, lane, at, big) {
    const V = ctx.audio.voices;
    const L = LANES[lane];
    const spb = ctx.clock.spb;
    V.tone(at, {
      freq: 150 + lane * 26, type: 'sine', bend: big ? 4.4 : 3.0,
      d: spb * (big ? 3.4 : 0.62), gain: big ? 0.075 : 0.05, pan: L.pan, rev: 0.14,
    });
    V.hit(at, { gain: 0.045, d: 0.05, hp: 1500, pan: L.pan, rev: 0.05 });
  },

  /** One bar of sustained four-part harmony — minus every voice that is out. */
  _bed(ctx, atTime, atBeat) {
    const s = this._;
    const V = ctx.audio.voices;
    const spb = ctx.clock.spb;
    for (let i = 0; i < N; i++) {
      if (!s.lanes[i].alive) continue;
      V.lead(atTime, {
        midi: this._midiFor(ctx, i, atBeat), dur: spb * 3.9, gain: 0.062,
        detune: 17, cutoff: 1.5, res: 1.1, pan: LANES[i].pan, rev: 0.45, dly: 0, vib: 0.5,
      });
    }
  },

  // --------------------------------------------------------------- verdicts

  _onJudged(ctx, note, verdict, errMs) {
    const s = this._;
    const i = note.lane;
    const lane = s.lanes[i];
    const now = ctx.clock.now();
    const combo = s.judge.stats.combo;
    const f = feelForCombo(verdict, combo);
    const hit = verdict !== 'miss';
    // Four notes of a chord judge in the same frame. Camera, flash and the
    // world callout belong to the group, not to each member, or a four-part
    // chord reads as one white flash and nothing else.
    const dense = now - s.lastVerdictAt < 0.055;
    s.lastVerdictAt = now;

    lane.stat.notes++;
    lane.stat[verdict]++;

    const wasAlive = lane.alive;
    lane.alive = hit ? 1 : 0;

    const pressAt = Math.max(note.time + errMs / 1000, ctx.audio.ctx.currentTime + 0.004);

    if (hit) {
      lane.sing = 1;
      s.cast.react(i, verdict);
      this._sing(ctx, i, verdict, pressAt, note.finale);
      if (!wasAlive) {
        // A voice coming back is a moment: it is the thing the player was
        // trying to fix.
        ctx.fx.flare([LANES[i].x, s.headY[i] + 0.5, 0], { color: s.colors[i].getHex(), size: 0.9, life: 0.3, to: 2.6 });
        ctx.fx.ring([LANES[i].x, s.headY[i], 0], {
          color: s.colors[i].getHex(), life: 0.45, from: 0.3, to: 2.2, thick0: 0.2, thick1: 0.02,
        });
      }
    } else {
      lane.sing = 0;
      s.cast.react(i, 'miss');
      if (wasAlive) this._sour(ctx, i, pressAt);
      // Grey smoke where a bright note should have been.
      ctx.fx.burst([LANES[i].x, s.headY[i], 0.1], { color: 0x8d7fa6, count: 10, speed: 2.6, size: 0.12, life: 0.55 });
    }

    if (!dense) ctx.audio.sfx(verdict, pressAt);

    const scale = note.finale ? 1.35 : (dense ? 0.82 : 1);
    ctx.fx.verdict(verdict, [LANES[i].x, s.headY[i] + 0.12, 0.05], {
      dir: [LANES[i].x * 0.09, 1, 0.15],
      combo, scale, stage: !dense, text: !dense,
      groundY: s.pedTop[i],
    });

    ctx.hitstop(note.finale ? FEEL.hitstop.finale : f.hitstop);
    ctx.bus.emit('judge', { verdict, errMs, beat: note.beat, lane: i });

    if (note.finale && hit) s.finale.hit++;

    ctx.ui.hud.setCombo(s.judge.stats.combo);
    ctx.ui.hud.setAccuracy(s.judge.accuracy);
    ctx.ui.hud.setScore(this._points(s) / Math.max(1, s.maxPoints) * 1000);
  },

  _points(s) {
    let p = 0;
    for (const n of s.notes) if (n.judged) p += SCORE[n.verdict] * (n.finale ? 2 : 1);
    return p;
  },

  // ------------------------------------------------------------------ input

  input(ctx, events) {
    const s = this._;
    if (!s || s.over) return;
    for (const e of events) {
      if (!e.down) continue;
      const lane = ACTION_TO_LANE[e.action];
      if (lane === undefined) continue;
      const r = s.judge.press(e.action, e.time);
      if (!r) {
        // A press that claimed no note. Silence here feels broken and a full
        // verdict feels punishing, so: a tick, and the singer flinches.
        s.lanes[lane].flinch = 1;
        ctx.audio.sfx('tick');
      }
    }
  },

  // ----------------------------------------------------------------- update

  update(ctx, dt, beat) {
    const s = this._;
    if (!s) return;
    s.time += dt;
    const clock = ctx.clock;
    const now = clock.now();

    s.judge.update(now);
    this._pumpCues(ctx, now, beat);
    this._telegraph(ctx, dt, beat);
    this._finale(ctx, dt, beat);

    // ------------------------------------------------------------ characters
    s.cast.update(dt, beat);
    for (let i = 0; i < N; i++) {
      const lane = s.lanes[i];
      lane.sing = damp(lane.sing, 0, 4.2, dt);
      lane.flinch = damp(lane.flinch, 0, 9, dt);
      lane.aliveT = damp(lane.aliveT, lane.alive, 7, dt);

      const j = s.cast.get(i).char.joints;
      // Inflate as the breath is drawn in. `head` scale is the one channel the
      // animator does not own, so this layers cleanly on top of every pose.
      const puff = lane.gulp * lane.armed;
      const wob = lane.flinch * Math.sin(s.time * 46) * 0.06;
      j.head.scale.set(1 + puff * 0.30 + wob, 1 + puff * 0.24 - wob, 1 + puff * 0.30);
      // Mouth: an O while gulping, wide open while singing.
      const open = Math.max(puff * 0.5, lane.sing * 1.0);
      if (j.mouth) {
        j.mouth.scale.y *= 1 + open * 0.85;
        j.mouth.scale.x *= 1 + open * 0.25;
      }
      if (j.gape) j.gape.scale.multiplyScalar(0.35 + open * 2.4);
      // A dead voice literally dims: the rim/accent light on it drops away.
      j.head.position.z = -0.02 * (1 - lane.aliveT);

      const d = s.dotEls[i];
      if (d) {
        d.style.opacity = String(0.18 + 0.82 * lane.aliveT);
        d.style.transform = `scale(${(0.72 + 0.28 * lane.aliveT) * (1 + lane.sing * 0.5)})`;
      }
    }

    // ---------------------------------------------------------------- props
    const v = s.view;
    for (let i = 0; i < N; i++) {
      v.gulp[i] = s.lanes[i].gulp;
      v.armed[i] = s.lanes[i].armed;
      v.sing[i] = s.lanes[i].sing;
      v.alive[i] = s.lanes[i].aliveT;
    }
    v.chordN = s.chordN;
    // Beat phase is `x - floor(x)`, NEVER `x % 1`: the transport runs negative
    // beats through the lead-in and `%` keeps the dividend's sign.
    v.beatPhase = beat - Math.floor(beat);
    s.props.update(v);

    if (!s.over && beat >= END_BEAT) {
      s.over = true;
      s.done = true;
    }
  },

  /** Fire scheduled audio a lookahead ahead of the ear. */
  _pumpCues(ctx, now, beat) {
    const s = this._;
    const horizon = now + 0.16;
    while (s.cueAt < s.cues.length && s.cues[s.cueAt].time <= horizon) {
      const c = s.cues[s.cueAt++];
      const at = Math.max(c.time, ctx.audio.ctx.currentTime + 0.003);
      if (c.kind === 'gulp') this._gulp(ctx, c.lane, at, c.big);
      else if (c.kind === 'bed') this._bed(ctx, at, c.beat);
      else if (c.kind === 'finish') ctx.audio.sfx('fanfare', at);
    }
  },

  /**
   * Per lane: how far into its next breath is it? Everything the player reads
   * comes out of this one number.
   */
  _telegraph(ctx, dt, beat) {
    const s = this._;
    let minBeat = Infinity;
    for (let i = 0; i < N; i++) {
      const list = s.laneList[i];
      let c = s.laneCursor[i];
      while (c < list.length) {
        const e = list[c];
        if (e.demo) {
          if (beat >= e.beat) { this._demoSing(ctx, i, e.beat); c++; continue; }
          break;
        }
        if (e.note.judged || e.beat < beat - 0.7) { c++; continue; }
        break;
      }
      s.laneCursor[i] = c;

      const lane = s.lanes[i];
      const e = list[c];
      let armed = 0;
      let g = 0;
      if (e) {
        const lead = e.lead || 1;
        const d = e.beat - beat;
        if (d <= lead && d > -0.4) {
          armed = 1;
          g = clamp01(1 - d / lead);
          if (e.beat < minBeat) minBeat = e.beat;
        }
      }
      lane.armedT = damp(lane.armedT, armed, 22, dt);
      lane.armed = lane.armedT;
      lane.gulp = g;

      // Pose: brace as soon as the breath starts. The chars rig's `ready` state
      // is literally the telegraph pose — knees bent, guard up, eyes wide.
      if (armed && g > 0.02 && g < 0.999) {
        const anim = s.cast.anim(i);
        if (anim && anim.state !== 'ready' && anim.state !== 'celebrate' && anim.state !== 'fail') anim.ready();
        else if (anim && anim.state === 'celebrate' && g > 0.4) anim.ready();
      }
    }

    s.chordN = 0;
    for (let i = 0; i < N; i++) {
      const list = s.laneList[i];
      const e = list[s.laneCursor[i]];
      const on = s.lanes[i].armed > 0.01 && e && Math.abs(e.beat - minBeat) < 1e-6 ? 1 : 0;
      s.chordFlag[i] = on;
      s.chordN += on;
    }
  },

  /** Lead-in demo: an unscored gulp-and-sing so the mechanic is shown, not told. */
  _demoSing(ctx, lane, atBeat) {
    const s = this._;
    if (s.demoFired & (1 << lane)) return;
    s.demoFired |= 1 << lane;
    s.lanes[lane].sing = 1;
    s.cast.react(lane, 'great');
    const t = Math.max(ctx.clock.timeAt(atBeat), ctx.audio.ctx.currentTime + 0.004);
    this._sing(ctx, lane, 'great', t, false);
    ctx.fx.ring([LANES[lane].x, s.headY[lane], 0], {
      color: s.colors[lane].getHex(THREE.SRGBColorSpace),
      life: 0.4, from: 0.3, to: 1.8, thick0: 0.18, thick1: 0.02,
    });
  },

  /** The scripted last bar. */
  _finale(ctx, dt, beat) {
    const s = this._;
    const F = s.finale;

    if (!F.banner && beat >= FINALE_BEAT - 3.2) {
      F.banner = true;
      ctx.ui.banner('ALL FOUR!', { sub: '← ↓ ↑ →  hold it', life: 1.3, color: '#ffe9a8' });
      ctx.audio.sfx('swoosh');
      ctx.stage.rig.frame({ target: [0, 1.3, 0], distance: 6.7, height: 1.25, lambda: 1.1 });
    }

    // Wait out the late half of the claim window before deciding how big the
    // moment was: a press up to 165ms behind the beat is still a hit.
    if (beat < FINALE_BEAT + 0.45 || beat > FINALE_END + 0.5) return;

    if (!F.armed) {
      F.armed = true;
      if (F.hit > 0) {
        ctx.stage.punchZoom(1.14);
        ctx.stage.shake(FEEL.shake.finale * (F.hit / 4), [0, 1, 0]);
        ctx.stage.flash(FEEL.flash.finale * (F.hit / 4), '#fff6d8');
        ctx.fx.confetti([0, 2.2, 0], { count: 120, up: 1.2, speed: 8.5 });
        ctx.audio.sfx('impact');
      }
    }

    // Sustain: hold the beams and the voices up through the last bar. Holding
    // the four keys for real is tracked separately and reported as a stat.
    F.frames++;
    let held = 0;
    for (let i = 0; i < N; i++) {
      if (ctx.input.isHeld(LANE_ACTIONS[i])) held++;
      if (s.lanes[i].alive) s.lanes[i].sing = Math.max(s.lanes[i].sing, 0.55 + 0.45 * Math.abs(Math.sin(beat * Math.PI)));
    }
    if (held === N) F.heldFrames++;

    if (!F.done && beat >= FINALE_END) {
      F.done = true;
      ctx.audio.music.stop({ fade: 1.1 });
      ctx.stage.rig.frame({ target: [0, 1.2, 0], distance: 7.4, height: 1.2, lambda: 1.4 });
      const rank = F.hit === N ? 'FULL CHORD!' : F.hit >= 2 ? 'NICE CHORUS' : 'ROUGH NIGHT';
      ctx.ui.banner(rank, { life: 1.8, color: '#ffe9a8' });
      if (F.hit === N) ctx.fx.confetti([0, 2.6, 0], { count: 160, up: 1.3, speed: 9 });
    }
  },

  // ----------------------------------------------------------------- result

  result(ctx) {
    const s = this._;
    if (!s || !s.done) return null;
    if (s.resultCache) return s.resultCache;

    const st = s.judge.stats;
    const points = this._points(s);
    const score = Math.round(1000 * points / Math.max(1, s.maxPoints));
    const accuracy = s.judge.accuracy;

    const lanes = LANES.map((L, i) => {
      const q = s.lanes[i].stat;
      const scored = q.perfect * SCORE.perfect + q.great * SCORE.great + q.good * SCORE.good;
      return {
        lane: i,
        key: ['left', 'down', 'up', 'right'][i],
        glyph: ['<', 'v', '^', '>'][i],
        notes: q.notes,
        perfect: q.perfect, great: q.great, good: q.good, miss: q.miss,
        accuracy: q.notes ? scored / (q.notes * SCORE.perfect) : 1,
      };
    });
    let weakest = null;
    for (const l of lanes) if (l.notes && (!weakest || l.accuracy < weakest.accuracy)) weakest = l;

    s.resultCache = {
      score,
      accuracy,
      rank: rankFor(accuracy, st.miss),
      stats: {
        ...st,
        bias: s.judge.bias,
        lanes,
        weakestLane: weakest ? weakest.lane : null,
        weakestKey: weakest ? weakest.key : null,
        finaleHits: s.finale.hit,
        finaleSustain: s.finale.frames ? s.finale.heldFrames / s.finale.frames : 0,
      },
      highlights: [
        weakest && weakest.miss > 0 ? `Weakest voice: ${weakest.glyph} (${Math.round(weakest.accuracy * 100)}%)` : 'All four voices held',
        s.finale.hit === N ? 'Full four-part finale' : `${s.finale.hit}/4 in the finale`,
      ],
    };
    return s.resultCache;
  },

  // ---------------------------------------------------------------- dispose

  dispose(ctx) {
    const s = this._;
    if (!s) return;
    s.judge.onJudged = null;
    s.unsubBeat?.();
    s.props?.dispose();
    s.cast?.dispose();
    try { s.env?.dispose(); } catch { /* stage may have detached first */ }
    s.root.traverse((o) => {
      if (o.isMesh) {
        o.geometry?.dispose?.();
        const m = o.material;
        if (Array.isArray(m)) m.forEach((x) => x?.dispose?.());
        else m?.dispose?.();
      }
    });
    s.root.removeFromParent();
    s.voiceDots?.remove();
    ctx.ui.hud.unmount();
    ctx.stage.rig.release();
    this._ = null;
  },
};
