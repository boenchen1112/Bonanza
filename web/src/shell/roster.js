/**
 * Player roster + character select.  [shell agent owns this file]
 *
 * The screen that decides whether this is "a game with menus" or "a party
 * game". So: portraits with faces, nameplates, a big 3D preview of whoever the
 * cursor is on, CPU opponents that pick their own characters in front of you,
 * and a READY! stamp that lands like a stamp.
 *
 * Two stages so no control ever means two things:
 *   LINEUP   ↑↓ pick a slot, ←→ set it to YOU / CPU / OFF, A to continue.
 *   CHARACTER ←→↑↓ move the grid, A to lock in for the current player.
 * Free Play skips LINEUP: one human, one pick, straight out.
 */

import * as THREE from 'three';
import { damp, clamp01, backOut } from '../core/util.js';
import {
  PAL, num, el, mountRoot, panel, sfx, createWipe, beatPulse, reducedMotion,
} from './theme.js';
import { createBackdrop } from './backdrop.js';
import { CHARS, charById, charMesh, charBeat, disposeChar, drawPortrait, preloadChars } from './chars.js';
import { profile, session } from './state.js';
import { goView } from './nav.js';

const COLS = 4;
const SLOT_TYPES = [
  { id: 'you', label: 'YOU', sub: 'human', skill: 0 },
  { id: 'cpu-easy', label: 'CPU', sub: 'easy', skill: 0.40 },
  { id: 'cpu-norm', label: 'CPU', sub: 'normal', skill: 0.63 },
  { id: 'cpu-hard', label: 'CPU', sub: 'hard', skill: 0.86 },
  { id: 'off', label: 'OFF', sub: '—', skill: 0 },
];
const PLAYER_COLORS = [PAL.yellow, PAL.cyan, PAL.coral, PAL.green];

let S = null;

export default {
  id: 'roster',
  name: 'Roster',

  load(ctx) {
    preloadChars();
    const mode = ctx.opts?.mode === 'party' ? 'party' : 'free';
    S = {
      t: 0, mode, stage: mode === 'party' ? 'lineup' : 'chars',
      cursor: 0, slotSel: 0, active: 0, cpuT: 0, goT: 0, exit: null,
      cards: [], slots: [], reduce: reducedMotion(),
      preview: null, previewId: null, previewSpin: 0,
    };

    // party defaults: one human, two normal CPUs, fourth slot off
    S.lineup = mode === 'party'
      ? [typeIdx('you'), typeIdx('cpu-norm'), typeIdx('cpu-norm'), typeIdx('off')]
      : [typeIdx('you'), typeIdx('off'), typeIdx('off'), typeIdx('off')];
    S.picks = [null, null, null, null];

    S.back = createBackdrop(ctx, { accent: num(PAL.cyan), density: 0.7 });
    ctx.fx.attach(ctx.scene);

    // ------------------------------------------------------------ 3D stand
    const stand = new THREE.Group();
    stand.position.set(-3.7, -1.5, 0);
    ctx.scene.add(stand);
    S.stand = stand;
    const ped = new THREE.Mesh(
      new THREE.CylinderGeometry(1.5, 1.7, 0.42, 18),
      new THREE.MeshStandardMaterial({ color: 0x2b2566, roughness: 0.45, metalness: 0.3, flatShading: true })
    );
    ped.position.y = 0.2;
    stand.add(ped);
    S.ped = ped;

    // confirmed cast lines up on a second deck to the right of the stand
    S.castGroup = new THREE.Group();
    S.castGroup.position.set(2.4, -1.5, -3.4);
    ctx.scene.add(S.castGroup);
    S.cast = [null, null, null, null];

    ctx.camera.position.set(0, 1.7, 9.4);
    ctx.camera.lookAt(0, 1.1, 0);

    // ---------------------------------------------------------------- DOM
    const root = mountRoot(ctx, 'sh-roster');
    S.root = root;
    injectRosterCss();

    const head = el('div', 'sh-ros__head');
    S.headMain = el('div', 'sh-display sh-ros__title', '');
    S.headSub = el('div', 'sh-sub sh-ros__sub', '');
    head.appendChild(S.headMain);
    head.appendChild(S.headSub);
    root.appendChild(head);

    // hovered character info under the 3D stand
    const info = el('div', 'sh-ros__info');
    S.infoName = el('div', 'sh-display', '');
    S.infoName.style.fontSize = 'clamp(20px,3.4vw,42px)';
    S.infoTrait = el('div', 'sh-sub', '');
    S.infoTrait.style.fontSize = 'clamp(10px,1.4vw,17px)';
    info.appendChild(S.infoName);
    info.appendChild(S.infoTrait);
    root.appendChild(info);
    S.info = info;

    // character grid
    const grid = el('div', 'sh-ros__grid');
    CHARS.forEach((def, i) => {
      const card = panel('sh-ros__card', '#' + def.color.toString(16).padStart(6, '0'));
      const cv = el('canvas', 'sh-portrait');
      drawPortrait(cv, def, { size: 104 });
      card.appendChild(cv);
      grid.appendChild(card);
      S.cards.push({ el: card, cv, def, takenBy: -1, t: i * 0.05 });
    });
    root.appendChild(grid);
    S.grid = grid;

    // slot strip
    const strip = el('div', 'sh-ros__strip');
    for (let i = 0; i < 4; i++) {
      const p = panel('sh-ros__slot', PLAYER_COLORS[i]);
      const tag = el('div', 'sh-ros__slotTag', 'P' + (i + 1));
      const face = el('canvas', 'sh-ros__face');
      face.width = 1; face.height = 1;
      const name = el('div', 'sh-ros__slotName', '—');
      const type = el('div', 'sh-ros__slotType', '');
      const stamp = el('div', 'sh-stamp sh-ros__ready', 'READY!');
      stamp.style.opacity = '0';
      p.appendChild(tag); p.appendChild(face); p.appendChild(name); p.appendChild(type); p.appendChild(stamp);
      strip.appendChild(p);
      S.slots.push({ el: p, face, name, type, stamp, readyT: -1 });
    }
    root.appendChild(strip);
    S.strip = strip;

    const hint = el('div', 'sh-hint');
    S.hint = hint;
    root.appendChild(hint);

    S.wipe = createWipe(root, { color: PAL.cyan, mode: 'in' });

    refreshSlots(ctx);
    refreshHead();
    setPreview(ctx, CHARS[0]);
  },

  start(ctx) {
    ctx.clock.setBpm(124);
    ctx.clock.start(ctx.clock.now() + 0.12, 0);
  },

  update(ctx, dt, beat) {
    if (!S) return;
    S.t += dt;
    S.back.update(dt, beat, S.t);
    S.wipe.update(dt);
    const pulse = beatPulse(beat, 6);
    const amp = S.reduce ? 0.4 : 1;

    // 3D preview turntable
    if (S.preview) {
      S.previewSpin += dt * 0.55;
      S.preview.rotation.y = S.previewSpin;
      charBeat(S.preview, beat, dt, 0.9 * amp);
      S.preview.rotation.y = S.previewSpin; // charBeat also writes rotation.y
    }
    S.ped.scale.y = 1 + pulse * 0.12;
    for (let i = 0; i < 4; i++) if (S.cast[i]) charBeat(S.cast[i], beat + i * 0.25, dt, 0.8 * amp);

    // grid cards: hovered one lifts, taken ones sit back
    const gridOn = S.stage === 'chars';
    S.grid.style.opacity = gridOn ? '1' : '0.42';
    for (let i = 0; i < S.cards.length; i++) {
      const c = S.cards[i];
      const on = gridOn && i === S.cursor;
      c.t += dt;
      const sel = on ? backOut(clamp01(c.t / 0.2)) : 0;
      const bob = Math.sin((beat + i * 0.31) * Math.PI) * 2 * amp;
      const sc = 1 + (on ? 0.13 + pulse * 0.03 : 0) * (0.6 + 0.4 * sel);
      c.el.style.transform = `translateY(${(bob - (on ? 8 : 0)).toFixed(2)}px) scale(${sc.toFixed(3)})`;
      c.el.classList.toggle('sh-panel--sel', on);
      c.el.style.filter = c.takenBy >= 0 ? 'saturate(.35) brightness(.6)' : '';
    }

    // slot strip: the slot being filled breathes
    for (let i = 0; i < 4; i++) {
      const s = S.slots[i];
      const active = (S.stage === 'lineup' && i === S.slotSel) || (S.stage === 'chars' && i === S.active);
      s.el.classList.toggle('sh-panel--sel', active);
      const sc = active ? 1.05 + pulse * 0.03 * amp : 1;
      s.el.style.transform = `scale(${sc.toFixed(3)})`;
      if (s.readyT >= 0) {
        s.readyT += dt;
        const k = clamp01(s.readyT / 0.34);
        const os = 2.2 - 1.2 * backOut(k);
        s.stamp.style.opacity = String(clamp01(k * 3));
        s.stamp.style.transform = `rotate(-12deg) scale(${os.toFixed(3)})`;
      }
    }

    // CPU characters pick themselves, one per half-beat, in front of you
    if (S.stage === 'cpu') {
      S.cpuT += dt;
      const step = 0.34;
      const next = S.lineup.findIndex((ti, i) => SLOT_TYPES[ti].id.startsWith('cpu') && !S.picks[i]);
      if (next < 0) { S.stage = 'go'; refreshHead(); }
      else if (S.cpuT > step) {
        S.cpuT = 0;
        const free = CHARS.filter((c) => !S.picks.includes(c.id));
        const def = free[Math.floor((ctx.rng ? ctx.rng() : Math.random()) * free.length)] || CHARS[0];
        lockIn(ctx, next, def, true);
      }
    }

    if (S.stage === 'go') {
      S.goT += dt;
      S.headMain.style.transform = `scale(${(1 + pulse * 0.05 * amp).toFixed(3)})`;
    }

    if (S.exit) {
      S.exit.t += dt;
      if (!S.exit.fired && S.exit.t > 0.1) {
        S.exit.fired = true;
        S.wipe.play(S.exit.go, S.exit.color);
      }
    }

    ctx.camera.position.x = damp(ctx.camera.position.x, Math.sin(S.t * 0.3) * 0.35, 1.4, dt);
    ctx.camera.lookAt(0, 1.1, 0);
  },

  input(ctx, events) {
    if (!S || S.exit) return;
    for (const e of events) {
      if (!e.down) continue;

      if (S.stage === 'lineup') {
        if (e.action === 'up') { S.slotSel = (S.slotSel + 3) % 4; sfx(ctx, 'ui'); }
        else if (e.action === 'down') { S.slotSel = (S.slotSel + 1) % 4; sfx(ctx, 'ui'); }
        else if (e.action === 'left' || e.action === 'right') {
          const d = e.action === 'right' ? 1 : -1;
          S.lineup[S.slotSel] = (S.lineup[S.slotSel] + d + SLOT_TYPES.length) % SLOT_TYPES.length;
          // slot 1 can never be empty — somebody has to hold the controller
          if (S.slotSel === 0 && SLOT_TYPES[S.lineup[0]].id === 'off') S.lineup[0] = typeIdx('you');
          sfx(ctx, 'ui');
          refreshSlots(ctx);
        } else if (e.action === 'a') {
          if (activeSlots().length < 2) { sfx(ctx, 'miss'); flashHead('NEED AT LEAST 2 PLAYERS'); continue; }
          sfx(ctx, 'ui');
          S.stage = 'chars';
          S.active = nextHuman(-1);
          if (S.active < 0) { S.stage = 'cpu'; }
          refreshHead();
        } else if (e.action === 'b' || e.action === 'pause') back(ctx);
        continue;
      }

      if (S.stage === 'chars') {
        if (e.action === 'left') { moveCursor(ctx, -1); }
        else if (e.action === 'right') { moveCursor(ctx, +1); }
        else if (e.action === 'up') { moveCursor(ctx, -COLS); }
        else if (e.action === 'down') { moveCursor(ctx, +COLS); }
        else if (e.action === 'a') {
          const def = CHARS[S.cursor];
          if (S.picks.includes(def.id)) { sfx(ctx, 'miss'); shakeCard(S.cursor); continue; }
          lockIn(ctx, S.active, def, false);
          const nxt = nextHuman(S.active);
          if (nxt >= 0) { S.active = nxt; refreshHead(); }
          else if (S.lineup.some((ti, i) => SLOT_TYPES[ti].id.startsWith('cpu') && !S.picks[i])) {
            S.stage = 'cpu'; S.cpuT = 0.2; refreshHead();
          } else { S.stage = 'go'; refreshHead(); }
        } else if (e.action === 'b' || e.action === 'pause') {
          const undone = lastConfirmedHuman();
          if (undone >= 0) { unlock(ctx, undone); S.active = undone; refreshHead(); }
          else back(ctx);
        }
        continue;
      }

      if (S.stage === 'go') {
        if (e.action === 'a') startRun(ctx);
        else if (e.action === 'b' || e.action === 'pause') {
          const undone = lastConfirmedHuman();
          if (undone >= 0) { unlock(ctx, undone); S.stage = 'chars'; S.active = undone; refreshHead(); }
          else back(ctx);
        }
      }
    }
  },

  dispose(ctx) {
    if (!S) return;
    if (S.preview) disposeChar(S.preview);
    for (const c of S.cast) if (c) disposeChar(c);
    S.ped.geometry.dispose(); S.ped.material.dispose();
    S.back.dispose();
    S.root.remove();
    S = null;
  },
};

// ---------------------------------------------------------------- helpers

const typeIdx = (id) => SLOT_TYPES.findIndex((t) => t.id === id);
const activeSlots = () => S.lineup.map((ti, i) => ({ ti, i })).filter((x) => SLOT_TYPES[x.ti].id !== 'off');

function nextHuman(after) {
  for (let i = after + 1; i < 4; i++) {
    if (SLOT_TYPES[S.lineup[i]].id === 'you' && !S.picks[i]) return i;
  }
  return -1;
}
function lastConfirmedHuman() {
  for (let i = 3; i >= 0; i--) if (SLOT_TYPES[S.lineup[i]].id === 'you' && S.picks[i]) return i;
  return -1;
}

function moveCursor(ctx, d) {
  const n = CHARS.length;
  S.cursor = (S.cursor + d + n) % n;
  S.cards[S.cursor].t = 0;
  sfx(ctx, 'ui');
  setPreview(ctx, CHARS[S.cursor]);
  ctx.stage.shake?.(0.025, [Math.sign(d), 0, 0]);
}

function shakeCard(i) {
  const c = S.cards[i];
  c.el.animate?.(
    [{ transform: 'translateX(-6px)' }, { transform: 'translateX(6px)' }, { transform: 'translateX(0)' }],
    { duration: 160, easing: 'ease-out' }
  );
}

function setPreview(ctx, def) {
  if (S.previewId === def.id) return;
  S.previewId = def.id;
  if (S.preview) { S.stand.remove(S.preview); disposeChar(S.preview); }
  const m = charMesh(def, {});
  m.scale.setScalar(1.55);
  m.position.y = 0.42;
  m.userData.baseY = 0.42;
  S.stand.add(m);
  S.preview = m;
  S.back.setAccent(def.color);
  S.infoName.textContent = def.name;
  S.infoTrait.textContent = def.trait;
  ctx.fx.ring([-3.7, -1.2, 0], { color: def.color, life: 0.5, from: 0.6, to: 3.4 });
}

function lockIn(ctx, slot, def, isCpu) {
  S.picks[slot] = def.id;
  const card = S.cards.find((c) => c.def.id === def.id);
  if (card) card.takenBy = slot;
  S.slots[slot].readyT = 0;
  drawPortrait(S.slots[slot].face, def, { size: 62 });
  S.slots[slot].name.textContent = def.name;
  refreshSlots(ctx);

  sfx(ctx, isCpu ? 'great' : 'perfect');
  ctx.stage.flash?.(isCpu ? 0.08 : 0.16, PLAYER_COLORS[slot]);
  ctx.stage.shake?.(isCpu ? 0.06 : 0.12, [0, -1, 0]);

  // the pick walks onto the cast deck
  const m = charMesh(def, {});
  m.position.set((slot - 1.5) * 1.6, 0.5, 0);
  m.userData.baseY = 0.5;
  m.userData.phase = slot * 0.3;
  m.scale.setScalar(0.8);
  S.castGroup.add(m);
  if (S.cast[slot]) { S.castGroup.remove(S.cast[slot]); disposeChar(S.cast[slot]); }
  S.cast[slot] = m;
  ctx.fx.confetti([2.4 + (slot - 1.5) * 1.6, -0.3, -3.4], { count: isCpu ? 14 : 30 });
}

function unlock(ctx, slot) {
  const id = S.picks[slot];
  const card = S.cards.find((c) => c.def.id === id);
  if (card) card.takenBy = -1;
  S.picks[slot] = null;
  S.slots[slot].readyT = -1;
  S.slots[slot].stamp.style.opacity = '0';
  S.slots[slot].name.textContent = '—';
  const f = S.slots[slot].face;
  f.width = 1; f.height = 1; f.style.width = '62px'; f.style.height = '62px';
  if (S.cast[slot]) { S.castGroup.remove(S.cast[slot]); disposeChar(S.cast[slot]); S.cast[slot] = null; }
  S.stage = 'chars';
  sfx(ctx, 'uiBack');
  refreshSlots(ctx);
}

function refreshSlots(ctx) {
  for (let i = 0; i < 4; i++) {
    const t = SLOT_TYPES[S.lineup[i]];
    const s = S.slots[i];
    s.type.textContent = t.id === 'off' ? 'EMPTY' : `${t.label} · ${t.sub}`;
    s.el.style.opacity = t.id === 'off' ? '0.34' : '1';
  }
  refreshHint();
}

function refreshHead() {
  if (!S) return;
  if (S.stage === 'lineup') {
    S.headMain.textContent = "WHO'S PLAYING?";
    S.headSub.textContent = '↑↓ pick a slot · ←→ set human / CPU / off · A to continue';
  } else if (S.stage === 'chars') {
    S.headMain.textContent = `PLAYER ${S.active + 1} — CHOOSE`;
    S.headSub.textContent = 'move with the arrows · A to lock in · B to go back';
  } else if (S.stage === 'cpu') {
    S.headMain.textContent = 'CPU IS CHOOSING…';
    S.headSub.textContent = 'they always take the good one';
  } else {
    S.headMain.textContent = S.mode === 'party' ? 'LINEUP SET — PRESS A' : 'READY — PRESS A';
    S.headSub.textContent = S.mode === 'party' ? 'four games, one crown' : 'pick a minigame next';
  }
  refreshHint();
}

function flashHead(msg) {
  const old = S.headSub.textContent;
  S.headSub.textContent = msg;
  S.headSub.style.color = PAL.coral;
  setTimeout(() => {
    if (!S) return;
    S.headSub.textContent = old;
    S.headSub.style.color = '';
  }, 900);
}

function refreshHint() {
  if (!S) return;
  S.hint.innerHTML = S.stage === 'lineup'
    ? '<span><b class="sh-key">←→</b>slot type</span><span><b class="sh-key">SPACE</b>continue</span><span><b class="sh-key">X</b>back</span>'
    : '<span><b class="sh-key">↑↓←→</b>choose</span><span><b class="sh-key">SPACE</b>lock in</span><span><b class="sh-key">X</b>back</span>';
}

function back(ctx) {
  sfx(ctx, 'uiBack');
  S.exit = { t: 0, fired: false, color: PAL.violet, go: () => goView(ctx, 'title', {}) };
}

function startRun(ctx) {
  const players = [];
  for (let i = 0; i < 4; i++) {
    const t = SLOT_TYPES[S.lineup[i]];
    if (t.id === 'off') continue;
    const def = charById(S.picks[i] || CHARS[i % CHARS.length].id);
    players.push({
      name: t.id === 'you' ? (profile.names[i] || 'P' + (i + 1)) : def.name,
      char: def.id,
      isCpu: t.id !== 'you',
      cpuSkill: t.skill,
      palette: def.color,
      slot: i,
    });
  }
  session.setPlayers(players);
  session.mode = S.mode;
  sfx(ctx, 'fanfare');
  ctx.stage.flash?.(0.3, PAL.yellow);
  ctx.fx.confetti([0, 1.5, 0], { count: 60 });
  S.exit = {
    t: 0, fired: false, color: S.mode === 'party' ? PAL.yellow : PAL.cyan,
    go: () => goView(ctx, S.mode === 'party' ? 'party' : 'freeplay', {}),
  };
}

// -------------------------------------------------------------------- css

let cssDone = false;
function injectRosterCss() {
  if (cssDone || document.getElementById('sh-roster-css')) { cssDone = true; return; }
  cssDone = true;
  const s = document.createElement('style');
  s.id = 'sh-roster-css';
  s.textContent = `
  .sh-ros__head{position:absolute;left:4%;top:5%;}
  .sh-ros__title{font-size:clamp(20px,3.5vw,44px);}
  .sh-ros__sub{font-size:clamp(10px,1.35vw,17px);margin-top:.35em;}
  .sh-ros__info{position:absolute;left:4%;top:64%;width:32%;text-align:left;}
  .sh-ros__grid{position:absolute;right:3.5%;top:17%;width:52%;
    display:grid;grid-template-columns:repeat(4,1fr);gap:clamp(6px,.9vw,14px);}
  .sh-ros__card{display:flex;align-items:center;justify-content:center;padding:clamp(4px,.5vw,8px);
    will-change:transform;}
  .sh-ros__card canvas{width:100%;height:auto;max-width:110px;}
  .sh-ros__strip{position:absolute;left:4%;right:3.5%;bottom:4.5%;display:grid;
    grid-template-columns:repeat(4,1fr);gap:clamp(6px,1vw,16px);height:clamp(74px,13vh,124px);}
  .sh-ros__slot{display:flex;align-items:center;gap:.6em;padding:0 .8em;overflow:hidden;
    will-change:transform;}
  .sh-ros__slotTag{font-weight:900;font-size:clamp(11px,1.5vw,19px);color:var(--accent);
    text-shadow:0 2px 0 rgba(0,0,0,.6);}
  .sh-ros__face{width:62px;height:62px;border-radius:10px;flex:0 0 auto;background:rgba(0,0,0,.25);}
  .sh-ros__slotName{font-weight:900;font-size:clamp(11px,1.5vw,20px);color:#fff;
    text-shadow:0 2px 0 rgba(0,0,0,.6);}
  .sh-ros__slotType{font-weight:800;font-size:clamp(8px,1.05vw,13px);color:${PAL.dim};margin-left:auto;
    text-align:right;letter-spacing:.04em;}
  .sh-ros__ready{right:6%;top:6%;font-size:clamp(10px,1.5vw,19px);background:${PAL.coral};
    transform:rotate(-12deg);pointer-events:none;}
  `;
  document.head.appendChild(s);
}
