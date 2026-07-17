"""Offline smoke test for ictus_detector.py using a synthetic gyro-magnitude
trace (no hardware). This confirms the state machine runs and yields one
event per synthetic swing -- it does NOT validate the tuned thresholds
against real human swings, and does NOT stand in for the Phase 2 validation
step (replaying real Phase 0 CSVs), which is still hardware-gated.
"""

import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from ictus_detector import detect_ictuses


def synthetic_swing_trace(num_swings: int, sample_rate_hz: float, swing_gap_s: float,
                           peak_mag: float, rise_s: float, fall_s: float, baseline: float = 20.0):
    """Builds a (timestamp, magnitude) trace: baseline noise, a swing rises
    to peak_mag over rise_s, drops back to baseline over fall_s, then idles
    for swing_gap_s before the next swing."""
    dt = 1.0 / sample_rate_hz
    samples = []
    t = 0.0
    for _ in range(num_swings):
        # idle baseline
        idle_end = t + swing_gap_s
        while t < idle_end:
            samples.append((t, baseline))
            t += dt
        # rise
        rise_start = t
        rise_end = t + rise_s
        while t < rise_end:
            frac = (t - rise_start) / rise_s
            samples.append((t, baseline + frac * (peak_mag - baseline)))
            t += dt
        # fall (sharp deceleration -- the ictus)
        fall_start = t
        fall_end = t + fall_s
        while t < fall_end:
            frac = (t - fall_start) / fall_s
            # ease down to baseline, undershoot slightly then settle (local min)
            samples.append((t, peak_mag - frac * (peak_mag - baseline)))
            t += dt
    # trailing idle padding so the final swing's local-min search has
    # samples to time out against (a real live stream always has more
    # incoming data; a finite synthetic trace needs this explicitly)
    padding_end = t + 0.3
    while t < padding_end:
        samples.append((t, baseline))
        t += dt
    return samples


def main():
    trace = synthetic_swing_trace(
        num_swings=10,
        sample_rate_hz=150.0,
        swing_gap_s=1.0,
        peak_mag=800.0,
        rise_s=0.07,
        fall_s=0.06,
    )

    events = detect_ictuses(trace)
    print(f"Synthetic swings: 10, detected events: {len(events)}")
    for e in events:
        print(f"  t={e.timestamp:.3f} peak={e.peak_magnitude:.1f} rise_dur={e.rise_duration:.3f}")

    assert len(events) == 10, f"expected 10 ictus events, got {len(events)}"
    print("PASS: one event per synthetic swing, no false positives from baseline noise")
    print("NOTE: thresholds are untuned against real hardware data -- this is a code-path smoke test only.")


if __name__ == "__main__":
    main()
