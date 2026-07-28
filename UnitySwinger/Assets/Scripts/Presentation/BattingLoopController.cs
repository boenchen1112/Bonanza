using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Swinger.Input;
using Swinger.Logic;
using UnityEngine;
using UnityEngine.UI;

namespace Swinger.Presentation
{
    // Build Plan v3 Phase 3, second half: wires the ported logic (Phase 2)
    // and the real input stream (Phase 1) into a playable round, driven by
    // Unity's Update loop. One canonical clock domain throughout: the
    // Stopwatch created here is shared with both ClockSync (audio scheduling)
    // and JoyConUdpReceiver (sample timestamps) via UseClock().
    public sealed class BattingLoopController : MonoBehaviour
    {
        [SerializeField] private JoyConUdpReceiver inputSource;
        [SerializeField] private Text judgmentText;
        [SerializeField] private GameObject summaryPanel;
        [SerializeField] private Text summaryText;
        [SerializeField] private Button calibrateButton;
        [SerializeField] private Button playAgainButton;
        [SerializeField] private double schedulingLookaheadSeconds = 0.5;
        [SerializeField] private double popupDurationSeconds = 0.6;

        private Stopwatch _sharedStopwatch;
        private ClockSync _clock;
        private BeatSchedule _schedule;
        private GameConfig _config;
        private IctusDetector _detector;
        private MeasureJudge _judge;
        private SessionLog _sessionLog;
        private SharpnessReference _sharpnessRef;
        private double _calibrationOffsetS;

        private AudioSource[] _sourcePool;
        private AudioClip _downbeatClip;
        private AudioClip _offbeatClip;
        private int _nextUnscheduledBeat;
        private int _nextPoolIndex;
        private bool _calibrated;
        private bool _gameOver;
        private double _popupUntil;
        private bool _inCalibrationFlow;
        private bool _summaryShown;

        private void Awake()
        {
            _sharedStopwatch = Stopwatch.StartNew();
            _clock = new ClockSync(_sharedStopwatch);
            if (inputSource != null)
            {
                inputSource.UseClock(_sharedStopwatch);
            }

            _sourcePool = new AudioSource[4];
            for (int i = 0; i < _sourcePool.Length; i++)
            {
                var go = new GameObject($"BattingVoice{i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                _sourcePool[i] = src;
            }
            _downbeatClip = ClickSoundFactory.MakeClickClip(1200.0);
            _offbeatClip = ClickSoundFactory.MakeClickClip(700.0);

            if (summaryPanel != null) summaryPanel.SetActive(false);
            if (judgmentText != null) judgmentText.text = "";
        }

        private void Start()
        {
            if (calibrateButton != null) calibrateButton.onClick.AddListener(() => StartCoroutine(RunCalibrationFlow()));
            if (playAgainButton != null) playAgainButton.onClick.AddListener(RestartRound);
            StartCoroutine(CalibrateThenBeginRound());
        }

        private IEnumerator CalibrateThenBeginRound()
        {
            while (!_clock.TickCalibration())
            {
                yield return null;
            }
            BeginRound();
        }

        private void BeginRound()
        {
            var (offsetS, sharpnessSeed, _) = CalibrationStore.Load();
            _calibrationOffsetS = offsetS;
            _sharpnessRef = new SharpnessReference(new List<double>(sharpnessSeed));

            _config = new GameConfig();
            _schedule = new BeatSchedule(_config.ScheduleConfig, _clock.CanonicalNow() + 1.0, _config.MaxMeasures);
            _detector = new IctusDetector();
            _judge = new MeasureJudge(_schedule, _calibrationOffsetS, _sharpnessRef);
            _sessionLog = new SessionLog();

            _nextUnscheduledBeat = 0;
            _nextPoolIndex = 0;
            _gameOver = false;
            _summaryShown = false;
            _inCalibrationFlow = false;
            if (summaryPanel != null) summaryPanel.SetActive(false);
            if (judgmentText != null) judgmentText.text = "";
            _calibrated = true;
        }

        private void RestartRound()
        {
            if (summaryPanel != null) summaryPanel.SetActive(false);
            StartCoroutine(CalibrateThenBeginRound());
        }

        private void Update()
        {
            if (!_calibrated || _inCalibrationFlow)
            {
                return;
            }

            double now = _clock.CanonicalNow();

            // Drain input samples through the detector, on the main thread
            // (samples were timestamped on the reader thread at arrival).
            if (inputSource != null)
            {
                while (inputSource.TryDequeue(out var sample))
                {
                    var ev = _detector.ProcessSample(sample.timestamp, sample.magnitude);
                    if (ev != null)
                    {
                        _judge.SubmitIctus(ev);
                    }
                }
            }

            if (!_gameOver)
            {
                ScheduleUpcomingClicks(now);

                if (_judge.MeasureIndex < _config.MaxMeasures)
                {
                    var record = _judge.Tick(now);
                    if (record != null)
                    {
                        _sessionLog.Swings.Add(record);
                        foreach (var w in _judge.WindupSamples) _sessionLog.WindupSamples.Add(w);
                        _judge.WindupSamples.Clear();

                        ShowJudgment(record);
                        if (_judge.IsOut(_config.MissesToOut))
                        {
                            _gameOver = true;
                        }
                    }
                }
                else
                {
                    _gameOver = true;
                }
            }

            if (_gameOver && now >= _popupUntil)
            {
                ShowSummary();
            }

            // Surface a lost Joy-Con signal instead of silently scoring an
            // unbroken string of Misses with no explanation (Phase A.3,
            // Swinger_Build_Plan_v4.md / J2 Unity-side equivalent). Only
            // shown outside an active judgment popup so it doesn't clobber
            // a fresh Perfect/Miss result.
            if (!_gameOver && inputSource != null && inputSource.IsSignalLost && now >= _popupUntil && judgmentText != null)
            {
                judgmentText.text = "Joy-Con signal lost";
            }
        }

        private void ScheduleUpcomingClicks(double now)
        {
            while (_nextUnscheduledBeat < _schedule.Count
                   && _schedule.BeatTime(_nextUnscheduledBeat) <= now + schedulingLookaheadSeconds)
            {
                int beatIndex = _nextUnscheduledBeat;
                bool isDownbeat = _schedule.IsDownbeat(beatIndex);
                var src = _sourcePool[_nextPoolIndex];
                _nextPoolIndex = (_nextPoolIndex + 1) % _sourcePool.Length;
                src.clip = isDownbeat ? _downbeatClip : _offbeatClip;
                src.PlayScheduled(_clock.ToDspTime(_schedule.BeatTime(beatIndex)));
                _nextUnscheduledBeat++;
            }
        }

        private void ShowJudgment(SwingRecord record)
        {
            if (judgmentText != null)
            {
                string line2 = record.HitTier != "Miss" ? $"{record.HitTier}!" : "Miss";
                judgmentText.text = $"{record.TimingTier}\n{line2}";
            }
            _popupUntil = _clock.CanonicalNow() + popupDurationSeconds;
        }

        // Persists the round's data on the way out (Unity-side H3:
        // SessionLog.ExportJson()/ExportCsv() already existed but nothing
        // ever called them, the exact same bug Python's H3 fix addressed --
        // ported the fix, not just the methods). Written under
        // Application.persistentDataPath/session_logs/ alongside
        // calibration.json's own persistence location.
        private void ExportSessionLog()
        {
            if (_sessionLog == null || (_sessionLog.Swings.Count == 0 && _sessionLog.WindupSamples.Count == 0))
            {
                return;
            }
            string outDir = System.IO.Path.Combine(Application.persistentDataPath, "session_logs");
            System.IO.Directory.CreateDirectory(outDir);
            string stamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
            _sessionLog.ExportJson(System.IO.Path.Combine(outDir, $"session_{stamp}.json"));
            _sessionLog.ExportCsv(System.IO.Path.Combine(outDir, $"session_{stamp}_swings.csv"));
        }

        // Covers the "quit/stop mid-round" path too, not just a normal
        // round ending in ShowSummary() -- mirrors Python's QUIT-path
        // export (H3/J2, Bug_Audit_2026-07-28.md: a crash/quit losing the
        // whole SessionLog was exactly the symptom that fix closed).
        // OnApplicationQuit covers a real built player quitting;
        // OnDisable additionally covers stopping Play Mode in the Editor
        // (which does NOT raise OnApplicationQuit) -- the actual
        // playtesting workflow this project uses.
        private void OnApplicationQuit()
        {
            ExportSessionLog();
        }

        private void OnDisable()
        {
            ExportSessionLog();
        }

        private void ShowSummary()
        {
            if (_summaryShown) return;
            _summaryShown = true;
            ExportSessionLog();

            if (summaryPanel != null) summaryPanel.SetActive(true);
            if (judgmentText != null) judgmentText.text = "";
            if (summaryText != null)
            {
                var offsets = _sessionLog.RawOffsetsMs();
                var sharp = _sessionLog.SharpnessValues();
                var variance = _sessionLog.WindupIntervalVariance();
                var sb = new StringBuilder();
                sb.AppendLine("Round over");
                if (offsets.Count > 0) sb.AppendLine($"Raw offsets (ms, uncorrected): mean {offsets.Average():F1}");
                if (sharp.Count > 0) sb.AppendLine($"Sharpness: mean {sharp.Average():F1}");
                if (variance.HasValue) sb.AppendLine($"Wind-up interval variance: {variance.Value:F5}");
                sb.AppendLine($"Outs: {_judge.Misses}/{_config.MissesToOut}");
                summaryText.text = sb.ToString();
            }
        }

        private IEnumerator RunCalibrationFlow()
        {
            _inCalibrationFlow = true;
            if (summaryPanel != null) summaryPanel.SetActive(false);
            if (judgmentText != null) judgmentText.text = "Calibrating...\nSwing on each click";

            var practiceConfig = _config != null ? _config.ScheduleConfig : new BeatScheduleConfig();
            int practiceMeasures = Calibration.PracticeMeasures;
            double startT = _clock.CanonicalNow() + 1.0;
            var practiceSchedule = new BeatSchedule(practiceConfig, startT, practiceMeasures);
            var practiceDetector = new IctusDetector();
            double halfWindow = practiceSchedule.BeatInterval / 2.0;

            var practiceIctusTimes = new List<double?>();
            var practiceExpectedTimes = new List<double>();
            var sharpnessSamples = new List<double>();

            int measureIdx = 0;
            int nextBeat = 0;
            int poolIdx = 0;
            IctusEvent locked = null;

            while (measureIdx < practiceMeasures)
            {
                double now = _clock.CanonicalNow();

                while (nextBeat < practiceSchedule.Count
                       && practiceSchedule.BeatTime(nextBeat) <= now + schedulingLookaheadSeconds)
                {
                    bool isDown = practiceSchedule.IsDownbeat(nextBeat);
                    var src = _sourcePool[poolIdx];
                    poolIdx = (poolIdx + 1) % _sourcePool.Length;
                    src.clip = isDown ? _downbeatClip : _offbeatClip;
                    src.PlayScheduled(_clock.ToDspTime(practiceSchedule.BeatTime(nextBeat)));
                    nextBeat++;
                }

                if (inputSource != null)
                {
                    while (inputSource.TryDequeue(out var sample))
                    {
                        var ev = practiceDetector.ProcessSample(sample.timestamp, sample.magnitude);
                        if (ev != null && locked == null)
                        {
                            double center = practiceSchedule.MeasureBeat1Time(measureIdx);
                            double lo = center - halfWindow, hi = center + halfWindow;
                            if (ev.Timestamp >= lo && ev.Timestamp <= hi)
                            {
                                locked = ev;
                            }
                        }
                    }
                }

                double windowEnd = practiceSchedule.MeasureBeat1Time(measureIdx) + halfWindow;
                if (now >= windowEnd + MeasureJudge.DefaultJudgeGraceS)
                {
                    double beat1 = practiceSchedule.MeasureBeat1Time(measureIdx);
                    practiceExpectedTimes.Add(beat1);
                    if (locked != null)
                    {
                        practiceIctusTimes.Add(locked.Timestamp);
                        double? maxDeriv = locked.MaxDerivative != 0.0 ? locked.MaxDerivative : (double?)null;
                        sharpnessSamples.Add(Scoring.RawSharpness(locked.PeakMagnitude, locked.RiseDuration, maxDeriv));
                    }
                    else
                    {
                        practiceIctusTimes.Add(null);
                    }
                    locked = null;
                    measureIdx++;
                    if (judgmentText != null) judgmentText.text = $"Calibrating...\n{measureIdx}/{practiceMeasures}";
                }

                // Signal-lost feedback during calibration too (Phase A.3):
                // this loop already advances on wall-clock time regardless
                // of whether samples arrive, so it can't literally hang --
                // but with no input at all it would otherwise tick through
                // all practiceMeasures silently recording misses, with
                // nothing telling the player *why* calibration is about to
                // fail (they'd only find out afterward via the "not enough
                // swings detected" result). Skip whenever the per-measure
                // "N/10" text was just written above so it isn't clobbered.
                if (inputSource != null && inputSource.IsSignalLost && judgmentText != null)
                {
                    judgmentText.text = $"Joy-Con signal lost\nCalibrating... {measureIdx}/{practiceMeasures}";
                }

                yield return null;
            }

            var result = Calibration.ComputeOffset(practiceIctusTimes, practiceExpectedTimes,
                practiceSchedule.BeatInterval, fallbackOffsetS: _calibrationOffsetS);
            CalibrationStore.Save(result.OffsetS, sharpnessSamples);
            _calibrationOffsetS = result.OffsetS;

            if (judgmentText != null) judgmentText.text = $"Calibration saved.\noffset={result.OffsetS * 1000:F1}ms";
            yield return new WaitForSecondsRealtime(1.5f);
            if (judgmentText != null) judgmentText.text = "";

            _inCalibrationFlow = false;
            RestartRound();
        }
    }
}
