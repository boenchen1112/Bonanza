using System;
using System.IO;
using System.Text.RegularExpressions;
using Bonanza.Core;
using NUnit.Framework;
using UnityEngine;

namespace Bonanza.Core.Tests
{
    /// Pins the music grid: the constants the player schedules against must be
    /// the ones the renderer measured (Assets/Game/Audio/Music/tracks.json,
    /// written by Tools/music/render.mjs) and must agree with the charts' tempo.
    public class MusicTests
    {
        [Serializable] class Manifest { public Track[] tracks; }
        [Serializable] class Track
        {
            public string id;
            public double bpm, secondsPerBeat, secondsPerBar, firstBeatOffsetSeconds, loopLengthSeconds;
            public int bars, beatsPerBar;
            public bool loop;
        }

        static string Json => File.ReadAllText(Path.Combine(Application.dataPath, "Game/Audio/Music/tracks.json"));

        static Manifest Load() => JsonUtility.FromJson<Manifest>(Json);

        [Test]
        public void TableMatchesRenderManifest()
        {
            var m = Load();
            Assert.AreEqual(MusicTracks.All.Length, m.tracks.Length, "every rendered track has a table entry");
            foreach (var t in m.tracks)
            {
                var info = MusicTracks.Find(t.id);
                Assert.IsNotNull(info, t.id);
                Assert.AreEqual(t.bpm, info.Bpm, t.id + " bpm");
                Assert.AreEqual(t.bars, info.Bars, t.id + " bars");
                Assert.AreEqual(t.loop, info.Loop, t.id + " loop");
                Assert.AreEqual(MusicTrackInfo.BeatsPerBar, t.beatsPerBar, t.id);
                Assert.AreEqual(t.firstBeatOffsetSeconds, info.BeatOffset, 1e-9, t.id + " first-beat offset");
                Assert.AreEqual(t.secondsPerBeat, info.SecondsPerBeat, 1e-9, t.id + " seconds per beat");
                if (t.loop) Assert.AreEqual(t.loopLengthSeconds, info.LoopSeconds, 1e-9, t.id + " loop length");
                // frames of the first mix file listed for this track
                var mm = Regex.Match(Json, "\"id\": \"" + t.id + "\"[\\s\\S]*?\"frames\": (\\d+)");
                Assert.IsTrue(mm.Success, t.id + " frames");
                Assert.AreEqual(int.Parse(mm.Groups[1].Value), info.Frames, t.id + " frames");
            }
        }

        [Test]
        public void GameTemposAreTheCharts()
        {
            Assert.AreEqual(SwingRules.Bpm, MusicTracks.Find("swing-kings").Bpm);
            Assert.AreEqual(DrumChart.Bpm, MusicTracks.Find("drumline-dash").Bpm);
            Assert.IsNull(MusicTracks.Find("bounce-brigade"), "Bounce Brigade plays the hymn clips, not a score of its own");
            Assert.AreEqual(ChompChart.Bpm, MusicTracks.Find("chomp-chorus").Bpm);
            Assert.IsNull(MusicTracks.Find("finale-fever"), "Finale Fever plays the hymn tempo clips, not a score of its own");
        }

        [Test]
        public void FilesHoldWholeBarsAndStartOnTheLeadIn()
        {
            foreach (var t in MusicTracks.All)
            {
                if (!t.Loop) continue;
                // A looping mix is the bar count to within a sample, so a loop never drifts off the grid.
                Assert.AreEqual(t.LoopSeconds, t.FileSeconds, 1.5 / MusicTrackInfo.Rate, t.Id);
                Assert.That(t.BeatOffset, Is.InRange(0.005, 0.02), t.Id + " render latency");
            }
            // The first lead-in bar: lead-in is 2 bars for every game.
            Assert.AreEqual(-8, MusicTracks.GameStartBeat);
            Assert.AreEqual(8, SwingRules.LeadInBeats);
            Assert.AreEqual(8, DrumChart.LeadBeats);
            Assert.AreEqual(8, ChompChart.LeadBeats);
        }

        [Test]
        public void LibraryHoldsEveryClipAtTheRenderedLength()
        {
            var lib = Resources.Load<MusicLibrary>("MusicLibrary");
            Assert.IsNotNull(lib, "Resources/MusicLibrary (rebuild with Tools/music/unity-setup.cs)");
            foreach (var t in MusicTracks.All)
            {
                var e = lib.Find(t.Id);
                Assert.IsNotNull(e, t.Id);
                Assert.AreEqual(t.Levels, e.mixes.Length, t.Id);
                foreach (var c in e.mixes)
                {
                    Assert.IsNotNull(c, t.Id);
                    Assert.AreEqual(t.Frames, c.samples, t.Id + " " + c.name);
                    Assert.AreEqual(MusicTrackInfo.Rate, c.frequency, t.Id);
                }
            }
        }

        [Test]
        public void IntensityFollowsComboAndMissStreaks()
        {
            var m = new MusicIntensity();
            Assert.AreEqual(1, m.Pending);
            for (int i = 1; i <= 4; i++) { m.OnJudge(true); Assert.AreEqual(1, m.Pending, "combo " + i); }
            m.OnJudge(true); Assert.AreEqual(2, m.Pending, "combo 5");
            for (int i = 6; i <= 19; i++) m.OnJudge(true);
            Assert.AreEqual(2, m.Pending, "combo 19");
            m.OnJudge(true); Assert.AreEqual(3, m.Pending, "combo 20");
            m.OnJudge(false); Assert.AreEqual(1, m.Pending, "one miss");
            m.OnJudge(false); Assert.AreEqual(0, m.Pending, "two misses");
            m.OnJudge(true); Assert.AreEqual(1, m.Pending, "a hit climbs back");
            m.Reset(); Assert.AreEqual(1, m.Pending);
        }
    }
}
