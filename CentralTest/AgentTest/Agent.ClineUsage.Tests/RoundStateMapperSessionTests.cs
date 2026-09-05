using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Axioms;
using Agent.Contract;
using Agent.Contract.Data;
using Agent.Core.Nodes;
using WorkFlowMethod.LoopFramework.Axioms;

namespace Agent.Modules.Tests
{
    /// <summary>
    /// RoundStateMapper 跨轮状态映射单元测试（离线）。
    /// 覆盖 session_id（Cline 会话连续性）在 Seed/Harvest 的跨轮透传。
    /// 注：Agent.Core.Tests 项目当前因引用已删除的 Agent.Planner/DynamicWorkflowMerger 而无法编译，
    /// 故临时置于可离线编译运行的 Agent.Modules.Tests 下，待 Core.Tests 修复后再迁回。
    /// </summary>
    [TestClass]
    [TestCategory("Modules_Unit")]
    public class RoundStateMapperSessionTests
    {
        /// <summary>由 Seed 派生的字典包成统一数据空间（Harvest 消费 IDataSpace）；测试进程未注册契约，故关闭校验。</summary>
        private static IDataSpace SeedIntoSpace(AxiomContext ctx)
        {
            var space = new DataSpace(validate: false);
            foreach (var kv in RoundStateMapper.Seed(ctx))
                space.Set(kv.Key, kv.Value);
            return space;
        }

        [TestMethod]
        public void Seed_带sessionId_应注入数据空间()
        {
            var ctx = new AxiomContext { UserInput = "hello" };
            ctx.Data[RoundKeys.SessionId] = "sess-1";

            var vars = RoundStateMapper.Seed(ctx);

            Assert.AreEqual("sess-1", vars[RoundKeys.SessionId], "Seed 应把跨轮 session_id 注入 vars");
        }

        [TestMethod]
        public void Seed_无sessionId_应为null()
        {
            var ctx = new AxiomContext { UserInput = "hello" };
            var vars = RoundStateMapper.Seed(ctx);

            Assert.IsNull(vars[RoundKeys.SessionId], "无历史 session_id 时应为 null");
        }

        [TestMethod]
        public void Harvest_sessionId_应持久化回上下文()
        {
            var ctx = new AxiomContext { UserInput = "hello" };
            var vars = SeedIntoSpace(ctx);
            vars.Set(RoundKeys.SessionId, "sess-2");

            RoundStateMapper.Harvest(ctx, vars);

            Assert.AreEqual("sess-2", ctx.Data[RoundKeys.SessionId], "Harvest 应把 session_id 持久化到 AxiomContext.Data");
        }

        [TestMethod]
        public void Harvest_不再收割ExecResult_R2由WorkflowRunner显式写()
        {
            // R2 收敛：ExecResult 从「Harvest 通配收割」改为「WorkflowRunner 轮内显式同源写」——
            // RoundStateMapper 层不再负责 ExecResult 收割（ur_nodes cline_execute.exec_result 端口为节点产出唯一权威，
            // state.Data[ExecResult] 仅作跨轮派生缓存，由 WorkflowRunner 单一显式写点维护）。故 vars 中即使有 ExecResult，
            // Harvest 也不应把它写进 AxiomContext.Data（消除 R2 产出值双落库）。
            var ctx = new AxiomContext { UserInput = "hello" };
            var vars = SeedIntoSpace(ctx);
            vars.Set(RoundKeys.ExecResult, new ClineResult(Ok: true, Text: "上轮结果", Error: null));

            RoundStateMapper.Harvest(ctx, vars);

            Assert.IsFalse(ctx.Data.ContainsKey(RoundKeys.ExecResult), "Harvest 不再通配收割 ExecResult（R2：改由 WorkflowRunner 显式写）");
        }
    }
}
