// Serves a Unity WebGL build (brotli) so you can play it in a normal browser.
// Usage: node Tools/serve-webgl.mjs [buildDir=Builds/WebGL] [port=7962]
//   then open http://127.0.0.1:7962/            (Free Play / title)
//        or  http://127.0.0.1:7962/?autoplay&game=swing-kings
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';

const DIR = process.argv[2] || 'Builds/WebGL';
const PORT = Number(process.argv[3] || 7962);
const TYPES = { '.html': 'text/html', '.js': 'application/javascript', '.css': 'text/css',
  '.wasm': 'application/wasm', '.data': 'application/octet-stream', '.png': 'image/png', '.ico': 'image/x-icon' };

http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]);
  if (p === '/') p = '/index.html';
  const f = path.join(DIR, p);
  if (!f.startsWith(path.resolve(DIR)) && !path.resolve(f).startsWith(path.resolve(DIR))) { res.writeHead(403); res.end(); return; }
  if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end(); return; }
  const headers = {};
  let ext = path.extname(f);
  if (ext === '.br') { headers['Content-Encoding'] = 'br'; ext = path.extname(f.slice(0, -3)); }
  headers['Content-Type'] = TYPES[ext] || 'application/octet-stream';
  res.writeHead(200, headers);
  fs.createReadStream(f).pipe(res);
}).listen(PORT, '127.0.0.1', () => console.log(`Bonanza WebGL build: http://127.0.0.1:${PORT}/  (Ctrl-C to stop)`));
