# blender -b --python rebind.py -- <rig.blend> <out.blend>
# Re-skin the batter after the bones were moved by hand in Edit Mode.
# Keeps every bone where it was placed; recomputes bone roll (so local X stays the hinge),
# replaces all vertex weights with automatic weights for the new joint positions,
# fills any vertex bone-heat misses from the nearest bone, then re-applies the coat-hem fix.
import sys, math, bpy, bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree
src, out = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.open_mainfile(filepath=src)
arm = bpy.data.objects["BatterRig"]; mesh = bpy.data.objects["Batter"]
sc = bpy.context.scene

# 1. inside check: 11 samples per bone against the closed mesh
bm = bmesh.new(); bm.from_mesh(mesh.data); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bvh = BVHTree.FromBMesh(bm)
def outside(p):
    loc, n, i, d = bvh.find_nearest(p); return (p - loc).dot(n) > 0, d
bad = 0
for b in arm.data.bones:
    if not b.use_deform: continue
    s = [b.head_local.lerp(b.tail_local, k / 10) for k in range(11)]
    o = [p for p in s if outside(p)[0]]
    if o:
        bad += 1
        print(f"OUTSIDE {b.name:12} {len(o)}/11 worst {max(outside(p)[1] for p in o)*100:.1f} cm")
print("BONES OUTSIDE", bad, "of", len(arm.data.bones))

# 2. roll: same convention as rig.py
for o in sc.objects: o.select_set(False)
bpy.context.view_layer.objects.active = arm; arm.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.armature.select_all(action="SELECT")
bpy.ops.armature.calculate_roll(type="GLOBAL_NEG_Y")
bpy.ops.object.mode_set(mode="OBJECT")
for pb in arm.pose.bones:
    pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0); pb.scale = (1, 1, 1)

# 3. fresh automatic weights (old groups were fitted to the old joints; Tail4 no longer exists)
for md in [m for m in mesh.modifiers if m.type == "ARMATURE"]: mesh.modifiers.remove(md)
mesh.vertex_groups.clear()
mw = mesh.matrix_world.copy(); mesh.parent = None; mesh.matrix_world = mw
for o in sc.objects: o.select_set(False)
mesh.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type="ARMATURE_AUTO")
segs = [(b.name, b.head_local, b.tail_local) for b in arm.data.bones if b.use_deform]
def seg_dist(p, a, b):
    ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared)); return (a + ab * t - p).length
missed = 0
for v in mesh.data.vertices:
    if sum(g.weight for g in v.groups) < 1e-3:
        n = min(segs, key=lambda s: seg_dist(v.co, s[1], s[2]))[0]
        g = mesh.vertex_groups.get(n) or mesh.vertex_groups.new(name=n)
        g.add([v.index], 1.0, "REPLACE"); missed += 1
print("BIND groups", len(mesh.vertex_groups), "missed verts filled", missed, "of", len(mesh.data.vertices))

# 4. coat hem (same rule as coatfix.py): leg weight far from the thigh axis goes to the hips
vg = mesh.vertex_groups; bones = arm.data.bones
legs = {s: (bones[f"UpperLeg.{s}"].head_local, bones[f"UpperLeg.{s}"].tail_local) for s in "LR"}
leg_groups = {vg[n].index: n for n in ("UpperLeg.L", "UpperLeg.R", "LowerLeg.L", "LowerLeg.R") if n in vg}
hips = vg["Hips"]
R0, R1, Z0, Z1 = 0.085, 0.17, 0.36, 0.44
touched = 0
for v in mesh.data.vertices:
    lw = [(g.group, g.weight) for g in v.groups if g.group in leg_groups and g.weight > 0]
    if not lw: continue
    side = "L" if leg_groups[lw[0][0]].endswith("L") else "R"
    d = seg_dist(v.co, *legs[side])
    f = min(1.0, max(0.0, (d - R0) / (R1 - R0))) * min(1.0, max(0.0, (v.co.z - Z0) / (Z1 - Z0)))
    if f <= 0: continue
    for gi, w in lw:
        vg[gi].add([v.index], w * (1 - f), "REPLACE"); hips.add([v.index], w * f, "ADD")
    touched += 1
print("COATFIX verts", touched)
bpy.ops.wm.save_as_mainfile(filepath=out)
print("REBIND DONE")
