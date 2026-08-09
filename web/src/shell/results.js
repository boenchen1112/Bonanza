/**
 * Results screen.  [shell agent owns this file]
 *
 * The one place a finished round becomes a permanent record: `profile.submit()`
 * runs here, once, on load — nowhere else in the codebase calls it. From here
 * "continue" goes back to wherever makes sense for how the round was reached:
 * free play returns to the carousel focused on the game just played; a party
 * round in progress advances party bookkeeping and heads to the party scene;
 * anything else falls back the way `play.js`'s pause-menu quit does.
 */
import * as THREE from 'three';
import { PAL, num, el, mountRoot, panel, sfx, createWipe, fmtScore, RANK_COLOR, reducedMotion } from './theme.js';
import { createBackdrop } from './backdrop.js';
import { CATALOG } from './games.js';
import { charById, charMesh, charBeat, disposeChar } from './chars.js';
import { profile, session } from './state.js';
import { goView, exitRoute } from './nav.js';

/** Rank -> the animator verdict whose reaction pose reads closest to it. */
const RANK_VERDICT = { S: 'perfect', A: 'great', B: 'good', C: 'good', D: 'miss' };

let S = null;

export default {
  id: 'results', name: 'Results',

  load(ctx) {
    const gameId = ctx.opts?.game || session.lastGame;
    const result = ctx.opts?.result || session.lastResult || {};
    const game = CATALOG.find((g) => g.id === gameId) || CATALOG[0];
    const inParty = !!(ctx.opts?.party ?? (session.mode === 'party' && session.party));

    S = {
      t: 0, gameId, result, game, from: ctx.opts?.from || 'freeplay', inParty,
      cast: null, exit: null, reduce: reducedMotion(),
    };

    // The one and only place a round becomes a permanent record.
    S.fold = profile.submit(gameId, result);
    if (inParty) session.recordPartyRound(gameId, result);

    S.back = createBackdrop(ctx, { accent: game.color, density: 0.75 });
    ctx.fx.attach(ctx.scene);
    ctx.scene.background = new THREE.Color(0x0b0a1a);

    const humanChar = session.players.find((p) => !p.isCpu)?.char;
    if (humanChar) {
      const m = charMesh(charById(humanChar), {});
      m.position.set(0, -1.1, 0);
      m.userData.baseY = -1.1;
      m.userData.baseScale = 1.3;
      m.scale.setScalar(1.3);
      ctx.scene.add(m);
      S.cast = m;
      const verdict = RANK_VERDICT[result.rank] || 'good';
      m.userData.charApi?.react?.(verdict);
    }

    ctx.camera.position.set(0, 1.1, 8.6);
    ctx.camera.lookAt(0, 0.6, 0);

    const root = mountRoot(ctx, 'sh-res');
    S.root = root;
    injectCss();

    const rank = result.rank || '–';
    const rankColor = RANK_COLOR[result.rank] || PAL.dim;

    const card = panel('sh-res__card', rankColor);
    const rankEl = el('div', 'sh-res__rank', rank);
    rankEl.style.color = rankColor;
    rankEl.style.borderColor = rankColor;
    card.appendChild(rankEl);

    const name = el('div', 'sh-display sh-res__name', game.name);
    card.appendChild(name);

    const scoreLine = el('div', 'sh-res__score');
    scoreLine.innerHTML = `<span class="sh-mono">${fmtScore(result.score || 0)}</span>`
      + `<span class="sh-res__acc">${((result.accuracy || 0) * 100).toFixed(1)}%</span>`;
    card.appendChild(scoreLine);

    const st = result.stats || {};
    const stats = el('div', 'sh-res__stats');
    [['PERFECT', st.perfect], ['GREAT', st.great], ['GOOD', st.good], ['MISS', st.miss], ['COMBO', st.maxCombo]]
      .forEach(([label, v]) => {
        const cell = el('div', 'sh-res__stat');
        cell.innerHTML = `<span class="sh-res__statN">${v || 0}</span><span class="sh-res__statL">${label}</span>`;
        stats.appendChild(cell);
      });
    card.appendChild(stats);

    if (S.fold?.newScore || S.fold?.newRank) {
      const badge = el('div', 'sh-stamp sh-res__badge', S.fold.newRank ? 'NEW RANK!' : 'NEW BEST!');
      card.appendChild(badge);
    }

    root.appendChild(card);
    S.card = card;

    const hint = el('div', 'sh-hint');
    hint.innerHTML = '<span><b class="sh-key">SPACE</b>continue</span>';
    root.appendChild(hint);

    S.wipe = createWipe(root, { color: rankColor === PAL.dim ? PAL.violet : rankColor, mode: 'in' });
  },

  start(ctx) { sfx(ctx, S.result.rank === 'S' ? 'fanfare' : 'great'); },

  update(ctx, dt, beat) {
    if (!S) return;
    S.t += dt;
    S.back.update(dt, beat, S.t);
    S.wipe.update(dt);
    if (S.cast) charBeat(S.cast, beat, dt);
    S.card.style.transform = `scale(${(1 + Math.sin(S.t * 3) * 0.006).toFixed(3)})`;

    if (S.exit) {
      S.exit.t += dt;
      if (!S.exit.fired && S.exit.t > 0.12) { S.exit.fired = true; S.wipe.play(S.exit.go, S.exit.color); }
    }
  },

  input(ctx, events) {
    if (!S || S.exit) return;
    for (const e of events) {
      if (!e.down) continue;
      if (e.action === 'a' || e.action === 'b' || e.action === 'pause') {
        sfx(ctx, 'ui');
        S.exit = { t: 0, fired: false, color: PAL.violet, go: () => routeOut(ctx) };
      }
    }
  },

  dispose() {
    if (!S) return;
    if (S.cast) disposeChar(S.cast);
    S.back.dispose();
    S.root.remove();
    S = null;
  },
};

function routeOut(ctx) {
  const inParty = S.inParty && !!session.party;
  const r = exitRoute({ inParty, from: S.from, gameId: S.gameId });
  goView(ctx, r.view, r.opts);
}

let cssDone = false;
function injectCss() {
  if (cssDone || document.getElementById('sh-res-css')) { cssDone = true; return; }
  cssDone = true;
  const s = document.createElement('style');
  s.id = 'sh-res-css';
  s.textContent = `
  .sh-res__card{position:absolute;left:50%;top:44%;transform:translate(-50%,-50%);
    width:min(56vw,620px);padding:clamp(18px,2.4vw,32px);text-align:center;}
  .sh-res__rank{width:2.2em;height:2.2em;margin:0 auto .3em;border-radius:16px;border:3px solid;
    display:flex;align-items:center;justify-content:center;font-weight:900;
    font-size:clamp(28px,4.4vw,54px);text-shadow:0 3px 0 rgba(0,0,0,.5);}
  .sh-res__name{font-size:clamp(20px,3.4vw,40px);}
  .sh-res__score{margin-top:.3em;font-weight:900;font-size:clamp(16px,2.4vw,28px);
    display:flex;justify-content:center;gap:.6em;align-items:baseline;}
  .sh-res__acc{color:${PAL.dim};font-size:.6em;}
  .sh-res__stats{margin-top:.9em;display:grid;grid-template-columns:repeat(5,1fr);gap:.6em;
    border-top:1px solid rgba(255,255,255,.09);padding-top:.7em;}
  .sh-res__stat{display:flex;flex-direction:column;}
  .sh-res__statN{font-weight:900;font-size:clamp(14px,2vw,24px);}
  .sh-res__statL{font-weight:800;font-size:clamp(7px,.9vw,11px);color:${PAL.dim};letter-spacing:.08em;}
  .sh-res__badge{position:absolute;right:-6%;top:-8%;font-size:clamp(11px,1.6vw,20px);
    background:${PAL.coral};transform:rotate(-10deg);}
  `;
  document.head.appendChild(s);
}
