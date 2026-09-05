using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Persistence.Core;             // SnapshotSerializer
using WorkFlowMethod.LoopFramework.Axioms; // AxiomContext / Scope

namespace Agent.Core.Tests
{
    /// <summary>
    /// 快照序列化往返回归基线（Phase 0）。Phase 5（跨轮状态入快照）将在此基线上扩展
    /// BoundaryAccumulator 投影，必须保持本用例始终通过（旧字段兼容）。
    /// </summary>
    [TestClass]
    [TestCategory("Persistence")]
    public class SnapshotSerializerTests
    {
        [TestMethod]
        public void RoundTrip_保留轮次_预算_令牌_数据()
        {
            var ctx = new AxiomContext
            {
                UserInput = "hello",
                Round = 3,
                LastResultText = "some text",
                Scope = Scope.LoopBody,
            };
            ctx.Token.Max = 5;
            ctx.Token.Advance();
            ctx.Token.Advance();
            ctx.Budget.Escalate();
            ctx.Data["boundary_state"] = "snapshot-me";
            ctx.Data["user_input"] = "hello";

            var json = SnapshotSerializer.Serialize(ctx);
            var restored = SnapshotSerializer.Deserialize(json);

            Assert.AreEqual(3, restored.Round);
            Assert.AreEqual(2, restored.Token.Value);
            Assert.AreEqual(5, restored.Token.Max);
            Assert.AreEqual(1, restored.Budget.Level);
            Assert.AreEqual("some text", restored.LastResultText);
            Assert.AreEqual("hello", restored.UserInput);
            Assert.AreEqual("snapshot-me", restored.Data["boundary_state"]);
            Assert.AreEqual(Scope.LoopBody, restored.Scope);
        }

        [TestMethod]
        public void RoundTrip_空上下文_应可往返()
        {
            var ctx = new AxiomContext { UserInput = "x" };
            var restored = SnapshotSerializer.Deserialize(SnapshotSerializer.Serialize(ctx));
            Assert.AreEqual("x", restored.UserInput);
            Assert.AreEqual(0, restored.Round);
        }
    }
}
