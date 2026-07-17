"""Offline check for Build Plan Section 3's required gate: detected ictus
timestamp must not systematically shift with swing intensity. Real
validation still needs actual soft/medium/hard human swings (Phase 0/2,
hardware-gated); this is a synthetic stand-in that at least exercises the
same measurement the plan calls for.

Each synthetic swing has an identical rise/fall *shape* (same durations)
but a different peak magnitude, so the rise starts at the same relative
sample. If detection is truly intensity-invariant, the ictus timestamp
should land at the same offset-from-rise-start for all three.
"""

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from ictus_detector import IctusDetector


def build_single_swing(sample_rate_hz, peak_mag, rise_s, fall_s, baseline=20.0,
                        pre_idle_s=0.3, post_idle_s=0.3):
    dt = 1.0 / sample_rate_hz
    samples = []
    t = 0.0
    while t < pre_idle_s:
        samples.append((t, baseline))
        t += dt
    rise_start = t
    while t < rise_start + rise_s:
        frac = (t - rise_start) / rise_s
        samples.append((t, baseline + frac * (peak_mag - baseline)))
        t += dt
    fall_start = t
    while t < fall_start + fall_s:
        frac = (t - fall_start) / fall_s
        samples.append((t, peak_mag - frac * (peak_mag - baseline)))
        t += dt
    post_idle_end = t + post_idle_s
    while t < post_idle_end:
        samples.append((t, baseline))
        t += dt
    return samples, rise_start


def main():
    sample_rate = 150.0
    # steep enough that even the softest peak still clears RISE_RATE_THRESHOLD
    rise_s, fall_s = 0.03, 0.06
    intensities = {"soft": 300.0, "medium": 550.0, "hard": 800.0}

    offsets_from_rise_start = {}
    for label, peak in intensities.items():
        samples, rise_start = build_single_swing(sample_rate, peak, rise_s, fall_s)
        detector = IctusDetector()
        events = []
        for t, mag in samples:
            ev = detector.process_sample(t, mag)
            if ev is not None:
                events.append(ev)
        assert len(events) == 1, f"{label}: expected 1 event, got {len(events)}"
        offsets_from_rise_start[label] = events[0].timestamp - rise_start
        print(f"{label}: peak={peak} ictus_offset_from_rise_start={offsets_from_rise_start[label]*1000:.2f}ms")

    values = list(offsets_from_rise_start.values())
    spread_ms = (max(values) - min(values)) * 1000
    print(f"Spread across intensities: {spread_ms:.2f}ms")
    # Loose tolerance -- this is a synthetic smoke check, not the real
    # validation the plan requires against actual human swing data.
    assert spread_ms < 15.0, (
        f"detected timestamp shifts {spread_ms:.2f}ms with intensity -- "
        "this is exactly the coupling Section 3 warns against"
    )
    print("PASS: detected timestamp offset is stable across synthetic intensities")
    print("NOTE: still needs real soft/medium/hard human-swing validation per Section 3 (hardware-gated).")


if __name__ == "__main__":
    main()
