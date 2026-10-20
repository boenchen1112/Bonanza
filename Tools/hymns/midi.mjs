// A minimal Standard MIDI File reader for the hymn tools, the JS twin of Assets/Game/Core/HymnMidi.cs
// (format 0/1, running status, tempo FF 51, time signature FF 58, note on/off). Also the helpers the
// tempo-map generator and the audio renderer share.
import fs from 'node:fs';

export function parseMidi(buf) {
  const d = buf;
  if (d.length < 14 || d.toString('latin1', 0, 4) !== 'MThd') throw new Error('not a MIDI file');
  const headerLen = d.readUInt32BE(4), tracks = d.readUInt16BE(10), tpq = d.readUInt16BE(12);
  if (tpq & 0x8000) throw new Error('SMPTE timing is not supported');
  const tempos = [], sigs = [], notes = [], names = [];
  let pos = 8 + headerLen, endTick = 0, trackIndex = 0;
  while (trackIndex < tracks && pos + 8 <= d.length) {
    const len = d.readUInt32BE(pos + 4), body = pos + 8, end = body + len;
    if (d.toString('latin1', pos, pos + 4) === 'MTrk') {
      const last = parseTrack(d, body, end, trackIndex, notes, tempos, sigs, names);
      endTick = Math.max(endTick, last);
      trackIndex++;
    }
    pos = end;
  }
  const norm = (list, key) => {   // stable by tick, the last at a tick wins (like HymnMidi.Normalize)
    const out = [];
    list.map((v, i) => [v, i]).sort((a, b) => a[0].tick - b[0].tick || a[1] - b[1]).forEach(([v]) => {
      if (out.length && out[out.length - 1].tick === v.tick) out[out.length - 1] = v; else out.push(v);
    });
    return out;
  };
  let t = norm(tempos), s = norm(sigs);
  if (!t.length || t[0].tick > 0) t.unshift({ tick: 0, us: 500000 });
  if (!s.length || s[0].tick > 0) s.unshift({ tick: 0, n: 4, d: 4 });
  notes.sort((a, b) => a.start - b.start || a.track - b.track || a.pitch - b.pitch);
  let verseEnd = endTick;
  for (const x of s) if (x.tick > 0 && x.tick < endTick && x.n === s[0].n && x.d === s[0].d) { verseEnd = x.tick; break; }
  return { tpq, tempos: t, sigs: s, notes, names, endTick, verseEnd };
}

function vlq(d, p) {
  let v = 0;
  for (let i = 0; i < 4; i++) { const b = d[p.i++]; v = (v << 7) | (b & 0x7f); if (!(b & 0x80)) return v; }
  throw new Error('bad variable-length number');
}

function parseTrack(d, pos, end, track, notes, tempos, sigs, names) {
  const p = { i: pos };
  let tick = 0, running = 0;
  const open = new Map();
  while (p.i < end) {
    tick += vlq(d, p);
    let status = d[p.i];
    if (status >= 0x80) { p.i++; if (status < 0xf0) running = status; } else { if (!running) throw new Error('data byte without a status'); status = running; }
    if (status === 0xff) {
      const type = d[p.i++], len = vlq(d, p);
      if (type === 0x51 && len === 3) tempos.push({ tick, us: (d[p.i] << 16) | (d[p.i + 1] << 8) | d[p.i + 2] });
      else if (type === 0x58 && len >= 2) sigs.push({ tick, n: d[p.i], d: 1 << d[p.i + 1] });
      else if (type === 0x03) names[track] = d.toString('latin1', p.i, p.i + len);
      p.i += len; running = 0;
      if (type === 0x2f) break;
    } else if (status === 0xf0 || status === 0xf7) { const len = vlq(d, p); p.i += len; running = 0; }
    else if (status >= 0xf1) throw new Error('unsupported status byte ' + status);
    else {
      const kind = status & 0xf0, ch = status & 0x0f;
      const a = d[p.i++], b = (kind === 0xc0 || kind === 0xd0) ? 0 : d[p.i++];
      const on = kind === 0x90 && b > 0, off = kind === 0x80 || (kind === 0x90 && b === 0);
      const key = ch * 128 + a;
      if ((on || off) && open.has(key)) { open.get(key).end = tick; open.delete(key); }
      if (on) { const n = { track, channel: ch, pitch: a, velocity: b, start: tick, end: tick }; open.set(key, n); notes.push(n); }
    }
  }
  for (const n of open.values()) n.end = tick;
  return tick;
}

/** Seconds at each tick for a [{tick, us}] tempo list (the same arithmetic as HymnMidi.SecondsAt). */
export function makeSeconds(tpq, tempos) {
  const at = [0];
  for (let i = 1; i < tempos.length; i++) at[i] = at[i - 1] + (tempos[i].tick - tempos[i - 1].tick) * (tempos[i - 1].us / 1e6 / tpq);
  return (tick) => {
    let i = 0;
    for (let k = tempos.length - 1; k >= 0; k--) if (tempos[k].tick <= tick) { i = k; break; }
    return at[i] + (tick - tempos[i].tick) * (tempos[i].us / 1e6 / tpq);
  };
}

/** JSON with // and /* comments and trailing commas, as the tempo maps are written. */
export function parseJsonc(text) {
  let out = '', i = 0;
  while (i < text.length) {
    const c = text[i];
    if (c === '"') { let j = i + 1; while (j < text.length && text[j] !== '"') j += text[j] === '\\' ? 2 : 1; out += text.slice(i, j + 1); i = j + 1; }
    else if (c === '/' && text[i + 1] === '/') { while (i < text.length && text[i] !== '\n') i++; }
    else if (c === '/' && text[i + 1] === '*') { const e = text.indexOf('*/', i + 2); i = e < 0 ? text.length : e + 2; }
    else { out += c; i++; }
  }
  return JSON.parse(out.replace(/,\s*([}\]])/g, '$1'));
}

export function readExcerptRows(file) {
  const rows = [];
  for (const raw of fs.readFileSync(file, 'utf8').split(/\r?\n/)) {
    const s = raw.replace(/#.*/, '').trim();
    if (!s) continue;
    const c = s.split(/\s+/);
    if (c.length === 6) rows.push({ hymn: c[0], pattern: c[1] === '-' ? 0 : Number(c[1]), pickup: c[2] === 'yes', use: c[5] === 'yes' });
  }
  return rows;
}
