// dotnet run --project Tools/hymns/tempocurves -- <hymns dir> <out dir> [hymn,hymn,...]
// For each hymn in <dir>/excerpts.txt marked `use yes` (or the list given): <out>/<hymn>.<level>.tempo.curve.txt for
// easy, medium and hard, the Finale Fever tempo curve of that hymn x level, written by the same Core code the game
// plays (TempoExcerpt.TryCut + TempoCurve.Generate, hymn from <dir>/<hymn>.mid and <dir>/tempo/<hymn>.json). The file
// name is TempoRound.ClipName + ".curve.txt". Format: see the header of Assets/Game/Core/TempoCurve.cs. Next to it
// <clip>.score.txt: the excerpt's notes laid on that curve (TempoExcerpt.Score), which render-hymns.mjs plays:
//     tempo-score 1
//     clip hymn_007.easy.tempo
//     beats 64
//     note <track> <pitch> <velocity> <start-beat> <end-beat> <start-seconds> <end-seconds>
// Prints one line per file; exits 1 when a hymn cannot be cut.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Bonanza.Core;

static class Program
{
    static string ScoreText(string clip, TempoCurve curve, TempoExcerpt x)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("tempo-score 1\nclip ").Append(clip).Append("\nbeats ").Append(curve.BeatCount).Append('\n');
        foreach (var n in x.Score(curve))
            sb.Append("note ").Append(n.Track).Append(' ').Append(n.Pitch).Append(' ').Append(n.Velocity).Append(' ')
              .Append(n.StartBeat.ToString("R", inv)).Append(' ').Append(n.EndBeat.ToString("R", inv)).Append(' ')
              .Append(n.Start.ToString("R", inv)).Append(' ').Append(n.End.ToString("R", inv)).Append('\n');
        return sb.ToString();
    }

    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: <hymns dir> <out dir> [hymn,...]"); return 2; }
        string dir = args[0], outDir = args[1];
        var only = args.Length > 2 ? args[2].Split(',') : null;
        Directory.CreateDirectory(outDir);
        var table = HymnExcerptTable.Parse(File.ReadAllText(Path.Combine(dir, "excerpts.txt")));
        foreach (var e in table.Errors) Console.Error.WriteLine("excerpts.txt " + e);
        int bad = 0;
        foreach (var row in table.UsedRows.Where(r => only == null || only.Contains(r.Hymn)))
        {
            try
            {
                var midi = HymnMidi.Parse(File.ReadAllBytes(Path.Combine(dir, row.Hymn + ".mid")));
                string tm = Path.Combine(dir, "tempo", row.Hymn + ".json");
                var map = File.Exists(tm) ? HymnTempoMap.Parse(File.ReadAllText(tm)) : HymnTempoMap.Identity(row.Hymn);
                var cand = HymnCandidate.From(midi, map, row);
                TempoExcerpt x;
                string err;
                if (!TempoExcerpt.TryCut(cand, out x, out err)) throw new InvalidOperationException(err);
                foreach (HymnLevel level in new[] { HymnLevel.Easy, HymnLevel.Medium, HymnLevel.Hard })
                {
                    var curve = x.CurveFor(level);
                    string clip = TempoRound.ClipName(row.Hymn, level);
                    File.WriteAllText(Path.Combine(outDir, clip + ".curve.txt"), curve.ToText());
                    File.WriteAllText(Path.Combine(outDir, clip + ".score.txt"), ScoreText(clip, curve, x));
                    Console.WriteLine($"{clip}: bars {x.StartBar}-{x.EndBar}, {curve.BeatCount} beats, base {x.BaseBpm:0.#} bpm, {curve.Duration:0.0} s");
                }
            }
            catch (Exception ex)
            {
                bad++;
                Console.Error.WriteLine($"{row.Hymn}: {ex.Message}");
            }
        }
        return bad == 0 ? 0 : 1;
    }
}
