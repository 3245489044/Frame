using Agent.Axioms;
using Agent.Core;
using Agent.Core.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlowMethod.LoopFramework.Axioms;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 决策端口（LoopDecisionMapper.MapEval）单测。聚焦 E4：exec 成功但仅缺/未知决策词致 ok=false、
    /// 且无实际错误文本时，不得凭空归 ExecutionFailure 虚增熔断预算，应落 Partial。
    /// E2：错误分类权威 = AgentRunner 包装层预填的 explicitError，MapEval 不再二次文本兜底分类。
    /// </summary>
    [TestClass]
    public class LoopDecisionMapperTests
    {
        private static readonly LoopDecisionMapper Mapper = new();

        private static AxiomActionResult Result(bool ok, string? error)
            => new() { Ok = ok, Error = error, Text = "" };

        [TestMethod]
        public void MapEval_okFalse且无错误文本_应落Partial而非Fail()
        {
            // E4 回归：仅缺决策词致 ok=false（exec 成功、无异常）→ 不归执行失败。
            var eval = Mapper.MapEval(Result(ok: false, error: null), error: null);

            Assert.AreEqual(EvalOutcome.Partial, eval);
        }

        [TestMethod]
        public void MapEval_okFalse且显式EmptyResult_应落Fail()
        {
            // 显式错误分类（含 EmptyResult）优先，不落入 Partial 兜底。
            var eval = Mapper.MapEval(Result(ok: false, error: null), error: ErrorClass.EmptyResult);

            Assert.AreEqual(EvalOutcome.Fail, eval);
        }

        [TestMethod]
        public void MapEval_okFalse且未预填错误分类_即使有错误文本_也落Partial()
        {
            // E2：错误分类职责收敛于 AgentRunner 包装层（DeriveErrorClass），MapEval 不再对文本二次分类；
            // 未预填 explicitError 时即使有文本也不凭空判 Fail（避免正常产出误判为错误类别）。
            var eval = Mapper.MapEval(Result(ok: false, error: "任务超时（30s）"), error: null);

            Assert.AreEqual(EvalOutcome.Partial, eval);
        }
    }
}