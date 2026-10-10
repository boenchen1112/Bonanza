#!/usr/bin/env node
/**
 * Screenshot the character showcase at exact beat phases.
 *
 * Usage:  node web/tests/shoot-chars.mjs [port] [outDir]
 *
 * Drives the standalone `chars-demo.html` host through its deterministic
 * stepper (`__CHARS__.toBeat`), so every shot lands on a named script step at
 * a known beat phase — and the same beat produces the same frame every run.
 */

import { chromium } from 'playwright';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';

const PORT = Number(process.argv[2] || 5999);
const OUT = path.resolve(process.argv[3] || 'runs/p4');
const URL = `http://127.0.0.1:${PORT}/chars-demo.html`;
const EXE = '/opt/pw-browsers/chromium-1194/chrome-linux/chrome';

/**
 * The showcase runs one script step per bar (4 beats) at 118bpm:
 *   0 idle · 1 ready · 2 swing · 3-6 verdicts · 7 taunt · 8 celebrate · 9 fail
 * Sample points are chosen to catch the *extremes* of each pose, not the
 * moment it settles: a windup at its deepest coil, a strike mid-follow-through.
 */
const SHOTS = [
  ['01-idle-downbeat', 0.02],
  ['02-idle-apex', 1.5],
  ['03-ready', 5.5],
  ['04-windup-coil', 8.9],
  ['05-strike-contact', 9.08],
  ['06-strike-follow', 9.3],
  ['07-verdicts-a', 12.35],
  ['08-verdicts-b', 16.35],
  ['09-verdicts-c', 20.35],
  ['10-verdicts-d', 24.35],
  ['11-taunt', 29.6],
  ['12-celebrate', 33.4],
  ['13-fail-fold', 36.3],
  ['14-fail-recover', 38.2],
];

/** Silhouette pass — the acceptance test for pose readability. */
const SIL_SHOTS = [
  ['S1-verdicts', 12.35],
  ['S2-celebrate', 33.4],
  ['S3-fail', 36.3],
];

async function main() {
  await mkdir(OUT, { recursive: true });
  const browser = await chromium.launch({
    executablePath: EXE,
    args: ['--use-gl=swiftshader', '--enable-unsafe-swiftshader', '--mute-audio',
      '--disable-lcd-text', '--hide-scrollbars'],
  });
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 }, deviceScaleFactor: 1 });

  const errors = [];
  page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', (e) => errors.push(String(e)));

  await page.goto(URL, { waitUntil: 'load' });
  await page.waitForFunction('!!window.__CHARS__', null, { timeout: 20000 });
  await page.evaluate('window.__CHARS__.play(false)');

  const shots = [];
  for (const [name, beat] of SHOTS) {
    await page.evaluate((b) => window.__CHARS__.toBeat(b), beat);
    await page.evaluate('window.__CHARS__.screenshotReady()');
    await page.screenshot({ path: path.join(OUT, `${name}.png`) });
    shots.push({ name, beat });
  }

  await page.evaluate('window.__CHARS__.silhouette(true)');
  // The silhouette pass has to re-walk from the current beat, so it samples
  // the NEXT loop of the same steps (the script cycles every 40 beats).
  for (const [name, beat] of SIL_SHOTS) {
    await page.evaluate((b) => window.__CHARS__.toBeat(b + 40), beat);
    await page.evaluate('window.__CHARS__.screenshotReady()');
    await page.screenshot({ path: path.join(OUT, `${name}.png`) });
    shots.push({ name, beat: beat + 40, silhouette: true });
  }
  await page.evaluate('window.__CHARS__.silhouette(false)');

  const tele = await page.evaluate('window.__CHARS__.telemetry()');
  const summary = { url: URL, shots, telemetry: tele, consoleErrors: errors.length, errors };
  await writeFile(path.join(OUT, 'summary.json'), JSON.stringify(summary, null, 2));
  console.log(JSON.stringify(summary, null, 2));

  await browser.close();
  if (errors.length) process.exitCode = 1;
}

main().catch((e) => { console.error(e); process.exit(1); });
