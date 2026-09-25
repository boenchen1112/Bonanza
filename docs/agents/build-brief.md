# Builder brief — Beat Bash Bonanza

You are building one piece of an original browser rhythm-party game series.
The bar is Nintendo first-party: not "good for a web game", not "good for an
AI", but something that would survive being put next to Mario Party's rhythm
minigames and still look like the better product.

**Read `web/ARCHITECTURE.md` first.** It defines the interfaces, the time
model, and who owns which files. Then read `web/src/core/clock.js`,
`judge.js` and `feel.js` — those three set the standard for the rest.

## Rules

1. **Own only your assigned files.** Others are working in this tree at the
   same time. Editing outside your assignment loses their work or yours.
2. **Do not run any `git` command.** Not `add`, not `commit`, not `stash`,
   not `checkout`. The integrator commits. Concurrent git operations in a
   shared tree corrupt each other.
3. **Original work only.** No Nintendo characters, assets, music, names, or
   one-for-one minigame designs. Study the craft; copy none of the content.
4. **No fetches outside the build** (ADR 0003). Authored assets (`.glb`,
   textures, fonts, wasm) may be bundled through `web/src/assets/index.js`
   from free sources, with a provenance README; nothing is requested from
   another origin. The one sanctioned network call is the leaderboard
   (ADR 0005). The build must run offline from a static server.
5. **Verify against the running game, not against your own reasoning.** Use
   the harness (below) and *look at the screenshots you produced*. If you did
   not open the images, you did not verify anything.

## Verifying your work

```bash
# from the repo root. Use YOUR OWN --dist and --out so parallel agents
# never collide.
node tools/harness/inspect.mjs \
  --scene <sceneId> --dist dist-<yourPieceId> --out runs/<yourPieceId> \
  --seconds 12 --shots 10 --play auto
```

Then **Read the PNGs**. `summary.json` must show `consoleErrors: 0`.
The harness renders on the real GPU (check `summary.json` → `gpu`,
`softwareRendered: false`), so what the PNGs show is what a player sees.
Judge performance on `cpuMs` (our JS per frame; budget < 4ms) and
`render.drawCalls` (budget < 120) — those transfer to other hardware;
`fps` only describes this machine.

## What "Nintendo first-party" means concretely

Cash these out in your piece; do not just nod at them.

- **Response is instant and legible.** Every input produces a visible change
  on the same frame, and the player can tell *which* input it was.
- **The moment is telegraphed.** A player who has never seen this before can
  predict what is about to be asked of them, one full beat early, from the
  visuals alone. Rhythm games that surprise you are broken, not hard.
- **Feedback is layered, not loud.** Sound + shape + colour + motion +
  position all say the same thing at once. Remove any one and it still reads.
- **Failure is funny, success is triumphant.** Nobody feels punished. The
  animation for a whiff is worth watching.
- **Nothing is ever static.** Idle poses breathe, the camera drifts, the
  crowd moves on the beat. But motion never competes with the thing the
  player must read.
- **It escalates.** The last eight bars are not the first eight bars.

## Reporting back

End with: what you built, what the harness measured, what you *saw* in the
screenshots, and the single weakest thing about your piece that you did not
get to. Be honest about the last one: it is where the next round starts.
