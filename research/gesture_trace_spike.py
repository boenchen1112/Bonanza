"""Build Plan v4 Phase B research spike: is a usable conducting-gesture
trace recoverable from gyro-only orientation integration, reset at each
detected downbeat?

STATUS (2026-07-28): tooling only, no verdict yet. This script is complete
and self-tested against a synthetic trace (see `_self_test()`), but Phase
B's actual exit criterion -- a written verdict in `reviews/` with
per-repetition shape-distance numbers from *real* captured conducting
gestures -- requires a human physically swinging a paired right Joy-Con
through real 2/4/3/4/4/4 patterns. That capture step cannot be done from
here. Do not treat the synthetic self-test's numbers as a Phase B verdict;
they only prove this script's own arithmetic is self-consistent.

To actually run Phase B once real captures exist:
    python research/gesture_trace_spike.py --capture path/to/capture.csv \\
        --downbeats 1.0,2.5,4.0,... --signature 4

`capture.csv` must have the same columns joycon_stream.py's CLI writes:
timestamp, gx, gy, gz, magnitude. `--downbeats` is a comma-separated list
of canonical-clock timestamps for each detected downbeat (reset points) --
in the real pipeline these come from IctusDetector events, matched the same
way calibration.py already matches practice-measure ictuses to expected
beat-1 times.
"""

import argparse
import csv
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from conducting_patterns import CANONICAL_PATTERNS, resample_path  # noqa: E402


def integrate_orientation(
    samples: list[tuple[float, float, float, float]],
    reset_times: list[float],
    axis_x: str = "gx",
    axis_y: str = "gy",
) -> list[tuple[float, float, float]]:
    """Gyro-only orientation integration (Euler, no accelerometer fusion),
    reset to (0, 0) at each time in reset_times (the detected-downbeat
    reset Phase B's spike plan calls for, bounding drift to within one
    measure instead of the whole session).

    samples: list of (t, gx, gy, gz) in the canonical clock domain, gyro in
    deg/s (pyjoycon convention). axis_x/axis_y select which two gyro axes
    become the 2D trace's x/y -- deliberately parameterized rather than
    hardcoded: which two axes correspond to "pitch"/"yaw" for the real
    conducting grip is exactly the kind of thing Phase B needs real data to
    confirm, not something to assume here (Build Plan v4 Phase B, "roll/
    pitch depending on grip").

    Returns (t, angle_x_deg, angle_y_deg) triples -- the live trace.
    """
    axis_idx = {"gx": 1, "gy": 2, "gz": 3}
    ix, iy = axis_idx[axis_x], axis_idx[axis_y]

    reset_times_sorted = sorted(reset_times)
    reset_ptr = 0
    angle_x = angle_y = 0.0
    prev_t = None
    trace = []

    for row in samples:
        t = row[0]
        while reset_ptr < len(reset_times_sorted) and t >= reset_times_sorted[reset_ptr]:
            angle_x = angle_y = 0.0
            prev_t = t  # don't integrate the gap that straddles the reset
            reset_ptr += 1

        if prev_t is not None:
            dt = t - prev_t
            if dt > 0:
                angle_x += row[ix] * dt
                angle_y += row[iy] * dt
        prev_t = t
        trace.append((t, angle_x, angle_y))

    return trace


def shape_distance(trace_points: list[tuple[float, float]], reference_points: list[tuple[float, float]], n: int = 50) -> float:
    """Phase B's scoring method: resample both paths to n points by arc
    length, then average point-to-nearest-reference-point distance,
    normalized by the reference pattern's own bounding size so the number
    is scale- and sample-count-independent."""
    resampled_trace = resample_path(trace_points, n)
    resampled_ref = resample_path(reference_points, n)

    ref_xs = [p[0] for p in resampled_ref]
    ref_ys = [p[1] for p in resampled_ref]
    ref_size = max(max(ref_xs) - min(ref_xs), max(ref_ys) - min(ref_ys))
    if ref_size == 0:
        ref_size = 1.0

    total = 0.0
    for tx, ty in resampled_trace:
        nearest = min(math.dist((tx, ty), rp) for rp in resampled_ref)
        total += nearest
    return (total / len(resampled_trace)) / ref_size


def score_repetition(
    samples: list[tuple[float, float, float, float]],
    measure_start: float,
    measure_end: float,
    reset_times: list[float],
    beats_per_measure: int,
    axis_x: str = "gx",
    axis_y: str = "gy",
) -> tuple[float, list[tuple[float, float]]]:
    """Integrates one measure's worth of samples and scores it against the
    canonical pattern for beats_per_measure. Returns (score, trace_xy)."""
    trace = integrate_orientation(samples, reset_times, axis_x, axis_y)
    trace_xy = [(x, y) for t, x, y in trace if measure_start <= t <= measure_end]
    if len(trace_xy) < 2:
        return float("nan"), trace_xy
    reference = CANONICAL_PATTERNS[beats_per_measure].points
    return shape_distance(trace_xy, reference), trace_xy


def plot_repetition(trace_xy: list[tuple[float, float]], reference_points: list[tuple[float, float]], score: float, out_path: str) -> None:
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    fig, ax = plt.subplots(figsize=(4, 4))
    tx = [p[0] for p in trace_xy]
    ty = [p[1] for p in trace_xy]
    rx = [p[0] for p in reference_points]
    ry = [p[1] for p in reference_points]
    ax.plot(rx, ry, "--o", color="gray", label="reference", alpha=0.6)
    ax.plot(tx, ty, "-", color="tab:red", label="recovered trace")
    ax.set_aspect("equal")
    ax.set_title(f"shape_distance={score:.3f}")
    ax.legend()
    fig.tight_layout()
    fig.savefig(out_path, dpi=120)
    plt.close(fig)


def _load_capture_csv(path: str) -> list[tuple[float, float, float, float]]:
    rows = []
    with open(path, newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            rows.append((float(row["timestamp"]), float(row["gx"]), float(row["gy"]), float(row["gz"])))
    return rows


def _self_test() -> None:
    """Synthetic sanity check ONLY -- proves the integration/scoring
    arithmetic is internally consistent, not that this representation
    works on a real conducting gesture. See module docstring."""
    print("Running synthetic self-test (NOT a Phase B verdict -- see module docstring)...")

    reference = CANONICAL_PATTERNS[4].points
    # Build a synthetic gyro trace whose integral traces the 4/4 reference
    # near-exactly: constant angular velocity per segment for a fixed
    # duration, derived directly from the reference's own deltas.
    dt = 0.01
    seg_duration = 0.3
    samples = []
    t = 0.0
    for i in range(len(reference) - 1):
        x0, y0 = reference[i]
        x1, y1 = reference[i + 1]
        vx = (x1 - x0) / seg_duration
        vy = (y1 - y0) / seg_duration
        steps = int(seg_duration / dt)
        for _ in range(steps):
            samples.append((t, vx, vy, 0.0))
            t += dt

    score, trace_xy = score_repetition(
        samples, measure_start=0.0, measure_end=t, reset_times=[0.0], beats_per_measure=4
    )
    print(f"Synthetic near-perfect trace shape_distance = {score:.4f} (expect close to 0)")
    assert score < 0.05, f"self-test failed: expected near-zero distance, got {score}"

    out_dir = os.path.join(os.path.dirname(__file__), "self_test_plots")
    os.makedirs(out_dir, exist_ok=True)
    plot_repetition(trace_xy, reference, score, os.path.join(out_dir, "synthetic_4beat.png"))
    print(f"PASS. Plot written to {out_dir}/synthetic_4beat.png")


def main():
    parser = argparse.ArgumentParser(description="Phase B gesture-trace research spike.")
    parser.add_argument("--capture", type=str, default=None, help="CSV path (joycon_stream.py format)")
    parser.add_argument("--downbeats", type=str, default=None, help="Comma-separated downbeat reset timestamps")
    parser.add_argument("--signature", type=int, default=4, choices=[2, 3, 4])
    parser.add_argument("--axis-x", type=str, default="gx")
    parser.add_argument("--axis-y", type=str, default="gy")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test or not args.capture:
        _self_test()
        return

    samples = _load_capture_csv(args.capture)
    reset_times = [float(x) for x in args.downbeats.split(",")] if args.downbeats else [samples[0][0]]
    score, trace_xy = score_repetition(
        samples,
        measure_start=samples[0][0],
        measure_end=samples[-1][0],
        reset_times=reset_times,
        beats_per_measure=args.signature,
        axis_x=args.axis_x,
        axis_y=args.axis_y,
    )
    print(f"shape_distance = {score:.4f}")
    reference = CANONICAL_PATTERNS[args.signature].points
    out_path = os.path.splitext(args.capture)[0] + "_trace.png"
    plot_repetition(trace_xy, reference, score, out_path)
    print(f"Plot written to {out_path}")


if __name__ == "__main__":
    main()
