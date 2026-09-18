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

The approved cast look (`docs/design/cast-sheet.html`, approved 2026-09-18).
All eight are built by the designed pipeline below; the older
`build-cast.py` bodies (one flat colour + a primitive crest) are superseded.

| | |
| --- | --- |
| Source | `ybot.glb` above; character data and colour roles from `web/src/shell/castData.js` |
| Built by | `node tools/assets/blender/build-character.mjs bopp zizz kwark tuff mimo nibb glub fizz` (Blender 4.2 LTS) — deterministic; see `tools/assets/blender/build-character.py` for every transform, one shape builder per portrait shape |
| Contents | same skeleton + 8 clips as `ybot.glb`; ONE skinned mesh with five role materials (`body`, `trim`, `skin`, `accent`, `eye`): the mannequin with its head removed, a per-shape head shell with a face, a body shell, crest and outfit parts, each weighted 100% to one bone; face morph targets `mouthOpen`, `smile`, `frown`, `lidsDown`, `browsUp`, `browsPinch` (head-bound vertices only) |
| Checked by | `node tools/assets/blender/cast-contract.mjs <glb>` (also run by `npm test`) and `node tools/assets/blender/contact-sheet.mjs <glb>` |
| Cost | 17,592 (ZIZZ) – 19,428 (GLUB) triangles; 6 draw calls in game each (5 role materials + a skinned outline hull added at runtime by `chars/blenderBodies.js`) |

Proportions: the sheet draws the cast at 2.5–3.4 heads tall; on the
unmodified Mixamo skeleton (shortening it would break the clips) the head
shells land at roughly 3.5–4.5 heads.

Blender bodies won the TUFF bake-off against the toy rig (5 draw calls /
16,720 tris vs. the toy rig's 22 / 7,234) on cost and look; picked by the
user 2026-09-16.
Animated in-game via `chars/blenderAnim.js`, which plays these baked clips
directly rather than the toy rig's procedural per-joint pose system — see
that file's header for why the two rigs need different animators.
