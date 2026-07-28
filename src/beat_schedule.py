"""Expected-beat-time lookup, kept separate from playback (metronome.py).

Deliberately isolated per Build Plan Section 0's "future direction" note:
a later version inverts the relationship (conductor leads, audio follows),
so "what time is beat N expected" must stay swappable without touching the
detector or scorer. This module is the only thing that should change if the
beat source stops being a fixed-BPM schedule.
"""

from dataclasses import dataclass


@dataclass(frozen=True)
class BeatScheduleConfig:
    bpm: float = 80.0
    # Defaults to 2/4 (v1 scope), but BeatSchedule below has no 2/4-specific
    # logic -- is_downbeat()/measure_beat1_time() are already generic on
    # this value, so 3 (3/4) or 4 (4/4) work without further changes
    # (Build Plan v4 Phase C: confirmed by reading, not re-derived).
    beats_per_measure: int = 2


class BeatSchedule:
    """Precomputed list of expected beat timestamps for a session.

    Timestamps are generated once at session start (not computed live) in
    the same perf_counter clock domain used by joycon_stream.py, so later
    phases can look up "expected time for beat N" without recomputation.
    """

    def __init__(self, config: BeatScheduleConfig, start_time: float, num_measures: int):
        self.config = config
        self.start_time = start_time
        self.beat_interval = 60.0 / config.bpm
        self._timestamps = self._build(num_measures)

    def _build(self, num_measures: int) -> list[float]:
        total_beats = num_measures * self.config.beats_per_measure
        return [self.start_time + i * self.beat_interval for i in range(total_beats)]

    def beat_time(self, beat_index: int) -> float:
        """Expected perf_counter timestamp for beat_index (0-based, global)."""
        return self._timestamps[beat_index]

    def measure_beat1_time(self, measure_index: int) -> float:
        """Expected timestamp of beat 1 (the scored downbeat) for a given measure."""
        return self.beat_time(measure_index * self.config.beats_per_measure)

    def is_downbeat(self, beat_index: int) -> bool:
        return beat_index % self.config.beats_per_measure == 0

    def __len__(self) -> int:
        return len(self._timestamps)

    def all_timestamps(self) -> list[float]:
        return list(self._timestamps)
