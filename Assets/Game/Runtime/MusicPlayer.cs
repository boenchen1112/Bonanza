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
// beat each frame and only a large drift is corrected (a seek). Finale
// Fever's live tempo ramp is followed by scaling the clips' pitch.
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
        const double HardResync = 0.08;       // seconds of drift before a seek
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
            public Func<double> Ratio;           // tempo ratio vs the file (null: 1)
            public float Gain = 1, FadeRate;     // fade multiplier, units per second when fading
            public float[] Level = new float[Slots];
            public int Current, Pending;
            public int LastBar = int.MinValue;
            public double PlayedAt;
            public bool Adaptive;
            // verification statistics (see LogStats)
            public readonly List<float> ErrBeat = new List<float>(), ErrDsp = new List<float>();
            public int Resyncs;
            public double Bias;                  // getter error accepted after a pause (see OnResume)
            public float CalAt = -1;
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
        /// `ratio` the tempo against the file's (Finale Fever only).
        public void PlayGame(string id, double beatZeroGame, Func<double> fileBeat, Func<double> ratio = null)
        {
            Begin(id, beatZeroGame, fileBeat, ratio, true);
            _intensity.Reset();
        }

        /// A shell track (title, results): starts now, free-running.
        public void PlayShell(string id)
        {
            Begin(id, _host.Now + 0.18, null, null, false);
        }

        void Begin(string id, double beatZeroGame, Func<double> fileBeat, Func<double> ratio, bool adaptive)
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
            deck.FileBeat = fileBeat; deck.Ratio = ratio;
            deck.Gain = 1;
            deck.Adaptive = adaptive;
            deck.Current = deck.Pending = adaptive ? 1 : 0;
            deck.LastBar = int.MinValue;
            deck.Resyncs = 0; deck.SeekHold = 0; deck.Bias = 0; deck.CalAt = -1;
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

        /// The round resumes after a pause. AudioListener.pause froze the channels (the output is silent, the
        /// probe in Tools/audio-probe.mjs shows the music carrying on where it stopped) and the game's clock
        /// was held, so the music is where it should be. But WebGL's timeSamples keeps counting through a
        /// pause, so the reading half a second after resuming is taken as the new zero rather than
        /// "correcting" a position that is right. (A seek is not an option anyway: a timeSamples seek before
        /// PlayScheduled landed a fraction of a beat away in the WebGL build.)
        public void OnResume()
        {
            _reportAt = Time.unscaledTime + 1.5f;
            foreach (var d in _decks) if (d != null && d.Active) d.CalAt = Time.unscaledTime + 0.5f;
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
                if (late > 0)
                {
                    // into the track already (a late start, or a restart after a pause)
                    double into = d.Ratio != null && d.FileBeat != null
                        ? (d.FileBeat() + (dsp - now) / d.Info.SecondsPerBeat * d.Ratio()) * d.Info.SecondsPerBeat + d.Info.BeatOffset
                        : late;
                    int p = (int)(into * s.clip.frequency);
                    if (d.Info.Loop) p %= s.clip.samples;
                    s.timeSamples = Mathf.Clamp(p, 0, s.clip.samples - 1);
                }
                s.PlayScheduled(dsp);
            }
            d.Started = true;
            d.StartDsp = dsp;
            d.PlayedAt = now;
            var c0 = d.Clips[0];
            Debug.Log($"[MUSIC] play id={d.Info.Id} startDsp={dsp:F4} in={dsp - now:F3}s late={Math.Max(0, late):F3}s samples={c0.samples}/{d.Info.Frames} rate={c0.frequency}");
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
                Debug.Log($"[MUSIC] after resume: {(_cur != null && _cur.Active ? _cur.Info.Id : "none")} bias={(_cur != null ? _cur.Bias * 1000 : 0):F0}ms err={LastError * 1000:F1}ms resyncs={(_cur != null ? _cur.Resyncs : 0)}");
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
            double ratio = d.Ratio != null ? d.Ratio() : 1;

            // Bar-synchronous level change: apply when the next frame would cross the line.
            if (d.Adaptive && d.FileBeat != null)
            {
                int bar = Mathf.FloorToInt((float)((beat + dt / (spb / ratio) * 0.5) / MusicTrackInfo.BeatsPerBar));
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
            // WebGL reports timeSamples as elapsed time x the current pitch, which is wrong once the pitch
            // has varied, so a deck that follows a tempo ramp is driven open-loop and never seeks.
            if (d.Ratio == null && beat > 0.05 && playing && s0.clip != null)
            {
                double rate = s0.clip.frequency;
                double expect = beat * spb + info.BeatOffset;
                double actual = s0.timeSamples / rate;
                double len = s0.clip.samples / rate;
                if (info.Loop) { expect = expect % len; double e = actual - expect; e -= Math.Round(e / len) * len; ComputeErr(d, e, ratio, s0, info, len); }
                else ComputeErr(d, actual - expect, ratio, s0, info, len);
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
                float pitch = (float)ratio;
                if (Math.Abs(s.pitch - pitch) > 1e-5f) s.pitch = pitch;
            }
        }

        void ComputeErr(Deck d, double err, double ratio, AudioSource s0, MusicTrackInfo info, double len)
        {
            if (Time.unscaledTime < d.SeekHold) return;
            if (d.CalAt > 0)
            {
                if (Time.unscaledTime < d.CalAt) return;
                d.Bias = err; d.CalAt = -1;
            }
            double raw = err;
            err -= d.Bias;
            if (info.Loop) err -= Math.Round(err / len) * len;
            LastError = err;
            if (d.ErrBeat.Count < 20000) d.ErrBeat.Add((float)err);
            // dsp view: where the file's start would be, given this frame's position.
            if (Math.Abs(ratio - 1) < 1e-4 && d.ErrDsp.Count < 20000)
            {
                double startEst = AudioSettings.dspTime - s0.timeSamples / (double)s0.clip.frequency;
                double beatZeroDsp = _host.DspAt(d.BeatZeroGame) - info.BeatOffset;
                double k = info.Loop ? Math.Round((startEst - beatZeroDsp) / len) : 0;
                d.ErrDsp.Add((float)(startEst - k * len - beatZeroDsp));
            }
            // A seek only for an error that holds (a frame hitch or one odd reading is not drift).
            if (Math.Abs(err) <= HardResync) d.OverSince = -1;
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
