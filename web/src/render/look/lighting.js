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
 * No shadow maps. At this scale and this art style, blob shadows (see
 * shadows.js) read better, cost a fraction, and never shimmer.
 */

import * as THREE from 'three';

export function createLightRig() {
  const group = new THREE.Group();
  group.name = '__houseLights';

  const key = new THREE.DirectionalLight(0xfff2dc, 2.5);
  key.position.set(-11, 15, 8);
  group.add(key);
  group.add(key.target);

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
    key.position.copy(_v);

    fill.color.copy(pal.col.fillSky);
    fill.groundColor.copy(pal.col.fillGround);
    fill.intensity = pal.num.fillIntensity;

    kick.color.copy(pal.col.rim);
    kick.intensity = 0.35 + pal.num.rimStrength * 0.45;
    _v.copy(pal.dir.rimDir).multiplyScalar(18);
    kick.position.copy(_v);
  }

  function dispose() {
    key.dispose?.(); fill.dispose?.(); kick.dispose?.();
  }

  return { group, key, fill, kick, update, dispose };
}
