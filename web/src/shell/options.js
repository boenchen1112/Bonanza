/**
 * Options screen.  [shell agent owns this file]
 *
 * Everything here writes straight through `profile.setOption()` (localStorage,
 * survives a refresh) and, for the two audio rows, live-applies immediately
 * via `ctx.audio.setVolume()` so a player hears the change before confirming
 * anything.
 */
import * as THREE from 'three';
import { PAL, num, el, mountRoot, panel, sfx, createWipe, beatPulse, reducedMotion, ensureStyle, tickExit, makeExit, startShellTransport } from './theme.js';
import { createBackdrop } from './backdrop.js';
import { profile } from './state.js';
import { charById, charMesh, charBeat, disposeChar } from './chars.js';
import { goView } from './nav.js';

const ROWS = [
  { id: 'music', label: 'MUSIC VOLUME', type: 'pct' },
  { id: 'sfx', label: 'SFX VOLUME', type: 'pct' },
  { id: 'offsetMs', label: 'TIMING OFFSET', type: 'ms' },
  { id: 'reduceMotion', label: 'REDUCE MOTION', type: 'bool' },
  // Swing Kings' conducting gesture: an optional second input, tap stays default.
  { id: 'swingInput', label: 'SWING KINGS INPUT', type: 'choice', choices: ['tap', 'mouse', 'camera'],
    names: { tap: 'TAP', mouse: 'MOUSE CONDUCT', camera: 'CAMERA CONDUCT' } },
  { id: 'reset', label: 'RESET PROGRESS', type: 'action' },
  { id: 'back', label: 'BACK', type: 'action' },
];
// Wide enough for Bluetooth output, which commonly adds 150-300ms the browser
// doesn't report. Negative = your presses land late (judge.js adds this).
const OFFSET_MIN = -300, OFFSET_MAX = 300, OFFSET_STEP = 5;

let S = null;

export default {
  id: 'options', name: 'Options',

  load(ctx) {
    S = { t: 0, sel: 0, rows: [], confirmReset: false, reduce: reducedMotion(), cast: [] };

    S.back = createBackdrop(ctx, { accent: num(PAL.green), density: 0.6 });
    ctx.fx.attach(ctx.scene);
    ctx.scene.background = new THREE.Color(0x0b0a1a);
    // Two of the cast keep the beat either side of the list, on the house
    // floor (the level camera at y=0 cut the horizon mid-screen over an
    // empty floor).
    ctx.scene.userData.groundY = -1.6;
    ctx.camera.position.set(0, 1.5, 9);
    ctx.camera.lookAt(0, 1.1, 0);
    [['tuff', -4.7, 0.35], ['mimo', 4.7, -0.35]].forEach(([id, x, ry]) => {
      const m = charMesh(charById(id), {});
      m.position.set(x, -1.6, 0.4);
      m.rotation.y = ry;
      m.scale.setScalar(1.25);
      ctx.scene.add(m);
      S.cast.push(m);
    });

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

    const hint = el('div', 'sh-hint');
    S.hint = hint;
    root.appendChild(hint);

    S.wipe = createWipe(root, { color: PAL.green, mode: 'in' });
    refreshAll();
    applySelection();
  },

  start(ctx) {
    startShellTransport(ctx);
    S.cast.forEach((m, i) => m.userData.charApi?.play('dance', {
      beatLock: true, bpm: 124, beat0: -i * 5, face: 'groove', beat: 0.25, blend: 0.3,
    }));
  },

  update(ctx, dt, beat) {
    if (!S) return;
    S.t += dt;
    S.back.update(dt, beat, S.t);
    S.wipe.update(dt);
    for (const m of S.cast) charBeat(m, beat, dt);
    const pulse = beatPulse(beat, 6);
    for (let i = 0; i < S.rows.length; i++) {
      const on = i === S.sel;
      const sc = on ? 1.03 + pulse * 0.02 : 1;
      S.rows[i].el.style.transform = `scale(${sc.toFixed(3)})`;
    }
    tickExit(S, dt);
  },

  input(ctx, events) {
    if (!S || S.exit) return;
    for (const e of events) {
      if (!e.down) continue;

      if (S.confirmReset) {
        // Anything but SPACE cancels — including moving off the row.
        if (e.action === 'a') {
          profile.resetProgress();
          sfx(ctx, 'fanfare');
        } else {
          sfx(ctx, 'uiBack');
        }
        setConfirm(false);
        refreshAll();
        continue;
      }

      if (e.action === 'up') { S.sel = (S.sel + ROWS.length - 1) % ROWS.length; sfx(ctx, 'ui'); applySelection(); }
      else if (e.action === 'down') { S.sel = (S.sel + 1) % ROWS.length; sfx(ctx, 'ui'); applySelection(); }
      else if (e.action === 'left' || e.action === 'right') {
        adjust(ctx, e.action === 'right' ? 1 : -1);
      } else if (e.action === 'a') {
        const row = ROWS[S.sel];
        if (row.id === 'reset') { setConfirm(true); sfx(ctx, 'uiBack'); }
        else if (row.id === 'back') back(ctx);
        else if (row.type === 'bool') { profile.setOption('reduceMotion', !profile.options.reduceMotion); refreshRow(row.id); sfx(ctx, 'ui'); }
        else if (row.type === 'choice') adjust(ctx, 1);
      } else if (e.action === 'b' || e.action === 'pause') back(ctx);
    }
  },

  dispose() {
    if (!S) return;
    for (const m of S.cast) { m.parent?.remove(m); disposeChar(m); }
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
  } else if (row.type === 'choice') {
    const i = Math.max(0, row.choices.indexOf(profile.options[row.id]));
    profile.setOption(row.id, row.choices[(i + dir + row.choices.length) % row.choices.length]);
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
  else if (row.type === 'choice') cell.textContent = row.names[profile.options[row.id]] || row.names[row.choices[0]];
  else if (row.id === 'reset') cell.textContent = S.confirmReset ? 'scores · ranks · stats' : '';
  else cell.textContent = '›';
}

function refreshAll() { ROWS.forEach((r) => refreshRow(r.id)); }

/**
 * The reset question is asked IN the reset row (label + value swap), not in
 * a floating line — that one landed on top of the BACK row.
 */
function setConfirm(on) {
  S.confirmReset = on;
  const i = ROWS.findIndex((r) => r.id === 'reset');
  const r = S.rows[i];
  r.el.classList.toggle('sh-opt__row--warn', on);
  r.el.firstChild.textContent = on ? 'RESET ALL PROGRESS?' : ROWS[i].label;
  refreshRow('reset');
  applySelection();
}

function applySelection() {
  for (let i = 0; i < S.rows.length; i++) S.rows[i].el.classList.toggle('sh-panel--sel', i === S.sel);
  const row = ROWS[S.sel];
  S.hint.innerHTML = S.confirmReset
    ? '<span><b class="sh-key">SPACE</b>reset</span><span><b class="sh-key">X</b>cancel</span>'
    : row.type === 'action'
      ? '<span><b class="sh-key">↑↓</b>choose</span><span><b class="sh-key">SPACE</b>select</span><span><b class="sh-key">X</b>back</span>'
      : '<span><b class="sh-key">↑↓</b>choose</span><span><b class="sh-key">←→</b>adjust</span><span><b class="sh-key">X</b>back</span>';
}

function back(ctx) {
  sfx(ctx, 'uiBack');
  S.exit = makeExit(() => goView(ctx, 'title', {}));
}

function injectCss() {
  ensureStyle('sh-opt-css', `
  .sh-opt__head{position:absolute;left:50%;top:8%;transform:translateX(-50%);text-align:center;}
  .sh-opt__title{font-size:clamp(22px,3.6vw,44px);}
  .sh-opt__list{position:absolute;left:50%;top:24%;transform:translateX(-50%);
    width:min(46vw,520px);display:flex;flex-direction:column;gap:clamp(8px,1.1vw,14px);}
  .sh-opt__row{display:flex;align-items:center;justify-content:space-between;
    padding:.7em 1em;will-change:transform;}
  .sh-opt__label{font-weight:900;font-size:clamp(12px,1.6vw,19px);}
  .sh-opt__value{font-weight:900;font-size:clamp(12px,1.6vw,19px);color:${PAL.dim};min-width:3em;text-align:right;}
  .sh-opt__row--warn{border-color:${PAL.coral}!important;box-shadow:0 9px 0 rgba(0,0,0,.55),0 0 26px -2px ${PAL.coral}!important;}
  .sh-opt__row--warn .sh-opt__label{color:${PAL.coral};}
  .sh-opt__row--warn .sh-opt__value{color:#ffd0d6;font-size:clamp(10px,1.2vw,14px);}
  `);
}
