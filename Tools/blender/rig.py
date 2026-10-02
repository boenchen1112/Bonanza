# blender -b --python rig.py -- <mesh.blend> <stage: markers|bind> <out.blend> <render_dir>
# Humanoid armature (Unity Humanoid names) + tail + ears. Character faces -Y, metres.
# Joints start from hand-placed guesses, then are fitted to the mesh: each limb joint moves
# to the centre of the mesh cross-section around it, and each chain end is pulled back
# inside the mesh. Every bone is then sampled against the (closed) mesh to report how much
# of it sits outside the surface.
import sys, os, math, bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
src, stage, out_blend, rdir = sys.argv[sys.argv.index("--") + 1:][:4]
os.makedirs(rdir, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=src)
mesh = bpy.data.objects["Batter"]
V = [mesh.matrix_world @ v.co for v in mesh.data.vertices]

XC = -0.05
J = {  # initial guesses
    "Hips": (XC, -0.12, 0.54), "Spine": (XC, -0.12, 0.68), "Chest": (XC, -0.12, 0.84),
    "Neck": (XC, -0.13, 1.06), "Head": (XC, -0.15, 1.16), "HeadTop": (XC, -0.15, 1.58),
    "EarBase.L": (0.135, -0.17, 1.68), "EarTip.L": (0.19, -0.17, 1.85),
    "EarBase.R": (-0.17, -0.17, 1.70), "EarTip.R": (-0.21, -0.17, 1.89),
    "Clav.L": (XC + 0.06, -0.06, 1.00), "Shoulder.L": (0.17, -0.03, 0.95), "Elbow.L": (0.295, 0.0, 0.78),
    "Wrist.L": (0.297, 0.0, 0.64), "HandTip.L": (0.30, -0.01, 0.55),
    "Clav.R": (XC - 0.06, -0.06, 1.00), "Shoulder.R": (-0.27, -0.09, 0.965), "Elbow.R": (-0.41, -0.11, 0.81),
    "Wrist.R": (-0.54, -0.14, 0.73), "HandTip.R": (-0.62, -0.14, 0.70),
    "Hip.L": (XC + 0.10, -0.12, 0.52), "Knee.L": (0.07, -0.14, 0.36), "Ankle.L": (0.12, -0.12, 0.17),
    "Ball.L": (0.14, -0.22, 0.07), "ToeTip.L": (0.14, -0.31, 0.06),
    "Hip.R": (XC - 0.10, -0.12, 0.52), "Knee.R": (-0.16, -0.14, 0.36), "Ankle.R": (-0.20, -0.12, 0.17),
    "Ball.R": (-0.22, -0.22, 0.07), "ToeTip.R": (-0.22, -0.31, 0.06),
    "Tail0": (XC + 0.05, -0.02, 0.64), "Tail1": (0.16, 0.10, 0.60), "Tail2": (0.36, 0.20, 0.42),
    "Tail3": (0.44, 0.27, 0.26), "TailTip": (0.46, 0.30, 0.12),
}
J = {k: Vector(v) for k, v in J.items()}
# bone: (head joint, tail joint, parent bone)
B = {
    "Hips": ("Hips", "Spine", None), "Spine": ("Spine", "Chest", "Hips"), "Chest": ("Chest", "Neck", "Spine"),
    "Neck": ("Neck", "Head", "Chest"), "Head": ("Head", "HeadTop", "Neck"),
    "Ear.L": ("EarBase.L", "EarTip.L", "Head"), "Ear.R": ("EarBase.R", "EarTip.R", "Head"),
    "Tail1": ("Tail0", "Tail1", "Hips"), "Tail2": ("Tail1", "Tail2", "Tail1"),
    "Tail3": ("Tail2", "Tail3", "Tail2"), "Tail4": ("Tail3", "TailTip", "Tail3"),
}
for s in "LR":
    B.update({
        f"Shoulder.{s}": (f"Clav.{s}", f"Shoulder.{s}", "Chest"),
        f"UpperArm.{s}": (f"Shoulder.{s}", f"Elbow.{s}", f"Shoulder.{s}"),
        f"LowerArm.{s}": (f"Elbow.{s}", f"Wrist.{s}", f"UpperArm.{s}"),
        f"Hand.{s}": (f"Wrist.{s}", f"HandTip.{s}", f"LowerArm.{s}"),
        f"UpperLeg.{s}": (f"Hip.{s}", f"Knee.{s}", "Hips"),
        f"LowerLeg.{s}": (f"Knee.{s}", f"Ankle.{s}", f"UpperLeg.{s}"),
        f"Foot.{s}": (f"Ankle.{s}", f"Ball.{s}", f"LowerLeg.{s}"),
        f"Toes.{s}": (f"Ball.{s}", f"ToeTip.{s}", f"Foot.{s}"),
    })

# fitting/checking copy with consistent outward normals (Meshy output has flipped patches)
import bmesh
_bm = bmesh.new(); _bm.from_mesh(mesh.data)
_before = [f.normal.copy() for f in _bm.faces]
bmesh.ops.recalc_face_normals(_bm, faces=_bm.faces)
_bm.normal_update()
print("NORMALS flipped faces", sum(1 for f, n in zip(_bm.faces, _before) if f.normal.dot(n) < 0), "of", len(_bm.faces))
bvh = BVHTree.FromBMesh(_bm)

def ray_t(p, u, maxd):
    loc, n, i, dist = bvh.ray_cast(p, u, maxd)
    return dist if loc is not None else None

def centre(p, d, r, iters=5):
    """Move p to the middle of the surrounding wall, in the plane normal to d: rays out in six
    axis pairs from inside, each pair pulls p halfway between its two hits. Straps or coat
    outside the nearest wall are never reached."""
    d = d.normalized(); a = d.orthogonal().normalized(); b = d.cross(a).normalized()
    n = 0
    for _ in range(iters):
        corr = Vector(); n = 0
        for ang in range(0, 180, 30):
            th = math.radians(ang); u = a * math.cos(th) + b * math.sin(th)
            tp, tm = ray_t(p, u, r), ray_t(p, -u, r)
            if tp is None or tm is None: continue
            corr += u * (tp - tm) / 2; n += 1
        if n == 0: break
        p = p + corr / n
    return p, n

def end_inside(base, guess, maxlen, margin=0.15, min_margin=0.012):
    """Cast from base toward guess; the tip goes where the ray leaves the mesh, stepped back inside."""
    d = (guess - base).normalized()
    t = ray_t(base, d, maxlen)
    if t is None: t = (guess - base).length
    return base + d * (t - max(min_margin, t * margin)), round(t, 3)

# chains fitted in order: (joint, neighbour-before, neighbour-after, radius)
chains = []
for s in "LR":
    chains += [
        [("Elbow." + s, "Shoulder." + s, "Wrist." + s, 0.08),
         ("Wrist." + s, "Elbow." + s, "HandTip." + s, 0.06)],
        [("Knee." + s, "Hip." + s, "Ankle." + s, 0.08), ("Ankle." + s, "Knee." + s, "Ball." + s, 0.08)],
    ]
chains.append([("Tail2", "Tail1", "Tail3", 0.16), ("Tail3", "Tail2", "TailTip", 0.14)])
for _ in range(3):
    for chain in chains:
        for j, a, b, r in chain:
            d = (J[b] - J[a]).normalized()
            J[j], n = centre(J[j], d, r)

def outside(p):
    loc, n, i, dist = bvh.find_nearest(p)
    return (p - loc).dot(n) > 0, dist

# tail root: march from inside the tail back toward the hips; the chain starts where the
# tail mesh ends (tail bones need not touch the hips, so nothing crosses the gap under the coat)
d = (J["Tail0"] - J["Tail2"]).normalized(); p = J["Tail2"].copy(); last = p.copy()
for _ in range(200):
    p = p + d * 0.005
    if outside(p)[0]: break
    last = p.copy()
J["Tail0"] = last - d * 0.01
J["Tail1"], _ = centre((J["Tail0"] + J["Tail2"]) / 2, J["Tail2"] - J["Tail0"], 0.16)
print("TAIL root", tuple(round(c, 3) for c in J["Tail0"]))
# ears: start inside each ear, centre, find the tip by ray, mirror the base down into the head
EAR = {"L": ((0.157, -0.17, 1.735), (0.055, 0, 0.17)), "R": ((-0.20, -0.17, 1.75), (-0.04, 0, 0.19))}
for s, (mid, ax) in EAR.items():
    mid, ax = Vector(mid), Vector(ax).normalized()
    mid, _ = centre(mid, ax, 0.06)
    J["EarTip." + s], t = end_inside(mid, mid + ax, 0.3)
    J["EarBase." + s] = mid - (J["EarTip." + s] - mid)
    print("END EarTip." + s, "ray", t)
# feet: ball centred in the toe box, toe tip by a level ray forward
for s in "LR":
    J["Ball." + s], _ = centre(J["Ball." + s], J["Ball." + s] - J["Ankle." + s], 0.08)
    J["ToeTip." + s], t = end_inside(J["Ball." + s], J["Ball." + s] + Vector((0, -1, 0)), 0.3)
    print("END ToeTip." + s, "ray", t)
for e, base, maxlen in (("HandTip.L", "Wrist.L", 0.2), ("HandTip.R", "Wrist.R", 0.2),
                        ("TailTip", "Tail3", 0.4), ("HeadTop", "Head", 0.7)):
    J[e], t = end_inside(J[base], J[e], maxlen)
    print("END", e, "ray", t)

# inside check against the closed mesh
bad = 0
for name, (h, t, par) in B.items():
    samples = [J[h].lerp(J[t], k / 10) for k in range(11)]
    out = [s for s in samples if outside(s)[0]]
    depth_tip = outside(J[t])[1] * (1 if not outside(J[t])[0] else -1)
    if out: bad += 1
    print(f"BONE {name:12} outside {len(out)}/11  tip depth {depth_tip*100:+.1f} cm")
print("BONES OUTSIDE", bad, "of", len(B))

sc = bpy.context.scene
if sc.camera is None:
    c = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(c); sc.camera = c
cam = sc.camera; cam.data.type = "ORTHO"
h = max(v.z for v in V)
sc.render.resolution_x, sc.render.resolution_y = 900, 1200

def shot(name, yaw_deg, scale, z, x=0.0):
    a = math.radians(yaw_deg); cam.data.ortho_scale = scale
    cam.location = (math.sin(a) * 5 + math.cos(a) * x, math.cos(a) * -5 + math.sin(a) * x, z)
    cam.rotation_euler = (math.radians(90), 0, a)
    sc.render.filepath = os.path.join(rdir, name + ".png"); bpy.ops.render.render(write_still=True)

def workbench(xray):
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "OBJECT" if xray else "TEXTURE"
    sc.display.shading.show_xray = xray
    sc.display.shading.xray_alpha = 0.35

if stage == "markers":
    mesh.color = (0.8, 0.8, 0.8, 1)
    for name, (hj, tj, par) in B.items():
        hd, tl = J[hj], J[tj]; d = tl - hd
        bpy.ops.mesh.primitive_cylinder_add(radius=0.006, depth=d.length, location=(hd + tl) / 2)
        c = bpy.context.object; c.rotation_mode = "QUATERNION"
        c.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
        c.color = (0.1, 0.3, 1.0, 1) if "Tail" in name or "Ear" in name else (1.0, 0.15, 0.1, 1)
        for p in (hd, tl):
            bpy.ops.mesh.primitive_uv_sphere_add(radius=0.012, location=p)
            bpy.context.object.color = (1.0, 0.9, 0.0, 1)
    workbench(True)
    shot("front", 0, h * 1.1, h / 2)
    shot("side", 90, h * 1.1, h / 2)
    shot("zoom_arm_R", 0, 0.45, 0.78, x=-0.40)
    shot("zoom_arm_L", 0, 0.45, 0.72, x=0.25)
    shot("zoom_feet", 0, 0.6, 0.18, x=-0.05)
    shot("zoom_feet_side", 90, 0.6, 0.18, x=-0.10)
    shot("zoom_head", 0, 0.7, 1.55, x=-0.02)
    shot("zoom_tail_side", 90, 0.7, 0.40, x=0.12)
    print("MARKERS DONE")
    sys.exit(0)

# ---- bind ----
arm_data = bpy.data.armatures.new("BatterRig")
arm = bpy.data.objects.new("BatterRig", arm_data)
sc.collection.objects.link(arm)
arm_data.display_type = "STICK"; arm.show_in_front = True
bpy.context.view_layer.objects.active = arm
for o in sc.objects: o.select_set(False)
arm.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
eb = arm_data.edit_bones
for name, (hj, tj, par) in B.items():
    b = eb.new(name); b.head = J[hj]; b.tail = J[tj]
for name, (hj, tj, par) in B.items():
    if par:
        eb[name].parent = eb[par]
        eb[name].use_connect = B[par][1] == hj
bpy.ops.armature.select_all(action="SELECT")
bpy.ops.armature.calculate_roll(type="GLOBAL_NEG_Y")
bpy.ops.object.mode_set(mode="OBJECT")

mesh.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type="ARMATURE_AUTO")

# any vertex bone-heat missed -> nearest bone segment
segs = [(n, J[h], J[t]) for n, (h, t, p) in B.items()]
def seg_dist(p, a, b):
    ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared)); return (a + ab * t - p).length
missed = 0
for v in mesh.data.vertices:
    if sum(g.weight for g in v.groups) < 1e-3:
        n = min(segs, key=lambda s: seg_dist(v.co, s[1], s[2]))[0]
        mesh.vertex_groups[n].add([v.index], 1.0, "REPLACE"); missed += 1
print("BIND missed verts filled", missed, "of", len(mesh.data.vertices))
bpy.ops.wm.save_as_mainfile(filepath=out_blend)
print("BIND DONE")
