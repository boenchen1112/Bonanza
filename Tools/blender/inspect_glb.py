# blender -b --python inspect_glb.py -- <file.glb>
import sys, bpy
from mathutils import Vector

path = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=path)
for o in bpy.context.scene.objects:
    line = f"{o.type:8} {o.name}"
    if o.type == "MESH":
        m = o.data
        tris = sum(len(p.vertices) - 2 for p in m.polygons)
        pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
        lo = [min(p[i] for p in pts) for i in range(3)]
        hi = [max(p[i] for p in pts) for i in range(3)]
        line += (f" verts={len(m.vertices)} faces={len(m.polygons)} tris={tris}"
                 f" uvs={[u.name for u in m.uv_layers]} colors={[c.name for c in m.color_attributes]}"
                 f" size={[round(h - l, 3) for l, h in zip(lo, hi)]} min={[round(v, 3) for v in lo]}")
    print("OBJ", line)
for mat in bpy.data.materials:
    imgs = [n.image.name for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image] if mat.use_nodes else []
    print("MAT", mat.name, imgs)
for img in bpy.data.images:
    print("IMG", img.name, tuple(img.size))
