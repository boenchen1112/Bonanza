/**
 * Options screen.  [shell agent owns this file]
 *
 * Everything here writes straight through `profile.setOption()` (localStorage,
 * survives a refresh) and, for the two audio rows, live-applies immediately
 * via `ctx.audio.setVolume()` so a player hears the change before confirming
 * anything.
 */
import * as THREE from 'three';
import { PAL, num, el, mountRoot, panel, sfx, createWipe, beatPulse, reducedMotion } from './theme.js';
import { createBackdrop } from './backdrop.js';
import { profile } from './state.js';
import { goView } from './nav.js';

const ROWS = [
  { id: 'music', label: 'MUSIC VOLUME', type: 'pct' },
  { id: 'sfx', label: 'SFX VOLUME', type: 'pct' },
  { id: 'offsetMs', label: 'TIMING OFFSET', type: 'ms' },
  { id: 'reduceMotion', label: 'REDUCE MOTION', type: 'bool' },
  { id: 'reset', label: 'RESET PROGRESS', type: 'action' },
  { id: 'back', label: 'BACK', type: 'action' },
];
const OFFSET_MIN = -150, OFFSET_MAX = 150, OFFSET_STEP = 5;

let S = null;

export default {
  id: 'options', name: 'Options',

  load(ctx) {
    S = { t: 0, sel: 0, rows: [], confirmReset: false, reduce: reducedMotion() };

    S.back = createBackdrop(ctx, { accent: num(PAL.green), density: 0.6 });
    ctx.fx.attach(ctx.scene);
    ctx.scene.background = new THREE.Color(0x0b0a1a);
    ctx.camera.position.set(0, 0, 9);

    const root = mountRoot(ctx, 'sh-opt');
    S.root = root;
    injectCss();

    const head = el('div', 'sh-opt__head');
    head.appendChild(el('div', 'sh-display sh-opt__title', 'OPTIONS'));
    head.appendChild(el('div', 'sh-sub', 'mix · timing · reset'));
    root.appendChild(head);

    const list = el('div', 'sh-opt__list');
    ROWS.forEach((row) => {
      const p = panel('sh-opt__row', PAL.green);
      const label = el('div', 'sh-opt__label', row.label);
      const value = el('div', 'sh-opt__value', '');
      p.appendChild(label);
      p.appendChild(value);
      list.appendChild(p);
      S.rows.push({ el: p, value });
    });
    root.appendChild(list);
    S.list = list;

    const confirm = el('div', 'sh-opt__confirm', 'RESET ALL PROGRESS? SPACE to confirm · X to cancel');
    confirm.style.opacity = '0';
    root.appendChild(confirm);
    S.confirmEl = confirm;

    const hint = el('div', 'sh-hint');
    S.hint = hint;
    root.appendChild(hint);

    S.wipe = createWipe(root, { color: PAL.green, mode: 'in' });
    refreshAll();
    applySelection();
  },

  start(ctx) { ctx.clock.setBpm(124); ctx.clock.start(ctx.clock.now() + 0.1, 0); },

  update(ctx, dt, beat) {
    if (!S) return;
    S.t += dt;
    S.back.update(dt, beat, S.t);
    S.wipe.update(dt);
    const pulse = beatPulse(beat, 6);
    for (let i = 0; i < S.rows.length; i++) {
      const on = i === S.sel;
      const sc = on ? 1.03 + pulse * 0.02 : 1;
      S.rows[i].el.style.transform = `scale(${sc.toFixed(3)})`;
    }
    if (S.exit) {
      S.exit.t += dt;
      if (!S.exit.fired && S.exit.t > 0.1) { S.exit.fired = true; S.wipe.play(S.exit.go, S.exit.color); }
    }
  },

  input(ctx, events) {
    if (!S || S.exit) return;
    for (const e of events) {
      if (!e.down) continue;

      if (S.confirmReset) {
        if (e.action === 'a') {
          profile.reset();
          S.confirmReset = false;
          S.confirmEl.style.opacity = '0';
          refreshAll();
          sfx(ctx, 'fanfare');
        } else if (e.action === 'b' || e.action === 'pause') {
          S.confirmReset = false;
          S.confirmEl.style.opacity = '0';
          sfx(ctx, 'uiBack');
        }
        continue;
      }

      if (e.action === 'up') { S.sel = (S.sel + ROWS.length - 1) % ROWS.length; sfx(ctx, 'ui'); applySelection(); }
      else if (e.action === 'down') { S.sel = (S.sel + 1) % ROWS.length; sfx(ctx, 'ui'); applySelection(); }
      else if (e.action === 'left' || e.action === 'right') {
        adjust(ctx, e.action === 'right' ? 1 : -1);
      } else if (e.action === 'a') {
        const row = ROWS[S.sel];
        if (row.id === 'reset') { S.confirmReset = true; S.confirmEl.style.opacity = '1'; sfx(ctx, 'uiBack'); }
        else if (row.id === 'back') back(ctx);
        else if (row.type === 'bool') { profile.setOption('reduceMotion', !profile.options.reduceMotion); refreshRow(row.id); sfx(ctx, 'ui'); }
      } else if (e.action === 'b' || e.action === 'pause') back(ctx);
    }
  },

  dispose() {
    if (!S) return;
    S.back.dispose();
    S.root.remove();
    S = null;
  },
};

function adjust(ctx, dir) {
  const row = ROWS[S.sel];
  if (row.type === 'pct') {
    const v = Math.min(1, Math.max(0, (profile.options[row.id] || 0) + dir * 0.05));
    profile.setOption(row.id, v);
    ctx.audio?.setVolume?.(row.id, v);
  } else if (row.type === 'ms') {
    const v = Math.min(OFFSET_MAX, Math.max(OFFSET_MIN, (profile.options.offsetMs || 0) + dir * OFFSET_STEP));
    profile.setOption('offsetMs', v);
  } else if (row.type === 'bool') {
    profile.setOption('reduceMotion', !profile.options.reduceMotion);
  } else {
    return;
  }
  refreshRow(row.id);
  sfx(ctx, 'ui');
}

function refreshRow(id) {
  const i = ROWS.findIndex((r) => r.id === id);
  if (i < 0) return;
  const row = ROWS[i];
  const cell = S.rows[i].value;
  if (row.type === 'pct') cell.textContent = Math.round((profile.options[row.id] || 0) * 100) + '%';
  else if (row.type === 'ms') cell.textContent = (profile.options.offsetMs > 0 ? '+' : '') + profile.options.offsetMs + 'ms';
  else if (row.type === 'bool') cell.textContent = profile.options.reduceMotion ? 'ON' : 'OFF';
  else cell.textContent = row.id === 'reset' ? '' : '›';
}

function refreshAll() { ROWS.forEach((r) => refreshRow(r.id)); }

function applySelection() {
  for (let i = 0; i < S.rows.length; i++) S.rows[i].el.classList.toggle('sh-panel--sel', i === S.sel);
  const row = ROWS[S.sel];
  S.hint.innerHTML = row.type === 'action'
    ? '<span><b class="sh-key">↑↓</b>choose</span><span><b class="sh-key">SPACE</b>select</span>'
    : '<span><b class="sh-key">↑↓</b>choose</span><span><b class="sh-key">←→</b>adjust</span><span><b class="sh-key">X</b>back</span>';
}

function back(ctx) {
  sfx(ctx, 'uiBack');
  S.exit = { t: 0, fired: false, color: PAL.violet, go: () => goView(ctx, 'title', {}) };
}

let cssDone = false;
function injectCss() {
  if (cssDone || document.getElementById('sh-opt-css')) { cssDone = true; return; }
  cssDone = true;
  const s = document.createElement('style');
  s.id = 'sh-opt-css';
  s.textContent = `
  .sh-opt__head{position:absolute;left:50%;top:8%;transform:translateX(-50%);text-align:center;}
  .sh-opt__title{font-size:clamp(22px,3.6vw,44px);}
  .sh-opt__list{position:absolute;left:50%;top:24%;transform:translateX(-50%);
    width:min(46vw,520px);display:flex;flex-direction:column;gap:clamp(8px,1.1vw,14px);}
  .sh-opt__row{display:flex;align-items:center;justify-content:space-between;
    padding:.7em 1em;will-change:transform;}
  .sh-opt__label{font-weight:900;font-size:clamp(12px,1.6vw,19px);}
  .sh-opt__value{font-weight:900;font-size:clamp(12px,1.6vw,19px);color:${PAL.dim};min-width:3em;text-align:right;}
  .sh-opt__confirm{position:absolute;left:50%;bottom:14%;transform:translateX(-50%);
    font-weight:900;font-size:clamp(11px,1.6vw,18px);color:${PAL.coral};text-align:center;
    text-shadow:0 2px 0 rgba(0,0,0,.6);transition:opacity .15s;}
  `;
  document.head.appendChild(s);
}
