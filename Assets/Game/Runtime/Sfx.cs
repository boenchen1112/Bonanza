// The web's SFX kit (audio/index.js SFX), played from clips baked offline by Tools/sfx/render-sfx.mjs.
//
// Every web SFX is chord-locked: it reads the chord the music sits on. The renderer baked each one for every
// chord a context can be on, so a clip is picked by (context, sfx, chord degree) and plays unpitched; Core's
// SfxCatalog names the clips and SfxTheory says which notes are in them (both golden-tested against the web).
//
// Prewarm rule: nothing is loaded or synthesised mid-round. Prepare(game) starts decoding the shell's set (title,
// results, the keyless ticks) and the game's set from Load(); the host holds the rules card until Ready. A clip
// whose samples are not resident is skipped, never loaded on the spot (a late load would sound as a late hit).
//
// Playback goes through the host's pooled sources at dsp-scheduled times (GameHost.PlayAt) with Settings.Sfx
// applied there. The chart-timing path never reads anything from here.

using System.Collections.Generic;
using System.Linq;
using Bonanza.Core;
using UnityEngine;

namespace Bonanza
{
    public sealed class Sfx
    {
        /// Playback level of a baked clip at SFX volume 1.0. The music plays at MusicPlayer.BaseGain of its render
        /// (0.425 at the default 75%), so the SFX, baked at the web's unity bus, keep the web's ratio at that level.
        public const float Level = 0.425f;

        GameHost _host;
        SfxLibrary _lib;
        readonly HashSet<string> _want = new HashSet<string>();
        readonly HashSet<string> _warned = new HashSet<string>();
        readonly List<string> _shell = new List<string>();
        AudioSource[] _ui;
        int _uiNext;

        /// Print a `[SFX]` line per sound played (autoplay gates and Tools/sfx-check.mjs read them).
        public bool Log;
        /// Sounds played, by clip (verification).
        public readonly Dictionary<string, int> Played = new Dictionary<string, int>();

        public bool Available => _lib != null;

        public void Init(GameHost host, GameObject owner)
        {
            _host = host;
            _lib = Resources.Load<SfxLibrary>("SfxLibrary");
            if (_lib == null) Debug.LogError("[SFX] Resources/SfxLibrary missing: no sound effects");
            // Pause-menu sounds play while the listener is paused (web: the menu is audible under a stopped transport).
            _ui = new AudioSource[2];
            for (int i = 0; i < _ui.Length; i++)
            {
                _ui[i] = owner.AddComponent<AudioSource>();
                _ui[i].playOnAwake = false;
                _ui[i].ignoreListenerPause = true;
            }
            foreach (var n in new[] { SfxCatalog.Tick, SfxCatalog.Swoosh }) _shell.Add(n);
            foreach (var ctx in new[] { "title", "results" })
                foreach (var c in SfxCatalog.ClipsOf(ctx)) _shell.Add(c.Name);
            Prepare(null);
        }

        /// Start decoding the shell's clips and `game`'s (null: the shell's only). Drops the other games' samples. Call from Load().
        public void Prepare(string game)
        {
            if (_lib == null) return;
            var want = new HashSet<string>(_shell);
            if (game != null && SfxCatalog.Contexts.Contains(game))
                foreach (var c in SfxCatalog.ClipsOf(game)) want.Add(c.Name);
            foreach (var name in _want)
                if (!want.Contains(name)) _lib.Find(name)?.UnloadAudioData();
            foreach (var name in want)
            {
                if (_want.Contains(name)) continue;
                var clip = _lib.Find(name);
                if (clip == null) { Warn("missing:" + name, "no clip " + name); continue; }
                clip.LoadAudioData();
            }
            _want.Clear();
            foreach (var n in want) _want.Add(n);
        }

        /// All requested clips are decoded (or failed, which is logged: a missing sound must not hold a round).
        public bool Ready
        {
            get
            {
                if (_lib == null) return true;
                foreach (var name in _want)
                {
                    var c = _lib.Find(name);
                    if (c == null) continue;
                    if (c.loadState == AudioDataLoadState.Failed) { Warn("failed:" + name, "decode failed: " + name); continue; }
                    if (c.loadState != AudioDataLoadState.Loaded) return false;
                }
                return true;
            }
        }

        void Warn(string key, string msg)
        {
            if (_warned.Add(key)) Debug.LogError("[SFX] " + msg);
        }

        /// Play `sfx` of `ctx` on chord degree `deg` at game time `t` (immediately when past). `arg` is the count
        /// beat or combo level. `gain` scales the web's per-call gain (tick: the gain asked for, relative to its bake).
        public void Play(string ctx, string sfx, int deg, double t, int arg = 0, float gain = 1, float pan = 0, bool whilePaused = false)
        {
            if (_lib == null) return;
            string name = SfxCatalog.ClipName(ctx, sfx, deg, arg);
            if (name == null) { Warn("none:" + ctx + "/" + sfx, $"{ctx} has no '{sfx}' clip"); return; }
            var clip = _lib.Find(name);
            if (clip == null || clip.loadState != AudioDataLoadState.Loaded)
            {
                Warn("cold:" + name, $"{name} is not decoded: skipped");
                return;
            }
            float vol = Level * gain * (name == SfxCatalog.Tick ? 1f / SfxCatalog.TickGain : 1f);
            if (whilePaused)
            {
                var s = _ui[_uiNext++ % _ui.Length];
                s.Stop();
                s.clip = clip;
                s.volume = vol * Settings.Sfx;
                s.panStereo = pan;
                s.Play();
            }
            else _host.PlayAt(clip, t, vol, 1, pan);
            Played.TryGetValue(name, out int n);
            Played[name] = n + 1;
            if (Log) Debug.Log($"[SFX] clip={name} t={t:F4} vol={vol * Settings.Sfx:F3}");
        }
    }
}
