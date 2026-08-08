/**
 * Pause overlay.  [shell agent owns this file]
 *
 * Owned by `play.js`, not a scene of its own: pausing must not tear down the
 * minigame. It dims and blurs the live frame, freezes the transport, and gives
 * back three unambiguous choices.
 *
 * The resume is the part that matters. We restart the transport at the exact
 * beat we froze on, three beats in the future, and count them in — so the
 * player gets their bearings back and the chart lines up to the sample rather
 * than to "about a second later".
 */

import { clamp01, backOut } from '../core/util.js';
import { PAL, el, panel, sfx, beatPulse } from './theme.js';

const ITEMS = [
  { id: 'resume', label: 'RESUME', sub: 'back to it', color: PAL.green },
  { id: 'restart', label: 'RESTART', sub: 'from the top', color: PAL.cyan },
  { id: 'quit', label: 'QUIT', sub: 'leave the round', color: PAL.coral },
];

export function createPause(root, { onSelect, title = 'PAUSED' } = {}) {
  const wrap = el('div', 'sh-pause');
  wrap.style.opacity = '0';
  wrap.style.display = 'none';

  const veil = el('div', 'sh-veil');
  wrap.appendChild(veil);

  const box = panel('sh-pause__box', PAL.cyan);
  const h = el('div', 'sh-display sh-pause__title', title);
  box.appendChild(h);
  const items = [];
  ITEMS.forEach((it) => {
    const d = el('div', 'sh-item sh-pause__item');
    d.style.setProperty('--accent', it.color);
    d.appendChild(el('span', 'sh-item__dot'));
    d.appendChild(el('span', '', it.label));
    d.appendChild(el('span', 'sh-item__sub', it.sub));
    box.appendChild(d);
    items.push(d);
  });
  wrap.appendChild(box);

  root.appendChild(wrap);

  // The countdown lives OUTSIDE the veil: it has to stay readable while the
  // dim is fading out, which is exactly when the player needs it.
  const count = el('div', 'sh-display sh-pause__count', '');
  count.style.opacity = '0';
  root.appendChild(count);
  injectCss();

  let sel = 0;
  let open = false;
  let a = 0;
  let countN = 0;

  function apply() {
    for (let i = 0; i < items.length; i++) items[i].classList.toggle('sh-item--sel', i === sel);
  }
  apply();

  return {
    el: wrap,
    get open() { return open; },
    get index() { return sel; },

    show() { open = true; sel = 0; apply(); wrap.style.display = 'flex'; },
    hide() { open = false; },

    /** Big beat-locked countdown while the transport spins back up. */
    setCount(n) {
      if (n === countN) return;
      countN = n;
      count.textContent = n > 0 ? String(n) : 'GO!';
      count.dataset.t = '0';
    },
    clearCount() { countN = 0; count.textContent = ''; count.style.opacity = '0'; },

    update(dt, beat) {
      const want = open ? 1 : 0;
      a += (want - a) * clamp01(dt * 14);
      if (a < 0.003 && !open) { wrap.style.display = 'none'; a = 0; }
      wrap.style.opacity = a.toFixed(3);
      const pulse = beatPulse(beat, 6);
      box.style.transform = `translateY(${((1 - a) * 40).toFixed(1)}px) scale(${(0.92 + a * 0.08 + pulse * 0.006).toFixed(3)})`;
      for (let i = 0; i < items.length; i++) {
        items[i].style.transform = i === sel ? `translateX(${(10 + pulse * 4).toFixed(1)}px) scale(1.06)` : '';
      }
      if (count.textContent) {
        const ct = Number(count.dataset.t || 0) + dt;
        count.dataset.t = String(ct);
        const k = clamp01(ct / 0.55);
        count.style.opacity = String(1 - k * k);
        count.style.transform = `translate(-50%,-50%) scale(${(backOut(clamp01(ct / 0.22)) * (1 + k * 0.7)).toFixed(3)})`;
      }
    },

    /** @returns {boolean} true if the event was consumed */
    input(ctx, e) {
      if (!open || !e.down) return false;
      if (e.action === 'up' || e.action === 'left') { sel = (sel + ITEMS.length - 1) % ITEMS.length; apply(); sfx(ctx, 'ui'); return true; }
      if (e.action === 'down' || e.action === 'right') { sel = (sel + 1) % ITEMS.length; apply(); sfx(ctx, 'ui'); return true; }
      if (e.action === 'a') { sfx(ctx, 'ui'); onSelect?.(ITEMS[sel].id); return true; }
      if (e.action === 'b' || e.action === 'pause') { sfx(ctx, 'uiBack'); onSelect?.('resume'); return true; }
      return true;
    },

    dispose() { wrap.remove(); count.remove(); },
  };
}

let cssDone = false;
function injectCss() {
  if (cssDone || document.getElementById('sh-pause-css')) { cssDone = true; return; }
  cssDone = true;
  const s = document.createElement('style');
  s.id = 'sh-pause-css';
  s.textContent = `
  .sh-pause{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;
    z-index:30;will-change:opacity;}
  .sh-pause__box{position:relative;padding:clamp(14px,2vw,28px) clamp(18px,2.6vw,38px);
    display:flex;flex-direction:column;gap:.5em;min-width:min(60vw,380px);will-change:transform;}
  .sh-pause__title{font-size:clamp(24px,4.2vw,52px);text-align:center;margin-bottom:.24em;
    letter-spacing:.04em;}
  .sh-pause__item{font-size:clamp(14px,1.9vw,24px);}
  .sh-pause__count{position:absolute;left:50%;top:44%;transform:translate(-50%,-50%);
    font-size:clamp(60px,15vw,190px);color:#fff;pointer-events:none;
    text-shadow:0 .06em 0 rgba(0,0,0,.6),0 0 .4em rgba(120,190,255,.6);}
  `;
  document.head.appendChild(s);
}
