// dotnet run --project Tools/hymns/beatmap -- <hymns dir> <out dir> [hymn,hymn,...]
// For each hymn in <dir>/excerpts.txt marked `use yes` (or the list given): <out>/<hymn>.beatmap.json, built
// from <dir>/<hymn>.mid and <dir>/tempo/<hymn>.json (identity when there is no map) by the same Core code the
// game runs. Prints one line per hymn; exits 1 when a hymn cannot be built.
using System;
using System.IO;
using System.Linq;
using Bonanza.Core;

static class Program
{
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
                string json = HymnBeatMap.ToJson(row.Hymn, midi, map, row.Pattern);
                File.WriteAllText(Path.Combine(outDir, row.Hymn + ".beatmap.json"), json);
                var g = HymnGrid.Build(map.Apply(midi), map.Pattern > 0 ? map.Pattern : row.Pattern);
                Console.WriteLine($"{row.Hymn}: {g.Beats.Count} beats, {g.Bars.Count} bars, pattern {g.Pattern}{(g.HasPickup ? " +pickup" : "")}");
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
