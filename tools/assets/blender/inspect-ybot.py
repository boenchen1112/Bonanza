"""Introspect ybot.glb: bone names/hierarchy, mesh names, material names,
animation clip names. One-off diagnostic, not part of the build pipeline."""
import bpy
import os
import sys

path = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else None
if not path:
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..',
                         'web', 'src', 'assets', 'chars', 'ybot.glb')
path = os.path.abspath(path)
print(f'loading {path}')

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=path)

print('\n--- objects ---')
for o in bpy.data.objects:
    print(f'{o.type:10s} {o.name}')

print('\n--- armature bone hierarchy ---')
for o in bpy.data.objects:
    if o.type == 'ARMATURE':
        def walk(bone, depth=0):
            print('  ' * depth + bone.name)
            for c in bone.children:
                walk(c, depth + 1)
        for root in o.data.bones:
            if root.parent is None:
                walk(root)

print('\n--- meshes / materials ---')
for o in bpy.data.objects:
    if o.type == 'MESH':
        mats = [s.material.name if s.material else None for s in o.material_slots]
        print(f'{o.name}: verts={len(o.data.vertices)} tris~={len(o.data.polygons)} mats={mats}')

print('\n--- animations ---')
for a in bpy.data.actions:
    print(f'{a.name}: frames {a.frame_range[0]:.0f}-{a.frame_range[1]:.0f}')

print('\nINSPECT_OK')
