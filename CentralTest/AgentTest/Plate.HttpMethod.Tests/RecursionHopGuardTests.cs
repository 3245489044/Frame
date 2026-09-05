using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plate.HttpMethod.Guard;

namespace Plate.HttpMethod.Tests
{
    [TestClass]
    public class RecursionHopGuardTests
    {
        [TestMethod]
        public void Defaults_MatchLegacyContract()
        {
            var g = new RecursionHopGuard();
            Assert.AreEqual(8, g.MaxHop, "默认 MaxHop 应保持既有 8（泛化不破坏契约）");
            Assert.AreEqual("X-Llm-Hop", g.HeaderName, "默认请求头应保持既有 X-Llm-Hop（curl 冒烟/既有调用方契约）");
        }

        [TestMethod]
        public void IsWithinLimit_Bounds()
        {
            var g = new RecursionHopGuard(maxHop: 4);
            Assert.IsTrue(g.IsWithinLimit(0));
            Assert.IsTrue(g.IsWithinLimit(3));
            Assert.IsFalse(g.IsWithinLimit(4), "hop=MaxHop 越界");
            Assert.IsFalse(g.IsWithinLimit(-1), "hop<0 越界");
        }

        [TestMethod]
        public void CustomHeaderName_Applied()
        {
            var g = new RecursionHopGuard(headerName: "X-Custom-Hop");
            Assert.AreEqual("X-Custom-Hop", g.HeaderName);
        }

        [TestMethod]
        public void BuildHeaderValue_Invariant()
        {
            var g = new RecursionHopGuard();
            Assert.AreEqual("5", g.BuildHeaderValue(5));
        }
    }
}
