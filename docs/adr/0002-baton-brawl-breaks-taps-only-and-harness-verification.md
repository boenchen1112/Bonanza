# Baton Brawl is the one Beat Bash Bonanza game exempt from "taps only" and standard harness verification

`docs/HANDOFF.md` §6 establishes taps-only input across Beat Bash Bonanza specifically so every game stays exercisable by the standard critic harness bot (`tools/harness/inspect.mjs --play auto|perfect|sloppy`). Baton Brawl deliberately breaks this: it reads a real Joy-Con via WebHID, which the headless-Chromium harness cannot drive — `navigator.hid.requestDevice()` requires a live user gesture and grants no synthetic-device path, and there is no way to feed it fabricated gyro data through the browser API surface it actually uses.

We chose to accept this rather than build a synthetic-ictus injection path, because Baton Brawl's actual novel logic (WebHID → ictus detection) sits *upstream* of the shared, already-tested `core/judge.js` `NoteJudge` — the scoring/verdict/rank machinery stays automatable and unit-testable even though full hardware-driven play does not.

## Consequences

Baton Brawl must be manually played and verified, not harness-verified, for as long as it exists in its current form. This is a deliberate, scoped exception — it is not precedent for adding other non-tap input games without separately re-litigating the harness-coverage trade-off.
