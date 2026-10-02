# blender -b --python decimate.py -- <in.glb> <out.blend> <render_dir> <target_tris>
import sys, os, math, bpy
from mathutils import Vector

src, out_blend, rdir, target = sys.argv[sys.argv.index("--") + 1:][:4]
target = int(target)
os.makedirs(rdir, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
obj = next(o for o in bpy.context.scene.objects if o.type == "MESH")
obj.name = "Batter"

mat = obj.active_material
for n in mat.node_tree.nodes:
    if n.type == "TEX_IMAGE":
        links = [f"{l.to_node.type}.{l.to_socket.name}" for l in n.outputs[0].links]
        print("TEX", n.image.name, n.image.colorspace_settings.name, links)

# origin at the feet, centred
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
m = obj.data
xs = [v.co.x for v in m.vertices]; ys = [v.co.y for v in m.vertices]; zs = [v.co.z for v in m.vertices]
shift = Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, min(zs)))
for v in m.vertices:
    v.co -= shift
m.update()

before = len(m.polygons)
mod = obj.modifiers.new("Decimate", "DECIMATE")
mod.decimate_type = "COLLAPSE"
mod.ratio = target / before
bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.ops.object.shade_smooth()
print("DECIMATED", before, "->", len(obj.data.polygons), "verts", len(obj.data.vertices))

bpy.ops.wm.save_as_mainfile(filepath=out_blend)

# previews: workbench, texture colour, orthographic front / side / back / face close-up
sc = bpy.context.scene
sc.render.engine = "BLENDER_WORKBENCH"
sc.display.shading.light = "STUDIO"
sc.display.shading.color_type = "TEXTURE"
sc.render.resolution_x, sc.render.resolution_y = 900, 1200
sc.render.film_transparent = False
world = bpy.data.worlds.new("W"); sc.world = world
h = max(v.co.z for v in obj.data.vertices)
cam_data = bpy.data.cameras.new("Cam"); cam = bpy.data.objects.new("Cam", cam_data)
sc.collection.objects.link(cam); sc.camera = cam
cam_data.type = "ORTHO"

def shot(name, yaw_deg, scale, z):
    a = math.radians(yaw_deg)
    cam_data.ortho_scale = scale
    cam.location = (math.sin(a) * -5, math.cos(a) * -5, z)
    cam.rotation_euler = (math.radians(90), 0, a)
    sc.render.filepath = os.path.join(rdir, name + ".png")
    bpy.ops.render.render(write_still=True)

shot("front", 0, h * 1.1, h / 2)
shot("side", 90, h * 1.1, h / 2)
shot("back", 180, h * 1.1, h / 2)
shot("face", 0, h * 0.28, h * 0.86)
print("HEIGHT", round(h, 3))
