/**
 * Pooled VFX.  [render agent owns this directory]
 *
 * One InstancedMesh per effect family, zero allocation per burst and none per
 * frame. A rhythm game fires effects on every beat; a GC pause on a downbeat is
 * a missed note.
 *
 * ── The one call that matters ──────────────────────────────────────────────
 *
 *     fx.verdict('perfect', [x, y, z], { dir: [1, 0.4, 0], combo: 12 })
 *
 * That composes the whole layered response — contact flare, directional shard
 * cone, inner and outer shockwaves, converging speed lines, spark spray,
 * ground bloom, world-space callout, confetti, echo — with the timing offsets
 * and per-verdict silhouettes defined and justified in `recipes.js`. Every
 * minigame calling it gets identical, tuned feedback for free, which is the
 * only way five games ship feeling like one product.
 *
 * Everything else here is a primitive that `verdict()` composes from, exposed
 * because a minigame will eventually need a bespoke moment.
 *
 * ── Families ──────────────────────────────────────────────────────────────
 *   flare    star sprites — contact cores, accents         (additive, billboard)
 *   sparks   4-point sparkles — the spray                  (additive, billboard)
 *   shards   directional debris, stretched by speed        (additive, stretch)
 *   streaks  speed lines converging on the hit point       (additive, stretch)
 *   confetti flat quads with real 3D tumbling              (normal,   tumble)
 *   smoke    the whiff puff                                (additive, billboard)
 *   decals   ground scorch / impact bloom                  (additive, ground)
 *   ambient  beat-synced motes so nothing is ever static   (additive, billboard)
 *   rings    shader shockwaves: thickness anim + distortion + any orientation
 *   trails   ribbon geometry following a moving object
 *   popText  in-world callouts from a boot-baked word atlas
 *
 * Peak cost is ~11 draw calls with every family alive; families with nothing
 * alive set `visible = false` and cost nothing at all.
 */

import * as THREE from 'three';
import { clamp01, makeRng } from '../../core/util.js';
import { FEEL, feelForCombo, comboTier, isMilestone, heatUp } from '../../core/feel.js';
import { spriteAtlas, spriteRect, SPRITE } from './textures.js';
import { ParticlePool, MODE, FADE } from './pool.js';
import { RingPool } from './rings.js';
import { TrailSystem } from './trails.js';
import { PopTextPool } from './poptext.js';
import { L, RECIPE, VOCAB } from './recipes.js';

/** Capacity per family at maximum quality. Memory is cheap; a stall is not. */
const CAP = {
  flare: 64, sparks: 512, shards: 256, streaks: 128,
  confetti: 320, smoke: 128, decals: 48, ambient: 128, rings: 48, text: 24,
};

/**
 * Quality tiers. A weak device must drop EFFECTS, never frames — so the knobs
 * are particle counts and whole optional families, not resolution.
 */
const QUALITY = {
  high: { mul: 1.00, ambient: 1, decals: 1, streaks: 1, echo: 1, confetti: 1.0 },
  med: { mul: 0.62, ambient: 1, decals: 1, streaks: 1, echo: 1, confetti: 0.65 },
  low: { mul: 0.32, ambient: 0, decals: 0, streaks: 0, echo: 0, confetti: 0.35 },
};

const PENDING = 160;

export function createFX({ stage, clock, bus = null }) {
  const rng = makeRng(0xfeed);
  const tex = spriteAtlas();
  let host = null;
  let time = 0;

  // ------------------------------------------------------------- families

  const pool = (cell, mode, max, blending, over) => {
    const p = new ParticlePool({
      max, texture: tex, uvRect: spriteRect(cell), mode, blending, rng,
    });
    if (over) p.defaults(over);
    return p;
  };

  const flare = pool(SPRITE.star6, MODE.BILLBOARD, CAP.flare, THREE.AdditiveBlending, {
    grav: 0, drag: 0, speed: 0, speedVar: 0, spinVar: 1.4, size: 0.55, sizeVar: 0.1,
    s0: 0.35, s1: 1.9, life: 0.15, lifeVar: 0.15, fade: FADE.LATE,
  });
  const sparks = pool(SPRITE.sparkle, MODE.BILLBOARD, CAP.sparks, THREE.AdditiveBlending, {
    grav: -9, drag: 2.4, size: 0.15, spinVar: 14, life: 0.5,
  });
  const shards = pool(SPRITE.shard, MODE.STRETCH, CAP.shards, THREE.AdditiveBlending, {
    grav: -6, drag: 3.2, size: 0.16, sizeVar: 0.4, life: 0.3, lifeVar: 0.4,
    stretch: 0.085, s0: 1, s1: 0.12, spinVar: 0,
  });
  const streaks = pool(SPRITE.streak, MODE.STRETCH, CAP.streaks, THREE.AdditiveBlending, {
    grav: 0, drag: 0, size: 0.14, sizeVar: 0.35, life: 0.13, lifeVar: 0.2,
    stretch: 0.055, s0: 1, s1: 0.5, fade: FADE.POP, spinVar: 0, speedVar: 0.15,
  });
  const confetti = pool(SPRITE.confetti, MODE.TUMBLE, CAP.confetti, THREE.NormalBlending, {
    grav: -7.5, drag: 1.1, turb: 2.6, size: 0.17, sizeVar: 0.3, aspect: 1.6,
    life: 1.5, lifeVar: 0.35, s0: 1, s1: 1, fade: FADE.LINEAR, tumble: 11,
  });
  const smoke = pool(SPRITE.smoke, MODE.BILLBOARD, CAP.smoke, THREE.AdditiveBlending, {
    grav: -1.1, drag: 3.4, size: 0.5, sizeVar: 0.4, life: 0.75, lifeVar: 0.3,
    s0: 0.5, s1: 1.9, fade: FADE.POP, alpha: 0.33, spinVar: 1.2, speed: 1.6,
  });
  const decals = pool(SPRITE.scorch, MODE.GROUND, CAP.decals, THREE.AdditiveBlending, {
    grav: 0, drag: 0, speed: 0, speedVar: 0, spinVar: 0.4, size: 1.5, sizeVar: 0.15,
    s0: 0.5, s1: 1.35, life: 0.95, lifeVar: 0.1, alpha: 0.5, fade: FADE.HOLD,
  });
  const ambient = pool(SPRITE.glow, MODE.BILLBOARD, CAP.ambient, THREE.AdditiveBlending, {
    grav: 0.16, drag: 0.4, speed: 0.35, size: FEEL.fx.ambient.size, sizeVar: 0.5,
    life: FEEL.fx.ambient.life, lifeVar: 0.35, s0: 0.4, s1: 1.0,
    fade: FADE.POP, alpha: 0.55, spinVar: 2,
  });

  const rings = new RingPool({ max: CAP.rings, rng });
  // A few rings that ignore depth: a climax ring big enough to reach the
  // ground was sliced flat by the field (`ring(pos, { overlay: true })`).
  const overlayRings = new RingPool({ max: 8, rng, depthTest: false });
  const trails = new TrailSystem({
    max: FEEL.fx.trail.maxTrails,
    segs: FEEL.fx.trail.segments,
    sampleHz: FEEL.fx.trail.sampleHz,
  });
  const popText = new PopTextPool({ max: CAP.text, words: VOCAB });

  const families = [
    ambient, decals, trails, smoke, confetti, sparks, shards, streaks, flare, rings, overlayRings, popText,
  ];

  // ------------------------------------------------------------- state

  let q = QUALITY[FEEL.fx.quality] || QUALITY.high;
  let qName = FEEL.fx.quality;
  const focus = [0, 0.55, 0];
  let groundY = null;         // null = derive from the hit position
  let combo = 0;
  let ambientBoost = 0;

  // Scratch — allocated once, reused forever.
  const nrm = [0, 0, 0];
  const tmpDir = [1, 0.4, 0];

  /**
   * Deferred layer queue. Preallocated records, scanned linearly; 160 slots is
   * ~8 simultaneous verdicts mid-flight, which no chart will ever exceed.
   */
  const pend = new Array(PENDING);
  for (let i = 0; i < PENDING; i++) {
    pend[i] = {
      on: false, t: 0, layer: 0, v: 'perfect',
      x: 0, y: 0, z: 0, dx: 1, dy: 0, dz: 0,
      color: 0xffffff, scale: 1, size: 1, count: 1, tier: 0, gy: 0, k: 0,
    };
  }
  let pendCursor = 0;

  function schedule(layer, delay, src, k = 0) {
    // Find a free record; if the queue is somehow saturated, overwrite the
    // oldest rather than silently dropping a layer of an impact.
    let r = null;
    for (let i = 0; i < PENDING; i++) {
      const c = pend[(pendCursor + i) % PENDING];
      if (!c.on) { r = c; pendCursor = (pendCursor + i + 1) % PENDING; break; }
    }
    if (!r) { r = pend[pendCursor]; pendCursor = (pendCursor + 1) % PENDING; }
    r.on = true;
    r.t = time + delay;
    r.layer = layer;
    r.k = k;
    r.v = src.v; r.x = src.x; r.y = src.y; r.z = src.z;
    r.dx = src.dx; r.dy = src.dy; r.dz = src.dz;
    r.color = src.color; r.scale = src.scale; r.size = src.size;
    r.count = src.count; r.tier = src.tier; r.gy = src.gy;
    return r;
  }

  /** The record `verdict()` fills before scheduling; never allocated. */
  const V = {
    v: 'perfect', x: 0, y: 0, z: 0, dx: 1, dy: 0.4, dz: 0,
    color: 0xffffff, scale: 1, size: 1, count: 1, tier: 0, gy: -1,
  };
  /** Scratch record for layers that fire inline (delay 0). Never queued. */
  const IMM = { on: false, t: 0, layer: 0, k: 0, ...V };
  /** Reused count-scale carrier, so `fire()` allocates nothing. */
  const CNT = { count: 1 };

  // ------------------------------------------------------------- helpers

  function readVec(v, out, dflt) {
    if (!v) { out[0] = dflt[0]; out[1] = dflt[1]; out[2] = dflt[2]; return out; }
    if (Array.isArray(v)) { out[0] = v[0]; out[1] = v[1]; out[2] = v[2]; }
    else { out[0] = v.x; out[1] = v.y; out[2] = v.z; }
    return out;
  }

  /**
   * A disc perpendicular to the hit direction is the honest way to draw an
   * impact ring — and it disappears the moment the hit is aimed at or away
   * from the camera. So the plane normal is biased toward the viewer: the ring
   * still tilts with the direction of the hit, but it can never be edge-on.
   */
  function orientedNormal(dx, dy, dz) {
    const cam = stage.camera;
    let cx = 0, cy = 0, cz = 1;
    if (cam) {
      const e = cam.matrixWorld.elements;
      cx = e[8]; cy = e[9]; cz = e[10];
    }
    let nx = dx * 0.55 + cx * 0.85;
    let ny = dy * 0.55 + cy * 0.85;
    let nz = dz * 0.55 + cz * 0.85;
    const l = Math.hypot(nx, ny, nz) || 1;
    nrm[0] = nx / l; nrm[1] = ny / l; nrm[2] = nz / l;
    return nrm;
  }

  const n = (base, r) => Math.max(1, Math.round(base * q.mul * r.count));

  // ------------------------------------------------------------- layers

  function fire(r) {
    const v = r.v;
    const B = FEEL.fx.burst[v] || FEEL.fx.burst.good;
    const sc = r.scale;
    const sz = r.size;
    CNT.count = r.count;
    const cnt = CNT;

    switch (r.layer) {
      // ACT 1 --------------------------------------------------------------
      case L.CORE: {
        // Two flares, different sizes and different durations, so even the
        // very first frame already has a foreground and a background element.
        const e = flare.begin();
        e.x = r.x; e.y = r.y; e.z = r.z;
        e.setColor(heatUp(r.color, 0.45));
        e.size = 0.52 * sc * sz; e.life = 0.16; e.s0 = 0.3; e.s1 = 2.1;
        flare.emit(1);
        e.setColor(FEEL.color.hot);
        e.size = 0.30 * sc * sz; e.life = 0.095; e.s0 = 0.55; e.s1 = 2.8;
        flare.emit(1);
        break;
      }
      case L.SHARDS: {
        const e = shards.begin();
        e.x = r.x; e.y = r.y; e.z = r.z;
        e.setDir([r.dx, r.dy, r.dz]);
        e.cone = 0.30;               // tight enough that the AIM is readable
        e.speed = 11.5 * sc;
        e.size = 0.165 * sz;
        e.setColor(heatUp(r.color, 0.25));
        shards.emit(n(B.shards, cnt));
        break;
      }
      case L.RING_IN: {
        rings.spawn([r.x, r.y, r.z], {
          color: heatUp(r.color, 0.5),
          life: 0.19,
          from: 0.22 * sc,
          to: 1.5 * sc * sz,
          thick0: 0.34, thick1: 0.055,
          wobble: 0.012,
          normal: orientedNormal(r.dx, r.dy, r.dz),
        });
        break;
      }
      case L.STREAKS: {
        // Converging speed lines. Spawned on a ring and aimed inward with a
        // speed that lands them on the hit point at the end of their life, so
        // they visibly *collapse* rather than merely shrink.
        const count = q.streaks ? n(B.streaks, cnt) : 0;
        if (!count) break;
        const cam = stage.camera;
        let rx = 1, ry = 0, rz = 0, ux = 0, uy = 1, uz = 0;
        if (cam) {
          const e2 = cam.matrixWorld.elements;
          rx = e2[0]; ry = e2[1]; rz = e2[2];
          ux = e2[4]; uy = e2[5]; uz = e2[6];
        }
        const R = 2.5 * sc;
        const life = 0.135;
        const e = streaks.begin();
        e.setColor(heatUp(r.color, 0.35));
        e.life = life;
        e.size = 0.135 * sz;
        e.speed = R / life;
        for (let i = 0; i < count; i++) {
          const a = (i / count) * Math.PI * 2 + rng() * 0.5;
          const rad = R * (0.82 + rng() * 0.36);
          const ox = (rx * Math.cos(a) + ux * Math.sin(a));
          const oy = (ry * Math.cos(a) + uy * Math.sin(a));
          const oz = (rz * Math.cos(a) + uz * Math.sin(a));
          e.x = r.x + ox * rad; e.y = r.y + oy * rad; e.z = r.z + oz * rad;
          e.dx = -ox; e.dy = -oy; e.dz = -oz;
          e.cone = 0;
          e.speed = rad / life;
          streaks.emit(1);
        }
        break;
      }

      // ACT 2 --------------------------------------------------------------
      case L.SPRAY: {
        const e = sparks.begin();
        e.x = r.x; e.y = r.y; e.z = r.z;
        e.jitter = 0.12;
        e.setDir([r.dx, r.dy, r.dz]);
        e.cone = 0.78;               // biased toward the hit, but nearly round
        e.speed = 7 * sc;
        e.size = 0.155 * sz;
        e.setColor(r.color);
        sparks.emit(n(B.sparks, cnt));
        break;
      }
      case L.RING_OUT: {
        const reach = (FEEL.fx.ringReach[v] ?? 1.5) * sc * sz;
        rings.spawn([r.x, r.y, r.z], {
          color: r.color,
          life: 0.44 + r.k * 0.06,
          from: 0.55 * sc,
          to: reach * (1 + r.k * 0.28),
          thick0: 0.22, thick1: 0.028,
          wobble: 0.035,
          alpha: 0.9 - r.k * 0.18,
        });
        if (r.k === 0 && v === 'perfect') {
          // ... and one lying flat on the floor, so the wave is visibly a
          // thing moving through the world rather than a decal on the lens.
          rings.spawn([r.x, r.gy + 0.03, r.z], {
            color: r.color, life: 0.6, from: 0.4 * sc, to: reach * 1.25,
            thick0: 0.16, thick1: 0.02, wobble: 0.05, alpha: 0.55,
            normal: [0, 1, 0],
          });
        }
        break;
      }
      case L.DECAL: {
        if (!q.decals) break;
        const e = decals.begin();
        e.x = r.x; e.y = r.gy + 0.02; e.z = r.z;
        e.size = FEEL.fx.decal.size * sc * sz;
        e.life = FEEL.fx.decal.life;
        e.setColor(r.color);
        e.alpha = 0.45;
        decals.emit(1);
        break;
      }

      // ACT 3 --------------------------------------------------------------
      case L.TEXT: {
        const label = FEEL.label[v] || '';
        const miss = v === 'miss';
        popText.spawn(label, [r.x, r.y + 0.5, r.z], {
          color: r.color,
          size: (miss ? 0.44 : 0.5) * sz,
          life: miss ? 0.95 : 0.78,
          rise: miss ? -0.55 : 1.15,    // the whiff SAGS. Failure is funny.
          tilt: miss ? -0.22 : 0,
          spin: miss ? -0.55 : 0,
        });
        break;
      }
      case L.CONFETTI: {
        // k === 1 marks a milestone shower, which has its own (larger) base.
        const base = r.k === 1 ? FEEL.milestone.confetti : B.confetti;
        const total = Math.round(n(base, cnt) * q.confetti);
        if (total <= 0) break;
        const cols = FEEL.color.party;
        const per = Math.max(1, Math.round(total / cols.length));
        const e = confetti.begin();
        e.x = r.x; e.y = r.y; e.z = r.z;
        e.jitter = 0.2;
        e.setDir([r.dx * 0.4, 1, r.dz * 0.4]);
        e.cone = 0.62;
        e.speed = 7.2 * sc;
        e.size = 0.17 * sz;
        for (let i = 0; i < cols.length; i++) {
          e.setColor(cols[i]);
          confetti.emit(per);
        }
        break;
      }
      case L.ECHO: {
        if (!q.echo) break;
        const reach = (FEEL.fx.ringReach[v] ?? 1.5) * sc * sz;
        rings.spawn([r.x, r.y, r.z], {
          color: heatUp(r.color, 0.6),
          life: 0.58, from: reach * 0.45, to: reach * 1.55,
          thick0: 0.055, thick1: 0.012, wobble: 0.055, alpha: 0.34,
        });
        break;
      }

      // MISS ---------------------------------------------------------------
      case L.PUFF: {
        const e = smoke.begin();
        e.x = r.x; e.y = r.y; e.z = r.z;
        e.jitter = 0.14;
        e.cone = 1;
        e.speed = 1.7;
        e.setColor(heatUp(r.color, 0.15), 0.75);
        smoke.emit(n(B.sparks, cnt));
        // a couple of sad sparks that just fall out of the puff
        const s = sparks.begin();
        s.x = r.x; s.y = r.y; s.z = r.z;
        s.cone = 1; s.speed = 1.6; s.speedVar = 0.7;
        s.grav = -11; s.drag = 1.2; s.size = 0.1; s.life = 0.6;
        s.setColor(r.color, 0.8);
        sparks.emit(Math.max(2, Math.round(4 * q.mul)));
        break;
      }
      case L.SAG_RING: {
        // Contracting, thickening, heavily distorted: the shape of something
        // deflating. The eye reads "expanding = force", so this reads as the
        // exact opposite, which is the point.
        rings.spawn([r.x, r.y, r.z], {
          color: r.color, life: 0.5,
          from: 1.5 * sc, to: 0.3,
          thick0: 0.08, thick1: 0.30,
          wobble: 0.095, alpha: 0.6,
        });
        break;
      }

      // COMBO --------------------------------------------------------------
      case L.MILESTONE_WAVE: {
        const k = r.k;
        rings.spawn([r.x, r.y, r.z], {
          color: heatUp(FEEL.color.combo, 0), life: 0.5 + k * 0.08,
          from: 0.3 + k * 0.5, to: 2.4 + k * 1.5,
          thick0: 0.16 - k * 0.03, thick1: 0.02,
          // The wave grows with k; so must its roundness, or a ×10 ring is a lumpy loop.
          wobble: 0.012 + k * 0.003, alpha: 0.85 - k * 0.2,
        });
        const e = flare.begin();
        e.x = r.x; e.y = r.y; e.z = r.z;
        e.setColor(FEEL.color.hot);
        e.size = (0.45 + k * 0.25) * sc; e.life = 0.22 + k * 0.05;
        e.s0 = 0.4; e.s1 = 2.0 + k * 0.4;
        flare.emit(1);
        break;
      }
      default: break;
    }
  }

  // ------------------------------------------------------------- verdict

  /**
   * THE entry point. One call per judged note; everything else is composed.
   *
   * @param {'perfect'|'great'|'good'|'miss'} v
   * @param {number[]|THREE.Vector3} pos world position of the impact
   * @param {object} [opts]
   * @param {number[]} [opts.dir]     hit direction; the shard cone and the
   *                                  inner ring orient to it
   * @param {number}   [opts.combo]   drives escalation; omit to use fx's own
   * @param {number}   [opts.scale]   whole-effect size multiplier
   * @param {number}   [opts.color]   override the verdict palette colour
   * @param {number}   [opts.groundY] floor height for the decal / ground wave
   * @param {boolean}  [opts.stage]   also drive camera shake + screen flash
   * @param {boolean}  [opts.text]    set false to suppress the world callout
   */
  function emitVerdict(v, pos, opts = {}) {
    if (!host) return;
    if (!RECIPE[v]) v = 'good';

    if (opts.combo !== undefined) combo = opts.combo;
    const f = feelForCombo(v, combo);

    readVec(pos, tmpDir, focus);
    V.v = v;
    V.x = tmpDir[0]; V.y = tmpDir[1]; V.z = tmpDir[2];
    const d = opts.dir;
    if (d) {
      const l = Math.hypot(d[0], d[1], d[2]) || 1;
      V.dx = d[0] / l; V.dy = d[1] / l; V.dz = d[2] / l;
    } else {
      // Default aim: up and out. A hit with no direction still has to look
      // like it SENT something, or the burst reads as an explosion in place.
      V.dx = 0.42; V.dy = 0.88; V.dz = 0.22;
    }
    V.color = opts.color ?? heatUp(f.color, f.hot);
    V.scale = (opts.scale ?? 1);
    V.size = f.sizeScale * (opts.scale ?? 1);
    V.count = f.countScale;
    V.tier = f.tier;
    V.gy = opts.groundY ?? (groundY ?? (V.y - FEEL.fx.decal.drop));

    const recipe = RECIPE[v];
    for (let i = 0; i < recipe.length; i++) {
      const layer = recipe[i][0];
      const delay = recipe[i][1];
      if (layer === L.TEXT && opts.text === false) continue;
      // `rings: false` — the big outward wave and its echo. In a game whose
      // subject stands AT the impact point they draw wobbly loops through the
      // character; the core flash and shards still sell the hit.
      if (opts.rings === false && (layer === L.RING_OUT || layer === L.ECHO)) continue;
      if (delay <= 0) {
        // Act 1 fires INLINE, on the same frame as the press. Queuing it would
        // cost a frame of latency, and that frame is the whole product.
        copyInto(IMM, V, layer, 0);
        fire(IMM);
      } else {
        schedule(layer, delay, V);
      }
    }

    // Combo escalation adds PHYSICAL layers, not just bigger numbers: extra
    // shockwaves trailing the first one, each wider, later, and fainter.
    for (let k = 1; opts.rings !== false && k <= f.extraRings; k++) {
      schedule(L.RING_OUT, 0.09 + k * 0.055, V, k);
    }

    if (opts.stage !== false && stage) {
      stage.shake(f.shake, [V.dx, V.dy * 0.5, 0]);
      if (f.flash > 0) stage.flash(f.flash, '#' + V.color.toString(16).padStart(6, '0'));
    }

    if (v !== 'miss') {
      ambientBoost = f.tier;
      if (isMilestone(combo)) milestone(combo, [V.x, V.y, V.z]);
    }
  }

  function copyInto(r, src, layer, k) {
    r.on = true; r.t = time; r.layer = layer; r.k = k;
    r.v = src.v; r.x = src.x; r.y = src.y; r.z = src.z;
    r.dx = src.dx; r.dy = src.dy; r.dz = src.dz;
    r.color = src.color; r.scale = src.scale; r.size = src.size;
    r.count = src.count; r.tier = src.tier; r.gy = src.gy;
  }

  /**
   * The combo-milestone flourish. A system, not a special case: every
   * milestone runs the same three-wave escalation, sized by tier, so tier 7
   * differs from tier 1 in the same way tier 1 differs from a normal hit.
   */
  function milestone(n0, pos) {
    if (!host) return;
    const tier = comboTier(n0);
    const M = FEEL.milestone;
    readVec(pos, tmpDir, focus);
    V.v = 'perfect';
    V.x = tmpDir[0]; V.y = tmpDir[1]; V.z = tmpDir[2];
    V.color = FEEL.color.combo;
    V.scale = 1 + tier * 0.06;
    V.size = 1 + tier * 0.08;
    V.count = 1 + tier * 0.22;
    V.tier = tier;
    V.gy = groundY ?? (V.y - FEEL.fx.decal.drop);

    for (let i = 0; i < M.waveDelays.length; i++) {
      schedule(L.MILESTONE_WAVE, M.waveDelays[i], V, i);
    }
    schedule(L.CONFETTI, 0.06, V, 1);

    const word = M.words[n0] || 'COMBO!';
    popText.spawn(popText.has(word) ? word : 'COMBO!', [V.x, V.y + 1.1, V.z], {
      color: FEEL.color.combo,
      size: 0.5 + tier * 0.045,
      life: 0.95,
      rise: 1.5,
    });

    if (stage) {
      stage.shake(M.shake * (1 + tier * 0.08), [0, 1, 0]);
      stage.flash(M.flash, '#ffffff');
    }
  }

  // ------------------------------------------------------------ primitives

  /** Radial burst of sparks. (Original API — preserved.) */
  function burst(pos, {
    count = 18, color = 0xffd93d, speed = 6, spread = 1, size = 0.14,
    life = 0.5, gravity = -9, dir = null, cone = Math.PI,
  } = {}) {
    const e = sparks.begin();
    readVec(pos, tmpDir, focus);
    e.x = tmpDir[0]; e.y = tmpDir[1]; e.z = tmpDir[2];
    if (dir) {
      e.setDir(dir);
      e.cone = clamp01((cone / Math.PI) * 0.5 + spread * 0.25);
    } else {
      e.cone = 1;
    }
    e.speed = speed; e.size = size; e.life = life; e.grav = gravity;
    e.setColor(color);
    sparks.emit(Math.max(1, Math.round(count * q.mul)));
  }

  /** Expanding shock ring — the single most legible "you hit it" cue. */
  function ring(pos, {
    color = 0xffffff, life = 0.42, from = 0.35, to = 3.2, billboard = true,
    thick0 = 0.24, thick1 = 0.035, wobble = 0.03, alpha = 1, normal = null, spin = 0, overlay = false,
  } = {}) {
    readVec(pos, tmpDir, focus);
    (overlay ? overlayRings : rings).spawn(tmpDir, {
      color, life, from, to, thick0, thick1, wobble, alpha, spin,
      normal: billboard ? null : (normal || [0, 1, 0]),
    });
  }

  /** Tumbling confetti. (Original API — preserved, now with real flat quads.) */
  function confettiBurst(pos, { count = 60, colors = FEEL.color.party, speed = 7.5, up = 1 } = {}) {
    readVec(pos, tmpDir, focus);
    const total = Math.max(1, Math.round(count * q.mul * q.confetti));
    const per = Math.max(1, Math.round(total / colors.length));
    const e = confetti.begin();
    e.x = tmpDir[0]; e.y = tmpDir[1]; e.z = tmpDir[2];
    e.jitter = 0.2;
    e.setDir([0, up, 0]);
    e.cone = 0.62;
    e.speed = speed;
    for (let i = 0; i < colors.length; i++) {
      e.setColor(colors[i]);
      confetti.emit(per);
    }
  }

  /** Directional impact cone — the primitive behind a hit that has an AIM. */
  function impact(pos, { dir = [0, 1, 0], color = 0xffffff, count = 16, speed = 11, size = 0.16, cone = 0.3, scale = 1 } = {}) {
    readVec(pos, tmpDir, focus);
    const e = shards.begin();
    e.x = tmpDir[0]; e.y = tmpDir[1]; e.z = tmpDir[2];
    e.setDir(dir);
    e.cone = cone;
    e.speed = speed * scale;
    e.size = size * scale;
    e.setColor(color);
    shards.emit(Math.max(1, Math.round(count * q.mul)));
  }

  /** Speed lines converging on a point. */
  function speedLines(pos, { color = 0xffffff, count = 12, radius = 2.5, size = 0.135 } = {}) {
    readVec(pos, tmpDir, focus);
    IMM.v = 'perfect';
    IMM.x = tmpDir[0]; IMM.y = tmpDir[1]; IMM.z = tmpDir[2];
    IMM.dx = 0; IMM.dy = 1; IMM.dz = 0;
    IMM.color = color; IMM.scale = radius / 2.5; IMM.size = size / 0.135;
    IMM.count = count / Math.max(1e-3, FEEL.fx.burst.perfect.streaks * q.mul);
    IMM.layer = L.STREAKS; IMM.k = 0; IMM.gy = 0;
    fire(IMM);
  }

  /** Ground scorch / impact bloom that fades. */
  function decal(pos, { color = 0xffffff, size = 1.5, life = FEEL.fx.decal.life, alpha = 0.45 } = {}) {
    if (!q.decals) return;
    readVec(pos, tmpDir, focus);
    const e = decals.begin();
    e.x = tmpDir[0]; e.y = tmpDir[1]; e.z = tmpDir[2];
    e.size = size; e.life = life; e.alpha = alpha;
    e.setColor(color);
    decals.emit(1);
  }

  /** In-world callout. `word` must be in recipes.js VOCAB. */
  function popTextAt(word, pos, opts) {
    readVec(pos, tmpDir, focus);
    return popText.spawn(word, tmpDir, opts);
  }

  /**
   * Acquire a ribbon trail. Feed it `handle.set(x, y, z)` each frame and call
   * `handle.release()` when the object stops. Returns null when all trails are
   * in use — callers must tolerate that rather than assume.
   */
  function trail(opts) { return trails.acquire({ width: FEEL.fx.trail.width, ...opts }); }

  // ------------------------------------------------------------- ambient

  /**
   * Beat-synced ambient sparkle. "Nothing is ever static" — but motion must
   * never compete with the thing the player has to read, so these are small,
   * dim, slow, and they live at the edges of the play volume, not on top of it.
   */
  function ambientBeat(beatInt) {
    if (!host || !q.ambient) return;
    const A = FEEL.fx.ambient;
    const down = ((beatInt % 4) + 4) % 4 === 0;
    const count = Math.round(
      (A.perBeat + (down ? A.downbeatBonus : 0) + ambientBoost) * q.mul
    );
    if (count <= 0) return;
    const e = ambient.begin();
    e.setColor(down ? FEEL.color.hot : FEEL.color.combo, down ? 1 : 0.72);
    e.alpha = down ? 0.6 : 0.4;
    e.size = A.size * (down ? 1.35 : 1);
    for (let i = 0; i < count; i++) {
      const a = rng() * Math.PI * 2;
      // hollow-ish annulus: motes stay out of the middle of the frame, where
      // the gameplay lives
      const rad = A.radius * (0.45 + rng() * 0.55);
      e.x = focus[0] + Math.cos(a) * rad;
      e.y = focus[1] + (rng() - 0.25) * A.height;
      e.z = focus[2] + Math.sin(a) * rad * 0.6 - rng() * 2;
      e.dx = (rng() - 0.5) * 0.4; e.dy = 1; e.dz = (rng() - 0.5) * 0.4;
      e.cone = 0.25;
      ambient.emit(1);
    }
  }

  if (clock?.onBeat) clock.onBeat(ambientBeat);

  // ------------------------------------------------------- legacy bridge

  /**
   * Compatibility shim. Minigames written before `fx.verdict()` existed only
   * emit a `judge` event on the bus; this gives them the full layered response
   * with no edit to their file. It disarms itself permanently the first time a
   * game calls `fx.verdict()` directly, so a game that composes its own
   * feedback never gets a second, uninvited copy.
   */
  let bridged = false;
  let auto = true;
  let autoCombo = 0;

  function tryBridge() {
    // The bus is injected by main.js; it used to be fished off the
    // `window.__BBB__` test API, which production builds don't ship.
    if (bridged || !bus || !bus.on) return;
    bridged = true;
    bus.on('judge', (j) => {
      if (!auto || !host || !j || !j.verdict) return;
      if (j.verdict === 'miss') autoCombo = 0; else autoCombo++;
      emitVerdict(j.verdict, focus, { combo: autoCombo });
    });
  }

  // --------------------------------------------------------- lifecycle

  function attach(scene) {
    host = scene;
    for (const f of families) scene.add(f.object3d);
    tryBridge();
  }

  function update(dt) {
    if (!host) return;
    time += dt;

    // drain the deferred layer queue
    for (let i = 0; i < PENDING; i++) {
      const r = pend[i];
      if (!r.on || r.t > time) continue;
      r.on = false;
      fire(r);
    }

    const cam = stage.camera;
    for (const f of families) f.update(dt, cam, time);
  }

  function reset() {
    for (const f of families) f.killAll();
    for (let i = 0; i < PENDING; i++) pend[i].on = false;
    if (host) for (const f of families) host.remove(f.object3d);
    host = null;
    combo = 0;
    autoCombo = 0;
    ambientBoost = 0;
    // Shared knobs go back to their defaults on every scene swap. Four games
    // turned auto-verdict off and one turned it back on, so whatever the last
    // minigame left behind was what the next one inherited. Unwinding belongs
    // here, not in five dispose() bodies that are free to forget.
    auto = true;
  }

  function setQuality(name) {
    if (!QUALITY[name]) return qName;
    qName = name;
    q = QUALITY[name];
    if (!q.ambient) ambient.killAll();
    return qName;
  }

  return {
    // original contract — do not change these five
    attach, burst, ring, confetti: confettiBurst, update, reset,

    // the one call that matters
    verdict(v, pos, opts) { auto = false; emitVerdict(v, pos, opts); },
    milestone,

    // primitives
    impact, speedLines, decal, popText: popTextAt, trail,
    flare(pos, o = {}) {
      readVec(pos, tmpDir, focus);
      const e = flare.begin();
      e.x = tmpDir[0]; e.y = tmpDir[1]; e.z = tmpDir[2];
      e.size = o.size ?? 0.5; e.life = o.life ?? 0.16;
      e.s0 = o.from ?? 0.35; e.s1 = o.to ?? 2.0;
      e.setColor(o.color ?? 0xffffff);
      flare.emit(1);
    },

    // configuration
    setQuality,
    get quality() { return qName; },
    setCombo(c) { combo = c; autoCombo = c; },
    get combo() { return combo; },
    setFocus(p) { readVec(p, focus, focus); },
    setGroundY(y) { groundY = y; },
    setAutoVerdict(on) { auto = !!on; },

    get attached() { return !!host; },
    dispose() {
      reset();
      for (const f of families) f.dispose();
    },
  };
}
