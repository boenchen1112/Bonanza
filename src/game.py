"""Main game loop: wires together metronome, detector, calibration,
scoring, and pygame rendering into the batting minigame (Section 4d).

The per-measure state machine (MeasureJudge) is kept free of pygame calls
so it can be driven by synthetic events for offline verification without
hardware or a display -- see tests/test_measure_judge_smoke.py.
"""

import time
from dataclasses import dataclass, field

import pygame

import calibration
from beat_schedule import BeatSchedule, BeatScheduleConfig
from ictus_detector import IctusDetector, IctusEvent
from metronome import Metronome
from scoring import SharpnessReference, hit_tier, raw_sharpness, timing_tier
from session_log import SessionLog, SwingRecord, WindupSample

MISSES_TO_OUT = 3
SCREEN_SIZE = (800, 480)

# Grace period tick() waits past a measure's window_end before judging.
# ictus_detector's POST_DROP_SEEK_MIN can legitimately take up to
# MIN_SEEK_TIMEOUT_S (150ms) plus smoothing lag (~20ms at typical rates)
# after the true local min before it finalizes and returns the IctusEvent
# -- without this margin, a genuinely in-window swing near the later half
# of the window can get judged (and misfiled as the *next* measure's
# windup sample once it finally arrives) before its own event is even
# delivered. Found via real-hardware playtest; see primer.md.
JUDGE_GRACE_S = 0.25


@dataclass
class GameConfig:
    schedule_config: BeatScheduleConfig = field(default_factory=BeatScheduleConfig)
    misses_to_out: int = MISSES_TO_OUT
    max_measures: int = 500  # generous upper bound; game ends on outs first


class MeasureJudge:
    """Per-measure WINDUP -> EXPECTING_ICTUS -> JUDGE state machine.

    Pure logic, no pygame/hardware dependency, so it's unit-testable with
    synthetic (timestamp, magnitude) or IctusEvent streams.
    """

    def __init__(
        self,
        schedule: BeatSchedule,
        calibration_offset_s: float,
        sharpness_ref: SharpnessReference,
        judge_grace_s: float = JUDGE_GRACE_S,
    ):
        self.schedule = schedule
        self.calibration_offset_s = calibration_offset_s
        self.sharpness_ref = sharpness_ref
        self.half_beat_window_s = schedule.beat_interval / 2.0
        self.judge_grace_s = judge_grace_s
        self.measure_index = 0
        self._locked_event: IctusEvent | None = None
        self.finished_measures: list[SwingRecord] = []
        self.windup_samples: list[WindupSample] = []
        self.misses = 0

    def _window_bounds(self, measure_index: int) -> tuple[float, float]:
        # Centered on the calibration-corrected beat-1 time, not raw beat1,
        # so the window stays symmetric around where the player is actually
        # expected to land once the offset is applied (F7).
        center = self.schedule.measure_beat1_time(measure_index) + self.calibration_offset_s
        return center - self.half_beat_window_s, center + self.half_beat_window_s

    def submit_ictus(self, event: IctusEvent) -> None:
        lo, hi = self._window_bounds(self.measure_index)
        if lo <= event.timestamp <= hi:
            if self._locked_event is None:
                self._locked_event = event
        else:
            self.windup_samples.append(WindupSample(self.measure_index, event.timestamp))

    def tick(self, now: float) -> SwingRecord | None:
        """Call periodically with the current time. Returns a SwingRecord
        once the current measure's window has closed AND judge_grace_s has
        passed -- the grace period gives the detector pipeline time to
        actually deliver an in-window event before judging locks it out."""
        _, window_end = self._window_bounds(self.measure_index)
        if now < window_end + self.judge_grace_s:
            return None
        record = self._judge()
        self.measure_index += 1
        self._locked_event = None
        return record

    def _judge(self) -> SwingRecord:
        beat1 = self.schedule.measure_beat1_time(self.measure_index)
        event = self._locked_event

        if event is None:
            record = SwingRecord(
                measure_index=self.measure_index,
                expected_time=beat1,
                ictus_time=None,
                raw_offset_ms=None,
                corrected_offset_ms=None,
                timing_tier="Miss",
                peak_magnitude=None,
                rise_duration=None,
                sharpness_raw=None,
                sharpness_bucket=None,
                hit_tier="Miss",
            )
            self.misses += 1
            self.finished_measures.append(record)
            return record

        raw_offset_s = event.timestamp - beat1
        corrected_offset_s = raw_offset_s - self.calibration_offset_s
        tier = timing_tier(corrected_offset_s * 1000)

        sharp_raw = raw_sharpness(event.peak_magnitude, event.rise_duration, event.max_derivative or None)
        bucket = None
        hit = "Miss"
        if tier != "Miss":
            bucket = self.sharpness_ref.bucket(sharp_raw)
            self.sharpness_ref.add(sharp_raw)
            hit = hit_tier(tier, bucket)
        else:
            self.misses += 1

        record = SwingRecord(
            measure_index=self.measure_index,
            expected_time=beat1,
            ictus_time=event.timestamp,
            raw_offset_ms=raw_offset_s * 1000,
            corrected_offset_ms=corrected_offset_s * 1000,
            timing_tier=tier,
            peak_magnitude=event.peak_magnitude,
            rise_duration=event.rise_duration,
            sharpness_raw=sharp_raw,
            sharpness_bucket=bucket,
            hit_tier=hit,
        )
        self.finished_measures.append(record)
        return record

    def is_out(self, misses_to_out: int) -> bool:
        return self.misses >= misses_to_out


def _draw_popup(screen, font, record: SwingRecord) -> None:
    screen.fill((15, 60, 20))
    line1 = f"{record.timing_tier}"
    line2 = f"{record.hit_tier}!" if record.hit_tier != "Miss" else "Miss"
    screen.blit(font.render(line1, True, (255, 255, 255)), (60, 180))
    screen.blit(font.render(line2, True, (255, 220, 100)), (60, 230))
    pygame.display.flip()


def _draw_summary(screen, font, log: SessionLog) -> None:
    screen.fill((10, 10, 20))
    y = 20
    screen.blit(font.render("Round over", True, (255, 255, 255)), (30, y))
    y += 40
    offsets = log.raw_offsets_ms()
    if offsets:
        mean_offset = sum(offsets) / len(offsets)
        screen.blit(
            font.render(f"Raw offsets (ms, uncorrected): mean {mean_offset:.1f}", True, (200, 200, 200)),
            (30, y),
        )
        y += 30
    sharp = log.sharpness_values()
    if sharp:
        screen.blit(
            font.render(f"Sharpness: mean {sum(sharp) / len(sharp):.1f}", True, (200, 200, 200)),
            (30, y),
        )
        y += 30
    variance = log.windup_interval_variance()
    if variance is not None:
        screen.blit(
            font.render(f"Wind-up interval variance: {variance:.5f}", True, (150, 150, 200)),
            (30, y),
        )
        y += 30
    screen.blit(font.render("Press S for Settings, any other key to exit", True, (180, 180, 180)), (30, y + 20))
    pygame.display.flip()


def _joycon_stream_factory():
    from joycon_stream import JoyConStream

    return JoyConStream()


def run_game() -> SessionLog:
    from metronome import init_mixer

    init_mixer()
    pygame.init()
    screen = pygame.display.set_mode(SCREEN_SIZE)
    pygame.display.set_caption("All-Star Swingers")
    clock = pygame.time.Clock()
    font = pygame.font.Font(None, 36)

    config = GameConfig()
    calibration_offset_s, sharpness_seed, _ = calibration.load_calibration()
    sharpness_ref = SharpnessReference(values=list(sharpness_seed))

    try:
        joycon_stream = _joycon_stream_factory()
    except RuntimeError as e:
        screen.fill((40, 10, 10))
        screen.blit(font.render(f"No Joy-Con: {e}", True, (255, 120, 120)), (20, 200))
        pygame.display.flip()
        time.sleep(3)
        pygame.quit()
        return SessionLog()

    start_time = time.perf_counter() + 1.0
    schedule = BeatSchedule(config.schedule_config, start_time, config.max_measures)
    metronome = Metronome(config.schedule_config)
    metronome.start(schedule)

    detector = IctusDetector()
    judge = MeasureJudge(schedule, calibration_offset_s, sharpness_ref)
    session_log = SessionLog()

    POPUP_DURATION_S = 0.6
    popup_record: SwingRecord | None = None
    popup_until = 0.0
    game_over = False

    frame_counter = 0
    for t, gx, gy, gz, mag in joycon_stream.stream(duration_s=config.max_measures * schedule.beat_interval * 2):
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                metronome.stop()
                pygame.quit()
                return session_log

        ev = detector.process_sample(t, mag)
        if ev is not None:
            judge.submit_ictus(ev)

        now = time.perf_counter()
        # Never call judge.tick() once max_measures is reached -- the
        # schedule only holds max_measures*beats_per_measure timestamps, so
        # judging one measure past that indexes off the end (A4).
        if not game_over and judge.measure_index < config.max_measures:
            record = judge.tick(now)
            if record is not None:
                session_log.swings.append(record)
                popup_record = record
                popup_until = now + POPUP_DURATION_S
                if judge.is_out(config.misses_to_out):
                    game_over = True
        elif not game_over:
            game_over = True

        for w in judge.windup_samples:
            session_log.windup_samples.append(w)
        judge.windup_samples.clear()

        frame_counter += 1
        if frame_counter % 5 == 0:
            # Popup rendering is a "visible until" flag inside the normal
            # sampling loop rather than a blocking pygame.time.wait(), so
            # samples (and QUIT events) keep flowing while it's shown --
            # otherwise the ~600ms freeze swallows every beat-2 wind-up
            # ictus and every measure's detector timing (F4).
            if popup_record is not None and now < popup_until:
                _draw_popup(screen, font, popup_record)
            else:
                popup_record = None
                screen.fill((20, 20, 40))
                pygame.display.flip()

        if game_over and (popup_record is None or now >= popup_until):
            break

    metronome.stop()
    _draw_summary(screen, font, session_log)

    waiting = True
    while waiting:
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                waiting = False
            if event.type == pygame.KEYDOWN:
                if event.key == pygame.K_s:
                    import settings_menu

                    settings_menu.run_settings_menu(screen, clock, _joycon_stream_factory, config.schedule_config)
                waiting = False
        clock.tick(30)

    pygame.quit()
    return session_log


if __name__ == "__main__":
    run_game()
