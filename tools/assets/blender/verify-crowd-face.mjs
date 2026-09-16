#!/usr/bin/env node
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { spawn } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import path from 'node:path';

const ROOT = path.resolve(import.meta.dirname, '../../..');
const WEB = path.join(ROOT, 'web');
const OUT = path.resolve(ROOT, 'runs/verify-crowd-face');
mkdirSync(OUT, { recursive: true });

const webRequire = createRequire(path.join(WEB, 'package.json'));
const pw = await import(pathToFileURL(webRequire.resolve('playwright')).href);
const chromium = pw.chromium ?? pw.default.chromium;
const PORT = 8600 + (process.pid % 300);
const server = spawn(process.execPath, [path.join(ROOT, 'tools/harness/serve.mjs'), WEB, String(PORT)], { stdio: ['ignore', 'pipe', 'pipe'] });
await new Promise((r) => { server.stdout.once('data', r); setTimeout(r, 1200); });

const browser = await chromium.launch({ channel: 'chromium', headless: true, args: process.platform === 'win32' ? ['--use-angle=d3d11'] : [] });
const page = await (await browser.newContext({ viewport: { width: 640, height: 480 } })).newPage();
page.on('pageerror', (e) => console.log('[pageerror]', e.message));
page.on('console', (m) => { if (m.type() === 'error') console.log('[console]', m.text()); });
await page.goto(`http://127.0.0.1:${PORT}/_verify.html`, { waitUntil: 'load' });
await page.waitForFunction('window.__ready', null, { timeout: 10000 }).catch(() => {});
await page.screenshot({ path: path.join(OUT, 'crowd-face-closeup.png') });
console.log('shot -> ' + OUT);
await browser.close();
server.kill();
