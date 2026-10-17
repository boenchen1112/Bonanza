// Pins the input vocabulary to the web prototype's core/input.js: KEY_MAP, PAD_MAP,
// the stick-as-d-pad rule, and the held set that blur releases.

using System.Collections.Generic;
using NUnit.Framework;
using Bonanza.Core;

namespace Bonanza.Core.Tests
{
    public class InputMapTests
    {
        [TestCase("Space", "a"), TestCase("KeyZ", "a"), TestCase("KeyJ", "a"), TestCase("Enter", "a")]
        [TestCase("KeyX", "b"), TestCase("KeyK", "b")]
        [TestCase("ArrowLeft", "left"), TestCase("KeyA", "left")]
        [TestCase("ArrowRight", "right"), TestCase("KeyD", "right")]
        [TestCase("ArrowUp", "up"), TestCase("KeyW", "up")]
        [TestCase("ArrowDown", "down"), TestCase("KeyS", "down")]
        [TestCase("Escape", "pause"), TestCase("KeyP", "pause")]
        public void KeyTableMatchesWeb(string code, string action) => Assert.AreEqual(action, InputMap.ForKey(code));

        [Test]
        public void UnmappedKeyIsNull() => Assert.IsNull(InputMap.ForKey("KeyQ"));

        [Test]
        public void KeyTableHasExactlySixteenKeys() => Assert.AreEqual(16, InputMap.Keys.Length);

        [TestCase(0, "a"), TestCase(1, "b"), TestCase(2, "b"), TestCase(3, "a")]
        [TestCase(12, "up"), TestCase(13, "down"), TestCase(14, "left"), TestCase(15, "right")]
        [TestCase(9, "pause")]
        public void PadTableMatchesWeb(int button, string action) => Assert.AreEqual(action, InputMap.ForPadButton(button));

        [Test]
        public void UnmappedPadButtonIsNull() => Assert.IsNull(InputMap.ForPadButton(4));

        [Test]
        public void BackIsBOrPause()
        {
            Assert.IsTrue(InputMap.IsBack("b"));
            Assert.IsTrue(InputMap.IsBack("pause"));
            Assert.IsFalse(InputMap.IsBack("a"));
            Assert.IsFalse(InputMap.IsBack("left"));
        }

        [Test]
        public void FinaleLanesFoldLikeTheWeb()
        {
            Assert.AreEqual("left", InputMap.FinaleLane("up"));
            Assert.AreEqual("right", InputMap.FinaleLane("down"));
            Assert.AreEqual("right", InputMap.FinaleLane("b"));
            Assert.AreEqual("a", InputMap.FinaleLane("a"));
            Assert.AreEqual("left", InputMap.FinaleLane("left"));
        }

        static List<string> Run(StickDpad s, params (float x, float y)[] samples)
        {
            var log = new List<string>();
            foreach (var (x, y) in samples) s.Update(x, y, (a, d) => log.Add((d ? "+" : "-") + a));
            return log;
        }

        [Test]
        public void StickDeadzoneIsHalfExclusive()
        {
            var log = Run(new StickDpad(), (0.5f, 0), (-0.5f, 0), (0, 0.5f), (0, -0.5f));
            Assert.IsEmpty(log);
            log = Run(new StickDpad(), (0.51f, 0));
            CollectionAssert.AreEqual(new[] { "+right" }, log);
        }

        [Test]
        public void StickUpIsNegativeY()
        {
            CollectionAssert.AreEqual(new[] { "+up" }, Run(new StickDpad(), (0, -0.9f)));
            CollectionAssert.AreEqual(new[] { "+down" }, Run(new StickDpad(), (0, 0.9f)));
        }

        [Test]
        public void StickEmitsOnlyChangesAndReleases()
        {
            var log = Run(new StickDpad(), (0.9f, 0), (0.95f, 0), (0, 0), (-0.9f, 0));
            CollectionAssert.AreEqual(new[] { "+right", "-right", "+left" }, log);
        }

        [Test]
        public void StickDiagonalHoldsTwoDirections()
        {
            var log = Run(new StickDpad(), (0.8f, -0.8f), (0, 0));
            CollectionAssert.AreEquivalent(new[] { "+right", "+up", "-right", "-up" }, log);
        }

        [Test]
        public void HeldSetIgnoresDoubleReleaseAndReleasesAllOnBlur()
        {
            var h = new HeldActions();
            Assert.IsTrue(h.Down("a"));
            Assert.IsTrue(h.Down("b"));
            Assert.IsTrue(h.IsHeld("a"));
            Assert.IsTrue(h.Up("a"));
            Assert.IsFalse(h.Up("a"));
            h.Down("right"); h.Down("a");
            CollectionAssert.AreEqual(new[] { "a", "b", "right" }, h.ReleaseAll());
            Assert.AreEqual(0, h.Count);
            Assert.IsFalse(h.IsHeld("a"));
            Assert.IsEmpty(h.ReleaseAll());
        }
    }
}
