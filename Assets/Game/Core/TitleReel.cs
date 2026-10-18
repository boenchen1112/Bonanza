// The title screen's attract reel and beat, ported from the web prototype's shell/title.js
// (IDLE_TO_ATTRACT, ATTRACT_HOLD, enterAttract, the attract step in update(), the attract branch
// of input(), beatPulse, the per-letter logo ripple) and shell/theme.js (SHELL_BPM, kick gains).
// Pure: time and presses in, events out. The host owns sound and drawing.

using System;

namespace Bonanza.Core
{
    public enum ReelEvent { None, Entered, Stepped }

    /// What a title press did to the reel: nothing (the menu takes it), woke the reel (consumed), or started it.
    public enum ReelPress { Menu, Left, Entered, Ignored }

    public sealed class TitleReel
    {
        /// Seconds without a press before the reel starts.
        public const double IdleToAttract = 8.5;
        /// Seconds each game is shown.
        public const double AttractHold = 3.2;

        readonly int _count;

        public TitleReel(int gameCount) { _count = Math.Max(1, gameCount); }

        public bool Attract { get; private set; }
        /// Seconds since the last press (or since the reel started).
        public double Idle { get; private set; }
        /// Seconds the current reel game has been shown.
        public double StepTime { get; private set; }
        /// Reel steps taken in all. Not reset when the reel is left: it resumes where it was (web attractIdx).
        public int Step { get; private set; }
        /// The catalog index of the game on show (registry order).
        public int GameIndex => Step % _count;
        /// 0..1 through the current game's hold; 0 outside the reel.
        public double Progress => Attract ? Math.Min(1, StepTime / AttractHold) : 0;

        /// One frame of `dt` seconds (web update()). `confirming` is a menu item already chosen.
        public ReelEvent Update(double dt, bool confirming)
        {
            Idle += dt;
            var ev = ReelEvent.None;
            if (!Attract && Idle > IdleToAttract && !confirming) { Enter(); ev = ReelEvent.Entered; }
            if (Attract)
            {
                StepTime += dt;
                if (StepTime > AttractHold)
                {
                    StepTime = 0;
                    Step++;
                    ev = ReelEvent.Stepped;
                }
            }
            return ev;
        }

        /// A button went down on the title (web input()). Every press resets the idle timer. In the reel
        /// a press leaves it and is consumed (never also confirms the item under it); b / pause start it.
        public ReelPress Press(string action, bool confirming)
        {
            Idle = 0;
            if (Attract) { Attract = false; StepTime = 0; return ReelPress.Left; }
            if (confirming) return ReelPress.Ignored;
            if (action == InputMap.B || action == InputMap.Pause) { Enter(); return ReelPress.Entered; }
            return ReelPress.Menu;
        }

        void Enter()
        {
            Attract = true;
            StepTime = 0;
            Idle = 0;
        }
    }

    /// The title's beat math (web theme.js beatPulse and title.js update()).
    public static class TitleBeat
    {
        /// The web shell transport's tempo (theme.js SHELL_BPM). The Unity title is locked to the audible
        /// title music's own grid (MusicPlayer.ShellClock), which the port renders at the track's tempo.
        public const double WebShellBpm = 124;
        /// Gain of the title kick on every beat in the menu and in the reel (web onBeat).
        public const float KickMenu = 0.34f, KickReel = 0.5f;
        /// A tick sounds on every fourth beat.
        public const int TickEvery = 4;
        /// Per-letter lag of the logo ripple, in beats.
        public const double LetterLag = 0.055;

        /// Fractional part, always in [0,1).
        public static double Phase(double beat) => beat - Math.Floor(beat);

        /// A spike on the beat that decays: exp(-phase * sharpness).
        public static double Pulse(double beat, double sharpness = 6) => Math.Exp(-Phase(beat) * sharpness);

        /// The beat as letter `i` of the logo sees it.
        public static double LetterBeat(double beat, int i) => beat - i * LetterLag;

        /// Logo letter bounce: pop (0..1, 1 on the beat), lift in px (negative = up), scale and tilt in degrees.
        public static void Letter(double beat, int i, double amp, out double scale, out double lift, out double tilt)
        {
            double local = LetterBeat(beat, i);
            double p = Math.Exp(-Phase(local) * 6.5);
            scale = 1 + p * 0.11 * amp;
            lift = -p * 9 * amp;
            tilt = Math.Sin(local * Math.PI * 0.5) * 1.6 * amp;
        }

        /// The whole wordmark's breath on each bar's downbeat.
        public static double LogoScale(double beat, double amp) => 1 + Pulse(beat / 4, 2.4) * 0.02 * amp;

        /// The kick's gain for the current screen state.
        public static float KickGain(bool attract) => attract ? KickReel : KickMenu;
    }
}
