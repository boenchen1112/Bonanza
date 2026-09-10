/**
 * Character showcase scene.  [chars agent owns this directory]
 *
 * Not a minigame — a rig/animation test bench that happens to implement the
 * standard scene interface so the shell and the critic harness can route to
 * it. It walks the whole cast through every animation state, one bar each, on
 * the beat, with the crowd reacting behind them.
 *
 * `setSilhouette(true)` flattens everything to black on white. That is the
 * acceptance test for this piece: if you cannot name the verdict from the
 * silhouette alone, the pose is not finished.
 */

import * as THREE from 'three';
import { damp, clamp01 } from '../core/util.js';
import { clone as cloneSkinned } from 'three/examples/jsm/utils/SkeletonUtils.js';
import { makeCast, makeCrowd, BUILD_IDS } from './index.js';
import { loadGLB } from '../assets/index.js';
import { CLIPS } from './clips.gen.js';

const VERDICTS = ['perfect', 'great', 'good', 'miss'];

/** One bar per step. The order tells a story: calm, braced, swing, results. */
const SCRIPT = [
  { name: 'IDLE', kind: 'idle' },
  { name: 'READY', kind: 'ready' },
  // Mocap steps: Mixamo motion retargeted onto the toy rig (retarget.js);
  // the grey figure upstage is the source skeleton playing the same frame.
  { name: 'MOCAP SWING', kind: 'mocapSwing' },
  { name: 'WINDUP / STRIKE', kind: 'swing' },
  { name: 'MOCAP PITCH', kind: 'mocapPitch' },
  { name: 'MOCAP PITCH', kind: 'hold' },
  { name: 'MOCAP PITCH', kind: 'hold' },
  { name: 'VERDICTS', kind: 'verdicts', rot: 0 },
  { name: 'VERDICTS', kind: 'verdicts', rot: 1 },
  { name: 'VERDICTS', kind: 'verdicts', rot: 2 },
  { name: 'VERDICTS', kind: 'verdicts', rot: 3 },
  { name: 'MOCAP DANCE', kind: 'mocapDance' },
  { name: 'MOCAP DANCE', kind: 'hold' },
  { name: 'TAUNT', kind: 'taunt' },
  { name: 'MOCAP TAUNT', kind: 'mocapTaunt' },
  { name: 'MOCAP TAUNT', kind: 'hold' },
  { name: 'CELEBRATE', kind: 'celebrate' },
  { name: 'FAIL', kind: 'fail' },
];

let root = null;
let cast = null;
let crowd = null;
let ctxRef = null;
let step = -1;
let lastBeatInt = -1e9;
let silhouette = false;
let savedBg = null;
let camDrift = 0;
/** The mocap SOURCE: the raw Mixamo rig upstage, mirroring member 0's clip. */
let ybot = null;
let ybotMixer = null;
let ybotActions = null;
let ybotShown = null;

function light(scene) {
  const hemi = new THREE.HemisphereLight(0x9fc4ff, 0x2a1a3a, 1.15);
  scene.add(hemi);

  const key = new THREE.DirectionalLight(0xfff2dd, 2.3);
  key.position.set(4.5, 8, 6.5);
  scene.add(key);

  // Rim from behind: the single most valuable light for silhouette reading.
  const rim = new THREE.DirectionalLight(0x88d5ff, 1.5);
  rim.position.set(-5, 4.5, -7);
  scene.add(rim);

  const fill = new THREE.DirectionalLight(0xff9ec4, 0.55);
  fill.position.set(-6, 2, 5);
  scene.add(fill);
  return { hemi, key, rim, fill };
}

function stageGeo(scene) {
  const g = new THREE.Group();

  const floor = new THREE.Mesh(
    new THREE.CircleGeometry(7.4, 48),
    new THREE.MeshStandardMaterial({ color: 0x241f4a, roughness: 0.92, metalness: 0 })
  );
  floor.rotation.x = -Math.PI / 2;
  g.add(floor);

  const lip = new THREE.Mesh(
    new THREE.TorusGeometry(7.4, 0.16, 6, 60),
    new THREE.MeshStandardMaterial({ color: 0xffd93d, roughness: 0.4, metalness: 0.1 })
  );
  lip.rotation.x = -Math.PI / 2;
  lip.position.y = 0.02;
  g.add(lip);

  const ground = new THREE.Mesh(
    new THREE.CircleGeometry(60, 32),
    new THREE.MeshStandardMaterial({ color: 0x11102a, roughness: 1 })
  );
  ground.rotation.x = -Math.PI / 2;
  ground.position.y = -0.06;
  g.add(ground);

  scene.add(g);
  return g;
}

const demo = {
  id: 'chars-demo',
  name: 'Character Showcase',
  blurb: 'Every state, every build, on the beat.',
  bpm: 118,
  durationBars: 999,
  controls: 'a',

  async load(ctx) {
    ctxRef = ctx;
    root = new THREE.Group();
    ctx.scene.add(root);
    ctx.scene.background = new THREE.Color(0x0b0a1a);
    ctx.scene.fog = new THREE.Fog(0x0b0a1a, 22, 58);

    light(ctx.scene);
    root.add(stageGeo(ctx.scene));

    cast = makeCast({
      scene: ctx.scene,
      count: 4,
      builds: BUILD_IDS,      // round, tall, small, wide — one of each
      spacing: 2.15,
      detail: 'full',
      seed: 0x517,
    });
    cast.group.position.z = 0.4;

    crowd = makeCrowd({ rows: 7, perRow: 44, radius: 9.2, seed: 0xbea7 });
    crowd.mesh.position.y = 0.2;
    ctx.scene.add(crowd.mesh);
    crowd.setEnergy(0.45);

    const gltf = await loadGLB('ybot');
    ybot = cloneSkinned(gltf.scene);
    ybot.position.set(0, 0, -3.2);
    ybot.scale.multiplyScalar(1.1);
    // Neutral grey: it is reference, not cast.
    ybot.traverse((o) => { if (o.isMesh) o.material = new THREE.MeshStandardMaterial({ color: 0x6d6f86, roughness: 0.7 }); });
    root.add(ybot);
    ybotMixer = new THREE.AnimationMixer(ybot);
    ybotActions = Object.fromEntries(gltf.animations.map((a) => [a.name, ybotMixer.clipAction(a)]));
    ybotShown = null;

    ctx.camera.position.set(0, 2.35, 8.6);
    ctx.camera.lookAt(0, 1.15, 0);
    ctx.fx?.attach?.(ctx.scene);

    step = -1;
    lastBeatInt = -1e9;
    camDrift = 0;
  },

  start(ctx) {
    ctx.clock?.setBpm?.(demo.bpm);
    if (ctx.clock && !ctx.clock.running) ctx.clock.start(ctx.clock.now() + 0.15, 0);
    ctx.ui?.banner?.('CHARACTER SHOWCASE', { sub: 'every state, on the beat', life: 2.0 });
  },

  update(ctx, dt, beat) {
    const b = Math.floor(beat);

    // Everything is driven off the float beat rather than off scheduled
    // callbacks, so the harness can freeze the scene at an exact beat and get
    // the same frame every time.
    if (b !== lastBeatInt) {
      const jumped = Math.abs(b - lastBeatInt) > 1;
      lastBeatInt = b;
      onBeat(ctx, b, beat, jumped);
    }

    cast.update(dt, beat);
    crowd.update(dt, beat);
    mirrorSource(dt);

    // The camera is never still. Slow, low-amplitude, never fights the read.
    camDrift += dt;
    const tgtX = Math.sin(camDrift * 0.23) * 0.55;
    const tgtY = 2.35 + Math.sin(camDrift * 0.31 + 1.2) * 0.10;
    ctx.camera.position.x = damp(ctx.camera.position.x, tgtX, 1.6, dt);
    ctx.camera.position.y = damp(ctx.camera.position.y, tgtY, 1.6, dt);
    ctx.camera.lookAt(0, 1.15, 0);
  },

  input(ctx, events) {
    for (const e of events) {
      if (!e.down) continue;
      if (e.action === 'a') {
        for (const m of cast.members) m.anim.strike({ power: 1 });
        crowd.hype(0.5);
      } else if (e.action === 'b') {
        demo.setSilhouette(!silhouette);
      }
    }
  },

  result() { return null; },

  dispose(ctx) {
    cast?.dispose();
    crowd?.dispose();
    root?.traverse((o) => { o.geometry?.dispose?.(); o.material?.dispose?.(); });
    ybotMixer?.stopAllAction();
    root = null; cast = null; crowd = null; ctxRef = null;
    ybot = null; ybotMixer = null; ybotActions = null; ybotShown = null;
    if (ctx?.scene) { ctx.scene.overrideMaterial = null; ctx.scene.fog = null; }
  },

  // ------------------------------------------------------------- test hooks

  /** Black-on-white silhouette check. */
  setSilhouette(on) {
    silhouette = !!on;
    const scene = ctxRef?.scene;
    if (!scene) return silhouette;
    if (silhouette) {
      savedBg = scene.background;
      scene.background = new THREE.Color(0xf2f4ff);
      scene.fog = null;
      scene.overrideMaterial = new THREE.MeshBasicMaterial({ color: 0x000000 });
    } else {
      scene.overrideMaterial?.dispose?.();
      scene.overrideMaterial = null;
      scene.background = savedBg || new THREE.Color(0x0b0a1a);
      scene.fog = new THREE.Fog(0x0b0a1a, 22, 58);
    }
    return silhouette;
  },

  /** Force a script step (0..SCRIPT.length-1) regardless of the beat. */
  setStep(i) {
    step = ((i | 0) % SCRIPT.length + SCRIPT.length) % SCRIPT.length;
    applyStep(ctxRef, step, 0, true);
    return SCRIPT[step].name;
  },

  get script() { return SCRIPT; },
  get stepName() { return SCRIPT[Math.max(0, step)]?.name ?? '—'; },
  get cast() { return cast; },
  get crowd() { return crowd; },
};

// ------------------------------------------------------------ mocap source

/**
 * Show the source rig at the exact clip frame member 0 is retargeting, so the
 * two can be compared side by side; idle loop otherwise.
 */
function mirrorSource(dt) {
  if (!ybotMixer || !cast) return;
  const anim = cast.members[0].anim;
  const t = anim.clipTime;
  const want = t !== null ? anim.variant.name : 'idle';
  if (want !== ybotShown) {
    ybotMixer.stopAllAction();
    ybotActions[want]?.reset().play();
    ybotShown = want;
  }
  if (t !== null) {
    ybotActions[want].time = t;
    ybotMixer.update(0);
  } else {
    ybotMixer.update(dt);
  }
}

// ------------------------------------------------------------------ script

function onBeat(ctx, beatInt, beat, jumped) {
  const barBeat = ((beatInt % 4) + 4) % 4;
  const bar = Math.floor(beatInt / 4);
  const s = ((bar % SCRIPT.length) + SCRIPT.length) % SCRIPT.length;
  if (s !== step || jumped) {
    step = s;
    applyStep(ctx, step, barBeat, true);
  } else {
    applyStep(ctx, step, barBeat, false);
  }
}

function applyStep(ctx, s, barBeat, entering) {
  if (!cast) return;
  const def = SCRIPT[s];
  const prevName = SCRIPT[(s - 1 + SCRIPT.length) % SCRIPT.length].name;
  if (entering && def.name !== prevName) ctx?.ui?.banner?.(def.name, { life: 1.1 });
  const spb = ctx?.clock?.spb ?? 0.5;
  const bpm = ctx?.clock?.bpm ?? 118;

  switch (def.kind) {
    case 'mocapSwing': {
      // Two beats of coil ending exactly at the bat-meets-ball frame, the
      // follow-through on beat 3 — the same timing a note would ask for.
      const contact = CLIPS.swing.contact;
      if (barBeat === 0) {
        for (const m of cast.members) m.anim.play('swing', { to: contact, dur: spb * 2, hold: true, face: 'focus', beat: 0.15 });
        crowd.setEnergy(0.6);
      } else if (barBeat === 2) {
        for (const m of cast.members) m.anim.play('swing', { from: contact, face: 'fierce', beat: 0.1 });
        crowd.hype(0.5);
      }
      break;
    }

    case 'mocapPitch':
      if (entering) for (const m of cast.members) m.anim.play('pitch', { face: 'focus', beat: 0.15 });
      break;

    case 'mocapDance':
      if (entering) {
        cast.members.forEach((m) => m.anim.play('dance', { beatLock: true, bpm, face: 'groove', beat: 0.2, beat0: Math.round(ctx.clock.beat) }));
        crowd.setEnergy(0.8);
      }
      break;

    case 'mocapTaunt':
      if (entering) for (const m of cast.members) m.anim.play('taunt', { face: 'smug', beat: 0.3 });
      break;

    case 'hold':
      break;

    case 'idle':
      if (entering) { cast.all('idle'); crowd.setEnergy(0.40); }
      break;

    case 'ready':
      if (entering) { cast.all('ready'); crowd.setEnergy(0.55); }
      break;

    case 'swing':
      // Anticipation gets a full beat of lead time — the windup is not a
      // flourish, it is the telegraph that makes the strike predictable.
      if (barBeat === 0 || barBeat === 2) {
        for (const m of cast.members) m.anim.windup();
      } else if (barBeat === 1 || barBeat === 3) {
        for (const m of cast.members) m.anim.strike({ power: 1 });
        crowd.hype(0.35);
      }
      break;

    case 'verdicts':
      if (barBeat === 0) {
        cast.members.forEach((m, i) => {
          const v = VERDICTS[(i + def.rot) % VERDICTS.length];
          m.anim.react(v);
        });
        crowd.hype(0.5);
      }
      break;

    case 'taunt':
      if (entering) { cast.all('taunt', { force: true }); crowd.setEnergy(0.5); }
      break;

    case 'celebrate':
      if (entering) {
        cast.members.forEach((m, i) => m.anim.setState('celebrate', {
          variant: i % 2 ? 'great' : 'perfect', force: true, dur: 4,
        }));
        crowd.hype(1.0);
        crowd.setEnergy(0.95);
      }
      break;

    case 'fail':
      if (entering) {
        cast.all('fail', { variant: 'miss', force: true, dur: 4 });
        crowd.deflate(1);
      }
      break;
    default:
      break;
  }
}

export default demo;
