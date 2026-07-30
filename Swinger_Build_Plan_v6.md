# Build Plan v6 — Conducting Trace Trainer: Stabilization & Trustworthy Scoring

## 0. Context (read this first)

v5 Phases 0 and 1 landed, and Phase 2 got real reference patterns for all
three signatures out of the source videos (a genuinely good result: 4/4
turned out to be a crossing pattern, structurally different from the
schematic guess it replaced). Then the 2026-07-30 audit
(`reviews/Bug_Audit_2026-07-30_hand_tracking_web.md`) found 30 issues, and
the important ones are not cosmetic:

- **The scoring does not work.** Measured, not inferred: the reference
  traced *backwards* scores 100%. A quarter of the 4/4 pattern scores 95%.
  A plain vertical line scores 95% against 3/4 and 4/4. Random scribble
  floors at ~75%. Meanwhile a geometrically perfect gesture that drifted
  25% of the pattern height scores 57%. The metric punishes almost exactly
  the wrong things.
- **The 2/4 reference image is drawn 48.8% too wide**, from hand-typed
  anchor pixels that disagree with the measured vertices already sitting
  in `research/captures/pattern_24_vertices_px.csv`.
- **Calibration can hang**, and `VIDEO_ASPECT` is a hardcoded guess about
  a camera resolution that is never read back.

So v6 is to v5 what v2 was to v1: a stabilization pass, not new scope.
The difference this time is that v6 also has to leave behind something
v1 through v5 never built for this tool, which is a way to *prove* the
scoring works without squinting at a canvas.

**A note worth keeping in view.** This is the sixth build plan in about
two weeks, and v1's own "v0 prototype-ready" bar was never actually met
before the project moved on. The pivots were right (gyro to webcam
especially), but the pattern is that plans have been outrunning shipped,
verified code. v6 therefore defines "complete" as a person completing a
session, in Phase 3, and everything else exists to serve that.

### Decisions taken (2026-07-30, no longer open)

1. **Scope: stabilization only.** Fix the audit, make the score
   defensible, make the tool survivable by a second human. Nothing else.
2. **What the score means: shape, plus ictus timing.** Gesture *shape and
   direction* are scored. Absolute position drift and overall gesture size
   are normalized away and not scored (they may be *reported*). Added to
   that, and stated explicitly by the user: **the lowest point of the
   downbeat stroke must land on the beat.** That is the ictus, the same
   quantity the parked baseball minigame was built around. See Phase 1.3.
3. **Platform: the browser tool is the product.** Not a prototype for an
   eventual Unity port. Invest in its UI accordingly. Unity stays parked
   for the baseball game only, and v5 Phase 2's open "where does this
   live" question is hereby closed.

### What is being kept, deliberately

The audit's own framing bears repeating so it does not get "fixed" by
accident: the architecture is sound. The chain from calibrated origin
through `calibScaleFactor` and `scalePxPerUnit` to the reference-image
transform is mathematically consistent, and the BOTTOM calibration hold
makes the live trace land at exactly `refDelta` logical units from the
origin, which is precisely where the image anchor puts beat 1. The bugs
below are wrong *inputs* to correct machinery, plus a scoring function
that was never right. Do not restructure the coordinate pipeline.

---

## 1. Phase 0 — Trust the measurement

**Goal:** nothing measured by this tool can be believed until the camera
geometry and the on-screen reference are correct. Everything in Phase 1
is unfalsifiable until this lands. All of it is verifiable with a webcam
and no second person.

### 0.1 Read the actual camera resolution (audit C2)
`VIDEO_ASPECT` is `640/480` hardcoded from a resolution that
`getUserMedia` only *requests*. Plain `width`/`height` constraints are
ideal, not exact, and high-framerate modes (now being requested at 60fps)
are frequently only offered at 16:9. If the stream arrives as 1280x720,
the skeleton still lines up with the video, because the normalized
landmarks and the stretched `drawImage` distort identically, while
`VIDEO_ASPECT` is wrong by 33% and silently corrupts every trace,
`calibScaleFactor`, and score.

- After `onloadedmetadata`, set the aspect from
  `video.videoWidth / video.videoHeight`.
- Size `mainCanvas` to the negotiated stream dimensions rather than a
  hardcoded 640x480.
- Print the negotiated resolution and frame rate into the status line and
  into the session record (0.4), so every run carries evidence of what it
  was actually measuring.

### 0.2 One generated source of truth for patterns and anchors (R1, R2, D7)
The root cause of the 48.8% stretch is that the same numbers are typed by
hand in three places: `research/captures/pattern_*_vertices_px.csv` (the
measured truth), `src/conducting_patterns.py`'s `_RAW_POINTS`, and
`index.html`'s `REFERENCE` plus `REFERENCE_IMAGE_ANCHORS`.

- Have `extract_pattern_from_video.py` emit **one generated artifact**
  (JSON, or a JS module) containing, per signature: the logical keyframe
  points, the pixel anchors, and the source image dimensions. Both
  consumers read it. Nobody transcribes anything.
- **Delete `axisMode` and the `scaleX`/`scaleY` split.** `to_logical()`
  divides both axes by a single `max_extent`, so `REFERENCE` already
  preserves the image's true aspect. The proof is in the data: ground
  truth pixel `dx/dy = 479/904 = 0.5299`, logical `dx/dy = 0.5299`,
  identical to four decimals. Avoiding *rotation* never required
  independent axis scales, and once the anchors are right, 2/4's own x and
  y scales agree anyway. Per-axis fitting is what let the bad anchors hide.
- Fit the uniform scale over **all** extracted vertices (3 to 5 per
  signature), not just prep and beat 1, and **log the residual**. A
  residual check would have caught R1 on the day it was introduced. Warn
  above a threshold.
- Assert the loaded bitmap's `naturalWidth`/`naturalHeight` match the
  recorded dimensions, and surface a visible message on mismatch (R3). The
  currently-unused `imgWidthPx`/`imgHeightPx` fields become real.

### 0.3 Stop failing silently (R5, R6, D5)
- `img.onerror` and a not-yet-decoded image must produce a visible status
  message, not a blank canvas indistinguishable from "no diagram exists."
- Repurpose the now-dead placeholder-vector-line branch (`hasRealImage` is
  always true, so it is unreachable) as exactly that fallback: draw the
  vector line, labeled, when the bitmap fails.
- Delete or archive `assets/reference_2_4.png`, the superseded 157x255
  asset sitting one underscore away from the live `reference_24.png`.
  This is the third instance of the stale-file-beside-the-good-one pattern
  in this repo (`swings.csv`, `primer.md.lnk`).
- Replace `ctx.setTransform` in `drawReferenceImage()` with a
  `save`/`transform`/`restore`, so correctness stops depending on call-site
  ordering that has already caused two mirror-direction bugs.

### 0.4 A calibration and session record (C7)
`server.py`'s own docstring says live tools must persist a record rather
than rely on the user describing what they saw. Calibration is the one
thing that does not. POST a record at run start containing: both hold
means and their sample spreads, the accepted origin, `physDelta`,
`calibScaleFactor`, the negotiated camera resolution and frame rate, and
whether the watchdog forced the hold (C8). Without this, the next "it
feels off" report is anecdote again.

**Exit criterion:** the reference image for all three signatures overlays
at the correct size and aspect, verified against the extraction overlays;
the negotiated camera resolution is visible on screen and in the record;
and a deliberately broken anchor or a missing image produces a visible
error rather than silence.

---

## 2. Phase 1 — Make the score mean something

**Goal:** a number a student can trust and a teacher can defend. This is
the substance of v6.

### 1.1 The regression harness, built before the fix (new; the highest-leverage item here)
There is currently no way to verify a scoring change except by eye, which
is how the current metric shipped. The adversarial table in the audit is
already the test suite; check it in.

- Extract `resamplePath()`, `shapeDistance()`, and the scoring entry point
  out of `index.html` into a plain ES module (`scoring.js`), imported both
  by the page and by a Node test file. No behavioural change in this step.
- Write the tests **against the current implementation first**, asserting
  the *desired* properties, and watch them fail. Concretely, per
  signature: exact reference scores high; reversed scores low; a single
  stroke scores low; a plain vertical line scores low; a circle scores
  low; another signature's pattern scores low; a correct shape drifted by
  25% of pattern height stays high; a correct shape at half size stays
  high.
- Assert **thresholds and orderings**, not exact numbers, so the tests
  survive tuning. Runnable via `node --test`, no browser, no camera.

This mirrors what the Python side learned with golden traces in v3 Phase
2, and it is what makes the rest of this phase checkable rather than
hopeful.

### 1.2 Fix the shape metric (S1 + S6 together, then S2 + S3)
These are not independent and must not be shipped separately.

- **Order awareness (S1) and phase alignment (S6) are one change.** Both
  paths are already arc-length resampled to 50 points, so ordering
  information exists and is being deliberately discarded. Compare
  index-wise (`mean(|trace[i] - ref[i]|)`), which is order-aware, cheaper
  (O(n) instead of O(n^2)), and fixes the one-directional chamfer hole
  (S2) as a side effect. **But** the scored window currently runs beat 1
  to beat 1 while `REFERENCE` runs prep to prep, a one-stroke phase
  offset that is invisible only because the current metric is order-blind.
  Fix ordering without fixing phase and every score collapses. Decide the
  canonical window (rolling `REFERENCE` to start at beat 1 is the smaller
  change) and make both sides agree in the same commit.
- **Translation alignment (S3).** Centre both paths on their centroid
  before rescaling, and rescale about the centroid rather than about
  (0,0). Per decision 2, drift is normalized away and not scored. It may
  be *reported* as a separate informational number.
- Per decision 2, gesture amplitude also stays unscored. Note the
  consequence in the code so it is not mistaken for an oversight:
  `calibScaleFactor`, and therefore the entire BOTTOM calibration hold,
  affects only how large the trace is *drawn*, not the score.

### 1.3 Ictus timing (per decision 2: "the lowest point should be on timing")
This is the one genuinely additive piece of v6, and it is small in
concept but drags real dependencies with it. Do not treat it as free.

- **`traceXY` needs timestamps.** It currently stores `[nx, ny]` with no
  time, so there is nothing to compare against a beat. Store
  `[nx, ny, t]`.
- **The ictus is the deepest minimum in `ny` within the measure**, not
  merely the last local minimum. A hand that dips twice must resolve to
  the deeper dip. Record how sharp that minimum is while you are there:
  that is the same "ictus sharpness" quantity the baseball prototype
  scored, and it is free to capture now and decide about later.
- **D3 stops being a P2 and becomes a blocker.** `onMeasureBoundary` is
  scheduled with `setTimeout`, which is subject to tens of milliseconds of
  jitter and is throttled in background tabs. Once the boundary *is* the
  timing datum, that jitter is measurement error in the score itself.
  Drive the boundary from `audioCtx.currentTime` inside the existing rAF
  `detectLoop` instead.
- **Frame timestamps are not detection timestamps.** `detectLoop` polls
  `video.currentTime !== lastVideoTime` inside rAF and stamps samples with
  `performance.now()` at *detection* time, which is later than when the
  frame was captured, by a variable amount. This is the same clock-domain
  lesson v1 Phase 0 learned the hard way with `perf_counter`. Use
  `HTMLVideoElement.requestVideoFrameCallback()`, whose metadata provides
  a real frame `mediaTime` and `expectedDisplayTime`, rather than
  inferring frame arrival from a polling loop.
- **Audio output latency needs a calibration offset, and this repo already
  has the scar tissue.** The click the student *hears* is later than the
  time it was scheduled at. This is exactly the problem that produced
  `DEFAULT_OFFSET_S` in v1 and the unresolved latency report that v3 Phase
  0 existed to close out. Reuse those lessons rather than rediscovering
  them: read `audioCtx.outputLatency` and `audioCtx.baseLatency` as a
  starting point, keep the offset as a single named constant, and follow
  the v1 rule that it is never auto-calibrated mid-session and never
  defaults to zero. If a manual tap-to-the-click calibration is needed
  later, that is a v7 item, not this one.
- **Report timing separately from shape.** Two numbers, not one blended
  score. Blending them is how the baseball prototype's audit found people
  conflating dimensions, and shape and timing fail for different reasons a
  student needs to distinguish.
- Fix D4 while in here: the displayed beat number comes from the
  scheduler's write pointer, which runs up to `SCHEDULE_AHEAD_S` (120ms)
  ahead of what is audible.

### 1.4 Calibrate the percentage mapping against real traces (S4)
`matchPct = (1 - min(d, 1)) * 100` produces a band where garbage lands at
74 to 88% and nothing meaningful exists below ~74%. This cannot be tuned
from first principles, and it cannot be tuned at all until 1.2 and 1.3
land.

- **Run a capture session.** There are three tiny CSVs in `logs/` and no
  recorded trace from anyone conducting competently. Record several
  measures per signature: clean, deliberately sloppy, deliberately
  reversed, deliberately partial. Store them as fixtures under
  `research/hand_tracking_web/fixtures/`.
- Those fixtures then serve double duty as the S4 calibration data *and*
  as real (rather than synthetic) inputs to the 1.1 harness.
- Map distance to percentage against the measured distributions. Keep the
  raw distance visible alongside the percentage during development so the
  mapping stays auditable.

**Exit criterion:** the 1.1 harness passes on every property, including
against real recorded fixtures; a reversed gesture, a partial gesture, and
a wrong-signature gesture all score clearly poorly; a correct gesture that
drifted or was performed small still scores well; and a downbeat conducted
deliberately late reports a late ictus with a plausible millisecond value.

---

## 3. Phase 2 — Survive a second human

**Goal:** the failures a first-time user hits in the first five minutes.
None of this is interesting, and all of it is between the tool and
Phase 3.

- **C1, the calibration hang.** The hold-completion check lives inside
  `onResults()`'s `if (landmarks)` branch, while the watchdog only covers
  `appState === "countdown"`. Lose tracking mid-hold and the promise never
  resolves: the run hangs on "Calibrating..." with the Stop button as the
  only escape and no explanation. With a 3-second hold, a mid-hold dropout
  is not an edge case. Move the elapsed check out of the landmark-gated
  branch, and if a hold ends with too few samples, say *why*.
- **C4, robust origin.** 90 to 180 samples are mean-averaged with no
  outlier rejection, and the origin anchors the trace, the reference
  image, and `calibScaleFactor`. One mis-detection (the other hand, a
  face, motion blur) drags all three. Use a median, and verify the hold's
  own spread rather than trusting the preceding 400ms stability window.
- **C6, direction validation.** `physDelta` is a `hypot` and therefore
  sign-blind, so holding BOTTOM *above* TOP still yields a happy-looking
  `calibScaleFactor` with an inverted vertical sense, which would read to
  a user as "the scoring is nonsense." Require `physDy > 0` with a clear
  message, and bound `physDelta` at both ends.
- **C3**, apply the aspect correction before the stability spread test, so
  `STABILITY_THRESHOLD` means one physical quantity rather than being 33%
  stricter vertically.
- **C5**, clear `origin` and `calibScaleFactor` together on any abort.
- **D1, the logging server.** `logSample()` POSTs once per *frame* (30 to
  60 per second) into `socketserver.TCPServer`, which is single-threaded
  and is also serving the page, the 175 to 233 KB reference PNGs, and the
  MP4 clips. Head-of-line blocking presents in the browser as stalled
  fetches and main-thread pressure, which is very plausibly the real cause
  of the "tracking felt slow" report that the 60fps camera request was
  chasing (and which that request made worse). Switch to
  `ThreadingHTTPServer` and batch samples client-side, flushing every
  ~0.5s and on stop. Do this before any further performance investigation,
  because it changes the thing being measured.
- **D2**, release the camera. `stopRun()` never stops the
  `MediaStreamTrack`s, so the camera indicator stays lit after a run ends.
  `video.srcObject`'s truthiness also gates re-acquisition, so fix both
  together.
- **D6**, a run token compared before each state write, so a stop during
  calibration cannot have its stale continuation overwrite the state of a
  run the user has already restarted.

**Exit criterion:** a full session survives a denied camera permission, a
hand leaving frame mid-calibration, a swapped TOP/BOTTOM hold, a mid-run
Stop, and an immediate restart, each with a clear on-screen explanation
and no reload required.

---

## 4. Phase 3 — The completion bar

Per Section 0, "complete" is a person completing a session, not a feature
count. This phase is the definition of done for v6 and for the trainer as
a v1 product.

- Daniel runs a full session, all three signatures, using only the
  on-screen instructions.
- **怡潔, or one of her students, runs a full session unattended**, with no
  explanation beyond what the page says. This is the same bar v1 Section 0
  set for the baseball prototype ("a non-technical observer could look at
  the summary and say yes, that reflects how they did"), and it is the bar
  that was never actually reached before the pivots.
- The specific question to ask afterwards, and to write down verbatim
  rather than paraphrase: **does the score match what they thought they
  did?** A teacher disagreeing with a score is the only real validation
  available, and it is worth more than any threshold tuned offline.
- Write the outcome to `reviews/` as a dated note, in the same discipline
  as the bug audits.

**Exit criterion for v6 as a whole:** a music teacher who has never seen
the tool completes a session and agrees with the scores it gave.

---

## 5. Explicitly deferred to v7 (do not build now)

- **Full tempo and rubato tracking**, and the conductor-leads-audio
  inversion that v1 Section 0 flagged as the long-term direction. v6
  scores ictus *placement* against a fixed metronome only. Adding tempo
  tracking on top of a shape metric that was broken until this version
  repeats the compounding-bugs pattern the 7/28 audit called out.
- A manual tap-to-the-click **audio latency calibration UI**. v6 uses
  `outputLatency`/`baseLatency` plus a constant.
- Scoring gesture **amplitude** (decision 2 excludes it; calibration
  already measures the student's range, so it stays cheap to add later).
- **Expressive and left-hand gesture** work, dynamics, cueing.
- **Multi-user, accounts, progress over time.**
- A **Unity port of the trainer** (decision 3 closes this: the browser is
  the product).
- **ML-based gesture scoring.** The geometric approach has not yet been
  given a fair test, since it has never been correctly implemented.
- The **baseball minigame and its Unity/Joy-Con port** stay parked, not
  abandoned, exactly as v5 left them.

---

## Summary — phase order

1. **Phase 0, trust the measurement.** Real camera resolution; one
   generated source of truth for patterns and anchors with a uniform scale
   and a logged residual; visible failures; a calibration record. Nothing
   downstream is falsifiable until this lands.
2. **Phase 1, make the score mean something.** Build the regression
   harness *first*, then fix ordering and phase together, then centroid
   alignment, then add ictus timing (with its dependencies: rAF-driven
   boundary, real frame timestamps, an audio latency offset), then
   calibrate the percentage mapping against a real capture session.
3. **Phase 2, survive a second human.** The calibration hang, robust
   origin, direction validation, the threading server, camera release.
4. **Phase 3, the completion bar.** 怡潔 or a student completes a session
   unattended and agrees with the score.

Two sequencing rules that are not negotiable, because getting them wrong
wastes the work: **the harness (1.1) is built before the scoring fix**,
not after, and **ordering and phase (1.2) ship in one commit**, because
either alone produces worse scores than the broken version does today.
