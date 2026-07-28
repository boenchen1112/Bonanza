"""Main game loop: wires together metronome, detector, calibration,
scoring, and pygame rendering into the batting minigame (Section 4d).

The per-measure state machine (MeasureJudge) is kept free of pygame calls
so it can be driven by synthetic events for offline verification without
hardware or a display -- see tests/test_measure_judge_smoke.py.
"""

import os
import time
from dataclasses import dataclass, field

import pygame

import calibration
import ictus_detector
from beat_schedule import BeatSchedule, BeatScheduleConfig
from ictus_detector import IctusDetector, IctusEvent
from metronome import Metronome
from scoring import SharpnessReference, hit_tier, raw_sharpness, timing_tier
from session_log import SessionLog, SwingRecord, WindupSample

MISSES_TO_OUT = 3
SCREEN_SIZE = (800, 480)
# G3 (Bug_Audit_2026-07-28.md): draw on elapsed wall-clock time, not a
# modulo on sample count -- at the device's real ~66Hz rate, every-5th-
# sample was ~13fps (75ms/frame), so popups could appear up to 75ms after
# they were decided, on top of any judging latency.
RENDER_INTERVAL_S = 1.0 / 60.0

# Grace period tick() waits past a measure's window_end before ruling a Miss
# -- only reached when no ictus locked in-window at all; a locked event is
# now judged immediately (G1, Bug_Audit_2026-07-28.md), so this only bounds
# how long the "nothing arrived" case waits to be sure. Derived from the
# detector's own worst-case delivery lag (G2) instead of a flat, unscaled
# 250ms guess: ictus_detector's POST_DROP_SEEK_MIN can legitimately take up
# to MIN_SEEK_TIMEOUT_S after the true local min to finalize, plus
# SMOOTHING_WINDOW's inherent lag, plus one more sample of scheduling slop.
# Device rate measured at ~66Hz in real captures (F3,
# reviews/Bug_Audit_2026-07-17.md).
MEASURED_DEVICE_RATE_HZ = 66.0
_SMOOTHING_LAG_S = (ictus_detector.SMOOTHING_WINDOW - 1) / 2 / MEASURED_DEVICE_RATE_HZ
_ONE_SAMPLE_MARGIN_S = 1.0 / MEASURED_DEVICE_RATE_HZ
JUDGE_GRACE_S = ictus_detector.MIN_SEEK_TIMEOUT_S + _SMOOTHING_LAG_S + _ONE_SAMPLE_MARGIN_S


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
        # Clamp to half_beat_window_s (G2, Bug_Audit_2026-07-28.md): an
        # unclamped grace larger than the inter-window gap lets it extend
        # past the *next* measure's window opening, and submit_ictus()
        # always tests against the current measure_index's window (which
        # hasn't advanced yet) -- genuinely in-window swings for the next
        # measure would get filed as wind-up samples and lost, producing
        # phantom Misses. Only reachable at high BPM with the default grace.
        if judge_grace_s > self.half_beat_window_s:
            import warnings

            warnings.warn(
                f"judge_grace_s ({judge_grace_s * 1000:.0f}ms) exceeds half_beat_window_s "
                f"({self.half_beat_window_s * 1000:.0f}ms) at this tempo; clamping. "
                "Grace can no longer fully cover the detector's worst-case delivery lag."
            )
        self.judge_grace_s = min(judge_grace_s, self.half_beat_window_s)
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
        """Call periodically with the current time. Judges immediately once
        an event has locked in-window (G1, Bug_Audit_2026-07-28.md):
        submit_ictus() locks only the *first* in-window event and ignores
        everything after, so the outcome is already fully determined the
        moment _locked_event is set -- waiting for window_end + grace after
        a lock only delays the result, it can't change it. That wait is
        still needed for the no-event case: only there does the grace
        period matter, to give the detector pipeline time to actually
        deliver an in-window event before ruling a Miss."""
        if self._locked_event is not None:
            record = self._judge()
            self.measure_index += 1
            self._locked_event = None
            return record

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


def _fmt(x: float | None) -> str:
    return f"{x:8.1f}" if x is not None else "    None"


def _export_session_log(session_log: SessionLog) -> None:
    """Persist the round's data on the way out (H3, Bug_Audit_2026-07-28.md).
    export_csv()/export_json() existed but were never called, so every
    round's data -- including the evidence a latency investigation needs --
    died with the process, even on the QUIT path."""
    if not session_log.swings and not session_log.windup_samples:
        return
    out_dir = os.path.join(os.path.dirname(__file__), "..", "session_logs")
    os.makedirs(out_dir, exist_ok=True)
    stamp = time.strftime("%Y%m%d-%H%M%S")
    session_log.export_json(os.path.join(out_dir, f"session_{stamp}.json"))
    session_log.export_csv(os.path.join(out_dir, f"session_{stamp}_swings.csv"))


def run_game(log_swings: bool = True) -> SessionLog:
    """log_swings prints per-swing scoring-vs-latency diagnostics live (not
    just at end-of-round) -- added for Swinger_Build_Plan_v3.md Phase 0's
    latency investigation. Defaults on since `python game.py` is currently
    how that investigation gets run."""
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

    last_draw_time = 0.0
    try:
        # stream() now yields once per poll interval regardless of whether
        # the device returned new data (J2, Bug_Audit_2026-07-28.md): a
        # still controller or a Bluetooth hiccup used to yield nothing at
        # all, so judge.tick()/pygame.event.get()/QUIT handling all silently
        # stalled until the whole stream duration expired. is_new tells new
        # samples (fed to the detector) apart from repeated cached reads
        # (loop-tick only, detector untouched).
        for t, gx, gy, gz, mag, is_new in joycon_stream.stream(
            duration_s=config.max_measures * schedule.beat_interval * 2
        ):
            for event in pygame.event.get():
                if event.type == pygame.QUIT:
                    metronome.stop()
                    _export_session_log(session_log)
                    pygame.quit()
                    return session_log

            if is_new:
                ev = detector.process_sample(t, mag)
                if ev is not None:
                    judge.submit_ictus(ev)
                    if log_swings:
                        # Detector-pipeline lag: how long after the true ictus
                        # (ev.timestamp, the local-min sample) did process_sample()
                        # actually hand back the event, in real wall-clock time (t
                        # is the current sample's read time, i.e. "now" here).
                        # Bounded by SMOOTHING_WINDOW lag + MIN_SEEK_TIMEOUT_S.
                        print(f"    [detector] event t={ev.timestamp:.3f} delivered_lag_ms={(t - ev.timestamp) * 1000:.1f}")

            now = time.perf_counter()
            # Never call judge.tick() once max_measures is reached -- the
            # schedule only holds max_measures*beats_per_measure timestamps, so
            # judging one measure past that indexes off the end (A4).
            if not game_over and judge.measure_index < config.max_measures:
                record = judge.tick(now)
                if record is not None:
                    if log_swings:
                        # Two separate numbers on purpose (Phase 0,
                        # Swinger_Build_Plan_v3.md): corrected_offset_ms is
                        # SCORING correctness (was the swing actually on time,
                        # per calibration); popup_lag_ms is FELT latency (real
                        # wall-clock delay from the swing to this judgment
                        # being available at all, including judge_grace_s).
                        # A "too laggy" complaint could be either -- these
                        # numbers tell them apart instead of guessing.
                        popup_lag_ms = (now - record.ictus_time) * 1000 if record.ictus_time is not None else None
                        print(
                            f"[measure {record.measure_index}] tier={record.timing_tier:8s} hit={record.hit_tier:10s} "
                            f"corrected_offset_ms={_fmt(record.corrected_offset_ms)} "
                            f"popup_lag_ms={_fmt(popup_lag_ms)} "
                            f"judge_grace_s={judge.judge_grace_s}"
                        )
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

            if now - last_draw_time >= RENDER_INTERVAL_S:
                # Popup rendering is a "visible until" flag inside the normal
                # sampling loop rather than a blocking pygame.time.wait(), so
                # samples (and QUIT events) keep flowing while it's shown --
                # otherwise the ~600ms freeze swallows every beat-2 wind-up
                # ictus and every measure's detector timing (F4). Cadence is
                # elapsed wall-clock time, not a modulo on sample count (G3).
                last_draw_time = now
                if popup_record is not None and now < popup_until:
                    _draw_popup(screen, font, popup_record)
                else:
                    popup_record = None
                    screen.fill((20, 20, 40))
                    pygame.display.flip()

            if game_over and (popup_record is None or now >= popup_until):
                break
    finally:
        # Release the HID handle before Settings potentially opens its own
        # (J1, Bug_Audit_2026-07-28.md) -- two concurrent JoyCon connections
        # on Windows HID is a classic source of "calibration silently reads
        # nothing", and it landed precisely on the path a player is directed
        # to when timing feels wrong.
        joycon_stream.close()

    metronome.stop()
    _export_session_log(session_log)
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
