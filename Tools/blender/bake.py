# blender -b --python bake.py -- <low.blend> <high.blend> <out.blend> <tex_dir> <render_dir> <res>
import sys, os, math, bpy
import numpy as np
low_p, high_p, out_blend, tdir, rdir, res = sys.argv[sys.argv.index("--") + 1:][:6]
res = int(res)
os.makedirs(tdir, exist_ok=True); os.makedirs(rdir, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=low_p)
low = bpy.data.objects["Batter"]
with bpy.data.libraries.load(high_p, link=False) as (src, dst):
    dst.objects = ["Batter"]
high = dst.objects[0]
bpy.context.scene.collection.objects.link(high)
print("HIGH", high.name, len(high.data.polygons), "mat", high.active_material.name)

# new UVs on the low mesh
for o in bpy.context.scene.objects: o.select_set(False)
bpy.context.view_layer.objects.active = low; low.select_set(True)
uv = low.data.uv_layers.new(name="BakeUV"); low.data.uv_layers.active = uv
bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004)
bpy.ops.object.mode_set(mode="OBJECT")

# low material: one image node per map
mat = bpy.data.materials.new("Batter"); mat.use_nodes = True
nt = mat.node_tree; bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
low.data.materials.clear(); low.data.materials.append(mat)
imgs = {}
def img_node(key, colorspace, is_float=False):
    im = bpy.data.images.new(f"Batter_{key}", res, res, alpha=False, float_buffer=is_float)
    im.colorspace_settings.name = colorspace
    n = nt.nodes.new("ShaderNodeTexImage"); n.image = im; imgs[key] = (im, n); return n
base = img_node("BaseColor", "sRGB"); rough = img_node("Roughness", "Non-Color", True)
metal = img_node("Metallic", "Non-Color", True); nrm = img_node("Normal", "Non-Color", True)
nt.links.new(base.outputs[0], bsdf.inputs["Base Color"])
nt.links.new(rough.outputs[0], bsdf.inputs["Roughness"])
nt.links.new(metal.outputs[0], bsdf.inputs["Metallic"])
nm = nt.nodes.new("ShaderNodeNormalMap"); nt.links.new(nrm.outputs[0], nm.inputs["Color"])
nt.links.new(nm.outputs[0], bsdf.inputs["Normal"])

# high material: route a BSDF input into emission for data bakes
hnt = high.active_material.node_tree
hb = next(n for n in hnt.nodes if n.type == "BSDF_PRINCIPLED")
out = next(n for n in hnt.nodes if n.type == "OUTPUT_MATERIAL" and n.is_active_output)
surf_src = out.inputs["Surface"].links[0].from_socket
emit = hnt.nodes.new("ShaderNodeEmission")
def route(input_name):
    sock = hb.inputs[input_name]
    for l in list(emit.inputs["Color"].links): hnt.links.remove(l)
    if sock.links:
        hnt.links.new(sock.links[0].from_socket, emit.inputs["Color"])
    else:
        v = sock.default_value
        emit.inputs["Color"].default_value = (v[0], v[1], v[2], 1) if hasattr(v, "__len__") else (v, v, v, 1)
    hnt.links.new(emit.outputs[0], out.inputs["Surface"])
print("HIGH INPUTS", {k: bool(hb.inputs[k].links) for k in ("Base Color", "Metallic", "Roughness", "Normal")})

sc = bpy.context.scene
sc.render.engine = "CYCLES"; sc.cycles.device = "CPU"; sc.cycles.samples = 4
bk = sc.render.bake
bk.use_selected_to_active = True; bk.cage_extrusion = 0.01; bk.max_ray_distance = 0.03; bk.margin = 8
high.select_set(True); low.select_set(True); bpy.context.view_layer.objects.active = low

def bake(key, typ):
    im, n = imgs[key]; nt.nodes.active = n
    bpy.ops.object.bake(type=typ)
    im.filepath_raw = os.path.join(tdir, f"Batter_{key}.png"); im.file_format = "PNG"; im.save()
    print("BAKED", key, flush=True)

route("Base Color"); bake("BaseColor", "EMIT")
route("Roughness"); bake("Roughness", "EMIT")
route("Metallic"); bake("Metallic", "EMIT")
hnt.links.new(surf_src, out.inputs["Surface"])
bake("Normal", "NORMAL")

# Unity URP metallic map: RGB = metallic, A = smoothness
m = np.array(imgs["Metallic"][0].pixels[:]).reshape(-1, 4)
r = np.array(imgs["Roughness"][0].pixels[:]).reshape(-1, 4)
ms = bpy.data.images.new("Batter_MetallicSmoothness", res, res, alpha=True, float_buffer=False)
ms.colorspace_settings.name = "Non-Color"
px = np.zeros_like(m); px[:, 0] = m[:, 0]; px[:, 1] = m[:, 0]; px[:, 2] = m[:, 0]; px[:, 3] = 1 - r[:, 0]
ms.pixels[:] = px.ravel()
ms.filepath_raw = os.path.join(tdir, "Batter_MetallicSmoothness.png"); ms.file_format = "PNG"; ms.save()
print("STATS metallic mean", round(float(m[:, 0].mean()), 3), "roughness mean", round(float(r[:, 0].mean()), 3))

# clean up: drop the high mesh and old UVs; base colour node active for previews
bpy.data.objects.remove(high, do_unlink=True)
low.data.uv_layers.remove(low.data.uv_layers["UVMap"]); low.data.uv_layers["BakeUV"].name = "UVMap"
for key, (im, n) in imgs.items():
    im.filepath = os.path.join(tdir, f"Batter_{key}.png"); im.source = "FILE"; im.reload()
nt.nodes.active = imgs["BaseColor"][1]
bpy.data.orphans_purge(do_recursive=True)
bpy.ops.wm.save_as_mainfile(filepath=out_blend)

# previews in Eevee, same framing as decimate2.py
try:
    sc.render.engine = "BLENDER_EEVEE_NEXT"
except TypeError:
    sc.render.engine = "BLENDER_EEVEE"
sc.render.resolution_x, sc.render.resolution_y = 900, 1200
w = sc.world or bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
bgn = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
bgn.inputs[0].default_value = (0.6, 0.6, 0.6, 1); bgn.inputs[1].default_value = 1.0
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); sc.collection.objects.link(sun)
sun.data.energy = 3; sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
if sc.camera is None:
    c = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(c); sc.camera = c
h = max(v.co.z for v in low.data.vertices); cam = sc.camera; cam.data.type = "ORTHO"
def shot(name, yaw_deg, scale, z):
    a = math.radians(yaw_deg); cam.data.ortho_scale = scale
    cam.location = (math.sin(a) * 5, math.cos(a) * -5, z); cam.rotation_euler = (math.radians(90), 0, a)
    sc.render.filepath = os.path.join(rdir, name + ".png"); bpy.ops.render.render(write_still=True)
shot("front", 0, h * 1.1, h / 2); shot("side", 90, h * 1.1, h / 2); shot("back", 180, h * 1.1, h / 2)
shot("face", 0, h * 0.4, h * 0.68); shot("torso", 0, h * 0.45, h * 0.45)
print("DONE")
