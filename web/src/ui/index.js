/**
 * UI layer: HUD, verdict popups, banners.  [ui agent owns this dir]
 *
 * DOM rather than in-scene text: crisp at every DPI, free layout, and it
 * cannot cost us a draw call on the beat. The only rule is that nothing here
 * may read the clock on its own — the game tells the UI what happened, the UI
 * never guesses.
 *
 * Text is rendered with the BASH procedural typeface (`font.js`) rather than
 * a system font, rasterised once per distinct string+style and cached there —
 * see `font.js` for why. Small system-ui labels (SCORE, COMBO, ACCURACY) are
 * the one place a system face is allowed; `styles.js` documents that split.
 */

import { clamp01, backOut, easeOutCubic } from '../core/util.js';
import { FEEL } from '../core/feel.js';
import { injectStyles } from './styles.js';
import { style as fontStyle, textImage } from './font.js';

const toColor = (c) => (typeof c === 'number' ? '#' + (c >>> 0).toString(16).padStart(6, '0') : c);

export function createUI({ root, bus, clock }) {
  root.innerHTML = '';
  const layer = document.createElement('div');
  layer.className = 'bbb-layer';
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

  /** A `.bbb-t` node showing `text` rasterised in the BASH face. Sizes off the
   *  `--cap` custom property inherited from its parent — callers size via CSS. */
  function glyph(text, { styleName = 'display', color = '#fff', extraClass = '' } = {}) {
    const img = textImage(String(text), fontStyle(styleName, { color: toColor(color) }));
    const d = document.createElement('div');
    d.className = extraClass ? `bbb-t ${extraClass}` : 'bbb-t';
    d.style.backgroundImage = `url(${img.url})`;
    d.style.setProperty('--ratio', String(img.ratio));
    d.style.setProperty('--ar', String(img.aspect));
    return d;
  }

  /** Replace a mounted glyph node's image in place, skipping work if the text
   *  didn't actually change — these are called from hot per-frame HUD setters. */
  function repaint(holder, text, opts) {
    const s = String(text);
    if (holder.dataset.text === s) return;
    holder.dataset.text = s;
    const img = textImage(s, fontStyle(opts.styleName || 'hud', { color: toColor(opts.color || '#fff') }));
    holder.style.backgroundImage = `url(${img.url})`;
    holder.style.setProperty('--ratio', String(img.ratio));
    holder.style.setProperty('--ar', String(img.aspect));
  }

  /** Verdict popup at a screen position (0..1 normalised). */
  function popup(text, { x = 0.5, y = 0.42, color = '#fff', scale = 1, life = FEEL.popupLife, kind = 'verdict' } = {}) {
    const d = el('bbb-pop bbb-pop--' + kind);
    d.style.left = x * 100 + '%';
    d.style.top = y * 100 + '%';
    d.style.color = toColor(color);
    d.style.setProperty('--s', String(scale));
    d.appendChild(glyph(text, { styleName: 'display', color }));
    layer.appendChild(d);
    live.push({ el: d, life: 0, maxLife: life, kind, x, y });
    return d;
  }

  /** Big centred banner (round title, "GO!", "FINISH!"). */
  function banner(text, { sub = '', life = 1.6, color = '#fff' } = {}) {
    const d = el('bbb-banner');
    d.appendChild(glyph(text, { styleName: 'title', color, extraClass: 'bbb-banner__main' }));
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

  // Lifetimes age on REAL elapsed time, not the frame dt main.js hands us:
  // that dt is clamped (and zeroed in hitstop), so a 900ms load hitch used to
  // leave a title banner up for seconds over live play while the audio-timed
  // count-in and pitches marched on underneath it.
  let lastMs = null;
  function update() {
    const nowMs = performance.now();
    const dt = lastMs === null ? 0 : Math.min(2, (nowMs - lastMs) / 1000);
    lastMs = nowMs;
    for (let i = live.length - 1; i >= 0; i--) {
      const p = live[i];
      p.life += dt;
      const t = clamp01(p.life / p.maxLife);
      if (p.kind === 'pulse') {
        if (t >= 1) { live.splice(i, 1); continue; }
        const s = 1.35 - 0.35 * backOut(t);
        p.el.style.transform = `scale(${s})`;
        continue;
      }
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
    _lastCombo: 0,
    mount() {
      if (this._root) return this._root;
      const d = el('bbb-hud');
      d.innerHTML = `
        <div class="bbb-hud__score">
          <div class="bbb-lab">SCORE</div>
          <div class="bbb-t bbb-odo" data-score></div>
          <div class="bbb-combo-wrap"><div class="bbb-combo" data-combo-wrap>
            <div class="bbb-t bbb-combo__n" data-combo></div>
            <div class="bbb-combo__lab">COMBO</div>
          </div></div>
        </div>
        <div class="bbb-hud__acc">
          <div class="bbb-lab">ACCURACY</div>
          <div class="bbb-acc__row"><div class="bbb-t bbb-acc__n" data-acc></div></div>
        </div>`;
      layer.appendChild(d);
      this._root = d;
      this._lastCombo = 0;
      repaint(d.querySelector('[data-score]'), '0', { styleName: 'hud' });
      repaint(d.querySelector('[data-acc]'), '0.0%', { styleName: 'hud', color: '#9fb2ff' });
      const comboWrap = d.querySelector('[data-combo-wrap]');
      comboWrap.style.opacity = '0';
      return d;
    },
    setScore(v) {
      const n = this._root?.querySelector('[data-score]');
      if (n) repaint(n, String(Math.round(v)), { styleName: 'hud' });
    },
    setCombo(v) {
      const wrap = this._root?.querySelector('[data-combo-wrap]');
      const n = this._root?.querySelector('[data-combo]');
      if (!wrap || !n) return;
      v = Math.round(v);
      if (v >= 2) {
        wrap.style.opacity = '1';
        repaint(n, `${v} `, { styleName: 'hud', color: '#ffd93d' });
        if (v > this._lastCombo) {
          live.push({ el: wrap, life: 0, maxLife: 0.28, kind: 'pulse' });
        }
      } else {
        wrap.style.opacity = '0';
      }
      this._lastCombo = v;
    },
    setAccuracy(a) {
      const n = this._root?.querySelector('[data-acc]');
      if (n) repaint(n, (a * 100).toFixed(1) + '%', { styleName: 'hud', color: '#9fb2ff' });
    },
    unmount() { this._root?.remove(); this._root = null; },
  };

  /**
   * The count-in number, centred and large. This is the presenter half of
   * the count-in only — `core/round.js` `countIn()` owns the driving (beat
   * subscription, tick SFX, which number this beat is), because that is what
   * five games were each re-implementing.
   */
  function countdown(text, opts = {}) {
    return banner(String(text), { life: 0.5, color: '#ffe9a8', ...opts });
  }

  return { layer, popup, banner, countdown, clear, update, hud, el };
}
