# blender -b --python decimate2.py -- <orig.blend> <out.blend> <render_dir> <target_tris> <weld_dist>
import sys, os, math, bpy, bmesh
src, out_blend, rdir, target, weld = sys.argv[sys.argv.index("--") + 1:][:5]
target = int(target); weld = float(weld)
os.makedirs(rdir, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=src)
obj = next(o for o in bpy.context.scene.objects if o.type == "MESH")
bpy.context.view_layer.objects.active = obj; obj.select_set(True)

bm = bmesh.new(); bm.from_mesh(obj.data)
v0 = len(bm.verts)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=weld)
bnd = sum(1 for e in bm.edges if e.is_boundary)
print("WELD", v0, "->", len(bm.verts), "boundary edges", bnd)
bm.to_mesh(obj.data); bm.free(); obj.data.update()

before = len(obj.data.polygons)
mod = obj.modifiers.new("Decimate", "DECIMATE")
mod.decimate_type = "COLLAPSE"
mod.ratio = target / before
bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
m = obj.data
print("DECIMATED", before, "->", len(m.polygons), "tris", sum(len(p.vertices) - 2 for p in m.polygons), "verts", len(m.vertices))
bpy.ops.wm.save_as_mainfile(filepath=out_blend)

sc = bpy.context.scene
sc.render.engine = "BLENDER_WORKBENCH"
sc.display.shading.light = "STUDIO"
sc.display.shading.color_type = "TEXTURE"
sc.render.resolution_x, sc.render.resolution_y = 900, 1200
h = max(v.co.z for v in m.vertices)
cam = sc.camera
if cam is None:
    cd = bpy.data.cameras.new("Cam"); cam = bpy.data.objects.new("Cam", cd)
    sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = "ORTHO"

def shot(name, yaw_deg, scale, z):
    a = math.radians(yaw_deg)
    cam.data.ortho_scale = scale
    cam.location = (math.sin(a) * 5, math.cos(a) * -5, z)
    cam.rotation_euler = (math.radians(90), 0, a)
    sc.render.filepath = os.path.join(rdir, name + ".png")
    bpy.ops.render.render(write_still=True)

shot("front", 0, h * 1.1, h / 2)
shot("side", 90, h * 1.1, h / 2)
shot("back", 180, h * 1.1, h / 2)
shot("face", 0, h * 0.4, h * 0.68)
shot("torso", 0, h * 0.45, h * 0.45)
