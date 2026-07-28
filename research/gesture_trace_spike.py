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
    python research/gesture_trace_spike.py --capture path/to/capture.csv --signature 4

`capture.csv` must have the same columns joycon_stream.py's CLI writes:
timestamp, gx, gy, gz, magnitude. Downbeats are auto-detected by default,
reusing the exact same IctusDetector already validated for the baseball
minigame (applied to the capture's own magnitude column) -- no manual
timestamp-hunting needed. Pass --downbeats 1.0,2.5,4.0,... to override with
your own timestamps instead (e.g. from a metronome-driven capture where you
already know the expected beat times).

With auto-detected downbeats, the capture is automatically split into one
repetition per inter-downbeat measure and each is scored separately --
matching Phase B's "report this number per repetition, not just a plot".
"""

import argparse
import csv
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from conducting_patterns import CANONICAL_PATTERNS, resample_path  # noqa: E402
from ictus_detector import IctusDetector  # noqa: E402
from joycon_stream import combined_magnitude  # noqa: E402


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


def _bounding_size(points: list[tuple[float, float]]) -> float:
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    return max(max(xs) - min(xs), max(ys) - min(ys))


def shape_distance(trace_points: list[tuple[float, float]], reference_points: list[tuple[float, float]], n: int = 50) -> float:
    """Phase B's scoring method: resample both paths to n points by arc
    length, then average point-to-nearest-reference-point distance,
    normalized so the number is scale- and sample-count-independent.

    Bug found from a real capture (2026-07-28): the canonical reference is
    normalized to a max bounding-box extent of 1.0
    (conducting_patterns.py), but a real integrated gyro trace comes out in
    real degrees -- hundreds, not ~1. An earlier version of this function
    only divided the *distance sum* by the reference's bounding size,
    without ever rescaling the trace itself, so the score was dominated by
    that scale mismatch (shape_distance in the hundreds) rather than shape
    (dis)similarity -- confirmed by looking at the plot: the reference dot
    was invisibly tiny next to a trace hundreds of units across. Both
    paths are now rescaled to the same bounding size before comparing, so
    this actually measures shape, not units.
    """
    resampled_trace = resample_path(trace_points, n)
    resampled_ref = resample_path(reference_points, n)

    ref_size = _bounding_size(resampled_ref)
    if ref_size == 0:
        ref_size = 1.0

    trace_size = _bounding_size(resampled_trace)
    if trace_size == 0:
        trace_size = 1.0
    scale = ref_size / trace_size
    resampled_trace = [(x * scale, y * scale) for x, y in resampled_trace]

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


def detect_downbeats(samples: list[tuple[float, float, float, float]]) -> list[float]:
    """Auto-detects downbeat (ictus) timestamps from a raw capture by
    reusing IctusDetector against the capture's own gyro magnitude --
    the same detection already validated for the baseball minigame,
    applied here to a free-form conducting capture instead of a
    metronome-scheduled swing. Removes the manual timestamp-hunting step
    from running a real Phase B capture session."""
    detector = IctusDetector()
    downbeats = []
    for t, gx, gy, gz in samples:
        mag = combined_magnitude(gx, gy, gz)
        ev = detector.process_sample(t, mag)
        if ev is not None:
            downbeats.append(ev.timestamp)
    return downbeats


def score_all_repetitions(
    samples: list[tuple[float, float, float, float]],
    downbeats: list[float],
    beats_per_measure: int,
    axis_x: str = "gx",
    axis_y: str = "gy",
) -> list[tuple[float, list[tuple[float, float]]]]:
    """Splits a capture into one repetition per beats_per_measure
    consecutive downbeats and scores each independently -- Phase B's "per
    repetition, not just a plot" requirement. Returns [(score, trace_xy), ...],
    one entry per complete measure found."""
    results = []
    for i in range(0, len(downbeats) - beats_per_measure, beats_per_measure):
        start = downbeats[i]
        end = downbeats[i + beats_per_measure]
        score, trace_xy = score_repetition(
            samples, measure_start=start, measure_end=end,
            reset_times=downbeats, beats_per_measure=beats_per_measure,
            axis_x=axis_x, axis_y=axis_y,
        )
        results.append((score, trace_xy))
    return results


def plot_repetition(trace_xy: list[tuple[float, float]], reference_points: list[tuple[float, float]], score: float, out_path: str) -> None:
    """Plots the trace rescaled to the reference's own bounding size (same
    rescaling shape_distance() uses internally) so the two are actually
    overlaid at comparable scale -- plotting raw units made the reference
    an invisible dot next to a trace hundreds of units across and the plot
    unreadable (found from a real capture, 2026-07-28)."""
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    ref_size = _bounding_size(reference_points) or 1.0
    trace_size = _bounding_size(trace_xy) or 1.0
    scale = ref_size / trace_size

    fig, ax = plt.subplots(figsize=(4.5, 4.5))
    tx = [p[0] * scale for p in trace_xy]
    ty = [p[1] * scale for p in trace_xy]
    rx = [p[0] for p in reference_points]
    ry = [p[1] for p in reference_points]
    ax.plot(rx, ry, "--o", color="gray", linewidth=2, markersize=8, label="reference (target shape)", alpha=0.7, zorder=3)
    ax.plot(tx, ty, "-", color="tab:red", label=f"your trace (rescaled {scale:.4g}x)", zorder=2)
    ax.plot(tx[0], ty[0], "^", color="darkred", markersize=8, zorder=4)
    ax.set_aspect("equal")
    ax.set_title(f"shape_distance={score:.3f}\n(0=identical shape, lower=better)", fontsize=10)
    ax.legend(fontsize=8)
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
    if args.downbeats:
        downbeats = [float(x) for x in args.downbeats.split(",")]
    else:
        downbeats = detect_downbeats(samples)
        print(f"Auto-detected {len(downbeats)} downbeats.")

    reference = CANONICAL_PATTERNS[args.signature].points
    base_path = os.path.splitext(args.capture)[0]

    repetitions = score_all_repetitions(samples, downbeats, args.signature, args.axis_x, args.axis_y)
    if not repetitions:
        print(f"Not enough downbeats ({len(downbeats)}) for even one {args.signature}-beat repetition.")
        return

    scores = [s for s, _ in repetitions if not math.isnan(s)]
    for i, (score, trace_xy) in enumerate(repetitions):
        print(f"repetition {i}: shape_distance = {score:.4f}")
        plot_repetition(trace_xy, reference, score, f"{base_path}_rep{i}_trace.png")
    if scores:
        mean = sum(scores) / len(scores)
        variance = sum((s - mean) ** 2 for s in scores) / len(scores) if len(scores) > 1 else 0.0
        print(f"\n{len(scores)} scored repetitions: mean={mean:.4f} stdev={variance ** 0.5:.4f}")
    print(f"Plots written to {base_path}_rep*_trace.png")


if __name__ == "__main__":
    main()
