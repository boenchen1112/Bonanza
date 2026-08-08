/**
 * UI layer: HUD, verdict popups, banners, countdown.  [ui agent owns this dir]
 *
 * DOM rather than in-scene text: crisp at every DPI, free layout, and it
 * cannot cost us a draw call on the beat. The only rule is that nothing here
 * may read the clock on its own — the game tells the UI what happened, the UI
 * never guesses.
 */

import { clamp01, backOut, easeOutCubic } from '../core/util.js';
import { FEEL } from '../core/feel.js';

export function createUI({ root, bus, clock }) {
  root.innerHTML = '';
  const layer = document.createElement('div');
  layer.style.cssText = 'position:absolute;inset:0;pointer-events:none;';
  root.appendChild(layer);

  injectStyles();

  /** @type {{el:HTMLElement,life:number,maxLife:number,kind:string,x:number,y:number}[]} */
  const live = [];

  function el(cls, text = '') {
    const d = document.createElement('div');
    d.className = cls;
    if (text) d.textContent = text;
    return d;
  }

  /** Verdict popup at a screen position (0..1 normalised). */
  function popup(text, { x = 0.5, y = 0.42, color = '#fff', scale = 1, life = FEEL.popupLife, kind = 'verdict' } = {}) {
    const d = el('bbb-pop bbb-pop--' + kind, text);
    d.style.color = typeof color === 'number' ? '#' + color.toString(16).padStart(6, '0') : color;
    d.style.left = x * 100 + '%';
    d.style.top = y * 100 + '%';
    d.style.setProperty('--s', String(scale));
    layer.appendChild(d);
    live.push({ el: d, life: 0, maxLife: life, kind, x, y });
    return d;
  }

  /** Big centred banner (round title, "GO!", "FINISH!"). */
  function banner(text, { sub = '', life = 1.6, color = '#fff' } = {}) {
    const d = el('bbb-banner');
    const m = el('bbb-banner__main', text);
    m.style.color = color;
    d.appendChild(m);
    if (sub) d.appendChild(el('bbb-banner__sub', sub));
    layer.appendChild(d);
    live.push({ el: d, life: 0, maxLife: life, kind: 'banner', x: 0.5, y: 0.5 });
    return d;
  }

  function clear() {
    for (const p of live) p.el.remove();
    live.length = 0;
    layer.innerHTML = '';
  }

  function update(dt) {
    for (let i = live.length - 1; i >= 0; i--) {
      const p = live[i];
      p.life += dt;
      const t = clamp01(p.life / p.maxLife);
      if (t >= 1) { p.el.remove(); live.splice(i, 1); continue; }
      if (p.kind === 'verdict') {
        // pop in with overshoot, drift up, fade out only at the very end
        const s = backOut(clamp01(t / 0.22)) * (1 + 0.06 * easeOutCubic(t));
        const rise = easeOutCubic(t) * 46;
        p.el.style.transform = `translate(-50%,-50%) translateY(${-rise}px) scale(${s * (p.el.style.getPropertyValue('--s') || 1)})`;
        p.el.style.opacity = String(t > 0.72 ? 1 - (t - 0.72) / 0.28 : 1);
      } else if (p.kind === 'banner') {
        const inT = clamp01(t / 0.18);
        const outT = clamp01((t - 0.78) / 0.22);
        const s = backOut(inT) * (1 - outT * 0.2);
        p.el.style.transform = `translate(-50%,-50%) scale(${s})`;
        p.el.style.opacity = String(1 - outT);
      }
    }
  }

  // --- HUD ------------------------------------------------------------------
  const hud = {
    _root: null,
    mount() {
      if (this._root) return this._root;
      const d = el('bbb-hud');
      d.innerHTML = `
        <div class="bbb-hud__left">
          <div class="bbb-hud__score" data-score>0</div>
          <div class="bbb-hud__combo" data-combo></div>
        </div>
        <div class="bbb-hud__right"><div class="bbb-hud__acc" data-acc></div></div>`;
      layer.appendChild(d);
      this._root = d;
      return d;
    },
    setScore(v) {
      const n = this._root?.querySelector('[data-score]');
      if (n) n.textContent = String(Math.round(v));
    },
    setCombo(v) {
      const n = this._root?.querySelector('[data-combo]');
      if (!n) return;
      n.textContent = v >= 2 ? `${v} COMBO` : '';
      if (v >= 2) { n.classList.remove('pulse'); void n.offsetWidth; n.classList.add('pulse'); }
    },
    setAccuracy(a) {
      const n = this._root?.querySelector('[data-acc]');
      if (n) n.textContent = (a * 100).toFixed(1) + '%';
    },
    unmount() { this._root?.remove(); this._root = null; },
  };

  return { layer, popup, banner, clear, update, hud, el };
}

function injectStyles() {
  if (document.getElementById('bbb-ui-style')) return;
  const s = document.createElement('style');
  s.id = 'bbb-ui-style';
  s.textContent = `
  .bbb-pop{position:absolute;transform:translate(-50%,-50%);font-weight:900;
    font-size:clamp(28px,5.2vw,64px);letter-spacing:-.02em;white-space:nowrap;
    text-shadow:0 4px 0 rgba(0,0,0,.45),0 0 24px currentColor;
    font-family:system-ui,-apple-system,"Segoe UI",sans-serif;will-change:transform,opacity;}
  .bbb-banner{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);
    text-align:center;font-family:system-ui,sans-serif;will-change:transform,opacity;}
  .bbb-banner__main{font-weight:900;font-size:clamp(40px,9vw,120px);color:#fff;
    letter-spacing:-.03em;text-shadow:0 6px 0 rgba(0,0,0,.5),0 0 40px rgba(255,255,255,.35);}
  .bbb-banner__sub{font-weight:700;font-size:clamp(14px,2.2vw,26px);color:#cfd3ff;margin-top:.3em;
    text-shadow:0 2px 0 rgba(0,0,0,.5);}
  .bbb-hud{position:absolute;inset:0;padding:clamp(12px,2.4vw,32px);display:flex;
    justify-content:space-between;align-items:flex-start;font-family:system-ui,sans-serif;}
  .bbb-hud__score{font-weight:900;font-size:clamp(24px,4vw,52px);color:#fff;
    text-shadow:0 3px 0 rgba(0,0,0,.5);font-variant-numeric:tabular-nums;}
  .bbb-hud__combo{font-weight:800;font-size:clamp(14px,2.2vw,26px);color:#ffd93d;
    text-shadow:0 2px 0 rgba(0,0,0,.5);min-height:1.2em;}
  .bbb-hud__combo.pulse{animation:bbbPulse .28s cubic-bezier(.2,1.6,.4,1);}
  .bbb-hud__acc{font-weight:800;font-size:clamp(14px,2.2vw,26px);color:#9fb2ff;
    text-shadow:0 2px 0 rgba(0,0,0,.5);font-variant-numeric:tabular-nums;}
  @keyframes bbbPulse{0%{transform:scale(1.35)}100%{transform:scale(1)}}
  @media (prefers-reduced-motion: reduce){.bbb-hud__combo.pulse{animation:none}}
  `;
  document.head.appendChild(s);
}
