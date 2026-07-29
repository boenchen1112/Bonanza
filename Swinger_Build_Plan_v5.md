# Build Plan v5 — Conducting Trace Trainer (Web Hand-Tracking)

## 0. Context (read this first — direction confirmed, tool validated)

Build Plan v4's Phase B question — can motion be tracked well enough to
show a student their own conducting shape? — has an answer, just not the
one v4 assumed. Gyro orientation integration was tried first and correctly
diagnosed as wrong: gyro measures rotation, not the hand's actual position
in space, and real hardware data showed tremor at thousands of deg/s with
no stable shape signal underneath. The pivot to webcam hand-position
tracking (`research/live_position_view.py`'s OpenCV color-blob spike, then
`research/hand_tracking_web/`'s MediaPipe HandLandmarker tool) is validated
and working: origin calibration, per-measure trace reset synced to an
audible metronome, and a merged camera+skeleton+trace+reference view all
function, per the latest commit (`3231252`).

**The goal from here is explicit: stop treating this as a research spike
and build it into a complete playable game.** The baseball minigame is
paused, not abandoned — per your instruction, it stays parked in its
current Unity state while this becomes the active line of work.

This document folds in an audit of the current tool (below), the four
requested changes, and what's left after those changes to call this
"complete."

---

## 1. Audit findings

### A1. `primer.md` is stale, contradicting last session's own summary
The file that exists (`D:\VS Code\Swinger\primer.md`, last touched 7/17)
describes only the original 17-bug fix pass — no mention of the Unity
port, v3, v4, or the hand-tracking pivot at all. Last session's summary
said "primer.md is rewritten with full detail for next session," but
`git log -- primer.md` shows one commit, from the initial 7/17 check-in.
Whatever rewrite happened either didn't save, didn't commit, or was
describing intent rather than a completed action. **Fix this in Phase 0**
— a handoff doc that's silently ~2 weeks and 4 build-plan-versions out of
date is worse than none, since it actively misleads whoever reads it next
instead of just being absent.

### A2. Substantial uncommitted work across the repo
`git status` shows modified or untracked files across `UnitySwinger/`,
`src/`, `research/`, and `tools/golden/` — real work (Unity port fixes,
research captures, the hand-tracking tool itself) sitting only in the
working tree. Real risk of loss or of losing track of what's actually
shipped. Should be committed (or explicitly triaged if some of it is
intentionally scratch) before more work piles on top.

### A3. The reference image is a disconnected thumbnail, not coordinate-mapped to the trace
`hand_tracking_web/index.html`'s `#refThumb` is a static, CSS-positioned
120px `<img>` in the top-right corner. It has no relationship to the
canvas's logical coordinate system (`origin`, `scalePxPerUnit`) that the
live trace and vector reference both use — it's decorative, not something
a student could actually trace against. **This is the real blocker for
changes 1–2 below**, and it's a coordinate-calibration problem, not a CSS
tweak: nothing currently maps "where beat 1 / beat 2 / etc. actually are"
inside `reference_2_4.png`'s pixels to the same logical unit space the
`REFERENCE` arrays use.

### A4. Only 2/4 has a real reference image
`REFERENCE_IMAGES = { 2: "assets/reference_2_4.png" }` — 3/4 and 4/4 have
no photographed diagram, only the programmatic vector line. Change 2
("use the image as the reference") can't fully apply to those two
signatures without new source diagrams. This is the same gap primer.md
already flagged ("no verified real diagrams for 3/4 and 4/4 yet") — still
open, not new, but directly blocks part of this session's requested work.

### A5. No scoring exists in the web tool at all
The tool draws a trace and a reference and logs raw samples — no numeric
or tiered feedback of any kind. This matters for change 4, but there's
good news: the scoring method itself is already built and validated (on
synthetic data) in `research/gesture_trace_spike.py`'s `shape_distance()`
+ `resample_path()` — arc-length-resample both paths to N points, then
average nearest-point distance normalized by the reference's bounding
size. This is representation-agnostic (works on any 2D point sequence vs.
a reference), so it ports directly to the position-tracking trace even
though it was built for the (abandoned) gyro-orientation approach.

### A6. Dead gyro-orientation code left undistinguished from the live tool
`research/live_trace_view.py` and `integrate_orientation()` in
`gesture_trace_spike.py` implement the approach that was correctly
diagnosed and abandoned this session. They're not marked as superseded
anywhere in-repo — the same "stale file sitting next to the good one"
pattern the 7/28 Python audit flagged (H5) for `swings.csv`. Someone
picking this up cold could reasonably reach for the wrong tool.
`shape_distance()`/`resample_path()` in the same file are still very much
alive and should be kept; only the gyro-integration path is dead.

### A7. No error handling if the camera/model fails to load
`ensureModelAndCamera()`'s `getUserMedia()` call has no `catch` — if a
user denies camera permission or has no camera, the promise rejects
unhandled and the UI is stuck on "Loading model..." with no explanatory
message. Tolerable for a researcher who knows to check the console; not
acceptable once this is "a complete playable game" someone else might run.

### A8. Minor: calibration UX is two identical-feeling waits
`countdown` (1.2s, no sampling) then `calibrating` (1.2s, sampling) is
2.4s of near-identical on-screen text before a run starts, with no
audible cue distinguishing "get ready" from "hold now, we're measuring."
Not a bug, worth polishing alongside the other UX work in Phase 1.

---

## 2. Phase 0 — Fix the audit findings (cheap, do first)

- Rewrite `primer.md` for real this time — cover the Unity port state,
  v3/v4, the gyro→position pivot, and where `hand_tracking_web/` stands,
  so this doesn't need re-discovering from git log again next session.
- Commit or explicitly triage the uncommitted changes across
  `UnitySwinger/`, `src/`, `research/`, `tools/golden/`.
- Mark `live_trace_view.py` and `integrate_orientation()` clearly as
  superseded/dead in a comment at the top of each, distinct from
  `shape_distance()`/`resample_path()`, which stay alive and get reused in
  Phase 1.4 below.
- Add a `.catch()` around `ensureModelAndCamera()` with a clear on-screen
  message ("camera access needed — check permissions and reload") instead
  of a silent stuck state.

**Exit criterion:** `primer.md` accurately describes the current state;
`git status` is clean or every remaining diff is a deliberate, named
in-progress item; the two dead-code files are clearly labeled; a denied
camera permission produces a visible message instead of a silent hang.

---

## 3. Phase 1 — The four requested changes

### 1 + 2. Transparent, centered reference image; remove the gray vector line

The image becomes the thing the student matches, not a corner thumbnail
next to a separate abstract line. This requires solving A3 first:

- **Calibrate the image's coordinate mapping.** Recommended approach:
  annotate 2–3 known logical points on each reference photo once (e.g.,
  where beat 1's lowest point and beat 2's position actually fall in the
  image's pixel space), and fit a similarity transform (uniform scale +
  translation, rotation if the photo isn't perfectly upright) from image-
  pixel space into the same logical unit space `REFERENCE` already uses.
  A faster, less accurate first pass — fit the image's overall bounding
  box to the vector reference's bounding box — is fine to ship first to
  unblock testing, but say explicitly in the code that it's an
  approximation, not the calibrated version, so it doesn't get mistaken
  for finished.
- Render the image centered on the calibrated origin, drawn into
  `mainCanvas` (not a separate absolutely-positioned `<img>`) with
  `ctx.globalAlpha` around 0.35–0.5 (tune by eye against the live camera
  feed — it needs to read as a guide, not obscure the student's own hand),
  scaled by the same `scalePxPerUnit` the trace uses.
- Remove the gray polyline+dot `drawOverlay()` reference-drawing code for
  any signature that has a real image. **3/4 and 4/4 still don't** (A4) —
  keep the vector line for those two only, clearly labeled as a
  placeholder ("no reference photo yet") rather than silently mixing
  "real diagram" and "programmatic line" with no visual distinction.
  Getting real 3/4/4/4 diagrams closes this gap; that's a Phase 2 item,
  not something to fake here.

### 3. Trace color: gray, not white
Once the vector reference's gray is freed up by the change above, reassign
`traceXY`'s stroke from `#f5f0e0` to gray. Don't reuse the exact
`#aaaaaa` blindly — verify by eye against the live camera feed once the
transparent reference image is also on screen, since three overlapping
gray-ish elements (skeleton dots, reference image, trace) need enough
contrast between them to stay legible; a slightly darker or more saturated
gray for the trace specifically may read better than reusing the old
reference line's exact value.

### 4. Game mode — real scoring, reusing what Phase B already validated
- Port `shape_distance()` and `resample_path()` from
  `gesture_trace_spike.py` into JS directly in `index.html` (not a network
  round-trip to `server.py` per measure — this needs to run in real time,
  in-browser, once per measure-reset).
- Score each completed measure the moment its trace resets (the same
  metronome-driven boundary already firing `onMeasureBoundary()`) against
  `REFERENCE[signature]` (or, once Phase 1.1–2 lands, the calibrated image
  reference), and show it immediately — numeric (a "match" percentage
  derived from the normalized shape-distance) and/or a simple tier if a
  tiered readout tests better than a raw number. Reusing the baseball
  game's Bunt/Line Drive/Home Run vocabulary isn't necessarily right here
  — that implied a single-instant timing judgment; a whole-gesture shape
  score may read better as something like a star rating or "close/off"
  band. Worth trying both against a couple of real users rather than
  guessing.
- Show a running/session summary (average score across measures so far,
  best measure) at minimum — mirrors the baseball prototype's per-swing +
  end-of-round-summary structure, which already proved legible.
- **Open product question, flagged rather than assumed:** does a bad
  score end anything (an out-style fail state), or is this purely
  formative practice with no fail condition? Recommended default for v5:
  **no fail state** — the pedagogical goal is seeing your own shape
  clearly, and a survival mechanic works against that goal by adding
  pressure that has nothing to do with gesture quality. Worth confirming
  this framing once real learners (怡潔's students, per the original
  research topics) are the actual audience, not assuming it forever.

**Exit criterion:** all four changes live in `hand_tracking_web/`; running
a 2/4 session shows the real reference photo centered and semi-transparent
on the live camera feed, a gray trace overlaid on top, and a score after
each measure plus a session summary at the end.

---

## 4. Phase 2 — Continuing steps after the changes

- **Get real 3/4 and 4/4 reference diagrams** — the same input this
  project has needed since the last session, now blocking two concrete
  things (A4's image reference and a complete image-based experience
  across all three signatures) instead of one.
- **Re-frame Build Plan v4's Phase B verdict.** V4 asked "does gyro-only
  orientation with per-beat reset work?" — the honest answer is no, and
  the project correctly moved to a different, working representation
  (webcam hand-position) instead of forcing the original question. Worth
  writing a short closing note to that effect (an update to
  `Phase_B_Spike_Status_2026-07-28.md` or a new dated note) so v4's Phase
  B doesn't sit open forever waiting for an answer to a question that's
  already been superseded by a better one.
- **Decide where this tool actually lives long-term.** Right now it's a
  browser page served by a local Python static server — is that the
  actual shipped product (a webcam-based browser trainer, no Unity, no
  Joy-Con), or does the validated hand-tracking approach eventually need
  porting into Unity (MediaPipe has Unity plugins, but that's its own
  bring-up effort, similar in shape to the Joy-Con Unity port)? v3/v4's
  entire premise was "Unity is the 2.5D platform" — don't let that keep
  being assumed by default now that the actual working prototype is a web
  page. Worth an explicit decision, not an accident of momentum.
- **Robustness/polish pass:** a replay/practice-again flow that doesn't
  require a full page reload, and the countdown/calibration UX split
  from A8.
- **Baseball + Unity Joy-Con port stay parked**, not resumed, until this
  line of work reaches its own "complete" bar — noted here so picking
  that back up later starts from an accurate state, not a guess.

---

## 5. Explicitly out of scope for v5

Baseball minigame development (paused, not abandoned), VR, any revival of
Joy-Con-based input for the trace trainer specifically (webcam
hand-position tracking has replaced it for this feature; Joy-Con remains
relevant only to the parked baseball game), and ML-based gesture scoring
(`shape_distance` stays a simple geometric comparison — a model is a much
bigger, separate question to revisit only if the geometric approach proves
insufficient with real learners).

---

## Summary — phase order

1. **Phase 0** — fix the audit: real `primer.md`, commit/triage the repo,
   label dead code, handle camera-permission failure visibly.
2. **Phase 1** — the four requested changes: calibrated transparent
   centered reference image (real photo where one exists, labeled
   placeholder line where it doesn't), gray trace, and real per-measure
   scoring ported from the already-validated `shape_distance()`.
3. **Phase 2** — close the loop: real 3/4/4/4 diagrams, an honest closing
   note on v4's now-superseded Phase B question, an explicit decision on
   whether this stays a web tool or eventually ports to Unity, and the
   remaining UX polish.

Do not skip Phase 0's `primer.md` rewrite — it's the second time a handoff
doc has gone stale mid-project, and the point of writing it is that the
next session (or the next person) shouldn't have to reconstruct the
current state from `git log` the way this audit just did.
