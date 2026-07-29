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
import csv
import math
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
    parser.add_argument(
        "--cutoff-hz", type=float, default=2.5,
        help="Low-pass cutoff (Hz) applied to raw gx/gy/gz before integration, via an "
             "exponential moving average. A real capture (2026-07-29) showed gz reversing "
             "sign every 50-150ms at thousands of deg/s during a self-reported gentle, "
             "continuous swing -- a scale/frequency consistent with hand tremor riding on "
             "top of the intended slow gesture, not the gesture itself. 0 disables filtering "
             "(raw, same as before this option existed).",
    )
    args = parser.parse_args()

    beat_interval_s = 60.0 / args.bpm
    measure_interval_s = beat_interval_s * args.signature
    reference = CANONICAL_PATTERNS[args.signature].points

    # Always log to disk (feedback, 2026-07-29): live/interactive tools must
    # persist a record automatically, not rely on the user transcribing a
    # live window or console output afterward.
    log_dir = os.path.join(os.path.dirname(__file__), "logs")
    os.makedirs(log_dir, exist_ok=True)
    run_stamp = time.strftime("%Y%m%d-%H%M%S")
    log_path = os.path.join(log_dir, f"live_trace_{run_stamp}.csv")
    snapshot_path = os.path.join(log_dir, f"live_trace_{run_stamp}_final.png")
    log_file = open(log_path, "w", newline="")
    log_writer = csv.writer(log_file)
    log_writer.writerow(["t", "measure_index", "raw_gx", "raw_gy", "raw_gz",
                          "filt_gx", "filt_gy", "filt_gz", "pitch_deg", "yaw_deg"])

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
    plt.show(block=False)
    plt.pause(0.1)  # force the window to actually appear before the read loop starts

    q = (1.0, 0.0, 0.0, 0.0)
    trace_xy = []
    prev_t = None
    measure_start_wall = None
    measure_index = -1
    last_draw = 0.0
    last_print = 0.0
    DRAW_INTERVAL_S = 0.05  # ~20fps, throttled so plotting doesn't starve sampling
    PRINT_INTERVAL_S = 0.5  # raw-value printout, independent of the plot -- lets you
    # confirm the gyro data itself is live and responding to swings even if
    # something about the plot rendering is in doubt.

    print(f"Reference: {args.signature}/4, strokes {CANONICAL_PATTERNS[args.signature].strokes}")
    print(f"Resetting every {measure_interval_s:.2f}s ({args.bpm} BPM x {args.signature} beats/measure) on a wall-clock "
          f"timer -- NOT on detected swings. Swing continuously in time with that pace. Ctrl+C to stop early.")
    print(f"Low-pass cutoff: {args.cutoff_hz} Hz" + (" (disabled, raw gyro)" if args.cutoff_hz <= 0 else ""))
    print(f"Logging every sample to {log_path}")

    filt_gx = filt_gy = filt_gz = 0.0
    filt_prev_t = None

    try:
        try:
            # stream() yields (t, gx, gy, gz, magnitude, is_new) -- is_new
            # distinguishes a genuinely fresh reading from a repeated cached
            # one at the device's native ~66Hz rate (J2, Bug_Audit_2026-07-28.md).
            # Skip duplicates for integration the same way game.py does for
            # detection: a repeated reading isn't a new angular-velocity sample,
            # integrating it again would double-count that instant's rotation.
            for t, gx, gy, gz, _mag, is_new in stream.stream(duration_s=args.seconds):
                if not is_new:
                    continue

                raw_gx, raw_gy, raw_gz = gx, gy, gz

                # Exponential-moving-average low-pass, applied before
                # integration: a real capture showed gz reversing sign every
                # 50-150ms at thousands of deg/s during self-reported gentle
                # swinging -- likely hand tremor riding on top of the intended
                # slow gesture. alpha derived from --cutoff-hz and the actual
                # sample dt so the cutoff means the same thing regardless of
                # the device's live poll rate.
                if args.cutoff_hz > 0 and filt_prev_t is not None:
                    dt_f = t - filt_prev_t
                    if dt_f > 0:
                        alpha = 1.0 - math.exp(-2.0 * math.pi * args.cutoff_hz * dt_f)
                        filt_gx += alpha * (gx - filt_gx)
                        filt_gy += alpha * (gy - filt_gy)
                        filt_gz += alpha * (gz - filt_gz)
                else:
                    filt_gx, filt_gy, filt_gz = gx, gy, gz
                filt_prev_t = t
                if args.cutoff_hz > 0:
                    gx, gy, gz = filt_gx, filt_gy, filt_gz

                if measure_start_wall is None:
                    measure_start_wall = t
                    measure_index = 0

                # Wall-clock reset schedule, independent of swing detection
                # (the whole point -- IctusDetector under-fires on gentle
                # continuous motion, so it can't drive this reliably).
                if t - measure_start_wall >= measure_interval_s:
                    q = (1.0, 0.0, 0.0, 0.0)
                    trace_xy = []
                    measure_start_wall = t
                    measure_index += 1
                    prev_t = t

                if prev_t is not None:
                    dt = t - prev_t
                    if dt > 0:
                        dq = _quat_from_angvel_deg(gx, gy, gz, dt)
                        q = _quat_mult(q, dq)
                prev_t = t

                pitch, yaw = _quat_to_pitch_yaw_deg(q)
                trace_xy.append((pitch, yaw))

                log_writer.writerow([f"{t:.4f}", measure_index, f"{raw_gx:.2f}", f"{raw_gy:.2f}", f"{raw_gz:.2f}",
                                      f"{filt_gx:.2f}", f"{filt_gy:.2f}", f"{filt_gz:.2f}", f"{pitch:.3f}", f"{yaw:.3f}"])

                if t - last_print >= PRINT_INTERVAL_S:
                    last_print = t
                    print(f"raw gx={raw_gx:8.1f} gy={raw_gy:8.1f} gz={raw_gz:8.1f}  |  integrated pitch={pitch:7.2f} yaw={yaw:7.2f}  |  trace points={len(trace_xy)}")

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
                    # plt.pause() (not just draw_idle()+flush_events()) --
                    # draw_idle() only *schedules* a redraw and TkAgg doesn't
                    # reliably flush it without pause() actually pumping the
                    # GUI event loop. draw_idle()+flush_events() alone is a
                    # common cause of a live plot window that opens but never
                    # visibly updates.
                    plt.pause(0.001)
        except KeyboardInterrupt:
            print("\nStopped.")
    finally:
        # Always persist a record, even on Ctrl+C or a mid-run exception
        # (feedback, 2026-07-29) -- nothing to "check afterward" is not an
        # acceptable failure mode for a live diagnostic tool.
        log_file.close()
        fig.savefig(snapshot_path, dpi=120)
        print(f"Log saved: {log_path}")
        print(f"Final snapshot saved: {snapshot_path}")

    print("Done. Close the plot window to exit.")
    plt.ioff()
    plt.show()


if __name__ == "__main__":
    main()
