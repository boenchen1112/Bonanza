# Blender build scripts

Scripted, headless Blender builds for authored assets — the Q9/Q10 answer
from the visual-polish grilling: smooth-mesh characters where they beat the
procedural toy rig on quality, built via `bpy` scripts (not hand-sculpted),
so every asset stays deterministic and reproducible the same way
`tools/assets/convert-mixamo.mjs` and `bake-clips.mjs` already are.

## Install

Pinned to **Blender 4.2 LTS** (reproducible output; a later LTS may change
default glTF export settings underneath a script without warning):

```
winget install --id BlenderFoundation.Blender.LTS.4.2 --version 4.2.16
```

Verify: `blender --background --version` should print `4.2.16 LTS`.

## Running a build script

```
node tools/assets/blender/run-blender.mjs <script.py> [-- <script args>]
```

`run-blender.mjs` finds the pinned install (winget's default path, or
`BLENDER_EXE` if set) and runs it with `--background --factory-startup`, so
output never depends on this machine's own Blender preferences/add-ons.
`smoke-test.py` proves the pipeline end to end (build a mesh, export
`.glb`) without touching any real asset — run it after a fresh install or
if a real build script starts failing, to rule out the pipeline itself.

## Where output goes

A build script exports next to itself while iterating (gitignored: `*.glb`,
`*.blend1` under this directory), then the script — or you, once it looks
right — copies the FINAL file into `web/src/assets/chars/` (or wherever it
ships) and writes a provenance README beside it there, matching the
`ybot.glb` README's table format: Source, License, Built by (the exact
command), Contents.

## What ships, per ADR 0003

Only the exported `.glb` is committed. The `.blend` source file is not — it
isn't a game asset (no need to ship a scene format three.js never loads),
and re-running the deterministic script from a documented source is the
same "one script is the whole record" contract the Mixamo pipeline already
uses. If a build script pulls in outside reference assets (a stock
model/texture used as a temporary rig aid, say), they follow the same rule
as everything else: free sources only (CC0), with their own provenance
note, and nothing fetched at runtime — Blender-time-only, same as
`Mixamo/`'s raw FBX never reaching the game.

## Budget

Every character built here still has to clear the game's existing budget:
**120 draw calls total** at 60fps on the reference iGPU (Intel Iris Xe).
Swing Kings, the richest scene, runs at ~96–99 draw calls today with the
procedural toy rig (20 draw calls per character — one per body segment). A
smooth Blender mesh should COST FEWER draw calls per character (one mesh,
one material, versus ~20 segments), which is headroom, not a given — check
`tools/harness/inspect.mjs --scene swing-kings` after swapping a character
in and confirm `render.drawCalls` stays inside budget before calling a
build done.
