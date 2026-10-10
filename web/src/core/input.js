/**
 * Unified, audio-timestamped input.
 *
 * Every press carries the audio time at which the player's finger actually
 * moved — taken from the DOM event timestamp, not from when the game loop
 * happened to notice. That is worth up to a full frame of accuracy and it is
 * the single cheapest thing you can do for rhythm feel.
 *
 * Actions are abstract ('a', 'b', 'left', ...) so minigames never touch key
 * codes and every device maps into the same vocabulary.
 */

const KEY_MAP = {
  Space: 'a', KeyZ: 'a', KeyJ: 'a', Enter: 'a',
  KeyX: 'b', KeyK: 'b',
  ArrowLeft: 'left', KeyA: 'left',
  ArrowRight: 'right', KeyD: 'right',
  ArrowUp: 'up', KeyW: 'up',
  ArrowDown: 'down', KeyS: 'down',
  Escape: 'pause', KeyP: 'pause',
};

// Gamepad button index -> action (standard mapping)
const PAD_MAP = {
  0: 'a', 1: 'b', 2: 'b', 3: 'a',
  12: 'up', 13: 'down', 14: 'left', 15: 'right',
  9: 'pause',
};

/** @typedef {{action:string, time:number, down:boolean, source:string, repeat:boolean}} InputEvent */

export class Input {
  /** @param {import('./clock.js').Clock} clock */
  constructor(clock, target = window) {
    this.clock = clock;
    this.target = target;
    this._listeners = new Set();
    this._held = new Set();
    /** @type {InputEvent[]} */
    this._queue = [];
    this._padPrev = {};
    this._enabled = true;
    this._bind();
  }

  on(fn) { this._listeners.add(fn); return () => this._listeners.delete(fn); }

  isHeld(action) { return this._held.has(action); }

  setEnabled(v) {
    this._enabled = v;
    if (!v) this._held.clear();
  }

  /**
   * Drain events accumulated since the last call. Minigames pull from here so
   * that presses are processed exactly once, in order, with their true times —
   * even if two presses land inside one frame.
   * @returns {InputEvent[]}
   */
  drain() {
    if (this._queue.length === 0) return EMPTY;
    const out = this._queue;
    this._queue = [];
    return out;
  }

  _emit(action, perfMs, down, source, repeat = false) {
    if (!this._enabled) return;
    const ev = {
      action,
      time: this.clock.toAudioTime(perfMs),
      down,
      source,
      repeat,
    };
    if (down) this._held.add(action); else this._held.delete(action);
    this._queue.push(ev);
    for (const fn of this._listeners) fn(ev);
  }

  _bind() {
    const t = this.target;

    t.addEventListener('keydown', (e) => {
      const action = KEY_MAP[e.code];
      if (!action) return;
      // Browsers autorepeat held keys. A rhythm game must never treat that as
      // a new press, but hold-style games still want to know the key is down.
      if (e.repeat) return;
      e.preventDefault();
      this._emit(action, e.timeStamp, true, 'key');
    }, { passive: false });

    t.addEventListener('keyup', (e) => {
      const action = KEY_MAP[e.code];
      if (!action) return;
      e.preventDefault();
      this._emit(action, e.timeStamp, false, 'key');
    }, { passive: false });

    // Pointer/touch: whole screen is the 'a' button, which is how this plays
    // on a phone. pointerdown fires before click and carries a real timestamp.
    t.addEventListener('pointerdown', (e) => {
      if (e.target && e.target.dataset && e.target.dataset.uiButton) return;
      this._emit('a', e.timeStamp, true, 'pointer');
    });
    t.addEventListener('pointerup', (e) => {
      this._emit('a', e.timeStamp, false, 'pointer');
    });

    // Lose focus -> release everything, or the player comes back to a stuck key.
    window.addEventListener('blur', () => {
      for (const a of [...this._held]) this._emit(a, performance.now(), false, 'blur');
      this._held.clear();
    });
  }

  /**
   * Poll gamepads. Gamepad state has no event timestamps we can trust for
   * sub-frame accuracy, so we attribute the press to the frame boundary — one
   * unavoidable frame of slop on pad, none on keyboard/touch.
   */
  pollGamepads(perfMs) {
    if (!this._enabled || !navigator.getGamepads) return;
    const pads = navigator.getGamepads();
    for (const pad of pads) {
      if (!pad) continue;
      const prev = this._padPrev[pad.index] || (this._padPrev[pad.index] = {});
      for (const [idx, action] of Object.entries(PAD_MAP)) {
        const btn = pad.buttons[idx];
        if (!btn) continue;
        const down = btn.pressed;
        if (down !== !!prev[idx]) {
          prev[idx] = down;
          this._emit(action, perfMs, down, 'pad');
        }
      }
      // Left stick as a d-pad, deadzoned.
      const [ax, ay] = pad.axes;
      const dirs = [
        ['left', ax < -0.5], ['right', ax > 0.5],
        ['up', ay < -0.5], ['down', ay > 0.5],
      ];
      for (const [action, on] of dirs) {
        const k = 'ax_' + action;
        if (on !== !!prev[k]) { prev[k] = on; this._emit(action, perfMs, on, 'pad'); }
      }
    }
  }
}

const EMPTY = Object.freeze([]);
