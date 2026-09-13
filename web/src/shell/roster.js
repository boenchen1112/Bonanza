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
  ensureStyle, tickExit, makeExit, startShellTransport,
} from './theme.js';
import { createBackdrop } from './backdrop.js';
import { CHARS, charById, charMesh, charBeat, disposeChar, drawPortrait, pumpBusts, bustsBuilt } from './chars.js';
import { CATALOG } from './games.js';
import { profile, session, partyPlaylist } from './state.js';
import { goView, rosterExitRoute } from './nav.js';

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
    const mode = ctx.opts?.mode === 'party' ? 'party' : 'free';
    S = {
      t: 0, mode, stage: mode === 'party' ? 'lineup' : 'chars',
      cursor: 0, slotSel: 0, active: 0, cpuT: 0, goT: 0, exit: null,
      cards: [], slots: [], reduce: reducedMotion(),
      preview: null, previewId: null, previewSpin: 0,
      bustsSeen: bustsBuilt(),
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
    // House floor at the pedestal base: the preview stands on the pedestal,
    // the locked-in picks on the floor beside it.
    ctx.scene.userData.groundY = -1.5;
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
      // Name over type in one column; the READY! stamp lands on the portrait,
      // so the text keeps the whole width (beside it, "CPU · normal" was cut
      // to "CPU · no…" — the very setting ←→ changes).
      const text = el('div', 'sh-ros__slotText');
      const name = el('div', 'sh-ros__slotName', '—');
      const type = el('div', 'sh-ros__slotType', '');
      text.appendChild(name); text.appendChild(type);
      const faceWrap = el('div', 'sh-ros__faceWrap');
      const stamp = el('div', 'sh-stamp sh-ros__ready', 'READY!');
      stamp.style.opacity = '0';
      faceWrap.appendChild(face); faceWrap.appendChild(stamp);
      p.appendChild(tag); p.appendChild(faceWrap); p.appendChild(text);
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
    startShellTransport(ctx);
  },

  update(ctx, dt, beat) {
    if (!S) return;
    S.t += dt;
    S.back.update(dt, beat, S.t);
    S.wipe.update(dt);
    // Fallback pump: title normally finishes warming the portrait cache
    // before a player ever reaches here, but a fast navigation (or a harness
    // boot straight into roster) can arrive before it's done. Whichever
    // cards/faces drew the cheap placeholder get upgraded once their bust
    // lands — a couple of `drawImage` calls, not another 3D render.
    pumpBusts();
    const bustsNow = bustsBuilt();
    if (bustsNow !== S.bustsSeen) {
      S.bustsSeen = bustsNow;
      for (const c of S.cards) drawPortrait(c.cv, c.def, { size: 104 });
      for (let i = 0; i < 4; i++) {
        const id = S.picks[i];
        if (id) drawPortrait(S.slots[i].face, charById(id), { size: 62 });
      }
    }
    const pulse = beatPulse(beat, 6);
    const amp = S.reduce ? 0.4 : 1;

    // 3D preview: a slow sway toward camera, not a full turntable — the face
    // is the character, and a full spin spent half its time showing the back.
    if (S.preview) {
      S.previewSpin += dt;
      S.preview.rotation.y = 0.35 + Math.sin(S.previewSpin * 0.6) * 0.45;
      charBeat(S.preview, beat, dt);
    }
    S.ped.scale.y = 1 + pulse * 0.12;
    for (let i = 0; i < 4; i++) if (S.cast[i]) charBeat(S.cast[i], beat + i * 0.25, dt);

    // grid cards: hovered one lifts, taken ones sit back
    const gridOn = S.stage === 'chars';
    // Dimmed, not faded: at 42% opacity the 3D crowd showed through the portraits.
    S.grid.style.filter = gridOn ? '' : 'saturate(.45) brightness(.5)';
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

    tickExit(S, dt);

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
          // One controller, one human: every minigame is played by P1, so the
          // other seats are CPU rivals or empty — never a second "YOU" that
          // the games would silently ignore.
          if (S.slotSel === 0) { sfx(ctx, 'miss'); flashHead('P1 IS YOU — SET THE RIVALS'); continue; }
          const d = e.action === 'right' ? 1 : -1;
          const seat = SEAT_TYPES.indexOf(S.lineup[S.slotSel]);
          S.lineup[S.slotSel] = SEAT_TYPES[(seat + d + SEAT_TYPES.length) % SEAT_TYPES.length];
          sfx(ctx, 'ui');
          refreshSlots(ctx);
        } else if (e.action === 'a') {
          if (activeSlots().length < 2) { sfx(ctx, 'miss'); flashHead('SEAT AT LEAST ONE RIVAL'); continue; }
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
/** What seats P2–P4 cycle through. */
const SEAT_TYPES = ['cpu-easy', 'cpu-norm', 'cpu-hard', 'off'].map(typeIdx);
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
  S.stand.add(m);
  S.preview = m;
  // Each character introduces itself with the mocap chest-thump taunt, then
  // settles into its beat idle.
  m.userData.charApi?.play('taunt', { face: 'smug', beat: 0.3, blend: 0.2 });
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
  m.position.set((slot - 1.5) * 1.6, 0, 0);
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
    s.type.textContent = t.id === 'off' ? 'EMPTY' : t.id === 'you' ? 'YOU' : `${t.label} · ${t.sub}`;
    s.el.style.filter = t.id === 'off' ? 'saturate(.3) brightness(.55)' : '';
    // Free Play is solo: no rival seats to show.
    s.el.style.visibility = S.mode === 'free' && i > 0 ? 'hidden' : '';
  }
  refreshHint();
}

function refreshHead() {
  if (!S) return;
  if (S.stage === 'lineup') {
    S.headMain.textContent = 'PICK YOUR RIVALS';
    S.headSub.textContent = 'you are P1 · ↑↓ pick a seat · ←→ CPU easy / normal / hard / off';
  } else if (S.stage === 'chars') {
    S.headMain.textContent = 'CHOOSE YOUR CHARACTER';
    S.headSub.textContent = 'move with the arrows · SPACE to lock in · X to go back';
  } else if (S.stage === 'cpu') {
    S.headMain.textContent = 'CPU IS CHOOSING…';
    S.headSub.textContent = 'they always take the good one';
  } else {
    S.headMain.textContent = S.mode === 'party' ? 'LINEUP SET — PRESS SPACE' : 'READY — PRESS SPACE';
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
  // One legend per stage — it must never say "lock in" once the lineup is set.
  const HINTS = {
    lineup: '<span><b class="sh-key">←→</b>slot type</span><span><b class="sh-key">SPACE</b>continue</span><span><b class="sh-key">X</b>back</span>',
    chars: '<span><b class="sh-key">↑↓←→</b>choose</span><span><b class="sh-key">SPACE</b>lock in</span><span><b class="sh-key">X</b>back</span>',
    cpu: '<span>CPU choosing…</span>',
    go: `<span><b class="sh-key">SPACE</b>${S.mode === 'party' ? 'start the party' : 'pick a game'}</span><span><b class="sh-key">X</b>back</span>`,
  };
  S.hint.innerHTML = HINTS[S.stage] || HINTS.chars;
}

function back(ctx) {
  sfx(ctx, 'uiBack');
  S.exit = makeExit(() => goView(ctx, 'title', {}));
}

function startRun(ctx) {
  const players = [];
  for (let i = 0; i < 4; i++) {
    const t = SLOT_TYPES[S.lineup[i]];
    if (t.id === 'off') continue;
    const def = charById(S.picks[i] || CHARS[i % CHARS.length].id);
    players.push({
      // The human goes by their character (BOPP beside ZIZZ, not "P1" beside
      // ZIZZ) unless they've set a name of their own; the hub marks them YOU.
      name: t.id === 'you' && profile.names[i] && !/^P\d$/.test(profile.names[i]) ? profile.names[i] : def.name,
      char: def.id,
      isCpu: t.id !== 'you',
      cpuSkill: t.skill,
      palette: def.color,
      slot: i,
    });
  }
  session.setPlayers(players);
  session.mode = S.mode;
  if (S.mode === 'party') {
    const len = Math.min(CATALOG.length, Math.max(1, profile.options.partyLength || 4));
    session.startParty(partyPlaylist(CATALOG.map((g) => g.id), len, ctx.rng), len, (ctx.rng() * 2 ** 32) >>> 0);
  }
  sfx(ctx, 'fanfare');
  ctx.stage.flash?.(0.3, PAL.yellow);
  ctx.fx.confetti([0, 1.5, 0], { count: 60 });
  const r = rosterExitRoute(S.mode);
  S.exit = makeExit(
    () => goView(ctx, r.view, r.opts),
    S.mode === 'party' ? PAL.yellow : PAL.cyan,
  );
}

// -------------------------------------------------------------------- css

function injectRosterCss() {
  ensureStyle('sh-roster-css', `
  .sh-ros__head{position:absolute;left:4%;top:5%;}
  .sh-ros__title{font-size:clamp(20px,3.5vw,44px);}
  .sh-ros__sub{font-size:clamp(10px,1.35vw,17px);margin-top:.35em;}
  .sh-ros__info{position:absolute;left:4%;top:64%;width:32%;text-align:left;}
  .sh-ros__grid{position:absolute;right:3.5%;top:17%;width:52%;
    display:grid;grid-template-columns:repeat(4,1fr);gap:clamp(6px,.9vw,14px);}
  .sh-ros__card{display:flex;align-items:center;justify-content:center;padding:clamp(4px,.5vw,8px);
    will-change:transform;}
  .sh-ros__card canvas{width:100%;height:auto;max-width:110px;}
  .sh-ros__strip{position:absolute;left:4%;right:3.5%;bottom:10%;display:grid;
    grid-template-columns:repeat(4,1fr);gap:clamp(6px,1vw,16px);height:clamp(74px,13vh,124px);}
  .sh-ros__slot{display:flex;align-items:center;gap:.6em;padding:0 .8em;overflow:hidden;
    will-change:transform;}
  .sh-ros__slotTag{font-weight:900;font-size:clamp(11px,1.5vw,19px);color:var(--accent);
    text-shadow:0 2px 0 rgba(0,0,0,.6);}
  .sh-ros__face{width:62px;height:62px;border-radius:10px;flex:0 0 auto;background:rgba(0,0,0,.25);}
  .sh-ros__slotName{font-weight:900;font-size:clamp(11px,1.5vw,20px);color:#fff;
    text-shadow:0 2px 0 rgba(0,0,0,.6);}
  .sh-ros__slotText{display:flex;flex-direction:column;gap:.2em;min-width:0;flex:1 1 auto;}
  .sh-ros__slotType{font-weight:800;font-size:clamp(8px,1.05vw,13px);color:${PAL.dim};
    white-space:nowrap;letter-spacing:.04em;overflow:hidden;text-overflow:ellipsis;}
  .sh-ros__faceWrap{position:relative;flex:0 0 auto;}
  .sh-ros__slot .sh-ros__ready{left:50%;bottom:-.2em;margin-left:-2.2em;font-size:clamp(9px,1.05vw,14px);
    background:${PAL.coral};transform:rotate(-12deg);pointer-events:none;}
  `);
}
