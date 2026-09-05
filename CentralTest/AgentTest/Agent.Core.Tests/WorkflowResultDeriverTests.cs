using System.Collections.Generic;
using System.Linq;
using Agent.Contract.Nodes;
using Agent.Core.Nodes;
using WorkflowResultDeriverRef = global::Operation.HierarchyMethod.Nodes.Relations.WorkflowResultDeriver;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 终答推导器 <see cref="WorkflowResultDeriver"/> 定向测试 —— 终答由**控制流引用链（Calls 边）**推导，
    /// 而非 FinalNode/FinalPort 字符串契约 / 逆序猜最后一步。覆盖 collect-analyze-act 的 cline_execute 同 FuncName 两次。
    /// </summary>
    [TestClass]
    public class WorkflowResultDeriverTests
    {
        /// <summary>collect-analyze-act 形态：cline_execute → answer → cline_execute（同 FuncName 两次，链尾 = act）。</summary>
        private static RoundSpec CollectAnalyzeAct() => new("caa", new RoundStep[]
        {
            new("cline_execute", new[] { "user_input" }, new[] { "collect_result" }),
            new("answer", new[] { "collect_result" }, new[] { "analyze_result" }),
            new("cline_execute", new[] { "analyze_result" }, new[] { "act_result" }),
        });

        [TestMethod]
        public void LoadWorkflow_CollectAnalyzeAct_ResultKeysParsed()
        {
            // 框架根因回归（8.23 会话回复为空·节点端口空）：nodePorts=[] 说明 ExtractProps 未写入任何 ResultKeys。
            // 若 ResultKeys 未被模板正确反序列化（空）→ ExtractProps 对 AgentExecutorResult 的定向写入 foreach 不执行 → 输出端口空 → 终答空。
            var spec = RoundSpecLoader.LoadWorkflow("collect-analyze-act.json");
            Assert.IsNotNull(spec, "真实模板应加载");
            Assert.AreEqual(3, spec!.Steps.Count);
            CollectionAssert.AreEqual(new[] { "collect_result" }, spec.Steps[0].ResultKeys.ToList(),
                "collect 步 ResultKeys 应解析为 collect_result");
            CollectionAssert.AreEqual(new[] { "analyze_result" }, spec.Steps[1].ResultKeys.ToList(),
                "analyze 步 ResultKeys 应解析为 analyze_result");
            CollectionAssert.AreEqual(new[] { "act_result" }, spec.Steps[2].ResultKeys.ToList(),
                "act 步 ResultKeys 应解析为 act_result");
            Assert.IsTrue(spec.Steps.All(s => s.ResultKeys is { Count: > 0 }),
                "全部步骤 ResultKeys 均非空（否则 ExtractProps 无 ResultKeys 可写 → 节点端口空 → 终答空）");
        }

        [TestMethod]
        public void LoadWorkflow_CollectAnalyzeAct_EffectiveNodeIdsAreUnique()
        {
            // 框架根因回归（8.23 会话回复为空）：真实 collect-analyze-act 模板中 collect/act 同为 cline_execute，
            // 若 EffectiveNodeId 回退 FuncName 会同名 → _stepNodes 键覆盖 → 节点数错位 → 终答推导越界 null。
            // 验证模板 NodeId 被正确解析为稳定节点实例 ID（collect/analyze/act 唯一），杜绝同名覆盖。
            var spec = RoundSpecLoader.LoadWorkflow("collect-analyze-act.json");
            Assert.IsNotNull(spec, "真实模板应加载（不应回退内置默认）");
            Assert.AreEqual(3, spec!.Steps.Count, "真实 collect-analyze-act 模板应为三步");
            var ids = spec.Steps.Select(s => s.EffectiveNodeId).ToList();
            CollectionAssert.AreEqual(new[] { "collect", "analyze", "act" }, ids);
            Assert.AreEqual(3, ids.Distinct().Count(), "collect/act 同为 cline_execute，但 EffectiveNodeId 必须唯一（节点管理面防同名覆盖）");
        }

        [TestMethod]
        public void ResolveFinalStepIndex_ChainTail_IsLastStep()
        {
            // 链尾（出度 0）应为索引 2（act 的 cline_execute），而非第一个 cline_execute（collect）。
            // 链尾推导已收敛至 Agent.Reference（纯逻辑），此处经别名调用验证同源。
            Assert.AreEqual(2, WorkflowResultDeriverRef.ResolveFinalStepIndex(CollectAnalyzeAct()));
        }

        [TestMethod]
        public void ResolveFinalAnswer_TakesChainTailNodeText()
        {
            var spec = CollectAnalyzeAct();
            var nodes = new[]
            {
                new NodeExecutionRecord("cline_execute", "caa",
                    new Dictionary<string, object?> { ["collect_result"] = "collect 产出" },
                    new Dictionary<string, (string Node, string Port)>()),
                new NodeExecutionRecord("answer", "caa",
                    new Dictionary<string, object?> { ["analyze_result"] = "analyze 产出" },
                    new Dictionary<string, (string Node, string Port)>()),
                new NodeExecutionRecord("cline_execute", "caa",
                    new Dictionary<string, object?> { ["act_result"] = "final act 产出" },
                    new Dictionary<string, (string Node, string Port)>()),
            };
            Assert.AreEqual("final act 产出", WorkflowResultDeriver.ResolveFinalAnswer(spec, nodes),
                "终答 = 控制流引用链尾（act）节点输出，而非 collect/analyze 中间产物");
        }

        [TestMethod]
        public void ResolveFinalAnswer_IndexMismatch_ReturnsNull()
        {
            // 模拟 CanExecute 跳过使 nodes（执行序）少于 steps（声明序）：链尾索引越界 → 回退 null（调用方兜底）。
            var spec = CollectAnalyzeAct();
            var nodes = new[]
            {
                new NodeExecutionRecord("cline_execute", "caa",
                    new Dictionary<string, object?> { ["collect_result"] = "c" },
                    new Dictionary<string, (string Node, string Port)>()),
                new NodeExecutionRecord("answer", "caa",
                    new Dictionary<string, object?> { ["analyze_result"] = "a" },
                    new Dictionary<string, (string Node, string Port)>()),
            };
            Assert.IsNull(WorkflowResultDeriver.ResolveFinalAnswer(spec, nodes));
        }
    }
}
