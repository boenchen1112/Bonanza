#!/usr/bin/env node
/**
 * Tile a run's screenshots into one image, so a motion sequence can be read
 * at a glance (and reviewed as one file instead of twenty).
 *
 *   node tools/harness/contact-sheet.mjs runs/sk-01 [--cols 5] [--crop x,y,w,h]
 *        [--from 0] [--to 99] [--out runs/sk-01/sheet.png] [--tile 320]
 *
 * --crop takes the region of each 1280x720 shot to keep (in shot pixels), so
 * a batter can be inspected closer than the full frame allows. Rendered by
 * Playwright's own Chromium (no image library needed).
 */

import { readdirSync, readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import path from 'node:path';

const a = process.argv.slice(2);
const dir = path.resolve(a[0]);
const opt = (k, d) => (a.includes(`--${k}`) ? a[a.indexOf(`--${k}`) + 1] : d);
const cols = Number(opt('cols', 5));
const tile = Number(opt('tile', 320));
const crop = opt('crop', '0,0,1280,720').split(',').map(Number);
const from = Number(opt('from', 0));
const to = Number(opt('to', 9999));
const out = path.resolve(opt('out', path.join(dir, 'sheet.png')));

const shots = readdirSync(dir).filter((f) => /^shot-\d+\.png$/.test(f)).sort()
  .filter((f) => { const i = Number(f.slice(5, 8)); return i >= from && i <= to; });
const [cx, cy, cw, ch] = crop;
const th = Math.round((tile * ch) / cw);
const scale = tile / cw;

const html = `<html><body style="margin:0;background:#111;display:grid;grid-template-columns:repeat(${cols},${tile}px);gap:2px">
${shots.map((f) => `<div style="position:relative;width:${tile}px;height:${th}px;overflow:hidden">
<img src="data:image/png;base64,${readFileSync(path.join(dir, f)).toString('base64')}" style="position:absolute;left:${-cx * scale}px;top:${-cy * scale}px;width:${1280 * scale}px">
<span style="position:absolute;left:3px;top:1px;font:11px monospace;color:#fff;text-shadow:0 0 3px #000">${f.slice(5, 8)}</span></div>`).join('')}
</body></html>`;

const req = createRequire(path.resolve(import.meta.dirname, '../../web/package.json'));
const pw = await import(pathToFileURL(req.resolve('playwright')).href);
const chromium = pw.chromium ?? pw.default.chromium;
const b = await chromium.launch({ headless: true });
const p = await b.newPage({ viewport: { width: cols * (tile + 2), height: 400 } });
await p.setContent(html, { waitUntil: 'load' });
await p.screenshot({ path: out, fullPage: true });
await b.close();
console.log(`sheet -> ${out} (${shots.length} shots)`);
