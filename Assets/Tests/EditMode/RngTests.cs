using NUnit.Framework;
using Bonanza.Core;

namespace Bonanza.Core.Tests
{
    public class RngTests
    {
        // Golden values from web/src/core/util.js makeRng (node, 2026-09-25).
        [TestCase(0x9e3779b9u, 0.35888998024165630, 0.10590326134115458, 0.67529047932475805, 0.91793455881997943)]
        [TestCase(0xc40fu, 0.84715384850278497, 0.36877721827477217, 0.52553966408595443, 0.03071541921235621)]
        [TestCase(0xd12bu, 0.99914270080626011, 0.11670192121528089, 0.02998671983368695, 0.08466413174755871)]
        [TestCase(0xb0c3u, 0.05056197592057288, 0.34424890181981027, 0.81947937840595841, 0.81169977225363255)]
        [TestCase(1u, 0.62707394058816135, 0.00273572118021548, 0.52744703995995224, 0.98105096747167408)]
        public void MatchesMulberry32FromTheWebBuild(uint seed, double a, double b, double c, double d)
        {
            var r = new Rng(seed);
            Assert.AreEqual(a, r.Next(), 1e-15);
            Assert.AreEqual(b, r.Next(), 1e-15);
            Assert.AreEqual(c, r.Next(), 1e-15);
            Assert.AreEqual(d, r.Next(), 1e-15);
        }
    }
}

