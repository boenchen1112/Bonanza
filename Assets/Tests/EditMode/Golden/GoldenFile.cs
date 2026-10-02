// Reads the golden text files written by Tools/golden/dump-charts.mjs: one
// record per line, space-separated, "#" header naming the columns, "-" = absent.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace RhythmGame.Core.Tests
{
    public sealed class GoldenRow
    {
        readonly Dictionary<string, string> _v;
        public GoldenRow(Dictionary<string, string> v) { _v = v; }
        public bool Has(string k) => _v.TryGetValue(k, out var s) && s != "-";
        public string Str(string k) => _v[k];
        public double Num(string k, double dflt = 0) => Has(k) ? double.Parse(_v[k], CultureInfo.InvariantCulture) : dflt;
        public int Int(string k, int dflt = -1) => Has(k) ? int.Parse(_v[k], CultureInfo.InvariantCulture) : dflt;
        public bool Flag(string k) => Has(k) && _v[k] == "1";
    }

    public static class GoldenFile
    {
        public static List<GoldenRow> Read(string name)
        {
            string path = Path.Combine(Application.dataPath, "Tests", "EditMode", "Golden", name);
            var rows = new List<GoldenRow>();
            string[] cols = null;
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.Length == 0) continue;
                if (line.StartsWith("#")) { cols = line.Substring(1).Trim().Split(' '); continue; }
                var cells = line.Split(' ');
                var d = new Dictionary<string, string>();
                for (int i = 0; i < cols.Length; i++) d[cols[i]] = cells[i];
                rows.Add(new GoldenRow(d));
            }
            return rows;
        }
    }
}
