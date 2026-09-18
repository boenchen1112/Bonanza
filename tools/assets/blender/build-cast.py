"""
Generalizes build-tuff.py: builds all 8 web/src/shell/chars.js CHARS entries
from the shared ybot.glb skeleton, instead of just TUFF. Same approach as the
bake-off-winning TUFF build - object-level non-uniform scale for silhouette
(per-bone pose-mode scale + armature_apply is not used: it corrupted skinning
during the TUFF bake-off), material recolor, and a bone-parented crest mesh -
generalized across the 4 rig builds (round/tall/small/wide) and 6 crest types
(antenna/bolt/plume/horns/cap/fin) instead of hard-coded to one character.

Crest placement below is a first-pass approximation (anchored to head width/
height, not a literal transcription of shell/chars.js's joint-local
coordinates - those are in the toy rig's own joint hierarchy, which has no
1:1 mapping to a Mixamo bone frame). It is meant to be checked against
renders before anything is wired into the live game, per the ticket's
contact-sheet gate - not assumed correct from the numbers alone.

    node tools/assets/blender/run-blender.mjs tools/assets/blender/build-cast.py

Writes tools/assets/blender/_cast-<id>.glb for each of the 8 characters
(gitignored scratch output; promoted to web/src/assets/chars/ only after the
checkpoint ticket's human review, per the ticket sequencing).
"""
import bpy
import bmesh
import math
import mathutils
import os
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..')
SRC = os.path.abspath(os.path.join(ROOT, 'web', 'src', 'assets', 'chars', 'ybot.glb'))
OUT_DIR = os.path.dirname(os.path.abspath(__file__))
HEAD_BONE = 'mixamorigHead'

# One character per Blender process, matching build-tuff.py's proven single-
# run shape exactly (fresh factory-startup Blender, one import, one export) -
# looping all 8 characters inside one long-lived session instead left stale
# transform/selection state between builds (bone-parented crests floating
# off the head on later characters in the loop, not reproducible when each
# character gets its own process). Driven by build-cast-all.mjs.
CHAR_ID = None
if '--' in sys.argv:
    args = sys.argv[sys.argv.index('--') + 1:]
    if args:
        CHAR_ID = args[0]


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_to_linear_rgba(h):
    r, g, b = ((h >> 16) & 255) / 255, ((h >> 8) & 255) / 255, (h & 255) / 255
    return (srgb_to_linear(r), srgb_to_linear(g), srgb_to_linear(b), 1.0)


def make_material(name, hexval, roughness=0.5):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value = hex_to_linear_rgba(hexval)
    bsdf.inputs['Roughness'].default_value = roughness
    return mat


# ------------------------------------------------------------------ builds
# Mirrors web/src/chars/rig.js BUILDS. total_h/head-to-height ratios are
# straight from that file's own header comment (heights 1.05/1.35/1.58/1.98,
# ratios 0.40/0.27/0.27/0.18 for small/wide/round/tall in that order).
BUILD_PARAMS = {
    'small': dict(head_w=0.46, head_h=0.44, torso_w=0.44, torso_h=0.30, torso_d=0.36, bobbleY=0.30, total_h=1.05),
    'wide':  dict(head_w=0.46, head_h=0.34, torso_w=0.86, torso_h=0.46, torso_d=0.54, bobbleY=0.22, total_h=1.35),
    'round': dict(head_w=0.48, head_h=0.44, torso_w=0.60, torso_h=0.48, torso_d=0.48, bobbleY=0.29, total_h=1.58),
    'tall':  dict(head_w=0.34, head_h=0.40, torso_w=0.40, torso_h=0.58, torso_d=0.36, bobbleY=0.34, total_h=1.98),
}
REF = BUILD_PARAMS['round']  # 'the default' per rig.js -> baseline scale (1,1,1)


def silhouette_scale(build):
    if build == 'wide':
        # Exact values already proven in the TUFF bake-off (reviewed, picked
        # by the user) - kept as-is rather than recomputed from the formula
        # below, which lands close (1.43/0.85/1.13) but wasn't the build the
        # user actually looked at.
        return (1.35, 0.85, 1.15)
    p = BUILD_PARAMS[build]
    return (p['torso_w'] / REF['torso_w'], p['total_h'] / REF['total_h'], p['torso_d'] / REF['torso_d'])


# -------------------------------------------------------------- characters
# Mirrors web/src/shell/chars.js CHARS exactly (id, color, accent, build via
# BUILD_BY_SHAPE, crest) - same identity, not a different character.
CHAR_DEFS = [
    dict(id='bopp',  color=0xffd93d, accent=0xff9f45, build='round', crest='antenna'),
    dict(id='zizz',  color=0x4dd6ff, accent=0x7aa6ff, build='wide',  crest='bolt'),
    dict(id='kwark', color=0xff5d73, accent=0xff9f45, build='round', crest='plume'),
    dict(id='tuff',  color=0x9ee87a, accent=0x39d4b4, build='wide',  crest='horns'),
    dict(id='mimo',  color=0xc08cff, accent=0xff7ad9, build='tall',  crest='cap'),
    dict(id='nibb',  color=0xff9f45, accent=0xffd93d, build='small', crest='antenna'),
    dict(id='glub',  color=0x39d4b4, accent=0x4dd6ff, build='round', crest='fin'),
    dict(id='fizz',  color=0xff7ad9, accent=0xc08cff, build='small', crest='plume'),
]


def head_rest_frame(armature):
    """The head bone's REST-pose world transform, decomposed into an origin
    (the bone's own origin, at its base) plus a right/up/forward basis - 'up'
    being the direction the bone points from base to tip. Reading this from
    bone.matrix_local (edit/rest data, independent of whatever frame the
    imported action happens to be sitting on) rather than the animated
    pose.bones transform means crest placement doesn't depend on which pose
    the clip happened to be in at script time. Composing with
    armature.matrix_world folds in this build's silhouette scale and the
    glTF importer's Y-up -> Z-up root rotation, so right/up/forward are
    already correct Blender-world-space directions - no separate bone-local
    axis convention to get right by hand (get it wrong once already, see the
    git history: a first attempt used BONE parent_type, which the TUFF
    bake-off's own approved asset turns out to have gotten wrong too - both
    horns floated off the head as soon as the swing pose moved. Vertex-group
    armature-deform, the same mechanism 'body'/'joints' already use
    correctly, doesn't have that ambiguity.).

    Also returns head_len, the bone's REAL world-space length (~0.1 for this
    skeleton) - NOT the same unit system as the toy rig's hw/hh design
    numbers (0.3-0.5), which are meters in the toy rig's own separately-
    built geometry. Multiplying a normalized direction by raw hw/hh (as an
    early version of this file did) overshoots 3-4x past the actual head -
    fine for a wide sideways spread like horns (which still visually reads
    as "near the head"), obviously wrong for anything anchored mostly along
    'up' with little spread, like a single antenna. Every crest below scales
    its offsets by head_len, using hw/hh only for the relative proportions
    between sub-parts (spread vs. height vs. radius), not as absolute
    world-space lengths."""
    bone = armature.data.bones[HEAD_BONE]
    head_world = armature.matrix_world @ bone.matrix_local
    m3 = head_world.to_3x3()
    origin = head_world.translation
    right = (m3 @ mathutils.Vector((1, 0, 0))).normalized()
    up = (m3 @ mathutils.Vector((0, 1, 0))).normalized()
    fwd = (m3 @ mathutils.Vector((0, 0, 1))).normalized()
    tip_world = head_world @ mathutils.Vector((0, bone.length, 0))
    head_len = (tip_world - origin).length
    return origin, right, up, fwd, head_len


def measure_crown(armature, origin, up, head_len):
    """The head mesh's own real top, measured directly from 'body' rather
    than guessed as a multiple of head_len: a first attempt used a single
    hand-tuned multiplier (calibrated against the round build alone) and it
    landed inside the head for round/small, and floating well clear of the
    head entirely for tall - the same non-uniform build scale that gives
    each build its silhouette also changes how far the visual head extends
    past the head bone's own rest length, by a different amount per build.
    Measuring it removes the need to re-tune this by hand for every build.

    Takes the highest point of 'body' within a generous horizontal radius of
    the head bone's origin, along 'up' - restricting to a radius (rather
    than the whole mesh) so an outstretched arm/hand elsewhere in whatever
    pose frame 1 happens to show can't be mistaken for the head."""
    body = bpy.data.objects.get('body')
    if body is None:
        return origin + up * (head_len * 2.2)
    radius = head_len * 2.0
    best_h = 0.0
    for v in body.data.vertices:
        rel = (body.matrix_world @ v.co) - origin
        h = rel.dot(up)
        if h <= best_h:
            continue
        horiz = (rel - up * h).length
        if horiz < radius:
            best_h = h
    return origin + up * best_h


def look_rotation(z_axis, up_hint):
    """A rotation matrix whose local Z points along z_axis - Blender's mesh
    primitives (cone/cylinder) extrude along local Z by default, so this is
    what lets a primitive's own axis point in an arbitrary world direction
    without hand-deriving Euler angles per crest."""
    z = z_axis.normalized()
    x = up_hint.cross(z)
    if x.length < 1e-6:
        x = mathutils.Vector((1, 0, 0)).cross(z)
    x = x.normalized()
    y = z.cross(x).normalized()
    m = mathutils.Matrix((x, y, z)).transposed().to_4x4()
    return m.to_euler()


def rigid_bind_to_bone(obj, armature, bone_name):
    """Attach obj to bone_name via armature-deform (vertex group weight 1.0
    + an Armature modifier), not parent_type='BONE' - see head_rest_frame's
    docstring for why. obj.location/rotation_euler must already be the
    desired WORLD-space transform (this function does not move it)."""
    vg = obj.vertex_groups.new(name=bone_name)
    vg.add(range(len(obj.data.vertices)), 1.0, 'REPLACE')
    obj.parent = armature
    obj.matrix_parent_inverse = armature.matrix_world.inverted()
    mod = obj.modifiers.new(name='CrestDeform', type='ARMATURE')
    mod.object = armature


# ---------------------------------------------------------------- crests
# hw/hh = head width/height for this build (three.js-authored proportions,
# used only for relative scale/spread); placement itself is anchored to the
# head bone's actual rest-world position/orientation via head_rest_frame,
# not a guessed local coordinate frame.

def crest_horns(hw, hh, armature, accent_hex, dark_hex):
    mat = make_material('horn', 0xfff1d6, 0.35)
    origin, right, up, fwd, hs = head_rest_frame(armature)
    crown = measure_crown(armature, origin, up, hs) + fwd * (hs * 0.08)
    r, h = hs * 0.1, hs * 0.34
    for side in (-1, 1):
        bpy.ops.mesh.primitive_cone_add(radius1=r, radius2=0.0, depth=h, location=(0, 0, 0))
        obj = bpy.context.active_object
        obj.name = f'crest_horn_{"L" if side < 0 else "R"}'
        obj.data.materials.append(mat)
        tip_dir = (up + right * (side * 0.55)).normalized()
        obj.rotation_euler = look_rotation(tip_dir, up)
        obj.location = crown + right * (side * hs * 0.34) + tip_dir * (h * 0.5)
        rigid_bind_to_bone(obj, armature, HEAD_BONE)


def crest_antenna(hw, hh, bobbleY, armature, accent_hex, dark_hex):
    mat = make_material('antenna', dark_hex, 0.4)
    origin, right, up, fwd, hs = head_rest_frame(armature)
    crown = measure_crown(armature, origin, up, hs) + fwd * (hs * 0.05)
    L = hs * 0.45
    bpy.ops.mesh.primitive_cylinder_add(radius=hs * 0.04, depth=L, location=(0, 0, 0))
    stick = bpy.context.active_object
    stick.name = 'crest_antenna_stick'
    stick.data.materials.append(mat)
    stick.rotation_euler = look_rotation(up, fwd)
    stick.location = crown + up * (L / 2)
    rigid_bind_to_bone(stick, armature, HEAD_BONE)

    bpy.ops.mesh.primitive_uv_sphere_add(radius=hs * 0.09, location=(0, 0, 0))
    tip = bpy.context.active_object
    tip.name = 'crest_antenna_tip'
    tip.data.materials.append(mat)
    tip.location = crown + up * L
    rigid_bind_to_bone(tip, armature, HEAD_BONE)


def crest_plume(hw, hh, bobbleY, armature, accent_hex, dark_hex):
    mat = make_material('plume', accent_hex, 0.4)
    origin, right, up, fwd, hs = head_rest_frame(armature)
    crown = measure_crown(armature, origin, up, hs) + fwd * (hs * 0.05)
    for ang, s in ((-0.45, 0.85), (0.0, 1.0), (0.45, 0.85)):
        bpy.ops.mesh.primitive_uv_sphere_add(radius=hs * 0.13, location=(0, 0, 0))
        f = bpy.context.active_object
        f.name = f'crest_plume_{ang}'
        f.data.materials.append(mat)
        f.scale = (0.55 * s, 0.55 * s, 2.4 * s)
        # Feathers fan outward (rotate around 'up' by ang) and tilt back
        # (toward 'fwd' negated) like the original three.js plume.
        tilt = mathutils.Matrix.Rotation(ang, 3, up) @ mathutils.Matrix.Rotation(0.35, 3, right)
        f.rotation_euler = look_rotation(tilt @ up, right)
        f.location = crown + right * (math.sin(ang) * hs * 0.12) + up * (hs * 0.22 * s)
        rigid_bind_to_bone(f, armature, HEAD_BONE)


def crest_cap(hw, hh, armature, accent_hex, dark_hex):
    mat = make_material('cap', accent_hex, 0.45)
    origin, right, up, fwd, hs = head_rest_frame(armature)
    crown = measure_crown(armature, origin, up, hs)
    # Dome: a full sphere sunk halfway into the head so only the top
    # hemisphere reads above the silhouette - avoids needing a true
    # hemisphere mesh while looking equivalent from outside.
    bpy.ops.mesh.primitive_uv_sphere_add(radius=hs * 0.54, location=(0, 0, 0))
    dome = bpy.context.active_object
    dome.name = 'crest_cap_dome'
    dome.data.materials.append(mat)
    dome.location = crown - up * (hs * 0.1) + fwd * (hs * 0.02)
    rigid_bind_to_bone(dome, armature, HEAD_BONE)
    # Brim: a short flattened cylinder, thin axis vertical, projecting
    # forward from the dome's base.
    bpy.ops.mesh.primitive_cylinder_add(radius=hs * 0.42, depth=hs * 0.06, location=(0, 0, 0))
    brim = bpy.context.active_object
    brim.name = 'crest_cap_brim'
    brim.data.materials.append(mat)
    brim.rotation_euler = look_rotation(up, fwd)
    brim.location = crown - up * (hs * 0.16) + fwd * (hs * 0.32)
    rigid_bind_to_bone(brim, armature, HEAD_BONE)


def crest_fin(hw, hh, bobbleY, armature, accent_hex, dark_hex):
    mat = make_material('fin', accent_hex, 0.4)
    origin, right, up, fwd, hs = head_rest_frame(armature)
    crown = measure_crown(armature, origin, up, hs) + fwd * (hs * 0.02)
    bpy.ops.mesh.primitive_cone_add(radius1=hs * 0.34, radius2=0.0, depth=hs * 0.5, vertices=3, location=(0, 0, 0))
    fin = bpy.context.active_object
    fin.name = 'crest_fin'
    fin.data.materials.append(mat)
    fin.scale = (1.0, 0.12, 1.0)
    fin.rotation_euler = look_rotation(up, fwd)
    fin.location = crown
    rigid_bind_to_bone(fin, armature, HEAD_BONE)


def crest_bolt(hw, hh, armature, accent_hex, dark_hex):
    mat = make_material('bolt', accent_hex, 0.4)
    origin, right, up, fwd, hs = head_rest_frame(armature)
    crown = measure_crown(armature, origin, up, hs) + fwd * (hs * 0.05)
    h = hs * 0.75
    pts = [
        (0, 0), (h * 0.18, h * 0.5), (h * 0.02, h * 0.5), (h * 0.22, h),
        (-h * 0.2, h * 0.38), (-h * 0.02, h * 0.38), (-h * 0.14, 0),
    ]
    mesh = bpy.data.meshes.new('bolt_mesh')
    bm = bmesh.new()
    verts = [bm.verts.new((x, y, 0)) for x, y in pts]
    face = bm.faces.new(verts)
    geom = bmesh.ops.extrude_face_region(bm, geom=[face])
    verts_ex = [g for g in geom['geom'] if isinstance(g, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, vec=(0, 0, 0.05), verts=verts_ex)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new('crest_bolt', mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    obj.rotation_euler = look_rotation(up, fwd)
    obj.location = crown - up * (h * 0.15)
    rigid_bind_to_bone(obj, armature, HEAD_BONE)


CREST_BUILDERS = {
    'horns': lambda hw, hh, bobbleY, arm, acc, dark: crest_horns(hw, hh, arm, acc, dark),
    'antenna': crest_antenna,
    'plume': crest_plume,
    'cap': lambda hw, hh, bobbleY, arm, acc, dark: crest_cap(hw, hh, arm, acc, dark),
    'fin': crest_fin,
    'bolt': lambda hw, hh, bobbleY, arm, acc, dark: crest_bolt(hw, hh, arm, acc, dark),
}


def darken(hexval, amount=0.55):
    r, g, b = (hexval >> 16) & 255, (hexval >> 8) & 255, hexval & 255
    r = int(r * (1 - amount)); g = int(g * (1 - amount)); b = int(b * (1 - amount))
    return (r << 16) | (g << 8) | b


def build_one(cdef):
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)

    bpy.ops.import_scene.gltf(filepath=SRC)
    armature = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    root_empty = armature.parent if armature.parent else armature
    p = BUILD_PARAMS[cdef['build']]
    root_empty.scale = tuple(a * b for a, b in zip(root_empty.scale, silhouette_scale(cdef['build'])))
    # Without this, armature.matrix_world (read by head_rest_frame/
    # measure_crown below) can still reflect the PRE-scale transform even
    # though the exported mesh ends up correctly scaled (the exporter forces
    # its own depsgraph evaluation) - harmless for 'round' (its multiplier is
    # 1.0, a no-op), badly wrong for every other build, worst for 'tall'
    # (its height factor is furthest from 1.0): crest objects get positioned
    # from a stale, too-small head reference, so they land far from the
    # head's real (correctly-scaled) exported position.
    bpy.context.view_layer.update()

    for name in ('body', 'joints'):
        obj = bpy.data.objects.get(name)
        if obj is None:
            continue
        mat = obj.data.materials[0]
        mat.use_nodes = True
        bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        bsdf.inputs['Base Color'].default_value = hex_to_linear_rgba(cdef['color'])
        bsdf.inputs['Roughness'].default_value = 0.5

    dark_hex = darken(cdef['color'])
    builder = CREST_BUILDERS[cdef['crest']]
    builder(p['head_w'], p['head_h'], p['bobbleY'], armature, cdef['accent'], dark_hex)

    bpy.ops.object.select_all(action='DESELECT')
    for o in bpy.data.objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = armature

    out = os.path.join(OUT_DIR, f"_cast-{cdef['id']}.glb")
    bpy.ops.export_scene.gltf(
        filepath=out, export_format='GLB', use_selection=True,
        export_animations=True, export_skins=True, export_apply=False,
    )
    print(f"BUILD_CAST_OK id={cdef['id']} bytes={os.path.getsize(out)}")


if CHAR_ID is None:
    raise SystemExit('usage: run-blender.mjs build-cast.py -- <char-id>  (one of bopp/zizz/kwark/tuff/mimo/nibb/glub/fizz)')

cdef = next((c for c in CHAR_DEFS if c['id'] == CHAR_ID), None)
if cdef is None:
    raise SystemExit(f'unknown character id: {CHAR_ID}')

build_one(cdef)
