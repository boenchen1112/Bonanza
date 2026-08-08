/**
 * Shell-side minigame catalogue + live previews.  [shell agent owns this file]
 *
 * The registry knows ids; the shell needs art direction. This table is the
 * shell's own view of the series — colour, blurb, one-line hook, control hint —
 * plus a procedural animated preview per game so the select screen shows
 * MOTION rather than a name. A card with a still image is a list; a card with
 * a moving preview is a menu you want to scroll.
 *
 * Previews are ~40 lines of canvas each, drawn only for the focused card.
 */

import { GAMES } from './registry.js';
import { PAL, num } from './theme.js';

const META = {
  'swing-kings': {
    color: num(PAL.yellow), blurb: 'One swing. One beat. Send it.',
    hook: 'Time the downbeat, launch it into orbit.', controls: 'A',
  },
  'drumline-dash': {
    color: num(PAL.cyan), blurb: 'Four lanes. Two sticks. No mercy.',
    hook: 'Hit every lane as the line sweeps past.', controls: '← → A',
  },
  'bounce-brigade': {
    color: num(PAL.green), blurb: 'Bounce in time or bounce out.',
    hook: 'Keep the squad airborne on every beat.', controls: 'A',
  },
  'chomp-chorus': {
    color: num(PAL.coral), blurb: 'Feed the beat before it eats you.',
    hook: 'Chomp on the note, never between them.', controls: 'A',
  },
  'finale-fever': {
    color: num(PAL.violet), blurb: 'Every last bar, twice as fast.',
    hook: 'The tempo climbs. So does the crowd.', controls: 'A ← →',
  },
};

const FALLBACK = { color: num(PAL.cyan), blurb: 'A minigame.', hook: 'Play it.', controls: 'A' };

/** @returns {{id:string,name:string,color:number,blurb:string,hook:string,controls:string}[]} */
export const CATALOG = GAMES.map((g) => ({
  id: g.id,
  name: g.name || g.id,
  ...(META[g.id] || FALLBACK),
}));

export const gameById = (id) => CATALOG.find((g) => g.id === id) || CATALOG[0];

// ------------------------------------------------------------------ preview

/**
 * Draw one frame of a game's preview.
 * @param {string} id
 * @param {CanvasRenderingContext2D} g
 * @param {number} w @param {number} h
 * @param {number} beat float beat  @param {number} t seconds
 */
export function drawPreview(id, g, w, h, beat, t) {
  const meta = gameById(id);
  const col = '#' + meta.color.toString(16).padStart(6, '0');
  const bf = beat - Math.floor(beat);
  const pulse = Math.exp(-bf * 5);

  g.clearRect(0, 0, w, h);
  const grd = g.createLinearGradient(0, 0, 0, h);
  grd.addColorStop(0, '#161235');
  grd.addColorStop(1, '#0a0820');
  g.fillStyle = grd;
  g.fillRect(0, 0, w, h);

  // floor line + beat glow, shared by every preview so they read as a series
  g.fillStyle = rgba(col, 0.10 + pulse * 0.18);
  g.fillRect(0, h * 0.72, w, h * 0.28);
  g.strokeStyle = rgba(col, 0.55 + pulse * 0.45);
  g.lineWidth = 2;
  g.beginPath(); g.moveTo(0, h * 0.72); g.lineTo(w, h * 0.72); g.stroke();

  switch (id) {
    case 'swing-kings': previewSwing(g, w, h, beat, t, col, pulse); break;
    case 'drumline-dash': previewDrum(g, w, h, beat, t, col, pulse); break;
    case 'bounce-brigade': previewBounce(g, w, h, beat, t, col, pulse); break;
    case 'chomp-chorus': previewChomp(g, w, h, beat, t, col, pulse); break;
    case 'finale-fever': previewFinale(g, w, h, beat, t, col, pulse); break;
    default: previewBounce(g, w, h, beat, t, col, pulse);
  }

  // vignette
  const vg = g.createRadialGradient(w / 2, h / 2, h * 0.25, w / 2, h / 2, h);
  vg.addColorStop(0, 'rgba(0,0,0,0)');
  vg.addColorStop(1, 'rgba(0,0,0,.55)');
  g.fillStyle = vg;
  g.fillRect(0, 0, w, h);
}

function previewSwing(g, w, h, beat, t, col, pulse) {
  const ground = h * 0.72;
  const bx = w * 0.34;
  // batter
  g.fillStyle = '#2b2560';
  g.beginPath(); g.ellipse(bx, ground - h * 0.14, w * 0.05, h * 0.15, 0, 0, Math.PI * 2); g.fill();
  g.fillStyle = col;
  g.beginPath(); g.arc(bx, ground - h * 0.34, h * 0.10, 0, Math.PI * 2); g.fill();
  // bat sweeps through the downbeat
  const sw = Math.min(1, Math.max(0, (beat - Math.floor(beat)) * 3));
  const ang = -2.1 + sw * 2.6;
  g.save();
  g.translate(bx, ground - h * 0.26);
  g.rotate(ang);
  g.fillStyle = '#f6e6c8';
  g.fillRect(0, -h * 0.022, w * 0.20, h * 0.044);
  g.restore();
  // ball flies out after the hit
  const bt = (beat % 1);
  const fly = Math.max(0, bt - 0.33) / 0.67;
  const px = bx + fly * w * 0.62;
  const py = ground - h * 0.26 - Math.sin(fly * Math.PI) * h * 0.5;
  g.fillStyle = '#fff';
  g.beginPath(); g.arc(px, py, h * 0.045, 0, Math.PI * 2); g.fill();
  if (bt < 0.2) {
    g.strokeStyle = rgba(col, 1 - bt * 5);
    g.lineWidth = 4;
    g.beginPath(); g.arc(bx + w * 0.14, ground - h * 0.28, h * 0.1 + bt * h * 0.9, 0, Math.PI * 2); g.stroke();
  }
}

function previewDrum(g, w, h, beat, t, col, pulse) {
  const lanes = 4;
  const hitY = h * 0.72;
  for (let i = 0; i < lanes; i++) {
    const x = w * (0.16 + i * 0.225);
    g.fillStyle = 'rgba(255,255,255,.05)';
    g.fillRect(x - w * 0.055, 0, w * 0.11, hitY);
    // notes fall on a 2-beat loop, offset per lane
    for (let k = 0; k < 2; k++) {
      const p = ((beat * 0.5 + i * 0.27 + k * 0.5) % 1);
      const y = p * hitY;
      const near = 1 - Math.abs(y - hitY) / (h * 0.2);
      g.fillStyle = near > 0 ? '#fff' : col;
      g.beginPath();
      g.roundRect ? g.roundRect(x - w * 0.045, y - h * 0.03, w * 0.09, h * 0.06, 4)
        : g.rect(x - w * 0.045, y - h * 0.03, w * 0.09, h * 0.06);
      g.fill();
    }
    g.fillStyle = rgba(col, 0.35 + pulse * 0.6);
    g.fillRect(x - w * 0.06, hitY - 3, w * 0.12, 6);
  }
}

function previewBounce(g, w, h, beat, t, col, pulse) {
  const ground = h * 0.72;
  for (let i = 0; i < 5; i++) {
    const x = w * (0.14 + i * 0.18);
    const ph = beat * Math.PI - i * 0.5;
    const b = Math.abs(Math.sin(ph));
    const y = ground - b * h * 0.44 - h * 0.06;
    const squash = 1 - (1 - b) * 0.35;
    g.fillStyle = i % 2 ? col : '#fff';
    g.save();
    g.translate(x, y);
    g.scale(1 / squash, squash);
    g.beginPath(); g.arc(0, 0, h * 0.075, 0, Math.PI * 2); g.fill();
    g.restore();
    g.fillStyle = 'rgba(0,0,0,.35)';
    g.beginPath(); g.ellipse(x, ground + 3, h * 0.07 * (0.6 + (1 - b) * 0.5), h * 0.016, 0, 0, Math.PI * 2); g.fill();
  }
}

function previewChomp(g, w, h, beat, t, col, pulse) {
  const cx = w * 0.30, cy = h * 0.44, r = h * 0.24;
  const open = 0.05 + 0.42 * Math.abs(Math.sin(beat * Math.PI));
  g.fillStyle = col;
  g.beginPath();
  g.moveTo(cx, cy);
  g.arc(cx, cy, r, open, Math.PI * 2 - open);
  g.closePath();
  g.fill();
  g.fillStyle = '#151230';
  g.beginPath(); g.arc(cx + r * 0.15, cy - r * 0.42, r * 0.12, 0, Math.PI * 2); g.fill();
  // notes queued to be eaten
  for (let i = 0; i < 4; i++) {
    const p = ((beat * 0.5 + i * 0.25) % 1);
    const x = w * (1.05 - p * 0.85);
    if (x < cx + r * 0.4) continue;
    g.fillStyle = i % 2 ? '#fff' : rgba(col, 0.85);
    g.beginPath(); g.arc(x, cy, h * 0.05, 0, Math.PI * 2); g.fill();
  }
}

function previewFinale(g, w, h, beat, t, col, pulse) {
  const n = 14;
  for (let i = 0; i < n; i++) {
    const x = w * (0.06 + i * 0.066);
    const v = 0.25 + 0.75 * Math.abs(Math.sin(beat * Math.PI * 0.5 + i * 0.7));
    const bh = v * h * 0.55;
    g.fillStyle = i % 3 === 0 ? '#fff' : rgba(col, 0.9);
    g.fillRect(x, h * 0.72 - bh, w * 0.045, bh);
  }
  const bf = beat % 2;
  if (bf < 0.5) {
    const a = 1 - bf * 2;
    for (let i = 0; i < 10; i++) {
      const ang = (i / 10) * Math.PI * 2;
      const rr = (1 - a) * h * 0.42;
      g.fillStyle = rgba(i % 2 ? '#ffffff' : col, a);
      g.beginPath();
      g.arc(w * 0.72 + Math.cos(ang) * rr, h * 0.34 + Math.sin(ang) * rr, 3, 0, Math.PI * 2);
      g.fill();
    }
  }
}

function rgba(hexStr, a) {
  const n = parseInt(hexStr.replace('#', ''), 16);
  return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${a})`;
}
