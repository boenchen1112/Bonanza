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
import { PAL, num, el, hex, mountRoot, panel, sfx, createWipe, fmtScore, RANK_COLOR, reducedMotion } from './theme.js';
import { clamp01 } from '../core/util.js';
import { createBackdrop } from './backdrop.js';
import { CATALOG } from './games.js';
import { charById, charMesh, charBeat, disposeChar } from './chars.js';
import { profile, session } from './state.js';
import { goView, exitRoute } from './nav.js';

/** Rank -> the animator verdict whose reaction pose reads closest to it. */
const RANK_VERDICT = { S: 'perfect', A: 'great', B: 'good', C: 'good', D: 'miss' };
const ORDINAL = ['1ST', '2ND', '3RD', '4TH'];
/** Seconds before the card takes input: a held or late tap from the game must not skip it. */
const LOCK_S = 0.7;
/** Reveal timeline (s): score count-up, stats, rank stamp, party placings. */
const COUNT_AT = 0.3, COUNT_S = 0.9, STATS_AT = 0.45, STAMP_AT = 1.3, PARTY_AT = 1.65, REVEAL_END = 2.4;
/** Each rank has its own sting — a D used to play the same cheer as a B. */
const RANK_STING = { S: 'fanfare', A: 'perfect', B: 'great', C: 'good', D: 'miss' };

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

    // Same void set as the title screen — see the note there.
    ctx.scene.userData.envPreset = 'void';
    ctx.scene.userData.groundY = -4.4;

    S.back = createBackdrop(ctx, { accent: game.color, density: 0.75 });
    ctx.fx.attach(ctx.scene);
    ctx.scene.background = new THREE.Color(0x0b0a1a);

    // Direct loads (deep link, harness) have no session: show the default star
    // rather than an empty stage beside the scorecard.
    const humanChar = session.players.find((p) => !p.isCpu)?.char || 'bopp';
    if (humanChar) {
      const m = charMesh(charById(humanChar), {});
      m.position.set(-4.5, -1.1, 0);   // left of the scorecard panel, clear of it
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

    // The card reveals itself in order (update() drives it): score counts up,
    // the stats land one by one, the rank stamps down with its own sting,
    // then — in a party — the round's placings slide in.
    const scoreLine = el('div', 'sh-res__score');
    scoreLine.innerHTML = `<span class="sh-mono">0</span>`
      + `<span class="sh-res__acc">${((result.accuracy || 0) * 100).toFixed(1)}% ACC</span>`;
    card.appendChild(scoreLine);
    S.scoreEl = scoreLine.firstChild;
    rankEl.classList.add('sh-res__rank--wait');
    S.rankEl = rankEl;
    S.stamped = false;

    const st = result.stats || {};
    const stats = el('div', 'sh-res__stats');
    S.reveal = [];
    [['PERFECT', st.perfect], ['GREAT', st.great], ['GOOD', st.good], ['MISS', st.miss], ['COMBO', st.maxCombo]]
      .forEach(([label, v], i) => {
        const cell = el('div', 'sh-res__stat sh-res__in');
        cell.innerHTML = `<span class="sh-res__statN">${v || 0}</span><span class="sh-res__statL">${label}</span>`;
        stats.appendChild(cell);
        S.reveal.push({ el: cell, at: STATS_AT + i * 0.08 });
      });
    card.appendChild(stats);

    // In a party the round is a contest: every player's placing, not just yours.
    if (inParty) {
      const round = session.party?.scores?.[session.party.scores.length - 1];
      if (round?.players?.length) {
        const list = el('div', 'sh-res__party');
        round.players.slice().sort((a, b) => a.place - b.place).forEach((r, i) => {
          const p = session.players.find((q) => q.id === r.id);
          if (!p) return;
          const tied = round.players.filter((q) => q.place === r.place).length > 1;
          const row = el('div', 'sh-res__prow sh-res__in');
          if (!p.isCpu) row.classList.add('sh-res__prow--you');
          row.style.setProperty('--c', hex(charById(p.char).color));
          row.innerHTML = `<span class="sh-res__pl">${tied ? 'T-' : ''}${ORDINAL[r.place - 1] || r.place + 'TH'}</span>`
            + `<span class="sh-res__pn"></span><span class="sh-res__pp sh-mono">+${r.points}</span>`;
          row.children[1].textContent = p.name;
          list.appendChild(row);
          S.reveal.push({ el: row, at: PARTY_AT + i * 0.12 });
        });
        card.appendChild(list);
      }
    }

    // A first-ever D is technically a record; it is not worth a sticker.
    if ((S.fold?.newScore || S.fold?.newRank) && result.rank !== 'D' && (result.score || 0) > 0) {
      const badge = el('div', 'sh-stamp sh-res__badge sh-res__in', S.fold.newRank ? 'NEW RANK!' : 'NEW BEST!');
      card.appendChild(badge);
      S.reveal.push({ el: badge, at: STAMP_AT + 0.25 });
    }

    root.appendChild(card);
    S.card = card;

    // The prompt appears when the card starts listening, not before.
    const hint = el('div', 'sh-hint sh-res__hint');
    hint.innerHTML = '<span><b class="sh-key">SPACE</b>continue</span>';
    root.appendChild(hint);
    S.hint = hint;

    S.wipe = createWipe(root, { color: rankColor === PAL.dim ? PAL.violet : rankColor, mode: 'in' });
  },

  start(ctx) { sfx(ctx, 'ui'); },

  update(ctx, dt, beat) {
    if (!S) return;
    S.t += dt;
    S.back.update(dt, beat, S.t);
    S.wipe.update(dt);
    if (S.t >= LOCK_S) S.hint.classList.add('sh-res__hint--on');
    if (S.cast) charBeat(S.cast, beat, dt);

    // --- the reveal
    const target = S.result.score || 0;
    const k = clamp01((S.t - COUNT_AT) / COUNT_S);
    S.scoreEl.textContent = fmtScore(Math.round(target * (1 - (1 - k) ** 3)));
    for (const r of S.reveal) if (S.t >= r.at) r.el.classList.add('sh-res__in--on');
    if (!S.stamped && S.t >= STAMP_AT) {
      S.stamped = true;
      S.rankEl.classList.remove('sh-res__rank--wait');
      S.rankEl.classList.add('sh-res__rank--stamp');
      sfx(ctx, RANK_STING[S.result.rank] || 'good');
      if (S.result.rank === 'S' || S.result.rank === 'A') {
        ctx.stage.flash?.(0.18, RANK_COLOR[S.result.rank]);
        ctx.fx.confetti([1.2, 2.2, 0], { count: S.result.rank === 'S' ? 90 : 40 });
      }
      ctx.stage.shake?.(S.result.rank === 'D' ? 0.03 : 0.08, [0, -1, 0]);
    }
    // The centring translate must be restated: an inline `scale()` alone
    // replaced the stylesheet's translate(-50%,-50%) and threw the card
    // (and its NEW RANK! stamp) off to the lower right.
    S.card.style.transform = `translate(-50%,-50%) scale(${(1 + Math.sin(S.t * 3) * 0.006).toFixed(3)})`;

    if (S.exit) {
      S.exit.t += dt;
      if (!S.exit.fired && S.exit.t > 0.12) { S.exit.fired = true; S.wipe.play(S.exit.go, S.exit.color); }
    }
  },

  input(ctx, events) {
    if (!S || S.exit || S.t < LOCK_S) return;
    for (const e of events) {
      if (!e.down) continue;
      // A press mid-reveal finishes it; the next one moves on.
      if (S.t < REVEAL_END && (e.action === 'a' || e.action === 'b')) { S.t = REVEAL_END; continue; }
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
  .sh-res__card{position:absolute;left:57%;top:44%;transform:translate(-50%,-50%);
    width:min(52vw,600px);padding:clamp(18px,2.4vw,32px);text-align:center;}
  .sh-res__rank{width:2.2em;height:2.2em;margin:0 auto .3em;border-radius:16px;border:3px solid;
    display:flex;align-items:center;justify-content:center;font-weight:900;
    font-size:clamp(28px,4.4vw,54px);text-shadow:0 3px 0 rgba(0,0,0,.5);}
  .sh-res__rank--wait{opacity:0;transform:scale(2.6) rotate(-14deg);}
  .sh-res__rank--stamp{opacity:1;transform:none;
    transition:transform .22s cubic-bezier(.3,1.7,.5,1),opacity .08s linear;}
  .sh-res__in{opacity:0;transform:translateY(.7em);}
  .sh-res__in--on{opacity:1;transform:none;transition:opacity .2s,transform .28s cubic-bezier(.3,1.6,.5,1);}
  .sh-res__party{margin-top:.8em;display:flex;flex-direction:column;gap:.25em;
    border-top:1px solid rgba(255,255,255,.09);padding-top:.6em;}
  .sh-res__prow{display:grid;grid-template-columns:3.4em 1fr auto;align-items:center;gap:.6em;
    font-weight:900;font-size:clamp(11px,1.3vw,16px);letter-spacing:.06em;text-align:left;
    padding:.2em .6em;border-radius:8px;border-left:4px solid var(--c);background:rgba(255,255,255,.04);}
  .sh-res__prow--you{background:rgba(255,229,138,.12);}
  .sh-res__prow--you .sh-res__pn::after{content:' · YOU';color:${PAL.yellow};}
  .sh-res__pl{color:${PAL.dim};}
  .sh-res__pn{color:var(--c);}
  .sh-res__name{font-size:clamp(20px,3.4vw,40px);}
  .sh-res__score{margin-top:.3em;font-weight:900;font-size:clamp(16px,2.4vw,28px);
    display:flex;justify-content:center;gap:.6em;align-items:baseline;}
  .sh-res__acc{color:${PAL.dim};font-size:.6em;}
  .sh-res__stats{margin-top:.9em;display:grid;grid-template-columns:repeat(5,1fr);gap:.6em;
    border-top:1px solid rgba(255,255,255,.09);padding-top:.7em;}
  .sh-res__stat{display:flex;flex-direction:column;}
  .sh-res__statN{font-weight:900;font-size:clamp(14px,2vw,24px);}
  .sh-res__statL{font-weight:800;font-size:clamp(7px,.9vw,11px);color:${PAL.dim};letter-spacing:.08em;}
  .sh-res__hint{opacity:0;transition:opacity .25s;}
  .sh-res__hint--on{opacity:1;}
  .sh-res__badge{position:absolute;right:-6%;top:-8%;font-size:clamp(11px,1.6vw,20px);
    background:${PAL.coral};transform:rotate(-10deg);}
  .sh-res__badge.sh-res__in{transform:rotate(-10deg) scale(1.9);}
  .sh-res__badge.sh-res__in--on{transform:rotate(-10deg);}
  `;
  document.head.appendChild(s);
}
