// The score's constants and the adaptive rule, with no engine types so tests
// can pin them. The audio itself is rendered offline by Tools/music/render.mjs
// from the web prototype's synth tracks; Assets/Game/Audio/Music/tracks.json
// is that tool's manifest and MusicTracksTests keeps this table equal to it.
//
// Finale Fever and Swing Kings play rendered hymn clips of their own (Tools/hymns, through
// MusicPlayer.PlayClip) and have no score here.
//
// Beat grid: the render puts the first beat of every track `BeatOffset`
// seconds into the file (graph latency measured by the renderer), so a clip
// is started that much before its first beat's audio time. Bar 0 of a game's
// track sounds on game beat -8 (the first lead-in bar, web `atBeat`).

using System;

namespace Bonanza
{
    public sealed class MusicTrackInfo
    {
        public string Id;
        public double Bpm;
        public int Bars;
        public double BeatOffset;   // seconds from file start to its first beat
        public bool Loop;
        public int Frames;          // sample frames in each mix file
        public int Levels;          // intensity mixes shipped: i0..i3, or the one that ever plays (title i1, results i0)
        /// A full-length, non-looping clip with its tempo built in, played at pitch 1: file position = game time since
        /// its first beat + BeatOffset, held to the clock tighter than a loop (MusicPlayer.PlayClip's hymn clips).
        public bool Ramped;
        /// File name stem of the mix clips (Assets/Game/Audio/Music/{Stem}.iN.ogg).
        public string Stem => Id;

        public const int BeatsPerBar = 4;
        public const int Rate = 44100;
        public double SecondsPerBeat => 60.0 / Bpm;
        public double LoopSeconds => Bars * BeatsPerBar * 60.0 / Bpm;
        public double FileSeconds => Frames / (double)Rate;
    }

    public static class MusicTracks
    {
        /// Game beat at which a game's track starts (the first lead-in bar).
        public const double GameStartBeat = -8;

        public static readonly MusicTrackInfo[] All =
        {
            new MusicTrackInfo { Id = "swing-kings",    Bpm = 124, Bars = 16, BeatOffset = 0.00933, Loop = true,  Frames = 1365677, Levels = 4 },
            new MusicTrackInfo { Id = "drumline-dash",  Bpm = 132, Bars = 16, BeatOffset = 0.00931, Loop = true,  Frames = 1282909, Levels = 4 },
            new MusicTrackInfo { Id = "chomp-chorus",   Bpm = 140, Bars = 16, BeatOffset = 0.00932, Loop = true,  Frames = 1209600, Levels = 4 },
            new MusicTrackInfo { Id = "title",          Bpm = 112, Bars = 8,  BeatOffset = 0.00932, Loop = true,  Frames = 756000,  Levels = 1 },
            new MusicTrackInfo { Id = "results",        Bpm = 128, Bars = 4,  BeatOffset = 0.00949, Loop = false, Frames = 463050,  Levels = 1 },
        };

        public static MusicTrackInfo Find(string id)
        {
            foreach (var t in All) if (t.Id == id) return t;
            return null;
        }
    }

    /// The web's combo -> intensity rule (audio/index.js onJudge), pure. A
    /// miss streak of 2 mutes down to 0, a single miss drops to 1, and hits
    /// climb 1 -> 2 at combo 5 -> 3 at combo 20. The player applies Pending on
    /// the next bar line.
    public sealed class MusicIntensity
    {
        public const int Mid = 5, High = 20;   // FEEL.comboMilestones[0], [2]
        int _streak, _missRun;
        public int Pending { get; private set; } = 1;

        public void Reset() { _streak = 0; _missRun = 0; Pending = 1; }

        public void OnJudge(bool hit)
        {
            if (!hit)
            {
                _streak = 0;
                _missRun++;
                Pending = _missRun >= 2 ? 0 : 1;
            }
            else
            {
                _streak++;
                _missRun = 0;
                Pending = _streak >= High ? 3 : _streak >= Mid ? 2 : 1;
            }
        }
    }
}
