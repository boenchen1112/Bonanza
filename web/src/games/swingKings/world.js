/**
 * Swing Kings — the ballpark.
 *
 * Everything here is a prop with an opinion about the beat. The set is
 * composed out of `render/env` pieces (ground, backdrop, spotlights, bunting,
 * floaters) rather than rebuilt, so it is lit, graded and beat-pulsed by the
 * house for free; only the things this game *is about* are hand-built:
 *
 *   machine   the pitching machine. Its wheels spin up a beat before it
 *             fires, which is the telegraph before the telegraph.
 *   pips      half-beat markers laid along the pitch's actual flight path.
 *             This is the idea the whole game rests on: the arc IS the
 *             metronome, so the metronome is drawn ON the arc.
 *   outs      three lamps. Outs are a scoring tier here, never an ejection.
 *   callout   BUNT / LINE DRIVE / HOME RUN in world space, from a canvas
 *             atlas — `fx.popText` draws from a vocabulary baked at boot that
 *             does not contain them, and silently drops words it lacks.
 *
 * Materials that this file animates, or that are shared between meshes, are
 * flagged `keepMaterial` so the house dress pass leaves them alone. That
 * matters for the cast and the crowd in particular: `chars/rig.js` caches its
 * materials at module scope, and letting the dress pass dispose one would
 * poison that cache for every later character in the app.
 */

import * as THREE from 'three';
import { makeCast, makeCrowd } from '../../chars/index.js';
import { damp, clamp01, easeOutCubic, backOut } from '../../core/util.js';

export const LAYOUT = {
  plate: [2.55, 0.02, 0.25],
  contact: [2.3, 1.25, 0.25],
  machine: [-7.4, 1.55, -0.25],
  batter: [3.05, 0, 0.55],
  batterFacing: -1.02,
};

const TIER_WORDS = ['BUNT', 'LINE DRIVE', 'HOME RUN!', 'GRAND SLAM!', 'FOUL!', 'OUT!'];

/** Protect a subtree's materials from the house dress pass. */
function protect(obj) {
  obj.traverse((o) => { if (o.isMesh || o.isSprite || o.isInstancedMesh) o.userData.keepMaterial = true; });
  return obj;
}

export function createWorld(ctx) {
  /** Textures + sprite materials: freed by hand, everything else by traversal. */
  const textures = [];
  const spriteMats = [];

  const root = new THREE.Group();
  root.name = 'swing:world';
  ctx.scene.add(root);

  // ---------------------------------------------------------------- the set
  const env = ctx.stage.createEnv(ctx.scene);
  env.stageSet('stage', {
    groundY: 0,
    per: {
      backdrop: { arches: 4, radius: 24, spacing: 7.5, z: -30, skyline: 30, skylineZ: -74 },
      spotlights: {},
    },
  });
  env.addBanners({ count: 30, radius: 13.5, y: 7.6, z: -3 });

  // --- stands: one sloped shell + a front wall, so the crowd has a stadium
  const ARC = Math.PI * 1.02;
  const standMat = new THREE.MeshStandardMaterial({
    color: 0x24406e, roughness: 0.95, metalness: 0, side: THREE.DoubleSide,
  });
  const slope = new THREE.Mesh(
    new THREE.CylinderGeometry(18.4, 11.0, 3.9, 56, 1, true, Math.PI - ARC / 2, ARC),
    standMat
  );
  slope.position.set(0, 2.55, -3);
  root.add(slope);

  const wall = new THREE.Mesh(
    new THREE.CylinderGeometry(11.0, 11.0, 1.35, 56, 1, true, Math.PI - ARC / 2, ARC),
    standMat
  );
  wall.position.set(0, 0.28, -3);
  root.add(wall);

  // --- crowd: 350-odd spectators, one draw call, and it reacts -------------
  const crowd = makeCrowd({
    rows: 6, perRow: 30, radius: 11.6, rowDepth: 1.2, rowRise: 0.634,
    arc: ARC * 0.94, center: [0, 0.95, -3], scale: 1.22, seed: 0x5147,
  });
  crowd.mesh.userData.keepMaterial = true;
  root.add(crowd.mesh);

  // ---------------------------------------------------------------- infield
  const dirt = new THREE.Mesh(
    new THREE.CircleGeometry(2.7, 40),
    new THREE.MeshStandardMaterial({ color: 0x8a5a34, roughness: 1 })
  );
  dirt.rotation.x = -Math.PI / 2;
  dirt.position.set(LAYOUT.plate[0] - 0.2, 0.02, LAYOUT.plate[2]);
  root.add(dirt);

  const plate = new THREE.Mesh(
    new THREE.CircleGeometry(0.36, 5),
    new THREE.MeshBasicMaterial({ color: 0xfff6e0, toneMapped: false })
  );
  plate.rotation.x = -Math.PI / 2;
  plate.rotation.z = Math.PI * 0.1;
  plate.position.set(LAYOUT.plate[0], 0.045, LAYOUT.plate[2]);
  root.add(plate);

  // Backstop behind the batter: catches whiffs, closes the right edge.
  // It used to be a raw wireframe cylinder whose arc (theta 1.26π..1.92π)
  // put it on the PITCHER's side of the plate — a curtain of vertical lines
  // drawn straight over the batter and the ball's last half-beat of flight.
  // It now sits behind the plate (+X) as a chain-link net.
  const netTex = makeNetTexture();
  textures.push(netTex);
  const backstop = new THREE.Mesh(
    new THREE.CylinderGeometry(3.3, 3.3, 3.4, 28, 1, true, Math.PI * 0.18, Math.PI * 0.5),
    new THREE.MeshBasicMaterial({
      map: netTex, color: 0xcfeaff, transparent: true, opacity: 0.55,
      side: THREE.DoubleSide, depthWrite: false, toneMapped: false,
    })
  );
  backstop.position.set(LAYOUT.plate[0] + 0.4, 1.7, LAYOUT.plate[2] - 0.6);
  root.add(backstop);
  // Top rail: gives the net a readable edge instead of fading into the sky.
  const rail = new THREE.Mesh(
    new THREE.TorusGeometry(3.3, 0.05, 6, 28, Math.PI * 0.5),
    new THREE.MeshStandardMaterial({ color: 0x2b2350, roughness: 0.6 })
  );
  // Torus angle a lands at (cos a, sin a) in XZ after the X quarter-turn;
  // the cylinder's theta lands at (sin θ, cos θ), so a = π/2 - θ: the net's
  // θ ∈ [0.18π, 0.68π] is a ∈ [-0.18π, 0.32π] — the arc rotated by -0.18π.
  rail.rotation.set(Math.PI / 2, 0, -Math.PI * 0.18);
  rail.position.set(backstop.position.x, 1.7 + 1.7, backstop.position.z);
  root.add(rail);

  // ------------------------------------------------------- pitching machine
  const machine = new THREE.Group();
  machine.name = 'swing:machine';
  machine.position.set(LAYOUT.machine[0], 0, LAYOUT.machine[2]);
  root.add(machine);

  const shellMat = new THREE.MeshStandardMaterial({ color: 0xe8563f, roughness: 0.45 });
  const darkMat = new THREE.MeshStandardMaterial({ color: 0x2b2350, roughness: 0.6 });

  const pedestal = new THREE.Mesh(new THREE.CylinderGeometry(0.26, 0.76, 1.55, 14), darkMat);
  pedestal.position.y = 0.775;
  machine.add(pedestal);

  const head = new THREE.Group();
  head.position.y = LAYOUT.machine[1];
  machine.add(head);

  const body = new THREE.Mesh(new THREE.SphereGeometry(0.64, 18, 12), shellMat);
  body.scale.set(1, 0.92, 1);
  head.add(body);

  const barrel = new THREE.Mesh(new THREE.CylinderGeometry(0.3, 0.44, 1.2, 16, 1, true), darkMat);
  barrel.rotation.z = -Math.PI / 2 - 0.34;
  barrel.position.set(0.58, 0.24, 0);
  head.add(barrel);

  const wheelGeo = new THREE.TorusGeometry(0.38, 0.11, 8, 20);
  const wheelMat = new THREE.MeshStandardMaterial({ color: 0xffd93d, roughness: 0.35 });
  const wheels = [];
  for (const s of [-1, 1]) {
    const wm = new THREE.Mesh(wheelGeo, wheelMat);
    wm.userData.keepMaterial = true;   // shared between the two wheels
    wm.position.set(0.2, s * 0.44, 0);
    wm.rotation.y = Math.PI / 2;
    head.add(wm);
    wheels.push(wm);
  }

  const hopper = new THREE.Mesh(new THREE.ConeGeometry(0.46, 0.66, 12, 1, true), shellMat);
  hopper.position.y = 0.8;
  head.add(hopper);

  const muzzleMat = new THREE.MeshBasicMaterial({
    color: 0xfff2c0, transparent: true, opacity: 0, side: THREE.DoubleSide, toneMapped: false,
  });
  const muzzle = new THREE.Mesh(new THREE.RingGeometry(0.34, 0.66, 20), muzzleMat);
  muzzle.userData.keepMaterial = true;
  muzzle.position.set(1.06, 0.38, 0);
  muzzle.rotation.y = Math.PI / 2;
  muzzle.rotation.x = 0.32;
  head.add(muzzle);

  // ------------------------------------------------------------------ balls
  const ballGeo = new THREE.SphereGeometry(0.2, 14, 10);
  const ballMat = new THREE.MeshStandardMaterial({
    color: 0xfffaf0, roughness: 0.45, emissive: 0x6b5320, emissiveIntensity: 0.9,
  });
  const balls = [];
  for (let i = 0; i < 4; i++) {
    const m = new THREE.Mesh(ballGeo, ballMat);
    m.userData.keepMaterial = true;    // shared geometry + material across the pool
    m.visible = false;
    m.name = 'swing:ball' + i;
    root.add(m);
    balls.push({ mesh: m, busy: false, trail: null });
  }

  // ------------------------------------------------------------- beat pips
  const PIP_MAX = 20;
  const pipGeo = new THREE.SphereGeometry(0.09, 8, 6);
  const pipMat = new THREE.MeshBasicMaterial({
    color: 0xcdefff, transparent: true, opacity: 0.92, toneMapped: false,
  });
  const pips = new THREE.InstancedMesh(pipGeo, pipMat, PIP_MAX);
  pips.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
  pips.frustumCulled = false;
  pips.userData.keepMaterial = true;
  pips.count = 0;
  root.add(pips);
  const pipBase = new Float32Array(PIP_MAX);
  const pipPop = new Float32Array(PIP_MAX);
  const pipPos = new Float32Array(PIP_MAX * 3);
  const _m4 = new THREE.Matrix4();
  const _v3 = new THREE.Vector3();
  const _q = new THREE.Quaternion();
  const _s3 = new THREE.Vector3();

  // ------------------------------------------------------------- outs lamps
  const outsGroup = new THREE.Group();
  outsGroup.position.set(LAYOUT.plate[0] - 0.15, 3.35, LAYOUT.plate[2] - 1.0);
  root.add(outsGroup);
  const lampGeo = new THREE.SphereGeometry(0.14, 10, 8);
  const lamps = [];
  for (let i = 0; i < 3; i++) {
    const m = new THREE.Mesh(lampGeo, new THREE.MeshBasicMaterial({ color: 0x2a3550, toneMapped: false }));
    m.userData.keepMaterial = true;
    m.position.x = (i - 1) * 0.42;
    outsGroup.add(m);
    lamps.push(m);
  }

  // ------------------------------------------------------------- tier text
  const wordMat = {};
  for (const w of TIER_WORDS) {
    const tex = makeWordTexture(w);
    textures.push(tex);
    const m = new THREE.SpriteMaterial({
      map: tex, transparent: true, depthWrite: false, depthTest: false, toneMapped: false,
    });
    spriteMats.push(m);
    wordMat[w] = m;
  }
  // Six slots: a dense section can put a word up every half-beat (~0.24s)
  // with ~1s lives, and a 4th word used to steal slot 0 mid-animation.
  const callouts = [];
  for (let i = 0; i < 6; i++) {
    const s = new THREE.Sprite(wordMat.BUNT);
    s.visible = false;
    s.renderOrder = 20;
    root.add(s);
    callouts.push({ sprite: s, life: 0, max: 1, x: 0, y: 0, z: 0, rise: 1, scale: 1 });
  }

  // ------------------------------------------------------------------ cast
  const cast = makeCast({
    scene: root, count: 1, builds: ['round'],
    positions: [LAYOUT.batter], scale: 1.55, seed: 0x51e, faceCamera: false,
  });
  const batter = cast.get(0);
  batter.char.rotation.y = LAYOUT.batterFacing;
  protect(batter.char);

  // Bat: parented to the right hand, so the rig's own coil and strike draw the
  // conducting arc and the trace is sampled from a real moving object.
  const batGroup = new THREE.Group();
  const batMesh = new THREE.Mesh(
    new THREE.CylinderGeometry(0.04, 0.08, 1.2, 10),
    new THREE.MeshStandardMaterial({ color: 0xd79a58, roughness: 0.5 })
  );
  batMesh.position.y = -0.6;
  batGroup.add(batMesh);
  const knob = new THREE.Mesh(
    new THREE.SphereGeometry(0.08, 8, 6),
    new THREE.MeshStandardMaterial({ color: 0x3a2418, roughness: 0.7 })
  );
  batGroup.add(knob);
  const batTip = new THREE.Object3D();
  batTip.position.y = -1.24;
  batGroup.add(batTip);
  // Fist grip: the bat leaves the hand across the forearm (hand-local +Z,
  // tilted up), not along it. Along-the-forearm read fine for the old
  // procedural coil but turned the mocap follow-through into a walking cane.
  batGroup.rotation.set(-Math.PI / 2 - 0.3, 0, 0);
  batter.char.attach('handR', batGroup);

  // ----------------------------------------------------------------- state
  let wheelSpin = 0;
  let wheelSpinT = 0;
  let muzzleFlash = 0;
  let outs = 0;
  let outsPulse = 0;

  // ------------------------------------------------------------------- api

  function acquireBall() {
    for (const b of balls) if (!b.busy) { b.busy = true; b.mesh.visible = true; return b; }
    balls[0].mesh.visible = true;
    return balls[0];
  }

  function freeBall(b) {
    if (!b) return;
    b.busy = false;
    b.mesh.visible = false;
    if (b.trail) { b.trail.release(); b.trail = null; }
  }

  /** Spin the wheels up — the machine's own one-beat telegraph. */
  function armMachine(strength = 1) { wheelSpinT = Math.max(wheelSpinT, 26 * strength); }
  function fireMachine() { muzzleFlash = 1; wheelSpinT = Math.max(wheelSpinT, 46); }

  /** Lay half-beat markers along a flight path. `sample(u, out)`. */
  function setPips(pitch, sample) {
    if (!pitch) { pips.count = 0; return; }
    const steps = Math.min(PIP_MAX, Math.max(2, Math.round(pitch.lead * 2)));
    for (let i = 0; i < steps; i++) {
      const u = (i + 1) / steps;               // skip the muzzle, include the plate
      const beatOff = u * pitch.lead;
      const whole = Math.abs(beatOff - Math.round(beatOff)) < 0.08;
      sample(u, _v3);
      pipPos[i * 3] = _v3.x; pipPos[i * 3 + 1] = _v3.y; pipPos[i * 3 + 2] = _v3.z;
      pipBase[i] = whole ? 1.5 : 0.72;
      pipPop[i] = 0;
    }
    pips.count = steps;
  }

  function popPip(i) { if (i >= 0 && i < pips.count) pipPop[i] = 1; }
  function clearPips() { pips.count = 0; }

  function setOuts(n) { outs = n; outsPulse = 1; }

  /** In-world word callout — a different channel from the verdict callout. */
  function callout(word, pos, { scale = 1, life = 1.05, rise = 1.1 } = {}) {
    const mat = wordMat[word];
    if (!mat) return;
    // Free slot, else the one closest to done — never a word mid-pop.
    let slot = callouts.find((c) => c.life <= 0);
    if (!slot) slot = callouts.reduce((a, c) => (c.life / c.max < a.life / a.max ? c : a));
    slot.sprite.material = mat;
    slot.sprite.visible = true;
    slot.life = life; slot.max = life; slot.scale = scale; slot.rise = rise;
    slot.x = pos[0]; slot.y = pos[1]; slot.z = pos[2];
  }

  function update(dt, beat) {
    // machine
    wheelSpinT = damp(wheelSpinT, 0, 1.6, dt);
    wheelSpin += dt * (2 + wheelSpinT);
    wheels[0].rotation.x = wheelSpin;
    wheels[1].rotation.x = -wheelSpin;
    head.rotation.z = Math.sin(beat * Math.PI) * 0.022;
    muzzleFlash = damp(muzzleFlash, 0, 9, dt);
    muzzleMat.opacity = muzzleFlash * 0.9;
    muzzle.scale.setScalar(1 + muzzleFlash * 0.8);

    // pips
    if (pips.count > 0) {
      for (let i = 0; i < pips.count; i++) {
        pipPop[i] = damp(pipPop[i], 0, 7, dt);
        _v3.set(pipPos[i * 3], pipPos[i * 3 + 1], pipPos[i * 3 + 2]);
        _s3.setScalar(pipBase[i] * (1 + pipPop[i] * 2.4));
        _m4.compose(_v3, _q, _s3);
        pips.setMatrixAt(i, _m4);
      }
      pips.instanceMatrix.needsUpdate = true;
    }

    // outs lamps
    outsPulse = damp(outsPulse, 0, 5, dt);
    for (let i = 0; i < 3; i++) {
      const on = i < outs;
      const isNew = on && i === outs - 1;
      lamps[i].material.color.setHex(on ? 0xff5d73 : 0x2a3550);
      lamps[i].scale.setScalar(
        on ? 1 + (isNew ? outsPulse * 0.9 : 0) + Math.sin(beat * Math.PI * 2 + i) * 0.04 : 0.78
      );
    }

    // callouts
    for (const c of callouts) {
      if (c.life <= 0) continue;
      c.life -= dt;
      const t = 1 - clamp01(c.life / c.max);
      const pop = backOut(clamp01(t / 0.24));
      c.sprite.position.set(c.x, c.y + easeOutCubic(t) * c.rise, c.z);
      const s = c.scale * pop * (1 + t * 0.12);
      c.sprite.scale.set(s * 3.1, s * 0.8, 1);
      c.sprite.material.opacity = t > 0.7 ? 1 - (t - 0.7) / 0.3 : 1;
      if (c.life <= 0) c.sprite.visible = false;
    }

    crowd.update(dt, beat);
    cast.update(dt, beat);
  }

  function dispose() {
    for (const b of balls) if (b.trail) { b.trail.release(); b.trail = null; }
    // Reparent the bat: `char.dispose()` takes the hand (and the bat) out of
    // the graph, and the traversal below is what actually frees GPU memory.
    root.add(batGroup);
    cast.dispose();
    crowd.dispose();
    pips.dispose();

    const geos = new Set();
    const mats = new Set();
    root.traverse((o) => {
      if (o.isSprite) return;            // sprite geometry is a THREE singleton
      if (o.geometry) geos.add(o.geometry);
      const m = o.material;
      if (Array.isArray(m)) m.forEach((x) => x && mats.add(x));
      else if (m) mats.add(m);
    });
    for (const g of geos) g.dispose();
    for (const m of mats) m.dispose();
    for (const m of spriteMats) m.dispose();
    for (const t of textures) t.dispose();

    root.removeFromParent();
    env.dispose();
  }

  return {
    root, env, crowd, cast, batter, batGroup, batTip, machine, machineHead: head,
    balls, acquireBall, freeBall,
    armMachine, fireMachine,
    setPips, popPip, clearPips,
    setOuts, callout, update, dispose,
    get outs() { return outs; },
  };
}

/** Chain-link net: a diamond lattice on transparent, tiled around the arc. */
function makeNetTexture() {
  const S = 64;
  const c = document.createElement('canvas');
  c.width = c.height = S;
  const g = c.getContext('2d');
  g.clearRect(0, 0, S, S);
  g.strokeStyle = 'rgba(255,255,255,0.85)';
  g.lineWidth = 2.2;
  g.beginPath();
  g.moveTo(0, 0); g.lineTo(S, S);
  g.moveTo(S, 0); g.lineTo(0, S);
  g.stroke();
  const tex = new THREE.CanvasTexture(c);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.repeat.set(18, 5);
  tex.anisotropy = 4;
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

/**
 * Word -> canvas texture. `fx.popText` draws from a fixed vocabulary baked at
 * boot and silently drops anything not in it, so the hit tiers get their own
 * atlas rather than a callout that quietly never appears.
 */
function makeWordTexture(word) {
  const W = 512, H = 128;
  const c = document.createElement('canvas');
  c.width = W; c.height = H;
  const g = c.getContext('2d');
  g.clearRect(0, 0, W, H);
  g.font = '900 78px system-ui, -apple-system, "Segoe UI", sans-serif';
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  // Chunky outline + drop shadow: legible over grass, sky or a particle burst.
  g.lineJoin = 'round';
  g.strokeStyle = 'rgba(10,7,24,0.95)';
  g.lineWidth = 18;
  g.strokeText(word, W / 2, H / 2 + 5);
  g.strokeText(word, W / 2, H / 2);
  const grad = g.createLinearGradient(0, 18, 0, H - 18);
  grad.addColorStop(0, '#ffffff');
  grad.addColorStop(0.55, '#ffe58a');
  grad.addColorStop(1, '#ff9a3a');
  g.fillStyle = grad;
  g.fillText(word, W / 2, H / 2);
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.needsUpdate = true;
  return tex;
}
