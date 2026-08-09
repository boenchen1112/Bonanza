/**
 * Party hub.  [shell agent owns this file]
 *
 * The scene `roster.js` hands off to once a party lineup is locked in, and
 * where `results.js` sends a finished round back to. Two states only:
 *   - mid-party: "ROUND N of LENGTH" for whichever game is up next, SPACE
 *     to start it.
 *   - party done: a recap of what was played, DONE back to title.
 *
 * There is no CPU-scoring model yet (see `results.js`/`state.js`'s
 * `recordPartyRound` comment), so this is a recap of the human's own runs,
 * not a multiplayer standings/crown screen.
 */
import * as THREE from 'three';
import { PAL, num, el, mountRoot, panel, sfx, createWipe, beatPulse, fmtScore, RANK_COLOR } from './theme.js';
import { createBackdrop } from './backdrop.js';
import { CATALOG } from './games.js';
import { session } from './state.js';
import { goView, partyConfirmRoute } from './nav.js';

let S = null;

export default {
  id: 'party', name: 'Party',

  load(ctx) {
    S = { t: 0, exit: null };

    S.back = createBackdrop(ctx, { accent: num(PAL.yellow), density: 0.75 });
    ctx.fx.attach(ctx.scene);
    ctx.scene.background = new THREE.Color(0x0b0a1a);
    ctx.camera.position.set(0, 0, 9);

    const root = mountRoot(ctx, 'sh-party');
    S.root = root;
    injectCss();

    if (!session.party) {
      // Reached with no party in progress (e.g. a deep link) — nothing to
      // show, so don't strand the player on a blank screen.
      S.exit = { t: 0, fired: true, color: PAL.violet, go: () => goView(ctx, 'title', {}) };
      S.wipe = createWipe(root, { color: PAL.violet, mode: 'in' });
      S.wipe.play(S.exit.go, S.exit.color);
      return;
    }

    S.done = session.partyDone;

    const card = panel('sh-party__card', PAL.yellow);
    root.appendChild(card);
    S.card = card;

    if (S.done) {
      card.appendChild(el('div', 'sh-display sh-party__title', 'PARTY COMPLETE'));
      const list = el('div', 'sh-party__recap');
      let total = 0;
      for (const round of session.party.scores) {
        total += round.score;
        const g = CATALOG.find((c) => c.id === round.game);
        const row = el('div', 'sh-party__recapRow');
        const rank = el('span', 'sh-party__recapRank', round.rank || '–');
        rank.style.color = RANK_COLOR[round.rank] || PAL.dim;
        row.appendChild(rank);
        row.appendChild(el('span', 'sh-party__recapName', g?.name || round.game));
        row.appendChild(el('span', 'sh-mono', fmtScore(round.score)));
        list.appendChild(row);
      }
      card.appendChild(list);
      const totalEl = el('div', 'sh-party__total');
      totalEl.innerHTML = `TOTAL <span class="sh-mono">${fmtScore(total)}</span>`;
      card.appendChild(totalEl);
    } else {
      const idx = session.party.index;
      const len = session.party.length;
      const gameId = session.currentGame;
      const g = CATALOG.find((c) => c.id === gameId);
      card.appendChild(el('div', 'sh-sub sh-party__round', `ROUND ${idx + 1} OF ${len}`));
      card.appendChild(el('div', 'sh-display sh-party__title', g?.name || gameId));
      if (g?.blurb) card.appendChild(el('div', 'sh-party__blurb', g.blurb));
    }

    const hint = el('div', 'sh-hint');
    hint.innerHTML = S.done
      ? '<span><b class="sh-key">SPACE</b>done</span>'
      : '<span><b class="sh-key">SPACE</b>start</span>';
    root.appendChild(hint);

    S.wipe = createWipe(root, { color: PAL.yellow, mode: 'in' });
  },

  start(ctx) { ctx.clock.setBpm(124); ctx.clock.start(ctx.clock.now() + 0.1, 0); sfx(ctx, S?.done ? 'fanfare' : 'ui'); },

  update(ctx, dt, beat) {
    if (!S) return;
    S.t += dt;
    S.back.update(dt, beat, S.t);
    S.wipe.update(dt);
    const pulse = beatPulse(beat, 6);
    if (S.card) S.card.style.transform = `scale(${(1 + pulse * 0.01).toFixed(3)})`;
    if (S.exit && !S.exit.fired) {
      S.exit.t += dt;
      if (S.exit.t > 0.1) { S.exit.fired = true; S.wipe.play(S.exit.go, S.exit.color); }
    }
  },

  input(ctx, events) {
    if (!S || !session.party || S.exit) return;
    for (const e of events) {
      if (!e.down) continue;
      if (e.action === 'a') {
        sfx(ctx, 'fanfare');
        const r = partyConfirmRoute({ done: S.done, gameId: session.currentGame });
        if (S.done) session.endParty();
        S.exit = { t: 0, fired: false, color: S.done ? PAL.violet : PAL.yellow, go: () => goView(ctx, r.view, r.opts) };
      } else if (e.action === 'b' || e.action === 'pause') {
        sfx(ctx, 'uiBack');
        session.endParty();
        const r = partyConfirmRoute({ done: true });
        S.exit = { t: 0, fired: false, color: PAL.violet, go: () => goView(ctx, r.view, r.opts) };
      }
    }
  },

  dispose() {
    if (!S) return;
    S.back.dispose();
    S.root.remove();
    S = null;
  },
};

let cssDone = false;
function injectCss() {
  if (cssDone || document.getElementById('sh-party-css')) { cssDone = true; return; }
  cssDone = true;
  const s = document.createElement('style');
  s.id = 'sh-party-css';
  s.textContent = `
  .sh-party__card{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);
    width:min(52vw,560px);padding:clamp(18px,2.4vw,32px);text-align:center;}
  .sh-party__round{font-size:clamp(10px,1.4vw,17px);letter-spacing:.1em;}
  .sh-party__title{font-size:clamp(22px,3.6vw,44px);margin-top:.15em;}
  .sh-party__blurb{margin-top:.4em;font-weight:700;font-size:clamp(10px,1.3vw,15px);color:${PAL.dim};}
  .sh-party__recap{margin-top:1em;display:flex;flex-direction:column;gap:.4em;
    border-top:1px solid rgba(255,255,255,.09);padding-top:.8em;}
  .sh-party__recapRow{display:flex;align-items:center;gap:.7em;font-weight:800;
    font-size:clamp(11px,1.4vw,16px);}
  .sh-party__recapRank{width:1.6em;font-weight:900;}
  .sh-party__recapName{flex:1;text-align:left;}
  .sh-party__total{margin-top:.9em;font-weight:900;font-size:clamp(14px,2vw,22px);
    border-top:1px solid rgba(255,255,255,.09);padding-top:.7em;}
  `;
  document.head.appendChild(s);
}
