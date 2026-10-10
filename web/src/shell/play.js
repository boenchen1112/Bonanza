/**
 * Minigame host.  [shell agent owns this file]
 *
 * A minigame implements the interface; it does not implement the shell. So the
 * shell wraps it: this scene loads the game module, forwards the lifecycle
 * verbatim, and owns the three things a game must never have to think about —
 *
 *   PAUSE     'pause' is intercepted here, the transport is frozen, and the
 *             resume counts back in at the exact beat we stopped on.
 *   RESULTS   `result()` is polled every frame, and `ctx.go('results', …)` is
 *             intercepted too, so a game may end either way and still land in
 *             the shell's results/party flow.
 *   ROUTING   where "back" goes depends on whether this is Free Play or game
 *             three of a party, and the game does not know or care.
 *
 * Booting a game id directly (the critic harness does) still works — it simply
 * skips this wrapper, and loses only pause.
 */

import { getScene } from './registry.js';
import { createPause } from './pause.js';
import { mountRoot, sfx, PAL, createWipe, el, hex } from './theme.js';
import { session, profile, ensurePlayers } from './state.js';
import { gamePlayers } from './chars.js';
import { CATALOG } from './games.js';
import { goView, exitRoute } from './nav.js';
import { clamp01 } from '../core/util.js';
import { normaliseResult } from '../core/result.js';

/** Beats of count-in after a pause. Three is the shortest that reads as one. */
const RESUME_BEATS = 3;
/** Hold after a game reports its result, so its own last flourish can land. */
const OUTRO_S = 0.55;
/** Frames the game draws under its title card before the card pulls away. */
const COVER_HOLD_S = 0.12;

let S = null;

export default {
  id: 'play',
  name: 'Play',

  async load(ctx) {
    const gameId = ctx.opts?.game || session.currentGame || CATALOG[0].id;
    S = {
      gameId, mod: null, paused: false, resumeAt: 0, pauseBeat: 0, pauseTime: 0,
      sched: null, finished: null, outro: 0, from: ctx.opts?.from || 'freeplay',
      canvasFilter: '', t: 0,
    };

    // Shadow ctx.go for the game's lifetime. A game that routes itself to
    // 'results' is doing the right thing; it just doesn't know about party
    // standings, so we take that call and re-route it through the shell.
    const baseGo = ctx.go.bind(ctx);
    S.baseGo = baseGo;
    ctx.go = (id, opts = {}) => {
      if (id === 'results') { finish(ctx, opts.result || opts, 'self'); return; }
      baseGo(id, opts);
    };

    // The lineup the roster built is who plays: the human's character is the
    // hero, the named CPUs are the rivals in games that field any.
    ctx.players = gamePlayers(ensurePlayers());
    // The player's latency calibration from Options, for each game's NoteJudge.
    ctx.offsetMs = profile.options.offsetMs || 0;

    // Title card over the swap: the menu's wipe covers the exit, this one
    // holds while the game builds and its shaders compile (a blocking ~1s on
    // some GPUs), then pulls away once the game has drawn a few frames.
    S.root = mountRoot(ctx, 'sh-play');
    const meta = CATALOG.find((g) => g.id === gameId);
    S.cover = createWipe(S.root, { color: hex(meta?.color ?? PAL.yellow), mode: 'in' });
    // Over the wipe, not inside it: in the skewed, oversized panel the name sat ~90px left of centre.
    S.coverLabel = el('div', 'sh-display sh-play__card', meta?.name || '');
    S.root.appendChild(S.coverLabel);

    const mod = await getScene(gameId);
    if (!mod) {
      console.error('play: unknown game', gameId);
      baseGo('title', {});
      return;
    }
    S.mod = mod;

    S.pause = createPause(S.root, {
      onSelect: (id) => {
        if (id === 'resume') resume(ctx);
        else if (id === 'restart') restart(ctx);
        else quit(ctx);
      },
    });

    await mod.load?.(ctx);
  },

  start(ctx) {
    S?.mod?.start?.(ctx);
  },

  update(ctx, dt, beat) {
    if (!S) return;
    S.t += dt;
    if (S.t > COVER_HOLD_S) {
      S.cover.update(dt);
      S.coverLabel.style.opacity = String(clamp01(1 - (S.t - COVER_HOLD_S) / 0.12));
    }
    S.pause.update(dt, beat);

    if (S.paused) {
      // Counting back in: the game stays frozen until the transport reaches
      // the beat it was frozen on, so nothing in the chart moves backwards.
      if (S.resumeAt) {
        const left = S.resumeAt - ctx.clock.now();
        if (left <= 0) {
          S.paused = false;
          S.resumeAt = 0;
          S.pause.setCount(0);
        } else {
          const n = Math.max(1, Math.ceil(left / ctx.clock.spb));
          S.pause.setCount(n);
        }
      }
      return;
    }

    S.mod?.update?.(ctx, dt, beat);

    if (!S.finished) {
      let r = null;
      try { r = S.mod?.result?.(ctx); } catch (e) { console.error(e); }
      if (r) finish(ctx, r, 'poll');
    } else if (S.finished.route) {
      S.outro += dt;
      if (S.outro >= OUTRO_S) {
        const go = S.finished.route;
        S.finished.route = null;
        go();
      }
    }
  },

  input(ctx, events) {
    if (!S) return;
    const forward = [];
    for (const e of events) {
      if (S.pause.open) { S.pause.input(ctx, e); continue; }
      if (e.down && e.action === 'pause') { openPause(ctx); continue; }
      if (S.paused || S.finished) continue;
      forward.push(e);
    }
    if (forward.length) {
      try { S.mod?.input?.(ctx, forward); } catch (e) { console.error(e); }
    }
  },

  /** Harness hook, forwarded so the bot can play a game hosted here too. */
  testChart(ctx) {
    return S?.mod?.testChart?.(ctx) ?? null;
  },

  dispose(ctx) {
    if (!S) return;
    unblur(ctx);
    try { S.mod?.dispose?.(ctx); } catch (e) { console.error('game dispose', e); }
    S.pause?.dispose();
    S.root?.remove();
    S = null;
  },
};

// ------------------------------------------------------------------ pause

function openPause(ctx) {
  if (S.paused || S.finished) return;
  S.paused = true;
  S.resumeAt = 0;
  S.pauseTime = ctx.clock.now();
  // suspend(), not stop(): stop() drops the one-shot schedule, and those
  // entries are keyed by beat, so they survive the resume untouched.
  S.pauseBeat = ctx.clock.suspend();
  // pause, not stop: stop() dropped the track, and nothing ever restarted it
  const music = ctx.audio?.music;
  if (music?.pause) music.pause(); else music?.stop?.();
  S.pause.show();
  S.pause.clearCount();
  sfx(ctx, 'uiBack');
  blur(ctx);
  ctx.bus?.emit?.('pause', { beat: S.pauseBeat });
}

function resume(ctx) {
  S.pause.hide();
  unblur(ctx);
  const lead = RESUME_BEATS * ctx.clock.spb;
  const at = ctx.clock.now() + lead;
  ctx.clock.start(at, S.pauseBeat);
  ctx.audio?.music?.resume?.();
  S.resumeAt = at;
  // Absolute note times the game computed before the pause are now `shift`
  // seconds early. Games that implement `shiftTime` stay in sync; the bus
  // event is the same news for anything else that cached an audio time.
  const shift = at - S.pauseTime;
  try { S.mod?.shiftTime?.(ctx, shift); } catch { /* optional hook */ }
  ctx.bus?.emit?.('transport:shift', shift);
  ctx.bus?.emit?.('resume', { beat: S.pauseBeat, shift });
  // No setEnabled(true) here: nothing ever disables Input, and it must stay
  // enabled while paused — the pause overlay is driven by the same drained
  // events (see input() above, which routes to S.pause.input while open).
}

function restart(ctx) {
  S.pause.hide();
  unblur(ctx);
  const opts = { game: S.gameId, from: S.from };
  const go = S.baseGo;
  S.paused = false;
  sfx(ctx, 'ui');
  // Route through nav so this works whether or not `play` has its own id.
  goView({ go, opts: {} }, 'play', opts);
}

function quit(ctx) {
  S.pause.hide();
  unblur(ctx);
  session.aborted = true;
  sfx(ctx, 'uiBack');
  const go = S.baseGo;
  const nav = { go, opts: {} };
  const inParty = session.mode === 'party' && !!session.party;
  const r = exitRoute({ inParty, from: S.from, gameId: S.gameId });
  goView(nav, r.view, inParty ? { ...r.opts, quit: true } : r.opts);
}

function blur(ctx) {
  const cv = ctx.renderer?.domElement;
  if (!cv) return;
  S.canvasFilter = cv.style.filter || '';
  cv.style.filter = 'blur(7px) saturate(.5) brightness(.62)';
  cv.style.transition = 'filter .16s ease-out';
}

function unblur(ctx) {
  const cv = ctx.renderer?.domElement;
  if (!cv) return;
  cv.style.filter = S?.canvasFilter || '';
}

// ---------------------------------------------------------------- finish

function finish(ctx, result, source) {
  if (S.finished) return;
  const res = normaliseResult(result);
  S.finished = { res, source, route: null };
  S.outro = 0;
  session.lastResult = res;
  session.lastGame = S.gameId;
  const go = S.baseGo;
  const nav = { go, opts: {} };
  S.finished.route = () => goView(nav, 'results', {
    result: res, game: S.gameId, from: S.from, party: session.mode === 'party' && !!session.party,
  });
}

export const PLAY_ACCENT = PAL.yellow;
