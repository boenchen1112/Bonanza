/**
 * Show spotlights.  [render agent owns this dir]
 *
 * Fake volumetric cones — additive, unlit, depth-write off — that sweep slowly
 * across the stage and flare on the beat. No actual SpotLight is created: real
 * spot lights cost a shadow map and a per-fragment attenuation for an effect
 * the player only ever reads as "there is a beam there".
 *
 * The cheat that sells it: alpha follows |dot(normal, view)| so the body of the
 * cone is bright and the silhouette edge fades out, plus a vertical ramp so
 * the beam thins as it falls. Two shapes, no texture, no sorting problems.
 */

import * as THREE from 'three';
import { damp } from '../../core/util.js';

const VERT = /* glsl */`
  varying vec3 vN;
  varying vec3 vV;
  varying float vH;
  void main() {
    vH = uv.y;
    vec4 mv = modelViewMatrix * vec4( position, 1.0 );
    vN = normalize( normalMatrix * normal );
    vV = normalize( -mv.xyz );
    gl_Position = projectionMatrix * mv;
  }
`;

const FRAG = /* glsl */`
  varying vec3 vN;
  varying vec3 vV;
  varying float vH;
  uniform vec3 uColor;
  uniform float uIntensity;
  void main() {
    float facing = abs( dot( normalize( vN ), normalize( vV ) ) );
    float body = pow( facing, 0.85 );
    float fall = smoothstep( 0.0, 0.35, vH ) * ( 0.25 + 0.75 * vH );
    float a = body * fall * uIntensity;
    gl_FragColor = vec4( uColor * a, a );
    #include <tonemapping_fragment>
    #include <colorspace_fragment>
  }
`;

export function makeSpotlights({ look, rng }, {
  count = 4, height = 13, spread = 9, radius = 2.6, z = -6,
} = {}) {
  const group = new THREE.Group();
  group.name = 'env:spotlights';

  const geo = new THREE.ConeGeometry(radius, height, 22, 1, true);
  geo.translate(0, -height / 2, 0);

  const cones = [];
  for (let i = 0; i < count; i++) {
    const mat = new THREE.ShaderMaterial({
      uniforms: { uColor: { value: new THREE.Color(0xffffff) }, uIntensity: { value: 0.3 } },
      vertexShader: VERT, fragmentShader: FRAG,
      transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
      side: THREE.DoubleSide, fog: false, toneMapped: true,
    });
    const m = new THREE.Mesh(geo, mat);
    m.renderOrder = 10;
    m.frustumCulled = false;
    const k = count === 1 ? 0.5 : i / (count - 1);
    m.position.set((k - 0.5) * spread * 2, height, z);
    group.add(m);
    cones.push({
      mesh: m, mat,
      swing: 0.5 + rng() * 0.5,
      phase: rng() * Math.PI * 2,
      tilt: (k - 0.5) * 0.5,
      hue: i % 3,
      pop: 0,
    });
  }

  const col = new THREE.Color();

  function pulse(strength, beatIndex = 0) {
    for (let i = 0; i < cones.length; i++) {
      // Only half the rig flares per beat, alternating — a full flash every
      // beat stops reading as an accent within four bars.
      if ((i + beatIndex) % 2 === 0) cones[i].pop = Math.max(cones[i].pop, strength);
    }
  }

  function update(dt, beat, time, pal) {
    for (let i = 0; i < cones.length; i++) {
      const c = cones[i];
      c.pop = damp(c.pop, 0, 6, dt);
      c.mesh.rotation.z = Math.sin(time * 0.35 * c.swing + c.phase) * 0.26 + c.tilt;
      c.mesh.rotation.x = Math.sin(time * 0.22 * c.swing + c.phase * 0.7) * 0.14 + 0.1;
      const base = c.hue === 0 ? pal.col.accent : c.hue === 1 ? pal.col.rim : pal.col.accent2;
      col.copy(base).lerp(WHITE, 0.35);
      c.mat.uniforms.uColor.value.copy(col);
      c.mat.uniforms.uIntensity.value = 0.16 + c.pop * 0.30;
    }
  }

  function dispose() {
    geo.dispose();
    for (const c of cones) c.mat.dispose();
  }

  return { group, update, pulse, dispose };
}

const WHITE = new THREE.Color(0xffffff);
