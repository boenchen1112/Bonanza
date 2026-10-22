// The Hymn Meter data the player loads: the hymns' MIDI, their tempo maps, the excerpt table and the
// rendered audio, all from Resources/Hymns/. That folder is NOT in the repository: Tools/hymns/prepare-data.sh
// fills it from the private hymns at build time (it is gitignored), so a clone without the hymns has none of
// it. Load() then throws a plain "hymn data missing" error, which the host's fault path turns into one
// [ERROR] line and the title screen; no other game is affected.
//
//   Hymns/excerpts        the excerpt table (HymnExcerptTable)
//   Hymns/audio           { beatOffset, clips[] } written by Tools/hymns/render-hymns.mjs
//   Hymns/<hymn>.mid      the MIDI (a .bytes asset)
//   Hymns/<hymn>.tempo    the tempo map (optional: none = the file's own timing)
//   Hymns/<clip>          the rendered mono clip: <hymn>, or <hymn>.<level> for a re-timed level, or
//                         <hymn>.<level>.tempo for Finale Fever (TempoRound.ClipName: the excerpt on its tempo curve)
//
// The grid is built here by Core from the MIDI + tempo map, the audio was rendered from the same pair, so they
// agree (the renderer checks it against Core's own beat map).

using System;
using System.Collections.Generic;
using System.Linq;
using Bonanza.Core;
using UnityEngine;

namespace Bonanza
{
    public sealed class HymnData
    {
        public const string Dir = "Hymns/";

        // beats / curveSeconds / lead: only on a tempo clip (the curve it was rendered from, and the silence before its beat 0)
        [Serializable] sealed class ClipRow { public string name; public float seconds; public int bytes; public int beats; public double curveSeconds; public double lead; }
        [Serializable] sealed class AudioManifest { public double beatOffset; public ClipRow[] clips; }

        /// The hymns that can be played (a row in use, its MIDI read, its clips rendered).
        public readonly List<HymnCandidate> Pool = new List<HymnCandidate>();
        /// The hymns Finale Fever can play: in Pool, cut into an excerpt, and all three of their tempo clips rendered
        /// from the very curve the game generates (beat count and length agree with audio.json).
        public readonly List<HymnCandidate> TempoPool = new List<HymnCandidate>();
        /// Why a hymn is not in TempoPool (only Finale Fever reports these).
        public readonly List<string> TempoProblems = new List<string>();
        readonly Dictionary<string, ClipRow> _rows = new Dictionary<string, ClipRow>();
        /// Seconds from a clip's start to tick 0 (the audio graph's latency, measured by the renderer).
        public double BeatOffset { get; private set; }
        /// Problems with single hymns (a hymn with a problem is left out of the pool).
        public readonly List<string> Problems = new List<string>();

        public static bool Exists => Resources.Load<TextAsset>(Dir + "excerpts") != null;

        /// Throws InvalidOperationException("hymn data missing: ...") when there is no usable data.
        public static HymnData Load()
        {
            var table = Resources.Load<TextAsset>(Dir + "excerpts");
            var audio = Resources.Load<TextAsset>(Dir + "audio");
            if (table == null || audio == null)
                throw new InvalidOperationException("hymn data missing: Assets/Resources/Hymns is not in this build (the hymns are private; run Tools/hymns/prepare-data.sh)");

            var d = new HymnData();
            var manifest = JsonUtility.FromJson<AudioManifest>(audio.text);
            if (manifest == null || manifest.clips == null || manifest.clips.Length == 0)
                throw new InvalidOperationException("hymn data missing: Hymns/audio lists no clips (run Tools/hymns/prepare-data.sh)");
            d.BeatOffset = manifest.beatOffset;
            var rendered = new HashSet<string>(manifest.clips.Select(c => c.name));
            foreach (var c in manifest.clips) d._rows[c.name] = c;

            var rows = HymnExcerptTable.Parse(table.text);
            foreach (var e in rows.Errors) d.Problems.Add("excerpts: " + e);
            foreach (var row in rows.UsedRows)
            {
                try
                {
                    var mid = Resources.Load<TextAsset>(Dir + row.Hymn + ".mid");
                    if (mid == null) { d.Problems.Add(row.Hymn + ": no MIDI"); continue; }
                    var midi = HymnMidi.Parse(mid.bytes);
                    var tm = Resources.Load<TextAsset>(Dir + row.Hymn + ".tempo");
                    var map = tm != null ? HymnTempoMap.Parse(tm.text) : HymnTempoMap.Identity(row.Hymn);
                    var c = HymnCandidate.From(midi, map, row);
                    var missing = new[] { HymnLevel.Easy, HymnLevel.Medium, HymnLevel.Hard }.Select(c.ClipFor).Distinct().FirstOrDefault(n => !rendered.Contains(n));
                    if (missing != null) { d.Problems.Add(row.Hymn + ": clip " + missing + " is not rendered"); continue; }
                    if (!c.CanCut()) { d.Problems.Add(row.Hymn + ": the excerpt table names bars it does not have"); continue; }
                    d.Pool.Add(c);
                }
                catch (FormatException ex) { d.Problems.Add(row.Hymn + ": " + ex.Message); }
            }
            foreach (var c in d.Pool) d.AddTempo(c);
            if (d.Pool.Count == 0)
                throw new InvalidOperationException("hymn data missing: no hymn can be played (" + string.Join("; ", d.Problems) + ")");
            return d;
        }

        /// Seconds of silence before beat 0 in a tempo clip (add to BeatOffset for the file position of the first beat).
        public double TempoLead(string clip) => _rows.TryGetValue(clip, out var r) ? r.lead : 0;

        void AddTempo(HymnCandidate c)
        {
            TempoExcerpt x;
            string err;
            if (!TempoExcerpt.TryCut(c, out x, out err)) { TempoProblems.Add(err); return; }
            foreach (var level in TempoRound.SlotLevels)
            {
                string clip = TempoRound.ClipName(c.Name, level);
                ClipRow r;
                if (!_rows.TryGetValue(clip, out r)) { TempoProblems.Add(c.Name + ": tempo clip " + clip + " is not rendered"); return; }
                var curve = x.CurveFor(level);
                // The clip was rendered from the dump of this same curve; a different count or length means the hymns,
                // a tempo map or the generator changed since (run Tools/hymns/prepare-data.sh).
                if (r.beats != curve.BeatCount || Math.Abs(r.curveSeconds - curve.Duration) > 0.002)
                { TempoProblems.Add(c.Name + ": tempo clip " + clip + " is stale (" + r.beats + " beats " + r.curveSeconds.ToString("0.000") + " s rendered, " + curve.BeatCount + " beats " + curve.Duration.ToString("0.000") + " s now)"); return; }
            }
            TempoPool.Add(c);
        }

        public static AudioClip Clip(string name) => Resources.Load<AudioClip>(Dir + name);
    }
}
