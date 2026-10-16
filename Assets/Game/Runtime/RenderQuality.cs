// Applies the Graphics option to the running player and feeds the Auto governor.
//
// Render scale and MSAA are set on the active URP asset at runtime (both are
// plain setters and work in a WebGL player). In the Editor the asset would stay
// modified after Play Mode, so the original values are put back on quit.
// The crowd / effect budgets and vignette scale are plain readable values for
// the look, crowd and FX code.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Bonanza
{
    public static class RenderQuality
    {
        public static QualityLevel Level { get; private set; } = GraphicsPolicy.Ladder[0];
        public static GraphicsSetting Setting { get; private set; } = GraphicsSetting.Auto;
        public static bool Auto => _governor != null;
        public static string Reason { get; private set; } = "start";
        public static int LadderIndex { get; private set; }

        /// Share of the crowd to draw (1 = all). Read by crowd code.
        public static float CrowdBudget => Level.CrowdBudget;
        /// Share of the effects budget (1 = all). Read by FX code.
        public static float FxBudget => Level.FxBudget;
        /// Multiplier on the composite vignette (0 on Low).
        public static float Vignette => Level.Vignette;

        static GraphicsGovernor _governor;
        static UniversalRenderPipelineAsset _rp;
        static float _origScale; static int _origMsaa; static bool _captured;
        static bool _hooked;

        static string DeviceKey => "rg:gfxLevel:" + SystemInfo.graphicsDeviceName;

        /// Apply the saved setting (call at startup and when the option changes).
        public static void Apply(GraphicsSetting setting)
        {
            Setting = setting;
            if (setting == GraphicsSetting.Auto)
            {
                int start = GraphicsPolicy.AutoStart(SystemInfo.graphicsDeviceName, Settings.Store.Has(DeviceKey) ? (int)Settings.Store.GetFloat(DeviceKey, -1) : -1);
                _governor = new GraphicsGovernor(GraphicsPolicy.Ladder.Length, start, true, 1000.0 / 60, IsLowBoundary);
                Reason = "start";
                SetLevel(start);
            }
            else
            {
                _governor = null;
                Reason = "fixed setting";
                Level = GraphicsPolicy.Fixed(setting);
                LadderIndex = System.Array.IndexOf(GraphicsPolicy.Ladder, Level);
                Push();
            }
        }

        static bool IsLowBoundary(int i) =>
            (GraphicsPolicy.Ladder[i + 1].Tier == QualityTier.Low) != (GraphicsPolicy.Ladder[i].Tier == QualityTier.Low);

        static void SetLevel(int i)
        {
            LadderIndex = i;
            Level = GraphicsPolicy.Ladder[i];
            Push();
        }

        static void Push()
        {
            if (!_hooked) { _hooked = true; Application.quitting += Restore; }
            _rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (_rp == null) return;
            if (!_captured) { _captured = true; _origScale = _rp.renderScale; _origMsaa = _rp.msaaSampleCount; }
            if (!Mathf.Approximately(_rp.renderScale, Level.Scale)) _rp.renderScale = Level.Scale;
            if (_rp.msaaSampleCount != Level.Msaa) _rp.msaaSampleCount = Level.Msaa;
        }

        static void Restore()
        {
            if (_rp == null || !_captured) return;
            _rp.renderScale = _origScale;
            _rp.msaaSampleCount = _origMsaa;
        }

        /// One frame. Auto walks the ladder; a fixed setting ignores it.
        public static void Tick(float frameSeconds, FramePhase phase)
        {
            if (_governor == null) return;
            var d = _governor.Sample(frameSeconds * 1000.0, phase);
            if (!d.HasValue) return;
            Reason = d.Value.Reason;
            if (d.Value.AtFloorOverBudget) return;
            SetLevel(d.Value.Index);
            Settings.Store.SetFloat(DeviceKey, d.Value.Index);   // remembered per device, like the web
            Settings.Store.Flush();
            Debug.Log($"[GFX] auto -> {Level} ({d.Value.Reason})");
        }
    }
}
