# Character assets

## `ybot.glb`

| | |
| --- | --- |
| Source | [Mixamo](https://www.mixamo.com) (Adobe) — character **Y Bot** + 8 animations, downloaded as FBX Binary, 30 fps, no keyframe reduction, 2026-09-11 |
| License | Mixamo's terms allow the characters/animations to be used royalty-free inside a game/project; the raw files must not be redistributed standalone. The raw FBX are therefore **not** committed (`Mixamo/` is gitignored) — only this converted, game-embedded file is. |
| Built by | `node tools/assets/convert-mixamo.mjs` (or `npm --prefix web run assets:mixamo`) — deterministic; see the script header for every transform applied |
| Contents | one shared 52-bone skeleton (`mixamorig*` names), two skinned meshes named `body` and `joints` (materials of the same names are placeholders — the runtime replaces them), 0.01 root scale (metres), clips below |

| Clip | Mixamo animation | Notes |
| --- | --- | --- |
| `idle` | Idle | hips pinned in place |
| `ready` | Baseball Idle | batting stance |
| `swing` | Baseball Hit | hips pinned (raw clip strides 45 cm) |
| `pitch` | Baseball Pitching | hips pinned |
| `celebrate` | Victory | |
| `fail` | Sitting Disbelief | seated from frame 0 — the crossfade in reads as a plop-down |
| `taunt` | Standing Taunt Chest Thump | |
| `dance` | Swing Dancing | root motion kept (it returns home) |

Mesh: welded and simplified to 30% of the source triangles (≈16.6k tris per
character, meshoptimizer error < 0.1%).

## `cast-<id>.glb` (bopp, zizz, kwark, tuff, mimo, nibb, glub, fizz)

| | |
| --- | --- |
| Source | `ybot.glb` above, reshaped/recoloured/crested per `shell/chars.js`'s `CHARS` entry for that id |
| Built by | `node tools/assets/blender/build-cast-all.mjs` — deterministic; see `tools/assets/blender/build-cast.py` for every transform applied |
| Contents | same skeleton + 8 clips as `ybot.glb` (untouched — only bone rest scale and materials change), plus a bone-skinned crest mesh (antenna/bolt/plume/horns/cap/fin, per character) |
| Cost | 4–6 draw calls / ~16.6k–19.5k triangles per character (see `runs/verify-cast/summary.json` from the last `verify-cast.mjs` run) |

Won the TUFF bake-off against the toy rig (5 draw calls / 16,720 tris vs. the
toy rig's 22 / 7,234) on cost and look; picked by the user 2026-09-16.
Animated in-game via `chars/blenderAnim.js`, which plays these baked clips
directly rather than the toy rig's procedural per-joint pose system — see
that file's header for why the two rigs need different animators.
