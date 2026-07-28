"""Self-contained metronome: schedules and plays click sounds against a
BeatSchedule, in its own thread so the pygame event loop stays responsive.

No sensor logic here (Phase 1 scope) -- this only produces the audible/
visual timing reference and logs actual play() call times for drift
verification.
"""

import array
import math
import threading
import time

import pygame

from beat_schedule import BeatSchedule, BeatScheduleConfig

# Windows time.sleep() has ~15ms granularity; sleep coarsely, then spin for
# the last stretch to hit perf_counter deadlines precisely.
COARSE_SLEEP_MARGIN_S = 0.005


def init_mixer() -> None:
    """Must be called before pygame.init() -- default mixer buffer adds
    60-150ms of output latency that would eat the whole Perfect window."""
    pygame.mixer.pre_init(frequency=44100, buffer=256)


def _make_click_sound(freq_hz: float, duration_s: float = 0.05, volume: float = 0.5) -> "pygame.mixer.Sound":
    sample_rate = 44100
    n_samples = int(sample_rate * duration_s)
    buf = array.array("h")
    amplitude = int(32767 * volume)
    for i in range(n_samples):
        t = i / sample_rate
        # simple decaying sine to avoid a harsh click transient
        envelope = math.exp(-t / (duration_s / 4))
        sample = int(amplitude * envelope * math.sin(2 * math.pi * freq_hz * t))
        buf.append(sample)
        buf.append(sample)  # stereo
    return pygame.mixer.Sound(buffer=buf.tobytes())


class Metronome:
    """Plays a click on every beat, accented on beat 1 (the downbeat)."""

    def __init__(self, config: BeatScheduleConfig | None = None):
        self.config = config or BeatScheduleConfig()
        self._downbeat_sound = _make_click_sound(freq_hz=1200.0)
        self._offbeat_sound = _make_click_sound(freq_hz=700.0)
        self._thread: threading.Thread | None = None
        self._stop_event = threading.Event()
        self.play_log: list[tuple[int, float, float]] = []  # (beat_index, expected, actual)
        self.on_beat = None  # optional callback(beat_index, is_downbeat, actual_time)

    def start(self, schedule: BeatSchedule) -> None:
        self._stop_event.clear()
        self._thread = threading.Thread(target=self._run, args=(schedule,), daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop_event.set()
        if self._thread is not None:
            self._thread.join(timeout=1.0)

    def join(self, timeout: float | None = None) -> None:
        """Block until the schedule finishes naturally (does not request stop)."""
        if self._thread is not None:
            self._thread.join(timeout=timeout)

    def _run(self, schedule: BeatSchedule) -> None:
        for beat_index, expected in enumerate(schedule.all_timestamps()):
            if self._stop_event.is_set():
                return
            self._wait_until(expected)
            if self._stop_event.is_set():
                return
            actual = time.perf_counter()
            is_downbeat = schedule.is_downbeat(beat_index)
            sound = self._downbeat_sound if is_downbeat else self._offbeat_sound
            sound.play()
            self.play_log.append((beat_index, expected, actual))
            if self.on_beat is not None:
                self.on_beat(beat_index, is_downbeat, actual)

    @staticmethod
    def _wait_until(target: float) -> None:
        while True:
            remaining = target - time.perf_counter()
            if remaining <= 0:
                return
            if remaining > COARSE_SLEEP_MARGIN_S:
                time.sleep(remaining - COARSE_SLEEP_MARGIN_S)
            else:
                # Spin for the final stretch, but yield the timeslice each
                # pass (H8, Bug_Audit_2026-07-28.md) -- a bare `pass` here is
                # a tight no-yield spin that can starve the sampling loop on
                # a single-core or heavily loaded machine; sleep(0) costs
                # nothing and removes that risk.
                time.sleep(0)

    def drift_report(self) -> dict:
        """Delta between expected and actual play() time per beat. Measures
        scheduling drift only -- constant speaker-output latency is invisible
        here and is what calibration (4a) exists to absorb."""
        deltas = [actual - expected for _, expected, actual in self.play_log]
        if not deltas:
            return {"count": 0, "mean_ms": None, "max_abs_ms": None}
        return {
            "count": len(deltas),
            "mean_ms": sum(deltas) / len(deltas) * 1000,
            "max_abs_ms": max(abs(d) for d in deltas) * 1000,
        }
