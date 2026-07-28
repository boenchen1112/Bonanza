"""Phase 2: turn a raw gyro-magnitude stream into discrete, timestamped
"ictus events" -- the direction-change/deceleration point right after the
swing's peak, not the peak itself.

All thresholds are named constants below; expect to retune them against
real Phase 0 CSVs before trusting live detection (Section 3 of the build
plan). Nothing here has been tuned against real hardware data yet.
"""

from collections import deque
from dataclasses import dataclass
from enum import Enum, auto

# --- Tunable constants (config block; retune against real Phase 0 CSVs) ---
SMOOTHING_WINDOW = 4  # samples; kills single-sample jitter without flattening the spike.
# NOTE (F10): this delays the detected local min by ~(window-1)/2 samples,
# a *sample-count* lag whose real-time size (window-1)/2 / sample_rate
# changes if the effective poll rate ever changes. Calibration absorbs a
# constant lag, but only for a constant rate -- this is another reason F3's
# single fixed poll rate must hold across capture/calibration/gameplay.
# mag units/SECOND (not per-sample -- process_sample() divides by the actual
# inter-sample dt so this threshold means the same thing regardless of poll
# rate; see F3 in reviews/Bug_Audit_2026-07-17.md). 7500.0 preserves the
# previous per-sample-at-150Hz behavior (50.0/sample / (1/150)s ~= 7500/s).
RISE_RATE_THRESHOLD = 7500.0
# mag units; filters small hand jitter from counting as a swing.
# Effectively vestigial as shipped (H4, Bug_Audit_2026-07-28.md): real
# resting magnitude is single digits to ~50 (swings_counted.csv p05=6,
# p10=53) and real swing peaks are 20,000-33,000, so 200 filters nothing
# RISE_RATE_THRESHOLD hasn't already caught on real hardware. Left
# unchanged rather than raised to match real-hardware scale, because the
# existing test fixtures (test_ictus_detector_smoke.py,
# test_ictus_intensity_invariance.py) use small synthetic peak magnitudes
# (300-800) not to-scale with real captures; raising this without also
# rescaling those fixtures would just replace one untested threshold with a
# broken test suite. Re-derive together if this is ever tightened.
PEAK_MIN_THRESHOLD = 200.0
DROP_FRACTION = 0.6  # qualifying drop: mag falls below this fraction of tracked peak
DROP_WINDOW_S = 0.08  # drop must happen within this long of the peak
REFRACTORY_S = 0.175  # after an ictus, ignore new rises for this long
RISING_TIMEOUT_S = 0.5  # abandon a RISING candidate that never qualifies
MIN_SEEK_TIMEOUT_S = 0.15  # finalize the local min if mag hasn't risen again by this long
# after the drop -- without this, a flat/idle signal after the drop (no
# uptick yet) stalls the search indefinitely and the ictus is only reported
# once the *next* swing's rise begins, which is too late to avoid the
# refractory window swallowing that next swing.


class _State(Enum):
    ARMED = auto()
    RISING = auto()
    POST_DROP_SEEK_MIN = auto()
    REFRACTORY = auto()


@dataclass
class IctusEvent:
    timestamp: float
    peak_magnitude: float
    rise_duration: float
    # Max per-sample positive smoothed-magnitude delta/sec seen during the
    # RISING state. Preferred sharpness input at device sample rates below
    # ~100Hz, where rise_duration quantizes to 1-2 samples and
    # peak/rise_duration becomes unstable (F2, reviews/Bug_Audit_2026-07-17.md).
    max_derivative: float = 0.0


class IctusDetector:
    """Streaming state machine. Feed samples one at a time via process_sample."""

    def __init__(
        self,
        smoothing_window: int = SMOOTHING_WINDOW,
        rise_rate_threshold: float = RISE_RATE_THRESHOLD,
        peak_min_threshold: float = PEAK_MIN_THRESHOLD,
        drop_fraction: float = DROP_FRACTION,
        drop_window_s: float = DROP_WINDOW_S,
        refractory_s: float = REFRACTORY_S,
        rising_timeout_s: float = RISING_TIMEOUT_S,
        min_seek_timeout_s: float = MIN_SEEK_TIMEOUT_S,
    ):
        self.smoothing_window = smoothing_window
        self.rise_rate_threshold = rise_rate_threshold
        self.peak_min_threshold = peak_min_threshold
        self.drop_fraction = drop_fraction
        self.drop_window_s = drop_window_s
        self.refractory_s = refractory_s
        self.rising_timeout_s = rising_timeout_s
        self.min_seek_timeout_s = min_seek_timeout_s

        self._smooth_buf: deque[float] = deque(maxlen=smoothing_window)
        self._prev_smoothed: float | None = None
        self._prev_t: float | None = None
        self._state = _State.ARMED

        self._rise_start_t: float | None = None
        self._peak_mag: float = 0.0
        self._peak_t: float = 0.0
        self._max_derivative: float = 0.0
        self._drop_deadline: float = 0.0
        self._seeking_min_val: float | None = None
        self._seeking_min_t: float | None = None
        self._seek_entered_t: float = 0.0
        self._refractory_until: float = 0.0

    def _smooth(self, raw_mag: float) -> float:
        self._smooth_buf.append(raw_mag)
        return sum(self._smooth_buf) / len(self._smooth_buf)

    def process_sample(self, t: float, raw_mag: float) -> IctusEvent | None:
        mag = self._smooth(raw_mag)
        event = None

        if self._state == _State.REFRACTORY:
            if t >= self._refractory_until:
                # Fall through to ARMED handling below instead of returning
                # early (H6, Bug_Audit_2026-07-28.md): the old early-return
                # meant the very sample that ends the refractory period was
                # never rise-tested, costing up to one sample (~15ms) of
                # detection latency on rapid repeat swings.
                self._state = _State.ARMED
            else:
                self._prev_smoothed = mag
                self._prev_t = t
                return None

        dt = (t - self._prev_t) if self._prev_t is not None else None
        rate = None
        if self._prev_smoothed is not None and dt is not None and dt > 0:
            rate = (mag - self._prev_smoothed) / dt

        if self._state == _State.ARMED:
            if rate is not None and rate > self.rise_rate_threshold:
                self._state = _State.RISING
                self._rise_start_t = t
                self._peak_mag = mag
                self._peak_t = t
                self._max_derivative = rate

        elif self._state == _State.RISING:
            if t - self._rise_start_t > self.rising_timeout_s:
                self._state = _State.ARMED
            else:
                if rate is not None and rate > self._max_derivative:
                    self._max_derivative = rate
                if mag > self._peak_mag:
                    self._peak_mag = mag
                    self._peak_t = t
                elif self._peak_mag >= self.peak_min_threshold and mag <= self._peak_mag * self.drop_fraction:
                    if t - self._peak_t <= self.drop_window_s:
                        self._state = _State.POST_DROP_SEEK_MIN
                        self._seeking_min_val = mag
                        self._seeking_min_t = t
                        self._seek_entered_t = t
                    else:
                        self._state = _State.ARMED

        elif self._state == _State.POST_DROP_SEEK_MIN:
            # Finalize on either a rise (true local min found) or a timeout
            # (signal went flat/idle without rising -- use the min seen so
            # far rather than stalling until the *next* swing's rise, which
            # would let the refractory window swallow it).
            timed_out = (t - self._seek_entered_t) > self.min_seek_timeout_s
            # Strict '<' (D1, Bug_Audit_2026-07-28.md): '<=' let a flat
            # bottom keep advancing _seeking_min_t on every tying sample, so
            # the reported ictus timestamp slid to the *end* of the plateau
            # instead of its start -- a bias that varies with swing style
            # rather than a constant calibration can absorb.
            if mag < self._seeking_min_val and not timed_out:
                self._seeking_min_val = mag
                self._seeking_min_t = t
            else:
                event = IctusEvent(
                    timestamp=self._seeking_min_t,
                    peak_magnitude=self._peak_mag,
                    rise_duration=self._peak_t - self._rise_start_t,
                    max_derivative=self._max_derivative,
                )
                self._refractory_until = t + self.refractory_s
                self._state = _State.REFRACTORY

        self._prev_smoothed = mag
        self._prev_t = t
        return event


def detect_ictuses(samples: list[tuple[float, float]], **detector_kwargs) -> list[IctusEvent]:
    """Offline replay helper: samples is a list of (timestamp, magnitude)."""
    detector = IctusDetector(**detector_kwargs)
    events = []
    for t, mag in samples:
        ev = detector.process_sample(t, mag)
        if ev is not None:
            events.append(ev)
    return events
