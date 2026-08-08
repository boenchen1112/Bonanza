/**
 * Finale Fever — the set.
 *
 * Everything geometric that is NOT a character: the two podiums, the conductor's
 * stand, the boss's baton, the strike zone the orbs fly into, the note orbs
 * themselves, and the two practical lights that carry the duel.
 *
 * Two constraints shaped all of it.
 *
 * **Draw calls.** The environment kit is already spending ~15 and two full
 * characters spend 40, so everything here is either instanced or merged. The
 * orbs — of which sixteen can be in the air at once during a groove phrase —
 * are two InstancedMeshes total: a solid core and an additive halo.
 *
 * **Readability under a tempo ramp.** At 202bpm an eighth is 148ms, so an orb
 * must be legible in about four frames. Hence: colour by lane (the only thing
 * the player must decide), size by weight, and a scale pulse that spikes in the
 * last quarter-beat of flight so arrival is visible in peripheral vision.
 */

import * as THREE from 'three';
import { clamp01, easeOutCubic, smootherstep } from '../../core/util.js';

/** Where the duel happens, in world space. Read by index.js. */
export const PLACE = {
  boss: [-3.7, 0.92, -1.25],
  bossYaw: 1.06,
  player: [3.15, 0, 0.35],
  playerYaw: -0.92,
  /** Baton tip resting point — where orbs are launched from. */
  launch: [-2.75, 2.65, -0.95],
  /** The point the player defends. Orbs converge here. */
  strike: [1.72, 1.28, 0.55],
};

const MAX_ORBS = 72;

/** Lane colours. Gold is "the button", cyan/magenta are the two chord lanes. */
const LANE_COLOR = { 0: 0xffd93d, '-1': 0x5ce1ff, 1: 0xff5fd0 };
const KIND_SIZE = { tap: 0.30, swing: 0.52, chord: 0.34, solo: 0.30, finale: 0.8 };

export function createSet(ctx, root) {
  const THREEg = THREE;
  /** Everything we allocated, so dispose() is a loop and not a list. */
  const owned = [];
  const keep = (x) => { owned.push(x); return x; };

  // ------------------------------------------------------------------ stage
  // A shallow performance floor sitting inside the arena the env kit built.
  // It exists to give both duellists a plinth so their feet read as ON
  // something, which is most of what sells a 2.5D stage as a place.

  const floorGeo = keep(new THREE.CylinderGeometry(7.6, 7.9, 0.22, 56));
  const floorMat = keep(new THREE.MeshStandardMaterial({ color: 0x2a0a22, roughness: 0.85, metalness: 0.05 }));
  const floor = new THREE.Mesh(floorGeo, floorMat);
  floor.position.set(-0.2, -0.11, -0.6);
  root.add(floor);

  const lipGeo = keep(new THREE.TorusGeometry(7.62, 0.09, 6, 72));
  const lipMat = keep(new THREE.MeshBasicMaterial({ color: 0xffb45a, toneMapped: false }));
  const lip = new THREE.Mesh(lipGeo, lipMat);
  lip.rotation.x = -Math.PI / 2;
  lip.position.set(-0.2, 0.01, -0.6);
  root.add(lip);

  // ----------------------------------------------------------------- podium
  const podGeo = keep(new THREE.CylinderGeometry(1.22, 1.42, PLACE.boss[1], 28));
  const podMat = keep(new THREE.MeshStandardMaterial({ color: 0x4a0c26, roughness: 0.7, metalness: 0.1 }));
  const podium = new THREE.Mesh(podGeo, podMat);
  podium.position.set(PLACE.boss[0], PLACE.boss[1] * 0.5, PLACE.boss[2]);
  root.add(podium);

  const podRingGeo = keep(new THREE.TorusGeometry(1.26, 0.06, 6, 40));
  const podRingMat = keep(new THREE.MeshBasicMaterial({ color: 0xff4d6d, toneMapped: false }));
  const podRing = new THREE.Mesh(podRingGeo, podRingMat);
  podRing.rotation.x = -Math.PI / 2;
  podRing.position.set(PLACE.boss[0], PLACE.boss[1] + 0.01, PLACE.boss[2]);
  root.add(podRing);

  // ------------------------------------------------------- conductor's stand
  // The single most identifying prop in the frame. Even in silhouette a raked
  // plate on a thin post says "this one is conducting" before anything moves.
  const standGroup = new THREE.Group();
  const postGeo = keep(new THREE.CylinderGeometry(0.045, 0.06, 1.05, 10));
  const standMat = keep(new THREE.MeshStandardMaterial({ color: 0x1b0713, roughness: 0.5, metalness: 0.35 }));
  const post = new THREE.Mesh(postGeo, standMat);
  post.position.y = 0.52;
  standGroup.add(post);
  const plateGeo = keep(new THREE.BoxGeometry(0.92, 0.05, 0.62));
  const plate = new THREE.Mesh(plateGeo, standMat);
  plate.position.y = 1.06;
  plate.rotation.x = -0.62;
  standGroup.add(plate);
  const sheetGeo = keep(new THREE.PlaneGeometry(0.8, 0.52));
  const sheetMat = keep(new THREE.MeshBasicMaterial({ color: 0xfff0d8, toneMapped: false, side: THREE.DoubleSide }));
  const sheet = new THREE.Mesh(sheetGeo, sheetMat);
  sheet.position.set(0, 1.11, 0.02);
  sheet.rotation.x = -0.62 - Math.PI / 2;
  standGroup.add(sheet);
  standGroup.position.set(PLACE.boss[0] + 1.05, PLACE.boss[1], PLACE.boss[2] + 0.75);
  standGroup.rotation.y = -0.5;
  root.add(standGroup);

  // ------------------------------------------------------------------ baton
  // Parented to the boss's right hand by index.js, so the animation rig swings
  // it for free and the ictus is a real direction change, not a keyframe.
  const batonGroup = new THREE.Group();
  const stickGeo = keep(new THREE.CylinderGeometry(0.018, 0.032, 0.72, 8));
  const stickMat = keep(new THREE.MeshStandardMaterial({ color: 0xf4e6cf, roughness: 0.35 }));
  const stick = new THREE.Mesh(stickGeo, stickMat);
  stick.position.y = -0.36;
  batonGroup.add(stick);
  const tipGeo = keep(new THREE.SphereGeometry(0.075, 12, 8));
  const tipMat = keep(new THREE.MeshBasicMaterial({ color: 0xffe9a8, toneMapped: false }));
  const batonTip = new THREE.Mesh(tipGeo, tipMat);
  batonTip.position.y = -0.72;
  batonGroup.add(batonTip);
  batonGroup.rotation.x = 0.35;

  // ------------------------------------------------------------- strike zone
  // Where the player's answers land. It breathes on every beat so the tempo is
  // readable even with your eyes parked on the boss.
  const zone = new THREE.Group();
  zone.position.set(PLACE.strike[0], PLACE.strike[1], PLACE.strike[2]);
  root.add(zone);

  const ringGeo = keep(new THREE.TorusGeometry(0.86, 0.055, 8, 48));
  const ringMat = keep(new THREE.MeshBasicMaterial({ color: 0xffd93d, toneMapped: false, transparent: true, opacity: 0.9 }));
  const ring = new THREE.Mesh(ringGeo, ringMat);
  zone.add(ring);

  const ring2Geo = keep(new THREE.TorusGeometry(1.16, 0.022, 6, 44));
  const ring2Mat = keep(new THREE.MeshBasicMaterial({ color: 0xff8a3c, toneMapped: false, transparent: true, opacity: 0.45 }));
  const ring2 = new THREE.Mesh(ring2Geo, ring2Mat);
  zone.add(ring2);

  // Two lane pips flanking the ring: the chord's left/right, permanently
  // visible so a chord orb pair has somewhere to point at.
  const pipGeo = keep(new THREE.OctahedronGeometry(0.15, 0));
  const pipL = new THREE.Mesh(pipGeo, keep(new THREE.MeshBasicMaterial({ color: LANE_COLOR['-1'], toneMapped: false, transparent: true, opacity: 0.55 })));
  const pipR = new THREE.Mesh(pipGeo, keep(new THREE.MeshBasicMaterial({ color: LANE_COLOR[1], toneMapped: false, transparent: true, opacity: 0.55 })));
  pipL.position.set(-1.34, 0, 0);
  pipR.position.set(1.34, 0, 0);
  zone.add(pipL, pipR);

  // ------------------------------------------------------------------- orbs
  const orbGeo = keep(new THREE.IcosahedronGeometry(1, 1));
  const orbMat = keep(new THREE.MeshBasicMaterial({ toneMapped: false }));
  const orbMesh = new THREE.InstancedMesh(orbGeo, orbMat, MAX_ORBS);
  orbMesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(MAX_ORBS * 3), 3);
  orbMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
  orbMesh.frustumCulled = false;
  orbMesh.userData.keepMaterial = true;
  root.add(orbMesh);

  const haloMat = keep(new THREE.MeshBasicMaterial({
    toneMapped: false, transparent: true, opacity: 0.30,
    blending: THREE.AdditiveBlending, depthWrite: false,
  }));
  const haloMesh = new THREE.InstancedMesh(orbGeo, haloMat, MAX_ORBS);
  haloMesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(MAX_ORBS * 3), 3);
  haloMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
  haloMesh.frustumCulled = false;
  haloMesh.renderOrder = 3;
  haloMesh.userData.keepMaterial = true;
  root.add(haloMesh);

  /** @type {{on:boolean, note:any, from:number, to:number, lane:number, kind:string, seed:number}[]} */
  const orbs = new Array(MAX_ORBS);
  for (let i = 0; i < MAX_ORBS; i++) {
    orbs[i] = { on: false, note: null, from: 0, to: 1, lane: 0, kind: 'tap', seed: i * 0.618, pop: 0 };
  }

  const _m = new THREE.Matrix4();
  const _p = new THREE.Vector3();
  const _q = new THREE.Quaternion();
  const _s = new THREE.Vector3();
  const _e = new THREE.Euler();
  const _c = new THREE.Color();
  const _c2 = new THREE.Color();
  const HIDE = new THREE.Matrix4().makeScale(0, 0, 0);

  function spawnOrb(note, fromBeat, toBeat) {
    for (let i = 0; i < MAX_ORBS; i++) {
      const o = orbs[i];
      if (o.on) continue;
      o.on = true; o.note = note; o.from = fromBeat; o.to = toBeat;
      o.lane = note.lane; o.kind = note.kind; o.pop = 0;
      note._orb = i;
      return i;
    }
    return -1;
  }

  function killOrb(i) {
    const o = orbs[i];
    if (!o.on) return;
    o.on = false;
    if (o.note && o.note._orb === i) o.note._orb = -1;
    o.note = null;
  }

  /** World position of an orb at flight fraction u (0 = launch, 1 = arrival). */
  function orbPos(u, lane, out) {
    const a = PLACE.launch, b = PLACE.strike;
    // Lane spread: chord orbs separate in the air and land on their pips, so
    // "which key" is answered by geometry a full beat before it is needed.
    const lx = lane * 1.34;
    const ly = lane * 0.0;
    const t = u;
    const it = 1 - t;
    // Quadratic bezier with the control point lifted — a real throw, not a lerp.
    const cx = (a[0] + b[0]) * 0.5 + lane * 0.9;
    const cy = Math.max(a[1], b[1]) + 1.55;
    const cz = (a[2] + b[2]) * 0.5 - 0.2;
    out.set(
      it * it * a[0] + 2 * it * t * cx + t * t * (b[0] + lx),
      it * it * a[1] + 2 * it * t * cy + t * t * (b[1] + ly),
      it * it * a[2] + 2 * it * t * cz + t * t * b[2]
    );
    return out;
  }

  // ----------------------------------------------------------------- lights
  // Practical lights, not house lights: the look system removes Directional /
  // Hemisphere / Ambient during its dress pass but leaves Point and Spot alone,
  // because those are always placed for a local reason. These two are the duel.
  const bossLight = new THREE.PointLight(0xff3b5c, 6, 14, 2);
  bossLight.position.set(PLACE.boss[0] - 0.4, PLACE.boss[1] + 2.6, PLACE.boss[2] + 1.2);
  root.add(bossLight);

  const playerLight = new THREE.PointLight(0xffd93d, 5, 13, 2);
  playerLight.position.set(PLACE.player[0] + 0.5, 2.9, PLACE.player[2] + 1.3);
  root.add(playerLight);

  // ------------------------------------------------------------------ state
  let ringPulse = 0;
  let zoneFlash = 0;
  let bossGlow = 1;
  let playerGlow = 1;

  const api = {
    root, zone, ring, ring2, batonGroup, batonTip, orbMesh, haloMesh,
    bossLight, playerLight, podRing, lip, sheet, standGroup,
    PLACE, MAX_ORBS,

    spawnOrb, killOrb,
    hitZone(strength = 1) { ringPulse = Math.max(ringPulse, strength); zoneFlash = Math.max(zoneFlash, strength); },
    setBossGlow(v) { bossGlow = v; },
    setPlayerGlow(v) { playerGlow = v; },

    /** Where an orb currently is — used to place the hit VFX on the orb. */
    orbWorld(note, out) {
      const i = note && note._orb;
      if (i === undefined || i === null || i < 0 || !orbs[i]?.on) {
        return out.set(PLACE.strike[0], PLACE.strike[1], PLACE.strike[2]);
      }
      const o = orbs[i];
      const u = clamp01((api._beat - o.from) / Math.max(1e-3, o.to - o.from));
      return orbPos(u, o.lane, out);
    },

    _beat: 0,

    /**
     * @param {number} dt
     * @param {number} beat
     * @param {{intensity:number, solo:boolean, heat:number}} st
     */
    update(dt, beat, time, st) {
      api._beat = beat;
      const frac = beat - Math.floor(beat);          // never `% 1`: beat goes negative
      const hop = 4 * frac * (1 - frac);

      // --- strike zone ------------------------------------------------------
      ringPulse = Math.max(0, ringPulse - dt * 6.5);
      zoneFlash = Math.max(0, zoneFlash - dt * 5.5);
      const rs = 1 + hop * 0.055 + ringPulse * 0.30;
      ring.scale.setScalar(rs);
      ring2.scale.setScalar(1 + hop * 0.10 + ringPulse * 0.16);
      ring2.rotation.z += dt * (0.5 + st.heat * 1.6);
      ringMat.opacity = (st.solo ? 0.55 : 0.85) + zoneFlash * 0.15;
      ring2Mat.opacity = 0.28 + hop * 0.18 + zoneFlash * 0.4;
      pipL.rotation.y += dt * 1.4;
      pipR.rotation.y -= dt * 1.4;
      pipL.position.y = Math.sin(time * 2.1) * 0.06;
      pipR.position.y = Math.sin(time * 2.1 + 1.6) * 0.06;

      // --- set dressing -----------------------------------------------------
      podRingMat.opacity = 1;
      podRing.scale.setScalar(1 + hop * 0.04);
      lip.scale.setScalar(1 + hop * 0.004);

      // --- lights -----------------------------------------------------------
      // Both practicals lift on the beat and the whole rig gains as the tempo
      // climbs: the room gets hotter, not just faster.
      bossLight.intensity = (2.0 + st.heat * 7.0) * bossGlow * (0.82 + hop * 0.28);
      playerLight.intensity = (3.2 + st.heat * 5.5) * playerGlow * (0.85 + hop * 0.3);
      bossLight.color.setHSL(0.98 - st.heat * 0.03, 0.85, 0.42 + st.heat * 0.12);

      // --- orbs -------------------------------------------------------------
      let live = 0;
      for (let i = 0; i < MAX_ORBS; i++) {
        const o = orbs[i];
        if (!o.on) { orbMesh.setMatrixAt(i, HIDE); haloMesh.setMatrixAt(i, HIDE); continue; }
        const span = Math.max(1e-3, o.to - o.from);
        const u = (beat - o.from) / span;
        if (u > 1.18 || (o.note && o.note.judged && u > 0.999)) { killOrb(i); orbMesh.setMatrixAt(i, HIDE); haloMesh.setMatrixAt(i, HIDE); continue; }
        live++;

        orbPos(clamp01(u), o.lane, _p);
        // Overshoot past the ring rather than stopping dead in it: a note the
        // player let through visibly sails past, which is the failure read.
        if (u > 1) _p.x += (u - 1) * 5.5;

        // Arrival pulse: the last quarter beat before contact, the orb swells.
        const near = smootherstep((u - 0.82) / 0.18);
        const base = KIND_SIZE[o.kind] ?? 0.30;
        const sc = base * (0.55 + 0.45 * easeOutCubic(clamp01(u * 1.6))) * (1 + near * 0.55)
          * (u > 1 ? Math.max(0, 1 - (u - 1) * 6) : 1);

        _e.set(beat * 1.9 + o.seed * 6, beat * 2.4 + o.seed * 9, 0);
        _q.setFromEuler(_e);
        _s.setScalar(sc);
        _m.compose(_p, _q, _s);
        orbMesh.setMatrixAt(i, _m);
        _s.setScalar(sc * (1.85 + near * 0.9));
        _m.compose(_p, _q, _s);
        haloMesh.setMatrixAt(i, _m);

        const hex = LANE_COLOR[String(o.lane)] ?? LANE_COLOR[0];
        const boost = 0.75 + near * 0.6;
        _c.setHex(hex, THREE.SRGBColorSpace);
        _c2.copy(_c).multiplyScalar(boost * 0.7);
        _c.multiplyScalar(boost + 0.4);
        orbMesh.setColorAt(i, _c);
        haloMesh.setColorAt(i, _c2);
      }
      orbMesh.count = MAX_ORBS;
      haloMesh.count = MAX_ORBS;
      orbMesh.instanceMatrix.needsUpdate = true;
      haloMesh.instanceMatrix.needsUpdate = true;
      if (orbMesh.instanceColor) orbMesh.instanceColor.needsUpdate = true;
      if (haloMesh.instanceColor) haloMesh.instanceColor.needsUpdate = true;
      orbMesh.visible = live > 0;
      haloMesh.visible = live > 0;
      api.liveOrbs = live;
    },

    liveOrbs: 0,

    dispose() {
      for (const o of owned) o.dispose?.();
      owned.length = 0;
      orbMesh.dispose();
      haloMesh.dispose();
      root.remove(orbMesh, haloMesh);
    },
  };

  void THREEg;
  return api;
}
