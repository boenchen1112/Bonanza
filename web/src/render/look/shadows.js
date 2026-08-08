/**
 * Blob shadows.  [render agent owns this dir]
 *
 * Deliberately not shadow maps. In a stylised, brightly-lit, fixed-camera game
 * the *only* job a shadow has is to tell the player where an object is in the
 * ground plane and how high it is off it. A soft ellipse does that perfectly,
 * costs one draw call for the whole cast, never shimmers, never needs a bias
 * tweak, and reads better at 720p than a 2048px cascade would.
 *
 * Height is encoded twice — the blob grows and fades as an object rises — so
 * "how high is that thing" is legible even when the object itself is off the
 * top of the frame.
 */

import * as THREE from 'three';

const MAX_BLOBS = 64;

const VERT = /* glsl */`
  attribute float aStrength;
  varying vec2 vUvB;
  varying float vS;
  void main() {
    vUvB = uv;
    vS = aStrength;
    vec4 mv = modelViewMatrix * instanceMatrix * vec4( position, 1.0 );
    gl_Position = projectionMatrix * mv;
  }
`;

const FRAG = /* glsl */`
  varying vec2 vUvB;
  varying float vS;
  uniform vec3 uColor;
  void main() {
    float r = length( vUvB - 0.5 ) * 2.0;
    float a = 1.0 - clamp( r, 0.0, 1.0 );
    a = a * a * ( 3.0 - 2.0 * a );
    gl_FragColor = vec4( uColor, a * vS );
    #include <tonemapping_fragment>
    #include <colorspace_fragment>
  }
`;

export function createShadowField() {
  const geo = new THREE.PlaneGeometry(1, 1);
  geo.rotateX(-Math.PI / 2);
  const strengths = new Float32Array(MAX_BLOBS);
  geo.setAttribute('aStrength', new THREE.InstancedBufferAttribute(strengths, 1));

  const uniforms = { uColor: { value: new THREE.Color(0x120a20) } };
  const mat = new THREE.ShaderMaterial({
    uniforms, vertexShader: VERT, fragmentShader: FRAG,
    transparent: true, depthWrite: false, fog: false, toneMapped: false,
  });

  const mesh = new THREE.InstancedMesh(geo, mat, MAX_BLOBS);
  mesh.name = '__houseShadows';
  mesh.frustumCulled = false;
  mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
  mesh.renderOrder = -5;
  mesh.count = 0;

  /** @type {{obj:THREE.Object3D|null,x:number,z:number,r:number,y:number,op:number,live:boolean}[]} */
  const slots = [];
  const m4 = new THREE.Matrix4();
  const pos = new THREE.Vector3();
  const scl = new THREE.Vector3();
  const q = new THREE.Quaternion();

  /**
   * @param {THREE.Object3D|null} obj followed each frame (null = static blob)
   * @param {{radius?:number, groundY?:number, opacity?:number, x?:number, z?:number}} o
   */
  function add(obj, { radius = 0.7, groundY = 0, opacity = 0.55, x = 0, z = 0 } = {}) {
    if (slots.length >= MAX_BLOBS) return null;
    const s = { obj, x, z, r: radius, y: groundY, op: opacity, live: true };
    slots.push(s);
    return {
      release() { s.live = false; },
      set(nx, nz) { s.x = nx; s.z = nz; },
    };
  }

  function clear() { slots.length = 0; mesh.count = 0; }

  function update() {
    let n = 0;
    for (let i = 0; i < slots.length; i++) {
      const s = slots[i];
      if (!s.live) continue;
      let px = s.x, pz = s.z, py = s.y;
      if (s.obj) {
        s.obj.getWorldPosition(pos);
        px = pos.x; pz = pos.z; py = pos.y;
      }
      const h = Math.max(0, py - s.y);
      // Grow and fade with altitude: a rising object's shadow spreads out.
      const grow = 1 + Math.min(h, 6) * 0.16;
      const fade = 1 / (1 + Math.min(h, 8) * 0.34);
      const r = s.r * grow;
      pos.set(px, s.y + 0.012, pz);
      scl.set(r * 2, 1, r * 2);
      m4.compose(pos, q, scl);
      mesh.setMatrixAt(n, m4);
      strengths[n] = s.op * fade;
      n++;
    }
    // Compact away released slots occasionally rather than every frame.
    if (n < slots.length && slots.length > 24) {
      for (let i = slots.length - 1; i >= 0; i--) if (!slots[i].live) slots.splice(i, 1);
    }
    mesh.count = n;
    if (n > 0) {
      mesh.instanceMatrix.needsUpdate = true;
      geo.getAttribute('aStrength').needsUpdate = true;
    }
    mesh.visible = n > 0;
  }

  function setPalette(pal) {
    // Shadow tint = a much darker, slightly cooler version of the ground, so
    // shadows sit in the palette instead of punching holes in it.
    uniforms.uColor.value.copy(pal.col.ground).multiplyScalar(0.28)
      .lerp(pal.col.fog, 0.25);
  }

  function dispose() { geo.dispose(); mat.dispose(); mesh.dispose(); }

  return { mesh, add, clear, update, setPalette, dispose };
}
