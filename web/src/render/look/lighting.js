/**
 * The house lighting rig.  [render agent owns this dir]
 *
 * Three lights, always the same three, in always the same places:
 *
 *  key   — one hard directional from up-left-front. It owns the toon ramp's
 *          terminator, so every object in the series breaks light on the same
 *          side and the whole show reads as one physical space.
 *  fill  — a hemisphere: sky colour from above, bounced ground colour from
 *          below. This is what stops shadow sides going to mud, and it is what
 *          ties props to the palette for free.
 *  kick  — a dim directional from the opposite side at grazing angle, tinted
 *          with the rim colour. The shader rim does the edge; this does the
 *          broad separation on large surfaces the fresnel can't reach.
 *
 * Shadows: blob shadows (shadows.js) remain the contact cue for everything.
 * On top of that the KEY casts one real shadow map, but only inside a tight
 * box a scene declares with `setShadowFocus(center, radius)` — the few
 * objects the player actually watches. A fixed camera and a small, known
 * frustum mean a 2048 map is crisp there and nothing shimmers; no focus, no
 * shadow pass. Objects opt in per mesh (castShadow / receiveShadow).
 */

import * as THREE from 'three';

export function createLightRig() {
  const group = new THREE.Group();
  group.name = '__houseLights';

  const key = new THREE.DirectionalLight(0xfff2dc, 2.5);
  key.position.set(-11, 15, 8);
  key.castShadow = false;
  key.shadow.mapSize.set(2048, 2048);
  key.shadow.bias = -0.0004;
  key.shadow.normalBias = 0.025;
  key.shadow.radius = 3;
  group.add(key);
  group.add(key.target);

  /** Where the shadow frustum sits; null = no shadow pass at all. */
  let focus = null;

  const fill = new THREE.HemisphereLight(0x9fb8ff, 0x3b1f52, 1.15);
  group.add(fill);

  const kick = new THREE.DirectionalLight(0x8fe8ff, 0.85);
  kick.position.set(12, 6, -13);
  group.add(kick);
  group.add(kick.target);

  const _v = new THREE.Vector3();

  function update(pal, beatPulse) {
    key.color.copy(pal.col.key);
    // The key breathes a few percent on the beat. Below the threshold you'd
    // consciously notice; above the threshold the room feels alive.
    key.intensity = pal.num.keyIntensity * (1 + beatPulse * 0.10);
    _v.copy(pal.dir.keyDir).multiplyScalar(20);
    if (focus) {
      // Aim the key at the focus so the ortho shadow box is centred on it.
      key.target.position.copy(focus.center);
      key.position.copy(focus.center).add(_v);
    } else {
      key.target.position.set(0, 0, 0);
      key.position.copy(_v);
    }

    fill.color.copy(pal.col.fillSky);
    fill.groundColor.copy(pal.col.fillGround);
    fill.intensity = pal.num.fillIntensity;

    kick.color.copy(pal.col.rim);
    kick.intensity = 0.35 + pal.num.rimStrength * 0.45;
    _v.copy(pal.dir.rimDir).multiplyScalar(18);
    kick.position.copy(_v);
  }

  /**
   * Cast real shadows inside a sphere of `radius` around `center` (world
   * units), or pass null to turn the pass off. Scenes call this in load().
   */
  function setShadowFocus(center, radius = 6) {
    if (!center) {
      focus = null;
      key.castShadow = false;
      return;
    }
    // 'rig': follow the stage camera's target every frame (games whose camera
    // travels, like Bounce Brigade's platform run) — see follow().
    const follow = center === 'rig';
    focus = { center: follow ? new THREE.Vector3() : new THREE.Vector3(...center), radius, follow };
    const cam = key.shadow.camera;
    cam.left = -radius; cam.right = radius;
    cam.top = radius; cam.bottom = -radius;
    cam.near = 0.5; cam.far = 20 + radius * 2;
    cam.updateProjectionMatrix();
    key.castShadow = true;
  }

  /**
   * For a 'rig' focus: centre the shadow box on `target` (the camera rig's
   * look target), snapped to whole shadow-map texels so a smoothly moving
   * box doesn't make every shadow edge crawl.
   */
  function follow(target) {
    if (!focus?.follow || !target) return;
    const texel = (focus.radius * 2) / key.shadow.mapSize.x;
    focus.center.set(
      Math.round(target.x / texel) * texel,
      0,
      Math.round(target.z / texel) * texel,
    );
  }

  function dispose() {
    key.dispose?.(); fill.dispose?.(); kick.dispose?.();
  }

  return { group, key, fill, kick, update, setShadowFocus, follow, dispose };
}
