# blender -b --python retarget.py -- <animrig.blend> <out.blend> "<Clip>|<mixamo.fbx>|<start>|<end>|<ref>|<key=value,...>" ...
# Transfers Mixamo clips onto BatterRig as FK keys, one action per job. start/end/ref are source
# frames at 30 fps; the clip is turned and centred so that at frame <ref> the hips face -Y over
# the origin (use the same ref for clips cut from one file so they join up).
# Each bone copies the source bone's world-space direction with a fixed twist, so the body keeps
# its own proportions. Hips travel is scaled by leg length. The bat is laid along the line
# through both grips (left hand at the knob end); when the hands are apart it stays in the
# right hand. Control-rig constraints are muted. Keys at 24 fps. key=value pairs go into the
# clip's metadata (numbers only), e.g. contact=7 or loop=1,beats=3.
import sys, os, math, json, bpy
from mathutils import Vector, Matrix
args = sys.argv[sys.argv.index("--") + 1:]
src_blend, out = args[:2]; jobs = [j.split("|") for j in args[2:]]
bpy.ops.wm.open_mainfile(filepath=src_blend)
sc = bpy.context.scene
arm = bpy.data.objects["BatterRig"]; B = arm.data.bones; pb = arm.pose.bones
for p in pb:
    for c in p.constraints: c.mute = True
    p.rotation_mode = "QUATERNION"
OURS = ["Hips", "Spine", "Chest", "Neck", "Head"] + [f"{n}.{s}" for s in "LR" for n in
        ("Shoulder", "UpperArm", "LowerArm", "Hand", "UpperLeg", "LowerLeg", "Foot", "Toes")]
MP = {"Hips": "Hips", "Spine": "Spine1", "Chest": "Spine2", "Neck": "Neck", "Head": "Head"}
for s, side in (("L", "Left"), ("R", "Right")):
    for k, v in {"Shoulder": "Shoulder", "UpperArm": "Arm", "LowerArm": "ForeArm", "Hand": "Hand",
                 "UpperLeg": "UpLeg", "LowerLeg": "Leg", "Foot": "Foot", "Toes": "ToeBase"}.items():
        MP[f"{k}.{s}"] = side + v
MP = {k: "mixamorig:" + v for k, v in MP.items()}

def rot(M): return M.to_3x3().normalized().to_quaternion()
AW = arm.matrix_world.copy(); AWi = AW.inverted(); AWq = rot(AW)
order = []
def walk(b):
    order.append(b)
    for c in b.children: walk(c)
for b in B:
    if b.parent is None: walk(b)
T_REST = {n: AWq @ rot(B[n].matrix_local) for n in OURS}
def frame_of(q):
    """Bone frame with a fixed twist: Y along the bone, Z toward the character's front (-Y)
    or, for bones pointing forward/back, toward up."""
    y = (q @ Vector((0, 1, 0))).normalized()
    sec = Vector((0, -1, 0)) if abs(y.y) < 0.7 else Vector((0, 0, 1))
    z = (sec - y * sec.dot(y)).normalized(); x = y.cross(z)
    return Matrix((x, y, z)).transposed().to_quaternion()
F_T = {n: frame_of(T_REST[n]) for n in OURS}
hips_rest = AW @ B["Hips"].head_local
leg_t = (B["UpperLeg.L"].head_local - B["Foot.L"].head_local).length
arm.animation_data_create(); GROUND = {}

for name, path, s0, s1, ref, extra in jobs:
    s0, s1, ref = int(s0), int(s1), int(ref)
    sc.render.fps = 30
    before = set(bpy.data.objects); bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    S = next(o for o in new if o.type == "ARMATURE")
    W = lambda n: S.matrix_world @ S.pose.bones[n].head
    sc.frame_set(ref)
    lft = W(MP["UpperLeg.L"]) - W(MP["UpperLeg.R"]); yaw = math.atan2(lft.y, lft.x); h0 = W(MP["Hips"])
    S.matrix_world = Matrix.Rotation(-yaw, 4, "Z") @ Matrix.Translation((-h0.x, -h0.y, 0)) @ S.matrix_world
    bpy.context.view_layer.update()
    SW = S.matrix_world.copy(); SWq = rot(SW)
    S_REST = {n: SWq @ rot(S.data.bones[MP[n]].matrix_local) for n in OURS}
    # source rest frame -> target rest frame, twist fixed by frame_of()
    OFF = {n: S_REST[n].inverted() @ frame_of(S_REST[n]) @ F_T[n].inverted() @ T_REST[n] for n in OURS}
    src_hip_rest_z = (SW @ S.data.bones[MP["Hips"]].head_local).z
    leg_s = (S.data.bones[MP["UpperLeg.L"]].head_local - S.data.bones[MP["Foot.L"]].head_local).length * SW.to_scale().x
    k = leg_t / leg_s

    act = bpy.data.actions.new(name + "_src"); act.use_fake_user = True
    arm.animation_data.action = act
    n_out = round((s1 - s0) / 30 * 24)
    PREV = {}
    def solve(f, dz):
        t = s0 + f * 30 / 24; fi = int(math.floor(t)); sc.frame_set(fi, subframe=t - fi)
        want = {n: AWq.inverted() @ (rot(S.matrix_world @ S.pose.bones[MP[n]].matrix) @ OFF[n]) for n in OURS}
        hp = W(MP["Hips"])
        hips_pos = AWi @ (hips_rest + Vector((hp.x * k, hp.y * k, (hp.z - src_hip_rest_z) * k + dz)))
        M = {}
        for b in order:
            par = b.parent
            rest_rel = (par.matrix_local.inverted() @ b.matrix_local) if par else b.matrix_local
            base = (M[par.name] @ rest_rel) if par else rest_rel
            if b.name in want:
                Mb = want[b.name].to_matrix().to_4x4()
                Mb.translation = hips_pos if b.name == "Hips" else base.translation
            else:
                Mb = base
            M[b.name] = Mb
        def grip(s):
            h = M["Hand." + s]; return h.translation + h.to_3x3() @ Vector((0, B["Hand." + s].length * 0.6, 0))
        gL, gR = grip("L"), grip("R")
        if 0.02 < (gR - gL).length < 0.2:
            d = (gR - gL).normalized()
            sm = (M["UpperArm.L"].translation + M["UpperArm.R"].translation) / 2
            v = sm - gL; bz = -(v - d * v.dot(d)).normalized()
            Mb = Matrix((d.cross(bz), d, bz)).transposed().to_4x4(); Mb.translation = gL - d * 0.04
            M["Bat"] = Mb
        return M
    # the legs have other proportions than the source: lower or raise the whole clip so the
    # lowest ankle of the clip sits at its standing height
    ankle = min(min(solve(f, 0)["Foot." + s].translation.z for s in "LR") for f in range(n_out + 1))
    dz = GROUND.setdefault(path, B["Foot.L"].head_local.z - ankle)   # one shift per source file, so cuts join up
    for f in range(n_out + 1):
        M = solve(f, dz)
        for b in order:
            par = b.parent
            rest_rel = (par.matrix_local.inverted() @ b.matrix_local) if par else b.matrix_local
            base = (M[par.name] @ rest_rel) if par else rest_rel
            p = pb[b.name]; p.matrix_basis = base.inverted() @ M[b.name]
            q = p.rotation_quaternion.copy(); last = PREV.get(b.name)
            if last is not None and q.dot(last) < 0: q.negate(); p.rotation_quaternion = q
            PREV[b.name] = q.copy()
            for prop in ("location", "rotation_quaternion", "scale"):
                p.keyframe_insert(prop, frame=f, group=b.name)
    meta = {"frames": n_out, "fps": 24, "loop": False, "source": os.path.basename(path)}
    for kv in filter(None, extra.split(",")):
        key, v = kv.split("="); meta[key] = bool(int(v)) if key == "loop" else float(v) if "." in v else int(v)
    act["meta"] = json.dumps(meta)
    arm.animation_data.action = None
    for o in new: bpy.data.objects.remove(o, do_unlink=True)
    print("RETARGET", name, "frames", n_out, "k", round(k, 3), "yaw", round(math.degrees(yaw)), "ground shift", round(dz, 3))
sc.render.fps = 24
bpy.ops.wm.save_as_mainfile(filepath=out)
print("RETARGET DONE")
