/**
 * Post-processing.  [render agent owns this dir]
 *
 * Hand-rolled rather than EffectComposer + UnrealBloomPass, because the stock
 * bloom is five mip levels of full-fat gaussian and we need the whole chain to
 * survive a 2019 integrated GPU while the game is also drawing a crowd. This
 * is four small passes at quarter and eighth resolution plus one composite:
 *
 *   scene -> HDR target
 *   bright cut (quarter res)        1 draw
 *   blur H / V  (quarter res)       2 draws
 *   blur H / V  (eighth res, wide)  2 draws   [high tier only]
 *   composite: tonemap + bloom + chromatic offset + vignette + dither
 *
 * The composite is where the grade lives. Rendering the scene to a linear
 * float target means tone mapping happens ONCE, at the end, after bloom has
 * been added in linear light — which is the difference between bloom that
 * looks like light and bloom that looks like fog.
 *
 * Chromatic aberration is scaled by radius and spikes on impact: at rest it is
 * a sub-pixel warmth at the corners, on a PERFECT it is a visible smear for
 * ~120ms. Vignette is doing real work — it drops the corners so the centre of
 * the frame, where gameplay is, is always the brightest thing on screen.
 */

import * as THREE from 'three';
import { clamp01 } from '../../core/util.js';

const TRI_POS = new Float32Array([-1, -1, 0, 3, -1, 0, -1, 3, 0]);
const TRI_UV = new Float32Array([0, 0, 2, 0, 0, 2]);

const QUAD_VERT = /* glsl */`
  varying vec2 vUv;
  void main() { vUv = uv; gl_Position = vec4( position.xy, 0.0, 1.0 ); }
`;

const BRIGHT_FRAG = /* glsl */`
  varying vec2 vUv;
  uniform sampler2D tSrc;
  uniform float uThreshold;
  uniform float uKnee;
  void main() {
    vec3 c = texture2D( tSrc, vUv ).rgb;
    float l = max( c.r, max( c.g, c.b ) );
    // Soft knee so a surface drifting past the threshold ramps in rather than
    // popping — popping bloom looks like a bug on every single beat.
    float s = clamp( ( l - uThreshold ) / max( uKnee, 0.0001 ), 0.0, 1.0 );
    s = s * s;
    gl_FragColor = vec4( c * s, 1.0 );
  }
`;

const BLUR_FRAG = /* glsl */`
  varying vec2 vUv;
  uniform sampler2D tSrc;
  uniform vec2 uDir;
  void main() {
    vec3 s = texture2D( tSrc, vUv ).rgb * 0.2270270270;
    s += texture2D( tSrc, vUv + uDir * 1.3846153846 ).rgb * 0.3162162162;
    s += texture2D( tSrc, vUv - uDir * 1.3846153846 ).rgb * 0.3162162162;
    s += texture2D( tSrc, vUv + uDir * 3.2307692308 ).rgb * 0.0702702703;
    s += texture2D( tSrc, vUv - uDir * 3.2307692308 ).rgb * 0.0702702703;
    gl_FragColor = vec4( s, 1.0 );
  }
`;

const COMP_FRAG = /* glsl */`
  varying vec2 vUv;
  uniform sampler2D tScene;
  uniform sampler2D tBloomA;
  uniform sampler2D tBloomB;
  uniform float uBloom;
  uniform float uWide;
  uniform float uExposure;
  uniform float uSaturation;
  uniform float uVignette;
  uniform float uChroma;
  uniform vec3 uFlashColor;
  uniform float uFlash;
  uniform float uTime;

  float hash21( vec2 p ) {
    p = fract( p * vec2( 123.34, 456.21 ) );
    p += dot( p, p + 45.32 );
    return fract( p.x * p.y );
  }

  // Filmic-ish shoulder that keeps saturation in the highlights. ACES crushes
  // chroma at the top end, which is exactly wrong for a candy-coloured game:
  // our brightest pixels are supposed to be our most colourful ones.
  vec3 tonemap( vec3 x ) {
    x = max( vec3( 0.0 ), x );
    vec3 a = x * ( 1.0 + x * 0.18 );
    return a / ( 1.0 + x );
  }

  void main() {
    vec2 uv = vUv;
    vec2 d = uv - 0.5;
    float r2 = dot( d, d );

    // Radial chromatic offset, strongest at the edges, zero dead centre.
    float ca = uChroma * ( 0.35 + r2 * 3.0 );
    vec3 col;
    if ( ca > 0.0001 ) {
      col.r = texture2D( tScene, uv + d * ca ).r;
      col.g = texture2D( tScene, uv ).g;
      col.b = texture2D( tScene, uv - d * ca ).b;
    } else {
      col = texture2D( tScene, uv ).rgb;
    }

    vec3 bloom = texture2D( tBloomA, uv ).rgb;
    bloom += texture2D( tBloomB, uv ).rgb * uWide;
    col += bloom * uBloom;

    col *= uExposure;
    col = tonemap( col );

    float lum = dot( col, vec3( 0.2126, 0.7152, 0.0722 ) );
    col = mix( vec3( lum ), col, uSaturation );

    col += uFlashColor * uFlash;

    // Vignette: multiplicative, gentle, never a black ring.
    float vig = 1.0 - uVignette * smoothstep( 0.15, 0.95, r2 * 2.1 );
    col *= vig;

    // Linear -> sRGB (this chain writes straight to the default framebuffer).
    col = max( col, vec3( 0.0 ) );
    vec3 srgb = mix( col * 12.92, 1.055 * pow( col, vec3( 1.0 / 2.4 ) ) - 0.055, step( 0.0031308, col ) );
    srgb += ( hash21( gl_FragCoord.xy + uTime ) - 0.5 ) * 0.0025;

    gl_FragColor = vec4( srgb, 1.0 );
  }
`;

function fullscreenGeometry() {
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(TRI_POS, 3));
  g.setAttribute('uv', new THREE.BufferAttribute(TRI_UV, 2));
  return g;
}

export function createPost({ renderer }) {
  const gl = renderer.getContext();
  const canFloat = !!(gl.getExtension('EXT_color_buffer_half_float')
    || gl.getExtension('EXT_color_buffer_float'));
  const hdrType = canFloat ? THREE.HalfFloatType : THREE.UnsignedByteType;

  const geo = fullscreenGeometry();
  const cam = new THREE.Camera();
  const scene = new THREE.Scene();
  const quad = new THREE.Mesh(geo, null);
  quad.frustumCulled = false;
  scene.add(quad);

  const mkMat = (frag, uniforms) => new THREE.ShaderMaterial({
    uniforms, vertexShader: QUAD_VERT, fragmentShader: frag,
    depthTest: false, depthWrite: false, toneMapped: false,
  });

  const brightU = { tSrc: { value: null }, uThreshold: { value: 0.72 }, uKnee: { value: 0.45 } };
  const blurU = { tSrc: { value: null }, uDir: { value: new THREE.Vector2() } };
  const compU = {
    tScene: { value: null }, tBloomA: { value: null }, tBloomB: { value: null },
    uBloom: { value: 0.85 }, uWide: { value: 1 }, uExposure: { value: 1.05 },
    uSaturation: { value: 1.08 }, uVignette: { value: 0.42 }, uChroma: { value: 0 },
    uFlashColor: { value: new THREE.Color(0xffffff) }, uFlash: { value: 0 },
    uTime: { value: 0 },
  };

  const brightMat = mkMat(BRIGHT_FRAG, brightU);
  const blurMat = mkMat(BLUR_FRAG, blurU);
  const compMat = mkMat(COMP_FRAG, compU);

  const rtOpts = {
    minFilter: THREE.LinearFilter, magFilter: THREE.LinearFilter,
    format: THREE.RGBAFormat, type: hdrType, depthBuffer: false, stencilBuffer: false,
  };
  let sceneRT = null;
  const bloomA = new THREE.WebGLRenderTarget(8, 8, rtOpts);
  const bloomB = new THREE.WebGLRenderTarget(8, 8, rtOpts);
  const bloomC = new THREE.WebGLRenderTarget(8, 8, rtOpts);
  const bloomD = new THREE.WebGLRenderTarget(8, 8, rtOpts);
  const black = new THREE.DataTexture(new Uint8Array([0, 0, 0, 255]), 1, 1);
  black.needsUpdate = true;

  let W = 8, H = 8;
  let enabled = true;
  let wide = true;
  let samples = 0;

  function makeSceneRT(w, h) {
    if (sceneRT) sceneRT.dispose();
    sceneRT = new THREE.WebGLRenderTarget(w, h, {
      minFilter: THREE.LinearFilter, magFilter: THREE.LinearFilter,
      format: THREE.RGBAFormat, type: hdrType,
      depthBuffer: true, stencilBuffer: false, samples,
    });
  }

  function setSize(w, h) {
    W = Math.max(2, Math.floor(w));
    H = Math.max(2, Math.floor(h));
    makeSceneRT(W, H);
    const q = (n) => Math.max(2, Math.floor(n));
    bloomA.setSize(q(W / 4), q(H / 4));
    bloomB.setSize(q(W / 4), q(H / 4));
    bloomC.setSize(q(W / 8), q(H / 8));
    bloomD.setSize(q(W / 8), q(H / 8));
  }

  /** 'high' = MSAA + wide bloom, 'medium' = no MSAA + one bloom level. */
  function setTier(tier) {
    enabled = tier !== 'low';
    wide = tier === 'high';
    samples = tier === 'high' ? 4 : 0;
    compU.uWide.value = wide ? 1 : 0;
    if (enabled) setSize(W, H);
  }

  function pass(mat, target) {
    quad.material = mat;
    renderer.setRenderTarget(target);
    renderer.render(scene, cam);
  }

  /** Apply the live palette's grade. */
  function setPalette(pal) {
    compU.uExposure.value = pal.num.exposure;
    compU.uBloom.value = pal.num.bloom;
    compU.uSaturation.value = pal.num.saturation;
    compU.uVignette.value = pal.num.vignette;
    brightU.uThreshold.value = pal.num.bloomThreshold;
  }

  function setChroma(v) { compU.uChroma.value = v; }
  function setFlash(a, color) {
    compU.uFlash.value = clamp01(a);
    if (color !== undefined) compU.uFlashColor.value.set(color);
  }

  /**
   * Draw the world through the chain. Returns false if post is off, in which
   * case the caller should render directly to the canvas.
   */
  function render(sceneToDraw, camera, time) {
    if (!enabled) return false;
    compU.uTime.value = time;

    renderer.setRenderTarget(sceneRT);
    renderer.clear();
    renderer.render(sceneToDraw, camera);

    brightU.tSrc.value = sceneRT.texture;
    pass(brightMat, bloomA);

    const px = 1 / bloomA.width, py = 1 / bloomA.height;
    blurU.tSrc.value = bloomA.texture;
    blurU.uDir.value.set(px, 0);
    pass(blurMat, bloomB);
    blurU.tSrc.value = bloomB.texture;
    blurU.uDir.value.set(0, py);
    pass(blurMat, bloomA);

    if (wide) {
      const qx = 1 / bloomC.width, qy = 1 / bloomC.height;
      blurU.tSrc.value = bloomA.texture;
      blurU.uDir.value.set(qx, 0);
      pass(blurMat, bloomD);
      blurU.tSrc.value = bloomD.texture;
      blurU.uDir.value.set(0, qy);
      pass(blurMat, bloomC);
      compU.tBloomB.value = bloomC.texture;
    } else {
      compU.tBloomB.value = black;
    }

    compU.tScene.value = sceneRT.texture;
    compU.tBloomA.value = bloomA.texture;
    pass(compMat, null);
    return true;
  }

  function dispose() {
    sceneRT?.dispose();
    bloomA.dispose(); bloomB.dispose(); bloomC.dispose(); bloomD.dispose();
    brightMat.dispose(); blurMat.dispose(); compMat.dispose();
    geo.dispose(); black.dispose();
  }

  return {
    render, setSize, setTier, setPalette, setChroma, setFlash, dispose,
    get enabled() { return enabled; },
    get hdr() { return canFloat; },
    /** Fullscreen draws the chain adds per frame (bright + blurs + composite). */
    get passCount() { return enabled ? (wide ? 6 : 4) : 0; },
  };
}
