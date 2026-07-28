"""Build Plan v4 Phase B: live real-time gesture trace viewer.

Diagnostic tool, not the Phase D Unity feature -- shows the same
quaternion-integrated orientation trace gesture_trace_spike.py computes
offline, but live, updating on screen as you swing, overlaid against the
canonical reference pattern for the chosen time signature. Built because
offline analysis with IctusDetector-based downbeat segmentation is
unreliable for a gentle, continuous conducting motion (IctusDetector is
tuned for a baseball bat's sharp ballistic swing) -- watching the trace
draw live sidesteps that entirely: the reset schedule here is driven by
wall-clock time from --bpm, not by detected swings.

Usage:
    python research/live_trace_view.py --bpm 80 --signature 2 --seconds 30

Requires a paired right Joy-Con (same as joycon_stream.py) and a display
(matplotlib interactive window) -- run this locally, not headless.
"""

import argparse
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from conducting_patterns import CANONICAL_PATTERNS  # noqa: E402
from gesture_trace_spike import _quat_from_angvel_deg, _quat_mult, _quat_to_pitch_yaw_deg, _bounding_size  # noqa: E402


def main():
    parser = argparse.ArgumentParser(description="Live real-time conducting-trace viewer (Phase B diagnostic).")
    parser.add_argument("--bpm", type=float, default=80.0)
    parser.add_argument("--signature", type=int, default=2, choices=[2, 3, 4])
    parser.add_argument("--seconds", type=float, default=60.0)
    args = parser.parse_args()

    beat_interval_s = 60.0 / args.bpm
    measure_interval_s = beat_interval_s * args.signature
    reference = CANONICAL_PATTERNS[args.signature].points

    try:
        from joycon_stream import JoyConStream
        stream = JoyConStream()
    except RuntimeError as e:
        print(f"ERROR: {e}", file=sys.stderr)
        sys.exit(1)

    import matplotlib
    matplotlib.use("TkAgg")
    import matplotlib.pyplot as plt

    plt.ion()
    fig, ax = plt.subplots(figsize=(5, 5))
    rx = [p[0] for p in reference]
    ry = [p[1] for p in reference]
    ref_line, = ax.plot(rx, ry, "--o", color="gray", linewidth=2, markersize=8, label="reference", alpha=0.6)
    trace_line, = ax.plot([], [], "-", color="tab:red", linewidth=2, label="live trace")
    ax.set_aspect("equal")
    ax.set_xlim(-1.5, 1.5)
    ax.set_ylim(-1.5, 1.5)
    ax.legend(loc="upper right")
    title = ax.set_title(f"{args.signature}/4 @ {args.bpm} BPM -- swing now")
    fig.tight_layout()

    q = (1.0, 0.0, 0.0, 0.0)
    trace_xy = []
    prev_t = None
    measure_start_wall = None
    last_draw = 0.0
    DRAW_INTERVAL_S = 0.05  # ~20fps, throttled so plotting doesn't starve sampling

    print(f"Reference: {args.signature}/4, strokes {CANONICAL_PATTERNS[args.signature].strokes}")
    print(f"Resetting every {measure_interval_s:.2f}s ({args.bpm} BPM x {args.signature} beats/measure) on a wall-clock "
          f"timer -- NOT on detected swings. Swing continuously in time with that pace. Ctrl+C to stop early.")

    try:
        for t, gx, gy, gz in stream.stream(duration_s=args.seconds):
            if measure_start_wall is None:
                measure_start_wall = t

            # Wall-clock reset schedule, independent of swing detection
            # (the whole point -- IctusDetector under-fires on gentle
            # continuous motion, so it can't drive this reliably).
            if t - measure_start_wall >= measure_interval_s:
                q = (1.0, 0.0, 0.0, 0.0)
                trace_xy = []
                measure_start_wall = t
                prev_t = t

            if prev_t is not None:
                dt = t - prev_t
                if dt > 0:
                    dq = _quat_from_angvel_deg(gx, gy, gz, dt)
                    q = _quat_mult(q, dq)
            prev_t = t

            pitch, yaw = _quat_to_pitch_yaw_deg(q)
            trace_xy.append((pitch, yaw))

            if t - last_draw >= DRAW_INTERVAL_S and len(trace_xy) >= 2:
                last_draw = t
                trace_size = _bounding_size(trace_xy) or 1.0
                ref_size = _bounding_size(reference) or 1.0
                scale = ref_size / trace_size
                tx = [p[0] * scale for p in trace_xy]
                ty = [p[1] * scale for p in trace_xy]
                trace_line.set_data(tx, ty)
                elapsed_in_measure = t - measure_start_wall
                title.set_text(f"{args.signature}/4 @ {args.bpm} BPM -- {elapsed_in_measure:.1f}/{measure_interval_s:.1f}s into measure")
                fig.canvas.draw_idle()
                fig.canvas.flush_events()
    except KeyboardInterrupt:
        print("\nStopped.")

    print("Done. Close the plot window to exit.")
    plt.ioff()
    plt.show()


if __name__ == "__main__":
    main()
