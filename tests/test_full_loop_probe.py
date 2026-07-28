"""Synthetic full-loop probe (Swinger_Build_Plan_v2.md Phase 3.5 exit
criterion): drives MeasureJudge for >500 measures with a fake gyro stream
that never misses, to confirm:

  - A4: max_measures round-end doesn't IndexError once measure_index
    reaches the schedule's bound.
  - F4: wind-up samples (beat-2 rebound ictuses) are actually captured
    across many measures, so session_log.windup_interval_variance()
    returns a real number, not None -- the direct symptom of the old
    blocking pygame.time.wait(600) bug.

No pygame/hardware -- MeasureJudge is pure logic (see game.py's docstring).
"""

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from beat_schedule import BeatSchedule, BeatScheduleConfig
from game import GameConfig, MeasureJudge
from ictus_detector import IctusEvent
from scoring import SharpnessReference
from session_log import SessionLog, WindupSample


def main():
    config = GameConfig()
    schedule = BeatSchedule(config.schedule_config, start_time=0.0, num_measures=config.max_measures)
    judge = MeasureJudge(schedule, calibration_offset_s=0.0, sharpness_ref=SharpnessReference(values=[]))
    session_log = SessionLog()

    measure_index = 0
    while measure_index < config.max_measures:
        beat1 = schedule.measure_beat1_time(measure_index)
        # On-time, sharp swing every measure -> never an out. max_derivative
        # set explicitly (T1, reviews/Bug_Audit_2026-07-28.md) so this
        # exercises the shipped sharpness metric, not the fallback.
        judge.submit_ictus(
            IctusEvent(timestamp=beat1 + 0.005, peak_magnitude=25000.0, rise_duration=0.05, max_derivative=300000.0)
        )
        # Beat-2 rebound wind-up ictus, well outside the scoring window --
        # this is what F4's blocking wait used to systematically lose.
        beat2 = schedule.measure_beat1_time(measure_index) + schedule.beat_interval
        judge.submit_ictus(
            IctusEvent(timestamp=beat2, peak_magnitude=5000.0, rise_duration=0.03, max_derivative=60000.0)
        )

        record = judge.tick(beat1 + schedule.beat_interval * 2)  # past both beats of the measure
        assert record is not None
        session_log.swings.append(record)
        for w in judge.windup_samples:
            session_log.windup_samples.append(w)
        judge.windup_samples.clear()

        measure_index = judge.measure_index

    print(f"Ran {measure_index} measures without IndexError (A4 guard: caller stops at max_measures).")
    assert measure_index == config.max_measures
    assert judge.misses == 0, f"expected zero misses, got {judge.misses}"

    variance = session_log.windup_interval_variance()
    print(f"Wind-up samples captured: {len(session_log.windup_samples)}")
    print(f"windup_interval_variance(): {variance}")
    assert variance is not None, "F4 regression: wind-up samples not reaching session_log"

    print("PASS: >=500-measure synthetic round completes cleanly with a real windup variance stat.")


if __name__ == "__main__":
    main()
