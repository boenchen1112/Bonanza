/**
 * Asset contract for a designed cast member's GLB (docs/design/cast-sheet.html):
 * what the game relies on, checked from the file itself.
 *
 *   node tools/assets/blender/cast-contract.mjs <file.glb> [...]
 *
 * Pure Node (reads the GLB's JSON and binary chunks), so the test suite runs
 * it against the committed character files.
 */
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

export const CONTRACT = {
  roles: ['body', 'trim', 'skin', 'accent', 'eye'],
  morphTargets: ['mouthOpen', 'smile', 'frown', 'lidsDown', 'browsUp', 'browsPinch'],
  clips: ['celebrate', 'dance', 'fail', 'idle', 'pitch', 'ready', 'swing', 'taunt'],
  maxTriangles: 20000,
  headBone: 'mixamorigHead',
};

/** Split a GLB buffer into its JSON document and binary chunk. */
export function readGlb(buf) {
  if (buf.readUInt32LE(0) !== 0x46546c67) throw new Error('not a GLB');
  let off = 12;
  let json = null;
  let bin = null;
  while (off < buf.length) {
    const len = buf.readUInt32LE(off);
    const type = buf.readUInt32LE(off + 4);
    const data = buf.subarray(off + 8, off + 8 + len);
    if (type === 0x4e4f534a) json = JSON.parse(data.toString('utf8'));
    else if (type === 0x004e4942) bin = data;
    off += 8 + len;
  }
  return { json, bin };
}

const COMPONENTS = { 5121: 1, 5123: 2, 5125: 4, 5126: 4 };
const SIZES = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4 };

function accessorReader(json, bin, index) {
  const a = json.accessors[index];
  const view = json.bufferViews[a.bufferView];
  const comp = COMPONENTS[a.componentType];
  const n = SIZES[a.type];
  const stride = view.byteStride || comp * n;
  const base = (view.byteOffset || 0) + (a.byteOffset || 0);
  const read = {
    5121: (o) => bin.readUInt8(o), 5123: (o) => bin.readUInt16LE(o),
    5125: (o) => bin.readUInt32LE(o), 5126: (o) => bin.readFloatLE(o),
  }[a.componentType];
  let scale = 1;
  if (a.normalized) scale = a.componentType === 5121 ? 255 : a.componentType === 5123 ? 65535 : 1;
  return {
    count: a.count,
    get: (i, k) => read(base + i * stride + k * comp) / (a.componentType === 5126 ? 1 : scale),
    min: a.min, max: a.max,
  };
}

/** [vertexIndex, [dx,dy,dz]] for a morph POSITION accessor, dense or sparse
 *  (Blender's exporter stores only the moved vertices, as a sparse accessor). */
function* morphDeltas(json, bin, index) {
  const a = json.accessors[index];
  if (a.bufferView !== undefined) {
    const r = accessorReader(json, bin, index);
    for (let i = 0; i < r.count; i++) yield [i, [r.get(i, 0), r.get(i, 1), r.get(i, 2)]];
    return;
  }
  if (!a.sparse) return;
  const s = a.sparse;
  const idxView = json.bufferViews[s.indices.bufferView];
  const valView = json.bufferViews[s.values.bufferView];
  const idxBase = (idxView.byteOffset || 0) + (s.indices.byteOffset || 0);
  const valBase = (valView.byteOffset || 0) + (s.values.byteOffset || 0);
  const readIdx = { 5121: (o) => bin.readUInt8(o), 5123: (o) => bin.readUInt16LE(o), 5125: (o) => bin.readUInt32LE(o) }[s.indices.componentType];
  const idxSize = COMPONENTS[s.indices.componentType];
  for (let k = 0; k < s.count; k++) {
    const o = valBase + k * 12;
    yield [readIdx(idxBase + k * idxSize), [bin.readFloatLE(o), bin.readFloatLE(o + 4), bin.readFloatLE(o + 8)]];
  }
}

/**
 * @returns {{ok:boolean, problems:string[], report:object}}
 */
export function checkCastGlb(buf, contract = CONTRACT) {
  const { json, bin } = readGlb(buf);
  const problems = [];
  const materials = (json.materials || []).map((m) => m.name);
  for (const r of contract.roles) if (!materials.includes(r)) problems.push(`missing material '${r}'`);
  const extra = materials.filter((m) => !contract.roles.includes(m));
  if (extra.length) problems.push(`unexpected materials: ${extra.join(', ')}`);

  const skinnedMeshes = (json.nodes || []).filter((n) => n.mesh !== undefined && n.skin !== undefined).map((n) => json.meshes[n.mesh]);
  const unskinned = (json.nodes || []).filter((n) => n.mesh !== undefined && n.skin === undefined);
  if (skinnedMeshes.length !== 1) problems.push(`expected exactly one skinned mesh, found ${skinnedMeshes.length}`);
  if (unskinned.length) problems.push(`unskinned meshes present: ${unskinned.map((n) => n.name).join(', ')}`);

  let triangles = 0;
  for (const mesh of skinnedMeshes) {
    for (const p of mesh.primitives) {
      triangles += (p.indices !== undefined ? json.accessors[p.indices].count : json.accessors[p.attributes.POSITION].count) / 3;
    }
  }
  if (triangles > contract.maxTriangles) problems.push(`${triangles} triangles > ${contract.maxTriangles}`);

  const mesh = skinnedMeshes[0];
  const targets = mesh?.extras?.targetNames || [];
  for (const t of contract.morphTargets) if (!targets.includes(t)) problems.push(`missing morph target '${t}'`);

  const clips = (json.animations || []).map((a) => a.name.replace(/_rig$/, ''));
  for (const c of contract.clips) if (!clips.includes(c)) problems.push(`missing clip '${c}'`);

  // Face morph targets must only move head-bound vertices: a morph that
  // drags body vertices would tear the mesh when the body animates.
  const skin = json.skins?.[0];
  const headJoint = skin ? skin.joints.findIndex((j) => json.nodes[j].name === contract.headBone) : -1;
  let strayMorphVerts = 0;
  if (mesh && bin && headJoint >= 0) {
    for (const p of mesh.primitives) {
      const joints = accessorReader(json, bin, p.attributes.JOINTS_0);
      const weights = accessorReader(json, bin, p.attributes.WEIGHTS_0);
      for (const target of p.targets || []) {
        for (const [i, d] of morphDeltas(json, bin, target.POSITION)) {
          const moved = Math.abs(d[0]) + Math.abs(d[1]) + Math.abs(d[2]) > 1e-6;
          if (!moved) continue;
          let head = 0;
          for (let k = 0; k < 4; k++) if (joints.get(i, k) === headJoint) head += weights.get(i, k);
          if (head < 0.99) strayMorphVerts++;
        }
      }
    }
    if (strayMorphVerts) problems.push(`${strayMorphVerts} morph-target vertices are not bound to ${contract.headBone}`);
  } else if (mesh) {
    problems.push('no skin or head joint to check morph binding against');
  }

  return {
    ok: problems.length === 0,
    problems,
    report: { materials, triangles, morphTargets: targets, clips, strayMorphVerts },
  };
}

if (import.meta.url === pathToFileURL(process.argv[1] || '').href) {
  let bad = 0;
  for (const f of process.argv.slice(2)) {
    const r = checkCastGlb(readFileSync(f));
    console.log(`${r.ok ? 'OK  ' : 'FAIL'} ${f}`, JSON.stringify(r.report));
    for (const p of r.problems) console.log('     -', p);
    if (!r.ok) bad++;
  }
  process.exit(bad ? 1 : 0);
}
