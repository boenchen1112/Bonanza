"""
Cast (8 Chomp Chorus / party characters) Blender -> Unity pipeline.

Run headless (Blender 4.2+ / 5.x):

  blender -b --factory-startup --python Tools/blender/cast_export.py -- <command> [ids...] [--src DIR] [--out DIR]

Commands
  export   import the web GLB, clean it, write <out>/fbx/cast-<id>.fbx and cast-<id>.json (sidecar)
  verify   re-import the FBX into a fresh scene and compare it with the GLB (counts, names,
           clip ranges, and posed world-space vertices / anchors at sampled frames)
  clipcheck  per-clip penetration scan (island vs island), writes <out>/clip-check/<id>.json
  sheets   render contact sheets of the worst frames (reads the clipcheck json)
  preview  render a turntable + idle still per character to <out>/preview/

Defaults: src = ../Bonanza-web/web/src/assets/chars (sibling checkout), out = Art/cast-staging.
The cast GLBs are the web build's source of truth; nothing here edits them.

What the export changes versus the GLB (and why), all in clean_scene():
  * drops the 'Icosphere' bone-shape helper the glTF importer adds and the 13 unweighted Mixamo
    end-joint empties (finger *4, toe/head *_End): they are not in the skin, so they move nothing;
  * keeps the four face anchors (face_anchor, head_top, head_centre, head_side) as empties
    parented to mixamorigHead, same as the web (headRadius / attach use them);
  * keeps the non-uniform per-character 'ybot' root scale: it is the cast's silhouette (TUFF
    wide, MIMO tall ...) and the web's clips are authored under it. Verified numerically.
"""
import bpy, sys, os, json, math, random
from mathutils import Vector, Matrix
try:
    import numpy as np
except Exception:  # Blender ships numpy; guard anyway
    np = None

IDS = ['bopp', 'zizz', 'kwark', 'tuff', 'mimo', 'nibb', 'glub', 'fizz']
ANCHORS = ['face_anchor', 'head_top', 'head_centre', 'head_side']
FPS = 24  # the GLB samples are at 24 fps (idle: 201 samples over 8.333 s)
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))


def parse_args():
    a = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    opts = {'src': os.path.abspath(os.path.join(REPO, '..', 'Bonanza-web', 'web', 'src', 'assets', 'chars')),
            'out': os.path.join(REPO, 'Art', 'cast-staging'), 'step': 3}
    pos = []
    i = 0
    while i < len(a):
        if a[i] in ('--src', '--out', '--step'):
            opts[a[i][2:]] = a[i + 1] if a[i] != '--step' else int(a[i + 1]); i += 2
        else:
            pos.append(a[i]); i += 1
    cmd = pos[0] if pos else 'export'
    ids = pos[1:] or IDS
    return cmd, ids, opts


# ------------------------------------------------------------------ scene helpers

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS


def import_glb(path):
    reset()
    bpy.ops.import_scene.gltf(filepath=path)
    return find_parts()


def import_fbx(path):
    reset()
    bpy.ops.import_scene.fbx(filepath=path, use_anim=True, automatic_bone_orientation=False,
                             global_scale=1.0)
    return find_parts()


def find_parts():
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    mesh = next(o for o in bpy.data.objects if o.type == 'MESH' and o.find_armature())
    return arm, mesh


def clean_scene(arm, mesh):
    """Remove importer helpers; returns list of removed names."""
    removed = []
    for o in list(bpy.data.objects):
        if o in (arm, mesh) or o.name in ANCHORS or (o.type == 'EMPTY' and o.name == 'ybot'):
            continue
        removed.append(o.name)
        bpy.data.objects.remove(o, do_unlink=True)
    # the importer parks every clip on its own NLA track as well as in the action list
    ad = arm.animation_data
    if ad:
        for t in list(ad.nla_tracks):
            ad.nla_tracks.remove(t)
    for a in bpy.data.actions:
        a.use_fake_user = True
    return removed


def actions_by_name():
    return {a.name: a for a in bpy.data.actions}


def set_clip(arm, name):
    """Assign an action (and its slot, Blender 4.4+ slotted actions) to the armature."""
    act = bpy.data.actions[name]
    arm.animation_data_create()
    arm.animation_data.action = act
    if hasattr(arm.animation_data, 'action_slot') and act.slots:
        slot = next((s for s in act.slots if s.target_id_type == 'OBJECT'), act.slots[0])
        arm.animation_data.action_slot = slot
    return act


def pose_at(frame):
    sc = bpy.context.scene
    sc.frame_set(int(frame))
    bpy.context.view_layer.update()


def clip_info(arm):
    out = {}
    for a in sorted(bpy.data.actions, key=lambda a: a.name):
        s, e = a.frame_range
        out[a.name] = {'start': s, 'end': e, 'frames': int(e - s) + 1, 'seconds': round((e - s) / FPS, 4)}
    return out


def world_verts(obj, shape_weights=None):
    """Evaluated (deformed) world-space vertex positions as an (n,3) array."""
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg)
    me = ev.to_mesh()
    n = len(me.vertices)
    co = np.empty(n * 3, dtype=np.float64)
    me.vertices.foreach_get('co', co)
    co = co.reshape(n, 3)
    mw = np.array(ev.matrix_world)
    out = co @ mw[:3, :3].T + mw[:3, 3]
    ev.to_mesh_clear()
    return out


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def anchor_world(name):
    o = bpy.data.objects.get(name)
    return np.array(o.matrix_world.translation) if o else None


# ------------------------------------------------------------------ export

def export_one(cid, opts):
    src = os.path.join(opts['src'], f'cast-{cid}.glb')
    outdir = os.path.join(opts['out'], 'fbx')
    os.makedirs(outdir, exist_ok=True)
    arm, mesh = import_glb(src)
    removed = clean_scene(arm, mesh)
    ybot = bpy.data.objects['ybot']
    info = {
        'id': cid, 'source': os.path.basename(src), 'removed': removed,
        'tris': tri_count(mesh), 'verts': len(mesh.data.vertices),
        'materials': [m.name if m else None for m in mesh.data.materials],
        'material_colors_linear': {m.name: [round(c, 5) for c in m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value[:3]] for m in mesh.data.materials if m and m.use_nodes},
        'shape_keys': [k.name for k in mesh.data.shape_keys.key_blocks] if mesh.data.shape_keys else [],
        'bones': [b.name for b in arm.data.bones],
        'ybot_scale_blender_xyz': [round(x, 6) for x in ybot.scale],
        'clips': clip_info(arm),
        'fps': FPS,
        'anchors': {n: [round(x, 5) for x in bpy.data.objects[n].matrix_world.translation] for n in ANCHORS},
    }
    # leave the rest pose on the armature; the FBX exporter bakes every action by itself
    arm.animation_data.action = None
    for o in bpy.data.objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    path = os.path.join(outdir, f'cast-{cid}.fbx')
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'EMPTY', 'ARMATURE', 'MESH'},
        use_mesh_modifiers=False, mesh_smooth_type='OFF', use_tspace=False,
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL',
        apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', global_scale=1.0,
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
        path_mode='STRIP', embed_textures=False)
    info['fbx_bytes'] = os.path.getsize(path)
    with open(os.path.join(outdir, f'cast-{cid}.json'), 'w') as f:
        json.dump(info, f, indent=1)
    print('EXPORTED', cid, path, info['fbx_bytes'], 'tris', info['tris'])


# ------------------------------------------------------------------ verify

def sample_frames(info_clips):
    pts = []
    for name, c in info_clips.items():
        a, b = int(c['start']), int(c['end'])
        pts += [(name, a), (name, (a + b) // 2), (name, b)]
        pts += [(name, a + (b - a) * k // 6) for k in (1, 2, 4, 5)]
    return pts


def snapshot(arm, mesh, frames):
    """{(clip, frame): (verts, anchors)}"""
    res = {}
    for name, f in frames:
        set_clip(arm, name)
        pose_at(f)
        res[(name, f)] = (world_verts(mesh), {n: anchor_world(n) for n in ANCHORS})
    return res


def nn_dist(a, b):
    """max over a of nearest distance to b (chunked brute force with scipy-free KD via mathutils)."""
    from mathutils.kdtree import KDTree
    kd = KDTree(len(b))
    for i, p in enumerate(b):
        kd.insert(Vector(p), i)
    kd.balance()
    d = [kd.find(Vector(p))[2] for p in a]
    return float(max(d)), float(np.mean(d))


def verify_one(cid, opts):
    glb = os.path.join(opts['src'], f'cast-{cid}.glb')
    fbx = os.path.join(opts['out'], 'fbx', f'cast-{cid}.fbx')
    arm, mesh = import_glb(glb)
    clean_scene(arm, mesh)
    ref_clips = clip_info(arm)
    frames = sample_frames(ref_clips)
    ref = snapshot(arm, mesh, frames)
    ref_stat = {'verts': len(mesh.data.vertices), 'tris': tri_count(mesh),
                'keys': [k.name for k in mesh.data.shape_keys.key_blocks], 'bones': sorted(b.name for b in arm.data.bones),
                'mats': [m.name for m in mesh.data.materials],
                'dims': [round(x, 4) for x in world_verts(mesh).max(0) - world_verts(mesh).min(0)]}
    # shape-key sanity: deltas for each key
    ref_keys = {}
    for kb in mesh.data.shape_keys.key_blocks[1:]:
        d = np.empty(len(kb.data) * 3); kb.data.foreach_get('co', d)
        bb = np.empty(len(kb.data) * 3); mesh.data.shape_keys.key_blocks[0].data.foreach_get('co', bb)
        ref_keys[kb.name] = float(np.abs(d - bb).max())

    arm2, mesh2 = import_fbx(fbx)
    c2 = clip_info(arm2)
    stat2 = {'verts': len(mesh2.data.vertices), 'tris': tri_count(mesh2),
             'keys': [k.name for k in mesh2.data.shape_keys.key_blocks] if mesh2.data.shape_keys else [],
             'bones': sorted(b.name for b in arm2.data.bones), 'mats': [m.name for m in mesh2.data.materials],
             'dims': [round(x, 4) for x in world_verts(mesh2).max(0) - world_verts(mesh2).min(0)]}
    clipnames2 = {n.split('|')[-1]: n for n in bpy.data.actions.keys()}
    report = {'id': cid, 'ref': ref_stat, 'fbx': stat2, 'clip_names_ref': sorted(ref_clips),
              'clip_names_fbx': sorted(clipnames2), 'clip_ranges': {}, 'pose_err': {}}
    for n, c in ref_clips.items():
        act = bpy.data.actions.get(clipnames2.get(n, '')) if n in clipnames2 else None
        report['clip_ranges'][n] = {'ref': [c['start'], c['end']], 'fbx': list(act.frame_range) if act else None}
    # map names for re-imported clips
    for k, v in clipnames2.items():
        pass
    worst = 0.0
    worst_anchor = 0.0
    per_clip = {}
    for (name, f), (v1, an1) in ref.items():
        real = clipnames2.get(name)
        if not real:
            continue
        set_clip(arm2, real)
        a = bpy.data.actions[real]
        pose_at(f + int(a.frame_range[0]))
        v2 = world_verts(mesh2)
        mx, mean = nn_dist(v2, v1)
        mx2, _ = nn_dist(v1, v2)
        m = max(mx, mx2)
        # anchors (names may be suffixed in the FBX import)
        ae = 0.0
        for n in ANCHORS:
            o2 = bpy.data.objects.get(n)
            if o2 is not None and an1[n] is not None:
                ae = max(ae, float(np.linalg.norm(np.array(o2.matrix_world.translation) - an1[n])))
        pc = per_clip.setdefault(name, {'vert': 0.0, 'anchor': 0.0})
        pc['vert'] = max(pc['vert'], m); pc['anchor'] = max(pc['anchor'], ae)
        worst = max(worst, m); worst_anchor = max(worst_anchor, ae)
    report['pose_err'] = {'per_clip': per_clip, 'max_vertex_m': worst, 'max_anchor_m': worst_anchor}
    report['shape_key_ref_maxdelta'] = ref_keys
    fk = {}
    if mesh2.data.shape_keys:
        kbs = mesh2.data.shape_keys.key_blocks
        bb = np.empty(len(kbs[0].data) * 3); kbs[0].data.foreach_get('co', bb)
        for kb in kbs[1:]:
            d = np.empty(len(kb.data) * 3); kb.data.foreach_get('co', d)
            fk[kb.name] = float(np.abs(d - bb).max())
    report['shape_key_fbx_maxdelta'] = fk
    report['shape_key_delta_match'] = all(abs(fk.get(k, -1) - v) < 1e-4 * max(1, abs(v)) + 1e-5 for k, v in ref_keys.items())
    p = os.path.join(opts['out'], 'fbx', f'verify-{cid}.json')
    with open(p, 'w') as f:
        json.dump(report, f, indent=1, default=str)
    print('VERIFY', cid, 'verts', ref_stat['verts'], '->', stat2['verts'], 'keys', stat2['keys'] == ref_stat['keys'],
          'bones', stat2['bones'] == ref_stat['bones'], 'maxvert', round(worst, 6), 'maxanchor', round(worst_anchor, 6), 'keydeltas_match', report['shape_key_delta_match'])



# ------------------------------------------------------------------ clip check
#
# The cast is ONE skinned mesh (mannequin + head shell + body shell + crest/ears/eyes/outfit), so
# "does it clip" cannot be a whole-mesh self-intersection test: parts overlap by design at bind
# (the shell wraps the torso, the head shell sits on the neck, joint spheres overlap limbs).
# We therefore
#   1. split vertices into segments by dominant bone (upper arm, forearm, hand+fingers, thigh, calf,
#      foot, hips, spine/shoulders, neck, head incl. crest/ears/eyes),
#   2. build a BVH per segment each frame (world space, so the non-uniform ybot scale is included),
#   3. for each ordered pair (A, B) test A's vertices against B's surface: a vertex is INSIDE B if it is
#      behind the nearest face (dot(v - p, n) < 0); depth = distance to that face,
#   4. report only NEW penetration: vertices that were outside B in the rest pose (so joint overlaps and
#      shell-around-torso are baseline, not clipping), deeper than a tolerance.
# Pass 2 repeats it with A dilated by the outline-hull width (0.0042 * camera distance, 7 m).

GROUPS = [
    ('hips', ['Hips']),
    ('spine', ['Spine', 'Spine1', 'Spine2', 'LeftShoulder', 'RightShoulder']),
    ('neck', ['Neck']),
    ('head', ['Head']),
    ('armL', ['LeftArm']), ('foreL', ['LeftForeArm']), ('handL', ['LeftHand']),
    ('armR', ['RightArm']), ('foreR', ['RightForeArm']), ('handR', ['RightHand']),
    ('thighL', ['LeftUpLeg']), ('calfL', ['LeftLeg']), ('footL', ['LeftFoot', 'LeftToeBase']),
    ('thighR', ['RightUpLeg']), ('calfR', ['RightLeg']), ('footR', ['RightFoot', 'RightToeBase']),
]
# pairs that touch by construction at every pose (parent/child joints): reported with a higher tolerance
ADJACENT = {frozenset(p) for p in [
    ('hips', 'spine'), ('spine', 'neck'), ('neck', 'head'), ('spine', 'armL'), ('spine', 'armR'),
    ('armL', 'foreL'), ('armR', 'foreR'), ('foreL', 'handL'), ('foreR', 'handR'),
    ('hips', 'thighL'), ('hips', 'thighR'), ('thighL', 'calfL'), ('thighR', 'calfR'),
    ('calfL', 'footL'), ('calfR', 'footR')]}
HULL_W = 0.0042 * 7.0   # metres at a 7 m camera distance


def group_of_bone(name):
    n = name.replace('mixamorig', '')
    for gname, prefixes in GROUPS:
        for pre in prefixes:
            if n == pre or (n.startswith(pre) and pre in ('LeftHand', 'RightHand') and n[len(pre):len(pre) + 1].isdigit()):
                return gname
            # fingers: LeftHandThumb1 ... belong to the hand group
            if pre in ('LeftHand', 'RightHand') and n.startswith(pre):
                return gname
    return None


def dominant_group(mesh):
    """Per-vertex index into GROUPS of the vertex's strongest bone (-1 if none)."""
    gnames = [g.name for g in mesh.vertex_groups]
    names = [g for g, _ in GROUPS]
    gi = {nm: (names.index(group_of_bone(nm)) if group_of_bone(nm) else -1) for nm in gnames}
    out = np.full(len(mesh.data.vertices), -1, dtype=np.int32)
    for v in mesh.data.vertices:
        best, bw = -1, 0.0
        for ge in v.groups:
            if ge.weight > bw:
                bw, best = ge.weight, ge.group
        out[v.index] = gi[gnames[best]] if best >= 0 else -1
    return out


def build_islands(mesh):
    """Weld vertices by position, then split into connected islands (the glTF mesh is split at every
    hard normal / material edge). Returns island id per vertex, triangles, island per triangle,
    and the number of open (non-manifold) edges per island."""
    from collections import Counter
    me = mesh.data
    n = len(me.vertices)
    co = np.empty(n * 3); me.vertices.foreach_get('co', co); co = co.reshape(n, 3)
    kk = np.round(co / 1e-4).astype(np.int64)
    d = {}
    rep = np.empty(n, dtype=np.int64)
    for i, k in enumerate(map(tuple, kk)):
        rep[i] = d.setdefault(k, i)
    par = list(range(n))

    def find(x):
        while par[x] != x:
            par[x] = par[par[x]]
            x = par[x]
        return x
    tris = []
    for p in me.polygons:
        vs = list(p.vertices)
        for k in range(1, len(vs) - 1):
            tris.append((vs[0], vs[k], vs[k + 1]))
        r = [rep[v] for v in vs]
        for v in r[1:]:
            x, y = find(r[0]), find(v)
            if x != y:
                par[x] = y
    tris = np.array(tris, dtype=np.int32)
    roots = np.array([find(rep[i]) for i in range(n)])
    uniq = {r: i for i, r in enumerate(sorted(set(roots.tolist())))}
    isl = np.array([uniq[r] for r in roots], dtype=np.int32)
    tri_isl = isl[tris[:, 0]]
    ec = Counter()
    for t in tris:
        r = [rep[x] for x in t]
        for i in range(3):
            ec[(min(r[i], r[(i + 1) % 3]), max(r[i], r[(i + 1) % 3]))] += 1
    open_edges = Counter()
    for (x, y), c in ec.items():
        if c != 2:
            open_edges[int(isl[x])] += 1
    return isl, tris, tri_isl, open_edges


def vertex_normals_world(obj):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg)
    me = ev.to_mesh()
    n = len(me.vertices)
    nn = np.empty(n * 3); me.vertex_normals.foreach_get('vector', nn)
    nn = nn.reshape(n, 3)
    mw = np.array(ev.matrix_world)[:3, :3]
    inv_t = np.linalg.inv(mw).T
    out = nn @ inv_t.T
    out /= np.maximum(np.linalg.norm(out, axis=1, keepdims=True), 1e-12)
    ev.to_mesh_clear()
    return out


def visible_tris(verts, tris):
    """Rest-pose exterior triangles: the face normal ray, started just off the face, escapes the mesh.
    Interior pieces (the mannequin torso under the body shell, joint spheres) are 'hidden' and never
    count as clipping, because the player cannot see them."""
    from mathutils.bvhtree import BVHTree
    tree = BVHTree.FromPolygons([tuple(map(float, v)) for v in verts], [tuple(map(int, t)) for t in tris], all_triangles=True)
    a, b, c = verts[tris[:, 0]], verts[tris[:, 1]], verts[tris[:, 2]]
    cen = (a + b + c) / 3
    nrm = np.cross(b - a, c - a)
    nrm /= np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-12)
    vis = np.zeros(len(tris), dtype=bool)
    for i in range(len(tris)):
        o = Vector(cen[i] + nrm[i] * 1e-4)
        hit = tree.ray_cast(o, Vector(nrm[i]))
        vis[i] = hit[0] is None
    return vis


_DIRS = [Vector((0.5773, 0.5773, 0.5773)).normalized(), Vector((-0.3, 0.9, 0.31)).normalized(),
         Vector((0.2, -0.35, -0.91)).normalized()]


def inside_island(tree, p):
    """Ray-parity point-in-solid, majority of three directions (robust to a ray grazing an edge)."""
    votes = 0
    for dvec in _DIRS:
        cnt, o = 0, p
        for _ in range(12):
            hit = tree.ray_cast(o, dvec)
            if hit[0] is None:
                break
            cnt += 1
            o = hit[0] + dvec * 1e-5
        votes += cnt & 1
    return votes >= 2


def penetrations(verts, norms, ctx, dilate=0.0):
    """{(group a, island b): [(vertex, depth)]}: rest-visible vertices of group a that are inside closed
    island b (b belongs to another group). depth = distance to b's nearest face."""
    from mathutils.bvhtree import BVHTree
    tris, tri_isl, isl = ctx['tris'], ctx['tri_isl'], ctx['isl']
    P = verts + (norms * dilate if dilate else 0.0)
    vgrp, isl_grp = ctx['vgrp'], ctx['isl_grp']
    vl = [tuple(map(float, v)) for v in verts]
    cand = np.nonzero(ctx['vvis'])[0]
    pc = P[cand]
    out = {}
    for b in ctx['targets']:
        sub = tris[tri_isl == b]
        pts = verts[np.unique(sub)]
        lo, hi = pts.min(0) - 0.01 - abs(dilate), pts.max(0) + 0.01 + abs(dilate)
        sel = cand[((pc >= lo) & (pc <= hi)).all(1)]
        if not len(sel):
            continue
        tree = BVHTree.FromPolygons(vl, [tuple(map(int, t)) for t in sub], all_triangles=True)
        for vi in sel:
            if vgrp[vi] < 0 or vgrp[vi] == isl_grp[b] or isl[vi] == b:
                continue
            p = Vector(P[vi])
            if inside_island(tree, p):
                dist = tree.find_nearest(p)[3]
                out.setdefault((int(vgrp[vi]), int(b)), []).append((int(vi), float(dist)))
    return out


def prepare_clipcheck(arm, mesh):
    isl, tris, tri_isl, open_edges = build_islands(mesh)
    names = [g for g, _ in GROUPS]
    vgrp = dominant_group(mesh)
    nisl = int(isl.max()) + 1
    isl_grp = np.array([(np.bincount(vgrp[isl == i][vgrp[isl == i] >= 0]).argmax() if (vgrp[isl == i] >= 0).any() else -1)
                        for i in range(nisl)], dtype=np.int32)
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0); pb.scale = (1, 1, 1)
    bpy.context.view_layer.update()
    v0 = world_verts(mesh); n0 = vertex_normals_world(mesh)
    vis = visible_tris(v0, tris)
    vvis = np.zeros(len(v0), dtype=bool); vvis[np.unique(tris[vis])] = True
    sizes = np.bincount(tri_isl, minlength=nisl)
    frac = np.array([vis[tri_isl == i].mean() if sizes[i] else 0 for i in range(nisl)])
    # targets: islands that are (nearly) closed, at least a quarter visible and big enough to see
    targets = [i for i in range(nisl) if frac[i] >= 0.25 and sizes[i] >= 60 and isl_grp[i] >= 0 and open_edges.get(i, 0) <= 8]
    ctx = dict(isl=isl, tris=tris, tri_isl=tri_isl, isl_grp=isl_grp, vvis=vvis, targets=targets, vgrp=vgrp)
    info = {'islands': nisl, 'visible_tris': int(vis.sum()), 'tris': int(len(vis)),
            'targets': [(int(b), names[isl_grp[b]], int(sizes[b]), int(open_edges.get(b, 0))) for b in targets],
            'open_islands': {int(k): v for k, v in open_edges.items() if v}}
    return ctx, v0, n0, info


def clipcheck_one(cid, opts):
    glb = os.path.join(opts['src'], f'cast-{cid}.glb')
    arm, mesh = import_glb(glb)
    clean_scene(arm, mesh)
    names = [g for g, _ in GROUPS]
    ctx, v0, n0, info = prepare_clipcheck(arm, mesh)
    base = {}
    for dil in (0.0, HULL_W):
        for key, lst in penetrations(v0, n0, ctx, dil).items():
            base[(dil, key)] = {vi for vi, _ in lst}
    results = {'id': cid, 'groups': names, 'hull_w': HULL_W, 'info': info, 'frames': []}
    step = opts.get('step', 3)
    for cname in sorted(bpy.data.actions.keys()):
        act = bpy.data.actions[cname]
        s, e = int(act.frame_range[0]), int(act.frame_range[1])
        for f in sorted(set(list(range(s, e + 1, step)) + [e])):
            set_clip(arm, cname)
            pose_at(f)
            v = world_verts(mesh); nrm = vertex_normals_world(mesh)
            rec = {'clip': cname, 'frame': f, 'body': [], 'hull': []}
            for dil, label in ((0.0, 'body'), (HULL_W, 'hull')):
                for (a, b), lst in penetrations(v, nrm, ctx, dil).items():
                    bs = base.get((dil, (a, b)), set())
                    new = [(vi, d) for vi, d in lst if vi not in bs]
                    if new:
                        mx = max(new, key=lambda t: t[1])
                        rec[label].append((names[a], f'{names[ctx["isl_grp"][b]]}#{b}', round(mx[1], 4), len(new), mx[0]))
            results['frames'].append(rec)
    p = os.path.join(opts['out'], 'clip-check', f'{cid}.json')
    os.makedirs(os.path.dirname(p), exist_ok=True)
    json.dump(results, open(p, 'w'))
    print('CLIPCHECK', cid, len(results['frames']), 'frames ->', p)



# ------------------------------------------------------------------ renders

def setup_render(res=420):
    sc = bpy.context.scene
    for eng in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT'):
        try:
            sc.render.engine = eng
            break
        except TypeError:
            continue
    sc.render.resolution_x = sc.render.resolution_y = res
    sc.render.film_transparent = False
    w = bpy.data.worlds.new('w'); w.use_nodes = True
    w.node_tree.nodes['Background'].inputs[0].default_value = (0.78, 0.8, 0.86, 1)
    w.node_tree.nodes['Background'].inputs[1].default_value = 1.0
    sc.world = w
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN'))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(50), 0, math.radians(35))
    bpy.context.collection.objects.link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    bpy.context.collection.objects.link(cam)
    sc.camera = cam
    try:
        sc.view_settings.view_transform = 'Standard'
    except Exception:
        pass
    return cam


def aim(cam, target, direction, dist, lens=50):
    cam.data.lens = lens
    d = Vector(direction).normalized()
    cam.location = Vector(target) + d * dist
    look = (Vector(target) - cam.location).to_track_quat('-Z', 'Y')
    cam.rotation_euler = look.to_euler()


VIEWS = {'front': (0, -1, 0.15), 'three': (-0.8, -0.8, 0.25), 'side': (-1, 0, 0.15), 'back': (0, 1, 0.15), 'three_r': (0.8, -0.8, 0.25)}


def render_view(cam, mesh, view, path, pad=1.1, focus=None, size=None):
    v = world_verts(mesh)
    lo, hi = v.min(0), v.max(0)
    c = (lo + hi) / 2 if focus is None else focus
    size = float(max(hi - lo)) * pad if size is None else size
    aim(cam, c, VIEWS[view], size / (2 * math.tan(math.radians(19))), lens=50)
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def tile(paths, out, cols):
    """Tile PNGs into one sheet with numpy (Blender's Python has no PIL)."""
    ims = []
    for p in paths:
        im = bpy.data.images.load(p)
        w, h = im.size
        a = np.empty(w * h * 4, dtype=np.float32); im.pixels.foreach_get(a)
        ims.append(a.reshape(h, w, 4)); bpy.data.images.remove(im)
    rows = (len(ims) + cols - 1) // cols
    sheet = np.ones((h * rows, w * cols, 4), dtype=np.float32)
    for i, a in enumerate(ims):
        r, c = divmod(i, cols)
        y0 = (rows - 1 - r) * h  # image rows are bottom-up
        sheet[y0:y0 + h, c * w:(c + 1) * w] = a
    img = bpy.data.images.new('sheet', w * cols, h * rows)
    img.pixels.foreach_set(sheet.ravel())
    img.filepath_raw = out; img.file_format = 'PNG'; img.save()
    bpy.data.images.remove(img)


def preview_one(cid, opts):
    glb = os.path.join(opts['src'], f'cast-{cid}.glb')
    arm, mesh = import_glb(glb)
    clean_scene(arm, mesh)
    cam = setup_render(360)
    tmp = os.path.join(opts['out'], 'tmp'); os.makedirs(tmp, exist_ok=True)
    os.makedirs(os.path.join(opts['out'], 'preview'), exist_ok=True)
    set_clip(arm, 'idle'); pose_at(0)
    paths = []
    for vname in ('front', 'three', 'side', 'back'):
        pth = os.path.join(tmp, f'{cid}_{vname}.png'); render_view(cam, mesh, vname, pth); paths.append(pth)
    # face morphs on the front view: neutral already above; smile, mouthOpen, frown+lids, browsUp
    keys = mesh.data.shape_keys.key_blocks
    for combo in ({'smile': 1, 'mouthOpen': .45}, {'mouthOpen': 1}, {'frown': 1, 'lidsDown': .85, 'browsPinch': 1}, {'browsUp': 1, 'mouthOpen': .55}):
        for k in keys[1:]:
            k.value = combo.get(k.name, 0)
        pth = os.path.join(tmp, f'{cid}_face_{"_".join(combo)}.png'); render_view(cam, mesh, 'front', pth, focus=anchor_world('head_centre'), size=float(np.linalg.norm(anchor_world('head_side') - anchor_world('head_centre'))) * 3.2)
        paths.append(pth)
    for k in keys[1:]:
        k.value = 0
    tile(paths, os.path.join(opts['out'], 'preview', f'{cid}.png'), 4)
    print('PREVIEW', cid)


def sheets_one(cid, opts, top=6):
    js = json.load(open(os.path.join(opts['out'], 'clip-check', f'{cid}.json')))
    scored = []
    for r in js['frames']:
        sc_ = 0.0
        for a, b, depth, cnt, vi in r['body']:
            if frozenset((a, b.split('#')[0])) in ADJACENT:
                continue
            sc_ = max(sc_, depth)
        scored.append((sc_, r['clip'], r['frame']))
    scored.sort(reverse=True)
    picks, seen = [], set()
    for sc_, c, f in scored:
        if (c, f // 12) in seen:
            continue
        seen.add((c, f // 12)); picks.append((sc_, c, f))
        if len(picks) >= top:
            break
    glb = os.path.join(opts['src'], f'cast-{cid}.glb')
    arm, mesh = import_glb(glb)
    clean_scene(arm, mesh)
    cam = setup_render(300)
    tmp = os.path.join(opts['out'], 'tmp')
    paths = []
    for sc_, c, f in picks:
        set_clip(arm, c); pose_at(f)
        for vname in ('front', 'three', 'side'):
            pth = os.path.join(tmp, f'{cid}_{c}_{f}_{vname}.png'); render_view(cam, mesh, vname, pth); paths.append(pth)
    tile(paths, os.path.join(opts['out'], 'clip-check', f'{cid}_worst.png'), 3)
    print('SHEETS', cid, [(c, f, round(sc_, 3)) for sc_, c, f in picks])


if __name__ == '__main__':
    cmd, ids, opts = parse_args()
    for cid in ids:
        {'export': export_one, 'verify': verify_one, 'clipcheck': clipcheck_one, 'preview': preview_one, 'sheets': sheets_one}.get(cmd, lambda *_: None)(cid, opts)
