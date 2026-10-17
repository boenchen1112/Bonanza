// The page URL's query string as typed options (web main.js boot: ?scene, ?quality, ?scale, ?perf,
// plus the harness flags this build adds: ?autoplay, ?game, ?party, ?swingInput, ?seed, ?debug,
// ?inputlog, ?latencySlew). Pure: text in, options and a reason list out. A value that does not
// parse is dropped (the option stays unset) and says why; nothing here throws.
//
// SceneIds maps a web scene id onto where the Unity shell goes (web nav.js resolveActivation:
// a game id is a game, an unregistered id is rejected).

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Bonanza.Core
{
    public sealed class UrlOptions
    {
        public bool Autoplay, Perf, Debug, InputLog, LatencyFast;
        /// `?party` or `?party=<n>` was given (the bot plays a party).
        public bool Party;
        /// `?party=<n>`: seed as if n parties had been played before; -1 when not given.
        public int PartySeed = -1;
        /// `?game=<id>`, not yet checked against the registry.
        public string Game;
        /// `?scene=<id>`, not yet checked against the registry.
        public string Scene;
        /// "auto" | "high" | "medium" | "low" (lower case), or null.
        public string Quality;
        /// Render scale override, already clamped to UrlParams.ScaleMin..ScaleMax.
        public float? Scale;
        /// "tap" | "mouse" | "camera" (lower case), or null.
        public string SwingInputName;
        /// `?seed=<n>`: replaces the game seed for the session.
        public uint? Seed;
        /// Why a parameter or value was ignored or adjusted. Empty when everything parsed.
        public readonly List<string> Ignored = new List<string>();
    }

    public static class UrlParams
    {
        public const float ScaleMin = 0.5f, ScaleMax = 2f;

        static readonly string[] Qualities = { "auto", "high", "medium", "low" };
        static readonly string[] SwingInputs = { "tap", "mouse", "camera" };

        /// Parse the query of a full URL ("http://h/?a=1#x"), or a bare query ("?a=1" / "a=1").
        public static UrlOptions Parse(string url)
        {
            var o = new UrlOptions();
            if (string.IsNullOrEmpty(url)) return o;
            int hash = url.IndexOf('#');
            if (hash >= 0) url = url.Substring(0, hash);
            int q = url.IndexOf('?');
            string query = q >= 0 ? url.Substring(q + 1) : (url.Contains("://") ? "" : url);
            if (query.Length == 0) return o;

            var seen = new HashSet<string>();
            foreach (var part in query.Split('&'))
            {
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                string key = Unescape(eq < 0 ? part : part.Substring(0, eq));
                string val = eq < 0 ? null : Unescape(part.Substring(eq + 1));
                if (key.Length == 0) continue;
                if (!seen.Add(key)) { o.Ignored.Add($"{key}: repeated, the first value is used"); continue; }
                switch (key)
                {
                    case "autoplay": o.Autoplay = Flag(val); break;
                    case "perf": o.Perf = Flag(val); break;
                    case "debug": o.Debug = Flag(val); break;
                    case "inputlog": o.InputLog = Flag(val); break;
                    case "latencySlew":
                        if (val == "fast") o.LatencyFast = true;
                        else o.Ignored.Add($"latencySlew: '{val}' is not 'fast'");
                        break;
                    case "game": o.Game = Id(key, val, o); break;
                    case "scene": o.Scene = Id(key, val, o); break;
                    case "party":
                        o.Party = true;
                        if (!string.IsNullOrEmpty(val))
                        {
                            if (int.TryParse(val, NumberStyles.None, CultureInfo.InvariantCulture, out int n)) o.PartySeed = n;
                            else o.Ignored.Add($"party: '{val}' is not a non-negative integer");
                        }
                        break;
                    case "quality":
                        string ql = Pick(val, Qualities);
                        if (ql != null) o.Quality = ql; else o.Ignored.Add($"quality: '{val}' is not auto, high, medium or low");
                        break;
                    case "swingInput":
                        string si = Pick(val, SwingInputs);
                        if (si != null) o.SwingInputName = si; else o.Ignored.Add($"swingInput: '{val}' is not tap, mouse or camera");
                        break;
                    case "scale":
                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float sc) && !float.IsNaN(sc) && !float.IsInfinity(sc))
                        {
                            float c = Math.Max(ScaleMin, Math.Min(ScaleMax, sc));
                            if (c != sc) o.Ignored.Add($"scale: {val} clamped to {c.ToString("0.##", CultureInfo.InvariantCulture)}");
                            o.Scale = c;
                        }
                        else o.Ignored.Add($"scale: '{val}' is not a number");
                        break;
                    case "seed":
                        if (uint.TryParse(val, NumberStyles.None, CultureInfo.InvariantCulture, out uint sd)) o.Seed = sd;
                        else o.Ignored.Add($"seed: '{val}' is not a non-negative integer");
                        break;
                    case "from":
                        o.Ignored.Add("from: not supported (the shell decides where a round came from)");
                        break;
                    default:
                        o.Ignored.Add($"unknown parameter '{key}'");
                        break;
                }
            }
            return o;
        }

        /// Present means on; `=0`, `=false`, `=off` and `=no` mean off.
        static bool Flag(string val)
        {
            if (val == null) return true;
            string v = val.Trim().ToLowerInvariant();
            return !(v == "0" || v == "false" || v == "off" || v == "no");
        }

        static string Id(string key, string val, UrlOptions o)
        {
            string v = val?.Trim();
            if (string.IsNullOrEmpty(v)) { o.Ignored.Add($"{key}: empty"); return null; }
            return v;
        }

        static string Pick(string val, string[] allowed)
        {
            if (val == null) return null;
            string v = val.Trim().ToLowerInvariant();
            return Array.IndexOf(allowed, v) >= 0 ? v : null;
        }

        static string Unescape(string s)
        {
            try { return Uri.UnescapeDataString(s.Replace('+', ' ')); }
            catch (Exception) { return s; }
        }
    }

    public enum SceneKind { Unknown, Title, FreePlay, Roster, Party, Options, Game }

    public readonly struct SceneTarget
    {
        public readonly SceneKind Kind;
        /// Kind == Game: the game id.
        public readonly string Game;
        public SceneTarget(SceneKind kind, string game = null) { Kind = kind; Game = game; }
        public override string ToString() => Game != null ? $"{Kind} {Game}" : Kind.ToString();
    }

    public static class SceneIds
    {
        /// Test-only game ids, honoured only when the caller allows debug (autoplay or ?debug).
        public static readonly string[] DebugGames = { "fault" };

        /// A game id from the registry (or a debug id when `debug`), else null.
        public static string ResolveGame(string id, IReadOnlyCollection<string> games, bool debug)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var g in games) if (g == id) return g;
            if (debug) foreach (var g in DebugGames) if (g == id) return g;
            return null;
        }

        /// Web scene id -> Unity screen. title; freeplay (or menu); roster; party; options; a game id;
        /// `play` with the game named by `playGame` (the web wrapper takes `game` in its opts).
        /// results and chars-demo have no Unity screen, and an unregistered id is Unknown.
        public static SceneTarget Resolve(string id, IReadOnlyCollection<string> games, bool debug, string playGame = null)
        {
            switch (id)
            {
                case "title": return new SceneTarget(SceneKind.Title);
                case "freeplay": case "menu": return new SceneTarget(SceneKind.FreePlay);
                case "roster": return new SceneTarget(SceneKind.Roster);
                case "party": return new SceneTarget(SceneKind.Party);
                case "options": return new SceneTarget(SceneKind.Options);
                case "play":
                    string pg = ResolveGame(playGame, games, debug);
                    return pg != null ? new SceneTarget(SceneKind.Game, pg) : new SceneTarget(SceneKind.Unknown);
            }
            string g = ResolveGame(id, games, debug);
            return g != null ? new SceneTarget(SceneKind.Game, g) : new SceneTarget(SceneKind.Unknown);
        }
    }
}
