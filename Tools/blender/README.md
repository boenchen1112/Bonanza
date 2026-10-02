# Blender character pipeline (headless)

Run with Blender 4.2 LTS: `& "C:\Program Files\Blender Foundation\Blender 4.2\blender.exe" -b --factory-startup --python <script> -- <args>`.
Renders go to `Captures/blender/` (gitignored).

1. `inspect_glb.py <file.glb>` - print meshes, triangle counts, UVs, materials, images.
2. `import_glb.py <file.glb> <orig.blend> <render_dir> 99999999` - import, origin to the
   feet (centred in X/Y), save the full-res mesh. (Its Decimate step is a no-op at that target.)
3. `decimate2.py <orig.blend> <low.blend> <render_dir> 40000 0.0001` - weld the Meshy
   UV-seam splits (1,094 loose parts) first, then Collapse-decimate. Without the weld the
   pieces tear apart.
4. `bake.py <low.blend> <orig.blend> <baked.blend> Art/blender/tex <render_dir> 4096` -
   Smart-UV the low mesh, bake base colour / roughness / metallic (emission route) and
   tangent normal from the high mesh (Cycles CPU, selected-to-active), write
   `Batter_MetallicSmoothness.png` for URP Lit.
5. `rig.py <baked.blend> markers none <dir>` - fit the joints and render an x-ray overlay.
   Joints start from hand-placed guesses (which must start *inside* the limb), then ray casts
   centre each elbow/wrist/knee/ankle/tail joint in its cross-section, chain ends (hands, toes,
   ears, tail tip, head top) stop 15% short of where a ray leaves the mesh, and the tail chain
   starts where the tail mesh begins. It prints, per bone, how many of 11 samples lie outside
   the mesh (target: 0);
   `rig.py <baked.blend> bind <rigged.blend> <dir>` - build armature (Unity Humanoid
   names + Tail1-4, Ear.L/R), automatic weights, test bends.
6. `coatfix.py <rigged.blend> <out.blend>` - move leg weight on the loose coat hem to the
   hips (fades with distance from the leg axis), so thigh raises do not drag the coat.
7. `pose.py <rigged.blend> <dir>` - batting-stance and extreme-bend deformation renders.
   `preview.py <file.blend> <dir> [eevee|workbench]` - turnaround previews.
8. `animrig.py <batter.blend> <batter_anim.blend>` - add the animation controls: `Root`
   (ground bone above Hips, carries squash/stretch, hops and spins), leg IK (`FootCtrl`,
   `KneePole`), arm IK onto grip bones on a `BatCtrl` bone parented to the chest, the toy
   bat (mesh skinned to a `Bat` bone under `Hand.R`), and a `Chain.R` bone for the chain that
   hangs from the right wrist (auto weights had glued it rigidly to the forearm). IK pole
   angles are calibrated so the rest pose is unchanged.
9. `anim.py <batter_anim.blend> <batter_anim.blend> <tmp_dir> [clip ...]` - the clips, keyed
   pose to pose at 24 fps in a party-game style (few strong poses, fast breakdowns, holds,
   Root squash/stretch, ears and tail following through). No mocap: the Mixamo-style
   motion read as an RPG action game. Clips: `Idle` (2-beat bounce loop), `Coil` (dip, leg
   kick, load, hold from frame 9), `Swing` (contact at frame 3, overshoot, settle),
   `Whiff` (over-spin, dizzy wobble, deflate), `HomeRun` (crouch, jump with the bat up,
   fist pumps; 28-36 loops). The arms are short for the coat, so a clavicle assist rolls
   the shoulder forward just enough for each hand to reach its grip. Poses are written
   straight into the F-curves with no action bound while posing, so the depsgraph cannot
   flush animated values over the pose being keyed.
10. `export_fbx.py <batter_anim.blend> Assets/Art/Character/Batter.fbx
    Assets/Art/Character/BatterClips.json <baked.blend> 124` - bake every clip to FK keys
    (visual keying), drop the control bones and constraints, export mesh + 30-bone skeleton
    + clips as one FBX, and write the timing sidecar (contact frame/seconds, Idle speed at
    the Swing Kings BPM). Unity import settings live in
    `Assets/Art/Character/Editor/BatterImport.cs` (Generic rig, no keyframe reduction).
11. `clipstrip.py <file.blend> <dir> <workbench|eevee> <video 0|1> [clip ...]` - contact
    sheets (and MP4s) of each clip. Env: `KEYS_ONLY=1`, `VIEWS=0,90,-35` (camera yaw),
    `ZOOM=<ortho scale>,<height>`, `SIZE=<w>,<h>`, `FRAMES=0,3,9`.

Bone axes after `calculate_roll(GLOBAL_NEG_Y)`: local X is the hinge (+X flexes elbows
and thighs forward, -X flexes knees), Y is twist, Z is side bend.

After moving joints by hand in `Art/blender/batter.blend` (Edit Mode on `BatterRig`):

- `jointview.py <batter.blend> <dir>` - x-ray renders with a ball on every joint (front,
  side, feet, arms, head, tail) to check each joint sits inside the mesh.
- `rebind.py <batter.blend> <out.blend>` - keep the joints, recompute bone roll, replace all
  weights with fresh automatic weights, fill missed vertices, re-apply the coat-hem fix.
  Copy the result over `batter.blend`, then run steps 8-10 again.
