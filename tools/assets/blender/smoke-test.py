"""
Pipeline smoke test: build a trivial mesh and export it to .glb the same way
a real character build will, so a broken Blender/glTF-exporter install shows
up here instead of thirty minutes into building TUFF.

    node tools/assets/blender/run-blender.mjs tools/assets/blender/smoke-test.py

Writes tools/assets/blender/_smoke.glb (gitignored — delete freely; nothing
in the pipeline reads it back).
"""
import bpy
import os

# Factory-startup still leaves the default cube/camera/light; clear them so
# every build script starts from a known-empty scene, same as the real ones.
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0))
cube = bpy.context.active_object
cube.name = 'smoke_cube'

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), '_smoke.glb')
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB')

size = os.path.getsize(out)
print(f'SMOKE_OK bytes={size} blender={bpy.app.version_string}')
assert size > 0, 'exported .glb is empty'
