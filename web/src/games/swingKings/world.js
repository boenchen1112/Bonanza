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
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { makeCast, makeCrowd, CLIPS, makePose, sampleClip } from '../../chars/index.js';
import { damp, clamp01, easeOutCubic, backOut } from '../../core/util.js';

export const LAYOUT = {
  plate: [2.55, 0.02, 0.25],
  contact: [2.3, 1.25, 0.25],
  machine: [-7.4, 1.55, -0.25],
  batter: [3.05, 0, 0.55],
  batterFacing: -1.02,
};

const TIER_WORDS = ['BUNT', 'LINE DRIVE', 'HOME RUN!', 'GRAND SLAM!', 'FOUL!', 'OUT!', 'LIKE THIS!', 'OUTS'];

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
      // A ballpark, not a stage: no confetti, no concentric rings.
      ground: { confetti: 0, rings: false },
      backdrop: { arches: 4, radius: 24, spacing: 7.5, z: -30, skyline: 30, skylineZ: -74 },
      spotlights: {},
      // Fewer, smaller and up in the sky: full-size floaters hung in front
      // of the stands as big crystals between the camera and the crowd.
      floaters: { count: 10, innerR: 26, outerR: 36, minY: 7, maxY: 16, size: 0.6 },
    },
  });
  env.addBanners({ count: 30, radius: 13.5, y: 7.6, z: -3 });

  // Mowed outfield: stripes in the texture, the palette's green in the colour.
  const grassTex = makeGrassTexture();
  textures.push(grassTex);
  const groundTop = env.pieces.ground.top;
  groundTop.material.map = grassTex;
  groundTop.material.needsUpdate = true;
  groundTop.receiveShadow = true;

  // The one real shadow map: the plate, the batter and the machine.
  ctx.stage.look.setShadowFocus([-2.3, 0, 0.1], 6.8);

  // --- stands: one sloped shell + a front wall, so the crowd has a stadium
  const ARC = Math.PI * 1.02;
  const seatTex = makeSeatTexture();
  textures.push(seatTex);
  const standMat = new THREE.MeshStandardMaterial({
    color: 0x2c4c82, map: seatTex, roughness: 0.95, metalness: 0, side: THREE.DoubleSide,
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
  // Enough of a diamond to read as baseball from a fixed side camera: the
  // cut-out around the plate, the mound under the machine, both foul lines
  // and the batter's boxes. (A full diamond would put 3rd base in the stands.)
  const dirtTex = makeDirtTexture();
  textures.push(dirtTex);
  const dirtMat = new THREE.MeshStandardMaterial({ color: 0xc08a5a, map: dirtTex, roughness: 1 });
  const dirt = new THREE.Mesh(new THREE.CircleGeometry(2.7, 48), dirtMat);
  dirt.rotation.x = -Math.PI / 2;
  dirt.position.set(LAYOUT.plate[0] - 0.2, 0.02, LAYOUT.plate[2]);
  dirt.receiveShadow = true;
  root.add(dirt);

  const mound = new THREE.Mesh(new THREE.CylinderGeometry(1.45, 1.95, 0.16, 40), dirtMat);
  mound.position.set(LAYOUT.machine[0] + 0.3, 0.06, LAYOUT.machine[2]);
  mound.receiveShadow = true;
  root.add(mound);

  // All chalk is one merged mesh (one draw call): each stroke is a flat strip
  // placed in world space, then baked together.
  const strokes = [];
  const chalk = (len, w, x, z, rotY, y = 0.035) => {
    const g = new THREE.PlaneGeometry(len, w);
    g.rotateX(-Math.PI / 2);
    g.translate(len / 2, 0, 0);
    g.rotateY(rotY);
    g.translate(x, y, z);
    strokes.push(g);
  };
  // Foul lines leave the plate at ±45° either side of the line to the mound.
  for (const s of [-1, 1]) chalk(13, 0.08, LAYOUT.plate[0] - 0.25, LAYOUT.plate[2], Math.PI + s * Math.PI * 0.25);
  // Batter's boxes: open rectangles either side of the plate.
  const W = 1.05, D = 1.7, T = 0.055;
  for (const s of [-1, 1]) {
    const bx = LAYOUT.plate[0], bz = LAYOUT.plate[2] + s * 0.95;
    chalk(D, T, bx - W / 2, bz + D / 2, Math.PI / 2, 0.04);
    chalk(D, T, bx + W / 2, bz + D / 2, Math.PI / 2, 0.04);
    chalk(W, T, bx - W / 2, bz - D / 2, 0, 0.04);
    chalk(W, T, bx - W / 2, bz + D / 2, 0, 0.04);
  }
  const chalkMesh = new THREE.Mesh(mergeGeometries(strokes),
    new THREE.MeshBasicMaterial({ color: 0xf6f1e4, toneMapped: false }));
  for (const g of strokes) g.dispose();
  root.add(chalkMesh);

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

  // "PITCH-O": a toy pitching machine with a face, because everything in
  // this series with a job to do also has an attitude about it. Squints when
  // it arms, blinks when it fires, and watches the plate the rest of the time.
  const shellMat = new THREE.MeshStandardMaterial({ color: 0xe8563f, roughness: 0.45 });
  const darkMat = new THREE.MeshStandardMaterial({ color: 0x2b2350, roughness: 0.6 });
  const trimMat = new THREE.MeshStandardMaterial({ color: 0xd8c7a6, roughness: 0.6 });   // below the bloom cut

  // Chassis: a squat wheeled cart, so it reads as a machine that was rolled
  // out onto the mound, not a lamp post.
  const cart = new THREE.Mesh(new THREE.BoxGeometry(1.7, 0.34, 1.15), darkMat);
  cart.position.y = 0.42;
  machine.add(cart);
  const tyreGeo = new THREE.CylinderGeometry(0.26, 0.26, 0.16, 18);
  tyreGeo.rotateX(Math.PI / 2);
  for (const x of [-0.6, 0.6]) {
    for (const z of [-0.62, 0.62]) {
      const t = new THREE.Mesh(tyreGeo, darkMat);
      t.position.set(x, 0.26, z);
      machine.add(t);
    }
  }
  const neck = new THREE.Mesh(new THREE.CylinderGeometry(0.2, 0.3, 0.75, 14), trimMat);
  neck.position.y = 0.95;
  machine.add(neck);

  const head = new THREE.Group();
  head.position.y = LAYOUT.machine[1];
  machine.add(head);

  // Body: a rounded capsule lying along the throw, with a cream belly band.
  const bodyGeo = new THREE.CapsuleGeometry(0.52, 0.55, 6, 18);
  bodyGeo.rotateZ(Math.PI / 2);
  const body = new THREE.Mesh(bodyGeo, shellMat);
  head.add(body);
  const band = new THREE.Mesh(new THREE.CylinderGeometry(0.535, 0.535, 0.2, 24, 1, true), trimMat);
  band.rotation.z = Math.PI / 2;
  band.position.x = -0.18;
  head.add(band);

  const barrel = new THREE.Mesh(new THREE.CylinderGeometry(0.3, 0.4, 0.85, 18, 1, true), darkMat);
  barrel.rotation.z = -Math.PI / 2 - 0.34;
  barrel.position.set(0.78, 0.2, 0);
  head.add(barrel);

  const wheelGeo = new THREE.TorusGeometry(0.34, 0.1, 8, 20);
  const wheelMat = new THREE.MeshStandardMaterial({ color: 0xffd93d, roughness: 0.35 });
  const wheels = [];
  for (const s of [-1, 1]) {
    const wm = new THREE.Mesh(wheelGeo, wheelMat);
    wm.userData.keepMaterial = true;   // shared between the two wheels
    // Above and below the barrel mouth, discs facing the camera: the spin-up
    // reads from the side, and neither wheel covers the face.
    wm.position.set(0.62, s * 0.5, 0.05);
    head.add(wm);
    wheels.push(wm);
  }

  // Hopper with a few balls waiting in it.
  const hopper = new THREE.Mesh(new THREE.CylinderGeometry(0.42, 0.2, 0.5, 16, 1, true), trimMat);
  hopper.position.set(-0.2, 0.72, 0);
  head.add(hopper);
  const spareGeo = new THREE.SphereGeometry(0.14, 10, 8);
  const spareMat = new THREE.MeshStandardMaterial({ color: 0xfffaf0, roughness: 0.5 });
  for (const [x, z] of [[-0.32, 0.08], [-0.1, -0.1], [-0.18, 0.18]]) {
    const b = new THREE.Mesh(spareGeo, spareMat);
    b.position.set(x, 0.9, z);
    head.add(b);
  }

  // Face on the camera side (+Z): two big eyes and a visor brow.
  const eyeWhite = new THREE.MeshBasicMaterial({ color: 0xffffff, toneMapped: false });
  const eyeDark = new THREE.MeshBasicMaterial({ color: 0x1a1030, toneMapped: false });
  const eyes = [];
  for (const x of [-0.34, 0.02]) {
    const e = new THREE.Group();
    e.position.set(x, 0.12, 0.46);
    const white = new THREE.Mesh(new THREE.SphereGeometry(0.17, 14, 10), eyeWhite);
    white.scale.set(1, 1, 0.55);
    const pupil = new THREE.Mesh(new THREE.SphereGeometry(0.085, 10, 8), eyeDark);
    pupil.position.set(0.05, 0, 0.08);   // looking toward the plate
    white.userData.keepMaterial = true;
    pupil.userData.keepMaterial = true;
    e.add(white, pupil);
    head.add(e);
    eyes.push(e);
  }
  const brow = new THREE.Mesh(new THREE.BoxGeometry(0.7, 0.07, 0.12), darkMat);
  brow.position.set(-0.16, 0.34, 0.47);
  head.add(brow);

  // Only the big shapes cast: tyres, spare balls and the face add draw calls
  // to the shadow pass without changing the shadow's silhouette.
  for (const m of [cart, neck, body, barrel, hopper, ...wheels]) m.castShadow = true;

  // Muzzle flash: an additive radial glow, not a solid disc (the old flat
  // beige ring read as a prop stuck to the barrel).
  const glowTex = makeGlowTexture();
  textures.push(glowTex);
  const muzzleMat = new THREE.MeshBasicMaterial({
    map: glowTex, color: 0xfff2c0, transparent: true, opacity: 0, side: THREE.DoubleSide,
    blending: THREE.AdditiveBlending, depthWrite: false, toneMapped: false,
  });
  const muzzle = new THREE.Mesh(new THREE.PlaneGeometry(1.5, 1.5), muzzleMat);
  muzzle.userData.keepMaterial = true;
  muzzle.position.set(1.16, 0.35, 0);
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
  // Enough segments to stay round when a whole-beat pip pops: the old 8x6
  // sphere scaled up read as a faceted disc.
  const pipGeo = new THREE.SphereGeometry(0.09, 16, 12);
  const pipMat = new THREE.MeshBasicMaterial({
    // Warm and bright: the pale blue pips vanished against the crowd.
    color: 0xffc83d, transparent: true, opacity: 1, toneMapped: false,
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
  outsGroup.visible = false;   // the outs count moved to the HUD (hud.js); kept for setOuts() callers
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
  // The lamps are labelled: three unlabelled dots over the crowd read as
  // random decoration, and a lit one as a stray red light.
  const outsLabel = new THREE.Sprite(wordMat.OUTS);
  outsLabel.scale.set(1.05, 0.27, 1);
  outsLabel.position.set(-0.98, 0, 0);
  outsLabel.renderOrder = 19;
  outsGroup.add(outsLabel);
  const outsAnchor = [outsGroup.position.x, outsGroup.position.y + 0.45, outsGroup.position.z];

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
  // The batter is whoever the shell's lineup put in P1 (booted cold, the
  // house default).
  const hero = ctx.players?.[0] || null;
  const cast = makeCast({
    scene: root, count: 1, builds: [hero?.build || 'round'],
    players: hero ? [{ id: hero.id, name: hero.name, palette: hero.palette }] : null,
    positions: [LAYOUT.batter], scale: 1.55, seed: 0x51e, faceCamera: false,
  });
  const batter = cast.get(0);
  hero?.dress?.(batter.char);
  batter.char.rotation.y = LAYOUT.batterFacing;
  protect(batter.char);
  batter.char.setShadowDetail('full');   // the hero: whole silhouette in the shadow map

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

  // Batting helmet instead of the build's headband: the headband sat over
  // the eyes in the stance and slid to the neck in the whiff pratfall.
  {
    const j = batter.char.joints;
    const hb = batter.char.build.head;
    if (j.gear && j.gear.parent === j.head) j.gear.visible = false;
    if (j.bobbleMesh) j.bobbleMesh.visible = false;
    // Team colour: the character's own trim, so the helmet belongs to them.
    const helmetMat = new THREE.MeshStandardMaterial({ color: hero?.palette?.trim ?? 0x1f2f6b, roughness: 0.35 });
    const dome = new THREE.Mesh(new THREE.SphereGeometry(hb.w * 0.56, 18, 10, 0, Math.PI * 2, 0, Math.PI * 0.5), helmetMat);
    dome.position.y = hb.h * 0.5 - hb.w * 0.22;
    const brim = new THREE.Mesh(new THREE.CylinderGeometry(hb.w * 0.4, hb.w * 0.4, 0.03, 18, 1, false, -Math.PI / 2, Math.PI), helmetMat);
    brim.position.set(0, hb.h * 0.5 - hb.w * 0.2, hb.w * 0.3);
    const flap = new THREE.Mesh(new THREE.SphereGeometry(hb.w * 0.2, 10, 8), helmetMat);
    flap.scale.set(0.5, 1, 1);
    flap.position.set(-hb.w * 0.5, hb.h * 0.12, 0);   // ear flap on the pitcher side
    for (const m of [dome, brim, flap]) { m.castShadow = true; j.head.add(m); }
  }

  const grips = calibrateBatter(batter, batGroup);
  const batHand = batGroup.parent;
  const batHandPos = batGroup.position.clone();
  let batDropped = false;

  /**
   * Curtain call: the bat goes down on the grass and the batter dances with
   * empty hands (the dance's low hand drove a held bat into the dirt).
   */
  function dropBat() {
    if (batDropped) return;
    batDropped = true;
    root.add(batGroup);
    batGroup.position.set(LAYOUT.plate[0] + 0.55, 0.07, LAYOUT.plate[2] + 0.95);
    batGroup.rotation.set(0, 0.5, Math.PI / 2);   // bat axis is local Y: lay it flat
  }

  function holdBat() {
    if (!batDropped) return;
    batDropped = false;
    batHand.add(batGroup);
    batGroup.position.copy(batHandPos);
    batGroup.quaternion.copy(grips.stance);
  }

  // ----------------------------------------------------------------- state
  let wheelSpin = 0;
  let wheelSpinT = 0;
  let muzzleFlash = 0;
  let outs = 0;
  let outsPulse = 0;
  let calloutMs = null;

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
      // Big enough to read as the beat from across the room; whole beats bigger.
      pipBase[i] = whole ? 2.0 : 1.35;
      pipPop[i] = 0;
    }
    pips.count = steps;
  }

  // A popped pip has been passed: it flashes, then recedes to half size, so
  // the markers behind the ball don't hang over the stands as glowing discs.
  function popPip(i) { if (i >= 0 && i < pips.count) { pipPop[i] = 1; pipBase[i] *= 0.5; } }
  function clearPips() { pips.count = 0; }

  function setOuts(n) { outs = n; outsPulse = 1; }

  /** In-world word callout — a different channel from the verdict callout. */
  /**
   * `word` may be "TIER|SUB" — a badge with the tier word big and a small
   * second line (the timing grade), built on first use and cached.
   */
  function wordMaterial(key) {
    if (wordMat[key]) return wordMat[key];
    const [w, sub] = key.split('|');
    if (!WORD_GRAD[w]) return null;
    const tex = makeWordTexture(w, sub);
    textures.push(tex);
    const m = new THREE.SpriteMaterial({
      map: tex, transparent: true, depthWrite: false, depthTest: false, toneMapped: false,
    });
    m.userData.aspect = tex.image.height / 128;
    spriteMats.push(m);
    wordMat[key] = m;
    return m;
  }

  /**
   * The HUD owns the top of the frame (score, accuracy, the OUTS pill down to
   * ~17%): a rising badge stops at HUD_TOP instead of sliding under the pill.
   */
  const HUD_TOP_NDC = 1 - 2 * 0.2;
  const _top = new THREE.Vector3();
  const _mid = new THREE.Vector3();
  function keepBelowHud(sprite) {
    const cam = ctx.camera;
    if (!cam) return;
    const half = sprite.scale.y * 0.5;
    _mid.copy(sprite.position).project(cam);
    _top.copy(sprite.position).setY(sprite.position.y + half).project(cam);
    const over = _top.y - HUD_TOP_NDC;
    const perUnit = (_top.y - _mid.y) / Math.max(1e-4, half);
    if (over > 0 && perUnit > 1e-4) sprite.position.y -= over / perUnit;
  }

  function callout(word, pos, { scale = 1, life = 1.05, rise = 1.1 } = {}) {
    const mat = wordMaterial(word);
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
    wheels[0].rotation.z = wheelSpin;
    wheels[1].rotation.z = -wheelSpin;
    head.rotation.z = Math.sin(beat * Math.PI) * 0.022;
    muzzleFlash = damp(muzzleFlash, 0, 9, dt);
    muzzleMat.opacity = muzzleFlash * 0.9;
    muzzle.scale.setScalar(1 + muzzleFlash * 0.8);

    // face: squint while the wheels spin up, blink shut on the shot
    const focusAmt = clamp01(wheelSpinT / 26);
    const open = (1 - focusAmt * 0.5) * (1 - muzzleFlash * 0.9);
    for (const e of eyes) e.scale.set(1, Math.max(0.08, open), 1);
    brow.position.y = 0.34 - focusAmt * 0.07;
    brow.rotation.z = -focusAmt * 0.12;

    // pips
    if (pips.count > 0) {
      for (let i = 0; i < pips.count; i++) {
        pipPop[i] = damp(pipPop[i], 0, 7, dt);
        _v3.set(pipPos[i * 3], pipPos[i * 3 + 1], pipPos[i * 3 + 2]);
        _s3.setScalar(pipBase[i] * (1 + pipPop[i] * 1.2));
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

    // callouts — aged on REAL elapsed time like the DOM popups (ui/index.js):
    // the frame dt is clamped and eaten by hitstop, which is exactly when a
    // callout spawns, so the word sat frozen mid-pop at scale ~0.
    const nowMs = performance.now();
    const realDt = calloutMs === null ? dt : Math.min(0.25, (nowMs - calloutMs) / 1000);
    calloutMs = nowMs;
    for (const c of callouts) {
      if (c.life <= 0) continue;
      c.life -= realDt;
      const t = 1 - clamp01(c.life / c.max);
      // Starts at 60% size: from zero, the first frames drew a speck.
      const pop = 0.6 + 0.4 * backOut(clamp01(t / 0.24));
      c.sprite.position.set(c.x, c.y + easeOutCubic(t) * c.rise, c.z);
      const s = c.scale * pop * (1 + t * 0.12);
      c.sprite.scale.set(s * 3.1, s * 0.8 * (c.sprite.material.userData.aspect || 1), 1);
      keepBelowHud(c.sprite);
      c.sprite.material.opacity = t > 0.7 ? 1 - (t - 0.7) / 0.3 : 1;
      if (c.life <= 0) c.sprite.visible = false;
    }

    crowd.update(dt, beat);
    cast.update(dt, beat);

    // Grip: bat up behind the head in the stance and the coil, across the
    // forearm for the strike and everything after it.
    const a = batter.anim;
    // Bat UP (stance grip) in the batting stance, the first part of the coil
    // and celebrations (arms up, bat raised — it used to point at the floor
    // like a cane); strike grip from late in the coil, so the bat is already
    // in its hitting orientation when the swing reaches the ball.
    const v = a.variant;
    const inCoil = a.state === 'clip' && v?.name === 'swing' && v.to < CLIPS.swing.contact;
    const coilU = inCoil ? (a.clipTime ?? 0) / Math.max(1e-3, v.to) : 1;
    const up = (a.state === 'clip' && v?.name === 'ready') || (inCoil && coilU < 0.72)
      || a.state === 'celebrate';
    // Rigid through mocap clips (squash would shear the twisted swing pose),
    // a lighter squash for the procedural idle and verdict poses.
    a.squashScale = a.state === 'clip' ? 0 : 0.45;
    if (!batDropped) batGroup.quaternion.slerp(up ? grips.stance : grips.strike, 1 - Math.exp(-(up ? 10 : 30) * dt));
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
    setOuts, callout, update, dispose, outsAnchor, dropBat, holdBat,
    /** World position of the bat's sweet spot, as posed right now. */
    batSweetSpot(out) {
      batGroup.updateWorldMatrix(true, false);   // poses were set this frame, matrices not yet
      return batGroup.localToWorld(out.set(0, -0.9, 0));
    },
    get outs() { return outs; },
  };
}

/**
 * Fit the batter to the mocap swing, once, at load. Posing the rig directly at
 * sampled clip frames (no damping, no beat layer), it:
 *  1. picks the stance GRIP that stands the bat up behind the head (the
 *     strike grip, across the forearm, held it level like a barbell there);
 *  2. picks the FACING at which the contact pose shows its chest to camera
 *     and pitcher (the authored facing left it edge-on, back to camera);
 *  3. moves the batter so the bat's sweet spot is exactly where the ball
 *     arrives, and lifts the arrival point to the bat's height — so the hit
 *     flash, which spawns at LAYOUT.contact, lands ON the bat.
 * Returns the two grip quaternions for the per-frame blend in update().
 */
function calibrateBatter(batter, batGroup) {
  const { char, anim } = batter;
  const pose = makePose();
  const legFrac = char.dims.legLen / char.dims.height;
  const clip = CLIPS.swing;
  const poseAt = (name, t) => {
    sampleClip(pose, CLIPS[name], t, legFrac);
    anim._applyPose(pose);
    char.updateMatrixWorld(true);
  };
  const v = new THREE.Vector3();
  const q = new THREE.Quaternion();
  const e = new THREE.Euler();

  // 1. strike grip = the authored one; stance grip = best "up and back".
  const strikeGrip = batGroup.quaternion.clone();
  poseAt('ready', 0.4);
  const want = new THREE.Vector3(0.25, 1, -0.35).normalize();
  let bestA = 0, bestS = -Infinity;
  for (let i = 0; i < 48; i++) {
    const ax = -Math.PI + (i / 48) * Math.PI * 2;
    batGroup.quaternion.setFromEuler(e.set(ax, 0, 0.2));
    char.updateMatrixWorld(true);
    batGroup.getWorldQuaternion(q);
    const s = v.set(0, -1, 0).applyQuaternion(q).dot(want);
    if (s > bestS) { bestS = s; bestA = ax; }
  }
  const stanceGrip = new THREE.Quaternion().setFromEuler(e.set(bestA, 0, 0.2));
  batGroup.quaternion.copy(strikeGrip);

  // 2. facing: chest (torso +Z) toward a point between camera and pitcher.
  const target = new THREE.Vector3(-1, 0.1, 1.15).normalize();
  let bestF = char.rotation.y, bestD = -Infinity;
  for (let i = 0; i < 72; i++) {
    const f = -Math.PI + (i / 72) * Math.PI * 2;
    char.rotation.y = f;
    poseAt('swing', clip.contact);
    char.joints.torso.getWorldQuaternion(q);
    const d = v.set(0, 0, 1).applyQuaternion(q).dot(target);
    if (d > bestD) { bestD = d; bestF = f; }
  }
  char.rotation.y = bestF;

  // 3. sweet spot onto the ball's arrival point.
  poseAt('swing', clip.contact);
  batGroup.localToWorld(v.set(0, -0.9, 0));
  char.position.x += LAYOUT.contact[0] - v.x;
  char.position.z += LAYOUT.contact[2] - v.z;
  LAYOUT.contact[1] = Math.min(1.7, Math.max(0.7, v.y));

  anim.setState('idle', { force: true, blend: 0.001 });
  return { stance: stanceGrip, strike: strikeGrip };
}

/** Deterministic speckle (no Math.random in anything the harness replays). */
function speckle(g, S, n, seed, colors, rMax) {
  let s = seed >>> 0;
  const r = () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296);
  for (let i = 0; i < n; i++) {
    g.fillStyle = colors[(r() * colors.length) | 0];
    const rad = 0.6 + r() * rMax;
    g.beginPath();
    g.arc(r() * S, r() * S, rad, 0, Math.PI * 2);
    g.fill();
  }
}

/**
 * Mowed grass: alternating light/dark bands with blade speckle. Values only —
 * the palette's green arrives through the material colour.
 */
function makeGrassTexture() {
  const S = 256;
  const c = document.createElement('canvas');
  c.width = c.height = S;
  const g = c.getContext('2d');
  const BANDS = 8;
  for (let i = 0; i < BANDS; i++) {
    g.fillStyle = i % 2 ? '#ffffff' : '#c9d9c6';
    g.fillRect((i * S) / BANDS, 0, S / BANDS, S);
  }
  speckle(g, S, 900, 0x9a55, ['rgba(255,255,255,0.18)', 'rgba(0,30,0,0.12)'], 1.1);
  const tex = new THREE.CanvasTexture(c);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.repeat.set(3, 3);
  tex.anisotropy = 8;
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

/** Infield dirt: warm tan, grain speckle, faint rake arcs. */
function makeDirtTexture() {
  const S = 256;
  const c = document.createElement('canvas');
  c.width = c.height = S;
  const g = c.getContext('2d');
  g.fillStyle = '#f2e2cf';
  g.fillRect(0, 0, S, S);
  speckle(g, S, 1400, 0xd127, ['rgba(120,70,30,0.22)', 'rgba(255,245,230,0.35)', 'rgba(90,50,20,0.14)'], 1.3);
  g.strokeStyle = 'rgba(120,70,30,0.10)';
  g.lineWidth = 2;
  for (let i = 0; i < 9; i++) {
    g.beginPath();
    g.arc(S / 2, S * 1.6, S * (0.9 + i * 0.12), Math.PI * 1.25, Math.PI * 1.75);
    g.stroke();
  }
  const tex = new THREE.CanvasTexture(c);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.anisotropy = 8;
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

/** Bleachers: stepped seat rows, so the stand reads as seating, not a slab. */
function makeSeatTexture() {
  const W = 64, H = 256;
  const c = document.createElement('canvas');
  c.width = W; c.height = H;
  const g = c.getContext('2d');
  const ROWS = 12;
  for (let i = 0; i < ROWS; i++) {
    const y = (i * H) / ROWS;
    g.fillStyle = '#ffffff';
    g.fillRect(0, y, W, H / ROWS);
    g.fillStyle = 'rgba(0,0,20,0.28)';          // riser shadow under each row
    g.fillRect(0, y + (H / ROWS) * 0.72, W, (H / ROWS) * 0.28);
  }
  const tex = new THREE.CanvasTexture(c);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.repeat.set(24, 1);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

/** Soft radial glow (white core to transparent), for additive flashes. */
function makeGlowTexture() {
  const S = 128;
  const c = document.createElement('canvas');
  c.width = c.height = S;
  const g = c.getContext('2d');
  const grad = g.createRadialGradient(S / 2, S / 2, 0, S / 2, S / 2, S / 2);
  grad.addColorStop(0, 'rgba(255,255,255,1)');
  grad.addColorStop(0.25, 'rgba(255,240,200,0.85)');
  grad.addColorStop(0.6, 'rgba(255,200,120,0.25)');
  grad.addColorStop(1, 'rgba(255,180,90,0)');
  g.fillStyle = grad;
  g.fillRect(0, 0, S, S);
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
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
/**
 * Each tier word has its own colour, so it reads as a different channel from
 * the gold verdict word (PERFECT!) that pops beside it — two gold words
 * stacked read as one repeated word.
 */
const WORD_GRAD = {
  BUNT: ['#ffffff', '#9ff0ff', '#3cc4ff'],
  'LINE DRIVE': ['#ffffff', '#fff39a', '#ffc21a'],
  'HOME RUN!': ['#ffffff', '#ffb27a', '#ff5a3c'],
  'GRAND SLAM!': ['#ffffff', '#fff3cf', '#ffd35a'],
  'FOUL!': ['#ffffff', '#e2c4ff', '#a86bff'],
  'OUT!': ['#ffffff', '#ffb0bd', '#ff5d73'],
  'LIKE THIS!': ['#ffffff', '#c8ffb0', '#6fe37a'],
  'WHIFF!': ['#ffffff', '#ffc0d8', '#ff5d8a'],
  'STRIKE!': ['#ffffff', '#ffd9b0', '#ff8a3c'],
  'SIDE RETIRED!': ['#ffffff', '#ffb0bd', '#ff4d6a'],
  OUTS: ['#ffffff', '#e3e8ff', '#a9b6e0'],
};

function makeWordTexture(word, sub = null) {
  const W = 512, H = sub ? 176 : 128;
  const c = document.createElement('canvas');
  c.width = W; c.height = H;
  const g = c.getContext('2d');
  g.clearRect(0, 0, W, H);
  const [c0, c1, c2] = WORD_GRAD[word] || WORD_GRAD['LINE DRIVE'];
  // A dark rounded plate behind the word: over a multicoloured crowd, a
  // stroke alone left "HOME RUN!" beige-on-busy and hard to read.
  if (word !== 'OUTS') {
    // Opaque: at 0.9 the pennants behind a GRAND SLAM! showed through as
    // dark triangles across the word.
    g.fillStyle = 'rgb(16,11,40)';
    g.strokeStyle = c2;
    g.lineWidth = 5;
    g.beginPath();
    g.roundRect(14, 10, W - 28, H - 20, 30);
    g.fill();
    g.stroke();
  }
  // Shrink to fit: at 78px "GRAND SLAM!" plus its 18px stroke ran off the
  // 512px canvas and lost the "!".
  let px = sub ? 74 : 78;
  const setFont = () => { g.font = `900 ${px}px system-ui, -apple-system, "Segoe UI", sans-serif`; };
  setFont();
  while (px > 40 && g.measureText(word).width + 70 > W) { px -= 2; setFont(); }
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  const cy = sub ? 70 : H / 2;
  // Chunky outline + drop shadow: legible over grass, sky or a particle burst.
  g.lineJoin = 'round';
  g.strokeStyle = 'rgba(10,7,24,0.95)';
  g.lineWidth = 14;
  g.strokeText(word, W / 2, cy + 4);
  g.strokeText(word, W / 2, cy);
  const grad = g.createLinearGradient(0, cy - px * 0.5, 0, cy + px * 0.5);
  grad.addColorStop(0, c0);
  grad.addColorStop(0.55, c1);
  grad.addColorStop(1, c2);
  g.fillStyle = grad;
  g.fillText(word, W / 2, cy);
  if (sub) {
    // The timing grade, small, under the result — one badge instead of two
    // words competing for the same patch of screen.
    g.font = '800 36px system-ui, -apple-system, "Segoe UI", sans-serif';
    g.lineWidth = 8;
    g.strokeText(sub, W / 2, 132);
    g.fillStyle = '#fff6d8';
    g.fillText(sub, W / 2, 132);
  }
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.needsUpdate = true;
  return tex;
}
