#!/usr/bin/env node
/**
 * Drive a BlenderCharacterAnimator through a realistic idle -> ready ->
 * windup -> strike -> celebrate sequence (matching Swing Kings' actual
 * call pattern) at a fixed timestep, screenshotting frequently, to check
 * for T-pose flashes, snaps, or frozen poses before this ever touches the
 * real game.
 *
 *   node tools/assets/blender/verify-blender-anim.mjs
 */
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, rmSync } from 'node:fs';
import path from 'node:path';

const ROOT = path.resolve(import.meta.dirname, '../../..');
const WEB = path.join(ROOT, 'web');
const OUT = path.resolve(ROOT, 'runs/verify-blender-anim');
mkdirSync(OUT, { recursive: true });

// Self-contained: write the test page here rather than depending on a
// separately-hand-edited _verify.html (whose content differs per test this
// tool suite runs) - one script is the whole record of this check.
const PAGE = `<!doctype html>
<html><head><meta charset="utf-8"><title>verify</title>
<style>html,body{margin:0;background:#1a1030;overflow:hidden}</style>
</head>
<body>
<script type="importmap">
{ "imports": {
  "three": "/node_modules/three/build/three.module.js",
  "three/examples/": "/node_modules/three/examples/"
} }
</script>
<script type="module">
import * as THREE from 'three';
import { GLTFLoader } from '/node_modules/three/examples/jsm/loaders/GLTFLoader.js';
import { clone as cloneSkinned } from '/node_modules/three/examples/jsm/utils/SkeletonUtils.js';
import { BlenderCharacterAnimator } from '/src/chars/blenderAnim.js';

const W = 640, H = 720;
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(W, H);
document.body.appendChild(renderer.domElement);

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x1a1030);
const camera = new THREE.PerspectiveCamera(35, W / H, 0.05, 50);
scene.add(new THREE.HemisphereLight(0xdfe6ff, 0x2a1d5e, 1.6));
const key = new THREE.DirectionalLight(0xffffff, 2.2);
key.position.set(2, 3, 4);
scene.add(key);
scene.add(new THREE.GridHelper(4, 8, 0x555577, 0x333355));

window.__V = { ready: false };

new GLTFLoader().loadAsync('/src/assets/chars/cast-tuff.glb').then((gltf) => {
  const clonedScene = cloneSkinned(gltf.scene);
  scene.add(clonedScene);
  // Cloned skinned meshes need an explicit updateMatrixWorld before their
  // bounding info is meaningful - without it Box3().setFromObject() reads
  // near-zero, because the clone's bone/skeleton matrices haven't been
  // evaluated yet. The real integration (blenderBodies.js) does this too.
  clonedScene.updateMatrixWorld(true);
  const box = new THREE.Box3().setFromObject(clonedScene);
  const size = box.getSize(new THREE.Vector3());
  const center = box.getCenter(new THREE.Vector3());
  const vFov = (camera.fov * Math.PI) / 180;
  const hFov = 2 * Math.atan(Math.tan(vFov / 2) * camera.aspect);
  const dist = Math.max((size.y / 2) / Math.tan(vFov / 2), (size.x / 2) / Math.tan(hFov / 2), 0.3) * 1.6;
  camera.position.set(center.x, center.y, center.z + dist);
  camera.lookAt(center);

  const mixer = new THREE.AnimationMixer(clonedScene);
  const actions = Object.fromEntries(gltf.animations.map((a) => [a.name, mixer.clipAction(a)]));
  const anim = new BlenderCharacterAnimator(clonedScene, mixer, actions);
  window.__V.anim = anim;
  window.__V.ready = true;
}).catch((e) => { window.__V.error = String(e); console.error(e); });

window.__step = (dt, beat) => {
  window.__V.anim.update(dt, beat);
  renderer.info.reset();
  renderer.render(scene, camera);
  window.__V.draws = renderer.info.render.calls;
  window.__V.state = window.__V.anim.state;
  window.__V.blend = window.__V.anim.blend;
};

function frame() {
  requestAnimationFrame(frame);
  renderer.render(scene, camera);
}
frame();
</script>
</body></html>
`;
const PAGE_PATH = path.join(WEB, '_verify-blender-anim.html');
writeFileSync(PAGE_PATH, PAGE);

const webRequire = createRequire(path.join(WEB, 'package.json'));
const pw = await import(pathToFileURL(webRequire.resolve('playwright')).href);
const chromium = pw.chromium ?? pw.default.chromium;
const PORT = 8500 + (process.pid % 300);
const server = spawn(process.execPath, [path.join(ROOT, 'tools/harness/serve.mjs'), WEB, String(PORT)], { stdio: ['ignore', 'pipe', 'pipe'] });
await new Promise((r) => { server.stdout.once('data', r); setTimeout(r, 1200); });

const browser = await chromium.launch({ channel: 'chromium', headless: true, args: process.platform === 'win32' ? ['--use-angle=d3d11'] : [] });
const page = await (await browser.newContext({ viewport: { width: 640, height: 720 } })).newPage();
const errors = [];
page.on('pageerror', (e) => errors.push(e.message));
page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });

await page.goto(`http://127.0.0.1:${PORT}/_verify-blender-anim.html`, { waitUntil: 'load' });
await page.waitForFunction('window.__V && window.__V.ready', null, { timeout: 15000 }).catch(() => {});
console.log('pageerrors so far:', JSON.stringify(errors));
const readyInfo = await page.evaluate(() => (window.__V ? { error: window.__V.error, ready: window.__V.ready } : { missing: true }));
console.log('readyInfo:', JSON.stringify(readyInfo));
if (readyInfo.missing || readyInfo.error) {
  console.log('ABORTING due to load failure');
  await browser.close(); server.kill(); rmSync(PAGE_PATH, { force: true });
  process.exit(1);
}

const STEP = 1 / 60;
let simTime = 0;
let beat = 0;
const BPM = 120;
const events = [];
let shotN = 0;
async function runFor(seconds, label, everyMs = 100) {
  const steps = Math.round(seconds / STEP);
  let sinceShot = 0;
  for (let i = 0; i < steps; i++) {
    beat += (STEP * BPM) / 60;
    const info = await page.evaluate(([dt, b]) => { window.__step(dt, b); return { draws: window.__V.draws, state: window.__V.state, blend: window.__V.blend }; }, [STEP, beat]);
    sinceShot += STEP * 1000;
    if (sinceShot >= everyMs) {
      sinceShot = 0;
      const p = path.join(OUT, `${String(shotN).padStart(3, '0')}-${label}-t${simTime.toFixed(2)}.png`);
      await page.screenshot({ path: p });
      events.push({ shot: path.basename(p), label, simTime: +simTime.toFixed(3), ...info });
      shotN++;
    }
    simTime += STEP;
  }
}

// idle settle
await runFor(0.5, 'idle', 100);
// ready
await page.evaluate(() => window.__V.anim.play('ready', { loop: true }));
await runFor(0.5, 'ready', 100);
// windup: scrub 0 -> contact over 0.35s, hold at contact
const contact = await page.evaluate(() => window.__V.anim.actions.swing.getClip().duration * 0.6);
await page.evaluate((c) => window.__V.anim.play('swing', { to: c, dur: 0.35, hold: true }), contact);
await runFor(0.5, 'windup', 60);
// strike: from contact to end
await page.evaluate((c) => window.__V.anim.play('swing', { from: c }), contact);
await runFor(1.2, 'strike', 80);
// celebrate
await page.evaluate(() => window.__V.anim.react('perfect'));
await runFor(1.0, 'celebrate', 120);

const finalErrors = errors.filter((e) => !/favicon/i.test(e));
console.log('events:', JSON.stringify(events, null, 2));
console.log('errors:', JSON.stringify(finalErrors));
console.log(finalErrors.length === 0 ? 'VERIFY_BLENDER_ANIM_NO_ERRORS' : 'VERIFY_BLENDER_ANIM_ERRORS_FOUND');
console.log('shots -> ' + OUT);
await browser.close();
server.kill();
rmSync(PAGE_PATH, { force: true });
