# blender -b --python clipstrip.py -- <blend> <out_dir> <workbench|eevee> <video 0|1> [clip ...]
# One contact-sheet PNG per action (every 2nd frame, 3/4 front view from the pitcher side),
# and optionally an MP4 per action.
import sys, os, math, bpy
import numpy as np
args = sys.argv[sys.argv.index("--") + 1:]
src, odir, eng, video = args[:4]; only = set(args[4:])
os.makedirs(odir, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=src)
sc = bpy.context.scene; sc.render.fps = 24
arm = bpy.data.objects["BatterRig"]
for o in sc.objects:
    if o.type == "ARMATURE": o.hide_render = True
if eng == "eevee":
    try: sc.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError: sc.render.engine = "BLENDER_EEVEE"
    w = sc.world or bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.55, 0.62, 0.7, 1); bg.inputs[1].default_value = 1.0
    if not any(o.type == "LIGHT" for o in sc.objects):
        sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); sc.collection.objects.link(sun)
        sun.data.energy = 3; sun.rotation_euler = (math.radians(50), 0, math.radians(30))
else:
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "TEXTURE"
    w = sc.world or bpy.data.worlds.new("W"); sc.world = w; w.color = (0.55, 0.62, 0.7)
# ground grid line for contact reading
if "Ground" not in bpy.data.objects:
    bpy.ops.mesh.primitive_plane_add(size=3, location=(0, 0, 0)); g = bpy.context.active_object; g.name = "Ground"
    m = bpy.data.materials.new("G"); m.use_nodes = True
    next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED").inputs["Base Color"].default_value = (0.35, 0.55, 0.3, 1)
    m.diffuse_color = (0.35, 0.55, 0.3, 1); g.data.materials.append(m)
if sc.camera is None:
    c = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(c); sc.camera = c
cam = sc.camera; cam.data.type = "ORTHO"; cam.data.ortho_scale = 2.5
ZS, ZZ = [float(v) for v in os.environ.get("ZOOM", "2.5,1.0").split(",")]
cam.data.ortho_scale = ZS
def aim(deg):
    a = math.radians(deg)
    cam.location = (math.sin(a) * 6, -math.cos(a) * 6, ZZ); cam.rotation_euler = (math.radians(90), 0, a)
VIEWS = [float(v) for v in os.environ.get("VIEWS", "35").split(",")]
aim(VIEWS[0])
W, H = [int(v) for v in os.environ.get("SIZE", "440,560").split(",")]
sc.render.resolution_x, sc.render.resolution_y = W, H; sc.render.resolution_percentage = 100
sc.render.film_transparent = False

acts = [a for a in bpy.data.actions if a.name.endswith("_src") or "meta_baked" in a]
for act in acts:
    name = act.name.replace("_src", "")
    if only and name not in only: continue
    arm.animation_data.action = act
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    if os.environ.get("KEYS_ONLY"):
        frames = sorted({int(round(k.co.x)) for fc in act.fcurves for k in fc.keyframe_points})
    else:
        frames = list(range(f0, f1 + 1, 2))
        if frames[-1] != f1: frames.append(f1)
    if os.environ.get("FRAMES"): frames = [int(x) for x in os.environ["FRAMES"].split(",")]
    grid = {}
    for f in frames:
      sc.frame_set(f)
      for vi, vw in enumerate(VIEWS):
        aim(vw)
        p = os.path.join(odir, f"_{name}_{f:03d}.png"); sc.render.filepath = p
        bpy.ops.render.render(write_still=True)
        im = bpy.data.images.load(p); grid[(vi, f)] = np.array(im.pixels[:]).reshape(H, W, 4); bpy.data.images.remove(im); os.remove(p)
    aim(VIEWS[0])
    PER = 6 if len(VIEWS) > 1 else 8
    for part in range(0, len(frames), PER):
        fs = frames[part:part + PER]; rows = len(VIEWS); cols = len(fs)
        sheet = np.ones((rows * H, cols * W, 4)) * 0.15
        for vi in range(rows):
            for c, f in enumerate(fs):
                sheet[(rows - 1 - vi) * H:(rows - vi) * H, c * W:(c + 1) * W] = grid[(vi, f)]
        img = bpy.data.images.new(name + "_sheet", cols * W, rows * H); img.pixels[:] = sheet.ravel()
        img.filepath_raw = os.path.join(odir, f"{name}_sheet{part // PER}.png"); img.file_format = "PNG"; img.save()
    print("SHEET", name, frames)
    if video == "1":
        sc.frame_start, sc.frame_end = f0, f1
        if name == "Idle":
            sc.frame_end = f1 + (f1 - f0) * 2 - 1  # plays the loop 3x
            for fc in act.fcurves:
                if not fc.modifiers: fc.modifiers.new("CYCLES")
        sc.render.image_settings.file_format = "FFMPEG"
        sc.render.ffmpeg.format = "MPEG4"; sc.render.ffmpeg.codec = "H264"; sc.render.ffmpeg.constant_rate_factor = "HIGH"
        sc.render.filepath = os.path.join(odir, name + ".mp4")
        bpy.ops.render.render(animation=True)
        sc.render.image_settings.file_format = "PNG"
        print("VIDEO", name)
print("STRIP DONE")
