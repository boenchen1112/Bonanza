/**
 * Party hub.  [shell agent owns this file]
 *
 * The scene `roster.js` hands off to once a party lineup is locked in, and
 * where `results.js` sends a finished round back to. The whole lineup stands
 * on the stage, each on a pedestal as tall as their points, the leader in a
 * crown, a nameplate in front of each: place, name, points and what the
 * last round paid. Two states:
 *   - mid-party: "ROUND N of LENGTH" for whichever game is up next, SPACE
 *     to start it.
 *   - party done: a podium — winner on the top step, crowned and dancing —
 *     and a round-by-round table of who earned what. DONE back to title.
 *
 * CPU rounds come from `state.js` `cpuRound()` (the minigames are single-
 * player; the CPUs post a skill-shaped result on the human's score scale).
 */
import * as THREE from 'three';
import { PAL, num, el, mountRoot, panel, sfx, createWipe, beatPulse, hex } from './theme.js';
import { createBackdrop } from './backdrop.js';
import { CATALOG } from './games.js';
import { charById, charMesh, charBeat, disposeChar, addCrown } from './chars.js';
import { session } from './state.js';
import { goView, partyConfirmRoute } from './nav.js';
import { damp } from '../core/util.js';

const FLOOR = -1.6;
const PLACE = ['1ST', '2ND', '3RD', '4TH'];
const PLACE_COLOR = [PAL.yellow, '#dfe6ff', '#ffb27a', PAL.dim];
/** Podium step heights for the final screen, by place. */
const PODIUM_H = [0.8, 0.55, 0.34, 0.1];
/** Characters stand a little larger than life on this stage. */
const CHAR_S = 1.3;

let S = null;
const _v = new THREE.Vector3();

export default {
  id: 'party', name: 'Party',

  load(ctx) {
    S = { t: 0, exit: null, cast: [], tags: [], geos: [], mats: [] };

    S.back = createBackdrop(ctx, { accent: num(PAL.yellow), density: 0.75 });
    ctx.fx.attach(ctx.scene);
    ctx.scene.background = new THREE.Color(0x0b0a1a);
    // The default arena's floor sits at 0 and would hide the pedestals and
    // the lower half of every character standing on the house floor.
    ctx.scene.userData.groundY = FLOOR;
    // Looking down onto the stage: the old level camera cut the horizon
    // through the middle of the frame over an empty floor.
    ctx.camera.position.set(0, 1.5, 10);
    ctx.camera.lookAt(0, 1.0, 0);

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
    const party = session.party;
    const last = party.scores[party.scores.length - 1] || null;
    const standings = session.standings();
    // Shared places are shared: level on points and wins is a tie, not an
    // order the id happened to pick. 0-based here for the lookup tables.
    const places = session.standingPlaces();
    const placeOf = new Map(standings.map((p) => [p.id, places.get(p.id) - 1]));
    const tiedAt = (pl) => standings.filter((q) => placeOf.get(q.id) === pl).length > 1;

    // ------------------------------------------------------------ the cast
    const players = session.players;
    const n = players.length;
    // Mid-party: lineup order, left to right. Final: a podium — 2nd, 1st, 3rd, 4th.
    const order = S.done
      ? [1, 0, 2, 3].map((pl) => standings[pl]).filter(Boolean)
      : players.slice();
    const spacing = n > 3 ? 2.5 : 2.8;
    order.forEach((p, i) => {
      const def = charById(p.char);
      const place = placeOf.get(p.id) ?? i;
      const x = (i - (order.length - 1) / 2) * spacing;
      const h = S.done ? PODIUM_H[place] : 0.16 + Math.min(1.3, p.points * 0.11);

      const geo = new THREE.BoxGeometry(1.5, 1, 1.2);
      geo.translate(0, 0.5, 0);
      const mat = new THREE.MeshStandardMaterial({
        color: new THREE.Color(def.color).lerp(new THREE.Color(0x1a1440), 0.45),
        emissive: new THREE.Color(def.color).multiplyScalar(0.12), roughness: 0.55, metalness: 0.2,
      });
      S.geos.push(geo); S.mats.push(mat);
      const ped = new THREE.Mesh(geo, mat);
      ped.position.set(x, FLOOR, 0);
      ped.scale.y = 0.05;
      ctx.scene.add(ped);

      const m = charMesh(def, {});
      m.position.set(x, FLOOR, 0.05);
      m.scale.setScalar(CHAR_S);
      ctx.scene.add(m);
      const crown = addCrown(m);
      crown.visible = p.points > 0 && place === 0;

      const api = m.userData.charApi;
      const lastRow = last?.players?.find((r) => r.id === p.id);
      if (S.done) {
        if (place === 0) api?.play('dance', { beatLock: true, bpm: 124, face: 'groove', beat: 0.25, blend: 0.3 });
        else api?.react?.(place === 1 ? 'great' : place === 2 ? 'good' : 'miss');
      } else if (lastRow) {
        api?.react?.(lastRow.place === 1 ? 'perfect' : lastRow.place === 2 ? 'great' : lastRow.place === 3 ? 'good' : 'miss');
      }

      // Nameplate: place · name · points (+ what the last round paid).
      const tag = el('div', 'sh-party__tag');
      tag.style.setProperty('--c', hex(def.color));
      const pl = el('span', 'sh-party__place', (tiedAt(place) ? 'T-' : '') + (PLACE[place] || ''));
      pl.style.color = PLACE_COLOR[place] || PAL.dim;
      tag.appendChild(pl);
      tag.appendChild(el('span', 'sh-party__name', p.name));
      const pts = el('span', 'sh-party__pts sh-mono', `${p.points} PTS`);
      tag.appendChild(pts);
      if (!S.done && lastRow) {
        const d = el('span', 'sh-party__delta', `+${lastRow.points}`);
        if (!lastRow.points) d.classList.add('sh-party__delta--zero');
        tag.appendChild(d);
      }
      if (!p.isCpu) tag.classList.add('sh-party__tag--you');
      root.appendChild(tag);

      S.cast.push({ m, ped, h, x, crown, tag, place, delay: 0.25 + i * 0.12 });
    });

    // --------------------------------------------------------------- card
    const card = panel('sh-party__card', S.done ? PAL.yellow : PAL.cyan);
    root.appendChild(card);
    S.card = card;

    if (S.done) {
      const ids = party.winners?.length ? party.winners : [party.winner ?? standings[0]?.id];
      const wins = ids.map((id) => players.find((p) => p.id === id)).filter(Boolean);
      const win = wins[0] || standings[0];
      card.appendChild(el('div', 'sh-sub sh-party__round', 'PARTY COMPLETE'));
      const title = el('div', 'sh-display sh-party__title', wins.length > 1
        ? `${wins.map((w) => w.name).join(' & ')} SHARE THE CROWN!`
        : `${win?.name || '???'} WINS!`);
      title.style.color = hex(charById(win?.char).color);
      // Level on points but split on wins: name the tiebreak that decided it.
      const second = standings[1];
      if (wins.length === 1 && second && second.points === win.points) {
        card.appendChild(el('div', 'sh-party__blurb', `Level on ${win.points} points — ${win.wins} round wins to ${second.wins} breaks the tie`));
      }
      card.appendChild(title);
      card.appendChild(recapTable(party, players));
    } else {
      const idx = party.index;
      const g = CATALOG.find((c) => c.id === session.currentGame);
      const finale = idx === party.length - 1;
      card.appendChild(el('div', 'sh-sub sh-party__round', finale ? `FINAL ROUND · ${idx + 1} OF ${party.length}` : `ROUND ${idx + 1} OF ${party.length}`));
      const title = el('div', 'sh-display sh-party__title', g?.name || session.currentGame);
      if (g?.color) title.style.color = typeof g.color === 'number' ? hex(g.color) : g.color;
      card.appendChild(title);
      if (g?.blurb) card.appendChild(el('div', 'sh-party__blurb', g.blurb));
      if (last) {
        const lg = CATALOG.find((c) => c.id === last.game);
        const top = (last.players || []).filter((r) => r.place === 1)
          .map((r) => players.find((p) => p.id === r.id)).filter(Boolean);
        if (top.length) {
          card.appendChild(el('div', 'sh-party__last', top.length > 1
            ? `${lg?.name || last.game}: ${top.map((w) => w.name).join(' & ')} tied it`
            : `${lg?.name || last.game}: ${top[0].name} took it`));
        }
      }
    }

    const hint = el('div', 'sh-hint');
    hint.innerHTML = S.done
      ? '<span><b class="sh-key">SPACE</b>done</span>'
      : '<span><b class="sh-key">SPACE</b>start</span><span><b class="sh-key">X</b>quit party</span>';
    root.appendChild(hint);

    S.wipe = createWipe(root, { color: PAL.yellow, mode: 'in' });
  },

  start(ctx) {
    ctx.clock.setBpm(124);
    ctx.clock.start(ctx.clock.now() + 0.1, 0);
    sfx(ctx, S?.done ? 'fanfare' : 'ui');
    if (S?.done) {
      const w = S.cast.find((c) => c.place === 0);
      if (w) ctx.fx.confetti([w.x, FLOOR + w.h + 2.2, 0], { count: 90 });
    }
  },

  update(ctx, dt, beat) {
    if (!S) return;
    S.t += dt;
    S.back.update(dt, beat, S.t);
    S.wipe.update(dt);
    const pulse = beatPulse(beat, 6);
    if (S.card) S.card.style.transform = `translate(-50%,0) scale(${(1 + pulse * 0.01).toFixed(3)})`;

    const cam = ctx.camera;
    cam.updateMatrixWorld();
    for (const c of S.cast) {
      // Pedestals rise one after another into their heights; the character rides the top.
      const target = S.t > c.delay ? c.h : 0.05;
      c.ped.scale.y = damp(c.ped.scale.y, target, 7, dt);
      c.m.position.y = FLOOR + c.ped.scale.y;
      charBeat(c.m, beat, dt);
      if (c.crown.visible) c.crown.rotation.y += dt * 0.8;
      // Tag follows the head in screen space.
      // Nameplate on the floor in front of the pedestal: over the heads it
      // collided with the card on the podium.
      _v.set(c.x, FLOOR, 0.75).project(cam);
      c.tag.style.left = `${((_v.x + 1) / 2) * 100}%`;
      c.tag.style.top = `${((1 - _v.y) / 2) * 100}%`;
      c.tag.style.opacity = S.t > c.delay + 0.15 ? '1' : '0';
    }

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
        // Wipe out in the next game's colour: its title card is the same colour.
        const next = CATALOG.find((c) => c.id === session.currentGame);
        const color = S.done ? PAL.violet : hex(next?.color ?? PAL.yellow);
        S.exit = { t: 0, fired: false, color, go: () => goView(ctx, r.view, r.opts) };
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
    for (const c of S.cast) {
      c.m.parent?.remove(c.m);
      c.ped.parent?.remove(c.ped);
      disposeChar(c.m);
    }
    for (const g of S.geos) g.dispose();
    for (const m of S.mats) m.dispose();
    S.back.dispose();
    S.root.remove();
    S = null;
  },
};

/** Round-by-round points: one row per game, one column per player, totals last. */
function recapTable(party, players) {
  const t = el('table', 'sh-party__table');
  const head = el('tr');
  head.appendChild(el('th', '', ''));
  for (const p of players) {
    const th = el('th', '', p.name);
    th.style.color = hex(charById(p.char).color);
    head.appendChild(th);
  }
  t.appendChild(head);
  for (const round of party.scores) {
    const tr = el('tr');
    const g = CATALOG.find((c) => c.id === round.game);
    tr.appendChild(el('td', 'sh-party__game', (g?.name || round.game) + (round.sim ? ' *' : '')));
    for (const p of players) {
      const r = round.players?.find((q) => q.id === p.id);
      const td = el('td', 'sh-mono', r ? `+${r.points}` : '–');
      if (r?.place === 1) td.classList.add('sh-party__win');
      tr.appendChild(td);
    }
    t.appendChild(tr);
  }
  const tot = el('tr', 'sh-party__totals');
  tot.appendChild(el('td', 'sh-party__game', 'TOTAL'));
  for (const p of players) tot.appendChild(el('td', 'sh-mono', String(p.points)));
  t.appendChild(tot);
  if (!party.scores.some((r) => r.sim)) return t;
  // Solo games have no rivals on screen; say how the CPUs' rounds were decided.
  const wrap = el('div');
  wrap.appendChild(t);
  wrap.appendChild(el('div', 'sh-party__note', '* solo game — CPU rivals played off-screen at their skill level'));
  return wrap;
}

let cssDone = false;
function injectCss() {
  if (cssDone || document.getElementById('sh-party-css')) { cssDone = true; return; }
  cssDone = true;
  const s = document.createElement('style');
  s.id = 'sh-party-css';
  s.textContent = `
  .sh-party__card{position:absolute;left:50%;top:4%;transform:translate(-50%,0);transform-origin:50% 0;
    width:min(60vw,640px);padding:clamp(12px,1.8vw,24px) clamp(16px,2.4vw,32px);text-align:center;}
  .sh-party__round{font-size:clamp(10px,1.3vw,16px);letter-spacing:.12em;}
  .sh-party__title{font-size:clamp(22px,3.8vw,46px);margin-top:.12em;}
  .sh-party__blurb{margin-top:.35em;font-weight:700;font-size:clamp(10px,1.25vw,15px);color:${PAL.dim};}
  .sh-party__last{margin-top:.6em;padding-top:.5em;border-top:1px solid rgba(255,255,255,.09);
    font-weight:800;font-size:clamp(10px,1.2vw,14px);color:#e9ecff;}
  .sh-party__note{margin-top:.5em;font-weight:700;font-size:clamp(8px,.95vw,12px);color:${PAL.dim};}
  .sh-party__table{margin:.7em auto 0;border-collapse:collapse;font-weight:800;
    font-size:clamp(10px,1.2vw,15px);}
  .sh-party__table th{padding:.15em .7em;font-weight:900;letter-spacing:.04em;}
  .sh-party__table td{padding:.18em .7em;color:#e9ecff;}
  .sh-party__table tr+tr td{border-top:1px solid rgba(255,255,255,.07);}
  .sh-party__game{text-align:left;color:${PAL.dim}!important;}
  .sh-party__win{color:${PAL.yellow}!important;}
  .sh-party__totals td{font-weight:900;color:#fff;border-top:2px solid rgba(255,255,255,.2)!important;}
  .sh-party__tag{position:absolute;transform:translate(-50%,10px);display:flex;flex-direction:column;
    align-items:center;gap:.12em;padding:.35em .8em .4em;border-radius:12px;white-space:nowrap;
    background:rgba(14,10,36,.88);border:2px solid var(--c);box-shadow:0 4px 0 rgba(0,0,0,.45);
    font-size:clamp(10px,1.2vw,15px);font-weight:900;transition:opacity .25s;opacity:0;}
  .sh-party__tag--you{box-shadow:0 4px 0 rgba(0,0,0,.45),0 0 18px -2px var(--c);}
  .sh-party__tag--you::before{content:'YOU';position:absolute;left:50%;top:0;transform:translate(-50%,-60%);
    font-weight:900;font-size:.62em;letter-spacing:.12em;color:#1a1030;background:var(--c);
    border-radius:999px;padding:.15em .6em;}
  .sh-party__place{font-size:.8em;letter-spacing:.12em;}
  .sh-party__name{font-size:1.15em;color:var(--c);letter-spacing:.02em;}
  .sh-party__pts{color:#fff;}
  .sh-party__delta{position:absolute;right:-.9em;top:-.8em;padding:.1em .45em;border-radius:999px;
    background:${PAL.green};color:#08131a;font-size:.9em;box-shadow:0 2px 0 rgba(0,0,0,.4);}
  .sh-party__delta--zero{background:#3a3f60;color:${PAL.dim};}
  `;
  document.head.appendChild(s);
}
