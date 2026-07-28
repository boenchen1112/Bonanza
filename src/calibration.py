"""Persistent calibration (Section 4a). Never runs automatically -- the
game only *loads* a saved offset on startup. Running calibration itself is
a Settings-menu action offered after a round, wired in settings_menu.py.
"""

import json
import os
from dataclasses import dataclass, field

CALIBRATION_PATH = os.path.join(os.path.dirname(__file__), "..", "calibration.json")

# Measured once on the dev machine (Section 4a). PLACEHOLDER: this has not
# actually been measured on real hardware yet -- 0.05s (50ms) is a
# reasonable Bluetooth+audio-latency guess to keep offset != 0, but must be
# replaced with a real measurement. (H1, Bug_Audit_2026-07-28.md: the
# previous comment pointed at "Phase 3 hardware bring-up", a phase that no
# longer exists under this name -- see Swinger_Build_Plan_v3.md Phase 0,
# which owns closing this out with an actual measured number.)
DEFAULT_OFFSET_S = 0.05

PRACTICE_MEASURES = 10  # 8-12 per spec; also seeds the sharpness reference
OUTLIER_BOUND_BEATS = 0.5  # discard candidate offsets larger than half a beat


@dataclass
class CalibrationResult:
    offset_s: float
    sharpness_seed: list[float] = field(default_factory=list)
    from_default: bool = False
    # Indices into the caller's practice_ictus_times/practice_expected_times
    # that survived the outlier filter and fed the offset average (C1,
    # Bug_Audit_2026-07-28.md) -- lets the caller build the sharpness seed
    # and the save-guard from the same filtered set instead of re-deriving
    # (and diverging from) it.
    used_indices: list[int] = field(default_factory=list)


def load_offset() -> tuple[float, bool]:
    """Returns (offset_s, was_loaded_from_file). Falls back to
    DEFAULT_OFFSET_S if no saved file exists yet."""
    offset_s, sharpness_seed, was_loaded = load_calibration()
    return offset_s, was_loaded


def load_calibration() -> tuple[float, list[float], bool]:
    """Returns (offset_s, sharpness_seed, was_loaded_from_file)."""
    if os.path.exists(CALIBRATION_PATH):
        try:
            with open(CALIBRATION_PATH) as f:
                data = json.load(f)
            return float(data["offset_s"]), list(data.get("sharpness_seed", [])), True
        except (json.JSONDecodeError, KeyError, ValueError, OSError):
            pass
    return DEFAULT_OFFSET_S, [], False


def save_offset(offset_s: float) -> None:
    save_calibration(offset_s, [])


def save_calibration(offset_s: float, sharpness_seed: list[float]) -> None:
    """Persists both the timing offset (4a) and the sharpness reference
    seed (4c) so bucketing against the player's own calibration swings
    survives a restart, not just the offset."""
    with open(CALIBRATION_PATH, "w") as f:
        json.dump({"offset_s": offset_s, "sharpness_seed": sharpness_seed}, f, indent=2)


def compute_offset(
    practice_ictus_times: list[float | None],
    practice_expected_times: list[float],
    beat_interval_s: float,
) -> CalibrationResult:
    """Given the closest ictus (or None if missed) per practice measure,
    compute the mean offset, discarding outliers beyond half a beat."""
    candidates = []
    used_indices = []
    for i, (ictus_t, expected_t) in enumerate(zip(practice_ictus_times, practice_expected_times)):
        if ictus_t is None:
            continue
        delta = ictus_t - expected_t
        if abs(delta) <= beat_interval_s * OUTLIER_BOUND_BEATS:
            candidates.append(delta)
            used_indices.append(i)

    if not candidates:
        offset_s, _ = load_offset()
        return CalibrationResult(offset_s=offset_s, from_default=True)

    offset_s = sum(candidates) / len(candidates)
    return CalibrationResult(offset_s=offset_s, used_indices=used_indices)
