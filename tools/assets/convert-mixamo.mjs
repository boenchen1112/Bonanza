#!/usr/bin/env node
/**
 * Mixamo FBX -> one game-ready GLB (mesh + skeleton + named clips).
 *
 *   node tools/assets/convert-mixamo.mjs [--src <dir with the raw FBX>]
 *
 * The raw FBX downloads are NOT committed (Adobe's terms: ship them inside a
 * game, don't redistribute the files), so this script is the only record of
 * how `web/src/assets/chars/ybot.glb` was made. Re-run it after re-downloading
 * or adding a clip; its output is deterministic for the same inputs.
 *
 * What it does, and why:
 *  - Parses with three's own FBXLoader, exports with GLTFExporter: bone names
 *    and track bindings then match exactly what GLTFLoader gives the runtime.
 *  - Welds the FBX's unindexed triangle soup (mergeVertices) — about a third
 *    of the vertices, same pixels.
 *  - Bakes the cm -> m conversion into a 0.01 root node.
 *  - Renames each clip to the animator state it plays, strips horizontal root
 *    motion where the character must stay on its mark (a batter who walks
 *    45cm off the plate during a swing snaps back on the next crossfade), and
 *    drops tracks for bones the mesh doesn't have.
 */

import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import path from 'node:path';

const ROOT = path.resolve(import.meta.dirname, '..', '..');
const WEB = path.join(ROOT, 'web');
const argv = process.argv.slice(2);
// Default: `Mixamo/` at the repo root (gitignored), or at the main checkout's
// root when this runs from a worktree under .claude/worktrees/.
const SRC = argv.includes('--src')
  ? path.resolve(argv[argv.indexOf('--src') + 1])
  : [path.join(ROOT, 'Mixamo'), path.join(ROOT, '..', '..', '..', 'Mixamo')].find((d) => existsSync(d));
if (!SRC) throw new Error('raw Mixamo FBX folder not found; pass --src <dir>');
const OUT = path.join(WEB, 'src', 'assets', 'chars', 'ybot.glb');

// three lives in web/node_modules; resolve it from there.
const req = createRequire(path.join(WEB, 'package.json'));
const imp = (id) => import(pathToFileURL(req.resolve(id)).href);
const THREE = await imp('three');
const { FBXLoader } = await imp('three/examples/jsm/loaders/FBXLoader.js');
const { GLTFExporter } = await imp('three/examples/jsm/exporters/GLTFExporter.js');
const { mergeVertices } = await imp('three/examples/jsm/utils/BufferGeometryUtils.js');
const { MeshoptSimplifier } = await imp('meshoptimizer/simplifier');
await MeshoptSimplifier.ready;

/**
 * Keep this fraction of triangles. Y Bot ships at 55k tris; the toon outline
 * draws every character twice and a Swing Kings stage holds up to eight of
 * them, so the budget is set by an integrated GPU, not by the mannequin.
 * Simplification only rewrites the index buffer — skin weights stay valid.
 */
const KEEP = 0.3;

function simplify(geo) {
  const pos = geo.attributes.position.array;
  const idx = new Uint32Array(geo.index.array);
  const target = Math.floor((idx.length * KEEP) / 3) * 3;
  const [out, err] = MeshoptSimplifier.simplify(idx, pos, 3, target, 0.02, ['LockBorder']);
  geo.setIndex(new THREE.BufferAttribute(out, 1));
  return err;
}

// GLTFExporter's binary path reads its Blob through a FileReader.
globalThis.FileReader ??= class {
  readAsArrayBuffer(blob) { blob.arrayBuffer().then((b) => { this.result = b; this.onloadend?.(); }); }
  readAsDataURL(blob) {
    blob.arrayBuffer().then((b) => {
      this.result = `data:${blob.type};base64,${Buffer.from(b).toString('base64')}`;
      this.onloadend?.();
    });
  }
};

/**
 * Clip table. `inPlace` pins the hips' horizontal position to frame 0;
 * `range` trims [start, end] seconds.
 */
const CLIPS = [
  { file: 'Idle.fbx', name: 'idle', inPlace: true },
  { file: 'Baseball Idle.fbx', name: 'ready', inPlace: true },
  { file: 'Baseball Hit.fbx', name: 'swing', inPlace: true },
  { file: 'Baseball Pitching.fbx', name: 'pitch', inPlace: true },
  { file: 'Victory.fbx', name: 'celebrate', inPlace: true },
  { file: 'Sitting Disbelief.fbx', name: 'fail', inPlace: true },
  { file: 'Standing Taunt Chest Thump.fbx', name: 'taunt', inPlace: true },
  // The dance travels and comes home on its own; pinning it kills the groove.
  { file: 'Swing Dancing.fbx', name: 'dance', inPlace: false },
];

/** Rebuild a geometry keeping only indexed vertices, in first-use order. */
function compact(geo) {
  const idx = geo.index.array;
  const remap = new Int32Array(geo.attributes.position.count).fill(-1);
  let n = 0;
  const newIdx = new Uint32Array(idx.length);
  for (let i = 0; i < idx.length; i++) {
    let r = remap[idx[i]];
    if (r < 0) { r = remap[idx[i]] = n++; }
    newIdx[i] = r;
  }
  const out = new THREE.BufferGeometry();
  for (const [k, a] of Object.entries(geo.attributes)) {
    const arr = new a.array.constructor(n * a.itemSize);
    for (let v = 0; v < remap.length; v++) {
      const r = remap[v];
      if (r < 0) continue;
      for (let c = 0; c < a.itemSize; c++) arr[r * a.itemSize + c] = a.array[v * a.itemSize + c];
    }
    out.setAttribute(k, new THREE.BufferAttribute(arr, a.itemSize, a.normalized));
  }
  out.setIndex(new THREE.BufferAttribute(n < 65536 ? new Uint16Array(newIdx) : newIdx, 1));
  return out;
}

function loadFbx(file) {
  const b = readFileSync(path.join(SRC, file));
  return new FBXLoader().parse(b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength), SRC + path.sep);
}

// ---------------------------------------------------------------- character
const char = loadFbx('Y Bot.fbx');
char.animations = [];

// FBXLoader gives each skinned mesh its own copy of the skeleton, so the
// export carried two bone hierarchies (`mixamorigHips`, `mixamorigHips_1`,
// ...) and a clip only ever drove one of them — the other mesh stood in
// T-pose. Rebind every mesh to the one hierarchy that is actually parented
// under the character, then drop the orphaned copies.
const canonical = new Map();
const hipsTop = char.children.find((c) => c.isBone);
hipsTop.traverse((o) => { if (o.isBone && !canonical.has(o.name)) canonical.set(o.name, o); });
const boneNames = new Set(canonical.keys());
char.traverse((o) => {
  if (!o.isSkinnedMesh) return;
  const bones = o.skeleton.bones.map((b) => {
    const c = canonical.get(b.name);
    if (!c) throw new Error(`bone ${b.name} missing from the canonical skeleton`);
    return c;
  });
  o.bind(new THREE.Skeleton(bones, o.skeleton.boneInverses), o.bindMatrix);
});
const orphans = [];
char.traverse((o) => { if (o.isBone && canonical.get(o.name) !== o) orphans.push(o); });
for (const o of orphans) o.parent?.remove(o);
console.log(`skeleton: ${canonical.size} bones (${orphans.length} duplicate roots removed)`);

char.traverse((o) => {
  if (o.isSkinnedMesh) {
    const before = o.geometry.attributes.position.count;
    // Welding needs identical attribute sets per vertex; drop per-vertex
    // colour/uv2 channels the FBX may carry that the game never reads.
    // (No textures, so no UVs either.)
    for (const k of Object.keys(o.geometry.attributes)) {
      if (!['position', 'normal', 'skinIndex', 'skinWeight'].includes(k)) o.geometry.deleteAttribute(k);
    }
    o.geometry = mergeVertices(o.geometry, 1e-4);
    const err = simplify(o.geometry);
    // Drop the vertices the simplified index no longer references.
    o.geometry = compact(o.geometry);
    o.geometry.computeBoundingSphere();
    console.log(`  simplify error ${err.toFixed(4)}`);
    // Stand-in materials; the runtime swaps in the house toon material and
    // tints by palette. Names are the contract: `body` and `joints`.
    const role = /joint/i.test(o.name) ? 'joints' : 'body';
    const src = [].concat(o.material)[0];
    o.material = new THREE.MeshStandardMaterial({ name: role, color: src.color, roughness: 0.6 });
    o.name = role;
    console.log(`mesh ${role}: ${before} -> ${o.geometry.attributes.position.count} verts, ${o.geometry.index.count / 3} tris`);
  }
});

const root = new THREE.Group();
root.name = 'ybot';
root.scale.setScalar(0.01);
root.add(char);
char.name = 'rig';

// ---------------------------------------------------------------- clips
const clips = [];
for (const c of CLIPS) {
  const g = loadFbx(c.file);
  const clip = g.animations[0].clone();
  clip.name = c.name;
  clip.tracks = clip.tracks.filter((t) => {
    const [node, prop] = t.name.split('.');
    if (!boneNames.has(node)) return false;
    // Only the hips translate in a humanoid clip; elsewhere position/scale
    // keys are constant noise that bloats the file.
    return prop === 'quaternion' || (prop === 'position' && /Hips$/.test(node));
  });
  if (c.inPlace) {
    const hips = clip.tracks.find((t) => /Hips\.position$/.test(t.name));
    const x0 = hips.values[0], z0 = hips.values[2];
    for (let i = 0; i < hips.values.length; i += 3) { hips.values[i] = x0; hips.values[i + 2] = z0; }
  }
  if (c.range) clip.tracks = THREE.AnimationUtils.subclip(clip, c.name, c.range[0] * 30, c.range[1] * 30, 30).tracks;
  clip.optimize();
  clip.resetDuration();
  clips.push(clip);
  console.log(`clip ${c.name.padEnd(9)} ${clip.duration.toFixed(2)}s  ${clip.tracks.length} tracks  <- ${c.file}`);
}

// ---------------------------------------------------------------- export
const glb = await new GLTFExporter().parseAsync(root, { binary: true, animations: clips, onlyVisible: false });
mkdirSync(path.dirname(OUT), { recursive: true });
writeFileSync(OUT, Buffer.from(glb));
console.log(`wrote ${path.relative(ROOT, OUT)} (${(glb.byteLength / 1024 / 1024).toFixed(2)} MB)`);
