"""Golden-trace generator for the Unity C# port (Build Plan v3 Phase 2).

Runs real hardware capture CSVs plus a synthetic swing trace through the
Python ictus_detector/scoring pipeline and dumps the expected outputs to
JSON. The C# port replays the identical input CSVs and must match these
outputs: timestamps within ~1e-6s epsilon, tiers exactly. This is the
strongest available check that the port did not silently change units,
float width, or state-machine behavior.

Usage: python tools/generate_golden_traces.py
Writes golden files to tools/golden/*.json
"""

import csv
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from ictus_detector import IctusDetector
from scoring import raw_sharpness, SharpnessReference

ROOT = os.path.join(os.path.dirname(__file__), "..")
SRC = os.path.join(ROOT, "src")
OUT_DIR = os.path.join(os.path.dirname(__file__), "golden")


def load_capture_csv(path):
    """Reads a (timestamp, magnitude) sample list from a real capture CSV."""
    samples = []
    with open(path, newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            samples.append((float(row["timestamp"]), float(row["magnitude"])))
    return samples


def synthetic_swing_trace(num_swings, sample_rate_hz, swing_gap_s,
                           peak_mag, rise_s, fall_s, baseline=20.0):
    """Same generator as tests/test_ictus_detector_smoke.py, kept in sync
    deliberately -- this is the synthetic golden case, not a new one."""
    dt = 1.0 / sample_rate_hz
    samples = []
    t = 0.0
    for _ in range(num_swings):
        idle_end = t + swing_gap_s
        while t < idle_end:
            samples.append((t, baseline))
            t += dt
        rise_start = t
        rise_end = t + rise_s
        while t < rise_end:
            frac = (t - rise_start) / rise_s
            samples.append((t, baseline + frac * (peak_mag - baseline)))
            t += dt
        fall_start = t
        fall_end = t + fall_s
        while t < fall_end:
            frac = (t - fall_start) / fall_s
            samples.append((t, peak_mag - frac * (peak_mag - baseline)))
            t += dt
    padding_end = t + 0.3
    while t < padding_end:
        samples.append((t, baseline))
        t += dt
    return samples


def run_case(name, samples):
    """Runs the full detector + sharpness pipeline over samples and returns
    the golden record for this case."""
    detector = IctusDetector()
    sharpness_ref = SharpnessReference(values=[])
    events_out = []
    for t, mag in samples:
        ev = detector.process_sample(t, mag)
        if ev is not None:
            sharp = raw_sharpness(ev.peak_magnitude, ev.rise_duration, ev.max_derivative)
            bucket = sharpness_ref.bucket(sharp)
            sharpness_ref.add(sharp)
            events_out.append({
                "timestamp": ev.timestamp,
                "peak_magnitude": ev.peak_magnitude,
                "rise_duration": ev.rise_duration,
                "max_derivative": ev.max_derivative,
                "sharpness_raw": sharp,
                "sharpness_bucket": bucket,
            })
    return {
        "case": name,
        "input_sample_count": len(samples),
        "event_count": len(events_out),
        "events": events_out,
    }


def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    cases = []

    synthetic = synthetic_swing_trace(
        num_swings=10, sample_rate_hz=150.0, swing_gap_s=1.0,
        peak_mag=800.0, rise_s=0.07, fall_s=0.06,
    )
    cases.append(("synthetic_10_swings", synthetic))

    for csv_name in ("swings_fresh.csv", "swings_counted.csv"):
        csv_path = os.path.join(SRC, csv_name)
        if os.path.exists(csv_path):
            samples = load_capture_csv(csv_path)
            case_name = os.path.splitext(csv_name)[0]
            cases.append((case_name, samples))
        else:
            print(f"WARNING: {csv_path} not found, skipping")

    for name, samples in cases:
        golden = run_case(name, samples)
        out_path = os.path.join(OUT_DIR, f"{name}.json")
        with open(out_path, "w") as f:
            json.dump(golden, f, indent=2)
        print(f"{name}: {golden['input_sample_count']} samples -> "
              f"{golden['event_count']} events -> {out_path}")


if __name__ == "__main__":
    main()
