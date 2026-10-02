# blender -b --python anim.py -- <animrig.blend> <out.blend> <render_dir> [clip ...]
# Hand-keyed, pose-to-pose clips in a party-game style: few strong poses, fast breakdowns,
# holds, squash/stretch on Root, follow-through on ears and tail. No mocap.
# Character frame: faces -Y, its left is +X. The pitcher is at +X, the plate at -Y.
# Body bones: local Euler degrees, X = hinge (+ flexes forward), Y = twist (+ turns the front
# toward the pitcher), Z = side bend. Offsets are in the character frame (metres).
import sys, os, math, copy, json, bpy
from mathutils import Vector, Matrix, Euler, Quaternion
args = sys.argv[sys.argv.index("--") + 1:]
src, out, rdir = args[:3]; only = set(args[3:])
os.makedirs(rdir, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=src)
sc = bpy.context.scene; sc.render.fps = 24
arm = bpy.data.objects["BatterRig"]; pb = arm.pose.bones; bones = arm.data.bones
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")
for b in pb: b.rotation_mode = "XYZ"
for n in ("FootCtrl.L", "FootCtrl.R", "BatCtrl"): pb[n].rotation_mode = "QUATERNION"
ARM_REACH = {s: bones["UpperArm." + s].length + bones["LowerArm." + s].length for s in "LR"}

def R(n): return bones[n].matrix_local.to_3x3()
def loc_of(n, v): return R(n).inverted() @ Vector(v)
def quat_of(n, e):
    q = Euler([math.radians(a) for a in e]).to_quaternion()
    r = R(n).to_quaternion(); return r.inverted() @ q @ r

BASE = {
    "rot": {}, "hips": (0, 0, 0), "root": (0, 0, 0), "spin": 0, "scale": (1, 1, 1),
    "foot": {"L": ((0, 0, 0), (0, 0, 0)), "R": ((0, 0, 0), (0, 0, 0))},
    "ep": {"L": (0, 0, 0), "R": (0, 0, 0)}, "bat": None, "ikL": 1.0,
}
def mk(base, rot=None, **kw):
    p = copy.deepcopy(base)
    if rot: p["rot"].update(rot)
    for k, v in kw.items():
        if k in ("foot", "ep"): p[k].update(v)
        else: p[k] = v
    return p

def apply(p):
    for b in pb:
        b.location = (0, 0, 0); b.rotation_euler = (0, 0, 0); b.rotation_quaternion = (1, 0, 0, 0); b.scale = (1, 1, 1)
    for n, e in p["rot"].items(): pb[n].rotation_euler = [math.radians(a) for a in e]
    pb["Root"].location = loc_of("Root", p["root"])
    pb["Root"].rotation_euler = (0, math.radians(p["spin"]), 0)
    sx, sup, sy = p["scale"]; pb["Root"].scale = (sx, sup, sy)
    pb["Hips"].location = loc_of("Hips", p["hips"])
    for s, (o, e) in p["foot"].items():
        pb["FootCtrl." + s].location = loc_of("FootCtrl." + s, o)
        pb["FootCtrl." + s].rotation_quaternion = quat_of("FootCtrl." + s, e)
    for s, o in p["ep"].items(): pb["ElbowPole." + s].location = loc_of("ElbowPole." + s, o)
    arm.pose.bones["LowerArm.L"].constraints["IK"].influence = p["ikL"]
    arm.pose.bones["Hand.L"].constraints["Copy Rotation"].influence = p["ikL"]
    bpy.context.view_layer.update()
    if p["bat"]:
        h_local, dvec = p["bat"]
        C = pb["Chest"].matrix @ bones["Chest"].matrix_local.inverted()
        H = C @ Vector(h_local); d = (C.to_3x3() @ Vector(dvec)).normalized() if p.get("batChest") else Vector(dvec).normalized()
        sm = (pb["UpperArm.L"].head + pb["UpperArm.R"].head) / 2
        v = sm - H; bz = -(v - d * v.dot(d)).normalized()
        knob = H - d * 0.075
        M = Matrix((d.cross(bz), d, bz)).transposed().to_4x4(); M.translation = knob
        pb["BatCtrl"].matrix = M
        bpy.context.view_layer.update()
        for s in "LR":
            if s == "L" and p["ikL"] < 1: continue
            clav(s)
    # the wrist chain hangs straight down whatever the arm does
    cb = pb["Chain.R"]; M = cb.matrix.copy(); sc_ = M.to_scale()
    y = Vector((0, 0, -1)); z = Vector((0, -1, 0)); x = y.cross(z)
    N = Matrix((x * sc_.x, y * sc_.y, z * sc_.z)).transposed().to_4x4(); N.translation = M.translation
    cb.matrix = N; bpy.context.view_layer.update()
    worst = 0
    for s in "LR":
        if s == "L" and p["ikL"] < 1: continue
        worst = max(worst, (pb["Grip." + s].head - pb["UpperArm." + s].head).length / ARM_REACH[s])
    return worst

def clav(s):
    """Clavicle assist: the chibi arms are short, so roll the shoulder forward/up just enough
    for the hand to reach its grip (smallest rotation that gets within 95% of the arm length)."""
    sb = pb["Shoulder." + s]; M = sb.matrix.copy(); Lb = bones["Shoulder." + s].length
    g = pb["Grip." + s].head.copy(); base = Vector(sb.rotation_euler)
    best = None
    for x in range(0, 65, 5):
        for z in range(-40, 45, 5):
            E = Euler((math.radians(x), 0, math.radians(z))).to_matrix().to_4x4()
            d = (g - M @ E @ Vector((0, Lb, 0))).length / ARM_REACH[s]
            cost = max(0, d - 0.95) * 100 + (abs(x) + abs(z)) * 0.001
            if best is None or cost < best[0]: best = (cost, x, z)
    _, x, z = best
    if x or z:
        sb.rotation_euler = (base.x + math.radians(x), base.y, base.z + math.radians(z))
        bpy.context.view_layer.update()

KEYED = [b.name for b in pb if not b.name.startswith(("KneePole", "Grip."))]
PREVQ = {}
def key(act, f, p, interp=None):
    """Pose with no action bound (so the depsgraph can't flush animated values over the pose),
    then write the snapshot straight into the action's F-curves."""
    arm.animation_data.action = None
    reach = apply(p)
    vals = []
    for n in KEYED:
        b = pb[n]; base = f'pose.bones["{n}"].'
        rp = "rotation_quaternion" if b.rotation_mode == "QUATERNION" else "rotation_euler"
        for prop in ("location", rp, "scale"):
            vec = list(getattr(b, prop))
            if prop == "rotation_quaternion":
                q = Quaternion(vec); last = PREVQ.get(n)
                if last is not None and q.dot(last) < 0: q.negate()
                PREVQ[n] = q; vec = list(q)
            for i, v in enumerate(vec): vals.append((base + prop, i, v, n))
    for bn, cn in (("LowerArm.L", "IK"), ("Hand.L", "Copy Rotation")):
        vals.append((f'pose.bones["{bn}"].constraints["{cn}"].influence', 0, pb[bn].constraints[cn].influence, None))
    arm.animation_data.action = act
    for path, i, v, grp in vals:
        fc = act.fcurves.find(path, index=i) or act.fcurves.new(path, index=i, action_group=grp or "")
        k = fc.keyframe_points.insert(f, v, options={"FAST"})
        k.interpolation = interp or "BEZIER"
    if reach > 0.97: print(f"  REACH frame {f}: {reach:.2f} of arm length")

# ---------------------------------------------------------------- poses
STANCE = mk(BASE,
    rot={"Hips": (0, -15, 0), "Spine": (6, -8, 0), "Chest": (8, -12, 0), "Neck": (0, 10, 0), "Head": (-6, 38, 4),
         "Tail1": (0, 0, -15), "Tail2": (0, 0, -10), "Tail3": (0, 0, -10), "Ear.L": (-8, 0, 0), "Ear.R": (-8, 0, 0)},
    hips=(0, 0.01, -0.07),
    foot={"L": ((0.10, -0.02, 0), (0, 0, 18)), "R": ((-0.10, 0.03, 0), (0, 0, -22))},
    ep={"R": (-0.20, 0.0, 0.22), "L": (0.05, 0, -0.25)},
    bat=((-0.14, -0.36, 0.8), (-0.35, 0.30, 0.89)))

def bob(p, down, sw):
    """Idle beat: down = squash on the beat, up = stretch between. sw = +/-1 swish side."""
    r = {"Tail1": (sw * 14, 0, -15), "Tail2": (sw * 10, 0, -10), "Tail3": (sw * 8, 0, -12),
         "Head": (-6 + (4 if down else -3), 38, 4 + sw * 3)}
    if down:
        return mk(p, r, scale=(1.05, 0.93, 1.05), hips=(0, 0.01, -0.10),
                  bat=((-0.14, -0.36, 0.77), (-0.40, 0.32, 0.86)))
    return mk(p, r, scale=(0.98, 1.03, 0.98), hips=(0, 0.01, -0.055),
              bat=((-0.14, -0.36, 0.82), (-0.28, 0.26, 0.92)))

COIL = mk(STANCE,
    rot={"Hips": (0, -46, -6), "Spine": (4, -16, -4), "Chest": (6, -30, -4), "Neck": (0, 22, 0), "Head": (-4, 70, 6),
         "Tail1": (0, 0, -30), "Tail2": (0, 0, -20), "Tail3": (0, 0, -20), "Ear.L": (-28, 0, 0), "Ear.R": (-28, 0, 0)},
    hips=(-0.06, 0.02, -0.06), scale=(0.98, 1.04, 0.98),
    foot={"L": ((0.05, -0.06, 0.15), (25, 0, 30))},
    bat=((-0.20, -0.33, 0.86), (-0.62, 0.62, 0.48)))
DIP = mk(STANCE, scale=(1.07, 0.9, 1.07), hips=(0, 0.01, -0.11), rot={"Ear.L": (8, 0, 0), "Ear.R": (8, 0, 0)},
         bat=((-0.14, -0.36, 0.75), (-0.42, 0.34, 0.84)))
STRIDE = mk(COIL, rot={"Hips": (0, -26, 0), "Chest": (6, -26, -2)},
            foot={"L": ((0.22, -0.03, 0.0), (0, 0, 20))}, hips=(0.02, 0.02, -0.08), scale=(1.03, 0.96, 1.03))
CONTACT = mk(STANCE,
    rot={"Hips": (0, 42, 0), "Spine": (4, 12, 6), "Chest": (4, 28, 8), "Neck": (0, -10, 0), "Head": (-4, -18, 0),
         "Tail1": (0, 0, 20), "Tail2": (0, 0, 15), "Tail3": (0, 0, 15), "Ear.L": (-30, 0, -20), "Ear.R": (-30, 0, 20)},
    hips=(0.07, 0.0, -0.09), scale=(1.08, 0.95, 0.96),
    foot={"L": ((0.22, -0.03, 0.0), (0, 0, 20)), "R": ((-0.08, 0.02, 0.05), (35, 0, 35))},
    ep={"R": (0.0, 0.1, -0.2), "L": (0.2, 0.1, 0.0)},
    bat=((0.0, -0.37, 0.78), (0.15, -0.98, 0.06)))
FOLLOW = mk(CONTACT,
    rot={"Hips": (0, 58, 0), "Spine": (0, 12, 6), "Chest": (0, 34, 8), "Head": (-6, -40, 0),
         "Tail1": (0, 0, 35), "Tail2": (0, 0, 25), "Tail3": (0, 0, 25), "Ear.L": (15, 0, -35), "Ear.R": (15, 0, 35)},
    hips=(0.08, 0.0, -0.06), scale=(0.96, 1.06, 0.96),
    foot={"R": ((-0.06, 0.0, 0.07), (45, 0, 60))},
    ep={"R": (0.1, 0.0, 0.2), "L": (0.25, 0.0, 0.25)},
    bat=((0.08, -0.34, 0.88), (0.55, 0.62, 0.56)))
OVER = mk(FOLLOW, rot={"Hips": (0, 70, 0), "Chest": (0, 42, 10), "Ear.L": (25, 0, -20), "Ear.R": (25, 0, 20),
                       "Tail1": (0, 0, 10), "Tail2": (0, 0, 0)},
          bat=((0.09, -0.32, 0.89), (0.25, 0.80, 0.54)))
FINISH = mk(FOLLOW, rot={"Hips": (0, 52, 0), "Chest": (0, 32, 6), "Head": (-8, -38, 0), "Ear.L": (-5, 0, -8), "Ear.R": (-5, 0, 8),
                         "Tail1": (0, 0, 22), "Tail2": (0, 0, 14), "Tail3": (0, 0, 10)},
            scale=(1, 1, 1), hips=(0.07, 0, -0.06),
            bat=((0.08, -0.34, 0.86), (0.35, 0.70, 0.62)))
SETTLE = mk(FINISH, scale=(1.03, 0.96, 1.03), hips=(0.07, 0, -0.08))

# whiff: over-spin like a top, dizzy wobble, then deflate
WHIFF_THRU = mk(CONTACT, bat=((0.0, -0.37, 0.75), (0.2, -0.93, -0.2)), scale=(1.02, 1.0, 1.02))
SPIN1 = mk(OVER, spin=130, root=(0, 0, 0.12), scale=(0.94, 1.1, 0.94),
           foot={"L": ((0.12, -0.02, 0.06), (0, 0, 0)), "R": ((-0.08, 0.02, 0.10), (20, 0, 0))})
SPIN2 = mk(SPIN1, spin=290, root=(0, 0, 0.10), rot={"Hips": (0, 30, 0), "Chest": (0, 20, 0)})
LAND = mk(STANCE, spin=360, scale=(1.12, 0.84, 1.12), hips=(0, 0, -0.13),
          rot={"Hips": (0, 10, 0), "Chest": (10, 5, 0), "Head": (10, 0, 0), "Ear.L": (30, 0, -25), "Ear.R": (30, 0, 25)},
          foot={"L": ((0.10, -0.02, 0), (0, 0, 18)), "R": ((-0.10, 0.03, 0), (0, 0, -22))},
          bat=((-0.02, -0.37, 0.78), (0.1, -0.7, -0.7)))
def wob(s, spin):
    return mk(LAND, spin=spin, scale=(1, 1, 1), hips=(0, 0, -0.08),
              rot={"Spine": (4, 0, 10 * s), "Chest": (6, 0, 10 * s), "Neck": (0, 0, 8 * s), "Head": (4, 0, 14 * s),
                   "Ear.L": (-20, 0, -15 + 20 * s), "Ear.R": (-20, 0, 15 + 20 * s), "Tail1": (0, 0, -10 * s)})
SLUMP = mk(LAND, spin=360, scale=(1.03, 0.96, 1.03), hips=(0, 0.02, -0.09),
           rot={"Hips": (0, 0, 0), "Spine": (18, 0, 0), "Chest": (12, 0, 0), "Neck": (12, 0, 0), "Head": (22, 0, 0),
                "Ear.L": (-55, 0, -35), "Ear.R": (-55, 0, 35), "Tail1": (0, 0, -45), "Tail2": (0, 0, -20), "Tail3": (0, 0, -10)},
           bat=((-0.04, -0.35, 0.7), (0.05, -0.35, -0.94)))
SIGH = mk(SLUMP, rot={"Spine": (12, 0, 0), "Head": (14, 0, 0)}, hips=(0, 0.02, -0.075), scale=(0.99, 1.02, 0.99))

# home run: release twist, crouch, big jump with the bat to the sky, fist pumps
FIST_CHEST = {"UpperArm.L": (75, 0, -20), "LowerArm.L": (125, 0, 0), "Hand.L": (0, 0, 0)}
FIST_UP = {"UpperArm.L": (15, 0, 130), "LowerArm.L": (20, 0, 0)}
HR_CROUCH = mk(STANCE, rot={"Hips": (0, 5, 0), "Spine": (14, 0, 0), "Chest": (10, 0, 0), "Head": (-10, 0, 0), "Neck": (0, 0, 0),
                            "Ear.L": (15, 0, 0), "Ear.R": (15, 0, 0), "Tail1": (0, 0, -30), **FIST_CHEST},
               scale=(1.12, 0.85, 1.12), hips=(0, 0, -0.15), ikL=0.0,
               foot={"L": ((0.09, -0.02, 0), (0, 0, 15)), "R": ((-0.09, 0.02, 0), (0, 0, -15))},
               bat=((-0.14, -0.34, 0.84), (-0.25, -0.15, 0.96)))
HR_LAUNCH = mk(HR_CROUCH, rot={"Spine": (-8, 0, 0), "Chest": (-10, 0, 0), "Head": (-18, 0, 0),
                               "Ear.L": (40, 0, -10), "Ear.R": (40, 0, 10), "Tail1": (0, 0, 30), "Tail2": (0, 0, 25), **FIST_UP},
               scale=(0.9, 1.16, 0.9), root=(0, 0, 0.30), hips=(0, 0, -0.02),
               foot={"L": ((0.08, 0.02, 0.10), (-20, 0, 10)), "R": ((-0.08, 0.02, 0.10), (-20, 0, -10))},
               bat=((-0.30, -0.12, 1.20), (-0.50, 0.0, 0.87)))
HR_APEX = mk(HR_LAUNCH, scale=(1, 1, 1), root=(0, 0, 0.40), rot={"Ear.L": (-10, 0, -30), "Ear.R": (-10, 0, 30),
                                                                   "Tail1": (20, 0, 30), "Tail2": (20, 0, 20)},
             foot={"L": ((0.10, 0.06, 0.22), (-35, 0, 10)), "R": ((-0.10, 0.06, 0.22), (-35, 0, -10))}, hips=(0, 0, -0.04))
HR_LAND = mk(HR_CROUCH, rot={**FIST_CHEST, "Ear.L": (35, 0, -20), "Ear.R": (35, 0, 20), "Tail1": (0, 0, 10)},
             bat=((-0.28, -0.14, 1.12), (-0.55, 0.0, 0.83)))
HR_UP = mk(HR_LAUNCH, root=(0, 0, 0), scale=(0.97, 1.04, 0.97), hips=(0, 0, -0.05),
           foot={"L": ((0.09, -0.02, 0), (0, 0, 15)), "R": ((-0.09, 0.02, 0), (0, 0, -15))},
           rot={"Ear.L": (-5, 0, -10), "Ear.R": (-5, 0, 10), "Tail1": (-25, 0, 20), "Tail2": (-20, 0, 15)})
HR_PUMP = mk(HR_UP, rot={**FIST_CHEST, "Spine": (10, 0, 0), "Head": (-4, 0, 0), "Ear.L": (20, 0, -5), "Ear.R": (20, 0, 5),
                         "Tail1": (25, 0, 20), "Tail2": (20, 0, 15)},
             scale=(1.05, 0.93, 1.05), hips=(0, 0, -0.09), bat=((-0.28, -0.14, 1.10), (-0.58, 0.0, 0.81)))

CLIPS = {
    # name: (frames, loop, [(frame, pose, interp)], markers)
    "Idle": (24, True, [(0, bob(STANCE, True, 1), None), (6, bob(STANCE, False, 0), None),
                        (12, bob(STANCE, True, -1), None), (18, bob(STANCE, False, 0), None),
                        (24, bob(STANCE, True, 1), None)], {"beats": 2}),
    "Coil": (14, False, [(0, bob(STANCE, True, 1), None), (3, DIP, None), (9, COIL, None),
                         (14, mk(COIL, hips=(-0.06, 0.02, -0.07)), None)], {"holdFrom": 9}),
    "Swing": (22, False, [(0, mk(COIL, hips=(-0.06, 0.02, -0.07)), "LINEAR"), (1, STRIDE, "LINEAR"), (3, CONTACT, None),
                          (6, FOLLOW, None), (9, OVER, None), (14, SETTLE, None), (22, FINISH, None)], {"contact": 3}),
    "Whiff": (40, False, [(0, mk(COIL, hips=(-0.06, 0.02, -0.07)), "LINEAR"), (1, STRIDE, "LINEAR"), (3, WHIFF_THRU, "LINEAR"),
                          (6, SPIN1, "LINEAR"), (10, SPIN2, None), (13, LAND, None), (17, wob(1, 352), None),
                          (21, wob(-1, 366), None), (26, SLUMP, None), (33, SIGH, None), (40, SLUMP, None)], {"contact": 3}),
    "HomeRun": (48, False, [(0, FINISH, None), (5, HR_CROUCH, None), (9, HR_LAUNCH, None), (14, HR_APEX, None),
                            (19, HR_LAND, None), (23, HR_UP, None), (28, HR_PUMP, None), (32, HR_UP, None),
                            (36, HR_PUMP, None), (40, HR_UP, None), (48, HR_UP, None)], {"loopFrom": 28, "loopTo": 36}),
}

meta = {}
for name, (n, loop, keys, marks) in CLIPS.items():
    if only and name not in only: continue
    act = bpy.data.actions.new(name + "_src"); act.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = act
    print("CLIP", name); PREVQ.clear()
    for f, p, ip in keys: key(act, f, p, ip)
    for fc in act.fcurves: fc.update()
    if loop:
        for fc in act.fcurves:
            fc.modifiers.new("CYCLES")
    meta[name] = {"frames": n, "fps": 24, "loop": loop, **marks}
    act["meta"] = json.dumps(meta[name])
bpy.ops.object.mode_set(mode="OBJECT")
bpy.ops.wm.save_as_mainfile(filepath=out)
print("ANIM SAVED", json.dumps(meta))
