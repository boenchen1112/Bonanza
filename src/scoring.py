"""Timing/sharpness tiers and the timing x sharpness -> hit-tier lookup
table (Section 4b-4d). Kept separate from game.py's state machine and from
calibration.py's offset logic so this same skeleton can be reused by other
minigames in the larger set (Strike It Rich, Take a Stab, Rhythm and
Bruise) -- those swap the tier-table labels, not this logic.
"""

from dataclasses import dataclass

# --- Timing tier windows (ms). Boundaries live inside EXPECTING_ICTUS
# (game.py's HALF_BEAT_WINDOW_S), which is strictly wider -- keep the two
# constants explicitly separate so tuning one never silently changes the
# other. ---
PERFECT_MS = 50.0
GREAT_MS = 100.0
GOOD_MS = 150.0


def timing_tier(corrected_offset_ms: float | None) -> str:
    """corrected_offset_ms is None when no ictus was detected in the window."""
    if corrected_offset_ms is None:
        return "Miss"
    abs_ms = abs(corrected_offset_ms)
    if abs_ms <= PERFECT_MS:
        return "Perfect"
    if abs_ms <= GREAT_MS:
        return "Great"
    if abs_ms <= GOOD_MS:
        return "Good"
    return "Miss"


# Fallback sharpness range used only before the player has ever calibrated
# (fresh install on the shipped default offset). Derived from two genuine
# post-F3 hardware captures (src/swings_fresh.csv, ~20s continuous swinging;
# src/swings_counted.csv, 15 discrete soft/medium/hard swings), 53 detected events
# combined, p10-p90 trimmed to drop a handful of very weak
# hesitation/false-start events on soft swings (min untrimmed was ~10,600 --
# see the Phase 4 count-mismatch note below). Range below covers the p10-p90
# span, rounded.
FALLBACK_SHARPNESS_LOW_HIGH = (60000.0, 420000.0)

# Phase 4 finding (src/swings_counted.csv, 15 intended discrete swings: 5
# soft/5 medium/5 hard): the detector found 30 raw events at the default
# REFRACTORY_S=0.175, mostly clustered in the soft-swing portion with
# sub-0.8s gaps. Confirmed with the player: this matches real
# hesitation/false-starts on soft swings, not a detector bug -- soft swings
# are harder to execute as one clean motion, and the detector correctly
# flags each qualifying direction-change. Raising REFRACTORY_S only
# marginally reduced the count (0.3s -> 29 events) at the cost of eating
# into the 1.5s (80bpm, 2/4) inter-measure margin, so it was left at 0.175s.
# In live gameplay this doesn't cause incorrect scoring -- MeasureJudge
# locks only the first in-window event per measure (game.py) -- but it does
# mean raw Phase 0 CSV event counts are not a reliable proxy for "number of
# real swings" on soft-intensity captures specifically. Re-verify with a
# steadier-paced capture if this needs tighter validation later.


@dataclass
class SharpnessReference:
    """Per-player reference distribution for percentile bucketing.

    Seeded from calibration swings (Section 4a), then rolled forward with
    live gameplay swings so it doesn't go stale or rest on only 8-12 points.
    """

    values: list[float]
    max_size: int = 200

    def add(self, value: float) -> None:
        self.values.append(value)
        if len(self.values) > self.max_size:
            self.values.pop(0)

    def bucket(self, value: float) -> str:
        if len(self.values) < 3:
            low, high = FALLBACK_SHARPNESS_LOW_HIGH
            span = high - low
            if value <= low + span / 3:
                return "Low"
            if value <= low + 2 * span / 3:
                return "Mid"
            return "High"
        sorted_vals = sorted(self.values)
        n = len(sorted_vals)
        rank = sum(1 for v in sorted_vals if v <= value) / n
        if rank <= 1 / 3:
            return "Low"
        if rank <= 2 / 3:
            return "Mid"
        return "High"


def raw_sharpness(peak_magnitude: float, rise_duration: float, max_derivative: float | None = None) -> float:
    """max_derivative is required (and preferred) when sample rate is low
    (~<100Hz, per Phase 0's measured rate) -- rise_duration quantization
    becomes a large relative error in the denominator at low rates."""
    if max_derivative is not None:
        return max_derivative
    if rise_duration <= 0:
        return 0.0
    return peak_magnitude / rise_duration


# Timing tier (rows) x sharpness bucket (columns) -> hit tier.
_HIT_TABLE = {
    ("Perfect", "Low"): "Bunt",
    ("Perfect", "Mid"): "Line Drive",
    ("Perfect", "High"): "Home Run",
    ("Great", "Low"): "Bunt",
    ("Great", "Mid"): "Line Drive",
    ("Great", "High"): "Home Run",
    ("Good", "Low"): "Bunt",
    ("Good", "Mid"): "Bunt",
    ("Good", "High"): "Line Drive",
}


def hit_tier(timing: str, sharpness_bucket: str | None) -> str:
    """Sharpness never causes a Miss or contributes to outs -- it only
    affects hit quality. A Miss on timing is a Miss regardless of
    sharpness."""
    if timing == "Miss":
        return "Miss"
    return _HIT_TABLE.get((timing, sharpness_bucket), "Bunt")
