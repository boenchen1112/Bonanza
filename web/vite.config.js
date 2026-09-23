import { defineConfig } from 'vite';

export default defineConfig({
  base: './',
  server: { host: '127.0.0.1', port: 5178, strictPort: true },
  preview: { host: '127.0.0.1', port: 5179, strictPort: true },
  build: {
    target: 'es2022',
    outDir: 'dist',
    sourcemap: true,
    // Embedded assets (see tools/embed-asset.mjs) are already base64 JS
    // string literals, not binary files Vite discovers on its own — this
    // only guards against someone importing a raw .glb/.png/.mp3 by
    // accident and getting a separate emitted file that fetch() can't
    // reach from file://. Keep it effectively infinite: every asset must
    // go through the embed script, never be Vite-inlined-by-size-luck.
    assetsInlineLimit: Number.MAX_SAFE_INTEGER,
  },
});
