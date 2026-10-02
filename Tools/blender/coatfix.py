# blender -b --python coatfix.py -- <rigged.blend> <out.blend>
# Coat hem / loose cloth: move leg weight to the hips, fading with distance from the leg bone axis.
import sys, bpy
from mathutils import Vector
src, out = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.open_mainfile(filepath=src)
mesh = bpy.data.objects["Batter"]; arm = bpy.data.objects["BatterRig"]
vg = mesh.vertex_groups
bones = arm.data.bones
def seg_dist(p, a, b):
    ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared)); return (a + ab * t - p).length
legs = {s: (bones[f"UpperLeg.{s}"].head_local, bones[f"UpperLeg.{s}"].tail_local) for s in "LR"}
leg_groups = {vg[n].index: n for n in ("UpperLeg.L", "UpperLeg.R", "LowerLeg.L", "LowerLeg.R")}
hips = vg["Hips"]
R0, R1, Z0, Z1 = 0.085, 0.17, 0.36, 0.44   # radial fade (m) and height fade (m)
moved = 0; touched = 0
for v in mesh.data.vertices:
    lw = [(g.group, g.weight) for g in v.groups if g.group in leg_groups and g.weight > 0]
    if not lw: continue
    side = "L" if leg_groups[lw[0][0]].endswith("L") else "R"
    d = seg_dist(v.co, *legs[side])
    fr = min(1.0, max(0.0, (d - R0) / (R1 - R0)))
    fz = min(1.0, max(0.0, (v.co.z - Z0) / (Z1 - Z0)))
    f = fr * fz
    if f <= 0: continue
    for gi, w in lw:
        take = w * f
        vg[gi].add([v.index], w - take, "REPLACE")
        hips.add([v.index], take, "ADD"); moved += take
    touched += 1
print("COATFIX verts", touched, "weight moved", round(moved, 1))
bpy.ops.wm.save_as_mainfile(filepath=out)
