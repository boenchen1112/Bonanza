using System.Linq;
using NUnit.Framework;

namespace Bonanza.Core.Tests
{
    // URL query parsing and web-scene-id resolution (web main.js boot params, nav.js resolveActivation).
    public class UrlParamsTests
    {
        static readonly string[] Games = { "swing-kings", "drumline-dash", "bounce-brigade", "chomp-chorus", "finale-fever" };

        [Test]
        public void EmptyAndQuerylessUrlsGiveDefaults()
        {
            foreach (var u in new[] { null, "", "http://h/", "http://h/index.html#x", "?" })
            {
                var o = UrlParams.Parse(u);
                Assert.IsFalse(o.Autoplay); Assert.IsFalse(o.Perf); Assert.IsFalse(o.Party);
                Assert.IsNull(o.Game); Assert.IsNull(o.Scene); Assert.IsNull(o.Scale);
                Assert.AreEqual(-1, o.PartySeed);
                Assert.AreEqual(0, o.Ignored.Count, u);
            }
        }

        [Test]
        public void ExistingHarnessParamsStillParse()
        {
            var o = UrlParams.Parse("http://127.0.0.1:7961/?autoplay&game=chomp-chorus&quality=low");
            Assert.IsTrue(o.Autoplay);
            Assert.AreEqual("chomp-chorus", o.Game);
            Assert.AreEqual("low", o.Quality);
            Assert.AreEqual(0, o.Ignored.Count);

            o = UrlParams.Parse("?autoplay&party=2");
            Assert.IsTrue(o.Party); Assert.AreEqual(2, o.PartySeed);

            o = UrlParams.Parse("?autoplay&party");
            Assert.IsTrue(o.Party); Assert.AreEqual(-1, o.PartySeed); Assert.AreEqual(0, o.Ignored.Count);

            o = UrlParams.Parse("?inputlog&latencySlew=fast");
            Assert.IsTrue(o.InputLog); Assert.IsTrue(o.LatencyFast);
        }

        [Test]
        public void FragmentIsNotPartOfTheQuery()
        {
            var o = UrlParams.Parse("http://h/?game=swing-kings#perf");
            Assert.AreEqual("swing-kings", o.Game);
            Assert.IsFalse(o.Perf);
        }

        [Test]
        public void FlagsAreOnWhenPresentUnlessExplicitlyOff()
        {
            Assert.IsTrue(UrlParams.Parse("?perf").Perf);
            Assert.IsTrue(UrlParams.Parse("?perf=1").Perf);
            Assert.IsFalse(UrlParams.Parse("?perf=0").Perf);
            Assert.IsFalse(UrlParams.Parse("?autoplay=false").Autoplay);
            Assert.IsTrue(UrlParams.Parse("?debug").Debug);
        }

        [Test]
        public void QualityAndSwingInputAreCaseInsensitiveAndValidated()
        {
            Assert.AreEqual("medium", UrlParams.Parse("?quality=Medium").Quality);
            Assert.AreEqual("auto", UrlParams.Parse("?quality=auto").Quality);
            Assert.AreEqual("mouse", UrlParams.Parse("?swingInput=MOUSE").SwingInputName);
            Assert.AreEqual("camera", UrlParams.Parse("?swingInput=camera").SwingInputName);

            var bad = UrlParams.Parse("?quality=ultra&swingInput=gamepad");
            Assert.IsNull(bad.Quality); Assert.IsNull(bad.SwingInputName);
            Assert.AreEqual(2, bad.Ignored.Count);
            StringAssert.Contains("ultra", bad.Ignored[0]);
            StringAssert.Contains("gamepad", bad.Ignored[1]);

            Assert.IsNull(UrlParams.Parse("?quality=3").Quality);   // a number is not a level
        }

        [Test]
        public void ScaleIsClampedAndGarbageIsIgnored()
        {
            Assert.AreEqual(1.25f, UrlParams.Parse("?scale=1.25").Scale);
            Assert.AreEqual(0.5f, UrlParams.Parse("?scale=0.5").Scale);
            Assert.AreEqual(2f, UrlParams.Parse("?scale=2").Scale);

            var lo = UrlParams.Parse("?scale=0.1");
            Assert.AreEqual(UrlParams.ScaleMin, lo.Scale);
            Assert.AreEqual(1, lo.Ignored.Count);
            StringAssert.Contains("clamped", lo.Ignored[0]);

            var hi = UrlParams.Parse("?scale=9");
            Assert.AreEqual(UrlParams.ScaleMax, hi.Scale);

            foreach (var junk in new[] { "?scale=abc", "?scale=", "?scale", "?scale=NaN", "?scale=Infinity" })
            {
                var o = UrlParams.Parse(junk);
                Assert.IsNull(o.Scale, junk);
                Assert.AreEqual(1, o.Ignored.Count, junk);
            }
        }

        [Test]
        public void PartySeedMustBeANonNegativeInteger()
        {
            var o = UrlParams.Parse("?party=abc");
            Assert.IsTrue(o.Party); Assert.AreEqual(-1, o.PartySeed); Assert.AreEqual(1, o.Ignored.Count);
            o = UrlParams.Parse("?party=-3");
            Assert.AreEqual(-1, o.PartySeed); Assert.AreEqual(1, o.Ignored.Count);
        }

        [Test]
        public void SeedParsesAsUnsigned()
        {
            Assert.AreEqual(12345u, UrlParams.Parse("?seed=12345").Seed);
            Assert.IsNull(UrlParams.Parse("?seed=-1").Seed);
            Assert.IsNull(UrlParams.Parse("?seed=x").Seed);
        }

        [Test]
        public void UnknownEmptyAndRepeatedKeysAreReportedNotFatal()
        {
            var o = UrlParams.Parse("?bogus=1&game=&scene=options&scene=title&from=party");
            Assert.IsNull(o.Game);
            Assert.AreEqual("options", o.Scene);   // the first wins
            Assert.AreEqual(4, o.Ignored.Count);
            Assert.IsTrue(o.Ignored.Any(r => r.Contains("unknown parameter 'bogus'")));
            Assert.IsTrue(o.Ignored.Any(r => r.StartsWith("game: empty")));
            Assert.IsTrue(o.Ignored.Any(r => r.StartsWith("scene: repeated")));
            Assert.IsTrue(o.Ignored.Any(r => r.StartsWith("from:")));
        }

        [Test]
        public void ValuesAreUnescaped()
        {
            Assert.AreEqual("swing-kings", UrlParams.Parse("?game=swing%2Dkings").Game);
            Assert.AreEqual("a b", UrlParams.Parse("?game=a%20b").Game);
            Assert.AreEqual("bad%zz", UrlParams.Parse("?game=bad%zz").Game);   // malformed escapes stay as typed
        }

        // ------------------------------------------------------------ scene ids

        [Test]
        public void ShellScenesMapToTheirScreens()
        {
            Assert.AreEqual(SceneKind.Title, SceneIds.Resolve("title", Games, false).Kind);
            Assert.AreEqual(SceneKind.FreePlay, SceneIds.Resolve("freeplay", Games, false).Kind);
            Assert.AreEqual(SceneKind.FreePlay, SceneIds.Resolve("menu", Games, false).Kind);
            Assert.AreEqual(SceneKind.Roster, SceneIds.Resolve("roster", Games, false).Kind);
            Assert.AreEqual(SceneKind.Party, SceneIds.Resolve("party", Games, false).Kind);
            Assert.AreEqual(SceneKind.Options, SceneIds.Resolve("options", Games, false).Kind);
        }

        [Test]
        public void EveryRegisteredGameIdResolvesToItself()
        {
            foreach (var g in Games)
            {
                var t = SceneIds.Resolve(g, Games, false);
                Assert.AreEqual(SceneKind.Game, t.Kind); Assert.AreEqual(g, t.Game);
                Assert.AreEqual(g, SceneIds.ResolveGame(g, Games, false));
            }
        }

        [Test]
        public void PlayNeedsAGameAndUnregisteredIdsAreUnknown()
        {
            Assert.AreEqual("finale-fever", SceneIds.Resolve("play", Games, false, "finale-fever").Game);
            Assert.AreEqual(SceneKind.Unknown, SceneIds.Resolve("play", Games, false).Kind);
            Assert.AreEqual(SceneKind.Unknown, SceneIds.Resolve("play", Games, false, "bogus").Kind);
            foreach (var id in new[] { "bogus", "", "Title", "results", "chars-demo", "swing_kings", null })
                Assert.AreEqual(SceneKind.Unknown, SceneIds.Resolve(id, Games, false).Kind, id);
            Assert.IsNull(SceneIds.ResolveGame("bogus", Games, true));
            Assert.IsNull(SceneIds.ResolveGame(null, Games, true));
        }

        [Test]
        public void DebugIdsNeedTheDebugFlag()
        {
            Assert.IsNull(SceneIds.ResolveGame("fault", Games, false));
            Assert.AreEqual("fault", SceneIds.ResolveGame("fault", Games, true));
            Assert.AreEqual(SceneKind.Unknown, SceneIds.Resolve("fault", Games, false).Kind);
            Assert.AreEqual("fault", SceneIds.Resolve("fault", Games, true).Game);
        }
    }
}
