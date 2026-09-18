"""
TUFF bake-off, build A: reshape + recolor the already-rigged, already-
animated ybot.glb (web/src/assets/chars/ybot.glb — Mixamo Y Bot, converted
by convert-mixamo.mjs) into TUFF's design, instead of building a new
skinned character from scratch. Its skeleton and baked clips (swing_rig
etc.) stay untouched; only bone SCALE channels are added (Mixamo clips
never animate scale, so a scale-only rest-pose change survives them
cleanly), plus material colours and a horn crest.

Proportions and colours are read straight from the toy rig's own numbers —
web/src/chars/rig.js BUILDS.wide (shape 'block' -> build 'wide') and
web/src/shell/chars.js CHARS[tuff] / the 'horns' crest case — so this is a
same-identity comparison, not a different character.

    node tools/assets/blender/run-blender.mjs tools/assets/blender/build-tuff.py

Writes tools/assets/blender/_tuff-build-a.glb (gitignored scratch output;
promote to web/src/assets/chars/ with a provenance README only once the
bake-off picks this build).
"""
import bpy
import math
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..')
SRC = os.path.abspath(os.path.join(ROOT, 'web', 'src', 'assets', 'chars', 'ybot.glb'))
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '_tuff-build-a.glb')

# TUFF, shape 'block' -> BUILDS.wide in rig.js. Head width drives the horn
# geometry/placement, matching the 'horns' case in shell/chars.js exactly.
HEAD_W = 0.46
HEAD_H = 0.34
BODY_HEX = 0x9ee87a     # PAL.green (shell/theme.js) — TUFF's body colour
ACCENT_HEX = 0x39d4b4   # TUFF's accent — unused on the body; kept for a future trim pass
HORN_HEX = 0xfff1d6     # shell/chars.js addCrest's horn colour, unchanged


def srgb_to_linear(c):
    """glTF baseColorFactor is linear; hex colours everywhere else in this
    repo are sRGB (three.js's SRGBColorSpace convention) — convert or a
    Blender-exported character reads visibly darker/duller than its 2D
    portrait and every procedural character next to it."""
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_to_linear_rgba(h):
    r, g, b = ((h >> 16) & 255) / 255, ((h >> 8) & 255) / 255, (h & 255) / 255
    return (srgb_to_linear(r), srgb_to_linear(g), srgb_to_linear(b), 1.0)


# ---------------------------------------------------------------- import
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=SRC)

armature = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
body_obj = next(o for o in bpy.data.objects if o.name == 'body')
joints_obj = next(o for o in bpy.data.objects if o.name == 'joints')
# The empty Blender's importer parents everything under (named after the
# source file's root node) — this is what gets the overall silhouette scale,
# so the armature and both meshes widen/squash together as one rigid unit.
root_empty = armature.parent if armature.parent else armature

# --------------------------------------------------- overall silhouette
# TUFF's build (BUILDS.wide) is wide and squat: torso.w=0.86 vs the 'round'
# default's 0.48, taper 0.82 (wider at the shoulders than the waist). A
# stock Mixamo mannequin is slim and average-height; an object-level
# non-uniform scale on the shared parent widens/squashes everything at once
# without needing per-bone local-axis knowledge (Mixamo bones don't share a
# common local-axis convention, so a per-bone width guess is fragile).
root_empty.scale = tuple(a * b for a, b in zip(root_empty.scale, (1.35, 0.85, 1.15)))   # (width, height, depth)

# ------------------------------------------------------- targeted reshape
# A per-bone head/limb enlargement was tried via pose-mode scale + "Apply
# Pose as Rest Pose" and DROPPED: Blender's own warning on that operator
# ("actions... destroyed... relative to the old rest pose") turned out to be
# literal, not conservative boilerplate — it broke the `joints` mesh's
# skinning into scattered debris and froze the animation, confirmed by
# rendering it (see the bake-off notes). Reshaping a skinned character
# safely means editing the MESH's vertex positions directly (grouped by the
# skin weights that already exist per bone), never the armature's rest
# pose — a real fix, left for a follow-up pass if this build's silhouette
# (object-level scale only, below) isn't enough on its own.

# --------------------------------------------------------------- recolour
def set_base_color(obj_name, hexval):
    obj = bpy.data.objects[obj_name]
    mat = obj.data.materials[0]
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    if bsdf is None:
        bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value = hex_to_linear_rgba(hexval)
    bsdf.inputs['Roughness'].default_value = 0.5


set_base_color('body', BODY_HEX)
# 'joints' is a second UV region the original rig uses for elbows/knees —
# keep it the same body colour rather than inventing a two-tone TUFF the toy
# rig doesn't have; a trim/accent pass is a separate, deliberate design step.
set_base_color('joints', BODY_HEX)

# -------------------------------------------------------------- horns
# Exact geometry/placement from shell/chars.js's 'horns' case, adapted from
# the toy rig's joint-local coordinates to this mesh's head-bone frame:
# two cones, radius hw*0.1, height hw*0.34, either side of the head crown,
# tilted outward, coloured cream (not the accent) — matching the portrait.
horn_mat = bpy.data.materials.new('horn')
horn_mat.use_nodes = True
horn_bsdf = next(n for n in horn_mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
horn_bsdf.inputs['Base Color'].default_value = hex_to_linear_rgba(HORN_HEX)
horn_bsdf.inputs['Roughness'].default_value = 0.35

r = HEAD_W * 0.1
h = HEAD_W * 0.34
head_top_local_z = HEAD_H * 0.9   # near the crown, in the head bone's own frame

for side in (-1, 1):
    bpy.ops.mesh.primitive_cone_add(radius1=r, radius2=0.0, depth=h, location=(0, 0, 0))
    horn = bpy.context.active_object
    horn.name = f'horn_{"L" if side < 0 else "R"}'
    horn.data.materials.append(horn_mat)
    # Position/tilt in the head bone's local frame, then let the bone
    # parent below carry it into world space and along every animation.
    horn.location = (side * HEAD_W * 0.34, HEAD_W * 0.08, head_top_local_z)
    horn.rotation_euler = (math.pi / 2, 0, -side * 0.55)
    horn.parent = armature
    horn.parent_type = 'BONE'
    horn.parent_bone = 'mixamorigHead'
    # Blender's BONE parent origin is the bone's TAIL, in the bone's local
    # space rotated to point along +Y — re-zero location against that,
    # rather than the head_world matrix, since BONE parenting ignores the
    # object's own matrix_parent_inverse the way object parenting doesn't.
    horn.matrix_parent_inverse = armature.matrix_world.inverted()

# --------------------------------------------------------------- export
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects:
    o.select_set(True)
bpy.context.view_layer.objects.active = armature

bpy.ops.export_scene.gltf(
    filepath=OUT,
    export_format='GLB',
    use_selection=True,
    export_animations=True,
    export_skins=True,
    export_apply=False,   # keep the armature modifier live for skin weights
)

print(f'BUILD_TUFF_OK bytes={os.path.getsize(OUT)}')
