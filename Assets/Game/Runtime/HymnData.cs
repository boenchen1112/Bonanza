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
//   Hymns/<clip>          the rendered mono clip: <hymn>, or <hymn>.<level> for a re-timed level
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

        [Serializable] sealed class ClipRow { public string name; public float seconds; public int bytes; }
        [Serializable] sealed class AudioManifest { public double beatOffset; public ClipRow[] clips; }

        /// The hymns that can be played (a row in use, its MIDI read, its clips rendered).
        public readonly List<HymnCandidate> Pool = new List<HymnCandidate>();
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
            if (d.Pool.Count == 0)
                throw new InvalidOperationException("hymn data missing: no hymn can be played (" + string.Join("; ", d.Problems) + ")");
            return d;
        }

        public static AudioClip Clip(string name) => Resources.Load<AudioClip>(Dir + name);
    }
}
