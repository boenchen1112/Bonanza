/**
 * Stage: the renderer, the camera rig, and the screen-wide effects that every
 * minigame is allowed to reach for.  [render agent owns this file]
 *
 * Minigames own their own THREE.Scene; the stage owns the surface it lands on.
 * Camera shake, flash and hitstop live here rather than in each game so that a
 * PERFECT punches identically everywhere.
 */

import * as THREE from 'three';
import { damp, clamp01 } from '../core/util.js';

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

  const size = { w: 1, h: 1, dpr: 1 };

  let scene = null;
  let camera = null;

  // --- camera shake state ---------------------------------------------------
  // Directional impulse + decay reads as force; white noise reads as a bug.
  const shakeDir = new THREE.Vector3();
  let shakeAmp = 0;
  let shakeFreq = 34;
  let shakePhase = 0;
  const camBase = new THREE.Vector3();
  const camOffset = new THREE.Vector3();

  // --- flash overlay --------------------------------------------------------
  const flashEl = document.createElement('div');
  flashEl.style.cssText = 'position:fixed;inset:0;pointer-events:none;background:#fff;opacity:0;mix-blend-mode:screen;z-index:50;';
  document.body.appendChild(flashEl);
  let flashA = 0;
  let flashColor = '#ffffff';

  // --- vignette / letterbox for cinematic beats ------------------------------
  let zoomTarget = 1;
  let zoom = 1;

  function resize(w, h) {
    size.w = Math.max(1, w);
    size.h = Math.max(1, h);
    // Cap DPR: a 3x retina phone rendering a full-screen 3D scene at native
    // resolution will drop frames, and frame drops are worse than soft pixels
    // in a game judged on timing.
    size.dpr = Math.min(window.devicePixelRatio || 1, 2);
    renderer.setPixelRatio(size.dpr);
    renderer.setSize(size.w, size.h, false);
  }

  function attach(s, c) {
    scene = s;
    camera = c;
    camBase.copy(c.position);
  }

  function detach() {
    scene = null;
    camera = null;
    shakeAmp = 0;
    flashA = 0;
    flashEl.style.opacity = '0';
  }

  /**
   * Directional camera kick.
   * @param {number} amp world units
   * @param {[number,number,number]} [dir] impulse direction in view space
   */
  function shake(amp, dir = null) {
    shakeAmp = Math.max(shakeAmp, amp);
    if (dir) shakeDir.set(dir[0], dir[1], dir[2]).normalize();
    else shakeDir.set(Math.random() * 2 - 1, Math.random() * 2 - 1, 0).normalize();
    shakePhase = 0;
  }

  function flash(alpha, color = '#ffffff') {
    if (alpha <= flashA) return;
    flashA = alpha;
    flashColor = color;
    flashEl.style.background = color;
  }

  /** Punch-in for big moments. 1 = normal. */
  function punchZoom(z, snapBack = true) {
    zoom = z;
    zoomTarget = snapBack ? 1 : z;
  }

  function setClear(hex) { renderer.setClearColor(hex, 1); }

  function render(dt) {
    if (!scene || !camera) return;

    // shake: damped sinusoid along a fixed axis
    if (shakeAmp > 0.0005) {
      shakePhase += dt * shakeFreq;
      const env = shakeAmp;
      camOffset.copy(shakeDir).multiplyScalar(Math.sin(shakePhase * Math.PI * 2) * env);
      shakeAmp = damp(shakeAmp, 0, 14, dt);
    } else {
      shakeAmp = 0;
      camOffset.set(0, 0, 0);
    }

    if (Math.abs(zoom - zoomTarget) > 0.0005) zoom = damp(zoom, zoomTarget, 11, dt);
    const fovScale = 1 / zoom;

    const prev = camera.position.clone();
    camera.position.add(camOffset);
    const prevFov = camera.fov;
    if (Math.abs(fovScale - 1) > 0.001) {
      camera.fov = prevFov * fovScale;
      camera.updateProjectionMatrix();
    }

    renderer.render(scene, camera);

    camera.position.copy(prev);
    if (camera.fov !== prevFov) {
      camera.fov = prevFov;
      camera.updateProjectionMatrix();
    }

    if (flashA > 0.001) {
      flashA = damp(flashA, 0, 16, dt);
      flashEl.style.opacity = String(clamp01(flashA));
    } else if (flashA !== 0) {
      flashA = 0;
      flashEl.style.opacity = '0';
    }
  }

  return {
    renderer, size, resize, attach, detach, render,
    shake, flash, punchZoom, setClear,
    get scene() { return scene; },
    get camera() { return camera; },
  };
}
