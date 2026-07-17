# Phase 3 — Metronome-only milestone: clock-domain verification

**Build Plan v3 Section 4, first milestone.** Exit criterion: "clicks
scheduled on the audio clock, drift logged over a 2+ minute run, and the
clock-domain mapping below verified in isolation."

## Design

CPU clock (`System.Diagnostics.Stopwatch`, wrapped by `ClockSync.CanonicalNow()`)
is the canonical domain — the same choice `BeatSchedule` and the eventual
input-sample stream both live in. `AudioSettings.dspTime` is mapped into that
domain via a single offset (`ClockSync.StartupOffset`), measured once at
startup and never re-applied mid-run; `AudioSource.PlayScheduled` calls
convert canonical beat times to dsp time through that fixed offset
(`ClockSync.ToDspTime`). A periodic re-measurement (`MeasureCurrentOffset`,
every 5s) logs how far the *live* offset has moved from the one actually
used for scheduling, without changing what's used for scheduling — that
delta is the metric this milestone exists to check.

Implementation: `Assets/Scripts/Presentation/ClockSync.cs` +
`MetronomeController.cs`, exercised via `Assets/Scenes/MetronomeTest.unity`.

## A real bug this design caught before it shipped

First implementation measured `StartupOffset` via a synchronous busy-loop
inside `Awake()`. `dspTime` only advances once per audio-buffer callback (a
separate thread) — a tight loop on the main thread can finish before a
single real tick happens, silently degrading "average of N samples" to one
low-quality sample. Symptom: a ~1.2s bogus "offset" in the first few seconds
of a test run that looked exactly like runaway drift. Fixed by spreading
calibration across real frames via a coroutine (`TickCalibration()` called
once per frame from `CalibrateThenSchedule()`, only counting a sample when
`dspTime` has visibly ticked forward since the last call).

## Environment note: Editor Play Mode ticking stall

Two independent test runs inside the Unity Editor's Play Mode stalled
consistently around t≈36s (Update() stopped being called, `editor_state`
reported the transition stuck) — reproducible regardless of window focus or
periodic MCP polling. This appears specific to this automation session
(no interactive desktop backing the Editor's message pump over an extended
unattended run) and unrelated to the clock-domain code: both stalled runs
showed clean, bounded, non-growing drift for every second they *did* run.

**Worked around by building and running a Standalone Windows64 player**
(`Builds/Windows/UnitySwinger.exe -batchmode -nographics`) instead, which
runs its own process loop independent of the Editor. That produced the real,
uninterrupted 2+ minute run below.

## Result (Standalone build, `-batchmode -nographics`)

200 beats (100 measures, 80bpm 2/4) scheduled over 149.3s real time.

- `StartupOffset` (dsp - canonical at startup): 0.929642s
- Live-offset samples every 5s throughout the run: oscillated between
  **-22.648ms and +8.922ms**, no monotonic trend -- the pattern of ups and
  downs is essentially the same shape repeating every ~20s, consistent with
  the audio buffer callback's own quantization jitter rather than clock skew.
- Final drift at t=145.2s: **+0.652ms**. Final measurement at shutdown:
  **-9.054ms**.

## Conclusion

The single-canonical-clock-domain design (Section 4's explicit
recommendation) holds over a real 2+ minute run: the startup-measured
dsp<->canonical offset stays valid to within ~23ms the whole time, well
inside even the Perfect timing window (50ms) let alone Good (150ms). No
re-measurement/re-calibration mid-session appears necessary for a
single-round-length session. This milestone's exit criterion is met;
proceeding to the 2.5D scene and batting loop (build plan Section 4,
second half) on top of this verified foundation.
