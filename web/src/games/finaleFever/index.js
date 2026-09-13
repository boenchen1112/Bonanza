/**
 * G5 · FINALE FEVER — "It only gets faster."
 *
 * The last game in the series and the one the other four exist to set up. A
 * conductor-boss states a phrase built out of the verbs you were taught — the
 * commit of Swing Kings, the call-and-response of Drumline Dash, the unbroken
 * groove of Bounce Brigade, the two-lane chords of Chomp Chorus — and you play
 * it back one bar later, while the transport accelerates from 150bpm to 202.5
 * underneath both of you without ever stepping.
 *
 * ── The three things this file gets right on purpose ───────────────────────
 *
 * **1. The ramp is continuous and the chart never drifts against it.**
 * `clock.setBpm()` is called every frame with a value off a smooth curve; it
 * rebases the tempo map at "now", so the beat number is exactly continuous
 * across every one of those thousands of calls. The chart is stored in BEATS,
 * and the audio time of every note still in play is re-derived from
 * `clock.timeAt(beat)` each frame. A note whose time was computed two bars early
 * at 150bpm lands ~15ms off by the time it arrives at 190 — inside the PERFECT
 * window, but only just, and the error grows with the ramp. Re-deriving makes it
 * exactly zero at every tempo, which is why `verify-ramp.mjs` can hold 100%
 * accuracy from the first bar to the last.
 *
 * Judgement windows are milliseconds and do NOT scale with tempo. At 1.35x the
 * game is harder because the notes come faster, not because the target shrank.
 * That is the intended shape and nothing here compensates for it.
 *
 * **2. Escalation is the design, and the last four bars are the point.**
 * The telegraph shortens as the round runs: four beats of orb flight in the
 * warm-up, three in the duel, two in the fever — and then, for the solo, none at
 * all. The orbs stop, the baton stops, the boss's light dies, and the only thing
 * left telling you when to play is the music and the fact that you have answered
 * this exact six-note hook thirteen times already. Everything before the solo
 * exists to put that rhythm in the player's hands.
 *
 * **3. Hearts are tension, never ejection.**
 * Three hearts, scored per PHRASE rather than per note. A phrase you visibly
 * lost costs one; a flawless phrase buys one back. Losing all three does not end
 * the round — it drops you into SURVIVE, where the boss goes into overdrive, the
 * room turns red, and you keep playing for score. Being knocked out of the
 * finale would be the worst possible ending to a party, so it cannot happen.
 */

import * as THREE from 'three';
import { NoteJudge, rankFor, SCORE } from '../../core/judge.js';
import { roundResult } from '../../core/result.js';
import { countIn } from '../../core/round.js';
import { FEEL, feelForCombo, isMilestone } from '../../core/feel.js';
import { clamp01, damp, beatPhase } from '../../core/util.js';
import { makeCharacter, makeAnimator } from '../../chars/index.js';
import { createSet, PLACE } from './set.js';
import {
  buildChart, bpmAt, BPM0, RAMP, LEAD_BEATS, END_BEAT, BEATS_PER_BAR,
  sectionAt, progressAt,
} from './chart.js';

/** How far ahead of the ear audio cues are handed to WebAudio. */
const CUE_LOOKAHEAD = 0.14;
/** Notes enter the judge this many bars before they are due. */
const ARM_BARS = 2;
/** Seconds of curtain call after the finale note before results are returned. */
const OUTRO_S = 2.6;

let S = null;

/** NEVER `b % 1`: the transport runs negative beats through the lead-in and
 *  JS modulo keeps the dividend's sign. This already crashed the codebase once. */
const beatFrac = beatPhase;
const hop = (p) => 4 * p * (1 - p);

// ============================================================================
// HUD
// ============================================================================

const STYLE_ID = 'ff-hud-style';

function injectStyle() {
  if (document.getElementById(STYLE_ID)) return;
  const s = document.createElement('style');
  s.id = STYLE_ID;
  s.textContent = `
  .ff-top{position:absolute;left:50%;top:clamp(10px,2.2vh,26px);transform:translateX(-50%);
    display:flex;flex-direction:column;align-items:center;gap:.28em;
    font-family:system-ui,-apple-system,"Segoe UI",sans-serif;text-align:center;}
  .ff-hearts{display:flex;gap:.34em;}
  .ff-heart{width:clamp(16px,2.4vw,30px);height:clamp(16px,2.4vw,30px);background:#ff5d73;
    clip-path:polygon(50% 100%,6% 44%,6% 22%,26% 4%,50% 20%,74% 4%,94% 22%,94% 44%);
    filter:drop-shadow(0 2px 0 rgba(0,0,0,.5)) drop-shadow(0 0 10px #ff5d73);
    transition:transform .12s cubic-bezier(.2,1.7,.4,1);}
  .ff-heart.gone{background:#3d1a33;filter:none;transform:scale(.72);}
  .ff-heart.pop{animation:ffPop .34s cubic-bezier(.2,1.7,.4,1);}
  @keyframes ffPop{0%{transform:scale(1.8)}100%{transform:scale(1)}}
  .ff-tempo{font-weight:900;font-size:clamp(12px,1.7vw,22px);color:#ffd93d;letter-spacing:.06em;
    text-shadow:0 2px 0 rgba(0,0,0,.6);font-variant-numeric:tabular-nums;}
  .ff-tempo b{font-size:1.35em;letter-spacing:-.01em;}
  .ff-turn{font-weight:900;font-size:clamp(12px,1.7vw,21px);letter-spacing:.18em;
    color:#ffb45a;text-shadow:0 2px 0 rgba(0,0,0,.6);min-height:1.1em;}
  .ff-turn.you{color:#9ee87a;}
  .ff-turn.solo{color:#ff5fd0;}
  .ff-turn.ff-survive{color:#ff5d73;animation:ffFlash .52s steps(2) infinite;}
  @keyframes ffFlash{0%{opacity:1}50%{opacity:.3}100%{opacity:1}}
  .ff-count{position:absolute;left:50%;top:36%;transform:translate(-50%,-50%);
    font-family:system-ui,sans-serif;font-weight:900;color:#fff;
    font-size:clamp(60px,13vw,170px);letter-spacing:-.04em;
    text-shadow:0 8px 0 rgba(0,0,0,.5),0 0 46px rgba(255,217,61,.65);opacity:0;}
  .ff-count.go{animation:ffCount .6s cubic-bezier(.15,1.5,.4,1) forwards;}
  @keyframes ffCount{0%{opacity:0;transform:translate(-50%,-50%) scale(2.1)}
    18%{opacity:1;transform:translate(-50%,-50%) scale(1)}
    70%{opacity:1;transform:translate(-50%,-50%) scale(1.03)}
    100%{opacity:0;transform:translate(-50%,-50%) scale(1.2)}}
  @media (prefers-reduced-motion: reduce){
    .ff-heart.pop,.ff-count.go,.ff-turn.ff-survive{animation:none}
    .ff-count.go{opacity:1}}
  `;
  document.head.appendChild(s);
}

function makeHud(ctx) {
  const wrap = ctx.ui.el('ff-top');
  const heartRow = ctx.ui.el('ff-hearts');
  const pips = [];
  for (let i = 0; i < 3; i++) {
    const h = ctx.ui.el('ff-heart');
    heartRow.appendChild(h);
    pips.push(h);
  }
  const tempo = ctx.ui.el('ff-tempo');
  tempo.innerHTML = '<b>150</b> BPM';
  const turn = ctx.ui.el('ff-turn', 'GET READY');
  wrap.append(heartRow, tempo, turn);
  ctx.ui.layer.appendChild(wrap);

  const count = ctx.ui.el('ff-count');
  ctx.ui.layer.appendChild(count);

  let lastBpm = -1;
  let lastTurn = '';
  return {
    setHearts(n, popIndex = -1) {
      for (let i = 0; i < pips.length; i++) {
        pips[i].classList.toggle('gone', i >= n);
        if (i === popIndex) {
          pips[i].classList.remove('pop');
          void pips[i].offsetWidth;
          pips[i].classList.add('pop');
        }
      }
    },
    setTempo(bpm) {
      const r = Math.round(bpm);
      if (r === lastBpm) return;
      lastBpm = r;
      tempo.innerHTML = `<b>${r}</b> BPM`;
    },
    setTurn(text, cls) {
      const key = text + '|' + cls;
      if (key === lastTurn) return;
      lastTurn = key;
      turn.textContent = text;
      turn.className = 'ff-turn ' + (cls || '');
    },
    flashCount(text) {
      count.textContent = text;
      count.classList.remove('go');
      void count.offsetWidth;
      count.classList.add('go');
    },
    destroy() { wrap.remove(); count.remove(); },
  };
}

// ============================================================================
// the minigame
// ============================================================================

export default {
  id: 'finale-fever',
  name: 'Finale Fever',
  blurb: 'It only gets faster. Answer the maestro — then answer alone.',
  bpm: BPM0,
  durationBars: 52,
  controls: 'a,left,right',

  // ------------------------------------------------------------------- load
  load(ctx) {
    injectStyle();

    const root = new THREE.Group();
    root.name = 'finale-fever';
    ctx.scene.add(root);
    ctx.scene.userData.palette = 'finale-fever';
    ctx.stage.setPalette('finale-fever', 0.001);

    // The env kit builds the room — floor, arches, stands, bunting, beam rig,
    // floaters — and beat-reacts for free. Composing is one call.
    const env = ctx.stage.createEnv(ctx.scene);
    env.stageSet('arena', { groundY: 0 });

    const set = createSet(ctx, root);

    // --- the duellists -----------------------------------------------------
    // The boss is the TALL build at 1.62x on a podium: long limbs draw long,
    // legible arcs, and a raised baton on a long arm is the clearest silhouette
    // in the frame. It looms over the player from the first frame.
    const boss = makeCharacter({
      palette: 'ultramarine', build: 'tall', seed: 0x8055, detail: 'full',
      scale: 1.62, name: 'maestro',
    });
    boss.position.set(PLACE.boss[0], PLACE.boss[1], PLACE.boss[2]);
    boss.rotation.y = PLACE.bossYaw;
    root.add(boss);

    const hero = ctx.players?.[0];
    const player = makeCharacter({
      palette: hero?.palette ?? 'ember',
      build: hero?.build || 'round', seed: 0x51e, detail: 'full', scale: 1.05, name: 'player',
    });
    hero?.dress?.(player);
    player.position.set(PLACE.player[0], PLACE.player[1], PLACE.player[2]);
    player.rotation.y = PLACE.playerYaw;
    root.add(player);

    // The look system's dress pass swaps any lit material it finds for a house
    // toon material AND disposes the original. Character materials are cached at
    // chars/rig.js module scope and SHARED across every scene, so letting that
    // happen would free a material other scenes still hold, and would throw away
    // the fresnel rim that separates a character from the backdrop. Opting these
    // meshes out is a one-line guard — see the report.
    for (const c of [boss, player]) {
      c.traverse((o) => { if (o.isMesh) o.userData.keepMaterial = true; });
    }

    boss.attach('handR', set.batonGroup);

    // The boss's cloak. One open cone off the torso: a draw call for a
    // silhouette nobody will confuse with the player's.
    const capeGeo = new THREE.ConeGeometry(0.46, 1.08, 14, 1, true);
    const capeMat = new THREE.MeshStandardMaterial({
      color: 0x2a1060, roughness: 0.88, side: THREE.DoubleSide,
    });
    const cape = new THREE.Mesh(capeGeo, capeMat);
    cape.position.set(0, -0.30, -0.06);
    cape.rotation.x = -0.16;
    boss.attach('torso', cape);

    ctx.fx.attach(ctx.scene);
    ctx.fx.setFocus(PLACE.strike);
    ctx.fx.setGroundY(0);

    ctx.stage.rig.frame({
      target: [0, 1.55, -0.4], distance: 10.3, height: 2.65, yaw: 0.02, fov: 52, lambda: 2.6,
    });
    ctx.stage.look.setShadowFocus('rig', 8);   // boss, player and the stage between them
    ctx.stage.rig.snap();
    ctx.stage.rig.setPushGain(1.15);

    ctx.ui.hud.mount();

    S = {
      root, env, set, boss, player,
      hud: makeHud(ctx),
      bossAnim: makeAnimator(boss, { seed: 0x8055 }),
      playerAnim: makeAnimator(player, { seed: 0x51e }),
      cape, capeGeo, capeMat,
      judge: null, chart: null,
      armed: 0,        // notes handed to the judge so far
      firstLive: 0,    // first not-yet-judged note
      cueA: 0, cueV: 0, cueW: 0,   // audio / ictus / windup cursors over cues
      orbCursor: 0, phraseCursor: 0,
      hearts: 3, survived: false,
      raw: 0, max: 0, combo: 0, maxCombo: 0,
      counts: { perfect: 0, great: 0, good: 0, miss: 0 },
      ghosts: 0,
      section: 'teach', heat: 0, solo: false,
      done: false, endT: 0, finaleJudged: false, finaleVerdict: null,
      unsubBeat: null, trail: null,
      bossLunge: 0, playerLean: 0, camPunch: 0,
      time: 0,
      _v: new THREE.Vector3(),
      _v2: new THREE.Vector3(),
    };
  },

  // ------------------------------------------------------------------ start
  start(ctx) {
    const { clock } = ctx;
    clock.setBpm(BPM0);
    clock.start(clock.now() + 0.55, -LEAD_BEATS);

    S.chart = buildChart();
    const judge = new NoteJudge();
    // Empty on purpose: notes are armed bar-by-bar by `armNotes`, and their
    // times are re-derived every frame by `refreshTimes`.
    judge.load([]);
    judge.onJudged = (note, verdict, errMs) => onJudged(ctx, note, verdict, errMs);
    S.judge = judge;

    ctx.audio.music.play('finale-fever');

    // The baton draws a conducting trace. The repo's own DNA, showing up in the
    // last game of the series: the shape of the gesture, on screen.
    S.trail = ctx.fx.trail({ color: 0xffe9a8, width: 0.10 });

    S.unsubBeat = countIn(ctx, {
      beats: LEAD_BEATS,
      show: (text) => S.hud.flashCount(text),
      onBeat: (b) => {
        if (b === -LEAD_BEATS) {
          ctx.ui.banner('FINALE FEVER', { sub: 'It only gets faster.', life: 1.5 });
        }
        if (b >= 0 && ((b % BEATS_PER_BAR) + BEATS_PER_BAR) % BEATS_PER_BAR === 0) {
          // The room agrees with the beat harder as the tempo climbs.
          ctx.stage.pulse(1.1 + S.heat * 0.55);
        }
      },
    });

    S.playerAnim.setState('idle');
    S.bossAnim.setState('idle');
    S.hud.setHearts(3);
  },

  // ----------------------------------------------------------------- update
  update(ctx, dt, beat) {
    if (!S) return;
    const { clock } = ctx;
    S.time += dt;

    // --- 1. the ramp -------------------------------------------------------
    // Every frame, off a smooth curve. setBpm rebases at "now" and preserves the
    // beat number exactly, so this is thousands of tiny tempo changes and zero
    // steps. During the lead-in it is pinned at BPM0 so the count-in is square.
    if (beat > 0) clock.setBpm(bpmAt(beat));

    S.heat = clamp01(progressAt(beat) * 1.05);
    const section = sectionAt(beat);
    if (section !== S.section) onSection(ctx, section);
    S.solo = section === 'solo';

    // --- 2. arm and re-time the chart --------------------------------------
    armNotes(ctx, beat);
    refreshTimes(ctx);
    S.judge.update(clock.now());
    advanceCursor();

    // --- 3. cues -----------------------------------------------------------
    fireAudioCues(ctx);
    fireIctusCues(ctx, beat);
    launchOrbs(beat);
    closePhrases(ctx, beat);

    // --- 4. the cast -------------------------------------------------------
    driveBoss(ctx, dt, beat);
    drivePlayer(ctx, dt, beat);
    S.bossAnim.update(dt, beat);
    S.playerAnim.update(dt, beat);

    // Cloak lags the torso: secondary motion, one line.
    S.cape.rotation.x = damp(S.cape.rotation.x, -0.16 - S.bossLunge * 0.45, 8, dt);
    S.cape.rotation.z = damp(S.cape.rotation.z, Math.sin(beat * Math.PI) * 0.12, 6, dt);

    // --- 5. the set --------------------------------------------------------
    S.set.setBossGlow(S.solo ? 0.08 : (S.hearts === 0 ? 1.6 : 1));
    S.set.setPlayerGlow(S.solo ? 2.2 : 1);
    S.set.update(dt, beat, S.time, { solo: S.solo, heat: S.heat });

    if (S.trail) {
      if (S.solo) {
        S.trail.release();
        S.trail = null;
      } else {
        S.set.batonTip.getWorldPosition(S._v);
        S.trail.set(S._v.x, S._v.y, S._v.z);
      }
    }

    // --- 6. camera ---------------------------------------------------------
    driveCamera(ctx, dt);

    // --- 7. hud ------------------------------------------------------------
    S.hud.setTempo(clock.bpm);
    updateTurn(beat);

    // --- 8. the end --------------------------------------------------------
    if (S.finaleJudged) {
      S.endT += dt;
      if (S.endT > OUTRO_S) S.done = true;
    } else if (beat > END_BEAT + 2.5) {
      // Safety net: never hang the round if the finale note was never committed.
      S.finaleJudged = true;
    }
  },

  // ------------------------------------------------------------------ input
  input(ctx, events) {
    if (!S || S.done) return;
    refreshTimes(ctx);
    for (const e of events) {
      if (!e.down) continue;
      let action = e.action;
      if (action === 'up') action = 'left';
      if (action === 'b' || action === 'down') action = 'right';
      if (action !== 'a' && action !== 'left' && action !== 'right') continue;

      let r = S.judge.press(action, e.time);
      // 'a' is the universal button. With no 'a' note in reach it claims the
      // nearest lane note instead, so a player who never mastered the chord can
      // still take half of one and nobody is locked out for using a thumb.
      if (!r && action === 'a') {
        r = S.judge.press('left', e.time) || S.judge.press('right', e.time);
      }
      if (!r) {
        S.ghosts++;
        ctx.audio.sfx('tick', undefined, FEEL.ghostPressTick);
        S.playerAnim.impulse(-0.16, 0);
      }
    }
  },

  // ----------------------------------------------------------------- result
  result() {
    if (!S || !S.done) return null;
    const acc = S.max > 0 ? clamp01(S.raw / S.max) : 1;
    let rank = rankFor(acc, S.counts.miss);
    // Losing every heart is not an ejection — but it is not an S, either.
    if (S.survived && (rank === 'S' || rank === 'A')) rank = 'B';
    return roundResult({
      score: Math.round(acc * 1000),
      accuracy: acc,
      rank,
      stats: {
        ...S.counts,
        maxCombo: S.maxCombo,
        notes: S.armed,
        hearts: S.hearts,
        tier: S.survived ? 'SURVIVOR' : (S.counts.miss === 0 ? 'MAESTRO' : 'DUELLIST'),
        finale: S.finaleVerdict || 'miss',
        topBpm: Math.round(BPM0 * RAMP),
        ghostPresses: S.ghosts,
      },
      highlights: [
        S.finaleVerdict && S.finaleVerdict !== 'miss' ? 'Took the last note' : null,
        S.survived ? 'Survived to the end' : null,
        S.maxCombo >= 40 ? `${S.maxCombo} combo` : null,
      ].filter(Boolean),
    });
  },

  // ──────────────────────────────────────────────────── harness: testChart

  /**
   * The answer notes, in BEATS. Times must not be precomputed here: the
   * transport ramps from 150bpm to 202.5 under the chart, so a time derived
   * at autoplay start is ~15ms out by the last bar and grows with the ramp.
   */
  testChart() {
    if (!S?.chart) return null;
    return S.chart
      .filter((n) => n.type !== 'call' && n.action)
      .map((n) => ({ beat: n.beat, action: n.action }));
  },

  // ---------------------------------------------------------------- dispose
  dispose(ctx) {
    if (!S) return;
    try { S.unsubBeat?.(); } catch { /* ignore */ }
    try { S.trail?.release?.(); } catch { /* ignore */ }

    S.set.dispose();
    S.capeGeo.dispose();
    S.capeMat.dispose();
    // Characters remove themselves from the graph first, so the sweep below
    // cannot reach (and must not free) chars/rig.js's shared geometry cache.
    S.boss.dispose();
    S.player.dispose();

    S.root.traverse((o) => {
      if (!o.isMesh) return;
      o.geometry?.dispose?.();
      const m = o.material;
      if (Array.isArray(m)) m.forEach((x) => x.dispose?.());
      else m?.dispose?.();
    });
    S.root.removeFromParent();

    try { S.env.dispose(); } catch { /* the stage may already have taken it */ }

    S.hud.destroy();
    ctx.ui.hud.unmount();
    document.getElementById(STYLE_ID)?.remove();
    S = null;
  },
};

// ============================================================================
// chart plumbing
// ============================================================================

/**
 * Hand the judge every note due within ARM_BARS — the "schedule incrementally"
 * rule. `time` is seeded here at the tempo in force now, and corrected every
 * frame afterwards, so even the seed can only ever be a couple of ms out.
 */
function armNotes(ctx, beat) {
  const notes = S.chart.notes;
  const horizon = beat + ARM_BARS * BEATS_PER_BAR;
  while (S.armed < notes.length && notes[S.armed].beat <= horizon) {
    const n = notes[S.armed];
    S.judge.notes.push({
      ...n, hold: 0, time: ctx.clock.timeAt(n.beat), judged: false, hit: false, _orb: -1,
    });
    S.armed++;
  }
}

/**
 * THE anti-drift pass. Re-derives the audio time of every live note from its
 * beat at the tempo in force right now. A bounded scan (never more than a few
 * dozen notes) that removes an entire class of bug.
 */
function refreshTimes(ctx) {
  const list = S.judge.notes;
  const end = Math.min(list.length, S.firstLive + 48);
  for (let i = S.firstLive; i < end; i++) {
    const n = list[i];
    if (!n.judged) n.time = ctx.clock.timeAt(n.beat);
  }
}

function advanceCursor() {
  const list = S.judge.notes;
  while (S.firstLive < list.length && list[S.firstLive].judged) S.firstLive++;
}

// ============================================================================
// cues — the boss's voice, and the boss's arm
// ============================================================================

/**
 * Boss audio, handed to WebAudio with a real head start. The time is taken at
 * DISPATCH, never at build, so the ramp cannot bend it.
 */
function fireAudioCues(ctx) {
  const cues = S.chart.cues;
  const horizonBeat = ctx.clock.beatAt(ctx.clock.now() + CUE_LOOKAHEAD);
  while (S.cueA < cues.length && cues[S.cueA].beat <= horizonBeat) {
    const c = cues[S.cueA++];
    const t = Math.max(ctx.clock.timeAt(c.beat), ctx.clock.rawNow() + 0.004);
    if (c.type === 'call') bossVoice(ctx, c, t);
    else soloTick(ctx, c, t);
  }
}

/**
 * The boss sounds like a boss: low brass over a taiko, an octave and a half
 * below anything the player will ever produce, panned hard to its side of the
 * stage. The player's answers are bells and plucks up top, panned to theirs. You
 * can tell who is playing with your eyes shut, which is the whole requirement.
 */
function bossVoice(ctx, c, t) {
  const V = ctx.audio.voices;
  const key = ctx.audio.music.scaleInfo();
  const chord = key && key.chord && key.chord.length ? key.chord : [52, 55, 59];
  const midi = chord[c.tone % chord.length] - 12;

  if (c.kind === 'swing') {
    V.orchHit(t, { midis: chord.map((m) => m - 12), gain: 0.24, rev: 0.5 });
    V.tom(t, { freq: 86, gain: 0.9, decay: 0.42, pan: -0.34, rev: 0.3 });
  } else if (c.kind === 'chord') {
    V.stab(t, { midis: [midi, midi + 7], dur: 0.2, gain: 0.19, wah: 1.7, rev: 0.3, pan: -0.3 });
    V.tom(t, { freq: 128, gain: 0.5, decay: 0.24, pan: -0.3 });
  } else {
    V.brass(t, {
      midi, dur: c.accent ? 0.3 : 0.16, gain: c.accent ? 0.19 : 0.12,
      bite: 1.4, rev: 0.28, pan: -0.32,
    });
    if (c.accent) V.tom(t, { freq: 112, gain: 0.4, decay: 0.2, pan: -0.28 });
  }
}

/**
 * The solo's only cue, and it is deliberately musical rather than informational:
 * a rim inside the arrangement on each note position. "Only the music tells you
 * when" — this is the music telling you.
 */
function soloTick(ctx, c, t) {
  ctx.audio.voices.rim(t, { gain: c.accent ? 0.24 : 0.15, pan: -0.08, rev: 0.14 });
}

/** The boss's arm: coil a little early, ictus exactly on the beat. */
function fireIctusCues(ctx, beat) {
  const cues = S.chart.cues;
  // Anticipation. Only on notes with room around them — a run of eighths would
  // otherwise turn the baton into a hummingbird.
  while (S.cueW < cues.length && cues[S.cueW].beat - 0.42 <= beat) {
    const c = cues[S.cueW++];
    if (c.type === 'call' && c.accent) S.bossAnim.windup({ dur: 0.24 });
  }
  while (S.cueV < cues.length && cues[S.cueV].beat <= beat) {
    const c = cues[S.cueV++];
    if (c.type !== 'call') continue;
    if (c.accent) {
      S.bossAnim.strike({ power: c.kind === 'swing' ? 1.3 : 0.85 });
      S.bossLunge = c.kind === 'swing' ? 1 : 0.55;
    } else {
      S.bossAnim.impulse(0.28, 0.012);
      S.bossLunge = Math.max(S.bossLunge, 0.22);
    }
    // A spark off the baton tip on every call note, so the phrase is visible as
    // well as audible even when the arm is barely moving.
    S.set.batonTip.getWorldPosition(S._v);
    ctx.fx.flare(S._v, { size: c.accent ? 0.44 : 0.24, life: 0.13, color: 0xffe9a8, to: 2.2 });
  }
}

/** Launch each note's orb at exactly `note.beat - note.telegraph`. */
function launchOrbs(beat) {
  const list = S.judge.notes;
  while (S.orbCursor < list.length) {
    const n = list[S.orbCursor];
    if (n.telegraph <= 0) { S.orbCursor++; continue; }
    if (n.beat - n.telegraph > beat) break;
    S.orbCursor++;
    if (!n.judged) S.set.spawnOrb(n, n.beat - n.telegraph, n.beat);
  }
}

// ============================================================================
// judgement
// ============================================================================

function onJudged(ctx, note, verdict, errMs) {
  const w = note.weight || 1;
  S.raw += SCORE[verdict] * w;
  S.max += SCORE.perfect * w;
  S.counts[verdict]++;
  if (verdict === 'miss') S.combo = 0;
  else { S.combo++; if (S.combo > S.maxCombo) S.maxCombo = S.combo; }

  const pos = S.set.orbWorld(note, S._v2);
  if (note.kind === 'finale') { finaleHit(ctx, note, verdict, errMs, pos); return; }

  const big = note.kind === 'swing';

  // fx.verdict composes the whole layered response — flare, shard cone, both
  // shockwaves, speed lines, spray, ground bloom, in-world callout, camera kick
  // and flash. Calling it directly disarms fx's `judge`-bus bridge, so the bus
  // emit below is telemetry only and there is exactly ONE callout on screen.
  ctx.fx.verdict(verdict, pos, {
    combo: S.combo,
    scale: big ? 1.5 : (note.kind === 'solo' ? 1.22 : 1),
    dir: [0.55, 0.72, 0.35],
    groundY: 0,
  });
  ctx.audio.sfx(verdict);
  if (verdict !== 'miss') playerVoice(ctx, note);

  const f = feelForCombo(verdict, S.combo);
  ctx.hitstop(Math.min(FEEL.hitstopMax, f.hitstop * (note.kind === 'solo' ? 1.25 : 1)));
  S.set.hitZone(verdict === 'miss' ? 0.25 : (big ? 1.4 : 0.9));

  if (verdict === 'miss') {
    S.playerAnim.react('miss', { dur: 0.42 });
    S.env.crowd?.pulse?.(0.2);
    S.camPunch = Math.max(S.camPunch, 0.3);
  } else if (verdict === 'good') {
    S.playerAnim.react('good', { dur: 0.36 });
  } else if (isMilestone(S.combo)) {
    S.playerAnim.react('perfect', { dur: 0.85 });
    S.env.crowd?.cheer?.(1.5);
    ctx.audio.sfx('combo', undefined, Math.min(9, Math.floor(S.combo / 10)));
  } else {
    S.playerAnim.strike({ power: big ? 1.25 : 0.8 });
  }
  S.playerLean = 1;

  pushHud(ctx);
  ctx.bus.emit('judge', { verdict, errMs, beat: note.beat });
}

/** The player's answer: bright, high, and in the chord the track is sitting on. */
function playerVoice(ctx, note) {
  const V = ctx.audio.voices;
  const key = ctx.audio.music.scaleInfo();
  const chord = key && key.chord && key.chord.length ? key.chord : [52, 55, 59];
  const midi = chord[S.combo % chord.length] + (note.kind === 'solo' ? 24 : 12);
  const t = ctx.clock.rawNow() + 0.004;
  V.bell(t, { midi, dur: 0.24, gain: 0.12, ratio: 2.01, index: 4, rev: 0.3, dly: 0.16, pan: 0.28 });
  if (note.kind === 'swing') {
    V.pluck(t, { midi: midi + 7, dur: 0.2, gain: 0.11, bright: 5, rev: 0.25, pan: 0.3 });
  }
}

/**
 * The scripted finale beat. Worth four ordinary notes, has its own animation,
 * and is the only place in the series FEEL's dedicated `finale` juice is spent.
 */
function finaleHit(ctx, note, verdict, errMs, pos) {
  S.finaleJudged = true;
  S.finaleVerdict = verdict;
  const hit = verdict !== 'miss';

  ctx.fx.verdict(hit ? 'perfect' : 'miss', pos, {
    combo: S.combo, scale: hit ? 2.4 : 1.4, dir: [0.4, 1, 0.2], groundY: 0,
    color: hit ? 0xfff6d8 : undefined,
  });
  ctx.stage.shake(FEEL.shake.finale, [0.3, 1, 0]);
  ctx.stage.flash(FEEL.flash.finale, hit ? '#fff6d8' : '#ff5d73');
  ctx.stage.punchZoom(hit ? 1.22 : 1.08);
  ctx.hitstop(FEEL.hitstop.finale);
  ctx.fx.confetti([0.2, 3.0, -0.5], { count: 180, speed: 10, up: 1.2 });
  ctx.fx.confetti(pos, { count: 110, speed: 8.5, up: 1 });
  S.env.crowd?.cheer?.(2);
  S.set.hitZone(2);
  S.camPunch = 1.6;

  if (hit) {
    ctx.audio.sfx('fanfare');
    S.playerAnim.react('perfect', { dur: 2.6 });
    S.bossAnim.setState('fail', { force: true, dur: 2.4 });
    ctx.ui.banner('FINALE!', { sub: 'You took the last note.', life: 2.2, color: '#ffd93d' });
  } else {
    // Failure is funny. The maestro bows; the confetti goes off anyway.
    ctx.audio.sfx('impact');
    S.playerAnim.react('miss', { dur: 2.2 });
    S.bossAnim.taunt({ dur: 2.4 });
    ctx.ui.banner('SO CLOSE!', { sub: 'The maestro takes the last note.', life: 2.2, color: '#ff5d73' });
  }

  pushHud(ctx);
  ctx.bus.emit('judge', { verdict, errMs, beat: note.beat });
}

function pushHud(ctx) {
  const acc = S.max > 0 ? S.raw / S.max : 1;
  ctx.ui.hud.setScore(Math.round(clamp01(acc) * 1000));
  ctx.ui.hud.setCombo(S.combo);
  ctx.ui.hud.setAccuracy(clamp01(acc));
}

// ============================================================================
// phrases and hearts
// ============================================================================

/**
 * Hearts are scored per PHRASE, never per note. One dropped note in a groove run
 * is a scoring event; a phrase you visibly lost is a heart. A clean phrase buys
 * one back, so the meter always moves in both directions and there is no
 * punishment spiral.
 */
function closePhrases(ctx, beat) {
  const ph = S.chart.phrases;
  while (S.phraseCursor < ph.length && beat >= ph[S.phraseCursor].endBeat) {
    const p = ph[S.phraseCursor++];
    if (p.finale) continue;
    let miss = 0;
    let top = 0;
    let n = 0;
    for (let i = p.idx0; i < p.idx1 && i < S.judge.notes.length; i++) {
      const note = S.judge.notes[i];
      if (!note.judged) continue;
      n++;
      if (note.verdict === 'miss') miss++;
      else if (note.verdict === 'perfect' || note.verdict === 'great') top++;
    }
    if (!n) continue;
    if (miss >= (p.solo ? 3 : 2)) loseHeart(ctx);
    else if (miss === 0 && top === n) gainHeart(ctx);
  }
}

function loseHeart(ctx) {
  if (S.hearts <= 0) {
    // Already surviving. There is nothing left to take, and the round continues.
    ctx.stage.shake(0.14, [1, 0.2, 0]);
    return;
  }
  S.hearts--;
  S.hud.setHearts(S.hearts, S.hearts);
  ctx.stage.flash(0.16, '#ff5d73');
  ctx.stage.shake(0.2, [1, -0.3, 0]);
  ctx.audio.sfx('miss');
  S.env.crowd?.pulse?.(0.15);
  if (S.hearts === 0) {
    S.survived = true;
    ctx.ui.banner('SURVIVE!', { sub: 'Nobody leaves the finale.', life: 1.9, color: '#ff5d73' });
    S.bossAnim.taunt({ dur: 1.8 });
    ctx.audio.sfx('impact');
  }
}

function gainHeart(ctx) {
  if (S.hearts >= 3) return;
  S.hearts++;
  S.hud.setHearts(S.hearts, S.hearts - 1);
  ctx.audio.sfx('coin');
  ctx.fx.popText('NICE!', [PLACE.strike[0], PLACE.strike[1] + 1.5, PLACE.strike[2]], {
    color: 0x9ee87a, size: 0.42, life: 0.8, rise: 1.2,
  });
  S.env.crowd?.cheer?.(1.1);
}

// ============================================================================
// staging
// ============================================================================

function onSection(ctx, section) {
  const prev = S.section;
  S.section = section;
  if (prev === section) return;

  if (section === 'play') {
    ctx.ui.banner('DUEL', { sub: 'Answer every phrase.', life: 1.2, color: '#ffd93d' });
  } else if (section === 'escalate') {
    ctx.ui.banner('FEVER', { sub: 'Less warning. More notes.', life: 1.4, color: '#ff8a3c' });
    ctx.stage.flash(0.16, '#ff8a3c');
    S.env.crowd?.cheer?.(1.3);
  } else if (section === 'solo') {
    // The moment the whole series has been walking toward: the telegraph
    // disappears, the maestro goes dark, the light collapses onto the player.
    ctx.ui.banner('SOLO', { sub: 'No signals. Just the music.', life: 1.8, color: '#ff5fd0' });
    ctx.stage.flash(0.3, '#ff5fd0');
    ctx.stage.shake(0.22, [0, 1, 0]);
    ctx.audio.sfx('swoosh');
    S.env.crowd?.cheer?.(1.8);
    S.bossAnim.setState('taunt', { force: true, dur: 3.2 });
    // Kill every orb still in the air. Nothing on screen may tell you when.
    for (let i = 0; i < S.set.MAX_ORBS; i++) S.set.killOrb(i);
  }
}

function driveBoss(ctx, dt, beat) {
  S.bossLunge = damp(S.bossLunge, 0, 6, dt);
  // The maestro leans in as the tempo climbs and rears back for the solo to
  // watch. Position, not just pose — it reads at a glance.
  const leanZ = S.solo ? -0.55 : S.bossLunge * 0.55 + S.heat * 0.35;
  S.boss.position.z = damp(S.boss.position.z, PLACE.boss[2] + leanZ, 4.5, dt);
  S.boss.position.y = PLACE.boss[1] + (S.solo ? 0 : S.bossLunge * 0.05);
  S.boss.rotation.y = damp(
    S.boss.rotation.y, PLACE.bossYaw + (S.solo ? 0.4 : -S.bossLunge * 0.18), 5, dt
  );
  S.set.batonGroup.rotation.z = damp(S.set.batonGroup.rotation.z, S.solo ? 0.95 : 0, 4, dt);
}

function drivePlayer(ctx, dt, beat) {
  S.playerLean = damp(S.playerLean, 0, 4.5, dt);
  const bar = Math.floor(beat / BEATS_PER_BAR);
  const answering = S.solo || (bar >= 0 && bar % 2 === 1);
  // Brace on your bar, watch on the maestro's. The turn is legible from the body
  // before it is legible from the HUD.
  const st = S.playerAnim.state;
  if (answering && (st === 'idle' || st === 'recover')) S.playerAnim.ready();
  else if (!answering && st === 'ready') S.playerAnim.relax();
  S.player.position.x = damp(
    S.player.position.x,
    PLACE.player[0] - (answering ? 0.24 : 0) - S.playerLean * 0.12, 5, dt
  );
  // A small forward bob on the beat while braced, so "your bar" is never still.
  S.player.position.z = PLACE.player[2] + (answering ? hop(beatFrac(beat)) * 0.05 : 0);
}

function driveCamera(ctx, dt) {
  S.camPunch = damp(S.camPunch, 0, 3.5, dt);
  const h = S.heat;
  if (S.solo) {
    ctx.stage.rig.frame({
      target: [PLACE.player[0] - 1.35, 1.5, 0.35],
      distance: 7.0 - S.camPunch * 0.35,
      height: 2.05, yaw: -0.24, fov: 49, lambda: 2.2,
    });
  } else {
    ctx.stage.rig.frame({
      target: [h * 0.5, 1.55 + h * 0.06, -0.4],
      distance: 10.3 - h * 1.35 - S.camPunch * 0.25,
      height: 2.65 - h * 0.4,
      yaw: 0.02 + h * 0.09,
      fov: 52 - h * 1.5,
      lambda: 2.6,
    });
  }
}

function updateTurn(beat) {
  if (S.hearts === 0 && !S.solo) { S.hud.setTurn('SURVIVE', 'ff-survive'); return; }
  if (beat < 0) { S.hud.setTurn('GET READY', ''); return; }
  if (S.solo) { S.hud.setTurn('YOUR SOLO', 'solo'); return; }
  const bar = Math.floor(beat / BEATS_PER_BAR);
  if (bar % 2 === 0) S.hud.setTurn('MAESTRO', '');
  else S.hud.setTurn('YOUR TURN', 'you');
}
