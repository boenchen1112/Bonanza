/**
 * Track compiler: pattern DATA -> a flat, sorted event list.  [audio agent]
 *
 * A track is written as human-readable patterns (drum grids as strings, melody
 * as [beat, scaleDegree, duration, velocity]) so that the music is editable as
 * *music*, not as a graph of oscillator calls. This file turns that into the
 * one structure the scheduler wants: every event in the loop, sorted by beat,
 * with its bar index precomputed.
 *
 * Compilation happens once per `play()`. The hot path then does zero parsing.
 *
 * ── Grid characters ──────────────────────────────────────────────────────
 *   x X   full velocity        o   0.62 (unaccented)
 *   1..9  velocity n/9         s   0.22 (ghost note)
 *   . - _ (space)   rest
 */

const VEL = { x: 1, X: 1, o: 0.62, s: 0.22, g: 0.34 };

function velOf(ch) {
  if (ch === '.' || ch === '-' || ch === '_' || ch === ' ' || ch === '0') return 0;
  if (VEL[ch] !== undefined) return VEL[ch];
  const n = ch.charCodeAt(0) - 48;
  if (n >= 1 && n <= 9) return n / 9;
  return 0;
}

/**
 * Swing displacement in beats. `s` is how far the "and" of the beat moves:
 * s = 1/6 lands it exactly on the triplet, which is a jazz shuffle. Sixteenths
 * move half as far, which is what keeps a shuffled hat pattern from collapsing.
 */
function swingOffset(beat, s) {
  if (!s) return 0;
  const frac = beat - Math.floor(beat);
  if (Math.abs(frac - 0.5) < 1e-6) return s;
  if (Math.abs(frac - 0.25) < 1e-6 || Math.abs(frac - 0.75) < 1e-6) return s * 0.5;
  return 0;
}

function barActive(mask, bar, bars) {
  if (!mask) return true;
  if (typeof mask === 'string') return velOf(mask[bar % mask.length]) > 0;
  if (Array.isArray(mask)) return mask.includes(bar % bars);
  return true;
}

/**
 * @returns {{events: Array, loopBeats: number, bars: number, beatsPerBar: number}}
 */
export function compileTrack(track) {
  const bpb = track.beatsPerBar ?? 4;
  const bars = track.bars ?? 16;
  const loopBeats = bpb * bars;
  const swing = Math.min(track.swing ?? 0, 0.22);
  const events = [];

  track.layers.forEach((layer, li) => {
    const span = layer.span ?? 1;
    const useSwing = layer.swing !== false;

    for (let bar = 0; bar < bars; bar++) {
      if (!barActive(layer.bars, bar, bars)) continue;

      // --- percussion grid ------------------------------------------------
      if (layer.grid) {
        const g = Array.isArray(layer.grid) ? layer.grid[bar % layer.grid.length] : layer.grid;
        const steps = g.length;
        const stepBeats = bpb / steps;
        for (let i = 0; i < steps; i++) {
          const v = velOf(g[i]);
          if (v <= 0) continue;
          const raw = bar * bpb + i * stepBeats;
          push(raw, {
            kind: 'drum', layer, li, vel: v, dur: layer.dur ?? 0.25, deg: 0, bar,
          });
        }
        continue;
      }

      // --- chord rhythm ---------------------------------------------------
      if (layer.rhythm) {
        const r = Array.isArray(layer.rhythm[0]) ? layer.rhythm : [layer.rhythm];
        const set = Array.isArray(r[0][0]) ? r[bar % r.length] : r;
        for (const [b, dur, vel] of set) {
          const raw = bar * bpb + b;
          if (raw >= loopBeats) continue;
          push(raw, {
            kind: 'chord', layer, li, vel: vel ?? 1, dur: dur ?? 1, deg: 0,
            bar: Math.floor(raw / bpb),
          });
        }
        continue;
      }

      // --- melodic phrase -------------------------------------------------
      const phraseSource = layer.seq || (layer.notes ? [layer.notes] : null);
      if (!phraseSource) continue;
      if (bar % span !== 0) continue; // a phrase is emitted once, at its start
      const phrase = phraseSource[Math.floor(bar / span) % phraseSource.length];
      if (!phrase) continue;
      for (const n of phrase) {
        const [b, deg, dur = 0.5, vel = 1] = n;
        const raw = bar * bpb + b;
        if (raw >= loopBeats) continue;
        push(raw, {
          kind: 'note', layer, li, vel, dur, deg, bar: Math.floor(raw / bpb),
        });
      }
    }

    function push(raw, ev) {
      ev.beat = raw + (useSwing ? swingOffset(raw, swing) : 0);
      events.push(ev);
    }
  });

  events.sort((a, b) => (a.beat - b.beat) || (a.li - b.li));
  return { events, loopBeats, bars, beatsPerBar: bpb };
}

/** Which layers sound at a given intensity. Pure — the tests use it too. */
export function layerActive(layer, intensity) {
  const min = layer.min ?? 0;
  const max = layer.max ?? 99;
  return intensity >= min && intensity <= max;
}
