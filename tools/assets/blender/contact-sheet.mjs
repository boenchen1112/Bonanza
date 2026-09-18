#!/usr/bin/env node
/**
 * Contact sheet for designed cast members: front, three-quarter, face states
 * (driving the morph targets) and a mid-swing frame, one PNG per character
 * plus draw-call/triangle counts — for comparing a build against
 * docs/design/cast-sheet.html by eye.
 *
 *   node tools/assets/blender/contact-sheet.mjs [--out dir] <file.glb> [...]
 */
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, copyFileSync, rmSync } from 'node:fs';
import path from 'node:path';

const args = process.argv.slice(2);
const outIdx = args.indexOf('--out');
const ROOT = path.resolve(import.meta.dirname, '../../..');
const OUT = path.resolve(ROOT, outIdx >= 0 ? args[outIdx + 1] : 'runs/contact-sheet');
const files = args.filter((a, i) => a !== '--out' && !(outIdx >= 0 && i === outIdx + 1));
const WEB = path.join(ROOT, 'web');
mkdirSync(OUT, { recursive: true });

// serve.mjs roots at web/ and refuses traversal above it: the page and the
// GLB under test are copied in for the run and removed afterwards.
const PAGE = path.join(WEB, '_contact-sheet.html');
const TEMP_GLB = path.join(WEB, '_contact-sheet.glb');
writeFileSync(PAGE, `<!doctype html><meta charset="utf-8">
<style>html,body{margin:0;background:#171533;overflow:hidden}</style>
<script type="importmap">{"imports":{"three":"/node_modules/three/build/three.module.js","three/addons/":"/node_modules/three/examples/jsm/"}}</script>
<script type="module">
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
const W = 1200, H = 520;
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(W, H); renderer.setPixelRatio(1);
renderer.outputColorSpace = THREE.SRGBColorSpace;
document.body.appendChild(renderer.domElement);
const gltf = await new GLTFLoader().loadAsync('/_contact-sheet.glb');
const LIGHT = () => { const g = new THREE.Group(); g.add(new THREE.HemisphereLight(0xdfe8ff, 0x3b2a55, 1.6)); const d = new THREE.DirectionalLight(0xffffff, 2.2); d.position.set(-2, 4, 5); g.add(d); return g; };
const MORPHS = ['mouthOpen','smile','frown','lidsDown','browsUp','browsPinch'];
function instance(clip, t, face) {
  const scene = new THREE.Scene();
  scene.add(LIGHT());
  const root = THREE.SkeletonUtils ? null : null;
  const obj = gltf.scene.clone(true);
  // SkinnedMesh clones must be rebound; the sheet only needs one pose per view,
  // so pose the original, render, and move on.
  const src = gltf.scene;
  const mixer = new THREE.AnimationMixer(src);
  const a = gltf.animations.find((x) => x.name.replace(/_rig$/, '') === clip);
  mixer.stopAllAction();
  if (a) { const act = mixer.clipAction(a); act.play(); mixer.setTime(t * a.duration); }
  src.traverse((o) => { if (o.morphTargetInfluences && o.morphTargetDictionary) {
    o.morphTargetInfluences.fill(0);
    for (const [k, v] of Object.entries(face || {})) { const i = o.morphTargetDictionary[k]; if (i !== undefined) o.morphTargetInfluences[i] = v; }
  } });
  scene.add(src);
  return { scene, mixer };
}
function shoot(view) {
  const { scene } = instance(view.clip, view.t, view.face);
  const box = new THREE.Box3().setFromObject(gltf.scene);
  const size = box.getSize(new THREE.Vector3()); const c = box.getCenter(new THREE.Vector3());
  const cam = new THREE.PerspectiveCamera(view.fov || 30, view.w / H, 0.01, 100);
  const dist = view.head ? size.y * 1.7 : size.y * 2.1;
  const headBone = gltf.scene.getObjectByName('mixamorigHead');
  const target = view.head && headBone
    ? headBone.getWorldPosition(new THREE.Vector3()).add(new THREE.Vector3(0, size.y * 0.1, 0))
    : c;
  cam.position.set(target.x + Math.sin(view.yaw) * dist, target.y + (view.head ? 0.02 : size.y * 0.05), target.z + Math.cos(view.yaw) * dist);
  cam.lookAt(target);
  renderer.setViewport(view.x, 0, view.w, H); renderer.setScissor(view.x, 0, view.w, H); renderer.setScissorTest(true);
  renderer.render(scene, cam);
  return renderer.info.render.calls;
}
renderer.autoClear = false; renderer.setClearColor(0x171533); renderer.clear();
const views = [
  { x: 0, w: 240, clip: 'idle', t: 0.1, yaw: 0 },
  { x: 240, w: 240, clip: 'idle', t: 0.1, yaw: 0.75 },
  { x: 480, w: 120, clip: 'idle', t: 0.1, yaw: 0, head: true, zoom: 0.2, face: {} },
  { x: 600, w: 120, clip: 'idle', t: 0.1, yaw: 0, head: true, zoom: 0.2, face: { browsUp: 1, mouthOpen: 0.6 } },
  { x: 720, w: 120, clip: 'idle', t: 0.1, yaw: 0, head: true, zoom: 0.2, face: { smile: 1, mouthOpen: 0.5, browsUp: 0.4 } },
  { x: 840, w: 120, clip: 'idle', t: 0.1, yaw: 0, head: true, zoom: 0.2, face: { lidsDown: 1, frown: 1, browsPinch: 1 } },
  { x: 960, w: 240, clip: 'swing', t: 0.45, yaw: 1.2 },
];
renderer.info.autoReset = false;
let draws = 0;
for (const v of views) { renderer.info.reset(); draws = Math.max(draws, shoot(v)); }
let triangles = 0; gltf.scene.traverse((o) => { if (o.isMesh) triangles += (o.geometry.index ? o.geometry.index.count : o.geometry.attributes.position.count) / 3; });
window.__SHEET = { draws, triangles, morphs: (() => { let d = null; gltf.scene.traverse((o) => { if (o.morphTargetDictionary) d = Object.keys(o.morphTargetDictionary); }); return d; })() };
</script>`);

const webRequire = createRequire(path.join(WEB, 'package.json'));
const pw = await import(pathToFileURL(webRequire.resolve('playwright')).href);
const chromium = pw.chromium ?? pw.default.chromium;
const PORT = 9900 + (process.pid % 90);
const server = spawn(process.execPath, [path.join(ROOT, 'tools/harness/serve.mjs'), WEB, String(PORT)], { stdio: ['ignore', 'pipe', 'pipe'] });
await new Promise((r) => { server.stdout.once('data', r); setTimeout(r, 1200); });
const browser = await chromium.launch({ channel: 'chromium', headless: true, args: process.platform === 'win32' ? ['--use-angle=d3d11'] : [] });

let failed = 0;
for (const f of files) {
  copyFileSync(path.resolve(f), TEMP_GLB);
  const page = await (await browser.newContext({ viewport: { width: 1200, height: 520 } })).newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto(`http://127.0.0.1:${PORT}/_contact-sheet.html`, { waitUntil: 'load' });
  await page.waitForFunction('window.__SHEET', null, { timeout: 30000 }).catch(() => {});
  const info = await page.evaluate(() => window.__SHEET || null);
  const shot = path.join(OUT, path.basename(f).replace(/\.glb$/, '.png'));
  await page.screenshot({ path: shot });
  console.log(path.basename(f), JSON.stringify(info), errors.length ? `ERRORS: ${errors.join(' | ')}` : '', '->', shot);
  if (!info || errors.length) failed++;
  await page.close();
}
await browser.close();
server.kill();
rmSync(PAGE, { force: true });
rmSync(TEMP_GLB, { force: true });
process.exit(failed ? 1 : 0);
