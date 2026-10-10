/**
 * Swing Kings camera-conduct source: webcam hand tracking.  [swingKings]
 *
 * MediaPipe HandLandmarker, fully bundled (ADR 0003): the wasm loader and
 * binary come from the npm package and the model from
 * web/src/assets/mediapipe/, all emitted into the build — nothing is fetched
 * from jsDelivr or Google Storage, unlike research/hand_tracking_web.
 * Everything here is lazy-loaded: tap-mode players never download it.
 *
 * It only produces positions. The conducting stroke itself (raise / ictus)
 * is found by gesture.js, the same detector the mouse mode uses.
 *
 * Deliberately low-stakes (spec §5): no accuracy bar, no calibration. If the
 * camera is missing or refused, `start()` rejects and the game falls back to
 * mouse conducting; the harness never drives this path (it cannot, headless).
 */

import loaderUrl from '../../../node_modules/@mediapipe/tasks-vision/wasm/vision_wasm_internal.js?url';
import wasmUrl from '../../../node_modules/@mediapipe/tasks-vision/wasm/vision_wasm_internal.wasm?url';
import modelUrl from '../../assets/mediapipe/hand_landmarker.task?url';

/** Palm centre (middle-finger knuckle): steadier than a fingertip for a beat. */
const TRACK_LANDMARK = 9;

/**
 * @param {{clock:any, onSample:(time:number, y:number, x:number)=>void,
 *          onLost?:()=>void}} o
 * @returns {Promise<{stop:()=>void, el:HTMLElement}>}
 */
export async function startCamera({ clock, onSample, onLost }) {
  if (!navigator.mediaDevices?.getUserMedia) throw new Error('no camera API');
  const stream = await navigator.mediaDevices.getUserMedia({
    video: { width: { ideal: 640 }, height: { ideal: 480 }, facingMode: 'user' }, audio: false,
  });

  const { HandLandmarker } = await import('@mediapipe/tasks-vision');
  let landmarker;
  try {
    landmarker = await HandLandmarker.createFromOptions(
      { wasmLoaderPath: loaderUrl, wasmBinaryPath: wasmUrl },
      {
        baseOptions: { modelAssetPath: modelUrl, delegate: 'GPU' },
        runningMode: 'VIDEO', numHands: 1,
        minHandDetectionConfidence: 0.5, minTrackingConfidence: 0.5,
      },
    );
  } catch (e) {
    stream.getTracks().forEach((t) => t.stop());
    throw e;
  }

  // A small mirrored preview with the tracked point, so the player can see
  // that the game sees their hand (and where it thinks the beat landed).
  const el = document.createElement('div');
  el.style.cssText = 'position:fixed;left:14px;bottom:14px;width:176px;height:132px;z-index:40;'
    + 'border-radius:12px;overflow:hidden;border:2px solid rgba(255,229,138,.8);'
    + 'box-shadow:0 6px 18px rgba(0,0,0,.45);background:#0a0820;pointer-events:none;';
  const video = document.createElement('video');
  video.muted = true; video.playsInline = true; video.autoplay = true;
  video.srcObject = stream;
  video.style.cssText = 'width:100%;height:100%;object-fit:cover;transform:scaleX(-1);opacity:.85;';
  const dot = document.createElement('div');
  dot.style.cssText = 'position:absolute;width:14px;height:14px;margin:-7px 0 0 -7px;border-radius:50%;'
    + 'background:#ffe58a;box-shadow:0 0 10px #ffe58a;display:none;';
  el.append(video, dot);
  document.body.appendChild(el);
  await video.play().catch(() => {});

  let running = true;
  let lastVideoTime = -1;
  let seen = false;
  function tick() {
    if (!running) return;
    if (video.readyState >= 2 && video.currentTime !== lastVideoTime) {
      lastVideoTime = video.currentTime;
      const perfMs = performance.now();
      const res = landmarker.detectForVideo(video, perfMs);
      const hand = res.landmarks?.[0];
      if (hand) {
        const p = hand[TRACK_LANDMARK];
        seen = true;
        dot.style.display = 'block';
        dot.style.left = `${(1 - p.x) * 100}%`;
        dot.style.top = `${p.y * 100}%`;
        onSample(clock.toAudioTime(perfMs), p.y, 1 - p.x);
      } else if (seen) {
        seen = false;
        dot.style.display = 'none';
        onLost?.();
      }
    }
    requestAnimationFrame(tick);
  }
  requestAnimationFrame(tick);

  return {
    el,
    stop() {
      running = false;
      stream.getTracks().forEach((t) => t.stop());
      try { landmarker.close(); } catch { /* already gone */ }
      el.remove();
    },
  };
}
