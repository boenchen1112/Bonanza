# blender -b --python jointview.py -- <rig.blend> <out_dir>
# X-ray renders with a red ball on every joint and a thin stick along every bone.
import sys, os, math, bpy
from mathutils import Vector
src, odir = sys.argv[sys.argv.index("--") + 1:][:2]
os.makedirs(odir, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=src)
sc = bpy.context.scene; arm = bpy.data.objects["BatterRig"]
red = bpy.data.materials.new("J"); red.diffuse_color = (1, 0.05, 0.05, 1)
yel = bpy.data.materials.new("S"); yel.diffuse_color = (1, 0.85, 0.1, 1)
seen = set()
for b in arm.data.bones:
    for p in (b.head_local, b.tail_local):
        k = tuple(round(c, 4) for c in p)
        if k in seen: continue
        seen.add(k)
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.012, location=p, segments=12, ring_count=6)
        bpy.context.active_object.data.materials.append(red)
    d = b.tail_local - b.head_local
    bpy.ops.mesh.primitive_cylinder_add(radius=0.004, depth=d.length, location=(b.head_local + b.tail_local) / 2, vertices=6)
    c = bpy.context.active_object; c.rotation_mode = "QUATERNION"
    c.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized()); c.data.materials.append(yel)
sc.render.engine = "BLENDER_WORKBENCH"
sh = sc.display.shading; sh.light = "STUDIO"; sh.color_type = "MATERIAL"; sh.show_xray = True; sh.xray_alpha = 0.35
bpy.data.objects["Batter"].active_material.diffuse_color = (0.8, 0.8, 0.85, 1)
cam = sc.camera; cam.data.type = "ORTHO"
sc.render.resolution_x, sc.render.resolution_y = 800, 800
def shot(name, yaw, scale, x, z, y=0.0):
    a = math.radians(yaw); cam.data.ortho_scale = scale
    # aim at (x, y, z) from distance 5
    cam.location = (x + math.sin(a) * 5, y - math.cos(a) * 5, z); cam.rotation_euler = (math.radians(90), 0, a)
    sc.render.filepath = os.path.join(odir, name + ".png"); bpy.ops.render.render(write_still=True)
shot("front", 0, 2.0, -0.05, 0.95); shot("side", 90, 2.0, 0, 0.95, -0.05)
shot("feet_front", 0, 0.6, -0.05, 0.25); shot("feet_side_R", -90, 0.6, 0, 0.25, -0.12)
shot("feet_side_L", 90, 0.6, 0, 0.25, -0.12)
shot("arms_front", 0, 1.0, -0.05, 0.85); shot("head_front", 0, 0.9, -0.05, 1.5); shot("head_side", 90, 0.9, 0, 1.5, -0.1)
shot("tail_back", 180, 1.0, 0.15, 0.45, 0.2); shot("tail_side", 90, 1.0, 0, 0.45, 0.1)
print("JOINTVIEW DONE")
