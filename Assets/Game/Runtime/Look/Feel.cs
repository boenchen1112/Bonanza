// Shared feel constants (web core/feel.js): hitstop, shake, flash and the combo
// escalation tables that make a PERFECT punch the same in every game. Pure
// numbers, no UnityEngine state, so any game and the FX layer read one source.
//
// Hitstop here is a VISUAL freeze only (see Look/VisualClock): it never touches
// the audio clock, judging or the game's own `now`.

using UnityEngine;
using Bonanza.Core;

namespace Bonanza.Look
{
    public static class Feel
    {
        public const float HitstopMax = 0.098f;
        public const float MilestoneShake = 0.24f, MilestoneFlash = 0.20f;

        /// Verdict colours (sRGB hex), used when a palette does not re-tint them.
        public const uint Perfect = 0xffd93d, Great = 0x4dd6ff, Good = 0x9ee87a, Miss = 0xff5d73;
        public const uint Hot = 0xfff6d8, Combo = 0xffffff;
        public static readonly uint[] Party = { 0xffd93d, 0x4dd6ff, 0xff5d73, 0x9ee87a, 0xc08bff, 0xffffff };

        public static readonly float[] CountScale = { 1.00f, 1.18f, 1.38f, 1.62f, 1.90f, 2.15f, 2.40f, 2.60f };
        public static readonly float[] SizeScale = { 1.00f, 1.06f, 1.12f, 1.20f, 1.28f, 1.36f, 1.44f, 1.50f };
        public static readonly float[] ShakeScale = { 1.00f, 1.06f, 1.14f, 1.24f, 1.34f, 1.45f, 1.54f, 1.60f };
        public static readonly float[] HitstopScale = { 1.00f, 1.04f, 1.09f, 1.15f, 1.21f, 1.27f, 1.32f, 1.35f };
        public static readonly float[] HotScale = { 0.00f, 0.06f, 0.13f, 0.21f, 0.30f, 0.40f, 0.50f, 0.60f };
        public static readonly int[] ExtraRings = { 0, 0, 1, 1, 2, 2, 3, 3 };
        static readonly int[] Milestones = { 5, 10, 20, 35, 50, 75, 100 };

        public static float BaseHitstop(Verdict v) => v switch { Verdict.Perfect => 0.078f, Verdict.Great => 0.048f, Verdict.Good => 0.024f, _ => 0.018f };
        public static float BaseShake(Verdict v) => v switch { Verdict.Perfect => 0.185f, Verdict.Great => 0.105f, Verdict.Good => 0.042f, _ => 0.075f };
        public static float BaseFlash(Verdict v) => v switch { Verdict.Perfect => 0.16f, Verdict.Great => 0.075f, Verdict.Good => 0f, _ => 0.05f };
        public const float FinaleHitstop = 0.17f, FinaleShake = 0.55f, FinaleFlash = 0.55f;

        public static uint BaseColor(Verdict v) => v switch { Verdict.Perfect => Perfect, Verdict.Great => Great, Verdict.Good => Good, _ => Miss };
        public static string Label(Verdict v) => v switch { Verdict.Perfect => "PERFECT!", Verdict.Great => "GREAT!", Verdict.Good => "OKAY", _ => "WHIFF" };

        /// Particle counts per verdict at tier 0, full quality: shards, sparks, streaks, confetti.
        public static void Burst(Verdict v, out int shards, out int sparks, out int streaks, out int confetti)
        {
            switch (v)
            {
                case Verdict.Perfect: shards = 22; sparks = 34; streaks = 14; confetti = 30; break;
                case Verdict.Great: shards = 13; sparks = 20; streaks = 9; confetti = 0; break;
                case Verdict.Good: shards = 0; sparks = 11; streaks = 0; confetti = 0; break;
                default: shards = 0; sparks = 9; streaks = 0; confetti = 0; break;
            }
        }

        /// World radius of each verdict's outer shockwave.
        public static float RingReach(Verdict v) => v switch { Verdict.Perfect => 2.8f, Verdict.Great => 1.9f, Verdict.Good => 1.15f, _ => 1.0f };

        public static int ComboTier(int combo)
        {
            int t = 0;
            for (int i = 0; i < Milestones.Length; i++) if (combo >= Milestones[i]) t = i + 1;
            return t;
        }

        public static bool IsMilestone(int combo) => System.Array.IndexOf(Milestones, combo) >= 0;

        public struct Result
        {
            public int Tier; public float Hitstop, Shake, Flash, CountScale, SizeScale, Hot; public int ExtraRings;
        }

        /// Verdict feel with the combo escalation folded in (feelForCombo).
        public static Result ForCombo(Verdict v, int combo)
        {
            int t = ComboTier(combo);
            int i = Mathf.Min(t, CountScale.Length - 1);
            return new Result
            {
                Tier = t,
                Hitstop = Mathf.Min(HitstopMax, BaseHitstop(v) * HitstopScale[i]),
                Shake = BaseShake(v) * ShakeScale[i],
                Flash = BaseFlash(v),
                CountScale = CountScale[i], SizeScale = SizeScale[i], Hot = HotScale[i], ExtraRings = ExtraRings[i],
            };
        }

        /// A hitstop shortened so the freeze ends well before the next unjudged note (web hitstopFor).
        public static float HitstopFor(float requested, System.Collections.Generic.IReadOnlyList<Note> notes, double judgedTime)
        {
            double gap = double.PositiveInfinity;
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                if (n.Judged || n.Time <= judgedTime + 1e-3) continue;
                gap = n.Time - judgedTime;
                break;
            }
            return Mathf.Max(0, Mathf.Min(requested, (float)(gap * 0.4)));
        }

        /// Blend a colour toward white by t (the combo heat curve).
        public static Color HeatUp(Color c, float t) => t <= 0 ? c : new Color(c.r + (1 - c.r) * t, c.g + (1 - c.g) * t, c.b + (1 - c.b) * t, 1);

        public static Color Hex(uint rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
    }
}
