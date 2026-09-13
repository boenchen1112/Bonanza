#!/usr/bin/env node
/** Dependency-free static server for the harness. */
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';

const dir = path.resolve(process.argv[2] || '.');
const port = Number(process.argv[3] || 5321);

const TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png', '.jpg': 'image/jpeg', '.svg': 'image/svg+xml',
  '.woff2': 'font/woff2', '.map': 'application/json',
  '.webp': 'image/webp', '.ktx2': 'image/ktx2',
  '.glb': 'model/gltf-binary', '.gltf': 'model/gltf+json', '.bin': 'application/octet-stream',
  // WebAssembly.instantiateStreaming refuses anything but application/wasm.
  '.wasm': 'application/wasm',
  '.ogg': 'audio/ogg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav',
};

createServer(async (req, res) => {
  try {
    const url = new URL(req.url, 'http://x');
    let p = path.join(dir, decodeURIComponent(url.pathname));
    if (!p.startsWith(dir)) { res.writeHead(403).end(); return; }
    try {
      if ((await stat(p)).isDirectory()) p = path.join(p, 'index.html');
    } catch {
      p = path.join(dir, 'index.html');
    }
    const body = await readFile(p);
    res.writeHead(200, {
      'content-type': TYPES[path.extname(p)] || 'application/octet-stream',
      'cache-control': 'no-store',
    });
    res.end(body);
  } catch (e) {
    res.writeHead(404).end(String(e.message));
  }
}).listen(port, '127.0.0.1', () => console.log('serving ' + dir + ' on ' + port));
