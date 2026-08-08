/**
 * Title screen.  [shell agent owns this file]
 *
 * Three jobs, in order of importance:
 *   1. Tell you what this game is before you read a word of it — a logo that
 *      moves on the beat, a stage with a cast on it, a crowd, lights.
 *   2. Make the first input you ever give the game feel good. Every menu move
 *      moves the logo, a character, the lights and the sound. Nothing here is
 *      a text list that merely highlights.
 *   3. Never sit still. After a few idle seconds it rolls an attract reel that
 *      demos all five minigames, and any button drops you back into the menu.
 *
 * Everything is procedural: the logo is DOM type (crisp at any DPI, free), the
 * stage is instanced geometry, the previews are canvas 2D.
 */

import * as THREE from 'three';
import { damp, clamp01, backOut } from '../core/util.js';
import {
  PAL, num, el, mountRoot, panel, sfx, kick, createWipe, beatPulse, fmtScore, reducedMotion,
} from './theme.js';
import { createBackdrop } from './backdrop.js';
import { CHARS, charMesh, charBeat, disposeChar, preloadChars } from './chars.js';
import { CATALOG, drawPreview } from './games.js';
import { profile } from './state.js';
import { goView } from './nav.js';

const LOGO_TOP = 'BEAT BASH';
const LOGO_BOT = 'BONANZA';
const IDLE_TO_ATTRACT = 8.5;
const ATTRACT_HOLD = 3.2;

const MENU = [
  { id: 'party', label: 'PARTY', sub: 'up to 4 players · 4 games · one crown', color: PAL.yellow },
  { id: 'free', label: 'FREE PLAY', sub: 'any minigame · chase your best rank', color: PAL.cyan },
  { id: 'options', label: 'OPTIONS', sub: 'mix · timing · reset', color: PAL.green },
];

/** @type {any} */
let S = null;

export default {
  id: 'title',
  name: 'Title',

  load(ctx) {
    preloadChars();
    S = {
      t: 0, idle: 0, sel: 0, mode: 'menu', selT: 0, confirm: null,
      attractIdx: 0, attractT: 0, attractA: 0, letters: [], items: [], cast: [],
      camTarget: new THREE.Vector3(0, 1.45, 9.2), lastBeat: -1, reduce: reducedMotion(),
    };

    S.back = createBackdrop(ctx, { accent: num(PAL.yellow) });
    ctx.fx.attach(ctx.scene);

    // ------------------------------------------------------------ 3D stage
    const stage = new THREE.Group();
    stage.position.y = -1.55;
    ctx.scene.add(stage);
    S.stage = stage;

    const deck = new THREE.Mesh(
      new THREE.CylinderGeometry(4.6, 5.0, 0.5, 24),
      new THREE.MeshStandardMaterial({ color: 0x2b2566, roughness: 0.5, metalness: 0.25, flatShading: true })
    );
    deck.position.set(0, 0.25, -0.5);
    stage.add(deck);
    S.deck = deck;

    const castIds = ['bopp', 'zizz', 'tuff', 'fizz'];
    castIds.forEach((id, i) => {
      const def = CHARS.find((c) => c.id === id) || CHARS[i];
      const m = charMesh(def, {});
      m.position.set((i - (castIds.length - 1) / 2) * 1.95, 0.5, -0.4 + (i % 2) * 0.5);
      m.userData.phase = i * 0.37;
      m.userData.baseY = 0.5;
      m.scale.setScalar(0.92);
      stage.add(m);
      S.cast.push(m);
    });

    ctx.camera.position.set(0, 1.45, 9.2);
    ctx.camera.lookAt(0, 1.15, 0);

    // ---------------------------------------------------------------- DOM
    const root = mountRoot(ctx, 'sh-title');
    S.root = root;

    const logo = el('div', 'sh-logo');
    logo.style.fontSize = 'clamp(34px, 8.4vw, 108px)';
    S.logo = logo;
    logo.appendChild(logoLine(LOGO_TOP, false, S.letters));
    logo.appendChild(logoLine(LOGO_BOT, true, S.letters));
    const tag = el('div', 'sh-logo__tag', 'FIVE GAMES · ONE BEAT');
    tag.style.fontSize = 'clamp(9px,1.35vw,17px)';
    logo.appendChild(tag);
    root.appendChild(logo);

    const menu = el('div', 'sh-menu');
    MENU.forEach((m, i) => {
      const it = el('div', 'sh-item');
      it.style.setProperty('--accent', m.color);
      it.appendChild(el('span', 'sh-item__dot'));
      it.appendChild(el('span', '', m.label));
      it.appendChild(el('span', 'sh-item__sub', m.sub));
      menu.appendChild(it);
      S.items.push(it);
    });
    root.appendChild(menu);
    S.menu = menu;

    const hint = el('div', 'sh-hint');
    hint.innerHTML = '<span><b class="sh-key">↑↓</b>choose</span><span><b class="sh-key">SPACE</b>select</span>';
    root.appendChild(hint);
    S.hint = hint;

    // top-right career line — tiny, but it says "this game remembers you"
    const stats = el('div', 'sh-title__stats');
    stats.style.cssText = 'position:absolute;right:3.2%;top:4.2%;text-align:right;font-weight:800;'
      + 'font-size:clamp(10px,1.25vw,15px);color:' + PAL.dim + ';text-shadow:0 2px 0 rgba(0,0,0,.6);line-height:1.5;';
    const best = bestOverall();
    stats.innerHTML = `${profile.stats.rounds} ROUNDS PLAYED<br>`
      + (best ? `BEST: ${best.name} · ${best.rank} · ${fmtScore(best.score)}` : 'NO RECORDS YET');
    root.appendChild(stats);

    // ------------------------------------------------------------- attract
    const at = el('div', 'sh-attract');
    at.style.opacity = '0';
    at.style.display = 'none';
    const card = panel('sh-attract__card', PAL.cyan);
    card.style.cssText += 'width:min(62vw,780px);padding:clamp(10px,1.4vw,18px);text-align:center;';
    const cv = el('canvas', 'sh-attract__cv');
    cv.width = 640; cv.height = 300;
    cv.style.cssText = 'width:100%;height:auto;display:block;border-radius:12px;background:#0a0820;';
    card.appendChild(cv);
    const nm = el('div', 'sh-display', '');
    nm.style.cssText = 'font-size:clamp(22px,4.2vw,54px);margin-top:.3em;';
    card.appendChild(nm);
    const hk = el('div', 'sh-sub', '');
    hk.style.cssText = 'font-size:clamp(11px,1.7vw,21px);margin-top:.25em;';
    card.appendChild(hk);
    at.appendChild(card);
    const press = el('div', 'sh-display', 'PRESS ANY BUTTON');
    press.style.cssText = 'position:absolute;left:50%;bottom:7%;transform:translateX(-50%);'
      + 'font-size:clamp(13px,2.1vw,26px);letter-spacing:.18em;color:#fff;white-space:nowrap;';
    at.appendChild(press);
    root.appendChild(at);
    S.attract = { root: at, cv, ctx2d: cv.getContext('2d'), name: nm, hook: hk, press, card };

    S.wipe = createWipe(root, { color: PAL.yellow, mode: 'in' });
    applySelection(ctx, true);
  },

  start(ctx) {
    ctx.clock.setBpm(124);
    ctx.clock.start(ctx.clock.now() + 0.12, 0);
    ctx.clock.onBeat((b, t) => {
      if (!S) return;
      // A four-on-the-floor pulse under the menu: the title screen should be
      // teaching you the tempo before you have chosen anything.
      kick(ctx, t, S.mode === 'attract' ? 0.5 : 0.34);
      if (b % 4 === 0) sfx(ctx, 'tick', t);
    });
    try { ctx.audio?.music?.play?.('menu'); } catch { /* optional */ }
  },

  update(ctx, dt, beat) {
    if (!S) return;
    S.t += dt;
    S.idle += dt;
    S.selT += dt;
    S.back.update(dt, beat, S.t);
    S.wipe.update(dt);

    const pulse = beatPulse(beat, 6);
    const down = beatPulse(beat / 4, 2.4);

    // ---- logo: a ripple that crosses the wordmark on every beat
    const amp = S.reduce ? 0.35 : 1;
    for (let i = 0; i < S.letters.length; i++) {
      const L = S.letters[i];
      const local = beat - i * 0.055;
      const f = local - Math.floor(local);
      const p = Math.exp(-f * 6.5);
      const s = 1 + p * 0.11 * amp;
      const y = -p * 9 * amp;
      const r = Math.sin(local * Math.PI * 0.5) * 1.6 * amp;
      L.style.transform = `translateY(${y.toFixed(2)}px) scale(${s.toFixed(3)}) rotate(${r.toFixed(2)}deg)`;
    }
    const lg = 1 + down * 0.02 * amp;
    S.logo.style.transform = `translate(-50%,-50%) scale(${lg.toFixed(3)})`;

    // ---- cast dances; the one under the cursor dances harder
    for (let i = 0; i < S.cast.length; i++) {
      const energy = (S.mode === 'attract') ? 1.35 : (i === S.sel ? 1.25 : 0.75);
      charBeat(S.cast[i], beat, dt, energy * (S.reduce ? 0.4 : 1));
    }
    S.deck.scale.y = 1 + pulse * 0.10;
    S.deck.rotation.y += dt * 0.12;

    // ---- selection spring
    for (let i = 0; i < S.items.length; i++) {
      const it = S.items[i];
      const on = i === S.sel;
      const k = on ? backOut(clamp01(S.selT / 0.22)) : 1;
      const sc = on ? 1.06 + 0.05 * k * (1 - clamp01(S.selT / 0.22)) + pulse * 0.012 : 1;
      const x = on ? 14 + pulse * 3 : 0;
      it.style.transform = `translateX(${x.toFixed(1)}px) scale(${sc.toFixed(3)})`;
    }

    // ---- attract mode
    if (S.mode === 'menu' && S.idle > IDLE_TO_ATTRACT && !S.confirm) enterAttract(ctx);
    const wantA = S.mode === 'attract' ? 1 : 0;
    S.attractA = damp(S.attractA, wantA, 7, dt);
    if (S.attractA > 0.002) {
      const a = S.attract;
      a.root.style.display = 'flex';
      a.root.style.opacity = S.attractA.toFixed(3);
      a.card.style.transform = `scale(${(0.94 + S.attractA * 0.06 + pulse * 0.008).toFixed(3)})`;
      a.press.style.opacity = (0.45 + 0.55 * pulse).toFixed(2);
      a.press.style.transform = `translateX(-50%) scale(${(1 + pulse * 0.05).toFixed(3)})`;
      const g = CATALOG[S.attractIdx % CATALOG.length];
      if (a.ctx2d) drawPreview(g.id, a.ctx2d, a.cv.width, a.cv.height, beat, S.t);
      // dim the menu behind the reel rather than hiding it — the player can
      // still see what they will come back to.
      S.menu.style.opacity = (1 - S.attractA * 0.85).toFixed(2);
      S.hint.style.opacity = (1 - S.attractA).toFixed(2);
      S.logo.style.opacity = (1 - S.attractA * 0.55).toFixed(2);
    } else if (S.attract.root.style.display !== 'none') {
      S.attract.root.style.display = 'none';
      S.menu.style.opacity = '1';
      S.hint.style.opacity = '1';
      S.logo.style.opacity = '1';
    }

    if (S.mode === 'attract') {
      S.attractT += dt;
      if (S.attractT > ATTRACT_HOLD) {
        S.attractT = 0;
        S.attractIdx++;
        const g = CATALOG[S.attractIdx % CATALOG.length];
        S.attract.name.textContent = g.name;
        S.attract.hook.textContent = g.hook;
        S.attract.card.style.setProperty('--accent', '#' + g.color.toString(16).padStart(6, '0'));
        S.back.setAccent(g.color);
        sfx(ctx, 'ui');
        ctx.fx.ring([0, 1.2, -1], { color: g.color, life: 0.6, to: 6 });
      }
      S.camTarget.set(Math.sin(S.t * 0.25) * 1.6, 1.2, 7.6);
    } else {
      S.camTarget.set(Math.sin(S.t * 0.32) * 0.55, 1.45 + Math.sin(S.t * 0.5) * 0.06, 9.2);
    }

    ctx.camera.position.x = damp(ctx.camera.position.x, S.camTarget.x, 1.6, dt);
    ctx.camera.position.y = damp(ctx.camera.position.y, S.camTarget.y, 1.6, dt);
    ctx.camera.position.z = damp(ctx.camera.position.z, S.camTarget.z, 1.6, dt);
    ctx.camera.lookAt(0, 1.15, 0);

    // ---- confirm sequence: hold, then wipe out
    if (S.confirm) {
      S.confirm.t += dt;
      if (!S.confirm.fired && S.confirm.t > 0.24) {
        S.confirm.fired = true;
        S.wipe.play(() => {
          const id = S.confirm.id;
          if (id === 'party') goView(ctx, 'roster', { mode: 'party' });
          else if (id === 'free') goView(ctx, 'roster', { mode: 'free' });
          else goView(ctx, 'options', {});
        }, MENU[S.sel].color);
      }
    }
  },

  input(ctx, events) {
    if (!S) return;
    for (const e of events) {
      if (!e.down) continue;
      S.idle = 0;

      if (S.mode === 'attract') {
        // Any button leaves the reel. Consume it: nobody wants to wake a demo
        // and immediately confirm the menu item that happened to be under it.
        S.mode = 'menu';
        S.attractT = 0;
        S.back.setAccent(num(MENU[S.sel].color));
        sfx(ctx, 'uiBack');
        continue;
      }
      if (S.confirm) continue;

      if (e.action === 'up' || e.action === 'left') move(ctx, -1);
      else if (e.action === 'down' || e.action === 'right') move(ctx, +1);
      else if (e.action === 'a') confirm(ctx);
      else if (e.action === 'b' || e.action === 'pause') enterAttract(ctx);
    }
  },

  dispose(ctx) {
    if (!S) return;
    for (const c of S.cast) disposeChar(c);
    S.deck.geometry.dispose(); S.deck.material.dispose();
    S.back.dispose();
    S.root.remove();
    S = null;
  },
};

// ---------------------------------------------------------------- helpers

function logoLine(text, alt, out) {
  const line = el('div', 'sh-logo__line');
  for (const ch of text) {
    if (ch === ' ') { line.appendChild(el('span', 'sh-logo__l sh-logo__l--sp', ' ')); continue; }
    const s = el('span', 'sh-logo__l' + (alt ? ' sh-logo__l--alt' : ''), ch);
    line.appendChild(s);
    out.push(s);
  }
  return line;
}

function move(ctx, d) {
  const n = MENU.length;
  S.sel = (S.sel + d + n) % n;
  S.selT = 0;
  applySelection(ctx, false);
  sfx(ctx, 'ui');
  ctx.stage.shake?.(0.035, [0, d > 0 ? -1 : 1, 0]);
}

function applySelection(ctx, silent) {
  for (let i = 0; i < S.items.length; i++) {
    S.items[i].classList.toggle('sh-item--sel', i === S.sel);
  }
  const c = MENU[S.sel].color;
  S.back.setAccent(num(c));
  if (!silent) {
    const m = S.cast[S.sel];
    if (m) {
      m.userData.baseY = 0.5;
      ctx.fx.ring([m.position.x, -0.9, m.position.z], { color: num(c), life: 0.45, from: 0.3, to: 2.2 });
      ctx.fx.burst([m.position.x, -0.6, m.position.z], { color: num(c), count: 9, speed: 3.4, life: 0.42, size: 0.1 });
    }
  }
}

function confirm(ctx) {
  const m = MENU[S.sel];
  S.confirm = { id: m.id, t: 0, fired: false };
  sfx(ctx, 'fanfare');
  ctx.stage.flash?.(0.24, m.color);
  ctx.stage.shake?.(0.14, [0, 1, 0]);
  ctx.fx.confetti([0, 1.4, 0], { count: 46 });
  S.items[S.sel].style.filter = 'brightness(1.6)';
}

function enterAttract(ctx) {
  if (S.mode === 'attract') return;
  S.mode = 'attract';
  S.attractT = 0;
  S.idle = 0;
  const g = CATALOG[S.attractIdx % CATALOG.length];
  S.attract.name.textContent = g.name;
  S.attract.hook.textContent = g.hook;
  S.attract.card.style.setProperty('--accent', '#' + g.color.toString(16).padStart(6, '0'));
  S.back.setAccent(g.color);
  sfx(ctx, 'ui');
}

function bestOverall() {
  let best = null;
  for (const g of CATALOG) {
    const r = profile.record(g.id);
    if (r.plays && (!best || r.score > best.score)) best = { name: g.name, score: r.score, rank: r.rank || '-' };
  }
  return best;
}
