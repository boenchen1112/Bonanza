// Plays the rendered score on the audio clock.
//
// Each track is a set of cumulative intensity mixes (i0..i3) of equal length.
// All of them start together with one PlayScheduled and stay in step; only the
// current level is audible. A level change is a volume swap on a bar line, so
// there is never a seek (the web crossfaded the same way with bar-quantised
// layer gates). Dedicated sources, not the SFX pool: the pool steals voices.
//
// Time: a deck is anchored at a GAME time (the audio time the track's first
// beat should sound) and converted to dspTime once, with the same DspAt() the
// SFX use, minus the render's beat-0 offset. A game also supplies its own
// beat <-> time map, so the playing position is checked against the game's
// beat each frame and a drift that holds is corrected (a seek). Finale
// Fever's tempo ramp is rendered into its file (MusicTrackInfo.Ramped), so it
// plays at pitch 1 and its position is the game's time since the track's
// first beat, like every other track.
//
// A pause is AudioListener.pause: Unity's WebGL layer pauses every channel at
// its exact position and resumes it from there, with dspTime frozen in
// between, so the music is back on the grid with no help (a restart at the
// resume was measured: Unity re-pauses and resumes the new channels itself,
// so a PlayScheduled at that instant is overridden, and one a few frames
// later is no more exact than the resume). What can go wrong is the clock
// the game reads (AudioClock.Rebase); OnResume only checks the position.
//
// The chart-timing path never reads anything from here.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bonanza
{
    public sealed class MusicPlayer : MonoBehaviour
    {
        /// Mix level at music volume 1.0. The default 75% gives 0.425: the level the music had at the old fixed 0.5 under an 0.85 master.
        public const float BaseGain = 0.425f / Settings.DefaultMusic;
        const int Slots = 4;
        const float SwapSeconds = 0.02f;      // volume swap between levels around the bar line
        const double HardResync = 0.08;       // seconds of drift before a seek (looping tracks)
        const double RampedResync = 0.015;    // ... the ramped track plays at a constant pitch: hold it tighter
        const float SeekAfter = 0.5f;         // ... that has lasted this long
        const double LoadTimeout = 6;

        sealed class Deck
        {
            public MusicTrackInfo Info;
            public AudioClip[] Clips;
            public AudioSource[] Src = new AudioSource[Slots];
            public bool Active, Started, Fading;
            public double BeatZeroGame;          // game time at which file beat 0 sounds
            public double StartDsp;
            public Func<double> FileBeat;        // game: music beats now; null: derived from time
            public float Gain = 1, FadeRate;     // fade multiplier, units per second when fading
            public float[] Level = new float[Slots];
            public int Current, Pending;
            public int LastBar = int.MinValue;
            public double PlayedAt;
            public bool Adaptive;
            // verification statistics (see LogStats)
            public readonly List<float> ErrBeat = new List<float>(), ErrDsp = new List<float>();
            public int Resyncs;
            public double Bias;                  // the position getter's offset since a pause (see OnResume)
            public float CalAt = -1;
            public int ResumeMarkDsp;
            public double ResumeErr = double.NaN;   // raw getter error half a second after a resume
            public int ResumeMark = -1;          // ErrBeat/ErrDsp length when the deck was restarted after a pause
            public float OverSince = -1;         // unscaled time the error first went past the seek threshold
            public float SeekHold;               // no position checks until this unscaled time (a seek lands late)
        }

        GameHost _host;
        MusicLibrary _lib;
        readonly Deck[] _decks = new Deck[2];
        Deck _cur;
        readonly MusicIntensity _intensity = new MusicIntensity();
        float _duck = 1, _duckTarget = 1, _musicVol = Settings.DefaultMusic;
        readonly HashSet<string> _requested = new HashSet<string>();

        public bool Available => _lib != null;
        public string Current => _cur != null && _cur.Active && !_cur.Fading ? _cur.Info.Id : null;
        public int Level => _cur?.Current ?? -1;
        /// Last measured position error of the live deck against the game's beat grid, in seconds.
        public double LastError { get; private set; }

        public void Init(GameHost host)
        {
            _host = host;
            _lib = Resources.Load<MusicLibrary>("MusicLibrary");
            if (_lib == null) { Debug.LogError("[MUSIC] Resources/MusicLibrary missing: no music"); return; }
            for (int d = 0; d < _decks.Length; d++)
            {
                var deck = _decks[d] = new Deck();
                for (int i = 0; i < Slots; i++)
                {
                    var go = new GameObject($"Music{d}.{i}");
                    go.transform.SetParent(transform, false);
                    var s = deck.Src[i] = go.AddComponent<AudioSource>();
                    s.playOnAwake = false;
                    s.spatialBlend = 0;
                    s.priority = 0;
                    s.bypassReverbZones = true;
                }
            }
        }

        // ------------------------------------------------------------ loading

        /// Start decompressing a track's clips (idempotent). Call from Load().
        public void Prepare(string id)
        {
            if (_lib == null) return;
            var e = _lib.Find(id);
            if (e == null) { Debug.LogError($"[MUSIC] no clips for {id}"); return; }
            // Keep the shell tracks and this one; drop other games' samples.
            foreach (var other in _lib.entries)
            {
                if (other.id == id || other.id == "title" || other.id == "results") continue;
                if (!_requested.Contains(other.id) || IsPlaying(other.id)) continue;
                foreach (var c in other.mixes) if (c) c.UnloadAudioData();
                _requested.Remove(other.id);
            }
            if (!_requested.Add(id)) return;
            foreach (var c in e.mixes)
            {
                if (c == null) { Debug.LogError($"[MUSIC] null clip in {id}"); continue; }
                c.LoadAudioData();
            }
        }

        bool IsPlaying(string id)
        {
            foreach (var d in _decks) if (d != null && d.Active && d.Info.Id == id) return true;
            return false;
        }

        /// All of a track's samples are decoded.
        public bool Ready(string id)
        {
            if (_lib == null) return true;   // already logged; do not hold the game up
            var e = _lib.Find(id);
            if (e == null) return true;
            foreach (var c in e.mixes) if (c == null || c.loadState == AudioDataLoadState.Loading || c.loadState == AudioDataLoadState.Unloaded) return false;
            return true;
        }

        public bool Failed(string id)
        {
            var e = _lib?.Find(id);
            if (e == null) return false;
            foreach (var c in e.mixes) if (c != null && c.loadState == AudioDataLoadState.Failed) return true;
            return false;
        }

        // ------------------------------------------------------------ transport

        /// A round's track: file beat 0 sounds at game time `beatZeroGame`.
        /// `fileBeat` reports the music beat the game is at now (game beat + 8);
        public void PlayGame(string id, double beatZeroGame, Func<double> fileBeat)
        {
            Begin(id, beatZeroGame, fileBeat, true);
            _intensity.Reset();
        }

        /// A shell track (title, results): starts now, free-running.
        public void PlayShell(string id)
        {
            Begin(id, _host.Now + 0.18, null, false);
        }

        void Begin(string id, double beatZeroGame, Func<double> fileBeat, bool adaptive)
        {
            if (_lib == null) return;
            var e = _lib.Find(id);
            var info = MusicTracks.Find(id);
            if (e == null || info == null) { Debug.LogError($"[MUSIC] unknown track {id}"); return; }
            Stop(0.25f);
            Prepare(id);
            Deck deck = null;
            foreach (var d in _decks) if (!d.Active) { deck = d; break; }
            if (deck == null) { deck = _decks[0] == _cur ? _decks[1] : _decks[0]; HardStop(deck); }
            deck.Info = info;
            deck.Clips = e.mixes;
            deck.Active = true; deck.Started = false; deck.Fading = false;
            deck.BeatZeroGame = beatZeroGame;
            deck.FileBeat = fileBeat;
            deck.Gain = 1;
            deck.Adaptive = adaptive;
            deck.Current = deck.Pending = adaptive ? 1 : 0;
            deck.LastBar = int.MinValue;
            deck.Resyncs = 0; deck.SeekHold = 0; deck.OverSince = -1; deck.ResumeMark = -1; deck.Bias = 0; deck.CalAt = -1;
            deck.ErrBeat.Clear(); deck.ErrDsp.Clear();
            _cur = deck;
            TryStart(deck);
        }

        public void Stop(float fade)
        {
            foreach (var d in _decks)
            {
                if (d == null || !d.Active || d.Fading) continue;
                LogStats(d);
                if (fade <= 0.001f) HardStop(d);
                else { d.Fading = true; d.FadeRate = d.Gain / fade; }
            }
        }

        void HardStop(Deck d)
        {
            foreach (var s in d.Src) { s.Stop(); s.clip = null; }
            d.Active = false; d.Started = false; d.Fading = false;
        }

        /// A judged note from the human player: drives the adaptive level.
        public void OnJudge(bool hit)
        {
            _intensity.OnJudge(hit);
            if (_cur != null && _cur.Active && _cur.Adaptive) _cur.Pending = _intensity.Pending;
        }

        float _reportAt = -1;

        double _pauseGameNow, _resumeGameNow;
        float _resumeUnscaled;

        /// The round pauses: the game clock is held from here (GameHost.Now).
        public void OnPause() => _pauseGameNow = _host.Now;

        /// The round resumes after a pause (the count-in has ended). The decks carry on where AudioListener.pause
        /// froze them; the position getter is then checked against the game's grid like any other frame, with its
        /// error half a second in kept as a verification statistic (LogResume). Where the platform's getter keeps
        /// counting through a pause (a constant offset of the pause's length, audio unaffected) that offset is
        /// taken out as a bias so it is not mistaken for drift.
        public void OnResume()
        {
            _resumeGameNow = _host.Now;
            _resumeUnscaled = Time.unscaledTime;
            _reportAt = Time.unscaledTime + 1.5f;
            foreach (var d in _decks)
                if (d != null && d.Active && d.Started && !d.Fading)
                {
                    d.Bias = 0; d.CalAt = Time.unscaledTime + 0.5f; d.OverSince = -1;
                    d.ResumeMark = d.ErrBeat.Count; d.ResumeMarkDsp = d.ErrDsp.Count;
                    d.ResumeErr = double.NaN;
                }
            Debug.Log($"[MUSIC] resume gameNow={_resumeGameNow:F4} pausedGameNow={_pauseGameNow:F4}");
        }

        /// Duck the music (1 = full). Smoothed like the web's setTargetAtTime(0.02).
        public void SetDuck(float v) => _duckTarget = Mathf.Clamp01(v);

        void TryStart(Deck d)
        {
            if (d.Started || !_host.ClockRunning) return;
            if (!Ready(d.Info.Id)) return;
            double dsp = _host.DspAt(d.BeatZeroGame) - d.Info.BeatOffset;
            double now = AudioSettings.dspTime;
            double late = now + 0.03 - dsp;   // how far into the file we are by the time we can start
            if (late > 0) { dsp = now + 0.03; d.SeekHold = Time.unscaledTime + 0.4f; }   // no position checks while it spins up
            Schedule(d, dsp, late > 0 ? late : 0);
            d.Started = true;
            d.StartDsp = dsp;
            d.PlayedAt = now;
            var c0 = d.Clips[0];
            Debug.Log($"[MUSIC] play id={d.Info.Id} startDsp={dsp:F4} in={dsp - now:F3}s late={Math.Max(0, late):F3}s samples={c0.samples}/{d.Info.Frames} rate={c0.frequency}");
        }

        /// Start every mix of a deck together at `dsp`, `into` seconds into the file (a loop wraps).
        void Schedule(Deck d, double dsp, double into)
        {
            for (int i = 0; i < Slots; i++)
            {
                var s = d.Src[i];
                bool used = i < d.Clips.Length && d.Clips[i] != null;
                if (!used) continue;
                s.Stop();
                s.clip = d.Clips[i];
                s.loop = d.Info.Loop;
                s.pitch = 1;
                s.volume = 0;
                if (into > 0)
                {
                    int p = (int)(into * s.clip.frequency);
                    if (d.Info.Loop) p %= s.clip.samples;
                    s.timeSamples = Mathf.Clamp(p, 0, s.clip.samples - 1);
                }
                s.PlayScheduled(dsp);
            }
        }

        // ------------------------------------------------------------ per frame

        public void Tick(bool paused)
        {
            if (_lib == null || paused) return;
            float dt = Time.unscaledDeltaTime;
            _musicVol = Settings.Music;
            if (_reportAt > 0 && Time.unscaledTime > _reportAt)
            {
                _reportAt = -1;
                LogResume(_cur);
            }
            _duck = Mathf.Lerp(_duck, _duckTarget, 1 - Mathf.Exp(-50f * dt));
            foreach (var d in _decks)
            {
                if (!d.Active) continue;
                if (!d.Started) { TryStart(d); if (!d.Started) continue; }
                Drive(d, dt);
            }
        }

        void Drive(Deck d, float dt)
        {
            var info = d.Info;
            double spb = info.SecondsPerBeat;
            double beat = d.FileBeat != null ? d.FileBeat() : (_host.Now - d.BeatZeroGame) / spb;

            // Bar-synchronous level change: apply when the next frame would cross the line.
            if (d.Adaptive && d.FileBeat != null)
            {
                int bar = Mathf.FloorToInt((float)((beat + dt / spb * 0.5) / MusicTrackInfo.BeatsPerBar));
                if (d.LastBar == int.MinValue) d.LastBar = bar;
                else if (bar != d.LastBar)
                {
                    d.LastBar = bar;
                    if (d.Pending != d.Current)
                    {
                        d.Current = d.Pending;
                        Debug.Log($"[MUSIC] level={d.Current} bar={bar}");
                    }
                }
            }

            // Position against the grid.
            var s0 = d.Src[0];
            bool playing = s0.isPlaying;
            if (beat > 0.05 && playing && s0.clip != null)
            {
                double rate = s0.clip.frequency;
                // The file position the game's clock asks for (a tempo ramp is rendered into the file, so this holds for it too).
                double expect = _host.NowRaw - d.BeatZeroGame + info.BeatOffset;   // the file position is read on the dsp axis, not the heard one
                double actual = s0.timeSamples / rate;
                double len = s0.clip.samples / rate;
                if (info.Loop) { expect = expect % len; double e = actual - expect; e -= Math.Round(e / len) * len; ComputeErr(d, e, s0, info, len); }
                else ComputeErr(d, actual - expect, s0, info, len);
            }

            // Fading and mix.
            if (d.Fading)
            {
                d.Gain -= d.FadeRate * dt;
                if (d.Gain <= 0.001f) { HardStop(d); return; }
            }
            else if (!info.Loop && d.Started && AudioSettings.dspTime > d.StartDsp + 0.5 && !playing)
            {
                LogStats(d);
                HardStop(d);   // a one-shot ran out
                return;
            }

            float swap = dt / SwapSeconds;
            float duck = d == _cur ? _duck : 1;
            for (int i = 0; i < Slots; i++)
            {
                bool on = info.Levels == 1 ? i == 0 : i == d.Current;
                d.Level[i] = Mathf.MoveTowards(d.Level[i], on ? 1 : 0, swap);
                var s = d.Src[i];
                if (s.clip == null) continue;
                s.volume = BaseGain * _musicVol * d.Gain * duck * d.Level[i];
            }
        }

        void ComputeErr(Deck d, double err, AudioSource s0, MusicTrackInfo info, double len)
        {
            if (Time.unscaledTime < d.SeekHold) return;
            if (d.CalAt > 0)
            {
                if (Time.unscaledTime < d.CalAt) return;
                d.ResumeErr = err; d.CalAt = -1;
                if (Math.Abs(err) > 0.05) d.Bias = err;   // not drift: the getter counted the pause (audio unaffected)
            }
            double raw = err;
            err -= d.Bias;
            if (info.Loop) err -= Math.Round(err / len) * len;
            LastError = err;
            if (d.ErrBeat.Count < 20000) d.ErrBeat.Add((float)err);
            // dsp view: where the file's start would be, given this frame's position.
            if (d.ErrDsp.Count < 20000)
            {
                double startEst = AudioSettings.dspTime - s0.timeSamples / (double)s0.clip.frequency;
                double beatZeroDsp = _host.DspAt(d.BeatZeroGame) - info.BeatOffset;
                double k = info.Loop ? Math.Round((startEst - beatZeroDsp) / len) : 0;
                d.ErrDsp.Add((float)(startEst - k * len - beatZeroDsp - d.Bias));
            }
            // A seek only for an error that holds (a frame hitch or one odd reading is not drift).
            double tol = info.Ramped ? RampedResync : HardResync;
            if (Math.Abs(err) <= tol) d.OverSince = -1;
            else if (d.OverSince < 0) d.OverSince = Time.unscaledTime;
            if (d.OverSince > 0 && Time.unscaledTime - d.OverSince > SeekAfter)
            {
                d.OverSince = -1;
                d.Resyncs++;
                d.SeekHold = Time.unscaledTime + 1f;
                double expectedNow = s0.timeSamples / (double)s0.clip.frequency - raw;
                d.Bias = 0;   // the getter follows a seek
                foreach (var s in d.Src)
                    if (s.clip != null) s.timeSamples = Mathf.Clamp((int)(expectedNow * s.clip.frequency), 0, s.clip.samples - 1);
                Debug.Log($"[MUSIC] resync id={info.Id} err={err * 1000:F0}ms");
            }
        }

        /// What the position readings did after the last restart: against the game's grid and against the dsp clock.
        void LogResume(Deck d)
        {
            // The game clock must have carried on from where it was held: its advance since the resume is the real time passed.
            double clockDrift = (_host.Now - _resumeGameNow) - (Time.unscaledTime - _resumeUnscaled);
            if (d == null || !d.Active || d.ResumeMark < 0) { Debug.Log($"[MUSIC] after resume: no deck clockDrift={clockDrift * 1000:F1}ms"); return; }
            var g = d.ErrBeat.GetRange(Math.Min(d.ResumeMark, d.ErrBeat.Count), Math.Max(0, d.ErrBeat.Count - d.ResumeMark));
            var p = d.ErrDsp.GetRange(Math.Min(d.ResumeMarkDsp, d.ErrDsp.Count), Math.Max(0, d.ErrDsp.Count - d.ResumeMarkDsp));
            Debug.Log($"[MUSIC] after resume id={d.Info.Id} n={g.Count} resumeErr={d.ResumeErr * 1000:F1}ms bias={d.Bias * 1000:F0}ms vsGrid(ms)={(g.Count > 0 ? Summarize(g) : "n/a")} vsDsp(ms)={(p.Count > 0 ? Summarize(p) : "n/a")} resyncs={d.Resyncs} clockDrift={clockDrift * 1000:F1}ms");
        }

        static void LogStats(Deck d)
        {
            if (d.ErrBeat.Count == 0) return;
            string a = Summarize(d.ErrBeat);
            string b = d.ErrDsp.Count > 0 ? Summarize(d.ErrDsp) : "n/a";
            Debug.Log($"[MUSIC] stats id={d.Info.Id} frames={d.ErrBeat.Count} vsGrid(ms)={a} vsDsp(ms)={b} resyncs={d.Resyncs}");
        }

        static string Summarize(List<float> v)
        {
            var abs = new List<float>(v.Count);
            foreach (var x in v) abs.Add(Mathf.Abs(x));
            abs.Sort();
            var sorted = new List<float>(v); sorted.Sort();
            float med = sorted[sorted.Count / 2];
            return $"median={med * 1000:F2} absP95={abs[(int)(abs.Count * 0.95f)] * 1000:F2} absMax={abs[abs.Count - 1] * 1000:F2}";
        }
    }
}
