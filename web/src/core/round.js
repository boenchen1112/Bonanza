/**
 * The count-in.  [INTEGRATOR-owned]
 *
 * `ARCHITECTURE.md` lists `ctx.ui.countdown()` in the GameContext. It was
 * never exported, so all five minigames hand-rolled the same thing against
 * `clock.onBeat` — five dialects, five presenters, one of them not even
 * beat-locked (it counted off `Math.floor(beat)` in update, so the number
 * landed on a frame rather than on the beat). There was no way to change the
 * count-in for the product, only for one game at a time.
 *
 * What is actually shared is the DRIVING, not the look: subscribe to the
 * beat, play the count tick at the right audio time, work out which number
 * this beat is, and stop at zero. The look stays each game's own — Swing
 * Kings prints up top over the grass, Finale Fever runs a bespoke CSS
 * keyframe — so `show` is a callback, and the default is the shared banner.
 *
 *   countIn(ctx, { beats: 8, go: 'PLAY BALL!' })
 *   countIn(ctx, { beats: LEAD, show: (n) => hud.flashCount(n) })
 *
 * Returns the unsubscribe, which is also registered on the scene's ledger by
 * `ctx.onBeat`, so forgetting it is no longer fatal.
 */

/**
 * @param {object} ctx GameContext — needs `onBeat`, `audio`, `ui`.
 * @param {object} [opts]
 * @param {number} [opts.beats]   how many beats of lead-in the transport runs
 * @param {number} [opts.from]    highest number spoken (default 4, i.e. "4 3 2 1")
 * @param {string|null} [opts.go] what the last beat says instead of "1"
 * @param {(text: string, n: number, time: number) => void} [opts.show]
 *        presenter. Default: the shared UI banner.
 * @param {(beat: number, time: number) => void} [opts.onBeat] extra per-beat work
 * @param {boolean} [opts.tick]   play the count SFX (default true)
 * @returns {() => void} unsubscribe
 */
export function countIn(ctx, opts = {}) {
  const {
    beats = 8,
    from = 4,
    go = null,
    show = (text) => ctx.ui?.banner?.(text, { life: 0.5, color: '#ffe9a8' }),
    onBeat = null,
    tick = true,
  } = opts;

  const goOnDownbeat = !!opts.goOnDownbeat;

  return ctx.onBeat((b, t) => {
    if (b === 0 && go && goOnDownbeat) show(go, 0, t);
    if (b >= 0 || b < -beats) { onBeat?.(b, t); return; }

    // `((b % 4) + 4) % 4` and not `b % 4`: beats are negative through the
    // lead-in and JS modulo keeps the dividend's sign.
    if (tick) ctx.audio?.sfx?.('count', t, ((b % 4) + 4) % 4);

    const n = -b;                       // 8,7,...,1 across the lead-in
    // Without `goOnDownbeat` the last counted beat says `go` instead of "1",
    // so the numbers shift down one: "3 2 1 PLAY BALL!" rather than
    // "4 3 2 1". With it, the numbers run to 1 and `go` lands on beat 0.
    const shift = go && !goOnDownbeat ? 1 : 0;
    if (n === 1 && shift) show(go, n, t);
    else if (n - shift >= 1 && n <= from + shift) show(String(n - shift), n, t);

    onBeat?.(b, t);
  });
}
