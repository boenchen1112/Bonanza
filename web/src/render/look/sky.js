/**
 * The sky.  [render agent owns this dir]
 *
 * A three-stop vertical gradient with a sun lobe, slow rotating light wedges
 * and an ordered dither, drawn on a camera-locked dome. One draw call, no
 * texture, no fetch.
 *
 * It is a dome rather than `scene.background` because the background can only
 * be a flat colour or an equirect texture, and neither of those can be told to
 * pulse on the beat or cross-fade between palettes. It draws with depthTest
 * off at renderOrder -1000, so it is always behind everything and never
 * interacts with the camera's far plane.
 *
 * The dither is not decoration: an 8-bit vertical gradient across 720px bands
 * visibly, and banding is the single fastest way to make a game look cheap.
 */

import * as THREE from 'three';

const VERT = /* glsl */`
  varying vec3 vDir;
  void main() {
    vDir = normalize( position );
    gl_Position = projectionMatrix * modelViewMatrix * vec4( position, 1.0 );
  }
`;

const FRAG = /* glsl */`
  varying vec3 vDir;
  uniform vec3 uTop;
  uniform vec3 uMid;
  uniform vec3 uBot;
  uniform vec3 uSun;
  uniform vec3 uSunDir;
  uniform float uSunSize;
  uniform float uSunStrength;
  uniform float uTime;
  uniform float uBeat;

  float hash21( vec2 p ) {
    p = fract( p * vec2( 123.34, 456.21 ) );
    p += dot( p, p + 45.32 );
    return fract( p.x * p.y );
  }

  void main() {
    vec3 d = normalize( vDir );
    float h = d.y;

    // Three-stop vertical ramp: deep zenith -> body -> hot horizon band.
    vec3 col = mix( uMid, uTop, smoothstep( 0.02, 0.85, h ) );
    vec3 low = mix( uBot * 0.28, uBot, smoothstep( -0.55, -0.01, h ) );
    col = mix( low, col, smoothstep( -0.02, 0.30, h ) );

    // Sun: tight core plus a wide halo that carries the horizon glow sideways.
    float s = max( dot( d, normalize( uSunDir ) ), 0.0 );
    float core = pow( s, mix( 220.0, 40.0, uSunSize ) );
    float halo = pow( s, mix( 14.0, 2.2, uSunSize ) );
    col += uSun * ( core * 1.6 + halo * 0.55 ) * uSunStrength * ( 1.0 + uBeat * 0.25 );

    // Slow rotating wedges — reads as haze in stage lighting, costs nothing.
    float a = atan( d.x, -d.z );
    float rays = 0.5 + 0.5 * sin( a * 7.0 + uTime * 0.09 );
    rays *= 0.5 + 0.5 * sin( a * 3.0 - uTime * 0.05 );
    col += uSun * rays * 0.05 * smoothstep( 0.55, -0.05, abs( h ) ) * uSunStrength;

    // Break up 8-bit banding before the gradient ever reaches the framebuffer.
    col += ( hash21( gl_FragCoord.xy ) - 0.5 ) * 0.0035;

    gl_FragColor = vec4( col, 1.0 );
    #include <tonemapping_fragment>
    #include <colorspace_fragment>
  }
`;

export function createSky() {
  const uniforms = {
    uTop: { value: new THREE.Color(0x1a1140) },
    uMid: { value: new THREE.Color(0x3b2470) },
    uBot: { value: new THREE.Color(0xff8f5e) },
    uSun: { value: new THREE.Color(0xffd6a0) },
    uSunDir: { value: new THREE.Vector3(0, 0, -1) },
    uSunSize: { value: 0.24 },
    uSunStrength: { value: 0.8 },
    uTime: { value: 0 },
    uBeat: { value: 0 },
  };

  const mat = new THREE.ShaderMaterial({
    uniforms, vertexShader: VERT, fragmentShader: FRAG,
    side: THREE.BackSide, depthWrite: false, depthTest: false, fog: false,
  });
  const mesh = new THREE.Mesh(new THREE.SphereGeometry(50, 32, 16), mat);
  mesh.name = '__houseSky';
  mesh.renderOrder = -1000;
  mesh.frustumCulled = false;
  mesh.matrixAutoUpdate = false;

  function update(pal, camera, beatPulse, time) {
    uniforms.uTop.value.copy(pal.col.skyTop);
    uniforms.uMid.value.copy(pal.col.skyMid);
    uniforms.uBot.value.copy(pal.col.skyBot);
    uniforms.uSun.value.copy(pal.col.sun);
    uniforms.uSunDir.value.copy(pal.dir.sunDir);
    uniforms.uSunSize.value = pal.num.sunSize;
    uniforms.uSunStrength.value = pal.num.sunStrength;
    uniforms.uTime.value = time;
    uniforms.uBeat.value = beatPulse;
    if (camera) {
      mesh.position.copy(camera.position);
      mesh.updateMatrix();
      mesh.updateMatrixWorld(true);
    }
  }

  function dispose() { mesh.geometry.dispose(); mat.dispose(); }

  return { mesh, uniforms, update, dispose };
}
