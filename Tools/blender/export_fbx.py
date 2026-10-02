# blender -b --python export_fbx.py -- <batter_anim.blend> <out.fbx> <clips.json> <baked.blend> <bpm>
# Bakes the hand-keyed control-rig clips to plain FK keys on the deform skeleton, strips the
# control bones and constraints, and exports mesh + skeleton + every clip as one FBX for Unity.
import sys, os, json, bpy
src, fbx, side, baked, bpm = sys.argv[sys.argv.index("--") + 1:][:5]
bpm = float(bpm)
bpy.ops.wm.open_mainfile(filepath=src)
sc = bpy.context.scene; sc.render.fps = 24
arm = bpy.data.objects["BatterRig"]
CONTROLS = ("FootCtrl.", "KneePole.", "ElbowPole.", "BatCtrl", "Grip.")
for o in sc.objects: o.select_set(False)
bpy.context.view_layer.objects.active = arm; arm.select_set(True)
bpy.ops.object.mode_set(mode="POSE")

srcs = [a for a in bpy.data.actions if a.name.endswith("_src")]
meta = {}
for act in srcs:
    name = act.name[:-4]; m = json.loads(act["meta"])
    arm.animation_data.action = act
    bpy.ops.nla.bake(frame_start=0, frame_end=m["frames"], step=1, only_selected=False, visual_keying=True,
                     clear_constraints=False, clear_parents=False, use_current_action=False, bake_types={"POSE"})
    out = arm.animation_data.action; out.name = name; out.use_fake_user = True; out["meta_baked"] = 1
    # constraint-influence and control-bone curves have no meaning without the control rig
    for fc in list(out.fcurves):
        if "constraints[" in fc.data_path or any(f'bones["{c}' in fc.data_path for c in CONTROLS):
            out.fcurves.remove(fc)
    m["seconds"] = round(m["frames"] / 24, 4)
    if "contact" in m: m["contactSeconds"] = round(m["contact"] / 24, 4)
    meta[name] = m
    print("BAKED", name, len(out.fcurves), "curves")
for a in srcs: bpy.data.actions.remove(a)

for b in arm.pose.bones:
    for c in list(b.constraints): b.constraints.remove(c)
bpy.ops.object.mode_set(mode="EDIT")
for eb in list(arm.data.edit_bones):
    if eb.name.startswith(CONTROLS): arm.data.edit_bones.remove(eb)
bpy.ops.object.mode_set(mode="OBJECT")
print("BONES", len(arm.data.bones), sorted(b.name for b in arm.data.bones))

first = next(a for a in bpy.data.actions if a.get("meta_baked"))
arm.animation_data.action = first
for o in sc.objects: o.select_set(o.name in ("BatterRig", "Batter", "Bat"))
os.makedirs(os.path.dirname(fbx), exist_ok=True)
bpy.ops.export_scene.fbx(
    filepath=fbx, use_selection=True, object_types={"ARMATURE", "MESH"},
    apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
    add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
    mesh_smooth_type="FACE", use_tspace=True, path_mode="STRIP",
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0)
beat = 60.0 / bpm
doc = {
    "note": "Batter clips, hand-keyed pose-to-pose in Tools/blender/anim.py. Import as Generic (tail, ears, "
            "bat and Root squash/stretch are not Humanoid). Frames are at 24 fps.",
    "fps": 24, "bpm": bpm,
    "idleSpeedAtBpm": round((meta["Idle"]["beats"] * beat) and meta["Idle"]["seconds"] / (meta["Idle"]["beats"] * beat), 4),
    "clips": meta,
}
with open(side, "w", encoding="utf-8") as f: json.dump(doc, f, indent=2)
bpy.ops.wm.save_as_mainfile(filepath=baked)
print("EXPORTED", fbx, os.path.getsize(fbx))
