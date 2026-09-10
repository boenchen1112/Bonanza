/**
 * Audio check — is the captured mix audible, and does it land on the beat?
 *
 * Pure functions over PCM so they can be unit-tested with signals whose
 * right answer is known (web/tests/audiocheck.test.mjs). The harness feeds
 * them the game's live mix, captured on the context clock, plus the game's
 * own beat grid.
 *
 * Alignment is judged by CONTRAST, not by hit rate: onsets are counted
 * within ±TOL of the grid and within ±TOL of the same grid shifted half a
 * step. Quantised music scores high on the first and low on the second;
 * randomly timed (or uniformly late) audio scores the same on both, however
 * dense it is. That is what keeps a busy mix from passing by accident.
 */

const TOL_S = 0.02;

/** Mixdown to mono. */
export function mono(channels) {
  if (channels.length === 1) return channels[0];
  const n = channels[0].length;
  const out = new Float32Array(n);
  for (const c of channels) for (let i = 0; i < n; i++) out[i] += c[i] / channels.length;
  return out;
}

function median(a) {
  if (!a.length) return 0;
  const s = Float64Array.from(a).sort();
  const m = s.length >> 1;
  return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
}

/**
 * Percussive onset times (seconds from sample 0).
 *
 * Energy of a pre-emphasised signal in short frames, positive log-energy
 * flux, adaptive threshold, then each peak is refined to the first sample
 * that reaches 30% of the local transient peak — frame-hop quantisation
 * alone would smear every onset by several ms.
 */
export function detectOnsets(x, sampleRate, { hopS = 0.005, winS = 0.02, minGapS = 0.05, deltaDb = 4 } = {}) {
  const hop = Math.max(1, Math.round(hopS * sampleRate));
  const win = Math.max(hop, Math.round(winS * sampleRate));
  const n = x.length;
  // Pre-emphasis: transients up, sustained low tones down.
  const y = new Float32Array(n);
  for (let i = 1; i < n; i++) y[i] = x[i] - 0.97 * x[i - 1];

  const frames = Math.max(0, Math.floor((n - win) / hop));
  const e = new Float32Array(frames);
  for (let k = 0; k < frames; k++) {
    let s = 0;
    const o = k * hop;
    for (let i = 0; i < win; i++) s += y[o + i] * y[o + i];
    e[k] = 10 * Math.log10(s / win + 1e-12);
  }
  // Flux against the max of the two previous frames: robust to slow swells.
  const d = new Float32Array(frames);
  for (let k = 2; k < frames; k++) d[k] = Math.max(0, e[k] - Math.max(e[k - 1], e[k - 2]));

  const half = Math.round(0.25 / hopS);
  const gap = Math.round(minGapS / hopS);
  const onsets = [];
  let last = -Infinity;
  for (let k = 3; k < frames - 3; k++) {
    if (d[k] <= deltaDb) continue;
    let peak = true;
    for (let j = -3; j <= 3; j++) if (d[k + j] > d[k]) { peak = false; break; }
    if (!peak) continue;
    // Adaptive floor: a peak must stand out from its half-second neighbourhood.
    const lo = Math.max(0, k - half), hi = Math.min(frames, k + half);
    let m = 0;
    for (let j = lo; j < hi; j++) m += d[j];
    if (d[k] < m / (hi - lo) + deltaDb) continue;
    if (k - last < gap) continue;
    last = k;

    // Refine: the frame whose window contains the rise.
    const a = Math.max(0, (k - 1) * hop), b = Math.min(n, k * hop + win);
    let pk = 0;
    for (let i = a; i < b; i++) pk = Math.max(pk, Math.abs(y[i]));
    let at = a;
    for (let i = a; i < b; i++) if (Math.abs(y[i]) >= 0.3 * pk) { at = i; break; }
    onsets.push(at / sampleRate);
  }
  return onsets;
}

/** Grid of `sub` steps per beat, interpolated between the given beat times. */
function subdivide(beatTimes, sub, shift = 0) {
  const g = [];
  for (let i = 0; i < beatTimes.length - 1; i++) {
    const a = beatTimes[i], step = (beatTimes[i + 1] - a) / sub;
    for (let s = 0; s < sub; s++) g.push(a + (s + shift) * step);
  }
  return g;
}

function nearestErr(t, grid) {
  let lo = 0, hi = grid.length - 1;
  while (hi - lo > 1) { const m = (lo + hi) >> 1; if (grid[m] <= t) lo = m; else hi = m; }
  const e1 = t - grid[lo], e2 = t - grid[hi];
  return Math.abs(e1) < Math.abs(e2) ? e1 : e2;
}

/**
 * How well onsets sit on a `sub`-per-beat grid. Only onsets inside the
 * grid's span count.
 */
export function gridAlignment(onsets, beatTimes, sub, tolS = TOL_S) {
  const grid = subdivide(beatTimes, sub);
  const off = subdivide(beatTimes, sub, 0.5);
  if (grid.length < 2) return { sub, n: 0, onGrid: 0, offGrid: 0, contrast: 0, biasMs: 0, medianAbsErrMs: 0 };
  const span = [beatTimes[0] - tolS, beatTimes[beatTimes.length - 1] + tolS];
  const inside = onsets.filter((t) => t >= span[0] && t <= span[1]);
  const errs = inside.map((t) => nearestErr(t, grid));
  const on = errs.filter((e) => Math.abs(e) <= tolS);
  const offCount = inside.filter((t) => Math.abs(nearestErr(t, off)) <= tolS).length;
  const nIn = inside.length || 1;
  return {
    sub,
    n: inside.length,
    onGrid: on.length / nIn,
    offGrid: offCount / nIn,
    contrast: (on.length - offCount) / nIn,
    // Bias over ALL onsets (not just the on-grid ones): a uniformly late mix
    // must show its lag even when nothing is within tolerance.
    biasMs: median(errs) * 1000,
    medianAbsErrMs: median(errs.map(Math.abs)) * 1000,
  };
}

/**
 * The verdict. `startTime` is the context time of sample 0; `beatTimes` are
 * context times of consecutive integer beats. Passes when the mix is
 * audible and at least one of beat / 8th / 16th grids shows clear contrast.
 */
export function checkAudio({ samples, sampleRate, startTime = 0, beatTimes, minOnsets = 8 }) {
  let sq = 0, peak = 0;
  for (let i = 0; i < samples.length; i++) { sq += samples[i] * samples[i]; peak = Math.max(peak, Math.abs(samples[i])); }
  const rmsDb = 10 * Math.log10(sq / Math.max(1, samples.length) + 1e-12);
  const silent = rmsDb < -60;
  const onsets = silent ? [] : detectOnsets(samples, sampleRate).map((t) => t + startTime);
  const grids = [1, 2, 4].map((s) => gridAlignment(onsets, beatTimes, s));
  const ok = (g) => g.n >= minOnsets && g.onGrid >= 0.5 && g.contrast >= 0.25;
  const best = grids.filter(ok).sort((a, b) => b.contrast - a.contrast)[0]
    || grids.slice().sort((a, b) => b.contrast - a.contrast)[0];
  const pass = !silent && Boolean(best && ok(best));
  return {
    pass,
    silent,
    rmsDb: Math.round(rmsDb * 10) / 10,
    peak: Math.round(peak * 1000) / 1000,
    clipped: peak >= 0.999,
    onsets: onsets.length,
    bestGrid: best?.sub ?? null,
    biasMs: best ? Math.round(best.biasMs * 10) / 10 : null,
    grids: grids.map((g) => ({ ...g, onGrid: +g.onGrid.toFixed(3), offGrid: +g.offGrid.toFixed(3), contrast: +g.contrast.toFixed(3), biasMs: +g.biasMs.toFixed(1), medianAbsErrMs: +g.medianAbsErrMs.toFixed(1) })),
  };
}

/** Interleaved 16-bit PCM WAV. Returns a Uint8Array. */
export function encodeWav(channels, sampleRate) {
  const nc = channels.length, n = channels[0].length;
  const buf = new Uint8Array(44 + n * nc * 2);
  const v = new DataView(buf.buffer);
  const w = (o, s) => { for (let i = 0; i < s.length; i++) buf[o + i] = s.charCodeAt(i); };
  w(0, 'RIFF'); v.setUint32(4, 36 + n * nc * 2, true); w(8, 'WAVE');
  w(12, 'fmt '); v.setUint32(16, 16, true); v.setUint16(20, 1, true); v.setUint16(22, nc, true);
  v.setUint32(24, sampleRate, true); v.setUint32(28, sampleRate * nc * 2, true);
  v.setUint16(32, nc * 2, true); v.setUint16(34, 16, true);
  w(36, 'data'); v.setUint32(40, n * nc * 2, true);
  let o = 44;
  for (let i = 0; i < n; i++) {
    for (let c = 0; c < nc; c++) {
      const s = Math.max(-1, Math.min(1, channels[c][i]));
      v.setInt16(o, s < 0 ? Math.round(s * 32768) : Math.round(s * 32767), true);
      o += 2;
    }
  }
  return buf;
}
