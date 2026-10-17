using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Bonanza.Core.Tests
{
    /// The SFX kit's note choices pinned to what the web's own audio graph played
    /// (Golden/sfx-notes.txt, sfx-keys.txt; regenerate with `node Tools/golden/dump-sfx.mjs`), and the baked
    /// clips pinned to the catalog (Assets/Game/Audio/Sfx/sfx.json, written by Tools/sfx/render-sfx.mjs).
    public class SfxTests
    {
        [Serializable] class Manifest { public Clip[] clips; public long totalBytes; public int graphDelaySamples; }
        [Serializable] class Clip { public string name; public string sfx; public double seconds; public float peak; public long bytes; }

        static string SfxDir => Path.Combine(Application.dataPath, "Game/Audio/Sfx");
        static Manifest LoadManifest() => JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(SfxDir, "sfx.json")));

        [Test]
        public void EverySfxPlaysTheNotesTheWebPlays()
        {
            var rows = GoldenFile.Read("sfx-notes.txt");
            Assert.Greater(rows.Count, 300);
            foreach (var r in rows)
            {
                string ctx = r.Str("ctx"), sfx = r.Str("sfx");
                int deg = r.Int("chordDeg");
                int a = r.Int("arg", 0);
                var key = SfxKey.Of(ctx, deg);
                Assert.AreEqual(r.Str("events"), SfxTheory.Describe(sfx, key, a), $"{ctx} chord {deg} {sfx} {a}");
            }
        }

        [Test]
        public void TheKeyAtABeatIsTheChordTheWebMusicSitsOn()
        {
            var rows = GoldenFile.Read("sfx-keys.txt");
            Assert.Greater(rows.Count, 700);
            foreach (var r in rows)
            {
                string ctx = r.Str("ctx");
                double beat = r.Num("beat");
                var h = Harmony.ForContext(ctx);
                int bars = h.Bars;
                int bar = ((h.BarAt(beat) % bars) + bars) % bars;
                var k = SfxKey.At(ctx, beat);
                Assert.AreEqual(r.Int("bar"), bar, $"{ctx} beat {beat} bar");
                Assert.AreEqual(r.Int("chordDeg"), k.ChordDeg, $"{ctx} beat {beat} chord degree");
                Assert.AreEqual(r.Str("chord"), string.Join(",", k.Chord), $"{ctx} beat {beat} chord");
            }
        }

        [Test]
        public void EveryChordAContextCanBeOnHasItsClips()
        {
            // Every sampled beat of every track, every chord SFX the context ships: a clip exists in the catalog.
            var names = new HashSet<string>(SfxCatalog.All().Select(c => c.Name));
            foreach (var r in GoldenFile.Read("sfx-keys.txt"))
            {
                string ctx = r.Str("ctx");
                foreach (var sfx in SfxTheory.ChordSfx)
                {
                    if (!SfxCatalog.Supports(ctx, sfx)) continue;
                    string n = SfxCatalog.ClipName(ctx, sfx, r.Int("chordDeg"));
                    Assert.IsTrue(names.Contains(n), $"{ctx} {sfx} chord {r.Int("chordDeg")} -> {n}");
                }
            }
        }

        [Test]
        public void ClipNamesAreUniqueAndResolve()
        {
            var all = SfxCatalog.All();
            Assert.AreEqual(all.Count, all.Select(c => c.Name).Distinct().Count());
            foreach (var c in all)
            {
                if (c.Context == null) continue;
                Assert.AreEqual(c.Name, SfxCatalog.ClipName(c.Context, c.Sfx, c.ChordDeg, Math.Max(c.Arg, 0)), c.Name);
            }
            Assert.AreEqual("title_fanfare", SfxCatalog.ClipName("results", "fanfare", 3), "results shares the title's fanfare");
            Assert.AreEqual("swing-kings_count_0", SfxCatalog.ClipName("swing-kings", "count", 0, 0));
            Assert.AreEqual("swing-kings_count_1", SfxCatalog.ClipName("swing-kings", "count", 0, 3));
            Assert.AreEqual("finale-fever_combo_9", SfxCatalog.ClipName("finale-fever", "combo", 0, 12));
            Assert.AreEqual("finale-fever_combo_0", SfxCatalog.ClipName("finale-fever", "combo", 0, 0), "combo 5 is level 0");
            Assert.IsNull(SfxCatalog.ClipName("bounce-brigade", "perfect", 0), "Bounce Brigade has no verdict sound on the web");
        }

        [Test]
        public void TheBakedClipsAreTheCatalog()
        {
            var m = LoadManifest();
            var want = SfxCatalog.All().Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var got = m.clips.Select(c => c.name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(want, got, "catalog and manifest list the same clips (Tools/sfx/render-sfx.mjs USE matrix)");
            foreach (var c in m.clips)
            {
                Assert.IsTrue(File.Exists(Path.Combine(SfxDir, c.name + ".ogg")), c.name + ".ogg");
                Assert.Greater(c.seconds, 0.02, c.name);
                if (c.name != "swoosh") Assert.Greater(c.peak, 0.02f, c.name + " is audible");   // the web's swoosh is the one near-silent voice (peak ~0.005)
            }
            Assert.Less(m.totalBytes, 6L * 1024 * 1024, "SFX payload");
        }

        [Test]
        public void RankStingsAndVerdictsFollowTheWeb()
        {
            Assert.AreEqual("fanfare", SfxTheory.RankSting("S"));
            Assert.AreEqual("perfect", SfxTheory.RankSting("A"));
            Assert.AreEqual("great", SfxTheory.RankSting("B"));
            Assert.AreEqual("good", SfxTheory.RankSting("C"));
            Assert.AreEqual("miss", SfxTheory.RankSting("D"));
            Assert.AreEqual("perfect", SfxTheory.VerdictSfx(Verdict.Perfect));
            Assert.AreEqual("miss", SfxTheory.VerdictSfx(Verdict.Miss));
        }

        [Test]
        public void TheChordLockedSfxDoNotAllFallOnOneChord()
        {
            // The point of the kit: a PERFECT on F and on Bb are different arpeggios.
            var f = SfxKey.Of("swing-kings", 0);
            var bb = SfxKey.Of("swing-kings", 3);
            Assert.AreNotEqual(SfxTheory.Describe("perfect", f), SfxTheory.Describe("perfect", bb));
            Assert.AreEqual(SfxTheory.Describe("count", f, 0), SfxTheory.Describe("count", bb, 0), "the count-in is root and scale only");
        }
    }
}
