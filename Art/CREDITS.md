# Art credits

## Batter character (Tasogare, human form)

- Mesh, base-colour texture and starting skeleton generated with **Tripo**
  (https://www.tripo3d.ai), image-to-3D with its auto-rigger, from our own character art.
  Attribution: "3D model generated with Tripo (tripo3d.ai)".
- Changes: welded, turned and scaled to the game's conventions, rest pose changed to an
  A-pose with closed hands, finger bones folded into the hands, tail and ear bones added,
  bat added, animated in Blender (scripts in `Tools/blender/`).
- The raw Tripo download (`Art/tripo/Batter.glb`) is kept locally, not in git.

## Batter animations

- Motion from **Mixamo** (https://www.mixamo.com, Adobe): Baseball Idle, Baseball Hit,
  Standing Taunt Chest Thump, retargeted to the batter in Blender
  (`Tools/blender/retarget.py`). The raw downloads are kept locally, not in git.

## Callout font

- **Lilita One** (Juan Montoreano, SIL Open Font License 1.1), baked to a static TextMeshPro SDF atlas
  (ASCII) for the Swing Kings hit badges: `Assets/Game/Fonts/LilitaOne-Regular.ttf`, licence text in
  `Assets/Game/Fonts/OFL-LilitaOne.txt`, atlas `Assets/Game/Resources/Look/CalloutFont.asset`.

## HUD display font

- **Titan One** (Rodrigo Fuenzalida, SIL Open Font License 1.1), the closest free match to the web build's
  heavy, rounded, slightly slanted display face. Baked to a static TextMeshPro SDF atlas (ASCII plus the middle
  dot) for the canvas HUD, banners, countdown and results card: `Assets/Game/Fonts/TitanOne-Regular.ttf`,
  licence text in `Assets/Game/Fonts/OFL-TitanOne.txt`, atlas `Assets/Game/Resources/Look/HudFont.asset`
  (materials `HudFontDisplay` outlined with a hard shadow, `HudFontLabel` plain).
