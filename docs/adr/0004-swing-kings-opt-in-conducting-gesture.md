# Swing Kings gets an opt-in conducting gesture; taps stay the default and the harness path

`docs/HANDOFF.md` §6 made Beat Bash Bonanza taps-only so every game stays drivable by the standard harness bot, and ADR 0002 made Baton Brawl the one scoped exception. The portfolio-polish pass (`docs/BBB_Portfolio_Polish_Spec.md` §5) needs the conducting thread — Swinger → Joy-Con analysis → this — visible in the hero game, so Swing Kings gets hold-and-release back as a *second* input mode.

## Decision

- **Tap stays the default and the primary path.** `profile.options.swingInput` defaults to `'tap'`; a tap swings on the press and earns power from timing (PERFECT home run / GREAT line drive / GOOD bunt).
- **Two opt-in conduct modes** (Options → SWING KINGS INPUT):
  - `mouse` — hold (key, pointer or touch) to wind up, then either release or finish a sharp downward drag; the swing fires at whichever comes first. Power is the windup length again (the original design: "the windup conducts the ball's arc").
  - `camera` — webcam hand tracking (MediaPipe HandLandmarker, fully bundled per ADR 0003). Raising the hand starts the windup; the downstroke's ictus swings. No camera, refused permission or no GPU → falls back to `mouse` with an on-screen notice.
- **Trigger-only.** Both gesture sources feed one pure detector (`web/src/games/swingKings/gesture.js`, unit-tested) that reports the *ictus* — the sharp deceleration ending a downstroke, the event CONTEXT.md anchors all timing to. The ictus time is what `NoteJudge` scores. Stroke shape and sharpness are never scored (the Swinger scoring model is not ported; see ADR 0001's timing-not-shape stance).
- **No accuracy bar.** Jank is acceptable; a player whose tracking flakes switches back to taps.

## Consequences

- The gesture modes are **exempt from the standard harness** for the same reason as ADR 0002: a webcam can't be driven headless, and the harness bot only emits key-downs. Instead:
  - `web/src/games/swingKings/verify.mjs` drives hold/release through the `__BBB__.swing` hooks (hold-power ladder + tap timing power);
  - `web/src/games/swingKings/smoke-conduct.mjs` drives a real conducted mouse stroke (the ictus, not the button-up, must release the swing) and boots camera mode on Chromium's fake device (the bundled pipeline must load offline with a clean console);
  - a person with a webcam does the real camera check.
- Tap mode remains fully harness-verified; `--play` runs are unaffected by the mode because the default is tap.
- This is the second scoped exception to taps-only, not a general licence: other minigames stay taps-only.
