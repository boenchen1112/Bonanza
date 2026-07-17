using System;
using System.Collections;
using System.Collections.Generic;
using Swinger.Logic;
using UnityEngine;

namespace Swinger.Presentation
{
    // Metronome-only milestone (Build Plan v3 Phase 3, first half): schedules
    // clicks on the audio clock via AudioSource.PlayScheduled, logs
    // expected-vs-measured-offset drift in the canonical CPU-clock domain
    // over a long run, before any batting logic is added on top.
    public sealed class MetronomeController : MonoBehaviour
    {
        [SerializeField] private double bpm = 80.0;
        [SerializeField] private int beatsPerMeasure = 2;
        [SerializeField] private int numMeasures = 100; // ~150s at 80bpm 2/4 -- comfortably over the 2min exit criterion
        [SerializeField] private double leadInSeconds = 1.0;
        [SerializeField] private double schedulingLookaheadSeconds = 0.5;
        [SerializeField] private double driftLogIntervalSeconds = 5.0;

        private ClockSync _clock;
        private BeatSchedule _schedule;
        private AudioSource[] _sourcePool;
        private AudioClip _downbeatClip;
        private AudioClip _offbeatClip;
        private int _nextUnscheduledBeat;
        private int _nextPoolIndex;
        private double _lastDriftLogCanonical = double.NegativeInfinity;
        private bool _calibrated;
        private readonly List<(int beat, double expected, double offsetAtSchedule)> _scheduleLog = new List<(int, double, double)>();

        private void Awake()
        {
            _clock = new ClockSync();

            _downbeatClip = MakeClickClip(1200.0);
            _offbeatClip = MakeClickClip(700.0);

            _sourcePool = new AudioSource[4];
            for (int i = 0; i < _sourcePool.Length; i++)
            {
                var go = new GameObject($"MetronomeVoice{i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                _sourcePool[i] = src;
            }
        }

        private void Start()
        {
            StartCoroutine(CalibrateThenSchedule());
        }

        // dspTime only ticks once per audio buffer callback (a separate
        // thread), so calibration is spread across several real frames
        // instead of a blocking spin -- see ClockSync's doc comment.
        private IEnumerator CalibrateThenSchedule()
        {
            while (!_clock.TickCalibration())
            {
                yield return null;
            }

            var config = new BeatScheduleConfig(bpm, beatsPerMeasure);
            _schedule = new BeatSchedule(config, _clock.CanonicalNow() + leadInSeconds, numMeasures);

            Debug.Log($"[Metronome] ClockSync startup offset (dsp - canonical) = {_clock.StartupOffset:F6}s, "
                + $"schedule: {_schedule.Count} beats over {numMeasures} measures, beatInterval={_schedule.BeatInterval:F4}s");

            _calibrated = true;
        }

        private void Update()
        {
            if (!_calibrated)
            {
                return;
            }

            double now = _clock.CanonicalNow();

            // Schedule any upcoming beats that fall within the lookahead window
            // and have not been scheduled yet.
            while (_nextUnscheduledBeat < _schedule.Count
                   && _schedule.BeatTime(_nextUnscheduledBeat) <= now + schedulingLookaheadSeconds)
            {
                int beatIndex = _nextUnscheduledBeat;
                double expected = _schedule.BeatTime(beatIndex);
                bool isDownbeat = _schedule.IsDownbeat(beatIndex);

                var src = _sourcePool[_nextPoolIndex];
                _nextPoolIndex = (_nextPoolIndex + 1) % _sourcePool.Length;
                src.clip = isDownbeat ? _downbeatClip : _offbeatClip;
                double dspTime = _clock.ToDspTime(expected);
                src.PlayScheduled(dspTime);

                _scheduleLog.Add((beatIndex, expected, _clock.StartupOffset));
                _nextUnscheduledBeat++;
            }

            // Periodic drift check: how far has the live dsp<->canonical offset
            // moved from the one actually used for scheduling at startup.
            if (now - _lastDriftLogCanonical >= driftLogIntervalSeconds)
            {
                _lastDriftLogCanonical = now;
                double liveOffset = _clock.MeasureCurrentOffset();
                double driftMs = (liveOffset - _clock.StartupOffset) * 1000.0;
                Debug.Log($"[Metronome] t={now:F1}s beat={_nextUnscheduledBeat}/{_schedule.Count} "
                    + $"liveOffset={liveOffset:F6}s startupOffset={_clock.StartupOffset:F6}s drift={driftMs:F3}ms");
            }

            if (_nextUnscheduledBeat >= _schedule.Count && _scheduleLog.Count == _schedule.Count)
            {
                LogFinalSummary();
                enabled = false;
            }
        }

        private void LogFinalSummary()
        {
            double finalLiveOffset = _clock.MeasureCurrentOffset();
            double totalDriftMs = (finalLiveOffset - _clock.StartupOffset) * 1000.0;
            double runDurationS = _scheduleLog.Count > 0
                ? _scheduleLog[_scheduleLog.Count - 1].expected - _schedule.StartTime
                : 0.0;
            Debug.Log($"[Metronome] DONE. {_scheduleLog.Count} beats scheduled over {runDurationS:F1}s. "
                + $"Final live-vs-startup dsp<->canonical offset drift = {totalDriftMs:F3}ms. "
                + "Exit criterion: this run's drift, logged above at each interval, should stay small and "
                + "bounded (not monotonically growing) over the full run.");
        }

        private static AudioClip MakeClickClip(double freqHz, double durationS = 0.05, float volume = 0.5f)
        {
            const int sampleRate = 44100;
            int nSamples = (int)(sampleRate * durationS);
            var data = new float[nSamples * 2]; // stereo interleaved
            for (int i = 0; i < nSamples; i++)
            {
                double t = i / (double)sampleRate;
                double envelope = Math.Exp(-t / (durationS / 4.0));
                float sample = (float)(volume * envelope * Math.Sin(2 * Math.PI * freqHz * t));
                data[i * 2] = sample;
                data[i * 2 + 1] = sample;
            }
            var clip = AudioClip.Create($"click_{freqHz}", nSamples, 2, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
