/**
 * The house material system.  [render agent owns this dir]
 *
 * Every solid surface in the series is one material family: a banded toon
 * ramp, a directional rim light, and a beat-driven emissive lift. Not photoreal
 * (we have no textures, no time, and no reason), not three.js default flat
 * shading (which reads as "programmer art" from the first frame).
 *
 * Why toon-ramp rather than a standard PBR material:
 *  - It is *readable in motion*. A hard terminator between two values gives
 *    every object a silhouette-plus-one-shape read, which survives 60fps
 *    motion, small screens, and video compression. Smooth PBR falloff does not.
 *  - It is colour-controllable. The ramp only multiplies light colour, so the
 *    palette owns the whole image and every game is recognisably the same show.
 *  - It is cheap. No env maps, no shadow maps, two lights total.
 *
 * The rim term is the important one. Without it, a character in front of a
 * saturated backdrop dissolves into it. The rim is directional (not a plain
 * fresnel) so it reads as a real backlight from the same place in every scene.
 *
 * Everything is built on MeshToonMaterial rather than a bare ShaderMaterial so
 * that instancing, skinning, morph targets, fog and vertex colours keep working
 * for the other agents without them having to know this file exists.
 */

import * as THREE from 'three';

/** Shared per-frame uniforms. Every house material references THESE objects,
 *  so the look updates in one write instead of N. */
export function createHouseUniforms() {
  return {
    uHouseTime: { value: 0 },
    uRimColor: { value: new THREE.Color(0x8fe8ff) },
    uRimDir: { value: new THREE.Vector3(0.62, 0.35, -0.7).normalize() },
    uRimStrength: { value: 0.85 },
    uRimPower: { value: 2.6 },
    uBeatPulse: { value: 0 },
    uAccent: { value: new THREE.Color(0xffd93d) },
  };
}

/** Ramp textures, cached per look instance. `steps` = number of light bands. */
function makeRamp(steps, softness) {
  const N = 64;
  const data = new Uint8Array(N * 4);
  for (let i = 0; i < N; i++) {
    const x = i / (N - 1);
    // Band the 0..1 lambert range, then let a little softness bleed across the
    // terminator so curved surfaces don't alias into stair-steps.
    const b = Math.floor(x * steps) / (steps - 1 || 1);
    const nextEdge = (Math.floor(x * steps) + 1) / steps;
    const d = (nextEdge - x) * steps;
    const soft = softness > 0 ? THREE.MathUtils.smoothstep(1 - d, 0.5 - softness, 0.5 + softness) : 0;
    const bNext = Math.min(1, (Math.floor(x * steps) + 1) / (steps - 1 || 1));
    let v = b + (bNext - b) * soft;
    // Lift the darkest band: pitch-black shadow sides look broken in a bright
    // toy world, and they kill the palette in the shadow half of every object.
    v = 0.34 + 0.66 * v;
    const c = Math.round(THREE.MathUtils.clamp(v, 0, 1) * 255);
    data[i * 4] = c; data[i * 4 + 1] = c; data[i * 4 + 2] = c; data[i * 4 + 3] = 255;
  }
  const tex = new THREE.DataTexture(data, N, 1, THREE.RGBAFormat);
  tex.minFilter = THREE.NearestFilter;
  tex.magFilter = THREE.NearestFilter;
  tex.generateMipmaps = false;
  tex.wrapS = tex.wrapT = THREE.ClampToEdgeWrapping;
  tex.needsUpdate = true;
  return tex;
}

const RIM_PATCH_MARK = '#define HOUSE_RIM';

/** GLSL injected at the point three assembles `outgoingLight`. */
const RIM_DECL = /* glsl */`
  uniform float uHouseTime;
  uniform vec3 uRimColor;
  uniform vec3 uRimDir;
  uniform float uRimStrength;
  uniform float uRimPower;
  uniform float uBeatPulse;
  uniform float uRimMul;
  uniform float uPulseMul;
  uniform float uEmissiveLift;
`;

const OUTGOING = 'vec3 outgoingLight = reflectedLight.directDiffuse + reflectedLight.indirectDiffuse + totalEmissiveRadiance;';

const RIM_BODY = /* glsl */`
  vec3 outgoingLight = reflectedLight.directDiffuse + reflectedLight.indirectDiffuse + totalEmissiveRadiance;
  {
    vec3 vDirH = normalize( vViewPosition );
    float fres = 1.0 - clamp( dot( normal, vDirH ), 0.0, 1.0 );
    vec3 rimV = normalize( ( viewMatrix * vec4( uRimDir, 0.0 ) ).xyz );
    // Directional backlight: only edges turned toward the rim source catch it.
    float facing = smoothstep( -0.30, 0.80, dot( normal, rimV ) );
    float rim = pow( fres, uRimPower ) * facing * uRimStrength * uRimMul;
    outgoingLight += uRimColor * rim * ( 1.0 + uBeatPulse * 0.45 );
    // Beat lift: emissive-ish brightening that the bloom pass picks up, so the
    // whole world breathes with the music without a single extra draw call.
    outgoingLight *= 1.0 + uBeatPulse * uPulseMul;
    outgoingLight += diffuseColor.rgb * uEmissiveLift * ( 0.55 + 0.45 * uBeatPulse );
  }
`;

export function createMaterialSystem() {
  const uniforms = createHouseUniforms();
  const ramps = new Map();
  const owned = new Set();

  function ramp(steps, softness) {
    const k = steps + ':' + softness;
    let t = ramps.get(k);
    if (!t) { t = makeRamp(steps, softness); ramps.set(k, t); }
    return t;
  }

  /**
   * The house surface.
   * @param {object} o
   * @param {number|THREE.Color} o.color base albedo
   * @param {number} [o.bands] light bands (2 = poster, 3 = default, 4 = soft)
   * @param {number} [o.rim] rim multiplier, 0 disables
   * @param {number} [o.pulse] how strongly this surface reacts to the beat
   * @param {number} [o.emissive] self-lit lift, 0..1 — bloom feeds on this
   */
  function toon({
    color = 0xffffff, bands = 3, softness = 0.22, rim = 1, pulse = 0.12,
    emissive = 0, emissiveColor = null, flat = false, transparent = false,
    opacity = 1, side = THREE.FrontSide, map = null, fog = true, depthWrite,
    vertexColors = false, toneMapped = true, name = 'house',
  } = {}) {
    const m = new THREE.MeshToonMaterial({
      color, map, transparent, opacity, side, fog, flatShading: flat, vertexColors,
      gradientMap: ramp(bands, softness),
      emissive: emissiveColor !== null ? emissiveColor : 0x000000,
      emissiveIntensity: emissiveColor !== null ? 1 : 0,
    });
    m.name = name;
    m.toneMapped = toneMapped;
    if (depthWrite !== undefined) m.depthWrite = depthWrite;
    patch(m, { rim, pulse, emissive });
    owned.add(m);
    return m;
  }

  /** Unlit slab — signage, light bars, anything that should ignore the sun. */
  function glow({
    color = 0xffffff, opacity = 1, transparent = false, additive = false,
    depthWrite, side = THREE.FrontSide, fog = false, map = null, pulse = 0.5,
  } = {}) {
    const m = new THREE.MeshBasicMaterial({
      color, opacity, transparent: transparent || additive, side, fog, map,
      blending: additive ? THREE.AdditiveBlending : THREE.NormalBlending,
      depthWrite: depthWrite !== undefined ? depthWrite : !(transparent || additive),
    });
    m.userData.housePulse = pulse;
    m.userData.houseBase = new THREE.Color().copy(m.color);
    owned.add(m);
    return m;
  }

  /** Attach the house shader patch to any lit material. */
  function patch(m, { rim = 1, pulse = 0.12, emissive = 0 } = {}) {
    if (m.userData.housePatched) return m;
    m.userData.housePatched = true;
    m.userData.houseU = {
      uRimMul: { value: rim },
      uPulseMul: { value: pulse },
      uEmissiveLift: { value: emissive },
    };
    const local = m.userData.houseU;
    m.onBeforeCompile = (sh) => {
      sh.uniforms.uHouseTime = uniforms.uHouseTime;
      sh.uniforms.uRimColor = uniforms.uRimColor;
      sh.uniforms.uRimDir = uniforms.uRimDir;
      sh.uniforms.uRimStrength = uniforms.uRimStrength;
      sh.uniforms.uRimPower = uniforms.uRimPower;
      sh.uniforms.uBeatPulse = uniforms.uBeatPulse;
      sh.uniforms.uRimMul = local.uRimMul;
      sh.uniforms.uPulseMul = local.uPulseMul;
      sh.uniforms.uEmissiveLift = local.uEmissiveLift;
      sh.fragmentShader = RIM_PATCH_MARK + '\n' + RIM_DECL + sh.fragmentShader.replace(OUTGOING, RIM_BODY);
    };
    m.customProgramCacheKey = () => 'house-rim';
    m.needsUpdate = true;
    return m;
  }

  /** Per-material knobs after creation (rim off for a background slab, etc). */
  function tune(m, { rim, pulse, emissive } = {}) {
    const u = m.userData.houseU;
    if (!u) return m;
    if (rim !== undefined) u.uRimMul.value = rim;
    if (pulse !== undefined) u.uPulseMul.value = pulse;
    if (emissive !== undefined) u.uEmissiveLift.value = emissive;
    return m;
  }

  /**
   * Convert a foreign material (a game that hasn't adopted the look yet, or a
   * three primitive) into the house style, keeping its colour identity.
   */
  function upgrade(src, opts = {}) {
    if (!src || src.userData?.housePatched || src.isMeshToonMaterial) return src;
    const color = src.color ? src.color.getHex() : 0xcccccc;
    const m = toon({
      color,
      flat: !!src.flatShading,
      transparent: !!src.transparent,
      opacity: src.opacity ?? 1,
      side: src.side ?? THREE.FrontSide,
      map: src.map || null,
      vertexColors: !!src.vertexColors,
      ...opts,
    });
    if (src.emissive && src.emissive.getHex() !== 0) {
      m.emissive.copy(src.emissive);
      m.emissiveIntensity = src.emissiveIntensity ?? 1;
    }
    return m;
  }

  /**
   * Inverted-hull outline. Opt-in, because it doubles the draw for that mesh —
   * worth it for the one or two objects the player must track, wrong for
   * scenery.
   */
  function outline(mesh, { color = 0x120a20, thickness = 0.035, scaleWithDepth = true } = {}) {
    const mat = new THREE.ShaderMaterial({
      uniforms: { uColor: { value: new THREE.Color(color) }, uThick: { value: thickness } },
      vertexShader: /* glsl */`
        uniform float uThick;
        #include <common>
        #include <skinning_pars_vertex>
        void main() {
          #include <beginnormal_vertex>
          #include <skinbase_vertex>
          #include <skinnormal_vertex>
          #include <defaultnormal_vertex>
          #include <begin_vertex>
          #include <skinning_vertex>
          vec4 mv = modelViewMatrix * vec4( transformed, 1.0 );
          vec3 n = normalize( normalMatrix * objectNormal );
          ${scaleWithDepth ? 'mv.xyz += n * uThick * -mv.z * 0.14;' : 'mv.xyz += n * uThick;'}
          gl_Position = projectionMatrix * mv;
        }`,
      fragmentShader: /* glsl */`
        uniform vec3 uColor;
        void main() { gl_FragColor = vec4( uColor, 1.0 ); #include <colorspace_fragment> }`,
      side: THREE.BackSide,
      toneMapped: false,
      fog: false,
    });
    const hull = new THREE.Mesh(mesh.geometry, mat);
    hull.name = '__houseOutline';
    hull.renderOrder = (mesh.renderOrder || 0) - 1;
    hull.frustumCulled = mesh.frustumCulled;
    mesh.add(hull);
    owned.add(mat);
    return hull;
  }

  /** Per-frame: push palette + beat into the shared uniforms. */
  function update(pal, beatPulse, time) {
    uniforms.uHouseTime.value = time;
    uniforms.uRimColor.value.copy(pal.col.rim);
    uniforms.uRimDir.value.copy(pal.dir.rimDir);
    uniforms.uRimStrength.value = pal.num.rimStrength;
    uniforms.uRimPower.value = pal.num.rimPower;
    uniforms.uBeatPulse.value = beatPulse;
    uniforms.uAccent.value.copy(pal.col.accent);
  }

  function dispose() {
    for (const m of owned) m.dispose();
    owned.clear();
    for (const t of ramps.values()) t.dispose();
    ramps.clear();
  }

  return { uniforms, toon, glow, patch, tune, upgrade, outline, update, dispose, owned };
}
