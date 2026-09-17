"""
Designed cast build (approved look: docs/design/cast-sheet.html).

Builds ONE character from the shared Y Bot skeleton into a single skinned mesh
with exactly five role materials (body, trim, skin, accent, eye), a head shell
with a real face, a body shell matching the portrait shape, crest and outfit
parts, and face shape keys (morph targets) the game drives:

    mouthOpen, smile, frown, lidsDown, browsUp, browsPinch

Driven by build-character.mjs, which reads web/src/shell/castData.js and
passes the character as JSON, so colours come from the same definition the
menus use:

    node tools/assets/blender/build-character.mjs tuff

Why the parts are joined into the body mesh rather than kept as separate
bone-parented objects (build-cast.py's approach): every separate mesh and
material is another draw call, and the approved budget is <= 6 per character
including the outline. Every part is weighted 100% to one bone, the same
armature-deform mechanism the body already uses, so nothing floats off when a
clip plays.

Blender world is Z-up; the character faces -Y; +X is screen-left of the face.
"""
import bpy
import bmesh
import json
import math
import mathutils
import os
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..')
SRC = os.path.abspath(os.path.join(ROOT, 'web', 'src', 'assets', 'chars', 'ybot.glb'))
ROLES = ['body', 'trim', 'skin', 'accent', 'eye']
HEAD = 'mixamorigHead'

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
if len(args) < 2:
    raise SystemExit('usage: run-blender.mjs build-character.py -- <json def> <out.glb>')
DEF = json.loads(args[0])
OUT = os.path.abspath(args[1])

# Silhouette scale per build, identical to build-cast.py (the wide values are
# the ones picked in the TUFF bake-off).
SILHOUETTE = {
    'round': (1.0, 1.0, 1.0),
    'wide': (1.35, 0.85, 1.15),
    'small': (0.44 / 0.60, 1.05 / 1.58, 0.36 / 0.48),
    'tall': (0.40 / 0.60, 1.98 / 1.58, 0.36 / 0.48),
}


# ------------------------------------------------------------------ helpers

def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def make_material(name, hexval):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    r, g, b = ((hexval >> 16) & 255) / 255, ((hexval >> 8) & 255) / 255, (hexval & 255) / 255
    bsdf.inputs['Base Color'].default_value = (srgb_to_linear(r), srgb_to_linear(g), srgb_to_linear(b), 1.0)
    bsdf.inputs['Roughness'].default_value = 0.6
    return mat


def dominant_groups(obj):
    names = {g.index: g.name for g in obj.vertex_groups}
    dom = [None] * len(obj.data.vertices)
    for v in obj.data.vertices:
        if v.groups:
            dom[v.index] = names[max(v.groups, key=lambda g: g.weight).group]
    return dom


def category(group):
    if group is None:
        return 'body'
    if group in (HEAD, 'mixamorigHeadTop_End'):
        return 'head'
    if 'Hand' in group:
        return 'skin'
    if 'Foot' in group or 'ToeBase' in group:
        return 'trim'
    return 'body'


def extents(points):
    mn = mathutils.Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
    mx = mathutils.Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
    return mn, mx


# ---------------------------------------------------------------- geometry

def rounded_box(size, radius, segments=3):
    def build(bm):
        bmesh.ops.create_cube(bm, size=1.0)
        for v in bm.verts:
            v.co.x *= size[0]
            v.co.y *= size[1]
            v.co.z *= size[2]
        r = min(radius, min(size) * 0.49)
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=r, segments=segments, profile=0.5, affect='EDGES', clamp_overlap=True)
    return build


def sphere(radius, u=14, v=10):
    def build(bm):
        bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=radius)
    return build


def cone(r1, r2, depth, segments=12):
    def build(bm):
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments, radius1=r1, radius2=r2, depth=depth)
    return build


def aim(direction):
    """Rotation taking local +Z (cone/cylinder axis) onto `direction`."""
    return mathutils.Vector((0, 0, 1)).rotation_difference(mathutils.Vector(direction).normalized()).to_euler()


class Builder:
    def __init__(self, mats):
        self.mats = mats
        self.parts = []

    def part(self, name, build, role, bone, loc, rot=(0, 0, 0), scale=(1, 1, 1), tags=()):
        """A mesh baked into world space, weighted 100% to `bone`, tagged with
        vertex groups the shape-key pass reads (removed before export)."""
        mesh = bpy.data.meshes.new(name)
        bm = bmesh.new()
        build(bm)
        m = (mathutils.Matrix.Translation(loc)
             @ mathutils.Euler(rot).to_matrix().to_4x4()
             @ mathutils.Matrix.Diagonal((*scale, 1)))
        bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(mesh)
        bm.free()
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.data.materials.append(self.mats[role])
        allv = range(len(mesh.vertices))
        obj.vertex_groups.new(name=bone).add(allv, 1.0, 'REPLACE')
        for t in tags:
            obj.vertex_groups.new(name=t).add(allv, 1.0, 'REPLACE')
        self.parts.append(obj)
        return obj


# ------------------------------------------------------------------ build

def recolour_and_split(obj, mats, joints=False):
    """Five role slots; faces take a role from their vertices' dominant bone.
    The mannequin head is deleted — the head shell replaces it."""
    dom = dominant_groups(obj)
    obj.data.materials.clear()
    for r in ROLES:
        obj.data.materials.append(mats[r])
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    doomed = []
    for f in bm.faces:
        votes = {}
        for v in f.verts:
            c = category(dom[v.index])
            votes[c] = votes.get(c, 0) + 1
        cat = max(votes, key=votes.get)
        if cat == 'head':
            doomed.append(f)
            continue
        role = 'trim' if joints and cat == 'body' else ('body' if cat == 'head' else cat)
        f.material_index = ROLES.index(role)
    bmesh.ops.delete(bm, geom=doomed, context='FACES')
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def measure(body):
    """World-space head and torso extents of the (scaled) mannequin."""
    dom = dominant_groups(body)
    mw = body.matrix_world
    head, torso = [], []
    for v in body.data.vertices:
        g = dom[v.index]
        p = mw @ v.co
        if category(g) == 'head':
            head.append(p)
        elif g in ('mixamorigHips', 'mixamorigSpine', 'mixamorigSpine1', 'mixamorigSpine2'):
            torso.append(p)
    return extents(head), extents(torso)


def bone_origin(arm, name):
    return (arm.matrix_world @ arm.data.bones[name].matrix_local).translation.copy()


def build_block(b, arm, head_ext, torso_ext, face):
    """TUFF: rounded-cube head, cream horns, underbite; block torso with a chest
    plate and wrist guards."""
    (hmn, hmx), (tmn, tmx) = head_ext, torso_ext
    hy = (hmn.y + hmx.y) / 2
    W, D, H = 0.62, 0.54, 0.58
    cz = hmn.z - 0.03 + H / 2
    b.part('head_shell', rounded_box((W, D, H), 0.12, 4), 'body', HEAD, (0, hy, cz))
    front = hy - D / 2
    face_parts(b, front, cz, W, H, face)

    top = cz + H / 2
    for s in (-1, 1):
        d = mathutils.Vector((s * 0.55, 0, 1)).normalized()
        base = mathutils.Vector((s * 0.22, hy, top - 0.04))
        b.part(f'horn_{s}', cone(0.065, 0.0, 0.26), 'skin', HEAD, base + d * 0.13, rot=aim(d))

    cy = (tmn.y + tmx.y) / 2
    TW, TD, TH = 0.60, 0.38, 0.64
    tz = 1.08 + TH / 2
    b.part('torso_shell', rounded_box((TW, TD, TH), 0.08, 3), 'body', 'mixamorigSpine1', (0, cy, tz))
    b.part('chest_plate', rounded_box((0.34, 0.06, 0.30), 0.03, 2), 'accent', 'mixamorigSpine1',
           (0, cy - TD / 2 - 0.02, tz + 0.08))

    for side, bone in ((1, 'mixamorigLeftForeArm'), (-1, 'mixamorigRightForeArm')):
        fore = bone_origin(arm, bone)
        hand = bone_origin(arm, bone.replace('ForeArm', 'Hand'))
        c = fore.lerp(hand, 0.78)
        b.part(f'wrist_{side}', cone(0.075, 0.075, 0.13, 14), 'accent', bone, c, rot=aim(hand - fore))


def face_parts(b, front, cz, W, H, face):
    """Eyes, pupils, brows, mouth on the front of a head shell. `face` carries
    the character's idle defaults from the sheet (brow tilt, mouth style)."""
    ex = W * 0.23
    ez = cz + H * 0.07
    for s, tag in ((1, 'fx_eyeL'), (-1, 'fx_eyeR')):
        b.part(f'eye_white_{s}', sphere(0.078), 'skin', HEAD, (s * ex, front + 0.012, ez), scale=(1, 0.5, 1.15), tags=('fx_eyes', tag))
        b.part(f'pupil_{s}', sphere(0.038, 10, 8), 'eye', HEAD, (s * ex, front - 0.03, ez - 0.005), scale=(1, 0.5, 1.1), tags=('fx_eyes', tag))
        tilt = face.get('browTilt', 0.0) * s
        b.part(f'brow_{s}', rounded_box((0.13, 0.035, 0.032), 0.012, 2), 'eye', HEAD,
               (s * ex, front - 0.012, ez + 0.115), rot=(0, tilt, 0), tags=('fx_brow' + ('L' if s > 0 else 'R'),))
    mz = cz - H * 0.26
    b.part('mouth', rounded_box((0.21, 0.03, 0.024), 0.01, 2), 'eye', HEAD, (0, front - 0.006, mz), tags=('fx_mouth',))
    if face.get('mouth') == 'underbite':
        for s in (-1, 1):
            b.part(f'tooth_{s}', cone(0.022, 0.0, 0.05, 8), 'skin', HEAD, (s * 0.055, front - 0.018, mz + 0.03), tags=('fx_teeth',))


SHAPE_BUILDERS = {
    'block': build_block,
}


def add_shape_keys(obj):
    """Face morph targets from the tagged vertex groups. Deltas are authored in
    world units and converted into the mesh's own (scaled) local space."""
    names = {g.index: g.name for g in obj.vertex_groups}
    tagged = {}
    for v in obj.data.vertices:
        for g in v.groups:
            n = names[g.group]
            if n.startswith('fx_') and g.weight > 0.5:
                tagged.setdefault(n, []).append(v.index)
    mw = obj.matrix_world
    inv = mw.inverted()
    world = [mw @ v.co for v in obj.data.vertices]

    obj.shape_key_add(name='Basis', from_mix=False)

    def key(name, move):
        sk = obj.shape_key_add(name=name, from_mix=False)
        for idx, p in move.items():
            sk.data[idx].co = inv @ p

    def centre(ids):
        pts = [world[i] for i in ids]
        return sum(pts, mathutils.Vector()) / len(pts)

    mouth = tagged.get('fx_mouth', [])
    teeth = tagged.get('fx_teeth', [])
    if mouth:
        mc = centre(mouth)
        half = max(abs(world[i].x - mc.x) for i in mouth) or 1
        open_ = {i: mathutils.Vector((mc.x + (world[i].x - mc.x) * 0.8, world[i].y, mc.z + (world[i].z - mc.z) * 5.0 - 0.02)) for i in mouth}
        open_.update({i: world[i] + mathutils.Vector((0, 0, -0.06)) for i in teeth})
        key('mouthOpen', open_)
        key('smile', {i: world[i] + mathutils.Vector((0, 0, 0.075 * ((world[i].x - mc.x) / half) ** 2)) for i in mouth})
        key('frown', {i: world[i] + mathutils.Vector((0, 0, -0.075 * ((world[i].x - mc.x) / half) ** 2)) for i in mouth})

    lids = {}
    for tag in ('fx_eyeL', 'fx_eyeR'):
        ids = tagged.get(tag, [])
        if not ids:
            continue
        top = max(world[i].z for i in ids)
        for i in ids:
            p = world[i]
            lids[i] = mathutils.Vector((p.x, p.y, top - (top - p.z) * 0.35))
    if lids:
        key('lidsDown', lids)

    brows = tagged.get('fx_browL', []) + tagged.get('fx_browR', [])
    if brows:
        key('browsUp', {i: world[i] + mathutils.Vector((0, 0, 0.05)) for i in brows})
        pinch = {}
        for tag in ('fx_browL', 'fx_browR'):
            ids = tagged.get(tag, [])
            if not ids:
                continue
            xs = [world[i].x for i in ids]
            inner = min(xs, key=abs)
            outer = max(xs, key=abs)
            span = abs(outer - inner) or 1
            for i in ids:
                t = 1 - abs(world[i].x - inner) / span        # 1 at the inner end
                pinch[i] = world[i] + mathutils.Vector((0, 0, -0.04 * t))
        key('browsPinch', pinch)

    for g in [g for g in obj.vertex_groups if g.name.startswith('fx_')]:
        obj.vertex_groups.remove(g)


def build():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.gltf(filepath=SRC)

    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    root = arm.parent if arm.parent else arm
    root.scale = tuple(a * b for a, b in zip(root.scale, SILHOUETTE[DEF['build']]))
    bpy.context.view_layer.update()

    for m in list(bpy.data.materials):
        m.name = '_src_' + m.name
    mats = {r: make_material(r, int(DEF['roles'][r])) for r in ROLES}

    body = bpy.data.objects['body']
    joints = bpy.data.objects['joints']
    head_ext, torso_ext = measure(body)
    recolour_and_split(body, mats)
    recolour_and_split(joints, mats, joints=True)

    for o in list(bpy.data.objects):
        if o.type == 'MESH' and o.name.startswith('Icosphere'):
            bpy.data.objects.remove(o, do_unlink=True)

    b = Builder(mats)
    builder = SHAPE_BUILDERS.get(DEF['shape'])
    if builder is None:
        raise SystemExit(f"no designed build for shape '{DEF['shape']}' yet")
    builder(b, arm, head_ext, torso_ext, DEF.get('face', {}))

    bpy.ops.object.select_all(action='DESELECT')
    for o in [joints, *b.parts, body]:
        o.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    body.name = DEF['id']
    add_shape_keys(body)

    bpy.ops.object.select_all(action='DESELECT')
    for o in bpy.data.objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.gltf(
        filepath=OUT, export_format='GLB', use_selection=True,
        export_animations=True, export_skins=True, export_morph=True,
        export_morph_normal=False, export_apply=False,
    )
    tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
    print(f"BUILD_CHARACTER_OK id={DEF['id']} tris~={tris} bytes={os.path.getsize(OUT)}")


build()
