"""Phase 0: minimal right-Joy-Con gyro read/log utility.

Streams gyro x/y/z, timestamps every sample with time.perf_counter() at
host read time (NOT any timestamp the library itself provides -- those are
a different clock domain and everything downstream, including
beat_schedule.py, must live in this one clock).

Requires a paired right Joy-Con over Bluetooth and the `joycon-python`
(`pyjoycon`) + `hid` packages. This module cannot be exercised without that
physical hardware -- running it here is gated, but the code is complete and
ready to run on a machine with a paired controller.

Usage:
    python joycon_stream.py --seconds 10 --out swings.csv
"""

import argparse
import csv
import math
import sys
import time

try:
    from pyjoycon import JoyCon, get_R_id
except ImportError as e:
    JoyCon = None
    get_R_id = None
    _import_error = e

# Single poll rate shared by every caller (Phase 0 CLI capture, game.py,
# settings_menu.py) -- oversamples the device's native ~66Hz update rate so
# stream()'s duplicate-read skip (below) never starves a real update, while
# staying fixed regardless of caller so thresholds tuned at one call site
# still mean the same thing at another (F3, reviews/Bug_Audit_2026-07-17.md).
POLL_INTERVAL_S = 1.0 / 200.0


def combined_magnitude(gx: float, gy: float, gz: float) -> float:
    return math.sqrt(gx * gx + gy * gy + gz * gz)


class JoyConStream:
    """Wraps pyjoycon's right-JoyCon gyro reads with perf_counter timestamps."""

    def __init__(self):
        if JoyCon is None:
            raise RuntimeError(
                f"pyjoycon import failed ({_import_error}). On Windows this is "
                "usually the `hidapi` package (not `hid`) missing, or PyGLM "
                "missing -- `pip install joycon-python hidapi PyGLM` and pair a "
                "right Joy-Con over Bluetooth before running this."
            )
        joycon_id = get_R_id()
        if joycon_id is None or joycon_id[0] is None:
            raise RuntimeError("No right Joy-Con found. Pair it over Bluetooth first.")
        self._joycon = JoyCon(*joycon_id)

    def read_sample(self) -> tuple[float, float, float, float]:
        """Returns (perf_counter_timestamp, gx, gy, gz) for one sample."""
        t = time.perf_counter()
        status = self._joycon.get_status()
        gyro = status["gyro"]
        return t, float(gyro["x"]), float(gyro["y"]), float(gyro["z"])

    def stream(self, duration_s: float, poll_interval_s: float = POLL_INTERVAL_S):
        """Yields (timestamp, gx, gy, gz, magnitude) for duration_s seconds.

        Skips consecutive duplicate reads (the device's internal buffer
        returns the same cached reading across multiple polls at its native
        ~66Hz update rate) so downstream dedup counts and event timing don't
        depend on how fast the caller happens to poll (A5/F3)."""
        end_time = time.perf_counter() + duration_s
        last_reading: tuple[float, float, float] | None = None
        while time.perf_counter() < end_time:
            t, gx, gy, gz = self.read_sample()
            reading = (gx, gy, gz)
            if reading != last_reading:
                last_reading = reading
                yield t, gx, gy, gz, combined_magnitude(gx, gy, gz)
            if poll_interval_s > 0:
                time.sleep(poll_interval_s)


def report_sample_rate(timestamps: list[float]) -> dict:
    if len(timestamps) < 2:
        return {"count": len(timestamps), "mean_hz": None, "jitter_ms": None}
    intervals = [b - a for a, b in zip(timestamps, timestamps[1:])]
    mean_interval = sum(intervals) / len(intervals)
    mean_hz = 1.0 / mean_interval if mean_interval > 0 else None
    jitter_ms = (max(intervals) - min(intervals)) * 1000
    return {"count": len(timestamps), "mean_hz": mean_hz, "jitter_ms": jitter_ms}


def main():
    parser = argparse.ArgumentParser(description="Log right-Joy-Con gyro data to CSV.")
    parser.add_argument("--seconds", type=float, default=10.0)
    parser.add_argument("--out", type=str, default="swings.csv")
    args = parser.parse_args()

    try:
        stream = JoyConStream()
    except RuntimeError as e:
        print(f"ERROR: {e}", file=sys.stderr)
        sys.exit(1)

    rows = []
    print(f"Logging {args.seconds}s of gyro data to {args.out} ... swing the Joy-Con now.")
    for t, gx, gy, gz, mag in stream.stream(args.seconds):
        rows.append((t, gx, gy, gz, mag))
        print(f"t={t:.4f} gx={gx:.2f} gy={gy:.2f} gz={gz:.2f} mag={mag:.2f}")

    with open(args.out, "w", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["timestamp", "gx", "gy", "gz", "magnitude"])
        writer.writerows(rows)

    stats = report_sample_rate([r[0] for r in rows])
    print(f"\nSamples: {stats['count']}  Mean rate: {stats['mean_hz']:.1f} Hz  "
          f"Jitter: {stats['jitter_ms']:.2f} ms"
          if stats["mean_hz"] else "\nNot enough samples for rate report.")


if __name__ == "__main__":
    main()
