using Agent.Axioms;
using Agent.Core.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Axioms.Tests.Loops
{
    /// <summary>RoundSpecLoader 旧 Schema 兼容测试（T7）+ 声明式模板契约测试（T8-T10，根治「区分不了声明加载 vs 回退」盲区）。</summary>
    [TestClass]
    public sealed class RoundSpecLoaderTests
    {
        [TestMethod]
        public void T7_OldSchema_LoadsWithoutException()
        {
            // agent-loop.json（当前 4.1.0，<5.0.0）→ 解析无异常、步骤非空、端口/全局映射保持 null（零配置）。
            RoundSpec spec = RoundSpecLoader.LoadedDefaultSpec;

            Assert.IsNotNull(spec);
            Assert.IsTrue(spec.Steps.Count > 0, "默认骨架应含步骤");
            Assert.IsNull(spec.InputPorts, "旧 Schema 不应声明输入端口");
            Assert.IsNull(spec.OutputPorts, "旧 Schema 不应声明输出端口");
            Assert.IsNull(spec.GlobalKeyMapping, "旧 Schema 不应声明全局映射");
        }

        [TestMethod]
        public void T8_CollectAnalyzeAct_DeclaredTemplate_MaterializesFullGraph()
        {
            // 根因 A 回归锁定：PascalCase 键（NodeId/FuncName/ParameterKeys/ResultKeys/NextNodeConfig…）须完整反序列化，
            // 不再是「多字属性全 null → BuildRoundSpec 返回 null → 回退两步 DefaultConversationSpec（act 层丢失）」。
            // 声明式模板真实生效：Source=Declared（区分「声明加载」vs「回退」，根治 T7 盲区）。
            RoundSpec spec = RoundSpecLoader.LoadWorkflow("collect-analyze-act.json");

            Assert.IsNotNull(spec, "collect-analyze-act.json 应被声明式加载，而非回退默认骨架");
            Assert.AreEqual(RoundSpecSource.Declared, spec.Source, "应来自声明式模板（真实生效），非内置回退");
            Assert.AreEqual(3, spec.Steps.Count, "三步：collect(cline_execute) → analyze(answer) → act(cline_execute)");
            Assert.AreEqual("cline_execute", spec.Steps[0].FuncName);
            Assert.AreEqual("answer", spec.Steps[1].FuncName);
            Assert.AreEqual("cline_execute", spec.Steps[2].FuncName);
            Assert.AreEqual("analyze_result", spec.Steps[1].ResultKeys[0], "answer 步应声明 analyze_result 结果键");
            Assert.IsTrue(spec.Steps[2].ParameterKeys.Contains("question"),
                "act 步应显式消费 question（E3：不依赖 l1 恰好是原文的隐式约定）");
            Assert.AreEqual(RoundSpecSuccessCriterion.RunToEnd, spec.SuccessCriterion,
                "会话模板应声明 run_to_end 成功准则（不依赖 decision 决策词）");
        }

        [TestMethod]
        public void T9_AgentLoop_DeclaredTemplate_DeclaredSource()
        {
            // agent-loop.json 应真实生效（Source=Declared），而非被硬编码 DefaultRoundSpec（步骤等价）兜底掩盖。
            RoundSpec spec = RoundSpecLoader.LoadedDefaultSpec;

            Assert.IsNotNull(spec);
            Assert.AreEqual(RoundSpecSource.Declared, spec.Source, "agent-loop.json 应被声明式加载，非内置兜底");
            Assert.AreEqual(4, spec.Steps.Count, "主路径：guard_input → provide_prompt → cline_execute → rule_match");
            Assert.AreEqual(RoundSpecSuccessCriterion.DecisionWord, spec.SuccessCriterion,
                "主循环模板保持 decision 决策词成功准则（主循环语义零回归）");
        }

        [TestMethod]
        public void T10_BuiltinDefaultConversation_FallbackSource()
        {
            // 内置默认会话骨架：来源标记 BuiltinDefault + RunToEnd 成功准则（模板缺失兜底时语义仍正确）。
            RoundSpec spec = RoundSpecLoader.DefaultConversationSpec;

            Assert.AreEqual(RoundSpecSource.BuiltinDefault, spec.Source);
            Assert.AreEqual(RoundSpecSuccessCriterion.RunToEnd, spec.SuccessCriterion);
            Assert.AreEqual(2, spec.Steps.Count);
        }
    }
}
