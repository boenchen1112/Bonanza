// Writes one editable tempo map per hymn, from the MIDI. Run once; the maps are then edited by hand.
//
//   node Tools/hymns/make-tempo-maps.mjs [--hymns <dir>] [--only hymn_007,hymn_058] [--force]
//
// <dir> is the private hymns folder (default: notes/hymns); the maps go to <dir>/tempo/hymn_NNN.json.
// An existing map is NEVER overwritten (it may hold hand edits): it is listed as kept. --force
// overwrites the named maps (--only) or every map.
//
// The map format is documented in Assets/Game/Core/HymnTempoMap.cs and in the header of each file.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseMidi, makeSeconds } from './midi.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (k, d) => { const i = process.argv.indexOf('--' + k); return i < 0 ? d : process.argv[i + 1]; };
const dir = path.resolve(arg('hymns', path.join(here, '../../notes/hymns')));
const only = arg('only', null)?.split(',');
const force = process.argv.includes('--force');
const outDir = path.join(dir, 'tempo');
fs.mkdirSync(outDir, { recursive: true });

const fmt = (v) => String(Math.round(v * 1e6) / 1e6);
const clock = (s) => `${Math.floor(s / 60)}:${(s % 60).toFixed(1).padStart(4, '0')}`;
let wrote = 0, kept = 0;
for (const f of fs.readdirSync(dir).filter((f) => /^hymn_\d+\.mid$/.test(f)).sort()) {
  const hymn = f.replace(/\.mid$/, '');
  if (only && !only.includes(hymn)) continue;
  const target = path.join(outDir, hymn + '.json');
  if (fs.existsSync(target) && !force) { console.log(`kept   ${hymn} (exists; --force to regenerate)`); kept++; continue; }
  const m = parseMidi(fs.readFileSync(path.join(dir, f)));
  const sec = makeSeconds(m.tpq, m.tempos);
  const sig0 = m.sigs.find((s) => s.tick > 0 && s.n !== 1) || m.sigs[0];
  const pickup = m.sigs[0].n === 1 && m.sigs.length > 1 && m.sigs[1].tick === m.tpq * 4 * m.sigs[0].n / m.sigs[0].d;
  const main = pickup ? m.sigs[1] : m.sigs[0];
  const pattern = main.d === 8 && main.n >= 6 && main.n % 3 === 0 ? main.n / 3 : main.n;
  const unit = Math.round((m.tpq * 4 * main.n / main.d) / pattern);
  const L = [];
  L.push(`// ${hymn} tempo map. Generated from ${f} on ${new Date().toISOString().slice(0, 10)}; edit by hand, then run Tools/hymns/prepare-data.sh.`);
  L.push('// Regenerating never overwrites this file unless --force is given.');
  L.push('//');
  L.push('//   scale    global tempo multiplier for every excerpt (1.2 = 20% faster, 0.8 = 20% slower)');
  L.push('//   pattern  conducted beats per bar: 4 = down left right up, 3 = left right up, 2 = right up (0 = detect from the meter)');
  L.push('//   tempo    the tempo changes of the file. "bpm" is as written in the MIDI; "x" multiplies that segment until the next entry');
  L.push('//            (slow a fermata, steady a rushed phrase). Entries from tick ' + m.verseEnd + ' on are verse 2 and later: the game plays verse 1 only.');
  L.push('//   meter    the time signatures. A leading 1/4 bar is the pickup bar. Keep the repeated block that starts verse 2: it marks where verse 1 ends.');
  L.push('//   levels   an extra scale per excerpt level; a level that is not 1.0 is rendered as its own clip (' + hymn + '.easy and so on).');
  L.push(`// This hymn: ${main.n}/${main.d}${pickup ? ' with a pickup bar' : ''}, conducted in ${pattern} (one beat = ${unit} ticks, ${m.tpq} ticks per quarter).`);
  L.push('{');
  L.push(`  "hymn": "${hymn}",`);
  L.push('  "scale": 1.0,');
  L.push(`  "pattern": ${pattern},`);
  L.push('  "tempo": [');
  m.tempos.forEach((t, i) => {
    const bpm = 60000000 / t.us;
    const verse = t.tick >= m.verseEnd ? 'verse 2+, ignored' : 'verse 1';
    L.push(`    { "tick": ${t.tick}, "bpm": ${fmt(bpm)}, "x": 1.0 }${i < m.tempos.length - 1 ? ',' : ' '}   // ${clock(sec(t.tick))} ${verse}`);
  });
  L.push('  ],');
  L.push('  "meter": [');
  m.sigs.forEach((s, i) => {
    const verse = s.tick >= m.verseEnd ? 'verse 2+' : 'verse 1';
    L.push(`    { "tick": ${s.tick}, "sig": "${s.n}/${s.d}" }${i < m.sigs.length - 1 ? ',' : ' '}   // ${clock(sec(s.tick))} ${verse}`);
  });
  L.push('  ],');
  L.push('  "levels": { "easy": { "scale": 1.0 }, "medium": { "scale": 1.0 }, "hard": { "scale": 1.0 } }');
  L.push('}');
  fs.writeFileSync(target, L.join('\n') + '\n');
  console.log(`wrote  ${hymn}: ${m.tempos.length} tempo entries, ${m.sigs.length} meter entries, ${main.n}/${main.d}${pickup ? ' +pickup' : ''} in ${pattern}`);
  wrote++;
}
console.log(`${wrote} written, ${kept} kept, in ${outDir}`);
