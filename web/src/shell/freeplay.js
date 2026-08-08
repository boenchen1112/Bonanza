/**
 * Free Play — the minigame carousel.  [shell agent owns this file]
 *
 * The rule this screen exists to obey: browsing is part of the game. So the
 * carousel is a spring, not an index. Presses inject velocity, the rail
 * overshoots and settles, and holding a direction lets the whole series fly
 * past — which is the difference between "a list of five" and "a shelf".
 *
 * Each card carries its record (best score, best rank, plays) and the focused
 * card runs a live procedural preview of the game on the beat.
 */

import * as THREE from 'three';
import { damp, clamp01, easeOutCubic } from '../core/util.js';
import { PAL, el, mountRoot, panel, sfx, createWipe, beatPulse, fmtScore, RANK_COLOR, reducedMotion } from './theme.js';
import { createBackdrop } from './backdrop.js';
import { CATALOG, drawPreview } from './games.js';
import { charById, charMesh, charBeat, disposeChar } from './chars.js';
import { profile, session, ensurePlayers } from './state.js';
import { goView, goPlay } from './nav.js';

let S = null;

export default {
  id: 'freeplay',
  name: 'Free Play',

  load(ctx) {
    ensurePlayers(ctx.rng);
    S = {
      t: 0, pos: 0, vel: 0, target: 0, cards: [], exit: null,
      reduce: reducedMotion(), lastFocus: -1,
    };
    if (ctx.opts?.game) {
      const i = CATALOG.findIndex((g) => g.id === ctx.opts.game);
      if (i >= 0) { S.pos = S.target = i; }
    }

    S.back = createBackdrop(ctx, { accent: CATALOG[S.target].color, density: 0.8 });
    ctx.fx.attach(ctx.scene);

    // the roster cheers from the front of the stage
    S.castGroup = new THREE.Group();
    S.castGroup.position.set(0, -1.55, 3.2);
    ctx.scene.add(S.castGroup);
    S.cast = [];
    const players = session.players.length ? session.players : [];
    players.slice(0, 4).forEach((p, i) => {
      const m = charMesh(charById(p.char), {});
      m.position.set((i - (Math.min(players.length, 4) - 1) / 2) * 2.2, 0.5, 0);
      m.userData.baseY = 0.5;
      m.userData.phase = i * 0.4;
      m.scale.setScalar(0.72);
      S.castGroup.add(m);
      S.cast.push(m);
    });

    ctx.camera.position.set(0, 1.5, 9);
    ctx.camera.lookAt(0, 1.2, 0);

    const root = mountRoot(ctx, 'sh-fp');
    S.root = root;
    injectCss();

    const head = el('div', 'sh-fp__head');
    const h1 = el('div', 'sh-display', 'FREE PLAY');
    h1.style.fontSize = 'clamp(20px,3.5vw,44px)';
    head.appendChild(h1);
    const h2 = el('div', 'sh-sub', 'pick a minigame · chase the S rank');
    h2.style.fontSize = 'clamp(10px,1.35vw,17px)';
    head.appendChild(h2);
    root.appendChild(head);

    const rail = el('div', 'sh-fp__rail');
    root.appendChild(rail);
    S.rail = rail;

    CATALOG.forEach((g, i) => {
      const col = '#' + g.color.toString(16).padStart(6, '0');
      const card = panel('sh-fp__card', col);
      const cv = el('canvas', 'sh-fp__cv');
      cv.width = 480; cv.height = 240;
      card.appendChild(cv);
      const nm = el('div', 'sh-fp__name', g.name);
      card.appendChild(nm);
      const bl = el('div', 'sh-fp__blurb', g.blurb);
      card.appendChild(bl);

      const rec = profile.record(g.id);
      const row = el('div', 'sh-fp__rec');
      const badge = el('div', 'sh-fp__rank', rec.rank || '–');
      badge.style.color = RANK_COLOR[rec.rank] || PAL.dim;
      badge.style.borderColor = RANK_COLOR[rec.rank] || 'rgba(255,255,255,.18)';
      row.appendChild(badge);
      const sc = el('div', 'sh-fp__score');
      sc.innerHTML = `<span class="sh-fp__lab">BEST</span><span class="sh-mono">${rec.plays ? fmtScore(rec.score) : '—'}</span>`;
      row.appendChild(sc);
      const pl = el('div', 'sh-fp__plays', rec.plays ? `${rec.plays} play${rec.plays > 1 ? 's' : ''}` : 'never played');
      row.appendChild(pl);
      card.appendChild(row);

      rail.appendChild(card);
      const c2d = cv.getContext('2d');
      if (c2d) drawPreview(g.id, c2d, cv.width, cv.height, i * 0.37, 0);
      S.cards.push({ el: card, cv, c2d, g });
    });

    const hint = el('div', 'sh-hint');
    hint.innerHTML = '<span><b class="sh-key">←→</b>browse</span><span><b class="sh-key">SPACE</b>play</span><span><b class="sh-key">X</b>back</span>';
    root.appendChild(hint);

    S.wipe = createWipe(root, { color: PAL.cyan, mode: 'in' });
    layout(0, 0);
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

    // --- spring rail. Stiff enough to feel snappy, loose enough to overshoot.
    const k = 150, damping = 11.5;
    S.vel += (S.target - S.pos) * k * dt;
    S.vel *= Math.exp(-damping * dt);
    S.pos += S.vel * dt;
    if (Math.abs(S.target - S.pos) < 0.0004 && Math.abs(S.vel) < 0.01) { S.pos = S.target; S.vel = 0; }

    const pulse = beatPulse(beat, 6);
    layout(pulse, beat);

    const focus = Math.max(0, Math.min(CATALOG.length - 1, Math.round(S.pos)));
    if (focus !== S.lastFocus) {
      S.lastFocus = focus;
      S.back.setAccent(CATALOG[focus].color);
    }
    const fc = S.cards[focus];
    if (fc?.c2d) drawPreview(fc.g.id, fc.c2d, fc.cv.width, fc.cv.height, beat, S.t);

    for (let i = 0; i < S.cast.length; i++) charBeat(S.cast[i], beat + i * 0.3, dt, S.reduce ? 0.4 : 1);

    ctx.camera.position.x = damp(ctx.camera.position.x, (S.pos - S.target) * 0.5 + Math.sin(S.t * 0.3) * 0.25, 3, dt);
    ctx.camera.lookAt(0, 1.2, 0);

    if (S.exit) {
      S.exit.t += dt;
      if (!S.exit.fired && S.exit.t > 0.12) { S.exit.fired = true; S.wipe.play(S.exit.go, S.exit.color); }
    }
  },

  input(ctx, events) {
    if (!S || S.exit) return;
    for (const e of events) {
      if (!e.down) continue;
      if (e.action === 'left' || e.action === 'right') {
        const d = e.action === 'right' ? 1 : -1;
        const n = CATALOG.length;
        S.target = Math.max(0, Math.min(n - 1, S.target + d));
        // A press at the end of the rail still has to DO something, or the
        // screen feels dead. Nudge and bounce back.
        if (S.target === S.pos && (S.target === 0 || S.target === n - 1)) {
          S.vel += d * 2.2;
          sfx(ctx, 'tick');
        } else {
          S.vel += d * 3.2;
          sfx(ctx, 'ui');
        }
        ctx.stage.shake?.(0.03, [d, 0, 0]);
      } else if (e.action === 'a') {
        const g = CATALOG[Math.round(S.pos)] || CATALOG[0];
        sfx(ctx, 'fanfare');
        ctx.stage.flash?.(0.22, '#' + g.color.toString(16).padStart(6, '0'));
        ctx.fx.confetti([0, 1.2, 1], { count: 40 });
        session.mode = 'free';
        S.exit = {
          t: 0, fired: false, color: '#' + g.color.toString(16).padStart(6, '0'),
          go: () => goPlay(ctx, g.id, { from: 'freeplay' }),
        };
      } else if (e.action === 'b' || e.action === 'pause') {
        sfx(ctx, 'uiBack');
        S.exit = { t: 0, fired: false, color: PAL.violet, go: () => goView(ctx, 'title', {}) };
      }
    }
  },

  dispose(ctx) {
    if (!S) return;
    for (const c of S.cast) disposeChar(c);
    S.back.dispose();
    S.root.remove();
    S = null;
  },
};

/** Position every card on the rail from the current spring position. */
function layout(pulse, beat) {
  for (let i = 0; i < S.cards.length; i++) {
    const c = S.cards[i];
    const d = i - S.pos;
    const ad = Math.abs(d);
    if (ad > 2.7) { c.el.style.visibility = 'hidden'; continue; }
    c.el.style.visibility = 'visible';
    const focus = clamp01(1 - ad);
    const x = d * 62 + Math.sign(d) * Math.min(ad, 1) * 6;
    const sc = 1 / (1 + ad * 0.42) * (1 + focus * pulse * 0.02);
    const ry = -d * 26;
    const z = -ad * 140;
    const y = -focus * 10;
    c.el.style.transform =
      `translate(-50%,-50%) translate3d(${x.toFixed(2)}%, ${y.toFixed(1)}px, ${z.toFixed(0)}px) `
      + `rotateY(${ry.toFixed(2)}deg) scale(${sc.toFixed(3)})`;
    c.el.style.opacity = String(clamp01(1.25 - ad * 0.55));
    c.el.style.zIndex = String(100 - Math.round(ad * 10));
    c.el.classList.toggle('sh-panel--sel', ad < 0.5);
    c.el.style.filter = ad < 0.5 ? '' : `brightness(${(0.62 + easeOutCubic(1 - clamp01(ad)) * 0.38).toFixed(2)})`;
  }
}

let cssDone = false;
function injectCss() {
  if (cssDone || document.getElementById('sh-fp-css')) { cssDone = true; return; }
  cssDone = true;
  const s = document.createElement('style');
  s.id = 'sh-fp-css';
  s.textContent = `
  .sh-fp__head{position:absolute;left:4%;top:5.5%;}
  .sh-fp__rail{position:absolute;left:0;right:0;top:0;bottom:0;perspective:1400px;
    transform-style:preserve-3d;}
  .sh-fp__card{position:absolute;left:50%;top:52%;width:clamp(210px,27vw,380px);
    padding:clamp(8px,1vw,14px);will-change:transform,opacity;transform-origin:50% 50%;}
  .sh-fp__cv{width:100%;height:auto;display:block;border-radius:11px;background:#0a0820;
    box-shadow:inset 0 0 0 1px rgba(255,255,255,.08);}
  .sh-fp__name{font-weight:900;font-size:clamp(15px,2.1vw,27px);margin-top:.42em;letter-spacing:-.02em;
    text-shadow:0 3px 0 rgba(0,0,0,.55);}
  .sh-fp__blurb{font-weight:700;font-size:clamp(9px,1.15vw,14px);color:${PAL.dim};margin-top:.25em;
    min-height:2.3em;line-height:1.25;}
  .sh-fp__rec{display:flex;align-items:center;gap:.7em;margin-top:.5em;
    border-top:1px solid rgba(255,255,255,.09);padding-top:.5em;}
  .sh-fp__rank{width:1.9em;height:1.9em;flex:0 0 auto;border-radius:9px;border:2px solid;
    display:flex;align-items:center;justify-content:center;font-weight:900;
    font-size:clamp(13px,1.7vw,22px);text-shadow:0 2px 0 rgba(0,0,0,.5);}
  .sh-fp__score{display:flex;flex-direction:column;line-height:1.1;font-weight:900;
    font-size:clamp(11px,1.5vw,19px);}
  .sh-fp__lab{font-size:.55em;color:${PAL.dim};letter-spacing:.12em;}
  .sh-fp__plays{margin-left:auto;font-weight:800;font-size:clamp(8px,1.05vw,13px);color:${PAL.dim};}
  `;
  document.head.appendChild(s);
}
