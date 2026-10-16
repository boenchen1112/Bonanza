// The Graphics option as (render scale, tier) levels, and the frame-time governor
// that walks them for Auto. Pure C# (no UnityEngine) so tests can drive it; the
// governor is a port of web render/governor.js and the ladder of render/quality.js.
//
// A tier decides what a frame costs beyond the render scale: MSAA samples, the
// composite vignette, and the crowd / effect budgets (1 = everything) that the
// crowd and FX code reads from RenderQuality when it lands.

using System;
using System.Collections.Generic;

namespace Bonanza
{
    public enum QualityTier { High, Medium, Low }

    public readonly struct QualityLevel
    {
        public readonly float Scale;
        public readonly QualityTier Tier;
        public QualityLevel(float scale, QualityTier tier) { Scale = scale; Tier = tier; }
        public override string ToString() => $"{Scale:0.##}/{Tier}";
        public int Msaa => Tier == QualityTier.High ? 4 : Tier == QualityTier.Medium ? 2 : 1;
        /// 1 = full composite vignette; 0 = skipped on Low.
        public float Vignette => Tier == QualityTier.Low ? 0 : 1;
        /// Share of the crowd to draw and animate (read by the crowd, ticket 8).
        public float CrowdBudget => Tier == QualityTier.High ? 1 : Tier == QualityTier.Medium ? 0.6f : 0.3f;
        /// Share of the particle / effect budget (read by the FX, ticket 9).
        public float FxBudget => Tier == QualityTier.High ? 1 : Tier == QualityTier.Medium ? 0.6f : 0.3f;
    }

    public static class GraphicsPolicy
    {
        /// Best to cheapest. Render scale drops before the tier does, as on the web.
        public static readonly QualityLevel[] Ladder =
        {
            new QualityLevel(1f, QualityTier.High),
            new QualityLevel(0.85f, QualityTier.High),
            new QualityLevel(0.85f, QualityTier.Medium),
            new QualityLevel(0.7f, QualityTier.Medium),
            new QualityLevel(0.7f, QualityTier.Low),
        };

        /// Where a fixed setting sits. Auto returns the start of the ladder (see AutoStart).
        public static QualityLevel Fixed(GraphicsSetting g) => g switch
        {
            GraphicsSetting.High => Ladder[0],
            GraphicsSetting.Medium => Ladder[2],
            GraphicsSetting.Low => Ladder[4],
            _ => Ladder[0],
        };

        /// Integrated GPUs (and software rasterisers) start Auto lower, like the web's Iris Xe rule.
        public static bool IsIntegrated(string renderer) =>
            !string.IsNullOrEmpty(renderer) && System.Text.RegularExpressions.Regex.IsMatch(renderer,
                @"\b(Intel|Iris|UHD Graphics|HD Graphics|SwiftShader|llvmpipe)\b|Radeon\(TM\) Graphics|Radeon Graphics",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// Auto's starting ladder index: the remembered one for this device, else by GPU class.
        public static int AutoStart(string renderer, int remembered)
        {
            if (remembered >= 0 && remembered < Ladder.Length) return remembered;
            return IsIntegrated(renderer) ? 2 : 0;
        }
    }

    public enum FramePhase { Loading, Playing, Between, Paused }

    public readonly struct GovernorMove
    {
        public readonly int Index;
        public readonly string Reason;
        public readonly bool AtFloorOverBudget;
        public GovernorMove(int index, string reason, bool floor = false) { Index = index; Reason = reason; AtFloorOverBudget = floor; }
    }

    /// Keeps Graphics Auto at a level the machine holds at 60 fps. Deterministic: a frame-time
    /// history fully determines what it does. Moves happen between rounds, except severe overload.
    public sealed class GraphicsGovernor
    {
        const double OVER = 1.05, SEVERE = 1.6, HEADROOM = 0.7;
        const double WINDOW_MS = 2000, MENU_WINDOW_MS = 3000;
        const double MIN_ROUND_MS = 20000, MIN_OVERLOAD_MS = 8000, ROUND_WARMUP_MS = 2000;
        const int HEADROOM_ROUNDS = 2;
        const double IGNORE_FRAME_MS = 500;

        sealed class Window
        {
            readonly List<double> _buf = new List<double>();
            double _total;
            readonly double _span;
            public Window(double span) { _span = span; }
            public void Push(double ms)
            {
                _buf.Add(ms); _total += ms;
                while (_buf.Count > 1 && _total - _buf[0] >= _span) { _total -= _buf[0]; _buf.RemoveAt(0); }
            }
            public bool Full => _total >= _span;
            public double P95 => GraphicsGovernor.P95(_buf);
            public void Clear() { _buf.Clear(); _total = 0; }
        }

        readonly int _last;
        readonly double _budget;
        readonly bool _enabled;
        readonly Func<int, bool> _recompiles;   // does stepping from this index to the next recompile shaders?
        int _index, _ceiling;
        FramePhase _phase = FramePhase.Loading;
        int _headroomRounds;
        bool _steppedThisRound;
        readonly Window _recent = new Window(WINDOW_MS), _menu = new Window(MENU_WINDOW_MS);
        List<double> _round = new List<double>();
        double _roundMs, _warmupMs;

        public int Index => _index;

        public GraphicsGovernor(int ladderLength, int startIndex = 0, bool enabled = true, double budgetMs = 1000.0 / 60, Func<int, bool> recompiles = null)
        {
            _last = ladderLength - 1;
            _index = Math.Max(0, Math.Min(_last, startIndex));
            _enabled = enabled;
            _budget = budgetMs;
            _recompiles = recompiles ?? (_ => false);
        }

        internal static double P95(List<double> v)
        {
            if (v.Count == 0) return 0;
            var s = new List<double>(v); s.Sort();
            return s[Math.Min(s.Count - 1, (int)Math.Floor(s.Count * 0.95))];
        }

        GovernorMove? Decide(int to, string reason, bool floor = false) { _index = to; return new GovernorMove(_index, reason, floor); }

        GovernorMove? StepDown(string reason)
        {
            if (_index >= _last) return null;
            _ceiling = Math.Max(_ceiling, _index + 1);
            _headroomRounds = 0;
            return Decide(_index + 1, reason);
        }

        GovernorMove? EndRound()
        {
            double q = P95(_round);
            bool lng = _roundMs >= MIN_ROUND_MS, enough = _roundMs >= MIN_OVERLOAD_MS, wasStepped = _steppedThisRound;
            _round = new List<double>(); _roundMs = 0; _warmupMs = 0; _steppedThisRound = false;
            if (q == 0 || !enough) { _headroomRounds = 0; return null; }
            if (q > _budget * OVER)
            {
                if (_index >= _last) return Decide(_index, $"floor overload (round p95 {q:F1}ms)", true);
                return StepDown($"overload (round p95 {q:F1}ms)");
            }
            if (!lng || wasStepped || q >= _budget * HEADROOM) { _headroomRounds = 0; return null; }
            if (++_headroomRounds < HEADROOM_ROUNDS || _index <= _ceiling) return null;
            _headroomRounds = 0;
            return Decide(_index - 1, $"headroom (round p95 {q:F1}ms)");
        }

        /// One finished frame and what the game is doing next. Returns a move or null.
        public GovernorMove? Sample(double frameMs, FramePhase next)
        {
            if (!_enabled || frameMs > IGNORE_FRAME_MS) return null;
            var prev = _phase;
            _phase = next;
            GovernorMove? @out = null;
            if (prev == FramePhase.Playing && _phase != FramePhase.Playing && _phase != FramePhase.Paused) @out = EndRound();
            if (_phase != FramePhase.Between) _menu.Clear();
            if (_phase != FramePhase.Playing) _recent.Clear();

            if (_phase == FramePhase.Playing && _warmupMs < ROUND_WARMUP_MS) _warmupMs += frameMs;
            else if (_phase == FramePhase.Playing)
            {
                _round.Add(frameMs); _roundMs += frameMs; _recent.Push(frameMs);
                // A step that recompiles shaders must never land mid-round.
                bool recompiles = _index < _last && _recompiles(_index);
                if (!_steppedThisRound && !recompiles && _recent.Full && _recent.P95 > _budget * SEVERE)
                {
                    var d = StepDown($"severe overload (p95 {_recent.P95:F1}ms)");
                    if (d.HasValue)
                    {
                        _steppedThisRound = true; _recent.Clear(); _round = new List<double>(); _roundMs = 0;
                        @out = d;
                    }
                }
            }
            else if (_phase == FramePhase.Between && !@out.HasValue)
            {
                _menu.Push(frameMs);
                if (_menu.Full && _menu.P95 > _budget * OVER) { _menu.Clear(); @out = StepDown("menu overload"); }
            }
            return @out;
        }
    }
}
