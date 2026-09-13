/**
 * Stage: the renderer, the camera rig, the house look, and the screen-wide
 * effects every minigame is allowed to reach for.  [render agent owns this file]
 *
 * Minigames own their own THREE.Scene; the stage owns the surface it lands on,
 * everything that lights it, and everything that happens to the whole image at
 * once. Camera shake, flash, chroma and hitstop live here rather than in each
 * game so that a PERFECT punches identically everywhere.
 *
 * ----------------------------------------------------------------- the look
 * A scene attached to the stage is automatically:
 *   - lit by the house rig (see render/look/lighting.js)
 *   - re-materialled into the house toon style (render/look/materials.js)
 *   - given a gradient sky, palette fog, blob shadows and a beat-reactive
 *     environment set (render/env/)
 *   - graded through bloom, vignette and chromatic offset (render/look/post.js)
 *
 * A minigame gets all of that by existing. It can then take control:
 *
 *   stage.setPalette('swing-kings')             // animated palette swap
 *   const env = stage.createEnv(scene);         // compose the set yourself
 *   env.stageSet('field', { groundY: -1.2 });
 *   stage.rig.frame({ target, distance, height });   // stage drives the camera
 *   stage.pulse(1.4)                            // manual beat accent
 *
 * ----------------------------------------------------------------- the beat
 * The stage watches the clock itself and pulses on every musical beat: lights
 * lift, the arena ring flares, the crowd hops, the camera pushes a few
 * millimetres in. It is deliberately below the threshold of "an effect" — the
 * point is that the world is never still and the tempo is always visible
 * somewhere in peripheral vision, even during silence in the gameplay layer.
 */

import * as THREE from 'three';
import { damp, clamp01, clamp, easeOutCubic } from '../core/util.js';
import { createLook } from './look/index.js';
import { createEnvKit } from './env/index.js';

/** Hard ceilings. Shake that breaks readability is not shake, it is a bug. */
const MAX_SHAKE = 0.34;   // world units
const MAX_ROLL = 0.018;   // radians (~1 degree)

export function createStage({ canvas, clock }) {
  const renderer = new THREE.WebGLRenderer({
    canvas,
    antialias: true,
    powerPreference: 'high-performance',
    alpha: false,
    stencil: false,
  });
  renderer.setClearColor(0x0b0a1a, 1);
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.0;
  renderer.autoClear = true;
  // Accumulate draw stats across every render() call in a frame (world, then
  // each post pass) and reset once per frame in render() below. With the
  // default autoReset each post pass wiped the count, so telemetry reported
  // one draw call for every scene.
  renderer.info.autoReset = false;
  // One soft shadow map, cast only inside a scene's declared focus box
  // (look.setShadowFocus); scenes that never declare one pay nothing.
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFShadowMap;   // r185: PCFSoft is deprecated; shadow.radius softens

  const size = { w: 1, h: 1, dpr: 1 };

  const look = createLook({ renderer });
  const envKit = createEnvKit({ look });

  let scene = null;
  let camera = null;
  let timeS = 0;

  // --- camera shake ---------------------------------------------------------
  // Directional impulse + fast decay reads as force; white noise reads as a bug.
  const shakeDir = new THREE.Vector3(1, 0, 0);
  let shakeAmp = 0;
  let shakeFreq = 31;
  let shakePhase = 0;
  let roll = 0;

  // --- beat-driven camera push ---------------------------------------------
  let push = 0;          // metres of dolly-in, decaying
  let pushGain = 1;
  let beatPulse = 0;     // 0..1+, drives the whole look
  let lastBeat = -1e9;
  let beatIndex = 0;

  // --- idle drift -----------------------------------------------------------
  let driftGain = 1;

  // --- flash / chroma -------------------------------------------------------
  const flashEl = document.createElement('div');
  flashEl.style.cssText = 'position:fixed;inset:0;pointer-events:none;background:#fff;opacity:0;mix-blend-mode:screen;z-index:50;';
  document.body.appendChild(flashEl);
  let flashA = 0;
  const flashColor = new THREE.Color(0xffffff);
  let chromaSpike = 0;
  const CHROMA_REST = 0.0011;

  // --- punch zoom -----------------------------------------------------------
  let zoomTarget = 1;
  let zoom = 1;

  // --- scratch --------------------------------------------------------------
  const camBase = new THREE.Vector3();
  const camOffset = new THREE.Vector3();
  const viewDir = new THREE.Vector3();
  const tmp = new THREE.Vector3();
  const savedPos = new THREE.Vector3();
  const savedQuat = new THREE.Quaternion();

  /** Per-frame render cost of the WORLD, excluding post's fullscreen quads. */
  const stats = { drawCalls: 0, triangles: 0, postPasses: 0 };

  /** @type {Set<object>} environments created through this stage */
  const envs = new Set();

  // ------------------------------------------------------------------ camera rig

  const rigState = {
    active: false,
    target: new THREE.Vector3(),
    look: new THREE.Vector3(),
    distance: 8,
    height: 2.2,
    yaw: 0,
    lambda: 4.5,
    fov: 50,
  };

  const rig = {
    /**
     * Hand the camera to the stage. Positions it on an arc around `target`
     * and eases there — no snapping, no per-game lerp constants.
     * @param {{target?:THREE.Vector3|number[], distance?:number, height?:number,
     *          yaw?:number, fov?:number, lambda?:number, immediate?:boolean}} o
     */
    frame(o = {}) {
      if (o.target) {
        const t = o.target;
        if (Array.isArray(t)) rigState.target.set(t[0], t[1], t[2]);
        else rigState.target.copy(t);
      }
      if (o.distance !== undefined) rigState.distance = o.distance;
      if (o.height !== undefined) rigState.height = o.height;
      if (o.yaw !== undefined) rigState.yaw = o.yaw;
      if (o.fov !== undefined) rigState.fov = o.fov;
      if (o.lambda !== undefined) rigState.lambda = o.lambda;
      rigState.active = true;
      if (o.immediate) rig.snap();
      return rig;
    },
    /** Jump to the framing this frame instead of easing into it. */
    snap() {
      if (!camera) return rig;
      desiredPosition(tmp);
      camera.position.copy(tmp);
      rigState.look.copy(rigState.target);
      camera.lookAt(rigState.target);
      if (rigState.fov && camera.fov !== rigState.fov) {
        camera.fov = rigState.fov;
        camera.updateProjectionMatrix();
      }
      return rig;
    },
    /** Give the camera back to the minigame. */
    release() { rigState.active = false; return rig; },
    get active() { return rigState.active; },
    /** How hard the camera reacts to the beat. 0 disables. */
    setPushGain(v) { pushGain = v; return rig; },
    setDriftGain(v) { driftGain = v; return rig; },
    state: rigState,
  };

  function desiredPosition(out) {
    const s = Math.sin(rigState.yaw), c = Math.cos(rigState.yaw);
    out.set(
      rigState.target.x + s * rigState.distance,
      rigState.target.y + rigState.height,
      rigState.target.z + c * rigState.distance
    );
    return out;
  }

  // --------------------------------------------------------------------- api

  function resize(w, h) {
    size.w = Math.max(1, w);
    size.h = Math.max(1, h);
    // Cap DPR: a 3x retina phone rendering a full-screen 3D scene at native
    // resolution will drop frames, and frame drops are worse than soft pixels
    // in a game judged on timing.
    size.dpr = Math.min(window.devicePixelRatio || 1, 2);
    renderer.setPixelRatio(size.dpr);
    renderer.setSize(size.w, size.h, false);
    look.post.setSize(size.w * size.dpr, size.h * size.dpr);
  }

  /**
   * Take over a freshly built scene. `id` must be the scene's registry id:
   * palette inference keys on it, so attaching without one grades the first
   * frames in whatever the *previous* scene left behind and then cross-fades
   * out of it over 0.7s — visible at the top of every screen.
   */
  function attach(s, c, id = null) {
    scene = s;
    camera = c;
    sceneId = id;
    camBase.copy(c.position);
    look.attach(s, c);
    rigState.active = false;
    timeS = 0;
    // Palette follows the scene unless the game overrides it; the look system
    // re-checks the scene id as the scene finishes building.
    autoPalette(true);
  }

  function detach() {
    for (const e of envs) { try { e.dispose(); } catch { /* ignore */ } }
    envs.clear();
    look.detach();
    scene = null;
    camera = null;
    // A game's explicit palette choice lasts for that game only; the next
    // scene goes back to inferring its own.
    paletteLocked = false;
    lastAutoName = null;
    sceneId = null;
    shakeAmp = 0;
    push = 0;
    roll = 0;
    beatPulse = 0;
    flashA = 0;
    chromaSpike = 0;
    flashEl.style.opacity = '0';
  }

  // ---- palette -------------------------------------------------------------

  let paletteLocked = false;
  let lastAutoName = null;
  /**
   * Id of the scene that finished loading (set by main.js). Palette and
   * default-environment inference key on it. This used to be read off the
   * `window.__BBB__` test API, which production builds don't ship.
   */
  let sceneId = null;

  function autoPalette(immediate = false) {
    if (paletteLocked) return;
    const id = scene?.userData?.palette || sceneId || null;
    if (!id || id === lastAutoName) return;
    lastAutoName = id;
    const named = look.paletteNamed(id);
    const fallback = look.paletteNamed('shell');
    look.setPalette(named === look.paletteNamed('default') && id !== 'default'
      ? (fallback ? 'shell' : 'default') : id, immediate ? 0 : 0.7);
  }

  /** Explicit palette choice by a minigame. Wins over scene-id inference. */
  function setPalette(name, dur = 0.55) {
    paletteLocked = true;
    look.setPalette(name, dur);
    return look.palette;
  }

  // ---- environment ---------------------------------------------------------

  /**
   * Create an environment bound to a scene. Registered with the stage, so it
   * is updated and beat-pulsed automatically and disposed on scene exit.
   */
  function createEnv(targetScene = scene, opts) {
    const env = envKit.createEnv(targetScene || scene, opts);
    envs.add(env);
    const rawDispose = env.dispose;
    env.dispose = () => { envs.delete(env); rawDispose(); };
    return env;
  }

  /**
   * Get a freshly built scene to the GPU before its clock starts: build the
   * default set, run the dress pass, and compile every program in parallel
   * (KHR_parallel_shader_compile). Left to the first frame, Swing Kings'
   * compiles blocked ~1.2s with the count-in already running.
   */
  async function warm() {
    if (!scene || !camera) return;
    ensureDefaultEnv();
    look.dress();
    try { await renderer.compileAsync(scene, camera); } catch (e) { console.warn('warm: compile', e); }
  }

  /** Build the default set for a scene that didn't ask for one. */
  function ensureDefaultEnv() {
    if (envs.size || !scene) return;
    if (scene.userData.env === false) return;
    // The scene declares its own set. This used to read `id === 'title' ||
    // id === 'results'` — the render layer naming shell screens, so renaming
    // one silently changed its lighting.
    const preset = scene.userData.envPreset || 'arena';
    const env = createEnv(scene);
    env.stageSet(preset, {
      // A scene can declare where its floor is (roster and freeplay stand
      // their casts on a deck well below 0; the default arena floor at 0 used
      // to slice through them).
      groundY: look.ground.found ? look.ground.y : (scene.userData.groundY ?? 0),
      skipGround: look.ground.found,
    });
  }

  // ---- impulses ------------------------------------------------------------

  /**
   * Directional camera kick.
   * @param {number} amp world units
   * @param {[number,number,number]} [dir] impulse direction in VIEW space
   *        (x = screen right, y = screen up, z = toward the camera)
   */
  function shake(amp, dir = null) {
    if (!(amp > 0)) return;
    shakeAmp = Math.min(MAX_SHAKE, Math.max(shakeAmp, amp));
    if (dir) shakeDir.set(dir[0], dir[1], dir[2]);
    else shakeDir.set(0.85, 0.35, 0);
    if (shakeDir.lengthSq() < 1e-6) shakeDir.set(1, 0, 0);
    shakeDir.normalize();
    shakePhase = 0;
    // A kick that big is an impact: give it a touch of chroma and roll too.
    chroma(clamp01(amp * 2.2) * 0.004);
    roll = clamp(-shakeDir.x * amp * 0.09, -MAX_ROLL, MAX_ROLL);
  }

  function flash(alpha, color = '#ffffff') {
    if (alpha <= flashA) return;
    flashA = alpha;
    flashColor.set(color);
    flashEl.style.background = typeof color === 'string' ? color
      : '#' + flashColor.getHexString();
  }

  /** Extra chromatic separation, decaying. Spikes on impact. */
  function chroma(amount) {
    chromaSpike = Math.max(chromaSpike, amount);
  }

  /** Punch-in for big moments. 1 = normal, >1 = closer. */
  function punchZoom(z, snapBack = true) {
    zoom = z;
    zoomTarget = snapBack ? 1 : z;
  }

  /** Manual beat accent — env pieces pop, lights lift, camera nudges. */
  function pulse(strength = 1) {
    beatPulse = Math.max(beatPulse, strength);
    push = Math.max(push, 0.05 * strength * pushGain);
    for (const e of envs) e.pulse(strength, beatIndex);
  }

  function setClear(hex) { renderer.setClearColor(hex, 1); }

  /** Quality tier: 'auto' | 'high' | 'medium' | 'low'. */
  function setQuality(t) {
    const tier = look.setTier(t);
    look.post.setSize(size.w * size.dpr, size.h * size.dpr);
    return tier;
  }

  // -------------------------------------------------------------------- frame

  function trackBeat(dt) {
    if (!clock?.running) { beatPulse = damp(beatPulse, 0, 9, dt); return; }
    const beat = clock.beat;
    if (beat >= lastBeat + 1 || beat < lastBeat - 0.5) {
      const whole = Math.floor(beat);
      if (whole !== lastBeat) {
        lastBeat = whole;
        beatIndex = ((whole % 4) + 4) % 4;
        // Downbeats hit harder than the beats between them, or 4/4 reads as a
        // flat tick instead of a bar.
        const strength = beatIndex === 0 ? 1.25 : 0.8;
        beatPulse = Math.max(beatPulse, strength);
        push = Math.max(push, 0.045 * strength * pushGain);
        for (const e of envs) e.pulse(strength, beatIndex);
      }
    }
    beatPulse = damp(beatPulse, 0, 9, dt);
  }

  function render(dt, nowS) {
    if (!scene || !camera) return;
    timeS += dt;
    renderer.info.reset();

    autoPalette();
    ensureDefaultEnv();
    trackBeat(dt);

    // --- world --------------------------------------------------------------
    if (rigState.active) look.lights.follow(rigState.target);
    look.update(dt, { camera, beatPulse, time: timeS });
    for (const e of envs) e.update(dt, clock?.beat || 0, timeS, look.palette);

    // --- camera rig ---------------------------------------------------------
    if (rigState.active) {
      desiredPosition(tmp);
      const l = rigState.lambda;
      camera.position.set(
        damp(camera.position.x, tmp.x, l, dt),
        damp(camera.position.y, tmp.y, l, dt),
        damp(camera.position.z, tmp.z, l, dt)
      );
      rigState.look.set(
        damp(rigState.look.x, rigState.target.x, l, dt),
        damp(rigState.look.y, rigState.target.y, l, dt),
        damp(rigState.look.z, rigState.target.z, l, dt)
      );
      camera.lookAt(rigState.look);
    }

    savedPos.copy(camera.position);
    savedQuat.copy(camera.quaternion);
    camOffset.set(0, 0, 0);

    // shake: damped sinusoid along a fixed VIEW-space axis
    if (shakeAmp > 0.0006) {
      shakePhase += dt * shakeFreq;
      tmp.copy(shakeDir).applyQuaternion(camera.quaternion);
      const env = shakeAmp * Math.sin(shakePhase * Math.PI * 2);
      camOffset.addScaledVector(tmp, env);
      shakeAmp = damp(shakeAmp, 0, 15, dt);
      roll = damp(roll, 0, 15, dt);
    } else {
      shakeAmp = 0;
      roll = 0;
    }

    // beat push: a few millimetres along the view axis, snappy in, eased out
    push = damp(push, 0, 7.5, dt);
    camera.getWorldDirection(viewDir);
    camOffset.addScaledVector(viewDir, push);

    // idle drift: the frame is never perfectly still
    if (driftGain > 0) {
      const d = driftGain * 0.03;
      camOffset.x += Math.sin(timeS * 0.37) * d;
      camOffset.y += Math.sin(timeS * 0.29 + 1.7) * d * 0.7;
    }

    // never let the rig move enough to break the read
    if (camOffset.lengthSq() > MAX_SHAKE * MAX_SHAKE) camOffset.setLength(MAX_SHAKE);
    camera.position.add(camOffset);
    if (roll !== 0) camera.rotateZ(roll);

    // punch zoom via fov
    if (Math.abs(zoom - zoomTarget) > 0.0005) zoom = damp(zoom, zoomTarget, 11, dt);
    const prevFov = camera.fov;
    const fovScale = 1 / zoom;
    if (Math.abs(fovScale - 1) > 0.001) {
      camera.fov = prevFov * fovScale;
      camera.updateProjectionMatrix();
    }

    // --- grade --------------------------------------------------------------
    chromaSpike = damp(chromaSpike, 0, 9, dt);
    look.post.setChroma(CHROMA_REST + chromaSpike + beatPulse * 0.0006);

    if (flashA > 0.001) flashA = damp(flashA, 0, 16, dt);
    else flashA = 0;

    const posted = look.post.enabled;
    if (posted) {
      // In-shader flash sits under the HUD and gets graded with the frame.
      look.post.setFlash(flashA * 0.85, flashColor);
      flashEl.style.opacity = '0';
      look.post.render(scene, camera, timeS);
    } else {
      renderer.setRenderTarget(null);
      renderer.render(scene, camera);
      flashEl.style.opacity = flashA > 0.001 ? String(clamp01(flashA)) : '0';
    }

    // Scene cost for the whole frame (info accumulates — see autoReset above).
    stats.drawCalls = renderer.info.render.calls;
    stats.triangles = renderer.info.render.triangles;
    if (posted) {
      // The post chain's own passes are already included above; subtract the
      // known fullscreen quads so the number means "what the world costs".
      stats.postPasses = look.post.passCount ?? 0;
      stats.drawCalls = Math.max(0, stats.drawCalls - stats.postPasses);
    } else {
      stats.postPasses = 0;
    }

    // --- restore ------------------------------------------------------------
    camera.position.copy(savedPos);
    camera.quaternion.copy(savedQuat);
    if (camera.fov !== prevFov) {
      camera.fov = prevFov;
      camera.updateProjectionMatrix();
    }
  }

  function dispose() {
    detach();
    look.dispose();
    flashEl.remove();
  }

  return {
    // --- original surface (do not break) ---
    renderer, size, resize, attach, detach, render, warm,
    stats,
    shake, flash, punchZoom, setClear,
    get scene() { return scene; },
    get camera() { return camera; },
    get sceneId() { return sceneId; },
    set sceneId(v) { sceneId = v; },

    // --- look ---
    look, rig, setPalette, createEnv, pulse, chroma, setQuality, dispose,
    get palette() { return look.palette; },
    get colors() { return look.palette.col; },
    get quality() { return look.tier; },
    get beatPulse() { return beatPulse; },
    /** Verdict colour for the CURRENT palette, as '#rrggbb' — UI/games use
     *  this instead of FEEL.color directly so the grade stays coherent. */
    verdictColor(v) { return look.palette.verdictHex(v); },
    verdictHex(v) { return look.palette.verdict[v]?.getHex() ?? 0xffffff; },
    /** Named palettes, for menus and debug overlays. */
    palettes() { return Object.keys(look.paletteNamed('__none__') ? {} : {}); },
  };
}
