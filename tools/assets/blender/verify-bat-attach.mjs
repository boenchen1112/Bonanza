#!/usr/bin/env node
/**
 * Isolated check of bone-attachment for a prop (the bat/helmet problem in
 * swingKings/world.js): load a Blender body directly, attach bright
 * oversized markers to the right-hand and head bones the exact same way
 * (bone.add(obj)), and screenshot close up - so a placement bug is obvious
 * without the rest of the game's geometry/camera distance hiding it.
 */
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, rmSync } from 'node:fs';
import path from 'node:path';

const ROOT = path.resolve(import.meta.dirname, '../../..');
const WEB = path.join(ROOT, 'web');
const OUT = path.resolve(ROOT, 'runs/verify-bat-attach');
mkdirSync(OUT, { recursive: true });

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

const W = 640, H = 720;
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(W, H);
document.body.appendChild(renderer.domElement);

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x1a1030);
const camera = new THREE.PerspectiveCamera(35, W / H, 0.05, 50);
scene.add(new THREE.HemisphereLight(0xdfe6ff, 0x2a1d5e, 1.8));
const key = new THREE.DirectionalLight(0xffffff, 2.4);
key.position.set(1, 2, 2);
scene.add(key);
scene.add(new THREE.GridHelper(4, 8, 0x555577, 0x333355));

window.__V = { ready: false };

new GLTFLoader().loadAsync('/src/assets/chars/cast-tuff.glb').then((gltf) => {
  const clonedScene = cloneSkinned(gltf.scene);
  scene.add(clonedScene);
  clonedScene.updateMatrixWorld(true);

  const box = new THREE.Box3().setFromObject(clonedScene);
  const size = box.getSize(new THREE.Vector3());
  const center = box.getCenter(new THREE.Vector3());
  const vFov = (camera.fov * Math.PI) / 180;
  const hFov = 2 * Math.atan(Math.tan(vFov / 2) * camera.aspect);
  const dist = Math.max((size.y / 2) / Math.tan(vFov / 2), (size.x / 2) / Math.tan(hFov / 2), 0.3) * 1.6;
  camera.position.set(center.x, center.y, center.z + dist);
  camera.lookAt(center);

  const handR = clonedScene.getObjectByName('mixamorigRightHand');
  const head = clonedScene.getObjectByName('mixamorigHead');
  window.__V.foundHandR = !!handR;
  window.__V.foundHead = !!head;

  // Bright axis-indicator markers, not the real bat mesh - a red sphere at
  // the bone's own origin (should sit exactly at the hand/head) plus a
  // green cone along local +Y and a blue cone along local +Z, so the
  // bone's local axis directions are visible directly rather than inferred.
  function markAxes(bone, label) {
    if (!bone) return;
    const s = new THREE.Vector3();
    bone.getWorldScale(s);
    const inv = new THREE.Vector3(1 / s.x, 1 / s.y, 1 / s.z);
    const originMat = new THREE.MeshBasicMaterial({ color: 0xff2222 });
    const origin = new THREE.Mesh(new THREE.SphereGeometry(0.05, 10, 8), originMat);
    origin.scale.copy(inv);
    bone.add(origin);
    const yMat = new THREE.MeshBasicMaterial({ color: 0x22ff44 });
    const yCone = new THREE.Mesh(new THREE.ConeGeometry(0.03, 0.3, 8), yMat);
    yCone.position.y = 0.15;
    yCone.scale.copy(inv);
    bone.add(yCone);
    const zMat = new THREE.MeshBasicMaterial({ color: 0x2266ff });
    const zCone = new THREE.Mesh(new THREE.ConeGeometry(0.03, 0.3, 8), zMat);
    zCone.rotation.x = Math.PI / 2;
    zCone.position.z = 0.15;
    zCone.scale.copy(inv);
    bone.add(zCone);
  }
  markAxes(handR, 'handR');
  markAxes(head, 'head');

  // The actual bat, at a few candidate rotations, spread out sideways so
  // they don't overlap - fastest way to pick the right one visually rather
  // than reasoning about the bone's local axes abstractly.
  function makeBat() {
    const g = new THREE.Group();
    const batMesh = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.08, 1.2, 10), new THREE.MeshStandardMaterial({ color: 0xd79a58 }));
    batMesh.position.y = -0.6;
    g.add(batMesh);
    const knob = new THREE.Mesh(new THREE.SphereGeometry(0.08, 8, 6), new THREE.MeshStandardMaterial({ color: 0x3a2418 }));
    g.add(knob);
    return g;
  }
  const candidates = [
    [-Math.PI / 2 - 0.3, 0, 0],
    [0, 0, 0],
    [Math.PI / 2, 0, 0],
    [0, 0, -Math.PI / 2],
    [0, 0, Math.PI / 2],
    [Math.PI / 2, 0, Math.PI / 2],
  ];
  const s = new THREE.Vector3();
  handR.getWorldScale(s);
  window.__V.candidateBats = [];
  for (let i = 0; i < candidates.length; i++) {
    const bat = makeBat();
    bat.rotation.set(...candidates[i]);
    bat.scale.set(1 / s.x, 1 / s.y, 1 / s.z);
    bat.name = 'batCandidate' + i;
    handR.add(bat);
    bat.visible = false;
    window.__V.candidateBats.push(bat);
  }
  window.__showBat = (i) => { window.__V.candidateBats.forEach((b, j) => { b.visible = j === i; }); };

  window.__V.mixer = new THREE.AnimationMixer(clonedScene);
  window.__V.actions = Object.fromEntries(gltf.animations.map((a) => [a.name, window.__V.mixer.clipAction(a)]));
  window.__V.ready = true;
}).catch((e) => { window.__V.error = String(e); console.error(e); });

window.__poseAt = (name, t) => {
  const a = window.__V.actions[name];
  if (!a) return;
  a.reset(); a.paused = true; a.enabled = true; a.setEffectiveWeight(1); a.play();
  a.time = t;
  window.__V.mixer.update(0);
  renderer.render(scene, camera);
};

function frame() {
  requestAnimationFrame(frame);
  renderer.render(scene, camera);
}
frame();
</script>
</body></html>
`;
const PAGE_PATH = path.join(WEB, '_verify-bat-attach.html');
writeFileSync(PAGE_PATH, PAGE);

const webRequire = createRequire(path.join(WEB, 'package.json'));
const pw = await import(pathToFileURL(webRequire.resolve('playwright')).href);
const chromium = pw.chromium ?? pw.default.chromium;
const PORT = 8400 + (process.pid % 300);
const server = spawn(process.execPath, [path.join(ROOT, 'tools/harness/serve.mjs'), WEB, String(PORT)], { stdio: ['ignore', 'pipe', 'pipe'] });
await new Promise((r) => { server.stdout.once('data', r); setTimeout(r, 1200); });

const browser = await chromium.launch({ channel: 'chromium', headless: true, args: process.platform === 'win32' ? ['--use-angle=d3d11'] : [] });
const page = await (await browser.newContext({ viewport: { width: 640, height: 720 } })).newPage();
page.on('pageerror', (e) => console.log('[pageerror]', e.message));
await page.goto(`http://127.0.0.1:${PORT}/_verify-bat-attach.html`, { waitUntil: 'load' });
await page.waitForFunction('window.__V && window.__V.ready', null, { timeout: 15000 }).catch(() => {});
const info = await page.evaluate(() => ({ foundHandR: window.__V.foundHandR, foundHead: window.__V.foundHead, error: window.__V.error }));
console.log('info:', JSON.stringify(info));
const worldScale = await page.evaluate(() => {
  const scene = window.__V.mixer._root;
  const handR = scene.getObjectByName('mixamorigRightHand');
  const v = { x: 0, y: 0, z: 0 };
  handR.getWorldScale(v);
  return { x: v.x, y: v.y, z: v.z };
});
console.log('handR world scale:', JSON.stringify(worldScale));

const swingDur = await page.evaluate(() => window.__V.actions.swing.getClip().duration);
for (const [label, frac] of [['rest', 0], ['windup', 0.3], ['contact', 0.6], ['follow', 0.85]]) {
  await page.evaluate(([n, tt]) => window.__poseAt(n, tt), ['swing', swingDur * frac]);
  await page.evaluate(() => { window.__showBat(0); });
  await page.screenshot({ path: path.join(OUT, `bat0-${label}.png`) });
}

console.log('shots -> ' + OUT);
await browser.close();
server.kill();
rmSync(PAGE_PATH, { force: true });
