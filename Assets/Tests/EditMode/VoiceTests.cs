using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Bonanza.Core.Tests
{
    /// The games' own instrument voices (web `audio.voices.*` in games/<game>/index.js), baked by
    /// Tools/sfx/render-voices.mjs from the web's charts and audio graph (Assets/Game/Audio/Voices/voices.json) and
    /// named by Core's VoiceCatalog from Core's charts and harmony. Each game's required clips are enumerated here
    /// again from the chart and theory data, independently of the catalog's own lists.
    public class VoiceTests
    {
        [Serializable] class Manifest { public Clip[] clips; public long totalBytes; }
        [Serializable] class Clip { public string name; public string game; public double seconds; public float peak; public long bytes; }

        static string VoiceDir => Path.Combine(Application.dataPath, "Game/Audio/Voices");
        static Manifest _m;
        static Manifest M => _m ??= JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(VoiceDir, "voices.json")));
        static HashSet<string> Baked => new HashSet<string>(M.clips.Select(c => c.name));

        static int BarOf(double beat, Harmony h) => (int)Math.Floor((beat - h.StartBeat) / h.BeatsPerBar);

        [Test]
        public void TheBakedVoicesAreTheCatalog()
        {
            var want = VoiceCatalog.All().OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var got = M.clips.Select(c => c.name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(want, got, "catalog and manifest list the same clips (Tools/sfx/render-voices.mjs recipes vs Core/VoiceCatalog.cs)");
            Assert.AreEqual(want.Length, want.Distinct().Count());
            foreach (var c in M.clips)
            {
                Assert.IsTrue(File.Exists(Path.Combine(VoiceDir, c.name + ".ogg")), c.name + ".ogg");
                Assert.Greater(c.peak, 0.02f, c.name + " is audible");
                Assert.Greater(c.seconds, 0.02, c.name);
                Assert.Less(c.seconds, 6.0, c.name + " is not an endless tail");
            }
        }

        [Test]
        public void TheVoicePayloadIsUnderTheCap()
        {
            long files = M.clips.Sum(c => new FileInfo(Path.Combine(VoiceDir, c.name + ".ogg")).Length);
            Assert.AreEqual(M.totalBytes, files, "manifest bytes are the files'");
            Assert.Less(files, 6L * 1024 * 1024, "voice payload (the ticket's cap)");
        }

        [Test]
        public void ADecodedGameStaysWithinTheMemoryBudget()
        {
            // DecompressOnLoad keeps 16-bit stereo at 44.1 kHz per clip while the game is loaded.
            foreach (var g in VoiceCatalog.Games)
            {
                double sec = M.clips.Where(c => c.game == g).Sum(c => c.seconds);
                double mb = sec * 44100 * 2 * 2 / 1048576;
                Assert.Less(mb, 32, $"{g}: {sec:F0} s decoded = {mb:F1} MB");
            }
        }

        [Test]
        public void SwingKingsHasNoVoicesOfItsOwn()
        {
            // Its web build only calls sfx(); the verdicts, impact, coin and ticks are the SFX kit's.
            Assert.AreEqual(0, VoiceCatalog.ClipsOf("swing-kings").Count);
            Assert.IsFalse(M.clips.Any(c => c.game == "swing-kings"));
        }

        [Test]
        public void DrumlineHasEveryTomAndPercussionVoice()
        {
            var baked = Baked;
            var h = new[] { VoiceCatalog.DrumCallHat, VoiceCatalog.DrumHandoff, VoiceCatalog.DrumCrash, VoiceCatalog.DrumFumble, VoiceCatalog.DrumSnare };
            foreach (var n in h) Assert.IsTrue(baked.Contains(n), n);
            // A call phrase indexes the five toms by note number, so the longest phrase decides how many are reached.
            int longest = DrumChart.Build().Max(p => p.CallBeats.Count);
            for (int i = 0; i < longest; i++) Assert.IsTrue(baked.Contains(VoiceCatalog.DrumCall(i)), "call note " + i);
            for (int r = 1; r < new DrumlineRound().Racers.Count; r++) Assert.IsTrue(baked.Contains(VoiceCatalog.DrumStumble(r)), "racer " + r);
        }

        [Test]
        public void ChompHasEveryLaneOnEveryChordItCanBeSungOn()
        {
            var baked = Baked;
            var h = Harmony.ChompChorus;
            var chart = ChompChart.Build(0);
            Assert.AreEqual(new[] { 0, 3, 4, 6 }, VoiceCatalog.Degrees(h), "the chorus sits on four chords");
            // Every note of every lane, on the chord of its bar, at each verdict's voice; and the bed of every bar.
            foreach (var n in chart)
            {
                int deg = h.ChordDegAt(BarOf(n.Beat, h));
                Assert.IsTrue(baked.Contains(VoiceCatalog.ChompSing(n.Lane, deg)), $"sing lane {n.Lane} chord {deg}");
                Assert.IsTrue(baked.Contains(VoiceCatalog.ChompBell(n.Lane, deg)), $"bell lane {n.Lane} chord {deg}");
                Assert.IsTrue(baked.Contains(VoiceCatalog.ChompSour(n.Lane, deg)), $"sour lane {n.Lane} chord {deg}");
                if (n.Finale) Assert.IsTrue(baked.Contains(VoiceCatalog.ChompFinaleSing(n.Lane, deg)), $"finale lane {n.Lane} chord {deg}");
            }
            for (int bar = (int)(-ChompChart.LeadBeats / 4); bar * 4 <= ChompChart.EndBeat; bar++)
                for (int lane = 0; lane < 4; lane++)
                    Assert.IsTrue(baked.Contains(VoiceCatalog.ChompBed(lane, h.ChordDegAt(bar + 2))), $"bed bar {bar} lane {lane}");
            for (int lane = 0; lane < 4; lane++)
            {
                Assert.IsTrue(baked.Contains(VoiceCatalog.ChompGulp(lane, false)));
                Assert.IsTrue(baked.Contains(VoiceCatalog.ChompGulp(lane, true)));
            }
        }

        [Test]
        public void TheMixConstantsAreTheWebsGains()
        {
            // web drumlineDash playCall x1.25; crash 0.30 over 0.16; snare 0.58 over 0.42.
            Assert.AreEqual(1.25f, VoiceMix.DrumFinaleCall);
            Assert.AreEqual(0.30f / 0.16f, VoiceMix.DrumCrashFinale, 1e-6);
            Assert.AreEqual(0.58f / 0.42f, VoiceMix.DrumSnarePerfect, 1e-6);
            // web chompChorus _sing: 0.30 perfect, 0.25 great (the baked clip), 0.18 good.
            Assert.AreEqual(1.2f, VoiceMix.ChompSingPerfect, 1e-6);
            Assert.AreEqual(0.72f, VoiceMix.ChompSingGood, 1e-6);
        }
    }
}
