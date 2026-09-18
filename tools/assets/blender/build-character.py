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
        # Named world positions exported as empties on the head bone:
        # face_anchor (centre of the face surface) and head_top (top of the
        # head shell) — what the game hangs expressions, crowns and helmets on.
        self.anchors = {}

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
    face_parts(b, lambda x, z: front, cz, W, H, face)

    top = cz + H / 2
    b.anchors['face_anchor'] = (0, front, cz)
    b.anchors['head_top'] = (0, hy, top)
    b.anchors['head_centre'] = (0, hy, cz)
    b.anchors['head_side'] = (W / 2, hy, cz)
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


def face_parts(b, surface, cz, W, H, face):
    """Eyes, pupils, brows, mouth on the front of a head shell.

    `surface(x, z)` is the head's front surface (world y) at that point, so
    features sit on curved heads too. Feature sizes were tuned on TUFF's
    0.62-wide head and scale with `W`. `face` carries the character's idle
    defaults from the sheet: eyeSpacing, eyeSquash (half-lids), pupil size,
    browTilt, browAsym, blush, mouth style."""
    k = W / 0.62
    ex = W * face.get('eyeSpacing', 0.23)
    ez = cz + H * 0.07
    squash = face.get('eyeSquash', 1.0)
    pupil = face.get('pupil', 1.0)
    for s, tag in ((1, 'fx_eyeL'), (-1, 'fx_eyeR')):
        x = s * ex
        front = surface(x, ez)
        b.part(f'eye_white_{s}', sphere(0.078 * k), 'skin', HEAD, (x, front + 0.012 * k, ez), scale=(1, 0.5, 1.15 * squash), tags=('fx_eyes', tag))
        b.part(f'pupil_{s}', sphere(0.038 * k * pupil, 10, 8), 'eye', HEAD, (x, front - 0.03 * k, ez - 0.005 * k), scale=(1, 0.5, 1.1 * squash), tags=('fx_eyes', tag))
        if face.get('glint'):
            b.part(f'glint_{s}', sphere(0.014 * k, 8, 6), 'skin', HEAD, (x + 0.014 * k, front - 0.045 * k, ez + 0.016 * k), tags=('fx_eyes', tag))
        tilt = face.get('browTilt', 0.0) * s
        lift = face.get('browAsym', 0.0) * (1 if s > 0 else 0) * k
        bz = ez + 0.115 * k * max(squash, 0.8) + lift
        b.part(f'brow_{s}', rounded_box((0.13 * k, 0.035 * k, 0.032 * k), 0.012 * k, 2), 'eye', HEAD,
               (x, surface(x, bz) - 0.012 * k, bz), rot=(0, tilt, 0), tags=('fx_brow' + ('L' if s > 0 else 'R'),))
        if face.get('blush'):
            cx, czb = s * ex * 1.25, ez - 0.13 * k
            b.part(f'blush_{s}', sphere(0.05 * k, 10, 8), 'accent', HEAD, (cx, surface(cx, czb) + 0.004 * k, czb), scale=(1.2, 0.3, 0.55))

    mz = cz - H * 0.26
    mf = surface(0, mz)
    style = face.get('mouth', 'line')
    if style == 'underbite':
        b.part('mouth', rounded_box((0.21 * k, 0.03 * k, 0.024 * k), 0.01 * k, 2), 'eye', HEAD, (0, mf - 0.006 * k, mz), tags=('fx_mouth',))
        for s in (-1, 1):
            b.part(f'tooth_{s}', cone(0.022 * k, 0.0, 0.05 * k, 8), 'skin', HEAD, (s * 0.055 * k, mf - 0.018 * k, mz + 0.03 * k), tags=('fx_teeth',))
    elif style == 'grin':
        b.part('mouth', sphere(0.5, 16, 10), 'eye', HEAD, (0, mf + 0.01 * k, mz), scale=(0.26 * k, 0.06 * k, 0.11 * k), tags=('fx_mouth',))
    elif style == 'shout':
        b.part('mouth', sphere(0.5, 14, 10), 'eye', HEAD, (0, mf + 0.01 * k, mz - 0.01 * k), scale=(0.13 * k, 0.06 * k, 0.15 * k), tags=('fx_mouth',))
    elif style == 'smirk':
        b.part('mouth', rounded_box((0.17 * k, 0.03 * k, 0.024 * k), 0.01 * k, 2), 'eye', HEAD, (0.035 * k, mf - 0.006 * k, mz), rot=(0, -0.2, 0), tags=('fx_mouth',))
    elif style == 'beak':
        d = mathutils.Vector((0, -1, -0.25)).normalized()
        b.part('mouth', cone(0.085 * k, 0.0, 0.2 * k, 12), 'accent', HEAD, mathutils.Vector((0, mf - 0.06 * k, mz + 0.06 * k)), rot=aim(d), scale=(1, 1, 1), tags=('fx_mouth',))
    elif style == 'wavy':
        for i in range(6):
            x = (-0.125 + i * 0.05) * k
            b.part(f'mouth_{i}', sphere(0.021 * k, 8, 6), 'eye', HEAD, (x, surface(x, mz) - 0.004 * k, mz + (0.012 if i % 2 else -0.012) * k), scale=(1.3, 0.6, 0.8), tags=('fx_mouth',))
    else:  # 'smile' / 'line'
        b.part('mouth', rounded_box((0.19 * k, 0.03 * k, 0.024 * k), 0.01 * k, 2), 'eye', HEAD, (0, mf - 0.006 * k, mz), tags=('fx_mouth', 'fx_curve'))
    return k


# -------------------------------------------------------------- shape kit

def torus(R, r, major=28, minor=10):
    def build(bm):
        rings = []
        for i in range(major):
            a = 2 * math.pi * i / major
            ring = []
            for j in range(minor):
                t = 2 * math.pi * j / minor
                rr = R + r * math.cos(t)
                ring.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), r * math.sin(t))))
            rings.append(ring)
        for i in range(major):
            for j in range(minor):
                a, b2 = rings[i], rings[(i + 1) % major]
                bm.faces.new((a[j], b2[j], b2[(j + 1) % minor], a[(j + 1) % minor]))
    return build


def prism(points, depth):
    """A 2D outline in the XZ plane, extruded along Y (the face direction)."""
    def build(bm):
        verts = [bm.verts.new((x, -depth / 2, z)) for x, z in points]
        face = bm.faces.new(verts)
        geom = bmesh.ops.extrude_face_region(bm, geom=[face])
        ex = [g for g in geom['geom'] if isinstance(g, bmesh.types.BMVert)]
        bmesh.ops.translate(bm, vec=(0, depth, 0), verts=ex)
    return build


def star_points(n, r_out, r_in, rot=math.pi / 2):
    pts = []
    for i in range(n * 2):
        a = rot + math.pi * i / n
        r = r_out if i % 2 == 0 else r_in
        pts.append((r * math.cos(a), r * math.sin(a)))
    return pts


def ellipsoid_front(hy, cz, W, D, H):
    def surface(x, z):
        q = 1 - (x / (W / 2)) ** 2 - ((z - cz) / (H / 2)) ** 2
        return hy - (D / 2) * math.sqrt(max(0.08, q))
    return surface


def measured(head_ext, torso_ext):
    (hmn, hmx), (tmn, tmx) = head_ext, torso_ext
    return dict(
        hw=hmx.x - hmn.x, hy=(hmn.y + hmx.y) / 2, neck=hmn.z,
        tw=tmx.x - tmn.x, td=tmx.y - tmn.y, ty=(tmn.y + tmx.y) / 2, tbot=tmn.z, tspan=hmn.z - tmn.z,
    )


def forearm_ring(b, arm, role, radius, depth, t=0.78):
    for side, bone in ((1, 'mixamorigLeftForeArm'), (-1, 'mixamorigRightForeArm')):
        fore = bone_origin(arm, bone)
        hand = bone_origin(arm, bone.replace('ForeArm', 'Hand'))
        b.part(f'cuff_{side}', cone(radius, radius, depth, 14), role, bone, fore.lerp(hand, t), rot=aim(hand - fore))


def plume(b, m, top, k, tip_star=False):
    for i, (ang, s) in enumerate(((-0.45, 0.85), (0.0, 1.0), (0.45, 0.85))):
        d = mathutils.Vector((math.sin(ang), 0.25, math.cos(ang))).normalized()
        c = mathutils.Vector((0, m['hy'] + 0.02 * k, top - 0.03 * k)) + d * (0.13 * k * s)
        b.part(f'feather_{i}', sphere(0.5, 12, 8), 'accent', HEAD, c, rot=aim(d), scale=(0.07 * k * s, 0.07 * k * s, 0.3 * k * s))
    if tip_star:
        b.part('plume_star', prism(star_points(5, 0.07 * k, 0.03 * k), 0.03 * k), 'skin', HEAD, (0, m['hy'] + 0.05 * k, top + 0.28 * k))


def antenna(b, m, top, k, x=0.0, tilt=0.0, name='antenna'):
    d = mathutils.Vector((math.sin(tilt), 0, math.cos(tilt)))
    L = 0.22 * k
    base = mathutils.Vector((x, m['hy'], top - 0.02 * k))
    b.part(f'{name}_stick', cone(0.013 * k, 0.013 * k, L, 8), 'trim', HEAD, base + d * (L / 2), rot=aim(d))
    b.part(f'{name}_ball', sphere(0.05 * k, 12, 8), 'accent', HEAD, base + d * L)


def ellipsoid_head(b, m, Wm, Dm, Hm, drop=0.05):
    W, D, H = m['hw'] * Wm, m['hw'] * Dm, m['hw'] * Hm
    cz = m['neck'] - drop * H + H / 2
    b.part('head_shell', sphere(0.5, 24, 14), 'body', HEAD, (0, m['hy'], cz), scale=(W, D, H))
    return W, D, H, cz, ellipsoid_front(m['hy'], cz, W, D, H)


def round_torso(b, m, Wm=1.25, Hm=0.85, Dm=1.35, zc=0.45):
    W, D, H = m['tw'] * Wm, m['td'] * Dm, m['tspan'] * Hm
    z = m['tbot'] + zc * m['tspan']
    b.part('torso_shell', sphere(0.5, 20, 12), 'body', 'mixamorigSpine1', (0, m['ty'], z), scale=(W, D, H))
    return W, D, H, z


def anchors(b, surface, cz, top, m, W):
    b.anchors['face_anchor'] = (0, surface(0, cz), cz)
    b.anchors['head_top'] = (0, m['hy'], top)
    # Head centre and one side: the game measures head radius from these and
    # scales crowns, helmets and anything else it hangs on a head.
    b.anchors['head_centre'] = (0, m['hy'], cz)
    b.anchors['head_side'] = (W / 2, m['hy'], cz)


# -------------------------------------------------------- characters

def build_round(b, arm, head_ext, torso_ext, face):
    """BOPP: round head, antenna with a bobbing ball, round belly with a band."""
    m = measured(head_ext, torso_ext)
    W, D, H, cz, surf = ellipsoid_head(b, m, 3.1, 2.8, 3.0)
    k = face_parts(b, surf, cz, W, H, face)
    top = cz + H / 2
    anchors(b, surf, cz, top, m, W)
    antenna(b, m, top, k)
    TW, TD, TH, tz = round_torso(b, m)
    band_z = tz - TH * 0.08
    b.part('belly_band', torus(0.5, 0.045, 32, 8), 'accent', 'mixamorigSpine1', (0, m['ty'], band_z), scale=(TW * 1.0, TD * 1.0, 1.0))


def build_spike(b, arm, head_ext, torso_ext, face):
    """ZIZZ: hexagon head with a lightning-bolt crest; hex chest with shoulder
    points, a cropped jacket and a bolt on the chest."""
    m = measured(head_ext, torso_ext)
    W, D, H = m['hw'] * 2.95, m['hw'] * 2.4, m['hw'] * 2.95
    cz = m['neck'] - 0.04 * H + H / 2
    hex_pts = star_points(3, 0.5, 0.5, rot=math.pi / 2)   # six equal points = hexagon, vertex up
    b.part('head_shell', prism(hex_pts, 1.0), 'body', HEAD, (0, m['hy'], cz), scale=(W, D, H))
    front = m['hy'] - D / 2
    surf = lambda x, z: front
    k = face_parts(b, surf, cz, W, H, face)
    top = cz + H / 2
    anchors(b, surf, cz, top, m, W)
    h = 0.26 * k
    bolt = [(0, 0), (h * 0.18, h * 0.5), (h * 0.02, h * 0.5), (h * 0.22, h), (-h * 0.2, h * 0.38), (-h * 0.02, h * 0.38), (-h * 0.14, 0)]
    b.part('bolt_crest', prism(bolt, 0.05 * k), 'accent', HEAD, (0.02 * k, m['hy'], top - 0.06 * k), rot=(0, -0.2, 0))

    TW, TD, TH = m['tw'] * 1.3, m['td'] * 1.3, m['tspan'] * 0.82
    tz = m['tbot'] + 0.46 * m['tspan']
    b.part('torso_shell', prism(star_points(3, 0.5, 0.5, rot=0), 1.0), 'body', 'mixamorigSpine1', (0, m['ty'], tz), rot=(math.pi / 2, 0, 0), scale=(TW, TH, TD))
    for side, bone in ((1, 'mixamorigLeftArm'), (-1, 'mixamorigRightArm')):
        o = bone_origin(arm, bone)
        d = mathutils.Vector((side, 0, 0.55)).normalized()
        b.part(f'shoulder_spike_{side}', cone(0.06 * k, 0.0, 0.16 * k, 6), 'body', 'mixamorigSpine2', o + d * 0.05 * k, rot=aim(d))
    jf = m['ty'] - TD / 2
    for side in (-1, 1):
        b.part(f'jacket_{side}', rounded_box((TW * 0.3, 0.05 * k, TH * 0.62), 0.02 * k, 2), 'trim', 'mixamorigSpine1', (side * TW * 0.3, jf - 0.01 * k, tz + TH * 0.08))
    hb = 0.16 * k
    chest_bolt = [(0, 0), (hb * 0.18, hb * 0.5), (hb * 0.02, hb * 0.5), (hb * 0.22, hb), (-hb * 0.2, hb * 0.38), (-hb * 0.02, hb * 0.38), (-hb * 0.14, 0)]
    b.part('chest_bolt', prism(chest_bolt, 0.03 * k), 'accent', 'mixamorigSpine1', (0.01 * k, jf - 0.03 * k, tz + TH * 0.02))


def build_beak(b, arm, head_ext, torso_ext, face):
    """KWARK: round head, orange beak, three-feather plume; round body, scarf."""
    m = measured(head_ext, torso_ext)
    W, D, H, cz, surf = ellipsoid_head(b, m, 3.0, 2.8, 2.9)
    k = face_parts(b, surf, cz, W, H, face)
    top = cz + H / 2
    anchors(b, surf, cz, top, m, W)
    plume(b, m, top, k)
    TW, TD, TH, tz = round_torso(b, m)
    neck_z = m['neck'] - 0.02 * k
    b.part('scarf', torus(0.5, 0.055, 28, 10), 'accent', 'mixamorigSpine2', (0, m['ty'], neck_z), scale=(TW * 0.62, TD * 0.75, 1.0))
    b.part('scarf_tail', rounded_box((0.07 * k, 0.04 * k, 0.24 * k), 0.02 * k, 2), 'accent', 'mixamorigSpine2',
           (0.09 * k, m['ty'] - TD * 0.36, neck_z - 0.12 * k), rot=(0, -0.25, 0))


def build_tall(b, arm, head_ext, torso_ext, face):
    """MIMO: tall capsule head with a pink cap; long torso, hoodie cuffs."""
    m = measured(head_ext, torso_ext)
    W, D, H = m['hw'] * 2.9, m['hw'] * 2.7, m['hw'] * 4.0
    cz = m['neck'] - 0.03 * H + H / 2
    b.part('head_shell', rounded_box((W, D, H), min(W, D) * 0.45, 5), 'body', HEAD, (0, m['hy'], cz))
    front = m['hy'] - D / 2
    surf = lambda x, z: front
    k = face_parts(b, surf, cz, W, H * 0.8, face)
    top = cz + H / 2
    anchors(b, surf, cz, top, m, W)
    b.part('cap_dome', sphere(0.5, 24, 12), 'accent', HEAD, (0, m['hy'], top - H * 0.12), scale=(W * 1.04, D * 1.04, H * 0.34))
    b.part('cap_brim', rounded_box((W * 0.78, D * 0.55, 0.035 * k), 0.015 * k, 2), 'accent', HEAD, (0, m['hy'] - D * 0.5, top - H * 0.2), rot=(-0.08, 0, 0))
    TW, TD, TH = m['tw'] * 1.2, m['td'] * 1.25, m['tspan'] * 0.86
    tz = m['tbot'] + 0.46 * m['tspan']
    b.part('torso_shell', rounded_box((TW, TD, TH), min(TW, TD) * 0.45, 4), 'body', 'mixamorigSpine1', (0, m['ty'], tz))
    forearm_ring(b, arm, 'trim', 0.07 * k, 0.12 * k)


def build_tiny(b, arm, head_ext, torso_ext, face):
    """NIBB: oversized round head, twin antennae; tiny body, whistle on a lanyard."""
    m = measured(head_ext, torso_ext)
    W, D, H, cz, surf = ellipsoid_head(b, m, 3.8, 3.3, 3.6, drop=0.08)
    k = face_parts(b, surf, cz, W, H, face)
    top = cz + H / 2
    anchors(b, surf, cz, top, m, W)
    for side in (-1, 1):
        antenna(b, m, top, k, x=side * W * 0.18, tilt=side * 0.38, name=f'antenna_{side}')
    TW, TD, TH, tz = round_torso(b, m, Wm=1.2, Hm=0.8)
    chest = mathutils.Vector((0, m['ty'] - TD / 2 - 0.01 * k, tz + TH * 0.05))
    for side in (-1, 1):
        start = mathutils.Vector((side * TW * 0.3, m['ty'] - TD * 0.3, tz + TH * 0.45))
        d = chest - start
        b.part(f'lanyard_{side}', cone(0.01 * k, 0.01 * k, d.length, 6), 'trim', 'mixamorigSpine1', start + d * 0.5, rot=aim(d))
    b.part('whistle', cone(0.03 * k, 0.03 * k, 0.08 * k, 10), 'accent', 'mixamorigSpine1', chest + mathutils.Vector((0, -0.01 * k, -0.03 * k)), rot=aim((1, 0, 0)))


def build_blob(b, arm, head_ext, torso_ext, face):
    """GLUB: wide blob head sunk into a drip-shaped body, dorsal fin, bubble collar."""
    m = measured(head_ext, torso_ext)
    W, D, H, cz, surf = ellipsoid_head(b, m, 3.4, 2.9, 2.6, drop=0.3)
    k = face_parts(b, surf, cz, W, H, face)
    top = cz + H / 2
    anchors(b, surf, cz, top, m, W)
    fd = mathutils.Vector((0, 0.35, 1)).normalized()
    b.part('fin', cone(0.15 * k, 0.0, 0.26 * k, 3), 'accent', HEAD, mathutils.Vector((0, m['hy'] + D * 0.18, top - 0.02 * k)) + fd * 0.1 * k, rot=aim(fd), scale=(0.25, 1, 1))
    TW, TD, TH, tz = round_torso(b, m, Wm=1.4, Hm=0.95, Dm=1.45, zc=0.42)
    ring_z = cz - H / 2 + 0.02 * k      # where the sunken head meets the body
    for i in range(9):
        a = 2 * math.pi * i / 9
        p = (math.cos(a) * TW * 0.42, m['ty'] + math.sin(a) * TD * 0.46, ring_z + 0.02 * k)
        b.part(f'bubble_{i}', sphere(0.05 * k, 8, 6), 'skin', 'mixamorigSpine2', p)


def build_star(b, arm, head_ext, torso_ext, face):
    """FIZZ: six-point star head, star-tipped plume; small body, star cape, blush."""
    m = measured(head_ext, torso_ext)
    W, D, H = m['hw'] * 3.5, m['hw'] * 2.3, m['hw'] * 3.5
    cz = m['neck'] - 0.08 * H + H / 2
    b.part('head_shell', prism(star_points(6, 0.5, 0.36), 1.0), 'body', HEAD, (0, m['hy'], cz), scale=(W, D, H))
    front = m['hy'] - D / 2
    surf = lambda x, z: front
    k = face_parts(b, surf, cz, W * 0.82, H * 0.8, face)
    top = cz + H / 2
    anchors(b, surf, cz, top, m, W)
    plume(b, m, top - H * 0.04, k, tip_star=True)
    TW, TD, TH, tz = round_torso(b, m, Wm=1.2, Hm=0.8)
    b.part('star_cape', prism(star_points(6, 0.5, 0.3), 1.0), 'accent', 'mixamorigSpine2',
           (0, m['ty'] + TD * 0.55, tz + TH * 0.1), scale=(TW * 1.9, 0.03 * k, TH * 1.7))


SHAPE_BUILDERS = {
    'block': build_block,
    'round': build_round,
    'spike': build_spike,
    'beak': build_beak,
    'tall': build_tall,
    'tiny': build_tiny,
    'blob': build_blob,
    'star': build_star,
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

    # A resting smile, baked into the base mesh before any key exists.
    curve = tagged.get('fx_curve', [])
    if curve:
        cc = sum((world[i] for i in curve), mathutils.Vector()) / len(curve)
        ch = max(abs(world[i].x - cc.x) for i in curve) or 1
        for i in curve:
            world[i] = world[i] + mathutils.Vector((0, 0, 0.03 * (ch / 0.105) * ((world[i].x - cc.x) / ch) ** 2))
            obj.data.vertices[i].co = inv @ world[i]

    # Feature scale relative to TUFF's (mouth half-width 0.105, brow 0.13),
    # which the key amplitudes below were tuned on.
    mouth_half = 0.105
    if tagged.get('fx_mouth'):
        ids = tagged['fx_mouth']
        mcx = sum(world[i].x for i in ids) / len(ids)
        mouth_half = max(abs(world[i].x - mcx) for i in ids) or 0.105
    kk = mouth_half / 0.105

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
        height = (max(world[i].z for i in mouth) - min(world[i].z for i in mouth)) or 0.024
        # A thin line opens 5x; an already-open grin or shout opens less.
        stretch = max(1.3, min(5.0, 0.12 * kk / height))
        open_ = {i: mathutils.Vector((mc.x + (world[i].x - mc.x) * 0.8, world[i].y, mc.z + (world[i].z - mc.z) * stretch - 0.02 * kk)) for i in mouth}
        open_.update({i: world[i] + mathutils.Vector((0, 0, -0.06 * kk)) for i in teeth})
        key('mouthOpen', open_)
        key('smile', {i: world[i] + mathutils.Vector((0, 0, 0.075 * kk * ((world[i].x - mc.x) / half) ** 2)) for i in mouth})
        key('frown', {i: world[i] + mathutils.Vector((0, 0, -0.075 * kk * ((world[i].x - mc.x) / half) ** 2)) for i in mouth})

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
        key('browsUp', {i: world[i] + mathutils.Vector((0, 0, 0.05 * kk)) for i in brows})
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
                pinch[i] = world[i] + mathutils.Vector((0, 0, -0.04 * kk * t))
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

    for name, loc in b.anchors.items():
        empty = bpy.data.objects.new(name, None)
        bpy.context.collection.objects.link(empty)
        empty.parent = arm
        empty.parent_type = 'BONE'
        empty.parent_bone = HEAD
        bpy.context.view_layer.update()
        empty.matrix_world = mathutils.Matrix.Translation(loc)

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
