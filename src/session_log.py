"""Per-swing session data: a decoupled-from-rendering holder for the
post-round summary (raw offsets, sharpness, wind-up consistency) that also
doubles as the session data for the mid-term feedback-loop goal. Kept as a
simple dataclass list so dumping to CSV/JSON later is trivial.
"""

import csv
import dataclasses
import json
from dataclasses import dataclass, field


@dataclass
class SwingRecord:
    measure_index: int
    expected_time: float
    ictus_time: float | None  # None if no qualifying ictus was detected
    raw_offset_ms: float | None  # ictus_time - expected_time, uncorrected, ms
    corrected_offset_ms: float | None  # raw_offset_ms - calibration_offset_ms
    timing_tier: str  # "Perfect" | "Great" | "Good" | "Miss"
    peak_magnitude: float | None
    rise_duration: float | None
    sharpness_raw: float | None
    sharpness_bucket: str | None  # "Low" | "Mid" | "High" | None
    hit_tier: str  # "Bunt" | "Line Drive" | "Home Run" | "Miss"


@dataclass
class WindupSample:
    measure_index: int
    ictus_time: float


@dataclass
class SessionLog:
    swings: list[SwingRecord] = field(default_factory=list)
    windup_samples: list[WindupSample] = field(default_factory=list)

    def record_swing(self, record: SwingRecord) -> None:
        self.swings.append(record)

    def record_windup(self, sample: WindupSample) -> None:
        self.windup_samples.append(sample)

    def out_count(self) -> int:
        return sum(1 for s in self.swings if s.timing_tier == "Miss")

    def raw_offsets_ms(self) -> list[float]:
        return [s.raw_offset_ms for s in self.swings if s.raw_offset_ms is not None]

    def sharpness_values(self) -> list[float]:
        return [s.sharpness_raw for s in self.swings if s.sharpness_raw is not None]

    def windup_interval_variance(self) -> float | None:
        """Variance of inter-beat-interval length across logged wind-up
        ictuses -- purely observational in v1, not scored."""
        times = sorted(w.ictus_time for w in self.windup_samples)
        if len(times) < 3:
            return None
        intervals = [b - a for a, b in zip(times, times[1:])]
        mean = sum(intervals) / len(intervals)
        return sum((x - mean) ** 2 for x in intervals) / len(intervals)

    def to_dicts(self) -> list[dict]:
        return [dataclasses.asdict(s) for s in self.swings]

    def export_json(self, path: str) -> None:
        with open(path, "w") as f:
            json.dump(
                {
                    "swings": self.to_dicts(),
                    "windup_samples": [dataclasses.asdict(w) for w in self.windup_samples],
                },
                f,
                indent=2,
            )

    def export_csv(self, path: str) -> None:
        rows = self.to_dicts()
        if not rows:
            return
        with open(path, "w", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
            writer.writeheader()
            writer.writerows(rows)
