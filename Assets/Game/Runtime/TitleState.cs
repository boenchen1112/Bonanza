using System.Collections.Generic;
using Bonanza.Core;

namespace Bonanza
{
    /// What the title screen shows right now, in one place for whatever draws it: the IMGUI placeholder today,
    /// the real stage, logo and reel cards in the visual phase. The host feeds it (reel time from the frame,
    /// beat from the title music's grid); drawing only reads.
    public sealed class TitleState
    {
        readonly IReadOnlyList<string> _games;

        public TitleState(IReadOnlyList<string> gameIds) { _games = gameIds; Reel = new TitleReel(gameIds.Count); }

        /// The attract reel's idle timer and step machine (Core).
        public TitleReel Reel { get; private set; }

        /// True while the reel is showing.
        public bool Attract => Reel.Attract;

        /// The game on show in the reel (a GameHost.Games id), or null outside it.
        public string CurrentGameId => Reel.Attract ? _games[Reel.GameIndex] : null;

        /// 0..1 through the current game's 3.2 s hold; 0 outside the reel.
        public float StepProgress => (float)Reel.Progress;

        /// Beats of the title music since its first beat (0 when the title music is not the shell track). Fractional.
        public double Beat { get; private set; }

        /// True while Beat follows the title music.
        public bool OnGrid { get; private set; }

        /// 0..1 within the current beat: 0 on the beat.
        public float BeatPhase => (float)TitleBeat.Phase(Beat);

        /// 1 on the beat, decaying: the spike every beat-locked element rides (web beatPulse, sharpness 6).
        public float BeatPulse => (float)TitleBeat.Pulse(Beat);

        /// 1 on each bar's downbeat, decaying slowly (the wordmark's breath).
        public float BarPulse => (float)TitleBeat.Pulse(Beat / 4, 2.4);

        /// Beats since the title was shown, as whole beats (for a consumer that counts).
        public long BeatIndex => (long)System.Math.Floor(Beat);

        /// The screen was entered afresh: idle timer and reel start over (web: the title view is rebuilt).
        public void Reset() { Reel = new TitleReel(_games.Count); Beat = 0; OnGrid = false; }

        public void SetBeat(double beat, bool onGrid) { Beat = beat; OnGrid = onGrid; }
    }
}
