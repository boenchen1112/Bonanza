/**
 * Animator for a Blender-built (skinned, GLTF-clip-driven) character body —
 * a parallel implementation of `anim.js`'s `CharacterAnimator` public
 * contract (`update/setState/play/react/windup/strike/ready/relax/taunt/
 * impulse`), targeting a real bone skeleton + 8 baked AnimationClips instead
 * of the toy rig's procedural pose-per-joint system.
 *
 * What is intentionally NOT reproduced: the toy rig's face (pupils/brows/
 * mouth are joints that don't exist on a skinned mesh), breathing, and the
 * continuous additive beat-layer bounce/sway (idle() in anim.js) — those are
 * specific to a hand-built joint rig with no equivalent on a GLTF skeleton.
 * `play()`'s `face`/`headTurn` options are therefore accepted and ignored,
 * not silently miscompiled into something that looks wrong.
 *
 * Blending strategy: rather than three.js's built-in `crossFadeTo` (which is
 * awkward to combine with a manually time-scrubbed target action — see
 * `play()`'s `to`/`dur` windows below), both the outgoing and incoming
 * action's weights are set by hand every frame from the SAME smootherstep
 * blend math `anim.js`'s own `update()` uses for pose blending. This is a
 * known-good, already-proven blend curve, just applied to two
 * AnimationAction weights instead of pose values.
 *
 * Clip-time control: a "natural" state (idle/ready/celebrate/fail/taunt/
 * dance with no `to`/`dur`) plays at its own rate via
 * `THREE.LoopRepeat`/`LoopOnce`, letting the mixer advance it normally. A
 * `play()` call with an explicit `to`/`dur` (the windup-lead-in / contact-
 * to-follow-through scrub Swing Kings actually uses) instead PAUSES the
 * action and sets `.time` by hand each frame — the same technique already
 * proven working in `chars/demo.js`'s `mirrorSource()` — so `mixer.update()`
 * doesn't fight the caller for control of exactly when the ball meets the
 * bat.
 */

import * as THREE from 'three';
import { STATE_DEF, VERDICT_POSE } from './anim.js';
import { CLIPS } from './clips.gen.js';
import { clamp01, damp, smootherstep } from '../core/util.js';

const ONE_SHOT = new Set(['celebrate', 'fail', 'taunt']);

function wrap(t, dur) {
  if (!(dur > 0)) return 0;
  const m = t % dur;
  return m < 0 ? m + dur : m;
}

export class BlenderCharacterAnimator {
  /**
   * @param {THREE.Object3D} root   the cloned GLB scene root
   * @param {THREE.AnimationMixer} mixer
   * @param {Record<string, THREE.AnimationAction>} actions  clip name -> action
   */
  constructor(root, mixer, actions, opts = {}) {
    this.root = root;
    this.mixer = mixer;
    this.actions = actions;
    this.rng = opts.rng || Math.random;

    this.state = 'idle';
    this.prevState = null;
    this.variant = null;
    this.t = 0;
    this.dur = Infinity;
    this.hold = false;
    this.blend = 1;
    this.blendDur = 0.2;
    this._clipNext = 'idle';
    this._beat = 0;

    this._action = null;      // current AnimationAction
    this._prevAction = null;  // outgoing, fading out
    this._spec = null;        // scrub spec for the CURRENT action, or null (natural playback)
    this._prevSpec = null;

    // NOT captured once here: the caller positions this character (and can
    // reposition it later - calibrateBatter() nudges char.position.x/z,
    // and any future caller might touch .y too) AFTER this constructor
    // runs, so a fixed snapshot goes stale the moment that happens. update()
    // instead subtracts its OWN last contribution from the current
    // position each frame to recover the real base, rather than assuming
    // this is still it.
    this._lastYOffset = 0;
    this._impulseSquash = 0;
    this._impulseLift = 0;

    this._enter('idle', { blend: 0.001 });
  }

  // ------------------------------------------------------------- transitions

  _actionFor(name) {
    return this.actions[name] || this.actions.idle || null;
  }

  /** Configure `action` for either natural playback or a manual time scrub. */
  _armAction(action, spec) {
    if (!action) return;
    action.reset();
    if (spec) {
      // Manually time-controlled: play() supplied from/to/dur, or a
      // beat-locked loop. Either way we set `.time` every frame ourselves.
      action.paused = true;
      action.enabled = true;
      action.setEffectiveTimeScale(1);
      action.play();
    } else {
      const oneShot = ONE_SHOT.has(this.state);
      action.setLoop(oneShot ? THREE.LoopOnce : THREE.LoopRepeat, Infinity);
      action.clampWhenFinished = oneShot;
      action.paused = false;
      action.enabled = true;
      action.time = 0;
      action.play();
    }
  }

  _enter(name, opts = {}) {
    const def = STATE_DEF[name] || STATE_DEF.idle;
    this.prevState = this.state;
    this.state = name;
    this.variant = opts.variant ?? null;
    this.t = 0;
    this.dur = opts.dur ?? def.dur;
    this.hold = opts.hold ?? def.hold ?? false;
    this.blendDur = Math.max(0.001, opts.blend ?? def.blend ?? 0.2);
    this.blend = 0;

    this._prevAction = this._action;
    this._prevSpec = this._spec;
    this._spec = opts.spec ?? null;

    const clipName = opts.clip ?? (this.actions[name] ? name : 'idle');
    this._action = this._actionFor(clipName);
    if (this._action === this._prevAction) {
      // Re-entering the same clip (e.g. two ready() calls back to back) -
      // don't restart it from frame 0 for a natural-playback state, only
      // for an explicit new scrub spec.
      if (opts.spec) this._armAction(this._action, this._spec);
      this._prevAction = null;
      this.blend = 1;
    } else {
      this._armAction(this._action, this._spec);
    }
    return this;
  }

  setState(name, opts = {}) {
    if (name === this.state && !opts.force && opts.variant === this.variant) return this;
    return this._enter(name, opts);
  }

  windup(opts = {}) { return this.play('swing', { to: CLIPS.swing?.contact ?? CLIPS.swing?.duration * 0.4, hold: true, ...opts }); }
  strike(opts = {}) {
    this.play('swing', { from: CLIPS.swing?.contact ?? 0, ...opts });
    this.impulse(0.4 * (opts.power ?? 1), 0);
    return this;
  }
  ready(opts = {}) { return this.play('ready', { loop: true, ...opts }); }
  relax(opts = {}) { return this.setState('idle', opts); }
  taunt(opts = {}) { return this.play('taunt', { force: true, ...opts }); }

  /**
   * Play a baked clip, matching `anim.js`'s `play()` surface. `face` and
   * `headTurn` are accepted (callers pass them unconditionally) and
   * intentionally no-op — see the module doc.
   */
  play(name, o = {}) {
    const action = this.actions[name];
    if (!action) return this;
    // clips.gen.js only covers the toy rig's own clip-backed states (idle/
    // ready/swing/pitch/taunt/dance) - celebrate/fail are purely procedural
    // there, so they have no CLIPS entry, even though this GLB has a real
    // baked clip for both. Fall back to the action's own clip duration and
    // treat it as beat-agnostic (no contact/tempo/downbeat) when absent.
    const meta = CLIPS[name];
    const fullDur = action.getClip().duration;
    const hasWindow = o.from !== undefined || o.to !== undefined || Number.isFinite(o.dur);
    const from = o.from ?? 0;
    const to = o.to ?? fullDur;
    const rate = Number.isFinite(o.rate) && o.rate > 0 ? o.rate : 1;
    const beatLock = !!(o.beatLock && meta?.tempo);

    let spec = null;
    let dur;
    if (beatLock) {
      const secPerBeat = 60 / (o.bpm ?? 120);
      spec = { beatLock: true, secPerBeat, beat0: o.beat0 ?? Math.floor(this._beat), phase: meta.downbeat ?? 0, clipDur: fullDur };
      dur = Infinity;
    } else if (o.loop) {
      spec = null; // natural repeat playback of the whole clip
      dur = Infinity;
    } else if (hasWindow) {
      spec = { from, to, clipDur: fullDur };
      dur = o.dur ?? (to - from) / rate;
    } else {
      spec = null;
      dur = o.dur ?? fullDur / rate;
    }

    this._enter('clip', {
      clip: name, spec, dur, hold: o.hold ?? false, force: true,
      blend: o.blend ?? STATE_DEF.clip.blend, variant: name,
    });
    this._clipNext = o.next ?? 'idle';
    return this;
  }

  react(verdict, opts = {}) {
    const m = VERDICT_POSE[verdict] || VERDICT_POSE.good;
    // No per-variant sub-poses without a dedicated clip per variant - map
    // the verdict's STATE to the identically-named clip where one exists
    // (celebrate/fail both do), otherwise fall back to idle.
    this.setState(m.state, { variant: m.variant, force: true, ...opts });
    if (verdict === 'perfect') this.impulse(0.5, 0.03);
    else if (verdict === 'great') this.impulse(0.32, 0.02);
    else if (verdict === 'miss') this.impulse(-0.4, 0);
    return m.state;
  }

  /** Squash/stretch-style impulse, approximated as a brief root scale/lift
   * pulse instead of the toy rig's per-joint squash (no equivalent on a
   * skinned mesh without visibly distorting it). */
  impulse(squash = 0.5, lift = 0) {
    this._impulseSquash = squash;
    this._impulseLift = lift;
    return this;
  }

  get finished() { return this.t >= this.dur; }

  get clipTime() {
    if (this.state !== 'clip' || !this._action) return null;
    return this._action.time;
  }

  /**
   * Force the skeleton to an exact clip frame, bypassing the state machine
   * entirely — the Blender-side equivalent of reaching into the toy rig's
   * `anim._applyPose(pose)`. Calibration-only: real gameplay drives poses
   * through `update()`, never this. Leaves every OTHER action's weight at
   * 0 so nothing but `name` contributes this frame.
   */
  _sampleRaw(name, t) {
    const action = this.actions[name];
    if (!action) return;
    for (const a of Object.values(this.actions)) if (a !== action) a.setEffectiveWeight(0);
    action.reset();
    action.paused = true;
    action.enabled = true;
    action.setEffectiveWeight(1);
    action.play();
    action.time = t;
    this.mixer.update(0);
  }

  // ------------------------------------------------------------------ update

  update(dt, beat) {
    this._beat = beat;
    this.t += dt;

    const def = STATE_DEF[this.state] || STATE_DEF.idle;
    if (!this.hold && isFinite(this.dur) && this.t >= this.dur && def.next) {
      const next = this.state === 'clip' ? this._clipNext : def.next;
      this._enter(next, {});
    }

    if (this.blend < 1) this.blend = Math.min(1, this.blend + dt / this.blendDur);
    const wIn = smootherstep(this.blend);

    if (this._action) this._driveSpec(this._action, this._spec, dt);
    if (this._prevAction && this._prevAction !== this._action) {
      this._driveSpec(this._prevAction, this._prevSpec, dt);
    }

    if (this._action) this._action.setEffectiveWeight(wIn);
    if (this._prevAction && this._prevAction !== this._action) {
      this._prevAction.setEffectiveWeight(1 - wIn);
    }
    if (wIn >= 1 && this._prevAction && this._prevAction !== this._action) {
      this._prevAction.stop();
      this._prevAction = null;
    }

    this.mixer.update(dt);

    // A beat-synced idle bob (matching the crowd's own hop shape) was tried
    // here and reverted: even a small one (0.018 units, only in idle/ready)
    // measurably widened the ball-to-bat gap in swingKings/verify.mjs's own
    // contact test past its passing threshold on some swings. That test is
    // this game's actual hit-feel contract; a cosmetic idle wobble isn't
    // worth risking it. Left as a deliberately-skipped idea, not silently
    // dropped - see this file's header for the other things skipped the
    // same way (breathing, blink, per-limb sway).
    const bob = 0;

    // Root-level impulse: a light scale/lift pulse standing in for the toy
    // rig's per-joint squash/stretch (see impulse() above).
    if (Math.abs(this._impulseSquash) > 0.002 || Math.abs(this._impulseLift) > 0.001) {
      const sy = 1 + this._impulseSquash * 0.08;
      const sxz = 1 - this._impulseSquash * 0.03;
      this.root.scale.set(sxz, sy, sxz);
      this._impulseSquash = damp(this._impulseSquash, 0, 11, dt);
      this._impulseLift = damp(this._impulseLift, 0, 11, dt);
    } else if (this.root.scale.y !== 1) {
      this.root.scale.set(1, 1, 1);
    }
    const trueBaseY = this.root.position.y - this._lastYOffset;
    const yOffset = this._impulseLift + bob;
    this.root.position.y = trueBaseY + yOffset;
    this._lastYOffset = yOffset;
  }

  /** Set an action's `.time` for this frame, per its spec (or leave it to
   * play at its own natural rate when spec is null). */
  _driveSpec(action, spec, dt) {
    if (!spec) return; // natural playback - mixer.update() already advances it
    if (spec.beatLock) {
      const beats = (this._beat - spec.beat0) * spec.secPerBeat + spec.phase;
      action.time = wrap(beats, spec.clipDur);
    } else {
      const u = clamp01(this.dur > 0 && isFinite(this.dur) ? this.t / this.dur : 1);
      action.time = spec.from + (spec.to - spec.from) * u;
    }
  }
}

export function makeBlenderAnimator(root, mixer, actions, opts) {
  return new BlenderCharacterAnimator(root, mixer, actions, opts);
}
