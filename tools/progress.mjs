#!/usr/bin/env node
/**
 * Renders progress/state.json into a single self-contained progress page.
 *
 * Deliberately dumb: read JSON, write HTML. Anything that has to be run by
 * hand mid-build needs to be impossible to get wrong.
 */

import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

const ROOT = path.resolve(path.join(import.meta.dirname, '..'));
const state = JSON.parse(await readFile(path.join(ROOT, 'progress/state.json'), 'utf8'));

const STATUS = {
  done: { label: 'shipped', c: 'ok' },
  building: { label: 'building', c: 'go' },
  reviewing: { label: 'in review', c: 'rev' },
  rework: { label: 'sent back', c: 'bad' },
  queued: { label: 'queued', c: 'idle' },
};

const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

const groups = [...new Set(state.pieces.map((p) => p.group))];

const counts = state.pieces.reduce((m, p) => ((m[p.status] = (m[p.status] || 0) + 1), m), {});
const doneN = counts.done || 0;
const pct = Math.round((doneN / state.pieces.length) * 100);

const rows = groups.map((g) => {
  const items = state.pieces.filter((p) => p.group === g);
  return `<section class="grp">
    <h2>${esc(g)}</h2>
    <div class="cards">
      ${items.map((p) => {
    const st = STATUS[p.status] || STATUS.queued;
    return `<article class="card s-${st.c}">
        <header><span class="id">${esc(p.id)}</span><span class="pill">${esc(st.label)}</span></header>
        <h3>${esc(p.name)}</h3>
        <p>${esc(p.detail)}</p>
        ${p.rounds ? `<div class="meta">${p.rounds} critic round${p.rounds === 1 ? '' : 's'}</div>` : ''}
        ${p.critic ? `<blockquote>${esc(p.critic)}</blockquote>` : ''}
      </article>`;
  }).join('')}
    </div>
  </section>`;
}).join('');

const log = (state.log || []).slice().reverse().map((l) =>
  `<li><time>${esc(l.t)}</time><span>${esc(l.msg)}</span></li>`).join('');

const html = `<title>${esc(state.title)} — build progress</title>
<style>
:root{
  --bg:#f7f7fb; --panel:#fff; --ink:#14141f; --dim:#5d5d76; --line:#e4e4ee;
  --ok:#1f9d55; --go:#2f6fed; --rev:#b8860b; --bad:#d1425a; --idle:#9aa0b4;
  --accent:#5b3df5;
}
:root:not([data-theme="light"]){ }
@media (prefers-color-scheme: dark){
  :root:not([data-theme="light"]){
    --bg:#0c0b14; --panel:#15142270; --ink:#f2f2f8; --dim:#a2a2bd; --line:#2a2840;
    --ok:#49d888; --go:#7aa2ff; --rev:#ffd166; --bad:#ff6b83; --idle:#666a85;
    --accent:#a48bff;
  }
}
:root[data-theme="dark"]{
  --bg:#0c0b14; --panel:#15142270; --ink:#f2f2f8; --dim:#a2a2bd; --line:#2a2840;
  --ok:#49d888; --go:#7aa2ff; --rev:#ffd166; --bad:#ff6b83; --idle:#666a85;
  --accent:#a48bff;
}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);
  font:16px/1.55 ui-sans-serif,system-ui,-apple-system,"Segoe UI",sans-serif;
  padding:clamp(20px,4vw,56px);max-width:1180px;margin-inline:auto;}
h1{font-size:clamp(28px,5vw,46px);letter-spacing:-.03em;margin:0 0 .2em;line-height:1.05}
.sub{color:var(--dim);margin:0 0 1.6em;font-size:clamp(15px,2vw,19px);max-width:60ch}
.bar{height:10px;border-radius:99px;background:var(--line);overflow:hidden;margin:.4em 0 .5em}
.bar i{display:block;height:100%;width:${pct}%;background:linear-gradient(90deg,var(--accent),var(--ok));border-radius:99px}
.top{display:flex;flex-wrap:wrap;gap:12px 28px;align-items:baseline;margin-bottom:.5em}
.big{font-size:clamp(30px,5vw,44px);font-weight:800;letter-spacing:-.03em}
.wave{display:inline-block;padding:.25em .8em;border-radius:99px;background:var(--accent);color:#fff;
  font-weight:700;font-size:14px;letter-spacing:.01em}
.note{background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:16px 18px;
  color:var(--dim);margin:1.2em 0 2.2em}
.grp{margin:0 0 2.4em}
.grp h2{font-size:13px;text-transform:uppercase;letter-spacing:.12em;color:var(--dim);
  margin:0 0 .9em;font-weight:700}
.cards{display:grid;gap:14px;grid-template-columns:repeat(auto-fill,minmax(270px,1fr))}
.card{background:var(--panel);border:1px solid var(--line);border-radius:16px;padding:16px 17px;
  border-left:4px solid var(--idle)}
.card.s-ok{border-left-color:var(--ok)} .card.s-go{border-left-color:var(--go)}
.card.s-rev{border-left-color:var(--rev)} .card.s-bad{border-left-color:var(--bad)}
.card header{display:flex;justify-content:space-between;align-items:center;margin-bottom:.5em}
.id{font:600 12px ui-monospace,SFMono-Regular,Menlo,monospace;color:var(--dim);letter-spacing:.06em}
.pill{font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.07em;color:var(--dim)}
.s-ok .pill{color:var(--ok)} .s-go .pill{color:var(--go)}
.s-rev .pill{color:var(--rev)} .s-bad .pill{color:var(--bad)}
.card h3{margin:0 0 .35em;font-size:17px;letter-spacing:-.01em}
.card p{margin:0;color:var(--dim);font-size:14px}
.meta{margin-top:.6em;font-size:12px;color:var(--dim);font-weight:600}
blockquote{margin:.7em 0 0;padding:.55em .8em;border-radius:10px;background:color-mix(in srgb,var(--bad) 10%,transparent);
  font-size:13px;color:var(--ink);border-left:2px solid var(--bad)}
ul.log{list-style:none;padding:0;margin:0;border-top:1px solid var(--line)}
ul.log li{display:flex;gap:16px;padding:.6em 0;border-bottom:1px solid var(--line);font-size:14px}
ul.log time{color:var(--dim);font:500 12px ui-monospace,Menlo,monospace;flex:0 0 148px;padding-top:.2em}
footer{margin-top:3em;color:var(--dim);font-size:13px}
</style>

<h1>${esc(state.title)}</h1>
<p class="sub">${esc(state.subtitle)}</p>

<div class="top">
  <span class="big">${pct}%</span>
  <span class="wave">${esc(state.wave)}</span>
  <span style="color:var(--dim)">${doneN} of ${state.pieces.length} pieces through review</span>
</div>
<div class="bar"><i></i></div>

<div class="note">${esc(state.note)}</div>

${rows}

<section class="grp">
  <h2>Build log</h2>
  <ul class="log">${log}</ul>
</section>

<footer>Updated ${esc(state.updated)} · every piece is built by one agent and judged by a separate one that inspects the running game, never the builder's report.</footer>
`;

await writeFile(path.join(ROOT, 'progress/index.html'), html);
console.log(`progress: ${pct}% (${doneN}/${state.pieces.length}) -> progress/index.html`);
