/**
 * Render governor: keeps Graphics "Auto" at a (render scale, tier) the
 * machine can actually hold at 60fps.
 *
 * Pure and deterministic. The caller feeds every frame's duration and the
 * scene phase; the governor answers with a ladder move (or null). No clock
 * reads, no randomness — a frame-time history fully determines behaviour.
 *
 * Why these rules:
 *  - The target is 60fps on every display. A 144Hz screen is not worth
 *    losing the look for; smooth 60 is what a timing game needs.
 *  - Quality moves between rounds, not during them: a move reallocates render
 *    targets, and nothing should hitch while a player is being judged. The
 *    one exception is severe overload — one immediate step beats a whole
 *    round of stutter.
 *  - A level that failed a round is never climbed back into (a sticky
 *    ceiling), or a borderline machine would oscillate forever.
 *  - Menus are lighter than games, so headroom there proves nothing and never
 *    raises quality; overload there still lowers it.
 */

const BUDGET_MS = 1000 / 60;
const OVER = 1.05;        // p95 above budget x this = overloaded
const SEVERE = 1.6;       // p95 above budget x this = severe (act mid-round)
const HEADROOM = 0.7;     // round p95 below budget x this = room to climb
const WINDOW_MS = 2000;   // mid-round severe check
const MENU_WINDOW_MS = 3000;
const MIN_ROUND_MS = 20000;       // play needed before headroom counts
const MIN_OVERLOAD_MS = 8000;     // play needed before overload counts
const ROUND_WARMUP_MS = 2000;     // a scene's first frames compile shaders; ignored
const HEADROOM_ROUNDS = 2;
/** A frame longer than this is a tab-away, a breakpoint or an OS hiccup, not
 *  evidence about this machine's speed. One of them used to be enough to drop
 *  Auto a level for good (the move is remembered per device). */
const IGNORE_FRAME_MS = 500;

function p95(samples) {
  if (!samples.length) return 0;
  const s = samples.slice().sort((a, b) => a - b);
  return s[Math.min(s.length - 1, Math.floor(s.length * 0.95))];
}

/** Trailing window of frame durations covering at least `spanMs`. */
function makeWindow(spanMs) {
  const buf = [];
  let total = 0;
  return {
    push(ms) {
      buf.push(ms);
      total += ms;
      while (buf.length > 1 && total - buf[0] >= spanMs) total -= buf.shift();
    },
    get full() { return total >= spanMs; },
    get p95() { return p95(buf); },
    clear() { buf.length = 0; total = 0; },
  };
}

/**
 * @param {{ladder:{scale:number,tier:string}[], startIndex?:number, enabled?:boolean, budgetMs?:number}} opts
 */
export function createGovernor({ ladder, startIndex = 0, enabled = true, budgetMs = BUDGET_MS }) {
  const last = ladder.length - 1;
  let index = Math.max(0, Math.min(last, startIndex));
  let ceiling = 0;                 // lowest index Auto may climb back to
  let phase = 'loading';
  let headroomRounds = 0;
  let steppedThisRound = false;
  const recent = makeWindow(WINDOW_MS);
  const menu = makeWindow(MENU_WINDOW_MS);
  let round = [];
  let roundMs = 0;       // measured play, after warm-up
  let warmupMs = 0;      // play seen so far this round, warm-up included

  const decide = (to, reason, extra = {}) => {
    index = to;
    return { index, scale: ladder[index].scale, tier: ladder[index].tier, reason, ...extra };
  };

  function stepDown(reason) {
    if (index >= last) return null;
    ceiling = Math.max(ceiling, index + 1);
    headroomRounds = 0;
    return decide(index + 1, reason);
  }

  function endRound() {
    const q = p95(round);
    const long = roundMs >= MIN_ROUND_MS;
    const enough = roundMs >= MIN_OVERLOAD_MS;
    const wasStepped = steppedThisRound;
    round = [];
    roundMs = 0;
    warmupMs = 0;
    steppedThisRound = false;
    if (!q || !enough) { headroomRounds = 0; return null; }
    if (q > budgetMs * OVER) {
      if (index >= last) return decide(index, `floor overload (round p95 ${q.toFixed(1)}ms)`, { atFloorOverBudget: true });
      return stepDown(`overload (round p95 ${q.toFixed(1)}ms)`);
    }
    if (!long || wasStepped || q >= budgetMs * HEADROOM) { headroomRounds = 0; return null; }
    if (++headroomRounds < HEADROOM_ROUNDS || index <= ceiling) return null;
    headroomRounds = 0;
    return decide(index - 1, `headroom (round p95 ${q.toFixed(1)}ms)`);
  }

  return {
    get index() { return index; },
    get level() { return ladder[index]; },

    /**
     * @param {number} frameMs duration of the frame just finished
     * @param {'loading'|'playing'|'between'|'paused'} nextPhase what the game is doing now
     * @returns {null|{index:number, scale:number, tier:string, reason:string, atFloorOverBudget?:boolean}}
     */
    sample(frameMs, nextPhase) {
      if (!enabled || frameMs > IGNORE_FRAME_MS) return null;
      const prev = phase;
      phase = nextPhase;
      let out = null;

      if (prev === 'playing' && phase !== 'playing' && phase !== 'paused') out = endRound();
      if (phase !== 'between') menu.clear();
      if (phase !== 'playing') recent.clear();

      if (phase === 'playing' && warmupMs < ROUND_WARMUP_MS) {
        warmupMs += frameMs;
      } else if (phase === 'playing') {
        round.push(frameMs);
        roundMs += frameMs;
        recent.push(frameMs);
        // Entering or leaving 'low' toggles the shadow map, which recompiles
        // every lit shader (~1.2s on an Iris Xe) — never mid-round.
        const next = ladder[index + 1];
        const recompiles = next && (next.tier === 'low') !== (ladder[index].tier === 'low');
        if (!steppedThisRound && !recompiles && recent.full && recent.p95 > budgetMs * SEVERE) {
          const d = stepDown(`severe overload (p95 ${recent.p95.toFixed(1)}ms)`);
          if (d) {
            steppedThisRound = true;
            recent.clear();
            round = [];
            roundMs = 0;
            out = d;
          }
        }
      } else if (phase === 'between' && !out) {
        menu.push(frameMs);
        if (menu.full && menu.p95 > budgetMs * OVER) {
          menu.clear();
          out = stepDown(`menu overload`);
        }
      }
      return out;
    },
  };
}
