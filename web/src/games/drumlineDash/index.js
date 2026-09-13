/**
 * G2 · DRUMLINE DASH — "Hear it. Hit it back."
 * 132 bpm · 4/4 · one button
 *
 * ─────────────────────────────────────────────────────────────────────────────
 * THE IDEA
 *
 * The drum major plays a one-bar rhythm. You play it back in the next bar. It
 * is the only game in the set where the EAR leads, and everything below exists
 * to protect that.
 *
 * The pattern IS reinforced visually — five drum heads light as he plays — but
 * the light is deliberately worse than the sound (see props.js `lightHead`):
 * it starts ~70ms early, ramps over 90ms and lingers, and the head that lights
 * is chosen by a fixed sweep rather than by the note. A player who only watches
 * gets the count and the density and then drifts. A player who listens locks
 * in. That asymmetry is the design, not a bug.
 *
 * THE TWIST: it is a race. Four marchers, three of them CPU, and the standings
 * are on screen every single frame. A memory test is a quiz; a memory test you
 * can watch yourself losing is a party game.
 *
 * ─────────────────────────────────────────────────────────────────────────────
 * WHOSE TURN IS IT — solved five ways at once, none of them a text label
 *
 *   LIGHT   exactly one beam is ever lit. Magenta over the drum major on the
 *           call, gold over you on the response. It snaps, it does not fade.
 *   CAMERA  the call pushes in on the podium; the response pulls back to frame
 *           the pack. The framing is different enough to read in one frame.
 *   COLOUR  the lit lane on the ground moves from his podium ring to your lane.
 *   SOUND   he plays tuned toms and rim, wet and panned right. You play a dry
 *           snare, panned left. Different instrument, different space.
 *   MUSIC   the track ducks under his call and comes back up under your answer,
 *           so even with your eyes shut the bar you are meant to fill is the
 *           bar with room in it.
 *
 * ─────────────────────────────────────────────────────────────────────────────
 * MASHING
 *
 * A call-and-response game dies if hammering sixteenths beats it. Every press
 * inside a phrase that claims no note costs 500 points (a half-note of value)
 * and 0.18 of the bar's race advance, and it visibly staggers your marcher. A
 * player who mashes every sixteenth to catch five notes scores zero for the bar
 * and loses ground. Playing nothing at all scores more than playing everything.
 *
 * Traps deliberately avoided: beat phase is always `x - Math.floor(x)` (the
 * transport runs negative beats through the lead-in and `%` keeps the sign);
 * `fx.verdict()` is called directly so the bus bridge is disarmed and there is
 * exactly one callout per note; `damp()` everywhere, never `lerp(a,b,0.1)`.
 */

import * as THREE from 'three';
import { NoteJudge, rankFor, SCORE } from '../../core/judge.js';
import { FEEL, feelForCombo } from '../../core/feel.js';
import { damp, clamp, clamp01, makeRng, easeOutCubic } from '../../core/util.js';
import { makeCharacter, makeAnimator } from '../../chars/index.js';
import { buildChart, chartEndBeat, chartDurationBars, maxPoints, ADVANCE_WEIGHT, LEAD_BEATS, OUTRO_BEATS } from './chart.js';
import { makeTrack, makeRack, makeBeams, makeFinish, makeSnare, makeMajorGear, makeMace } from './props.js';

// ──────────────────────────────────────────────────────────────── constants

const BPM = 132;

/** Lanes, near-to-far. The player owns the nearest one: biggest, unobstructed. */
const LANE_Z = [2.0, 0.55, -0.9, -2.35];

/** Where the drum major stands. Ahead of the band and to the right. */
const MAJOR = { x: 7.7, y: 0, z: -2.5 };

/** Facing for everyone. Marchers angle toward the camera so faces read. */
const MARCH_YAW = 0.78;
const MAJOR_YAW = -0.52;
const RACK_YAW = 2.618;

/** Framing. The only two camera states in the game, and they are far apart. */
const CAM_RESP = { target: [0.6, 1.5, 0.1], distance: 11.4, height: 3.9, yaw: 0.30, fov: 50 };
const CAM_CALL = { target: [6.6, 2.2, -1.7], distance: 8.6, height: 3.0, yaw: 0.52, fov: 46 };
/** Reused so the per-frame camera update allocates nothing. */
const _camTarget = [0, 0, 0];

/** Beam indices. */
const BEAM_MAJOR = 0;
const BEAM_YOU = 1;

const CALL_HUE = 0xff5fd0;   // magenta: his turn
const RESP_HUE = 0xffb03a;   // gold: your turn

/**
 * The field. Three opponents, tuned so the ace is beatable but only by playing
 * well: including stumbles their expected advance per bar is 0.64 / 0.73 / 0.87
 * against your 0.30 (fumble everything) .. 1.00 (flawless). You need roughly an
 * 85% bar to take CRASH, which is about where "I actually memorised that"
 * lands. Nobody is ever eliminated; last place still crosses the line.
 */
const CPUS = [
  { name: 'PICCOLO', skill: 0.70, stumble: 0.16, palette: 'lagoon', build: 'small' },
  { name: 'TENOR', skill: 0.80, stumble: 0.12, palette: 'lime', build: 'wide' },
  { name: 'CRASH', skill: 0.90, stumble: 0.08, palette: 'slate', build: 'tall' },
];

const MASH_POINTS = 500;    // value burned by one press that claimed nothing
const MASH_ADVANCE = 0.18;  // race advance burned by the same press

// ─────────────────────────────────────────────────────────────────── helpers

/** 0..1 position inside the beat. NEVER `%`: the lead-in runs negative beats. */
const beatFrac = (b) => b - Math.floor(b);

const hexStr = (h) => '#' + (h >>> 0).toString(16).padStart(6, '0');

// ─────────────────────────────────────────────────────────────────── the game

const phrases = buildChart();
const MAX_POINTS = maxPoints(phrases);
const END_BEAT = chartEndBeat(phrases);

export default {
  id: 'drumline-dash',
  name: 'Drumline Dash',
  blurb: 'Hear it. Hit it back.',
  bpm: BPM,
  durationBars: chartDurationBars(phrases),
  controls: 'a',

  // ─────────────────────────────────────────────────────────────────── load

  load(ctx) {
    const S = this._s = {};
    S.root = new THREE.Group();
    S.root.name = 'drumlineDash';
    ctx.scene.add(S.root);
    ctx.scene.userData.palette = 'drumline-dash';

    const mats = ctx.stage.look.materials;
    S.mats = mats;
    S.owned = [];   // every material this game asked the house to make

    // --- the world ---------------------------------------------------------
    // Composed from the env kit rather than built from scratch: stands, sky,
    // bunting, floaters and the arena floor all come free and, more usefully,
    // already agree with the other four games.
    ctx.stage.setPalette('drumline-dash');
    S.env = ctx.stage.createEnv(ctx.scene);
    S.env.stageSet('field', { groundY: 0, per: { crowd: { z: -6 }, floaters: { count: 16 } } });
    S.env.addBanners({ y: 8.4, radius: 14, count: 22, z: -3 });

    // --- the track ---------------------------------------------------------
    S.track = makeTrack({ mats, lanes: LANE_Z, laneWidth: 1.45, span: 30 });
    S.root.add(S.track.group);
    S.laneColors = [];

    // --- the rack ----------------------------------------------------------
    S.rack = makeRack({ mats, count: 5, radius: 2.2, y: 1.95 });
    S.rack.group.position.set(MAJOR.x, MAJOR.y, MAJOR.z);
    S.rack.group.rotation.y = RACK_YAW;
    S.root.add(S.rack.group);

    // --- beams -------------------------------------------------------------
    S.beams = makeBeams({ height: 8.6, radius: 2.5, count: 2 });
    S.beams.place(BEAM_MAJOR, MAJOR.x, 0.02, MAJOR.z);
    S.beams.setColor(BEAM_MAJOR, CALL_HUE);
    S.beams.place(BEAM_YOU, 0, 0.02, LANE_Z[0]);
    S.beams.setColor(BEAM_YOU, RESP_HUE);
    S.root.add(S.beams.mesh);

    // --- finish gate -------------------------------------------------------
    S.finish = makeFinish({ mats, width: 11.5, height: 5.4 });
    S.root.add(S.finish.group);

    // --- the racers --------------------------------------------------------
    S.racers = [];
    const mkRacer = (i, name, palette, build, detail) => {
      const char = makeCharacter({
        palette, build, detail, scale: i === 0 ? 1.45 : 1.34,
        seed: 0x0d00 + i * 7919, name: `dd:${name}`,
      });
      char.position.set(0, 0, LANE_Z[i]);
      char.rotation.y = MARCH_YAW;
      S.root.add(char);
      const r = {
        i, name, char, anim: makeAnimator(char, { seed: 0x51e + i * 131 }),
        progress: 0, x: 0, xTarget: 0, place: i + 1,
        color: char.palette.body, isCpu: i > 0,
      };
      S.racers.push(r);
      S.laneColors.push(char.palette.body);
      return r;
    };

    // The field is the shell's lineup: P1 marches in lane one, the named CPU
    // rivals take the next lanes at a pace set by their difficulty, and house
    // drummers fill any empty lane. Booted cold, it's the house field.
    const hero = ctx.players?.[0];
    const rivals = (ctx.players || []).filter((p) => p.isCpu).slice(0, CPUS.length);
    S.you = mkRacer(0, 'YOU', hero?.palette ?? 'sunburst', hero?.build || 'round', 'full');
    S.you.playerId = hero ? hero.id : null;
    hero?.dress?.(S.you.char);
    CPUS.forEach((c, k) => {
      const p = rivals[k];
      // A party races only its own lineup: a house drummer in the spare lane
      // ranked you 4th on the board while the party called it 3rd.
      // (Free Play's lineup is you alone: no rivals seated, so the house races.)
      if (!p && rivals.length) { S.laneColors.push(0x3a2a55); return; }
      const r = mkRacer(k + 1, p ? p.name : c.name, p ? p.palette : c.palette, p ? p.build : c.build, 'lite');
      if (p) {
        // Easy .40 → 0.73 pace, hard .86 → 0.90: the ace is still CRASH-fast.
        r.skill = 0.58 + 0.37 * p.cpuSkill;
        r.stumbleChance = 0.28 - 0.23 * p.cpuSkill;
        r.playerId = p.id;
        p.dress?.(r.char);
      } else {
        r.skill = c.skill;
        r.stumbleChance = c.stumble;
        r.playerId = null;
      }
    });

    // the player carries a snare, so "you are the drummer" needs no caption
    S.snare = makeSnare({ mats });
    S.snare.mesh.position.set(0.30, -0.02, 0.24);
    S.you.char.attach('hips', S.snare.mesh);

    // --- the drum major ----------------------------------------------------
    S.major = makeCharacter({
      palette: 'orchid', build: 'tall', detail: 'lite', scale: 1.6,
      seed: 0xd12, name: 'dd:major',
    });
    S.major.position.set(MAJOR.x, 1.14, MAJOR.z);
    S.major.rotation.y = MAJOR_YAW;
    S.root.add(S.major);
    S.majorAnim = makeAnimator(S.major, { seed: 0x9a1 });

    S.shako = makeMajorGear({ mats });
    S.major.attach('head', S.shako.mesh);
    S.mace = makeMace({ mats });
    S.mace.mesh.rotation.x = -0.22;
    S.major.attach('handR', S.mace.mesh);

    // --- fx / judge --------------------------------------------------------
    ctx.fx.attach(ctx.scene);
    ctx.fx.setAutoVerdict(false);   // we compose our own; never two callouts
    ctx.fx.setGroundY(0);
    ctx.fx.setFocus([1.2, 1.4, 0.4]);

    S.judge = new NoteJudge();
    S.rng = makeRng(0xd12b + (ctx.opts?.seed | 0));

    // --- state -------------------------------------------------------------
    S.phraseIdx = 0;
    S.mode = 'lead';
    S.armedIdx = -1;
    S.callCursor = 0;
    S.callEvents = [];
    S.bar = { weight: 0, notes: 0, extras: 0 };
    S.tot = { perfect: 0, great: 0, good: 0, miss: 0 };
    S.points = 0;
    S.combo = 0;
    S.maxCombo = 0;
    S.extras = 0;
    S.errors = [];
    S.speed = 4.4;
    S.speedTarget = 4.4;
    S.callGlow = 0;
    S.finished = false;
    S.result = null;
    S.time = 0;
    S.finaleAnnounced = false;

    // --- hud ---------------------------------------------------------------
    ctx.ui.hud.mount();
    mountStandings(ctx, S);

    // Every material we asked the house material system for. Each prop frees
    // its own; this list exists so dispose() can also drop them from the
    // system's registry and a later global teardown cannot double-free.
    for (const p of [S.track, S.rack, S.finish]) S.owned.push(...(p.materials || []));
    S.owned.push(S.snare.mat, S.shako.mat, S.mace.mat);
  },

  // ────────────────────────────────────────────────────────────────── start

  start(ctx) {
    const S = this._s;
    ctx.clock.setBpm(BPM);
    ctx.clock.start(ctx.clock.now() + 0.45, -LEAD_BEATS);

    ctx.audio.music.play('drumline-dash');
    ctx.audio.setVolume('music', 0.72);
    S.musicDuck = 0.72;

    ctx.stage.rig.frame({ ...CAM_RESP, lambda: 3.4, immediate: true });
    ctx.stage.look.setShadowFocus('rig', 9);   // real shadows follow the camera between call and response
    ctx.stage.rig.setPushGain(0.7);

    // --- schedule every call, once, at absolute audio times ----------------
    // Lookahead scheduling through the clock: a sound that says "now" is a
    // sound that is already late, and this game lives or dies on the call
    // being exactly where it claims to be.
    const lat = () => ctx.clock.outputLatency;
    for (const p of phrases) {
      p.callBeats.forEach((b, i) => {
        S.callEvents.push({ beat: b, phrase: p, i, n: p.callBeats.length });
        ctx.clock.at(b, (t) => playCall(ctx, t + lat(), p, i));
      });
      // the handoff: a short rise on the last beat of the call bar tells the
      // ear "it comes to you next" a full beat early
      ctx.clock.at(p.respBeat - 1, (t) => {
        ctx.audio.voices.hat(t + lat(), { gain: 0.16, open: 0.22, pan: 0.1, dest: ctx.audio.sfxBus });
      });
    }
    S.callEvents.sort((a, b) => a.beat - b.beat);

    // --- beat-locked countdown --------------------------------------------
    S.unsubBeat = ctx.onBeat((b, t) => {
      if (b >= 0) return;
      if (b >= -LEAD_BEATS && b < -4) {
        ctx.audio.sfx('count', t + lat(), ((b % 4) + 4) % 4);
        ctx.ui.banner(String(-4 - b), { life: 0.40, color: hexStr(RESP_HUE) });
      }
    });

    S.judge.onJudged = (note, verdict, errMs) => onJudged(ctx, S, note, verdict, errMs);

    ctx.ui.banner('DRUMLINE DASH', { sub: 'Hear it. Hit it back.', life: 1.5 });
  },

  // ───────────────────────────────────────────────────────────────── update

  update(ctx, dt, beat) {
    const S = this._s;
    S.time += dt;
    const now = ctx.clock.now();
    const bp = ctx.stage.beatPulse;

    // ---- phrase state machine --------------------------------------------
    // Resolved in a loop so a phrase that ends this frame and the next phrase
    // that starts this frame both land on the same frame: at 132bpm the call's
    // downbeat and the previous bar's close are the SAME instant, and putting
    // a frame between them would put a hole in the groove.
    let guard = 0;
    while (guard++ < 4) {
      const p = phrases[S.phraseIdx];
      if (!p) {
        if (!S.finished) finishRound(ctx, S);
        break;
      }
      // Arm the judge a full bar early. `input()` runs BEFORE `update()` in the
      // frame loop, so loading on the response downbeat would drop any press
      // that lands in the same frame the bar turns over — and a press slightly
      // ahead of the first note is a legitimate early hit, not a stray.
      if (S.armedIdx !== p.index && beat >= p.callBeat - 0.02) armPhrase(ctx, S, p);

      const nextMode = beat < p.callBeat ? 'lead' : beat < p.respBeat ? 'call' : 'resp';
      if (nextMode !== S.mode) {
        S.mode = nextMode;
        if (nextMode === 'call') onCallStart(ctx, S, p);
        if (nextMode === 'resp') onRespStart(ctx, S, p);
      }
      if (beat < p.endBeat) break;
      closePhrase(ctx, S, p);
      S.phraseIdx++;
      S.mode = 'lead';
    }

    S.judge.update(now);

    // ---- the call: light the heads (late, soft, smeared on purpose) -------
    while (S.callCursor < S.callEvents.length) {
      const ev = S.callEvents[S.callCursor];
      // 70ms of visual lead + a 90ms ramp: the eye gets a blur straddling the
      // note while the ear gets a transient exactly on it.
      if (now < ctx.clock.timeAt(ev.beat) - 0.07) break;
      S.callCursor++;
      S.rack.lightHead(ev.i, 1);
      S.callGlow = 1;
      S.majorAnim.strike({ power: 0.85 });
      if (ev.i === 0) ctx.stage.pulse(1.1);
    }

    // ---- animation --------------------------------------------------------
    const onCall = S.mode === 'call';
    S.callGlow = damp(S.callGlow, onCall ? 0.55 : 0, 6, dt);

    for (const r of S.racers) {
      r.anim.update(dt, beat);
      r.x = damp(r.x, r.xTarget, 2.6, dt);
      r.char.position.x = r.x;
      // marchers lean into the run; the leader leans hardest
      r.char.rotation.y = MARCH_YAW - clamp(r.x, -5, 5) * 0.018;
    }
    S.majorAnim.update(dt, beat);

    // ---- the field moves --------------------------------------------------
    S.speed = damp(S.speed, S.speedTarget, 2.2, dt);
    S.track.update(dt, S.speed, bp);
    S.track.setLaneColors(S.laneColors, S.mode === 'resp' ? 0 : -1);

    // ---- beams ------------------------------------------------------------
    S.beams.place(BEAM_YOU, S.you.x, 0.02, LANE_Z[0]);
    S.beams.set(BEAM_MAJOR, onCall ? 1 : 0.06);
    S.beams.set(BEAM_YOU, S.mode === 'resp' ? 1 : 0.05);
    S.beams.update(dt, bp);
    S.rack.update(dt, 0x2a1030, CALL_HUE, onCall);

    // ---- camera -----------------------------------------------------------
    const want = onCall ? CAM_CALL : CAM_RESP;
    if (onCall) {
      _camTarget[0] = want.target[0]; _camTarget[1] = want.target[1]; _camTarget[2] = want.target[2];
    } else {
      // On your bar the frame drifts with you: pulling ahead literally moves
      // the camera, so leading feels like leading.
      _camTarget[0] = 0.6 + S.you.x * 0.24;
      _camTarget[1] = 1.5;
      _camTarget[2] = 0.1;
    }
    ctx.stage.rig.frame({
      target: _camTarget, distance: want.distance, height: want.height,
      yaw: want.yaw, fov: want.fov, lambda: 3.6,
    });

    // ---- finish gate ------------------------------------------------------
    const last = phrases[phrases.length - 1];
    const toEnd = last.endBeat - beat;
    if (toEnd < 12) {
      S.finish.show(true);
      const t = clamp01((12 - toEnd) / 12);
      S.finish.group.position.x = 2.5 + 52 * (1 - easeOutCubic(t));
    }

    // ---- hud --------------------------------------------------------------
    const score01 = clamp01(S.points / MAX_POINTS);
    ctx.ui.hud.setScore(Math.round(score01 * 1000));
    ctx.ui.hud.setCombo(S.combo);
    ctx.ui.hud.setAccuracy(score01);
    updateStandings(S);
  },

  // ────────────────────────────────────────────────────────────────── input

  input(ctx, events) {
    const S = this._s;
    if (S.finished) return;
    for (const e of events) {
      if (!e.down || e.action !== 'a') continue;
      const r = S.judge.press('a', e.time);
      if (r) continue;

      // Claimed nothing. Not a miss — a fumble. It costs, it is audible, and
      // it visibly staggers the marcher, so mashing reads as mashing.
      const p = phrases[S.phraseIdx];
      if (!p || S.mode === 'lead') { ctx.audio.sfx('tick', undefined, FEEL.ghostPressTick); continue; }
      const mul = p.finale ? 2 : 1;
      S.extras++;
      S.bar.extras++;
      S.points = Math.max(0, S.points - MASH_POINTS * mul);
      S.combo = 0;
      fumble(ctx, S);
    }
  },

  // ───────────────────────────────────────────────────────────────── result

  result() {
    return this._s?.result ?? null;
  },

  // ──────────────────────────────────────────────────────────────── dispose

  dispose(ctx) {
    const S = this._s;
    if (!S) return;
    S.unsubBeat?.();
    ctx.clock.clearSchedule();

    for (const r of S.racers) r.char.dispose();
    S.major?.dispose();

    S.track?.dispose();
    S.rack?.dispose();
    S.beams?.dispose();
    S.finish?.dispose();

    // attachments
    for (const a of [S.snare, S.shako, S.mace]) {
      if (!a) continue;
      a.mesh.removeFromParent();
      a.geo.dispose();
      a.mat.dispose();
    }

    // The props freed their own materials above; drop them from the house
    // registry too so a later global teardown cannot double-free.
    const reg = S.mats?.owned;
    if (reg) for (const m of S.owned) reg.delete(m);

    S.env?.dispose();
    S.root?.removeFromParent();

    unmountStandings(S);
    ctx.ui.hud.unmount();
    ctx.audio.setVolume('music', 0.75);
    ctx.stage.rig.release();
    ctx.stage.rig.setPushGain(1);
    ctx.fx.setAutoVerdict(true);
    this._s = null;
  },

  // ───────────────────────────────────────── test hooks (harness + verify)

  /**
   * What the drum major is playing / just played, in beats. The bundled
   * `verify-call-response.mjs` reads this to answer a call exactly, which is
   * the only way to prove a call-and-response chart is actually beatable —
   * the shell's generic eighth-note autoplay cannot.
   */
  __probe() {
    const S = this._s;
    if (!S) return null;
    const p = phrases[S.phraseIdx] || null;
    return {
      mode: S.mode,
      phrase: p && {
        index: p.index, section: p.section, bars: p.bars, finale: p.finale,
        callBeat: p.callBeat, respBeat: p.respBeat, endBeat: p.endBeat,
        slots: p.slots, callBeats: p.callBeats, noteBeats: p.noteBeats,
      },
      totals: { ...S.tot },
      points: S.points,
      maxPoints: MAX_POINTS,
      extras: S.extras,
      combo: S.combo,
      maxCombo: S.maxCombo,
      progress: S.racers.map((r) => ({ name: r.name, p: r.progress, place: r.place })),
      finished: S.finished,
    };
  },
};

// ═════════════════════════════════════════════════════════════════ internals

/** Load the response notes. Called one bar early — see the note at the call. */
function armPhrase(ctx, S, p) {
  S.armedIdx = p.index;
  S.judge.load(p.noteBeats.map((b) => ({ time: ctx.clock.timeAt(b), action: 'a', data: p })));
  S.bar = { weight: 0, notes: p.noteBeats.length, extras: 0 };
}

function onCallStart(ctx, S, p) {
  S.speedTarget = 4.0;
  S.majorAnim.setState('ready');
  S.you.anim.setState('ready');
  // duck the track so the call sits in a hole in the mix
  ctx.audio.setVolume('music', p.finale ? 0.30 : 0.34);
  ctx.audio.voices.crash(ctx.clock.rawNow() + 0.01, {
    gain: p.finale ? 0.30 : 0.16, decay: 0.9, rev: 0.45, pan: 0.35, dest: ctx.audio.sfxBus,
  });
  if (p.finale && !S.finaleAnnounced) {
    S.finaleAnnounced = true;
    ctx.ui.banner('FINALE', { sub: 'two bars — give it back whole', life: 1.5, color: hexStr(CALL_HUE) });
    ctx.stage.punchZoom(1.10);
    ctx.stage.flash(0.22, hexStr(CALL_HUE));
    S.env.crowd?.cheer?.(1.4);
  }
}

function onRespStart(ctx, S, p) {
  S.speedTarget = 5.6;
  S.majorAnim.setState('taunt');
  S.you.anim.setState('ready');
  ctx.audio.setVolume('music', 0.72);
  S.track.flashLane(0, 1);
}

/** A press that claimed no note. Funny, costly, unmistakable. */
function fumble(ctx, S) {
  const r = S.you;
  r.stumbleT = 1;
  r.anim.impulse(-0.85, 0);
  r.anim.setState('recover', { variant: 'good', force: true });
  const t = ctx.clock.rawNow() + 0.005;
  ctx.audio.voices.rim(t, { gain: 0.34, pan: -0.3, rev: 0.06, dest: ctx.audio.sfxBus });
  ctx.audio.voices.hit(t, { gain: 0.16, d: 0.07, hp: 260, lp: 1100, dest: ctx.audio.sfxBus });
  ctx.stage.shake(0.05, [-1, -0.2, 0]);
  ctx.fx.burst([r.x, 0.55, LANE_Z[0]], { color: 0x8a7fa8, count: 8, speed: 4.2, size: 0.11 });
  S.track.flashLane(0, 0.5);
}

/** One judged response note. */
function onJudged(ctx, S, note, verdict, errMs) {
  const p = note.data;
  const mul = p?.finale ? 2 : 1;
  const hit = verdict !== 'miss';

  S.tot[verdict]++;
  S.points += SCORE[verdict] * mul;
  S.bar.weight += ADVANCE_WEIGHT[verdict];
  if (hit) {
    S.combo++;
    S.maxCombo = Math.max(S.maxCombo, S.combo);
    S.errors.push(errMs);
  } else {
    S.combo = 0;
  }

  const r = S.you;
  const pos = [r.x + 0.25, 1.35, LANE_Z[0] + 0.25];
  const f = feelForCombo(verdict, S.combo);

  // ONE layered response, composed by fx. The bus event is emitted afterwards
  // purely for telemetry — the auto-bridge was disarmed in load(), so this
  // cannot produce a second callout.
  ctx.fx.verdict(verdict, pos, {
    dir: [0.75, 0.62, 0.22],
    combo: S.combo,
    scale: mul > 1 ? 1.35 : 1,
    groundY: 0,
  });
  ctx.hitstop(p?.finale ? Math.min(FEEL.hitstopMax, f.hitstop * 1.5) : f.hitstop);

  const t = ctx.clock.rawNow() + 0.005;
  if (hit) {
    ctx.audio.voices.snare(t, {
      gain: 0.42 + (verdict === 'perfect' ? 0.16 : 0), decay: 0.12,
      snap: 1.25, tone: 1.12, rev: 0.10, pan: -0.18, dest: ctx.audio.sfxBus,
    });
    ctx.audio.sfx(verdict, t);
    r.anim.strike({ power: verdict === 'perfect' ? 1.1 : 0.8 });
    S.track.flashLane(0, verdict === 'perfect' ? 1 : 0.6);
    S.env.crowd?.pulse?.(verdict === 'perfect' ? 1.3 : 0.9, 0);
  } else {
    ctx.audio.sfx('miss', t);
    r.anim.impulse(-0.5, 0);
  }

  ctx.bus.emit('judge', { verdict, errMs, beat: ctx.clock.beatAt(note.time) });
}

/** The bar is over: everybody moves, and you can see who moved further. */
function closePhrase(ctx, S, p) {
  const mul = p.finale ? 2 : 1;

  // --- you ---------------------------------------------------------------
  const q = clamp01((S.bar.notes ? S.bar.weight / S.bar.notes : 0) - MASH_ADVANCE * S.bar.extras);
  const adv = (0.30 + 0.70 * q) * mul;
  S.you.progress += adv;

  if (q >= 0.92) {
    S.you.anim.react('perfect');
    S.env.crowd?.cheer?.(p.finale ? 1.6 : 1.0);
    ctx.fx.confetti([S.you.x, 1.8, LANE_Z[0]], { count: p.finale ? 90 : 34, up: 1 });
  } else if (q >= 0.6) {
    S.you.anim.react('great');
  } else if (q >= 0.3) {
    S.you.anim.react('good');
  } else {
    S.you.anim.react('miss');
    S.env.crowd?.pulse?.(0.3, 2);
  }

  // --- the field ---------------------------------------------------------
  for (let i = 1; i < S.racers.length; i++) {
    const r = S.racers[i];
    let a = r.skill + (S.rng() - 0.5) * 0.18;
    const stumbled = S.rng() < r.stumbleChance;
    if (stumbled) a = 0.22;
    r.progress += a * mul;
    if (stumbled) {
      r.anim.react('miss');
      r.stumbleT = 1;
      ctx.audio.voices.tom(ctx.clock.rawNow() + 0.02, {
        freq: 96, gain: 0.24, decay: 0.3, pan: (i - 2) * 0.3, rev: 0.3, dest: ctx.audio.sfxBus,
      });
    } else if (a > r.skill + 0.04) {
      r.anim.react('great');
    }
  }

  restand(S);
}

/** Relative X + standings. tanh keeps the whole field on screen forever. */
function restand(S) {
  let mean = 0;
  for (const r of S.racers) mean += r.progress;
  mean /= S.racers.length;
  for (const r of S.racers) {
    r.xTarget = Math.tanh((r.progress - mean) * 0.55) * 5.1;
  }
  const order = S.racers.slice().sort((a, b) => b.progress - a.progress);
  order.forEach((r, k) => { r.place = k + 1; });
}

function finishRound(ctx, S) {
  S.finished = true;
  restand(S);
  const score01 = clamp01(S.points / MAX_POINTS);
  const place = S.you.place;
  ctx.ui.banner(place === 1 ? 'WINNER!' : `${place}${['st', 'nd', 'rd', 'th'][place - 1]} PLACE`, {
    sub: `${S.tot.perfect} perfect · best streak ${S.maxCombo}`,
    life: 2.4,
    color: hexStr(place === 1 ? RESP_HUE : 0xffffff),
  });
  ctx.audio.sfx('fanfare', ctx.clock.rawNow() + 0.02);
  ctx.stage.punchZoom(1.14);
  ctx.env?.crowd?.cheer?.(1.6);
  S.env.crowd?.cheer?.(1.6);
  ctx.fx.confetti([S.you.x, 2.2, LANE_Z[0]], { count: 120, up: 1.2 });
  S.you.anim.setState(place === 1 ? 'celebrate' : 'taunt', { variant: 'perfect', force: true });

  const notes = S.tot.perfect + S.tot.great + S.tot.good + S.tot.miss;
  // Accuracy is hit quality, the same number every game reports; the race
  // points (which also carry chart multipliers and mash penalties) are the score.
  const hit01 = notes
    ? (S.tot.perfect * SCORE.perfect + S.tot.great * SCORE.great + S.tot.good * SCORE.good) / (notes * SCORE.perfect)
    : 0;
  S.result = {
    score: Math.round(score01 * 1000),
    accuracy: hit01,
    rank: rankFor(hit01, S.tot.miss),
    // The race was run against the lineup: the party takes these places as-is.
    field: S.racers.map((r) => ({ id: r.playerId ?? null, place: r.place })),
    stats: {
      ...S.tot,
      notes,
      combo: S.combo,
      maxCombo: S.maxCombo,
      fumbles: S.extras,
      place,
      standings: S.racers.slice().sort((a, b) => a.place - b.place).map((r) => ({ name: r.name, place: r.place })),
      meanAbsErrMs: S.errors.length
        ? S.errors.reduce((a, b) => a + Math.abs(b), 0) / S.errors.length : 0,
      score: Math.round(score01 * 1000),
    },
  };
}

// ─────────────────────────────────────────────────────────────── call audio

/**
 * The drum major's voice.
 *
 * Tuned toms + a rim click, wet, panned right. Nothing else in the game uses
 * this timbre or this side of the stereo field, so "he is playing" and "I am
 * playing" are separable with your eyes closed — which is the requirement, not
 * a nicety, in the one game where the ear leads.
 */
const TOM_HZ = [252, 214, 180, 152, 128];

function playCall(ctx, t, p, i) {
  const V = ctx.audio.voices;
  const out = ctx.audio.sfxBus;
  const k = i % TOM_HZ.length;
  const big = p.finale ? 1.25 : 1;
  // Gain raised from 0.62/0.26 — playtesting found the call too quiet to hear
  // clearly even with the music duck in onCallStart().
  V.tom(t, {
    freq: TOM_HZ[k], gain: 0.85 * big, decay: 0.26,
    pan: 0.24 + k * 0.05, rev: 0.30, dest: out,
  });
  V.rim(t, { gain: 0.36 * big, pan: 0.34, rev: 0.22, dest: out });
  if (i === 0) V.hat(t, { gain: 0.18, open: 0.06, pan: 0.4, dest: out });
}

// ──────────────────────────────────────────────────────────────── standings

/**
 * Standings, on screen every frame. The whole twist of this game is that you
 * can SEE yourself losing, so this never hides, never animates in, and never
 * needs to be read carefully: place number, colour, name, bar.
 */
function mountStandings(ctx, S) {
  if (!document.getElementById('dd-style')) {
    const st = document.createElement('style');
    st.id = 'dd-style';
    st.textContent = `
    .dd-stand{position:absolute;left:clamp(12px,2.4vw,32px);bottom:clamp(12px,2.4vw,32px);
      display:flex;flex-direction:column;gap:.34em;font-family:system-ui,-apple-system,sans-serif;
      min-width:clamp(190px,20vw,272px);}
    .dd-row{display:flex;align-items:center;gap:.5em;padding:.22em .5em;border-radius:.5em;
      background:rgba(12,4,22,.42);transition:background .18s;}
    .dd-row--you{background:rgba(255,176,58,.20);}
    .dd-place{font-weight:900;font-size:clamp(13px,1.7vw,20px);color:#fff;width:1.3em;
      text-align:center;text-shadow:0 2px 0 rgba(0,0,0,.6);font-variant-numeric:tabular-nums;}
    .dd-swatch{width:.62em;height:1.5em;border-radius:.18em;font-size:clamp(13px,1.7vw,20px);
      box-shadow:0 0 10px currentColor;background:currentColor;flex:none;}
    .dd-name{font-weight:800;font-size:clamp(11px,1.35vw,16px);color:#fff;letter-spacing:.06em;
      text-shadow:0 2px 0 rgba(0,0,0,.6);width:5.4em;}
    .dd-bar{flex:1;height:.5em;border-radius:.25em;background:rgba(255,255,255,.16);overflow:hidden;}
    .dd-bar>i{display:block;height:100%;border-radius:.25em;background:currentColor;
      box-shadow:0 0 8px currentColor;width:0;}
    `;
    document.head.appendChild(st);
    S.styleEl = st;
  }
  const wrap = ctx.ui.el('dd-stand');
  S.rows = S.racers.map((r) => {
    const row = ctx.ui.el('dd-row' + (r.isCpu ? '' : ' dd-row--you'));
    const place = ctx.ui.el('dd-place', '1');
    const sw = ctx.ui.el('dd-swatch');
    sw.style.color = hexStr(r.color);
    const nm = ctx.ui.el('dd-name', r.name);
    const bar = ctx.ui.el('dd-bar');
    const fill = document.createElement('i');
    bar.style.color = hexStr(r.color);
    bar.appendChild(fill);
    row.append(place, sw, nm, bar);
    wrap.appendChild(row);
    return { row, place, fill, lastPlace: 0, lastPct: -1 };
  });
  ctx.ui.layer.appendChild(wrap);
  S.standWrap = wrap;
}

function updateStandings(S) {
  if (!S.rows) return;
  const total = phrases.length + 1;
  for (let i = 0; i < S.racers.length; i++) {
    const r = S.racers[i];
    const row = S.rows[i];
    if (row.lastPlace !== r.place) {
      row.lastPlace = r.place;
      row.place.textContent = String(r.place);
      row.row.style.order = String(r.place);
    }
    const pct = Math.round(clamp01(r.progress / total) * 100);
    if (pct !== row.lastPct) {
      row.lastPct = pct;
      row.fill.style.width = pct + '%';
    }
  }
}

function unmountStandings(S) {
  S.standWrap?.remove();
  S.styleEl?.remove();
  S.rows = null;
}
