using System.Collections.Generic;
using Agent.Axioms;
using Agent.Core.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlowMethod.LoopFramework.Axioms;

namespace Agent.Axioms.Tests
{
    /// <summary>
    /// 会话工作流参数种子断链修复测试：RoundStateMapper.Seed 必须把 state.Data 中
    /// ConversationPipeline.SeedWorkflow 写入的会话键（system/l1/l2/prompt/question/conversation）透传到骨架 Vars，
    /// 否则 collect-analyze-act 等无 provide_prompt 步的对话驱动线性链模板步骤参数为空 → 空返回 → 不触 LLM（终答空 / CallLog=0）。
    /// </summary>
    [TestClass]
    public class RoundStateMapperTests
    {
        [TestMethod]
        public void Seed_PropagatesConversationSeedKeys()
        {
            var data = new Dictionary<string, object>
            {
                ["user_input"] = "GUI 测试：统一 LLM 网关本地路由",
                ["session_id"] = "s1",
                ["task_id"] = "s1",
                ["system"] = "你是严谨的对话助手",
                ["l1"] = "任务级提示",
                ["l2"] = "步骤级提示",
                ["prompt"] = "对话窗口",
                ["question"] = "GUI 测试：统一 LLM 网关本地路由",
                ["conversation"] = "对话窗口",
            };
            var state = new AxiomContext(data);

            var vars = RoundStateMapper.Seed(state);

            Assert.AreEqual("你是严谨的对话助手", vars[RoundKeys.System], "system 应透传到骨架 Vars");
            Assert.AreEqual("任务级提示", vars[RoundKeys.L1], "l1 应透传到骨架 Vars");
            Assert.AreEqual("步骤级提示", vars[RoundKeys.L2], "l2 应透传到骨架 Vars");
            Assert.AreEqual("GUI 测试：统一 LLM 网关本地路由", vars["question"], "question 应透传到骨架 Vars");
            Assert.AreEqual("对话窗口", vars["conversation"], "conversation 应透传到骨架 Vars");
            Assert.AreEqual("对话窗口", vars[RoundKeys.Prompt], "prompt 应透传到骨架 Vars");
        }
    }
}
