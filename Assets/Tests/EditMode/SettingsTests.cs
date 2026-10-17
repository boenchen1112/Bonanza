using System.Collections.Generic;
using NUnit.Framework;

namespace Bonanza.Core.Tests
{
    sealed class MemStore : ISettingsStore
    {
        public readonly Dictionary<string, object> D = new Dictionary<string, object>();
        public bool Has(string k) => D.ContainsKey(k);
        public float GetFloat(string k, float def) => D.TryGetValue(k, out var v) && v is float f ? f : def;
        public void SetFloat(string k, float v) => D[k] = v;
        public string GetString(string k, string def) => D.TryGetValue(k, out var v) && v is string s ? s : def;
        public void SetString(string k, string v) => D[k] = v;
        public void Delete(string k) => D.Remove(k);
        public void Flush() { }
    }

    public class SettingsTests
    {
        ISettingsStore _saved;
        System.Func<bool> _savedOs;
        MemStore _m;

        [SetUp] public void SetUp()
        {
            _saved = Settings.Store; _m = new MemStore(); Settings.Store = _m; Settings.CameraInputAvailable = false;
            _savedOs = Settings.OsReduceMotion; Settings.OsReduceMotion = () => false;
        }
        [TearDown] public void TearDown() { Settings.Store = _saved; Settings.OsReduceMotion = _savedOs; }

        [Test] public void ReduceMotion_is_the_option_or_the_OS_flag()
        {
            bool os = false;
            Settings.OsReduceMotion = () => os;
            // option off, OS off
            Assert.IsFalse(Settings.ReduceMotion);
            // option off, OS on: the OS flag alone turns it on, the stored option stays off
            os = true;
            Assert.IsTrue(Settings.ReduceMotion);
            Assert.IsFalse(Settings.ReduceMotionOption);
            // option on, OS on
            Settings.ReduceMotionOption = true;
            Assert.IsTrue(Settings.ReduceMotion);
            // option on, OS off
            os = false;
            Assert.IsTrue(Settings.ReduceMotion);
            // back to option off, OS off
            Settings.ReduceMotionOption = false;
            Assert.IsFalse(Settings.ReduceMotion);
        }

        [Test] public void ReduceMotion_follows_the_OS_flag_live_and_ResetAll_keeps_it()
        {
            bool os = false;
            Settings.OsReduceMotion = () => os;
            Assert.IsFalse(Settings.ReduceMotion);
            os = true;                                   // the media query's change event flips the source
            Assert.IsTrue(Settings.ReduceMotion);
            Settings.ResetAll();                         // clears the saved option, not the OS setting
            Assert.IsFalse(Settings.ReduceMotionOption);
            Assert.IsTrue(Settings.ReduceMotion);
        }

        [Test] public void NotifyChanged_raises_Changed()
        {
            int n = 0; System.Action h = () => n++;
            Settings.Changed += h;
            try { Settings.NotifyChanged(); } finally { Settings.Changed -= h; }
            Assert.AreEqual(1, n);
        }

        [Test] public void Defaults_match_the_web()
        {
            Assert.AreEqual(0.75f, Settings.Music);
            Assert.AreEqual(0.85f, Settings.Sfx);
            Assert.AreEqual(0f, Settings.OffsetMs);
            Assert.IsFalse(Settings.ReduceMotion);
            Assert.AreEqual(GraphicsSetting.Auto, Settings.Graphics);
            Assert.AreEqual(SwingInput.Tap, Settings.SwingInputChoice);
        }

        [Test] public void Percent_steps_by_five_and_clamps_without_drift()
        {
            float v = 0.85f;
            for (int i = 0; i < 10; i++) v = Settings.StepPct(v, 1);
            Assert.AreEqual(1f, v);
            for (int i = 0; i < 30; i++) v = Settings.StepPct(v, -1);
            Assert.AreEqual(0f, v);
            v = Settings.StepPct(Settings.StepPct(0.7f, 1), 1);
            Assert.AreEqual(0.8f, v, 1e-6f);
            Assert.AreEqual(0.55f, Settings.StepPct(0.5f, 1), 1e-6f);
        }

        [Test] public void Offset_steps_by_five_within_300()
        {
            Assert.AreEqual(5f, Settings.StepOffset(0, 1));
            Assert.AreEqual(-5f, Settings.StepOffset(0, -1));
            Assert.AreEqual(300f, Settings.StepOffset(300, 1));
            Assert.AreEqual(-300f, Settings.StepOffset(-300, -1));
            Assert.AreEqual(295f, Settings.StepOffset(300, -1));
            Assert.AreEqual(-300f, Settings.ClampOffset(-999));
            Settings.OffsetMs = 9999;
            Assert.AreEqual(300f, Settings.OffsetMs);
        }

        [Test] public void Choices_cycle_both_ways_with_wrap()
        {
            Assert.AreEqual(GraphicsSetting.High, Settings.Cycle(GraphicsSetting.Auto, 1));
            Assert.AreEqual(GraphicsSetting.Auto, Settings.Cycle(GraphicsSetting.Low, 1));
            Assert.AreEqual(GraphicsSetting.Low, Settings.Cycle(GraphicsSetting.Auto, -1));
            Assert.AreEqual(SwingInput.Tap, Settings.Cycle(SwingInput.Camera, 1));
            Assert.AreEqual(SwingInput.Camera, Settings.Cycle(SwingInput.Tap, -1));
        }

        [Test] public void Values_round_trip_through_the_store()
        {
            Settings.Music = 0.4f; Settings.Sfx = 0.1f; Settings.OffsetMs = -35; Settings.ReduceMotionOption = true;
            Settings.Graphics = GraphicsSetting.Low; Settings.SwingInputChoice = SwingInput.Mouse;
            Assert.AreEqual(0.4f, Settings.Music, 1e-6f);
            Assert.AreEqual(0.1f, Settings.Sfx, 1e-6f);
            Assert.AreEqual(-35f, Settings.OffsetMs);
            Assert.IsTrue(Settings.ReduceMotion);
            Assert.AreEqual(GraphicsSetting.Low, Settings.Graphics);
            Assert.AreEqual(SwingInput.Mouse, Settings.SwingInputChoice);
            Assert.AreEqual("low", _m.D[Settings.KeyGraphics]);
        }

        [Test] public void Garbage_in_the_store_falls_back_to_defaults_or_clamps()
        {
            _m.SetString(Settings.KeyGraphics, "ultra");
            _m.SetString(Settings.KeySwing, "");
            _m.SetFloat(Settings.KeyMusic, 7f);
            _m.SetFloat(Settings.KeySfx, float.NaN);
            Assert.AreEqual(GraphicsSetting.Auto, Settings.Graphics);
            Assert.AreEqual(SwingInput.Tap, Settings.SwingInputChoice);
            Assert.AreEqual(1f, Settings.Music);
            Assert.AreEqual(Settings.DefaultSfx, Settings.Sfx);
        }

        [Test] public void Camera_falls_back_to_mouse_until_available_but_is_kept()
        {
            Settings.SwingInputChoice = SwingInput.Camera;
            Assert.AreEqual(SwingInput.Camera, Settings.SwingInputChoice);
            Assert.AreEqual(SwingInput.Mouse, Settings.SwingInputEffective);
            Settings.CameraInputAvailable = true;
            Assert.AreEqual(SwingInput.Camera, Settings.SwingInputEffective);
        }

        [Test] public void Old_single_volume_migrates_once_and_keeps_the_levels()
        {
            _m.SetFloat(Settings.KeyLegacyVolume, 0.85f);
            Settings.Migrate();
            Assert.AreEqual(0.85f, Settings.Sfx, 1e-6f);
            Assert.AreEqual(0.75f, Settings.Music, 1e-6f);
            Assert.IsFalse(_m.Has(Settings.KeyLegacyVolume));
            Settings.Music = 0.2f;
            Settings.Migrate();   // nothing left to migrate
            Assert.AreEqual(0.2f, Settings.Music, 1e-6f);
        }

        [Test] public void Migration_does_not_overwrite_new_values_and_keeps_offset()
        {
            _m.SetFloat(Settings.KeyOffset, -20);
            _m.SetFloat(Settings.KeyMusic, 0.3f);
            _m.SetFloat(Settings.KeyLegacyVolume, 1f);
            Settings.Migrate();
            Assert.AreEqual(0.3f, Settings.Music, 1e-6f);
            Assert.AreEqual(Settings.DefaultSfx, Settings.Sfx);
            Assert.AreEqual(-20f, Settings.OffsetMs);
        }

        [Test] public void Migrated_volume_is_scaled_and_clamped()
        {
            Settings.MigrateVolume(0f, out float m, out float f);
            Assert.AreEqual(0f, m); Assert.AreEqual(0f, f);
            Settings.MigrateVolume(1f, out m, out f);
            Assert.AreEqual(1f, f); Assert.AreEqual(0.9f, m, 1e-6f);
        }

        [Test] public void ResetAll_restores_defaults()
        {
            Settings.Music = 0.1f; Settings.Graphics = GraphicsSetting.High; Settings.OffsetMs = 40;
            Settings.ResetAll();
            Assert.AreEqual(Settings.DefaultMusic, Settings.Music);
            Assert.AreEqual(GraphicsSetting.Auto, Settings.Graphics);
            Assert.AreEqual(0f, Settings.OffsetMs);
        }

        [Test] public void Changed_fires_on_every_write()
        {
            int n = 0; System.Action h = () => n++;
            Settings.Changed += h;
            Settings.Music = 0.5f; Settings.ReduceMotionOption = true;
            Settings.Changed -= h;
            Assert.AreEqual(2, n);
        }
    }

    public class GraphicsTests
    {
        static GraphicsGovernor Gov(int start = 0) =>
            new GraphicsGovernor(GraphicsPolicy.Ladder.Length, start, true, 1000.0 / 60,
                i => (GraphicsPolicy.Ladder[i + 1].Tier == QualityTier.Low) != (GraphicsPolicy.Ladder[i].Tier == QualityTier.Low));

        static GovernorMove? Feed(GraphicsGovernor g, double ms, double totalMs, FramePhase ph)
        {
            GovernorMove? last = null;
            for (double t = 0; t < totalMs; t += ms) { var m = g.Sample(ms, ph); if (m.HasValue) last = m; }
            return last;
        }

        [Test] public void Fixed_settings_map_to_ladder_levels()
        {
            Assert.AreEqual(QualityTier.High, GraphicsPolicy.Fixed(GraphicsSetting.High).Tier);
            Assert.AreEqual(QualityTier.Medium, GraphicsPolicy.Fixed(GraphicsSetting.Medium).Tier);
            Assert.AreEqual(QualityTier.Low, GraphicsPolicy.Fixed(GraphicsSetting.Low).Tier);
            Assert.Greater(GraphicsPolicy.Fixed(GraphicsSetting.High).Scale, GraphicsPolicy.Fixed(GraphicsSetting.Low).Scale);
        }

        [Test] public void Tiers_get_cheaper()
        {
            var h = GraphicsPolicy.Fixed(GraphicsSetting.High); var m = GraphicsPolicy.Fixed(GraphicsSetting.Medium); var l = GraphicsPolicy.Fixed(GraphicsSetting.Low);
            Assert.Greater(h.Msaa, m.Msaa); Assert.Greater(m.Msaa, l.Msaa);
            Assert.Greater(h.CrowdBudget, l.CrowdBudget); Assert.Greater(h.FxBudget, l.FxBudget);
            Assert.AreEqual(0f, l.Vignette); Assert.AreEqual(1f, h.Vignette);
        }

        [Test] public void Auto_starts_lower_on_integrated_gpus_and_honours_a_remembered_level()
        {
            Assert.IsTrue(GraphicsPolicy.IsIntegrated("ANGLE (Intel, Intel(R) Iris(R) Xe Graphics)"));
            Assert.IsFalse(GraphicsPolicy.IsIntegrated("NVIDIA GeForce RTX 4070"));
            Assert.AreEqual(2, GraphicsPolicy.AutoStart("Intel Iris Xe", -1));
            Assert.AreEqual(0, GraphicsPolicy.AutoStart("NVIDIA GeForce RTX 4070", -1));
            Assert.AreEqual(3, GraphicsPolicy.AutoStart("Intel Iris Xe", 3));
            Assert.AreEqual(0, GraphicsPolicy.AutoStart("NVIDIA", 99));
        }

        [Test] public void A_slow_round_steps_down_once_the_round_ends()
        {
            var g2 = Gov(0);
            // 25 ms frames (40 fps) are over budget but not severe (< 26.7 ms): no move mid-round.
            Assert.IsNull(Feed(g2, 25, 12000, FramePhase.Playing));
            var move = g2.Sample(16, FramePhase.Between);
            Assert.IsTrue(move.HasValue);
            Assert.AreEqual(1, move.Value.Index);
        }

        [Test] public void Severe_overload_steps_down_mid_round()
        {
            var g = Gov(0);
            var m = Feed(g, 40, 6000, FramePhase.Playing);
            Assert.IsTrue(m.HasValue);
            StringAssert.StartsWith("severe", m.Value.Reason);
            Assert.AreEqual(1, g.Index);
        }

        [Test] public void A_step_across_the_low_boundary_is_never_taken_mid_round()
        {
            var g = Gov(3);   // next step is Low
            Assert.IsNull(Feed(g, 40, 12000, FramePhase.Playing));
            Assert.AreEqual(3, g.Index);
            var m = g.Sample(16, FramePhase.Between);   // round ends: overload is handled then
            Assert.IsTrue(m.HasValue);
            Assert.AreEqual(4, m.Value.Index);
        }

        [Test] public void Headroom_climbs_back_only_after_two_fast_rounds_and_not_past_the_ceiling()
        {
            var g = Gov(2);
            for (int r = 0; r < 2; r++)
            {
                Feed(g, 8, 24000, FramePhase.Playing);
                var m = g.Sample(8, FramePhase.Between);
                if (r == 0) Assert.IsFalse(m.HasValue);
                else { Assert.IsTrue(m.HasValue); Assert.AreEqual(1, m.Value.Index); }
            }
        }

        [Test] public void A_level_that_failed_a_round_is_never_climbed_back_into()
        {
            var g = Gov(0);
            Feed(g, 25, 12000, FramePhase.Playing);
            Assert.AreEqual(1, g.Sample(16, FramePhase.Between).Value.Index);
            for (int r = 0; r < 4; r++)
            {
                Feed(g, 8, 24000, FramePhase.Playing);
                g.Sample(8, FramePhase.Between);
            }
            Assert.AreEqual(1, g.Index);
        }

        [Test] public void Menus_only_lower_quality_and_long_frames_are_ignored()
        {
            var g = Gov(0);
            Assert.IsNull(Feed(g, 600, 10000, FramePhase.Between));   // tab-away hitches are no evidence
            var m = Feed(g, 25, 4000, FramePhase.Between);
            Assert.IsTrue(m.HasValue);
            Assert.AreEqual(1, m.Value.Index);
            var fast = Gov(2);
            Assert.IsNull(Feed(fast, 5, 30000, FramePhase.Between));
            Assert.AreEqual(2, fast.Index);
        }

        [Test] public void Disabled_governor_never_moves()
        {
            var g = new GraphicsGovernor(5, 0, false);
            Assert.IsNull(Feed(g, 100, 20000, FramePhase.Playing));
        }
    }
}
