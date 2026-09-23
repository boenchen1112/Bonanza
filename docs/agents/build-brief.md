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
   one-for-one minigame designs — including AI-generated ones. A mockup or
   Meshy model prompted from "like Mario Party's X" is still one-for-one; the
   reference guides direction, the output must not be recognizable as it.
4. **Assets load fine by plain URL now — the game deploys to a real hosted
   URL, not `file://`.** `tools/embed-asset.mjs` still exists for the rare
   case you want an asset inlined, but it's optional, not the rule.
5. **The only network call in this codebase is the leaderboard client**
   (`web/src/net/`), and it only fires from `load()`/`result()`/shell
   screens, never `update()`/`input()`. No other piece talks to a server,
   an API, or an AI service at runtime — that's a dev-time tool, not a
   shipped feature. If your piece isn't the leaderboard, you have no reason
   to write a `fetch()` at all.
6. **Verify against the running game, not against your own reasoning.** Use
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
Ignore `fps`/`frameMs` — the harness renders through SwiftShader, so those
numbers say nothing about real hardware. Judge performance on `cpuMs`
(our JS per frame; budget < 4ms) and `render.drawCalls` (budget < 120).
**These budgets did not move for imported assets** — an embedded Blender/
Meshy model has to fit inside them, same as procedural geometry. If it
doesn't, decimate/merge/atlas it in Blender before embedding; don't ask to
raise the ceiling.

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
get to. Be honest about the last one — a separate critic is going to find it
anyway, and pretending it isn't there just costs a round.
