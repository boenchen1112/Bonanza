# Bug Audit — 2026-07-30: hand-tracking conducting trainer (v5 Phase 2)

Scope: `research/hand_tracking_web/` (`index.html`, `server.py`,
`assets/`), plus `research/extract_pattern_from_video.py` and
`src/conducting_patterns.py` where they feed it. Audit of the state after
v5 Phase 2's UX-polish and reference-pattern-extraction work. **No fixes
applied.**

Requested focus: **calibration**, **reference-image scale**, and whether
**scoring actually works**. Short answer on the third: **no — it is
measuring close to the opposite of what it should.** Numbers below.

Method: full static read of all four files; recomputed the reference-image
anchor transform against the extraction script's own ground-truth vertex
CSVs (`research/captures/pattern_*_vertices_px.csv`); verified PNG and
source-video pixel dimensions; and ported `shapeDistance()`/
`resamplePath()` to Python line-for-line and ran adversarial traces
through them to measure what the scorer actually rewards.

Severity: **P1** = produces wrong results a user would notice or trust
wrongly; **P2** = real defect, narrower blast radius; **P3** =
correctness/robustness/hygiene.

Design note before the findings: **the overall architecture here is
sound**, and one thing in particular is worth stating so it doesn't get
"fixed" by mistake. The chain calibrated-origin → `calibScaleFactor` →
`scalePxPerUnit` → reference-image transform is *mathematically
consistent*: the BOTTOM calibration point makes the live trace land at
exactly `refDelta` logical units from the origin, and the image anchor
maps `beat1Px` to the same `refDelta`. Trace and image therefore agree by
construction. The bugs below are wrong *inputs* to that correct machinery
(R1, R2, C2) and a scoring function that was never right (S1–S4) — not a
broken design.

---

## Part A — Reference-image scale (focus area)

### R1 (P1). 2/4's image anchors are hand-guessed and materially wrong — and the correct values are already in the repo, unused
`REFERENCE_IMAGE_ANCHORS[2]` uses `prepPx: [900, 75]`,
`beat1Px: [1265, 1100]`, documented as "user-supplied prep/beat1 pixel
coordinates (2026-07-30)". But
`research/captures/pattern_24_vertices_px.csv` — written by
`extract_pattern_from_video.py`, the same script the `REFERENCE` numbers
came from — records the *measured* positions in that exact bitmap:

| point | in use (`index.html`) | ground truth (CSV) | error |
|---|---|---|---|
| prep | (900, 75) | (984, 113) | (−84, −38) px |
| beat 1 | (1265, 1100) | (1463, 1017) | (−198, +83) px |
| Δ | (365, 1025) | (479, 904) | x 24% short, y 13% long |

Consequences, computed:

```
in use:  scaleX = 1.452e-3, scaleY = 9.756e-4   ->  X/Y = 1.488
truth:   scaleX = 1.106e-3, scaleY = 1.106e-3   ->  X/Y = 1.000
```

The 2/4 reference image is therefore drawn **48.8% too wide horizontally**
and its vertical scale is **~12% off** as well. Native bitmap aspect is
2558×1306 = 1.959; as drawn it becomes 2.91. The student is matching a
visibly stretched diagram.

3/4 and 4/4 are fine by comparison: 3/4's anchor reproduces the truth
almost exactly (scaleY 7.3635e-4 vs 7.364e-4), 4/4 is off by 1.3% in
scale. So **the three signatures are not equally trustworthy**, which is
itself a trap — checking 3/4 and concluding "the anchoring works" would
be a false negative for 2/4.

Verified as a precondition: PNG dimensions match their source videos
exactly (`reference_24.png` 2558×1306 = `2_4.mp4` 2558×1306;
`reference_34.png` 2558×1308 = `3_4.mp4` 2558×1308; `reference_44.png`
2558×1306 = `4_4.mp4`). The extracted pixel coordinates are directly
usable as anchors with no rescaling.

**Fix direction:** read the anchors from the extraction CSVs instead of
transcribing them by hand (see D7 — same root cause).

### R2 (P1). `axisMode: "xy"` is conceptually wrong: per-axis scaling *must* distort, because `REFERENCE` is uniformly normalized
`extract_pattern_from_video.py::to_logical()` divides x and y by a
**single** `max_extent`:

```python
max_extent = max(max(abs(x), abs(y)) for x, y in pts) or 1.0
return [(x / max_extent, y / max_extent) for x, y in pts]
```

That is a uniform scale, so `REFERENCE`'s logical coordinates preserve the
image's true aspect ratio. Proof from the data: ground-truth pixel
`dx/dy = 479/904 = 0.5299`, and logical `dx/dy = 0.5299/1.0 = 0.5299` —
identical to four decimals. There is exactly one correct scale factor per
signature, and any independent x/y fit is guaranteed to introduce
distortion.

The code comment justifies `axisMode` as honoring "the user explicitly
asked to not rotate the image, transforming the axes independently
instead." Those are not the same request: **avoiding rotation does not
require independent axis scales.** A single uniform scale + translation
(no rotation term) satisfies the instruction *and* preserves the diagram's
shape. The two-mode design is what let R1's bad anchors pass unnoticed —
with a uniform scale, a bad anchor produces an obviously wrong *size*
(easy to spot); with per-axis scales it silently absorbs the error into a
stretch instead.

**Fix direction:** delete `axisMode` and the `scaleX`/`scaleY` split; use
one uniform scale for all three signatures. Note that once R1's anchors
are correct, 2/4's own x and y scales *agree to four decimals anyway* —
the "xy" mode was only ever compensating for the bad anchors.

### R3 (P2). `imgWidthPx`/`imgHeightPx` are declared but never read — the anchors are unvalidated
Both fields exist in every `REFERENCE_IMAGE_ANCHORS` entry and appear
nowhere in `drawReferenceImage()` or anywhere else. They imply a
dimension check that isn't happening. They happen to be correct today
(verified above), but if a reference PNG is ever re-exported at a
different size, every anchor silently becomes wrong with no error.

**Fix direction:** assert `img.naturalWidth === anchor.imgWidthPx` (and
height) when the image loads, and surface a visible message on mismatch —
or drop the fields so they stop implying a guarantee.

### R4 (P2). The fit uses only 2 of the 3–5 available extracted vertices, so it cannot self-check
Every signature has 3–5 measured vertices in its CSV (2/4: prep + 2
beats; 4/4: prep + 4 beats), and the transform uses only prep and beat 1.
A least-squares uniform-scale + translation fit over *all* vertices would
(a) average out per-point picking error and (b) yield a residual — a
number that would have flagged R1 immediately, since bad anchors can't fit
the remaining points.

**Fix direction:** fit over all vertices; log/display the residual and
warn above a threshold.

### R5 (P2). Reference image failing to load is silent; and a stale reference asset sits beside the live one
`drawReferenceImage()` early-returns on `!img.complete`. A 404, a decode
error, or a slow load all render as "no reference at all" with no message
— indistinguishable from a signature that has no diagram.

Separately, `assets/reference_2_4.png` (157×255) is the superseded 2/4
asset, sitting next to the live `assets/reference_24.png` (2558×1306) with
a one-underscore filename difference. This is the third instance of the
"stale file next to the good one" pattern this project has been bitten by
(`swings.csv` in the 7/28 audit, `primer.md.lnk` in the 7/17 audit).

**Fix direction:** `img.onerror` → visible status message; delete or
`archive/` the stale asset.

### R6 (P3). `drawReferenceImage()` uses `setTransform`, which silently discards any ambient canvas transform
`ctx.setTransform(...)` is absolute, not relative. It works today only
because the function is called after `renderFrame()`'s mirror
`save`/`restore` has been unwound — which the comment correctly documents
as a bug that "has bitten this tool twice already." Since correctness
depends on call-site ordering rather than anything local, this is a
landmine for the next edit. `ctx.transform(...)` inside a
`save`/`restore`, or an explicit reset, makes the assumption
self-enforcing.

---

## Part B — Scoring (focus area): confirmed broken

`shapeDistance()` was ported faithfully from
`gesture_trace_spike.py`, and it is genuinely scale-invariant as
advertised. But as a *conducting-gesture* score it rewards the wrong
things. Measured by running the ported function on adversarial traces
(exact same arithmetic as the JS, `n = 50`):

| trace fed to the scorer | 2/4 | 3/4 | 4/4 |
|---|---|---|---|
| the reference itself (perfect) | 100% | 100% | 100% |
| **the reference traced BACKWARDS** | **100%** | **100%** | **100%** |
| **only the first stroke (prep→beat 1)** | **99%** | **95%** | **95%** |
| a plain vertical line | 77% | **95%** | **95%** |
| a circle | 67% | 77% | 82% |
| random 30-point scribble | 74% | 79% | 80% |
| a *different signature's* pattern | 76–78% | 88% | 80–83% |
| correct shape, drifted 25% of pattern height | **57%** | — | — |
| correct shape, drifted 50% | **0%** | — | — |

### S1 (P1). The metric is order-blind: conducting the pattern backwards scores 100%
`shapeDistance` computes average nearest-*point* distance, discarding
sequence entirely. Both paths are already arc-length resampled to 50
points, so ordering information is available and deliberately thrown
away. For a conducting trainer, direction *is* the skill — a reversed
downbeat is the single most important error to catch, and it currently
scores perfect.

**Fix direction:** compare index-wise after resampling (both are already
arc-length uniform, so `d = mean(|trace[i] − ref[i]|)` is order-aware and
no more expensive), or use DTW if some time-warp tolerance is wanted.
Fixing this makes S6's phase offset load-bearing — do them together.

### S2 (P1). One-directional (trace→ref) chamfer distance: incomplete gestures score ~95–99%
Nothing measures ref→trace, so reference geometry the student never
visited is free. Tracing *only the down-stroke* of a 4/4 cross — a
quarter of the pattern — scores **95%**. A plain vertical line scores
**95%** against both 3/4 and 4/4. A student who does nothing but drop
their hand straight down is told they nailed it.

**Fix direction:** make the distance symmetric (add the ref→trace
direction, or take the max — a Hausdorff-style bound). The index-wise fix
in S1 also solves this as a side effect, which is the cheaper path.

### S3 (P1). No translation alignment, and the trace is rescaled about (0,0) rather than its own centroid — so position drift is punished harder than shape error
`shapeDistance` normalizes *scale* but never *translation*. Worse, the
rescale `[x*scale, y*scale]` is about the coordinate origin, so changing
the scale also moves the trace bodily. Result: a **geometrically perfect**
pattern that has drifted 25% of the pattern's height scores **57%**, and
at 50% drift it scores **0%** — while a backwards gesture (S1) scores
100%.

This is the inversion at the heart of "I'm not sure the scoring is
functioning." Hand drift relative to the calibrated origin is the *least*
pedagogically meaningful error (the camera moved, the student stepped, the
calibration hold was slightly off); gesture shape and direction are what
matter.

**Fix direction:** centre both paths on their centroid before scaling
(scale about the centroid, not the origin), then decide deliberately
whether absolute position should be scored at all — and if it should, as a
*separate* reported number, not folded into the shape score.

### S4 (P1). `matchPct` has almost no usable dynamic range
`matchPct = (1 − min(distance, 1)) × 100`. Empirically, garbage inputs
land at **74–88%** and wrong-signature patterns at **76–88%**. The
entire meaningful band is roughly 75%→100%, and everything below ~74% is
unreachable except via the translation artifact in S3. A student cannot
tell a good measure from a bad one, and a session average will hover near
80% regardless of performance.

**Fix direction:** after S1–S3 land, recollect real traces and calibrate
the distance→percentage mapping against measured distributions (what does
a genuinely good measure score, what does a sloppy one score) instead of
the arbitrary `1 − d` linear map. Report the raw distance alongside it
during development so the mapping stays auditable.

### S5 (P2). The student is scored against the vector `REFERENCE` but is *visually* matching the reference image — which R1/R2 currently draw distorted
`scoreMeasure()` calls `shapeDistance(trace, REFERENCE[signature])`. The
on-screen guide is the bitmap. While R1/R2 are outstanding for 2/4, a
student who traces exactly what they see is scored against something
48.8% narrower — penalized for succeeding. Even after R1/R2 are fixed,
this coupling deserves an explicit note in the code: the image transform
and the scoring reference must stay derived from the same numbers.

### S6 (P2). The scored window is offset by one beat from the reference's own start point
`onMeasureBoundary()` fires on the downbeat, and its own comment confirms
"the reset instant IS the bottom of the stroke." So `traceXY` for measure
N runs **beat 1 → beat 2 → … → beat 1 of measure N+1**, while
`REFERENCE[sig]` runs **prep → beat 1 → … → prep**. Same closed loop,
different starting phase.

This is currently invisible *only* because the metric is order-blind
(S1) — for a point-set comparison the start index doesn't matter. The
moment S1 is fixed to be order-aware, every measure will be compared
against a reference rotated by one stroke and scores will collapse.

**Fix direction:** decide the canonical window and make both sides agree
— either roll `REFERENCE` so it starts at beat 1, or capture the scoring
window prep-to-prep (i.e. offset the scored slice from the reset slice).
Must land in the same change as S1.

### S7 (P2). The entire second calibration point is discarded by the scorer
`shapeDistance` rescales the trace to the reference's bounding size, so
gesture *amplitude* is unscored: a cramped 5cm pattern and a full-arm
pattern score identically. That makes `calibScaleFactor` — the whole
reason the BOTTOM calibration hold exists — purely cosmetic (it only
affects how big the trace is drawn).

This may be intended (scale invariance was a deliberate fix for a real
unit-mismatch bug on gyro data, per the comment). But for a *trainer*,
"is your gesture big enough to be visible to an ensemble" is plausibly
something 怡潔's students should be told. Flagging as a product decision,
not asserting a bug: either score amplitude as a second dimension
(calibration already provides the reference range for free), or document
that it is deliberately unscored so the BOTTOM hold isn't mistaken for
affecting the score.

### S8 (P3). Minor scoring cleanups
- `shapeDistance` is O(n²) = 2,500 `hypot` calls per measure; fine at
  once-per-measure, but the index-wise fix in S1 makes it O(n).
- `Math.max(0, ...)` in `matchPct` is dead — `Math.min(distance, 1)`
  already bounds the result at 0.
- `boundingSizeArr` uses `Math.max(...xs)` spread on 50-element arrays —
  fine here, but it will throw on very long paths if ever reused on raw
  traces (spread arg limit).

---

## Part C — Calibration (focus area)

### C1 (P1). No watchdog covers the `calibrating` phase — losing hand tracking mid-hold hangs the run
The watchdog interval in `calibratePoint()` only fires for
`appState === "countdown"`:

```js
if (appState === "countdown" && !forcedStart && ... > CALIBRATION_MAX_WAIT_MS)
```

But the hold's completion check lives inside `onResults()`'s
`if (landmarks)` branch:

```js
} else if (appState === "calibrating") {
  calibSamples.push([x, y]);
  if (calibHoldStart !== null && now - calibHoldStart >= CALIBRATION_HOLD_MS && calibCompletionResolve) {
    calibCompletionResolve();
  }
}
```

If the hand leaves frame (or detection drops) *after* stability is
detected, `landmarks` is null, the completion check never runs, and
`calibCompletionResolve` is never called. `CALIBRATION_MAX_WAIT_MS` can't
help — the state is no longer `"countdown"`. The run hangs indefinitely
on "Calibrating…", with the Stop button as the only escape and no
explanation. Given the calibration hold is now 3s of holding still, a
mid-hold tracking dropout is not an edge case.

**Fix direction:** move the elapsed-hold check out of the
landmark-gated branch (or extend the watchdog to cover `"calibrating"`),
and if the hold ends with too few samples, report *why* rather than
falling through to the generic "no hand detected".

### C2 (P1). `VIDEO_ASPECT` is a hardcoded assumption about a resolution that is only *requested*, never verified
```js
const VIDEO_W = 640, VIDEO_H = 480;
const VIDEO_ASPECT = VIDEO_W / VIDEO_H;
```
…while the constraint is a non-exact hint:
```js
video: { width: VIDEO_W, height: VIDEO_H, frameRate: { ideal: 60, min: 30 } }
```
`video.videoWidth`/`videoHeight` are never read. Browsers treat plain
`width`/`height` as *ideal*, not `exact` — and many webcams are natively
16:9, so a 1280×720 or 640×360 stream is entirely possible, especially
now that a 60fps mode is being requested (high-framerate modes are often
only offered at 16:9).

If that happens the failure is nasty because it *looks* fine: the
skeleton still lines up with the video (MediaPipe's normalized coords and
the stretched `drawImage` distort identically), but `VIDEO_ASPECT` is
wrong by 33% (1.333 vs 1.778), which corrupts `nx` for every sample →
corrupts `calibScaleFactor` → corrupts trace geometry → corrupts every
score. A silent, machine-dependent geometry error sitting upstream of the
whole pipeline, on a page whose scoring is already under suspicion.

**Fix direction:** after `onloadedmetadata`, set
`VIDEO_ASPECT = video.videoWidth / video.videoHeight`, size `mainCanvas`
to the actual stream dimensions, and log the negotiated resolution +
frame rate to the status line so it's visible in every session record.

### C3 (P2). Stability detection compares x and y spreads in mismatched units
```js
const spread = Math.max(max(xs)-min(xs), max(ys)-min(ys));
if (spread < STABILITY_THRESHOLD ...)
```
`x` and `y` are raw normalized frame fractions — the exact mismatch
`VIDEO_ASPECT` exists to correct — so on a 4:3 frame the same physical
wobble registers 33% larger in y than in x. "Stable" is therefore
meaningfully stricter vertically than horizontally, and the threshold
means different physical distances on different cameras (compounding C2).

**Fix direction:** apply the aspect correction before computing the
spread, so `STABILITY_THRESHOLD` is one physical quantity.

### C4 (P2). Calibration samples are mean-averaged with no outlier rejection or stillness verification
```js
const ox = calibSamples.reduce((s,p)=>s+p[0],0)/calibSamples.length;
```
Across a 3-second hold at 30–60fps that's 90–180 samples averaged with a
plain mean. A single mis-detection — the model latching onto the other
hand, a face, or a frame of motion blur — drags the origin arbitrarily,
and the origin is the anchor for the trace, the reference image, *and*
`calibScaleFactor`. Nothing checks that the sampled window was actually
still; stability was verified over the *preceding* 400ms window only, and
the 3s hold that follows is unverified.

**Fix direction:** median instead of mean, plus a post-hoc spread check
on the accepted samples — reject and re-prompt if the hold itself wasn't
still, and report the measured spread.

### C5 (P2). Partial calibration failure leaves inconsistent state
If TOP succeeds and BOTTOM fails, `origin` stays set from this attempt
while `calibScaleFactor` remains at its stale value (only reset at the
top of `start()`). `drawOverlay()`'s `if (origin)` then keeps drawing the
reference image at that origin while `appState === "idle"`, implying a
calibration that didn't complete. Cosmetic today, but it's a mismatched
origin/scale pair sitting in globals.

**Fix direction:** clear `origin` and `calibScaleFactor` together on any
calibration abort.

### C6 (P2). Only a lower bound is checked on the TOP→BOTTOM distance — direction is never validated
```js
if (physDelta < 0.01) { ...fail... }
calibScaleFactor = refDelta / physDelta;
```
`physDelta` is a `hypot`, so it's sign-blind. A user who holds BOTTOM
*above* TOP (misread the prompt, or the two holds got swapped) still
produces a happy-looking `calibScaleFactor`, and every subsequent trace
is mapped with an inverted vertical sense — which would read exactly as
"the scoring is nonsense." There's also no upper bound, so a spurious
BOTTOM sample near the frame edge yields an absurdly small
`calibScaleFactor` and a microscopic trace.

**Fix direction:** require `physDy > 0` (BOTTOM genuinely below TOP) with
a clear error message, and sanity-bound `physDelta` at both ends.

### C7 (P3). Calibration produces no record of what it measured
`calibScaleFactor`, the accepted origin, the sample spread, and whether
the watchdog forced the hold are all discarded — only the per-frame
samples reach `/log`. When calibration "works but the run feels wrong,"
there is nothing to inspect afterwards. Given this tool's whole premise
is "live/interactive tools must persist a record, not rely on the user
describing what they saw" (`server.py`'s own docstring), calibration is
the one thing that doesn't.

**Fix direction:** POST a calibration record (origin, both hold means and
spreads, `physDelta`, `calibScaleFactor`, negotiated camera resolution,
forced-start flag) at run start.

### C8 (P3). A watchdog-forced calibration is indistinguishable from a clean one
When `CALIBRATION_MAX_WAIT_MS` expires, the hold starts anyway on a
demonstrably unstable hand and the result is used with no marker. The
status text says so at the time and then scrolls away. Tag it in the
record (C7) and consider surfacing a "calibration was low-confidence"
note in the session summary.

---

## Part D — Everything else

### D1 (P1). One HTTP POST per frame into a single-threaded server — the likely real cause of "tracking felt slow"
`logSample()` fires a `fetch` POST for **every processed frame** (30–60
per second), and `server.py` runs `socketserver.TCPServer` — *not*
`ThreadingTCPServer`/`ThreadingHTTPServer`. It handles one request at a
time, and it is also the server for the page, the 175–233 KB reference
PNGs, and the MP4 clips. Each `/log` POST additionally does a CSV write +
`flush()`.

Head-of-line blocking here shows up in the browser as stalled `fetch`
promises and main-thread pressure — i.e. exactly the "tracking felt slow"
symptom that the 60fps camera request was chasing. Raising the frame rate
made the POST rate worse.

**Fix direction:** switch to `ThreadingHTTPServer`, and batch samples
client-side (accumulate and POST every ~0.5s, flush on stop) instead of
one request per frame. Worth doing before any further "feels slow"
investigation, since it's cheap and it changes the measurement.

### D2 (P2). The camera is never released
`stopRun()` clears the metronome and state but never stops the
`MediaStreamTrack`s. The camera stays live with its indicator on after
the run ends and after the page goes idle. `video.srcObject`'s truthiness
is also the guard for re-acquisition, so this is load-bearing for the
replay flow — fix both together (stop tracks, clear `srcObject`).

### D3 (P2). Measure boundaries are scheduled with `setTimeout`, so the scored window is not sample-accurate
```js
const delayMs = Math.max(0, (nextBeatTime - audioCtx.currentTime) * 1000);
setTimeout(onMeasureBoundary, delayMs);
```
The click itself is sample-accurate (Web Audio), but the trace
reset/score boundary is a `setTimeout` — tens of ms of jitter under load,
clamped to ≥4ms nesting, and heavily throttled in a background tab. The
boundary that defines *which points belong to which measure* therefore
wobbles relative to the audible downbeat, adding noise to every score.
Minor next to S1–S4, but it's a floor on how accurate scoring can get.

**Fix direction:** compare `audioCtx.currentTime` against
`nextBeatTime` inside the existing rAF `detectLoop` and fire the boundary
there, rather than trusting a timer.

### D4 (P2). The displayed beat number can be a beat ahead of what's audible
`beatInMeasure` is the *scheduler's* write pointer, advanced up to
`SCHEDULE_AHEAD_S` (120ms) before the corresponding click sounds. The
`measure N beat k/sig` readout can therefore show the next beat early. At
80 BPM (750ms/beat) that's ~16% of a beat; at faster tempi it becomes a
full beat off.

**Fix direction:** derive the displayed beat from `audioCtx.currentTime`,
not the scheduler cursor.

### D5 (P3). The placeholder-vector-line branch is now dead code
All three signatures have entries in `REFERENCE_IMAGE_ANCHORS`, so
`hasRealImage` is always true and the entire `else` branch in
`drawOverlay()` — the gray polyline, its vertex dots, and the
"no reference photo yet -- placeholder line" label — is unreachable.
Either delete it or keep it deliberately as the R5 fallback (draw it when
the image fails to load), which would be the better use for it.

### D6 (P3). Stop-during-calibration leaves the previous `start()` continuation alive
`requestStop()` sets `appState = "idle"` immediately, but the awaiting
`start()` continues after `calibratePoint()` resolves and can then write
`statusEl.textContent` / `appState` — potentially after the user has
already pressed Start again. Low-probability, but the fix is a run-token
check (compare a monotonically increasing `runToken` before each state
write) rather than more flags.

### D7 (P3). `REFERENCE` is hand-transcribed into three places — the root cause of R1
The extracted numbers exist in `research/captures/pattern_*_vertices_px.csv`
(pixels) and are then hand-copied into `src/conducting_patterns.py`'s
`_RAW_POINTS` *and* `index.html`'s `REFERENCE`, with the anchor pixels
typed in a third time by hand in `REFERENCE_IMAGE_ANCHORS`. R1 is exactly
what that invites. Note also that `conducting_patterns.py` re-normalizes
by `max_extent` on load while `index.html` assumes the values are already
normalized — currently harmless (all three already have max magnitude
1.0) but two divergent assumptions about the same numbers.

**Fix direction:** have `extract_pattern_from_video.py` emit one
generated JSON/JS artifact containing both the logical points and the
pixel anchors, and have both consumers read it. This closes R1, R3, R4,
and D7 at once.

---

## Suggested fix order for Sonnet

1. **C2** (read the real camera resolution) — it sits upstream of trace
   geometry, calibration scale, *and* scoring; every measurement below is
   suspect until it's pinned down. Cheap.
2. **R1 + R2 + D7** (correct uniform anchors, sourced from the extraction
   CSVs, single generated artifact). This is what makes the on-screen
   reference honest, and it's a precondition for judging any scoring
   change by eye.
3. **S1 + S6 together, then S2 + S3** (order-aware index-wise distance
   with the phase offset resolved; then centroid-relative alignment).
   These must land as one change — fixing ordering without fixing phase
   will collapse all scores, and fixing alignment without ordering leaves
   the backwards-gesture hole open. Re-run the adversarial table in this
   audit afterwards: **reversed must drop well below 100%, partial well
   below 95%, and a drifted-but-correct shape must stay high.**
4. **S4** (recalibrate the percentage mapping) — only meaningful after
   step 3, and it needs fresh real traces, so it gates on a capture
   session.
5. **C1, C4, C6** (calibration hang, robust averaging, direction check) —
   these are what a second user will actually hit first.
6. **D1** (threading server + batched logging) — do before any further
   "feels slow" work; it changes the thing being measured.
7. **C3, C5, C7, C8, R3–R6, D2–D6, S5, S7, S8** — cleanup, with C7
   (calibration record) worth pulling earlier since it makes the next
   round of "it feels off" reports diagnosable instead of anecdotal.

Open product questions to answer rather than assume: whether gesture
**amplitude** should be scored at all (S7), and whether **absolute
position drift** should count against the student or be normalized away
(S3). Both change what the score means, so decide before tuning S4's
mapping.
