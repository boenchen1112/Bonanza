"""Canonical conducting beat-pattern reference shapes (Build Plan v4 Phase C).

Defines the standard 2/4, 3/4, and 4/4 conducting patterns as normalized 2D
keyframe polylines -- the "guide letter" a student's live trace will
eventually be shown against (Phase D). Deliberately independent of Phase B's
still-open question of what coordinate space a *live* trace gets recovered
in (gyro-integrated orientation vs. something else): these reference shapes
are standard conducting pedagogy, not something that needs empirical
validation, so they don't need to wait on Phase B's verdict. Phase B's own
scoring will resample a live trace and compare it against these.

Convention: x-right, y-up, origin at the neutral/preparatory position,
normalized so each pattern's bounding box has a max half-extent of 1.0.
"""

import json
import math
import os
from dataclasses import dataclass

# The classic beat-pattern vocabulary per time signature (Swinger_Build_Plan_v4.md
# Phase C/0): each entry is the sequence of strokes from beat 1 through the
# last beat, ending back near the preparatory position for beat 1 of the
# next measure. Labels only (used for plot annotation) -- the actual shape
# comes from _RAW_POINTS below, not from summing these as direction
# vectors: real conducting patterns are specific schematic loops (e.g. 4/4's
# "left" and "right" strokes both sit *below* center and never retrace the
# same line), not literal sums of unit cardinal vectors. An earlier version
# of this module did exactly that and produced a degenerate self-overlapping
# 4/4 shape (beat 3 landing exactly on beat 1's point) -- caught by looking
# at the rendered plot, which is exactly why this module renders one.
_STROKE_SEQUENCES = {
    2: ["down", "up"],
    3: ["down", "right", "up"],
    4: ["down", "left", "right", "up"],
}

# Keyframes per signature: [prep, beat1, beat2, ...]. x-right, y-up,
# conductor's own perspective.
#
# v5 Phase 2 (2026-07-29): all three replaced with REAL data extracted
# from user-supplied reference videos (2_4.mp4, 3_4.mp4, 4_4.mp4 -- see
# research/extract_pattern_from_video.py), not schematic guesses. The
# videos are generated diagrams of the pattern being drawn stroke-by-
# stroke with beat-number labels; extraction uses connected-component
# analysis plus each label's first-appearance frame to recover true beat
# order (color-based and pure-geometry approaches were both tried and
# failed -- see that script's module docstring). Verified visually against
# research/captures/pattern_*4_overlay.png before being copied in here.
# 2/4 previously hand-corrected against a real photo (2026-07-29 diagram)
# is now confirmed by this independent video source: same "J"-hook shape,
# beat 1 lowest, beat 2 a rebound up-and-right (not back at prep). 3/4 and
# 4/4 were unverified schematic guesses before this -- 4/4 in particular
# turned out to be a "cross" (down/left/right/up, strokes crossing near
# center), not the previous guess's non-crossing checkmark shape.
#
# Loaded from research/hand_tracking_web/assets/reference_patterns.json --
# used to be a second hand-typed copy of these numbers (the first is
# index.html's own REFERENCE), which already caused one shipped bug (2/4
# drawn 48.8% too wide from the two copies drifting). Both now read the
# one generated JSON.
_REFERENCE_PATTERNS_PATH = os.path.join(
    os.path.dirname(__file__), "..", "research", "hand_tracking_web", "assets", "reference_patterns.json"
)


def _load_raw_points() -> dict[int, list[tuple[float, float]]]:
    with open(_REFERENCE_PATTERNS_PATH) as f:
        data = json.load(f)
    return {int(sig): [tuple(pt) for pt in points] for sig, points in data.items()}


_RAW_POINTS = _load_raw_points()


@dataclass(frozen=True)
class ConductingPattern:
    beats_per_measure: int
    strokes: list[str]  # stroke name per beat, len == beats_per_measure
    points: list[tuple[float, float]]  # keyframes: [prep, beat1, beat2, ...]

    def beat_point(self, beat_index_in_measure: int) -> tuple[float, float]:
        """0-based beat index within the measure -> its ictus keyframe."""
        return self.points[beat_index_in_measure + 1]


def _build_pattern(beats_per_measure: int) -> ConductingPattern:
    strokes = _STROKE_SEQUENCES[beats_per_measure]
    points = list(_RAW_POINTS[beats_per_measure])

    # Normalize so the pattern's max coordinate magnitude is 1.0 -- keeps
    # patterns of different stroke counts comparable in scale.
    max_extent = max(max(abs(px), abs(py)) for px, py in points)
    if max_extent > 0:
        points = [(px / max_extent, py / max_extent) for px, py in points]

    return ConductingPattern(beats_per_measure=beats_per_measure, strokes=strokes, points=points)


CANONICAL_PATTERNS: dict[int, ConductingPattern] = {
    n: _build_pattern(n) for n in _STROKE_SEQUENCES
}


def resample_path(points: list[tuple[float, float]], n: int) -> list[tuple[float, float]]:
    """Arc-length resample a polyline to exactly n evenly-spaced points
    (Phase B's shape-distance method, defined here so both Phase B's spike
    script and Phase D's eventual live comparison share one implementation
    instead of two subtly different ones)."""
    if len(points) < 2:
        return list(points) * n if points else []

    seg_lengths = [
        math.dist(points[i], points[i + 1]) for i in range(len(points) - 1)
    ]
    total = sum(seg_lengths)
    if total == 0:
        return [points[0]] * n

    cum = [0.0]
    for seg_len in seg_lengths:
        cum.append(cum[-1] + seg_len)

    result = []
    for i in range(n):
        target = (i / (n - 1)) * total if n > 1 else 0.0
        # Find the segment containing `target` along the cumulative length.
        seg_idx = 0
        while seg_idx < len(seg_lengths) - 1 and cum[seg_idx + 1] < target:
            seg_idx += 1
        seg_start_len = cum[seg_idx]
        seg_len = seg_lengths[seg_idx] if seg_lengths[seg_idx] > 0 else 1e-9
        frac = (target - seg_start_len) / seg_len
        frac = min(max(frac, 0.0), 1.0)
        x0, y0 = points[seg_idx]
        x1, y1 = points[seg_idx + 1]
        result.append((x0 + frac * (x1 - x0), y0 + frac * (y1 - y0)))
    return result


def plot_pattern(pattern: ConductingPattern, out_path: str) -> None:
    """Renders a pattern as a static reference-shape PNG (Phase C exit
    criterion: "a canonical reference pattern defined and renderable"),
    labeling each beat's ictus point."""
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    xs = [p[0] for p in pattern.points]
    ys = [p[1] for p in pattern.points]

    fig, ax = plt.subplots(figsize=(4, 4))
    ax.plot(xs, ys, "-o", color="tab:blue")
    ax.plot(xs[0], ys[0], "s", color="black", markersize=10, label="prep")
    for i, stroke in enumerate(pattern.strokes):
        px, py = pattern.points[i + 1]
        ax.annotate(f"beat {i + 1}\n({stroke})", (px, py), textcoords="offset points", xytext=(8, 8))
    ax.set_title(f"{pattern.beats_per_measure}/4 conducting pattern")
    ax.set_aspect("equal")
    ax.set_xlim(-1.5, 1.5)
    ax.set_ylim(-1.5, 1.5)
    ax.legend(loc="upper right")
    fig.tight_layout()
    fig.savefig(out_path, dpi=120)
    plt.close(fig)


def main():
    import os

    out_dir = os.path.join(os.path.dirname(__file__), "..", "research", "patterns")
    os.makedirs(out_dir, exist_ok=True)
    for n, pattern in CANONICAL_PATTERNS.items():
        out_path = os.path.join(out_dir, f"pattern_{n}_{n if n != 2 else 2}4.png".replace(f"{n}_{n}", f"{n}_{n}"))
        out_path = os.path.join(out_dir, f"pattern_{n}4.png")
        plot_pattern(pattern, out_path)
        print(f"{n}/4: {pattern.strokes} -> {out_path}")


if __name__ == "__main__":
    main()
