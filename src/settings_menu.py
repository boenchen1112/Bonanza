"""Post-round Settings menu + Calibrate entry point (Section 4a/4d).

Presented after a round ends. Calibration is never automatic -- this is
the only place it can be triggered from.
"""

import time

import pygame

import calibration
from beat_schedule import BeatSchedule, BeatScheduleConfig
from ictus_detector import IctusDetector
from metronome import Metronome
from scoring import raw_sharpness

FONT_NAME = None  # default pygame font
BG_COLOR = (20, 20, 30)
TEXT_COLOR = (230, 230, 230)
HILITE_COLOR = (255, 210, 80)


def _draw_menu(screen, font, options: list[str], selected: int, title: str) -> None:
    screen.fill(BG_COLOR)
    title_surf = font.render(title, True, TEXT_COLOR)
    screen.blit(title_surf, (40, 40))
    for i, opt in enumerate(options):
        color = HILITE_COLOR if i == selected else TEXT_COLOR
        surf = font.render(opt, True, color)
        screen.blit(surf, (60, 100 + i * 40))
    pygame.display.flip()


def run_settings_menu(screen, clock, joycon_stream_factory, config: BeatScheduleConfig) -> None:
    """Simple keyboard-navigable menu: Calibrate / Back.

    joycon_stream_factory: zero-arg callable returning a JoyConStream
    instance (hardware-gated -- pass a factory so this module doesn't
    import joycon_stream at module load time and fail without hardware).
    """
    font = pygame.font.Font(FONT_NAME, 28)
    options = ["Calibrate", "Back"]
    selected = 0
    running = True

    while running:
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                return
            if event.type == pygame.KEYDOWN:
                if event.key == pygame.K_UP:
                    selected = (selected - 1) % len(options)
                elif event.key == pygame.K_DOWN:
                    selected = (selected + 1) % len(options)
                elif event.key == pygame.K_RETURN:
                    if options[selected] == "Calibrate":
                        run_calibration(screen, clock, joycon_stream_factory, config)
                    else:
                        running = False
                elif event.key == pygame.K_ESCAPE:
                    running = False

        _draw_menu(screen, font, options, selected, "Settings")
        clock.tick(60)


def run_calibration(screen, clock, joycon_stream_factory, config: BeatScheduleConfig) -> None:
    """Runs PRACTICE_MEASURES practice measures, finds the closest ictus to
    each expected beat-1, computes and persists the offset, and seeds the
    sharpness reference distribution from the same swings.
    """
    font = pygame.font.Font(FONT_NAME, 28)

    try:
        joycon_stream = joycon_stream_factory()
    except RuntimeError as e:
        _draw_menu(screen, font, [f"Calibration needs hardware: {e}", "Press any key to return"], -1, "Calibrate")
        _wait_for_keypress()
        return

    metronome = Metronome(config)
    start_time = time.perf_counter() + 1.0
    schedule = BeatSchedule(config, start_time, num_measures=calibration.PRACTICE_MEASURES)
    detector = IctusDetector()

    detected_ictuses: list = []  # IctusEvent

    def on_beat(beat_index, is_downbeat, actual_time):
        pass  # visuals could hook here; not required for calibration itself

    metronome.on_beat = on_beat
    metronome.start(schedule)

    end_time = schedule.all_timestamps()[-1] + config.beats_per_measure * (60.0 / config.bpm)
    frame_counter = 0
    try:
        # is_new: stream() now heartbeats every poll interval regardless of
        # duplicates (J2, Bug_Audit_2026-07-28.md) so QUIT handling and
        # rendering keep running even on a still controller; only feed
        # genuinely fresh samples to the detector.
        for t, gx, gy, gz, mag, is_new in joycon_stream.stream(duration_s=end_time - time.perf_counter()):
            if is_new:
                ev = detector.process_sample(t, mag)
                if ev is not None:
                    detected_ictuses.append(ev)

            for event in pygame.event.get():
                if event.type == pygame.QUIT:
                    metronome.stop()
                    return

            # Draw at a fixed cadence rather than gating the sample loop on
            # clock.tick() -- stream() already paces polling (F3); throttling
            # the sampling loop itself here re-introduces the below-device-rate
            # drop that motivated this fix.
            frame_counter += 1
            if frame_counter % 10 == 0:
                _draw_menu(
                    screen, font,
                    [f"Calibrating... swing on beat 1 ({len(detected_ictuses)} detected)"],
                    -1, "Calibrate",
                )
    finally:
        joycon_stream.close()

    metronome.stop()

    beat1_times = [schedule.measure_beat1_time(m) for m in range(calibration.PRACTICE_MEASURES)]
    closest_event_per_measure = []  # list[IctusEvent | None]
    for expected in beat1_times:
        candidates = [ev for ev in detected_ictuses if abs(ev.timestamp - expected) <= (60.0 / config.bpm)]
        closest_event_per_measure.append(
            min(candidates, key=lambda ev: abs(ev.timestamp - expected)) if candidates else None
        )
    closest_times = [ev.timestamp if ev is not None else None for ev in closest_event_per_measure]

    result = calibration.compute_offset(closest_times, beat1_times, 60.0 / config.bpm)
    # Drive the sharpness seed and the save-guard off the events
    # compute_offset() actually used (survived the +-half-beat outlier
    # filter), not every closest-per-measure match (C1, Bug_Audit_2026-07-28.md).
    # The old matched_events was pre-filter, so a wildly mistimed swing that
    # compute_offset() correctly rejected for the offset was still admitted
    # into the sharpness reference, and a calibration where only one swing
    # survived the outlier bound but three matched loosely could still
    # overwrite calibration.json.
    used_events = [closest_event_per_measure[i] for i in result.used_indices]

    if result.from_default or len(used_events) < 3:
        # Don't clobber a previously saved good calibration with an empty
        # or noise-derived one (F5) -- leave calibration.json untouched.
        _draw_menu(
            screen,
            font,
            [
                "Calibration didn't take -- not enough swings detected.",
                "Previous calibration (if any) was kept. Press any key to return.",
            ],
            -1,
            "Calibrate",
        )
        _wait_for_keypress()
        return

    # Seed the sharpness reference only from the swings actually matched to
    # a beat-1 window, not every detected ictus (rebounds/noise) -- F6.
    sharpness_seed = [
        raw_sharpness(ev.peak_magnitude, ev.rise_duration, ev.max_derivative or None) for ev in used_events
    ]
    calibration.save_calibration(result.offset_s, sharpness_seed)

    _draw_menu(
        screen,
        font,
        [f"Calibration complete. Offset: {result.offset_s * 1000:.1f} ms", "Press any key to return"],
        -1,
        "Calibrate",
    )
    _wait_for_keypress()


def _wait_for_keypress() -> None:
    waiting = True
    while waiting:
        for event in pygame.event.get():
            if event.type in (pygame.QUIT, pygame.KEYDOWN):
                waiting = False
