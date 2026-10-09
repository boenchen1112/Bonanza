# blender -b --factory-startup --python import_rigged.py -- <rigged.glb> <out.blend> <tex_dir> [scale]
# Brings an auto-rigged GLB (Unity Humanoid bone names, T-pose, facing +X) into this pipeline's
# conventions, so animrig.py / anim.py / export_fbx.py run on it:
#   - faces -Y with its left at +X, hips over the origin, feet on the ground; `scale` (default
#     1.9) keeps arm reach and leg length equal to the body the clips were keyed on
#   - mesh welded (the GLB stores every triangle with its own vertices), one opaque material
#   - bones named UpperArm.L / LowerLeg.R ..., roll GLOBAL_NEG_Y (local X is the hinge)
#   - rest pose changed from T-pose to the A-pose the clips expect, fingers closed to a grip;
#     finger, toe-end and neck-twist weights folded into their parents and those bones removed
#   - Tail1-3 and Ear.L/R bones added (the auto-rigger has none) and weighted
# Objects are named BatterRig and Batter.
import sys, os, math, re, bpy, bmesh
from mathutils import Vector, Matrix, Quaternion
args = sys.argv[sys.argv.index("--") + 1:]
src, out, tex = args[:3]; SCALE = float(args[3]) if len(args) > 3 else 1.9
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
sc = bpy.context.scene
arm = next(o for o in sc.objects if o.type == "ARMATURE")
body = next(o for o in sc.objects if o.type == "MESH" and any(m.type == "ARMATURE" for m in o.modifiers))
for o in list(sc.objects):
    if o not in (arm, body): bpy.data.objects.remove(o, do_unlink=True)
def activate(o, mode="OBJECT"):
    if bpy.context.object and bpy.context.object.mode != "OBJECT": bpy.ops.object.mode_set(mode="OBJECT")
    for x in sc.objects: x.select_set(False)
    bpy.context.view_layer.objects.active = o; o.select_set(True)
    if mode != "OBJECT": bpy.ops.object.mode_set(mode=mode)

# one transform for both: hips over the origin, turn to face -Y, scale; then identity objects
aw = arm.matrix_world.copy(); bw = body.matrix_world.copy()
hips = aw @ arm.data.bones["Hips"].head_local
T = Matrix.Scale(SCALE, 4) @ Matrix.Rotation(math.radians(-90), 4, "Z") @ Matrix.Translation((-hips.x, -hips.y, 0))
body.parent = None; arm.parent = None
body.data.transform(T @ bw); arm.data.transform(T @ aw)
body.matrix_world = Matrix.Identity(4); arm.matrix_world = Matrix.Identity(4)
zmin = min(v.co.z for v in body.data.vertices)
body.data.transform(Matrix.Translation((0, 0, -zmin))); arm.data.transform(Matrix.Translation((0, 0, -zmin)))
body.parent = arm
arm.name = "BatterRig"; arm.data.name = "BatterRig"; body.name = "Batter"; body.data.name = "Batter"
me = body.data

n0 = len(me.vertices)
activate(body, "EDIT"); bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.remove_doubles(threshold=1e-5 * SCALE)
activate(body)
print("WELD", n0, "->", len(me.vertices))

def rename(n):
    m = re.match(r"(Left|Right)_(.+)", n)
    return f"{m.group(2)}.{m.group(1)[0]}" if m else n
for b in arm.data.bones: b.name = rename(b.name)          # vertex groups follow
vg = body.vertex_groups
def weights(v): return {vg[g.group].name: g.weight for g in v.groups if g.weight > 0}
def set_weights(i, w):
    for g in list(me.vertices[i].groups): vg[g.group].remove([i])
    top = sorted(w.items(), key=lambda t: -t[1])[:4]; tot = sum(x for _, x in top)
    for n, x in top:
        if x > 0: (vg.get(n) or vg.new(name=n)).add([i], x / tot, "REPLACE")

# ---------------------------------------------------------------- tail and ears
bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
def island(seed):
    seen = {seed}; st = [bm.verts[seed]]
    while st:
        a = st.pop()
        for e in a.link_edges:
            b = e.other_vert(a)
            if b.index not in seen: seen.add(b.index); st.append(b)
    return sorted(seen)
co = [v.co.copy() for v in me.vertices]
tail = island(max(range(len(co)), key=lambda i: co[i].y))
bm.free()
B = arm.data.bones
hip = B["Hips"].head_local
base = min((co[i] for i in tail), key=lambda p: (p - hip).length)
dist = {i: (co[i] - base).length for i in tail}; far = max(dist.values())
def station(f):
    pts = [co[i] for i in tail if abs(dist[i] / far - f) < 0.07]
    return sum(pts, Vector()) / len(pts)
tj = [station(0.07), station(1 / 3), station(2 / 3), co[max(tail, key=lambda i: dist[i])]]
def lerp01(x, a, b): return max(0.0, min(1.0, (x - a) / (b - a)))
for i in tail:
    u = dist[i] / far * 3
    w = {"Hips": 1 - lerp01(u, 0.0, 0.35)}
    w["Tail1"] = (1 - w["Hips"]) * (1 - lerp01(u, 0.75, 1.25))
    w["Tail3"] = lerp01(u, 1.75, 2.25)
    w["Tail2"] = max(0.0, 1 - w["Hips"] - w["Tail1"] - w["Tail3"])
    set_weights(i, w)
print("TAIL verts", len(tail), "joints", [tuple(round(c, 2) for c in p) for p in tj])

ej = {}
headz = B["Head"].head_local.z
for s, sign in (("L", 1), ("R", -1)):
    side = [i for i in range(len(co)) if co[i].x * sign > 0.05 and co[i].z > headz + 0.3]
    tip = co[max(side, key=lambda i: co[i].z)]
    ej[s] = [None, tip]
tipz = (ej["L"][1].z + ej["R"][1].z) / 2
for s, sign in (("L", 1), ("R", -1)):
    tip = ej[s][1]
    # root of the ear: 0.30 m below the tip, leaning in toward the head
    root = Vector((tip.x - sign * 0.07, tip.y - 0.02, tip.z - 0.30)); ej[s][0] = root
    ax = tip - root; L = ax.length; ax.normalize(); n = 0
    for i in range(len(co)):
        p = co[i] - root; t = p.dot(ax) / L
        if t <= 0.12 or co[i].x * sign < 0.03: continue
        r = (p - ax * (t * L)).length
        f = lerp01(t, 0.12, 0.45) * (1 - lerp01(r, 0.13, 0.17))
        w = weights(me.vertices[i]); hw = w.get("Head", 0)
        if f <= 0 or hw <= 0: continue
        w["Head"] = hw * (1 - f); w["Ear." + s] = hw * f; set_weights(i, w); n += 1
    print("EAR", s, "verts", n, "root", tuple(round(c, 2) for c in root), "tip", tuple(round(c, 2) for c in tip))

activate(arm, "EDIT")
eb = arm.data.edit_bones
for b in eb: b.use_connect = False
for k in range(3):
    b = eb.new(f"Tail{k + 1}"); b.head = tj[k]; b.tail = tj[k + 1]; b.parent = eb[f"Tail{k}" if k else "Hips"]
for s in "LR":
    b = eb.new("Ear." + s); b.head = ej[s][0].lerp(ej[s][1], 0.12); b.tail = ej[s][1]; b.parent = eb["Head"]

# ---------------------------------------------------------------- rest pose: A-pose arms, closed hands
activate(arm, "POSE")
pb = arm.pose.bones
def turn(b, q):
    h = b.head.copy()
    b.matrix = Matrix.Translation(h) @ q.to_matrix().to_4x4() @ Matrix.Translation(-h) @ b.matrix
    bpy.context.view_layer.update()
def aim(n, d): b = pb[n]; turn(b, b.matrix.col[1].xyz.rotation_difference(Vector(d).normalized()))
Y = Vector((0, 1, 0)); X = Vector((1, 0, 0))
for s, sign in (("L", 1), ("R", -1)):
    for f in ("Index", "Middle", "Ring", "Pinky"):
        for seg, deg in (("Proximal", 70), ("Intermediate", 80), ("Distal", 45)):
            turn(pb[f"{f}{seg}.{s}"], Quaternion(Y, math.radians(deg * sign)))
    for seg, deg in (("Proximal", 35), ("Intermediate", 30), ("Distal", 30)):
        turn(pb[f"Thumb{seg}.{s}"], Quaternion(X, math.radians(deg)))
    aim("UpperArm." + s, (0.110 * sign, 0.064, -0.132))
    aim("LowerArm." + s, (0.108 * sign, 0.009, -0.236))
    aim("Hand." + s, (0.03 * sign, 0.0, -0.093))
activate(body)
mod = next(m for m in body.modifiers if m.type == "ARMATURE")
bpy.ops.object.modifier_apply(modifier=mod.name)
activate(arm, "POSE"); bpy.ops.pose.armature_apply(selected=False)
activate(arm)
mod = body.modifiers.new("Armature", "ARMATURE"); mod.object = arm

# ---------------------------------------------------------------- fold helper bones into their parents
def fold(into, pred):
    names = [b.name for b in arm.data.bones if pred(b.name)]
    for n in names:
        g = vg.get(n)
        if not g: continue
        dst = vg.get(into) or vg.new(name=into)
        for v in me.vertices:
            for e in v.groups:
                if e.group == g.index and e.weight > 0: dst.add([v.index], e.weight, "ADD")
        vg.remove(g)
    return names
gone = []
for s in "LR":
    gone += fold("Hand." + s, lambda n: n.endswith("." + s) and n.startswith(("Thumb", "Index", "Middle", "Ring", "Pinky")))
    gone += fold("Toes." + s, lambda n: n == "ToesEnd." + s)
gone += fold("Neck", lambda n: n.startswith("Neck_Twist"))
gone += fold("Head", lambda n: n.startswith("Eye"))
activate(arm, "EDIT")
eb = arm.data.edit_bones
for n in gone: eb.remove(eb[n])
up = Vector((0, 0, 1))
for n in ("Hips", "Spine", "Chest", "UpperChest", "Neck", "Head"):
    b = eb[n]; kid = next((c for c in b.children if c.name in ("Spine", "Chest", "UpperChest", "Neck", "Head")), None)
    b.tail = Vector((b.head.x, kid.head.y, kid.head.z)) if kid else b.head + up * b.length
eb["Hips"].head.x = 0; eb["Hips"].tail.x = 0
for s in "LR":
    # knees a little in front of the hip-ankle line, so leg IK has a bend plane
    u, l = eb["UpperLeg." + s], eb["LowerLeg." + s]
    a, c = u.head.copy(), l.tail.copy(); t = (l.head - a).dot(c - a) / (c - a).length_squared
    k = a.lerp(c, t); k.y = min(l.head.y, k.y - 0.008 * SCALE); u.tail = k; l.head = k
    eb["LowerArm." + s].head = eb["UpperArm." + s].tail; eb["Hand." + s].head = eb["LowerArm." + s].tail
    h = eb["Hand." + s]; h.tail = h.head + (h.tail - h.head).normalized() * 0.09
for b in eb: b.select = True; b.select_head = True; b.select_tail = True
bpy.ops.armature.calculate_roll(type="GLOBAL_NEG_Y")
activate(arm)
for v in me.vertices: set_weights(v.index, weights(v))
for g in [g for g in vg if g.name not in arm.data.bones]: vg.remove(g)
print("BONES", len(arm.data.bones), sorted(b.name for b in arm.data.bones))

# ---------------------------------------------------------------- one opaque material, texture next to the blend
os.makedirs(tex, exist_ok=True)
mat = body.material_slots[0].material; mat.name = "Batter"; mat.blend_method = "OPAQUE"
bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
bsdf.inputs["Roughness"].default_value = 0.8; bsdf.inputs["Metallic"].default_value = 0.0
if bsdf.inputs["Alpha"].is_linked: mat.node_tree.links.remove(bsdf.inputs["Alpha"].links[0])
img = next(n.image for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image)
img.name = "Batter_BaseColor"; p = os.path.join(tex, "Batter_BaseColor.png")
img.filepath_raw = p; img.file_format = "PNG"; img.save()
if img.packed_file: img.unpack(method="REMOVE")
img.filepath = p
bpy.ops.wm.save_as_mainfile(filepath=out, relative_remap=True)
B = arm.data.bones
print("DONE height", round(max(v.co.z for v in me.vertices), 3), "shoulder", round(B["UpperArm.L"].head_local.z, 3),
      "reach", round(B["UpperArm.L"].length + B["LowerArm.L"].length, 3),
      "leg", round(B["UpperLeg.L"].length + B["LowerLeg.L"].length, 3), "tris", len(me.polygons))
