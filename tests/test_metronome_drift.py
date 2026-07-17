"""Phase 1 exit-criterion check: clicks stay in time with no scheduling
drift. Run with SDL_AUDIODRIVER=dummy in headless/no-audio-hardware
environments -- this checks perf_counter scheduling only, not audible
output or the (invisible-to-software) speaker latency that calibration
exists to absorb.
"""

import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import pygame

import metronome
from beat_schedule import BeatSchedule, BeatScheduleConfig


def main():
    metronome.init_mixer()
    pygame.init()

    config = BeatScheduleConfig(bpm=80.0)
    m = metronome.Metronome(config)
    start = time.perf_counter() + 0.3
    num_measures = 20  # 40 beats * 0.75s = 30s run
    schedule = BeatSchedule(config, start, num_measures=num_measures)

    m.start(schedule)
    m.join()  # blocks until the schedule finishes naturally

    report = m.drift_report()
    print(f"Beats played: {report['count']}")
    print(f"Mean drift: {report['mean_ms']:.3f} ms")
    print(f"Max abs drift: {report['max_abs_ms']:.3f} ms")

    assert report["count"] == num_measures * config.beats_per_measure, "not all beats played"
    assert report["max_abs_ms"] < 10.0, f"drift exceeded 10ms tolerance: {report['max_abs_ms']}"
    print("PASS: no growing scheduling drift over the run (see caveat above re: speaker latency)")


if __name__ == "__main__":
    main()
