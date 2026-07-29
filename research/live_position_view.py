"""Build Plan v4 Phase B: live real-time gesture POSITION viewer (webcam).

Pivot from gyro-only orientation tracking (live_trace_view.py): real
capture data showed a gyro-derived trace that stayed chaotic regardless of
low-pass filtering, and the user raised the right question -- conducting
is fundamentally a POSITION path, and a gyro is blind to pure translation
(moving the hand through a shape while the controller's orientation barely
changes, which is how someone without conducting training would naturally
trace a shape in the air). No amount of filtering fixes a signal that was
never carrying the intended motion.

This tool tracks actual 2D position instead, via simple HSV color
thresholding on a webcam feed -- click the Joy-Con's color once to
calibrate, then each frame's tracked centroid is an independent position
measurement (no integration, so no drift to fight, unlike gyro or
accelerometer approaches).

Usage:
    python research/live_position_view.py --bpm 80 --signature 2 --seconds 60

Requires a webcam and a display. Click the Joy-Con in the calibration
window, then press any key to start tracking.
"""

import argparse
import os
import sys
import time

import cv2
import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from conducting_patterns import CANONICAL_PATTERNS  # noqa: E402
from gesture_trace_spike import _bounding_size  # noqa: E402

CANVAS_SIZE = 500
CALIB_PATCH_RADIUS = 12
HSV_TOLERANCE = np.array([12, 70, 70])  # H, S, V half-widths around the sampled color


def _hue_ranges(hue: float, tolerance: float) -> list[tuple[float, float]]:
    """OpenCV hue wraps at 0/179 (it's a circular quantity, not linear) --
    a color sampled near either end (e.g. red, a common Joy-Con color)
    needs two ranges combined, or half the true hue tolerance silently
    gets clipped off instead of wrapping to the other side."""
    lo, hi = hue - tolerance, hue + tolerance
    if lo < 0:
        return [(0, hi), (180 + lo, 179)]
    if hi > 179:
        return [(lo, 179), (0, hi - 180)]
    return [(lo, hi)]


def calibrate_color(cap: cv2.VideoCapture) -> list[tuple[np.ndarray, np.ndarray]]:
    """Shows one live frame; user clicks the target color; returns a list
    of (hsv_lower, hsv_upper) bounds for cv2.inRange (usually one, two if
    the sampled hue wraps around 0/179)."""
    sampled = {"hsv": None}

    def on_click(event, x, y, flags, param):
        if event == cv2.EVENT_LBUTTONDOWN:
            ret, frame = cap.read()
            if not ret:
                return
            hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
            y0, y1 = max(0, y - CALIB_PATCH_RADIUS), y + CALIB_PATCH_RADIUS
            x0, x1 = max(0, x - CALIB_PATCH_RADIUS), x + CALIB_PATCH_RADIUS
            patch = hsv[y0:y1, x0:x1].reshape(-1, 3)
            sampled["hsv"] = np.median(patch, axis=0)
            print(f"Sampled HSV: {sampled['hsv']}")

    window = "Calibrate: click the Joy-Con's color, then press any key"
    cv2.namedWindow(window)
    cv2.setMouseCallback(window, on_click)

    while True:
        ret, frame = cap.read()
        if not ret:
            continue
        display = frame.copy()
        if sampled["hsv"] is not None:
            cv2.putText(display, "Sampled -- press any key to confirm, or click again", (10, 30),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
        else:
            cv2.putText(display, "Click on the Joy-Con", (10, 30),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 255), 2)
        cv2.imshow(window, display)
        key = cv2.waitKey(30)
        if key != -1 and sampled["hsv"] is not None:
            break
        if key == 27:  # ESC
            sys.exit("Calibration cancelled.")

    cv2.destroyWindow(window)
    hsv = sampled["hsv"]
    s_lower, s_upper = np.clip(hsv[1] - HSV_TOLERANCE[1], 0, 255), np.clip(hsv[1] + HSV_TOLERANCE[1], 0, 255)
    v_lower, v_upper = np.clip(hsv[2] - HSV_TOLERANCE[2], 0, 255), np.clip(hsv[2] + HSV_TOLERANCE[2], 0, 255)
    bounds = []
    for h_lo, h_hi in _hue_ranges(hsv[0], HSV_TOLERANCE[0]):
        bounds.append((
            np.array([h_lo, s_lower, v_lower]),
            np.array([h_hi, s_upper, v_upper]),
        ))
    return bounds


def find_target(frame: np.ndarray, bounds: list[tuple[np.ndarray, np.ndarray]]) -> tuple[float, float] | None:
    """Returns the pixel centroid of the largest matching-color contour, or
    None if nothing big enough was found this frame. bounds is a list of
    (lower, upper) HSV ranges (usually one, two if hue wraps 0/179)."""
    hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
    mask = None
    for lower, upper in bounds:
        m = cv2.inRange(hsv, lower, upper)
        mask = m if mask is None else cv2.bitwise_or(mask, m)
    mask = cv2.erode(mask, None, iterations=2)
    mask = cv2.dilate(mask, None, iterations=2)
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not contours:
        return None
    largest = max(contours, key=cv2.contourArea)
    if cv2.contourArea(largest) < 50:
        return None
    m = cv2.moments(largest)
    if m["m00"] == 0:
        return None
    return m["m10"] / m["m00"], m["m01"] / m["m00"]


def draw_canvas(reference: list[tuple[float, float]], trace_xy: list[tuple[float, float]], elapsed: float, measure_interval_s: float) -> np.ndarray:
    canvas = np.full((CANVAS_SIZE, CANVAS_SIZE, 3), 255, dtype=np.uint8)
    cx, cy = CANVAS_SIZE // 2, CANVAS_SIZE // 2
    px_per_unit = CANVAS_SIZE * 0.3

    def to_px(p):
        return int(cx + p[0] * px_per_unit), int(cy - p[1] * px_per_unit)  # y-up -> y-down for image coords

    for a, b in zip(reference, reference[1:]):
        cv2.line(canvas, to_px(a), to_px(b), (160, 160, 160), 2, cv2.LINE_AA)
    for p in reference:
        cv2.circle(canvas, to_px(p), 6, (160, 160, 160), -1, cv2.LINE_AA)

    if len(trace_xy) >= 2:
        trace_size = _bounding_size(trace_xy) or 1.0
        ref_size = _bounding_size(reference) or 1.0
        scale = ref_size / trace_size
        scaled = [(p[0] * scale, p[1] * scale) for p in trace_xy]
        for a, b in zip(scaled, scaled[1:]):
            cv2.line(canvas, to_px(a), to_px(b), (0, 0, 220), 2, cv2.LINE_AA)

    cv2.putText(canvas, f"{elapsed:.1f}/{measure_interval_s:.1f}s into measure", (10, CANVAS_SIZE - 15),
                cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 0, 0), 1, cv2.LINE_AA)
    return canvas


def main():
    parser = argparse.ArgumentParser(description="Live real-time conducting-position viewer via webcam color tracking (Phase B).")
    parser.add_argument("--bpm", type=float, default=80.0)
    parser.add_argument("--signature", type=int, default=2, choices=[2, 3, 4])
    parser.add_argument("--seconds", type=float, default=60.0)
    parser.add_argument("--camera-index", type=int, default=0)
    args = parser.parse_args()

    measure_interval_s = (60.0 / args.bpm) * args.signature
    reference = CANONICAL_PATTERNS[args.signature].points

    cap = cv2.VideoCapture(args.camera_index)
    if not cap.isOpened():
        sys.exit(f"ERROR: could not open camera index {args.camera_index}")

    color_bounds = calibrate_color(cap)

    print(f"Reference: {args.signature}/4, strokes {CANONICAL_PATTERNS[args.signature].strokes}")
    print(f"Resetting every {measure_interval_s:.2f}s ({args.bpm} BPM x {args.signature} beats/measure) on a wall-clock timer.")
    print("Tracking window shows the raw camera feed with a marker on the detected position; "
          "Trace window shows the reference (gray) vs. your traced path (red). Press 'q' or ESC in either to quit.")

    trace_xy = []
    origin = None
    measure_start_wall = None
    start_time = time.perf_counter()

    try:
        while time.perf_counter() - start_time < args.seconds:
            ret, frame = cap.read()
            if not ret:
                continue
            now = time.perf_counter()

            target = find_target(frame, color_bounds)
            display = frame.copy()
            if target is not None:
                px, py = target
                cv2.circle(display, (int(px), int(py)), 10, (0, 255, 0), 2)

                if measure_start_wall is None:
                    measure_start_wall = now
                if now - measure_start_wall >= measure_interval_s:
                    trace_xy = []
                    measure_start_wall = now
                    origin = (px, py)
                if origin is None:
                    origin = (px, py)

                # Pixel -> normalized (y-up), relative to this measure's
                # starting position -- no integration involved, so this
                # carries no drift; each frame is an independent
                # measurement of where the tracked color actually is.
                frame_h = frame.shape[0]
                nx = (px - origin[0]) / frame_h
                ny = -(py - origin[1]) / frame_h
                trace_xy.append((nx, ny))

            cv2.imshow("Camera (tracking marker in green)", display)
            elapsed = (now - measure_start_wall) if measure_start_wall is not None else 0.0
            cv2.imshow("Trace: reference (gray) vs. you (red)", draw_canvas(reference, trace_xy, elapsed, measure_interval_s))

            key = cv2.waitKey(1) & 0xFF
            if key in (ord("q"), 27):
                break
    finally:
        cap.release()
        cv2.destroyAllWindows()

    print("Done.")


if __name__ == "__main__":
    main()
