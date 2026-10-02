# blender -b --python animrig.py -- <batter.blend> <out.blend>
# Adds the animation controls and the bat to the rigged batter:
#   Root (ground, parent of Hips) - Unity Generic root
#   Bat (deform, child of Hand.R) + Bat mesh (wood, red grip tape, teal ring)
#   control bones (removed again at export): FootCtrl.L/R + KneePole.L/R (leg IK),
#   BatCtrl (child of Chest) with Grip.L/R (arm IK targets, hand orientation), ElbowPole.L/R
import sys, math, bpy, bmesh
from mathutils import Vector, Matrix
src, out = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.open_mainfile(filepath=src)
arm = bpy.data.objects["BatterRig"]; body = bpy.data.objects["Batter"]
sc = bpy.context.scene
for o in sc.objects: o.select_set(False)
bpy.context.view_layer.objects.active = arm; arm.select_set(True)

BAT_LEN, GRIP_L, GRIP_R, WRIST_OFF = 0.86, 0.035, 0.115, 0.035

bpy.ops.object.mode_set(mode="EDIT")
eb = arm.data.edit_bones
def new(name, head, tail, parent=None, deform=False, roll_z=None):
    b = eb.new(name); b.head = head; b.tail = tail; b.use_deform = deform
    if parent: b.parent = eb[parent]
    if roll_z is not None: b.align_roll(roll_z)
    return b

root = new("Root", (0, 0, 0), (0, 0, 0.2), deform=False, roll_z=Vector((0, -1, 0)))
eb["Hips"].parent = root
for s in "LR":
    f = eb["Foot." + s]
    new("FootCtrl." + s, f.head, f.tail, "Root", roll_z=f.z_axis)
    knee = eb["LowerLeg." + s].head
    new("KneePole." + s, knee + Vector((0, -0.45, 0)), knee + Vector((0, -0.55, 0)), "FootCtrl." + s)
    elbow = eb["LowerArm." + s].head
    new("ElbowPole." + s, elbow + Vector((0, 0.40, -0.05)), elbow + Vector((0, 0.50, -0.05)), "Chest")

# bat at rest: hanging from the right hand, knob up, barrel pointing down-forward
hr = eb["Hand.R"]
grip_pt = hr.head.lerp(hr.tail, 0.6)
d = Vector((0, -0.55, -0.83)).normalized()
knob = grip_pt - d * GRIP_R
bat = new("Bat", knob, knob + d * BAT_LEN, "Hand.R", deform=True, roll_z=Vector((1, 0, 0)))
batc = new("BatCtrl", knob, knob + d * BAT_LEN, "Chest", roll_z=Vector((1, 0, 0)))
M = batc.matrix.copy(); bx, by, bz = M.col[0].xyz, M.col[1].xyz, M.col[2].xyz
for s, t in (("L", GRIP_L), ("R", GRIP_R)):
    p = knob + by * t
    # grip bone = where the hand should lie: wrist on one side of the handle, fingers across it,
    # knuckle line (hand X) along the bat
    new("Grip." + s, p - bz * WRIST_OFF, p + bz * (0.09 - WRIST_OFF), "BatCtrl", roll_z=bx.cross(bz))
# the chain dangling from the right wrist gets its own bone so it can hang instead of sticking out
new("Chain.R", (-0.47, -0.19, 0.68), (-0.48, -0.22, 0.43), "LowerArm.R", deform=True, roll_z=Vector((0, -1, 0)))
bpy.ops.object.mode_set(mode="OBJECT")
def seg(p, a, b):
    ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared)); return (a + ab * t - p).length
B = arm.data.bones; vg = body.vertex_groups
chain = [(B[n].head_local, B[n].tail_local) for n in ("UpperArm.R", "LowerArm.R", "Hand.R")]
idx = {vg[n].index: vg[n] for n in ("UpperArm.R", "LowerArm.R", "Hand.R")}
cg = vg.new(name="Chain.R"); moved = 0
for v in body.data.vertices:
    if v.co.z > 0.70: continue
    lw = [(g.group, g.weight) for g in v.groups if g.group in idx and g.weight > 0]
    if not lw: continue
    d = min(seg(v.co, a, b) for a, b in chain)
    f = min(1.0, max(0.0, (d - 0.14) / 0.05))
    if f <= 0: continue
    for gi, w in lw:
        idx[gi].add([v.index], w * (1 - f), "REPLACE"); cg.add([v.index], w * f, "ADD")
    moved += 1
print("CHAIN verts", moved)

# bat mesh: revolved profile along the Bat bone, 14 sides
prof = [(0.0, 0.0), (0.0, 0.026), (0.012, 0.030), (0.022, 0.018), (0.26, 0.017), (0.32, 0.020),
        (0.50, 0.036), (0.62, 0.047), (0.80, 0.050), (0.845, 0.044), (0.86, 0.025), (0.862, 0.0)]
bm = bmesh.new(); N = 14
rings = []
bone = arm.data.bones["Bat"]
Mb = arm.matrix_world @ bone.matrix_local
for t, r in prof:
    ring = []
    for i in range(N if r > 0 else 1):
        a = 2 * math.pi * i / N
        ring.append(bm.verts.new(Mb @ Vector((math.cos(a) * r, t, math.sin(a) * r))))
    rings.append(ring)
for a, b in zip(rings, rings[1:]):
    if len(a) == 1:
        for i in range(N): bm.faces.new((a[0], b[(i + 1) % N], b[i]))
    elif len(b) == 1:
        for i in range(N): bm.faces.new((a[i], a[(i + 1) % N], b[0]))
    else:
        for i in range(N): bm.faces.new((a[i], a[(i + 1) % N], b[(i + 1) % N], b[i]))
bm.normal_update()
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
me = bpy.data.meshes.new("Bat"); bm.to_mesh(me); bm.free()
batobj = bpy.data.objects.new("Bat", me); sc.collection.objects.link(batobj)
def mat(name, rgb, rough):
    m = bpy.data.materials.new(name); m.use_nodes = True
    p = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    p.inputs["Base Color"].default_value = (*rgb, 1); p.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*rgb, 1); return m
for m in (mat("BatWood", (0.80, 0.55, 0.30), 0.55), mat("BatGrip", (0.62, 0.06, 0.07), 0.8), mat("BatRing", (0.10, 0.62, 0.62), 0.4)):
    me.materials.append(m)
for poly in me.polygons:
    t = (Mb.inverted() @ poly.center).y
    poly.material_index = 1 if 0.02 < t < 0.27 else 2 if 0.27 <= t < 0.33 else 0
    poly.use_smooth = True
vg = batobj.vertex_groups.new(name="Bat"); vg.add(list(range(len(me.vertices))), 1.0, "REPLACE")
batobj.parent = arm; mod = batobj.modifiers.new("Armature", "ARMATURE"); mod.object = arm

# constraints
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")
pb = arm.pose.bones
def ik(bone, target, pole):
    c = pb[bone].constraints.new("IK"); c.target = arm; c.subtarget = target
    c.pole_target = arm; c.pole_subtarget = pole; c.chain_count = 2; return c
iks = {}
for s in "LR":
    iks["LowerLeg." + s] = ik("LowerLeg." + s, "FootCtrl." + s, "KneePole." + s)
    c = pb["Foot." + s].constraints.new("COPY_ROTATION"); c.target = arm; c.subtarget = "FootCtrl." + s
    iks["LowerArm." + s] = ik("LowerArm." + s, "Grip." + s, "ElbowPole." + s)
    c = pb["Hand." + s].constraints.new("COPY_ROTATION"); c.target = arm; c.subtarget = "Grip." + s
c = pb["Bat"].constraints.new("COPY_TRANSFORMS"); c.target = arm; c.subtarget = "BatCtrl"

# pole angles: pick the one that reproduces the rest pose when the target sits at the rest joint
def rest_fit(bone, target):
    knee_rest = arm.data.bones[bone].head_local.copy()
    best = None
    for ang in range(-180, 181, 5):
        iks[bone].pole_angle = math.radians(ang)
        bpy.context.view_layer.update()
        err = (pb[bone].head - knee_rest).length
        if best is None or err < best[1]: best = (ang, err)
    iks[bone].pole_angle = math.radians(best[0]); return best
for s in "LR":
    print("POLE LowerLeg." + s, rest_fit("LowerLeg." + s, "FootCtrl." + s))
# arms: temporarily put the grip targets at the rest wrists to calibrate
for s in "LR":
    g = pb["Grip." + s]; w = arm.data.bones["Hand." + s].head_local
    m = g.matrix.copy(); m.translation = w; g.matrix = m
    bpy.context.view_layer.update()
    print("POLE LowerArm." + s, rest_fit("LowerArm." + s, "Grip." + s))
for b in pb: b.location = (0, 0, 0); b.rotation_quaternion = (1, 0, 0, 0); b.rotation_euler = (0, 0, 0); b.scale = (1, 1, 1)
bpy.ops.object.mode_set(mode="OBJECT")
bpy.ops.wm.save_as_mainfile(filepath=out)
print("ANIMRIG DONE", len(arm.data.bones), "bones")
