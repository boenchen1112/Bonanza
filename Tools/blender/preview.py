# blender -b --python preview.py -- <file.blend> <render_dir> [eevee|workbench]
import sys, os, math, bpy
args = sys.argv[sys.argv.index("--") + 1:]
src, rdir = args[:2]; mode = args[2] if len(args) > 2 else "eevee"
os.makedirs(rdir, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=src)
sc = bpy.context.scene
mesh = bpy.data.objects["Batter"]
if sc.camera is None:
    cd = bpy.data.cameras.new("Cam"); cam = bpy.data.objects.new("Cam", cd)
    sc.collection.objects.link(cam); sc.camera = cam
cam = sc.camera; cam.data.type = "ORTHO"
sc.render.resolution_x, sc.render.resolution_y = 900, 1200
if mode == "eevee":
    try:
        sc.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        sc.render.engine = "BLENDER_EEVEE"
    w = sc.world or bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.55, 0.55, 0.58, 1); bg.inputs[1].default_value = 1.0
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); sc.collection.objects.link(sun)
    sun.data.energy = 3; sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
    sc.view_settings.view_transform = "Standard"
else:
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "TEXTURE"
h = max((mesh.matrix_world @ v.co).z for v in mesh.data.vertices)
def shot(name, yaw_deg, scale, z):
    a = math.radians(yaw_deg); cam.data.ortho_scale = scale
    cam.location = (math.sin(a) * 5, math.cos(a) * -5, z); cam.rotation_euler = (math.radians(90), 0, a)
    sc.render.filepath = os.path.join(rdir, name + ".png"); bpy.ops.render.render(write_still=True)
shot("front", 0, h * 1.1, h / 2); shot("side", 90, h * 1.1, h / 2); shot("back", 180, h * 1.1, h / 2)
shot("face", 0, h * 0.4, h * 0.68); shot("torso", 0, h * 0.45, h * 0.45)
print("PREVIEW DONE", len(mesh.data.polygons), "faces")
