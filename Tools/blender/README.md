# Blender character pipeline (headless)

Run with Blender 4.2 LTS: `& "C:\Program Files\Blender Foundation\Blender 4.2\blender.exe" -b --factory-startup --python <script> -- <args>`.
Renders go to `Captures/blender/` (gitignored).

The source model is an image-to-3D download with an auto-generated skeleton (preset "Unity
Humanoid Compatible"), kept locally as `Art/tripo/Batter.glb`.

1. `inspect_glb.py <file.glb>` - print meshes, triangle counts, UVs, materials, images.
   `preview.py <file.blend> <dir> [eevee|workbench]` - turnaround previews.
2. `import_rigged.py Art/tripo/Batter.glb Art/blender/batter.blend Art/blender/tex [scale]` -
   bring the download into this pipeline's conventions: faces -Y with its left at +X, hips over
   the origin, scaled x1.9 (the size the clips were keyed at), mesh welded (the GLB stores
   every triangle with its own vertices: 136,380 -> 23,676), one opaque material. Bones are
   renamed (`Left_UpperArm` -> `UpperArm.L`), the T-pose is replaced by an A-pose rest with
   the hands closed to a grip, finger / toe-end / neck-twist weights are folded into their
   parents (27 bones left), and `Tail1-3` and `Ear.L/R` are added and weighted - the tail is
   its own mesh island, the ears are picked by distance from the ear axis.
3. `animrig.py <batter.blend> <batter_anim.blend>` - add the animation controls: `Root`
   (ground bone above Hips, carries squash/stretch, hops and spins), leg IK (`FootCtrl`,
   `KneePole`), arm IK onto grip bones on a `BatCtrl` bone parented to the chest, and the toy
   bat (mesh skinned to a `Bat` bone under `Hand.R`). IK pole angles are calibrated so the
   rest pose is unchanged.
4. `anim.py <batter_anim.blend> <batter_anim.blend> <tmp_dir> [clip ...]` - the clips, keyed
   pose to pose at 24 fps in a party-game style (few strong poses, fast breakdowns, holds,
   Root squash/stretch, ears and tail following through). Clips: `Idle` (4-beat loop: a small
   settle on each beat and a slow sway), `Coil` (dip, leg kick, load, hold from frame 9),
   `Swing` (contact at frame 3, overshoot, settle), `Whiff` (over-spin, dizzy wobble,
   deflate), `HomeRun` (crouch, jump with the bat up, fist pumps; 28-36 loops). Bat positions
   follow the shoulders of the loaded body. The arms are short for the coat, so a clavicle
   assist rolls the shoulder forward just enough for each hand to reach its grip. Poses are
   written straight into the F-curves with no action bound while posing, so the depsgraph
   cannot flush animated values over the pose being keyed.
5. `export_fbx.py <batter_anim.blend> Assets/Art/Character/Batter.fbx
   Assets/Art/Character/BatterClips.json <baked.blend> 124` - bake every clip to FK keys
   (visual keying), drop the control bones and constraints, export mesh + 29-bone skeleton
   + clips as one FBX, and write the timing sidecar (contact frame/seconds, Idle speed at
   the Swing Kings BPM). Copy `Art/blender/tex/Batter_BaseColor.png` to
   `Assets/Art/Character/Textures/`. Unity import settings live in
   `Assets/Art/Character/Editor/BatterImport.cs` (Generic rig, no keyframe reduction).
6. `clipstrip.py <file.blend> <dir> <workbench|eevee> <video 0|1> [clip ...]` - contact
   sheets (and MP4s) of each clip. Env: `KEYS_ONLY=1`, `VIEWS=0,90,-35` (camera yaw),
   `ZOOM=<ortho scale>,<height>`, `SIZE=<w>,<h>`, `FRAMES=0,3,9`.

Bone axes after `calculate_roll(GLOBAL_NEG_Y)`: local X is the hinge (+X flexes elbows
and thighs forward, -X flexes knees), Y is twist, Z is side bend.
