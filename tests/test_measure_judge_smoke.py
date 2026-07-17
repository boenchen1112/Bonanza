"""Offline smoke test for game.py's MeasureJudge (WINDUP/EXPECTING_ICTUS/
JUDGE state machine) using synthetic IctusEvents -- no pygame, no
hardware. Confirms the code path runs and produces sane tiers; does not
validate tuned thresholds.
"""

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from beat_schedule import BeatSchedule, BeatScheduleConfig
from game import JUDGE_GRACE_S, MeasureJudge
from ictus_detector import IctusEvent
from scoring import SharpnessReference


def main():
    config = BeatScheduleConfig(bpm=80.0)
    start = 0.0
    schedule = BeatSchedule(config, start, num_measures=4)
    judge = MeasureJudge(schedule, calibration_offset_s=0.0, sharpness_ref=SharpnessReference(values=[]))
    # tick() now waits window_end + JUDGE_GRACE_S before judging (found via
    # real-hardware playtest: the detector can legitimately take up to
    # ~150-200ms after the true ictus to deliver the event) -- ticks in
    # this test need to clear that grace period too, not just window_end.
    past_window = schedule.beat_interval / 2.0 + JUDGE_GRACE_S + 0.001

    # Measure 0: dead-on-time, sharp swing -> expect Perfect/Home Run
    beat1_m0 = schedule.measure_beat1_time(0)
    judge.submit_ictus(IctusEvent(timestamp=beat1_m0 + 0.01, peak_magnitude=800.0, rise_duration=0.05))
    r0 = judge.tick(beat1_m0 + past_window)
    assert r0 is not None and r0.timing_tier == "Perfect", r0

    # Measure 1: no swing at all -> Miss
    r1 = None
    while r1 is None:
        beat1_m1 = schedule.measure_beat1_time(1)
        r1 = judge.tick(beat1_m1 + past_window)
    assert r1.timing_tier == "Miss" and r1.hit_tier == "Miss", r1

    # Measure 2: late but inside window (300ms late, half-beat window is 375ms) -> Miss (timing) but not out-of-window
    beat1_m2 = schedule.measure_beat1_time(2)
    judge.submit_ictus(IctusEvent(timestamp=beat1_m2 + 0.3, peak_magnitude=800.0, rise_duration=0.05))
    r2 = judge.tick(beat1_m2 + past_window)
    assert r2.timing_tier == "Miss" and r2.ictus_time is not None, r2

    print(f"Measure 0: {r0.timing_tier} / {r0.hit_tier}")
    print(f"Measure 1: {r1.timing_tier} / {r1.hit_tier}")
    print(f"Measure 2: {r2.timing_tier} / {r2.hit_tier} (late-but-in-window, measurable offset)")
    print(f"Misses so far: {judge.misses}")
    assert judge.misses == 2
    print("PASS: MeasureJudge state machine produces correct tiers for synthetic events")


if __name__ == "__main__":
    main()
