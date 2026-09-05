using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent.Axioms.Skills;
using Agent.Contract.Llm;
using Agent.Core.Signal;
using Operation.LoopFoldMethod.Fold;
using Agent.Skill;
using Operation.LoopFoldMethod.Operator;
using Operation.LoopFoldMethod.Signal;

namespace Agent.Core.Tests
{
    /// <summary>
    /// SK 会话折叠算子（<see cref="SkConversationFoldOperator"/>）测试：纯聊天路径 / 带 MCP 工具路径（Function Calling）。
    /// 验证「skill + MCP + 信号」共同驱动中，算子每步按信号/stepKey 执行并经 SK 产出顶点；MCP 工具装配时走带工具聊天。
    /// 全部零真实 LLM/零真实 MCP（假客户端 + 假工具提供），可在纯 CI 跑。
    /// </summary>
    [TestClass]
    public class SkConversationFoldOperatorTests
    {
        private sealed class FakeLlmClient : ILlmClient
        {
            public string Name => "fake";
            public bool UsedTools;
            public IReadOnlyList<LlmTool>? LastTools;
            public string? LastSystem;
            public string? LastUser;

            public Task<LlmResult> ChatAsync(
                string system, string user, IReadOnlyList<LlmMessage>? history = null, CancellationToken ct = default)
            {
                UsedTools = false;
                LastSystem = system;
                LastUser = user;
                return Task.FromResult(new LlmResult("回复"));
            }

            public Task<LlmResult> ChatWithToolsAsync(
                string system, string user, IReadOnlyList<LlmTool>? tools = null,
                IReadOnlyList<LlmMessage>? history = null, CancellationToken ct = default)
            {
                UsedTools = true;
                LastTools = tools;
                LastSystem = system;
                LastUser = user;
                return Task.FromResult(new LlmResult("回复"));
            }
        }

        private sealed class FakeToolProvider : IMcpFoldToolProvider
        {
            public IReadOnlyList<LlmTool> Tools { get; set; } = new List<LlmTool>
            {
                new("seq_think", "序列思考", _ => Task.FromResult("{\"ok\":true}")),
            };

            public string? LastCalledTool;
            public string? LastArgsJson;

            public Task<IReadOnlyList<LlmTool>> GetToolsAsync(CancellationToken ct = default)
                => Task.FromResult(Tools);

            public Task<string> CallAsync(string toolName, string? argumentsJson, CancellationToken ct = default)
            {
                LastCalledTool = toolName;
                LastArgsJson = argumentsJson;
                return Task.FromResult($"{{\"status\":\"success\",\"tool\":\"{toolName}\"}}");
            }
        }

        private static FoldContext<ConversationFoldVertex> Ctx()
            => new(0, FoldBudget.Unlimited, Array.Empty<FoldVertex<ConversationFoldVertex>>());

        [TestMethod]
        public async Task ExpandAsync_NoToolProvider_UsesPureChat_ProducesVertex()
        {
            var llm = new FakeLlmClient();
            var op = new SkConversationFoldOperator(llm);
            var signal = new FoldSignal<ConversationFoldVertex>(FoldSignalKind.ExternalInput, "start", "请分析");

            var step = await op.ExpandAsync(
                new ConversationFoldState("s1", "分析", null), signal, "collect", Ctx(), default);

            Assert.IsFalse(llm.UsedTools);
            Assert.AreEqual("collect", step.StepKey);
            Assert.AreEqual("回复", step.Vertex.OutputText);
            Assert.AreEqual("sk", step.Vertex.FuncName);
            Assert.AreEqual("回复", step.Next.LastOutput);
        }

        [TestMethod]
        public async Task ExpandAsync_WithToolProvider_UsesFunctionCallingTools()
        {
            var llm = new FakeLlmClient();
            var op = new SkConversationFoldOperator(llm, toolProvider: new FakeToolProvider());
            var signal = new FoldSignal<ConversationFoldVertex>(FoldSignalKind.ExternalInput, "start", "请分析");

            var step = await op.ExpandAsync(
                new ConversationFoldState("s1", "分析", null), signal, "collect", Ctx(), default);

            Assert.IsTrue(llm.UsedTools);
            Assert.IsNotNull(llm.LastTools);
            Assert.AreEqual(1, llm.LastTools.Count);
            Assert.AreEqual("seq_think", llm.LastTools[0].Name);
            Assert.AreEqual("回复", step.Vertex.OutputText);
        }

        [TestMethod]
        public async Task ExpandAsync_ToolProviderEmpty_FallsBackToPureChat()
        {
            var llm = new FakeLlmClient();
            var op = new SkConversationFoldOperator(llm, toolProvider: new FakeToolProvider { Tools = Array.Empty<LlmTool>() });
            var signal = new FoldSignal<ConversationFoldVertex>(FoldSignalKind.ExternalInput, "start", "请分析");

            var step = await op.ExpandAsync(
                new ConversationFoldState("s1", "分析", null), signal, "collect", Ctx(), default);

            Assert.IsFalse(llm.UsedTools);
            Assert.AreEqual("回复", step.Vertex.OutputText);
        }

        [TestMethod]
        public async Task ExpandAsync_SkillDeclaresExecutor_UsesMcpCall()
        {
            // 调用逻辑写在 skill：该步 skill 声明 executor=answer → 算子经 MCP 调用命名工具，而非走 SK 聊天。
            var llm = new FakeLlmClient();
            var provider = new FakeToolProvider();
            var op = new SkConversationFoldOperator(
                llm, toolProvider: provider,
                skillSource: new[] { new SkillDefinition { Name = "collect", Executor = "answer", Body = "调用 answer 执行端" } });
            var signal = new FoldSignal<ConversationFoldVertex>(FoldSignalKind.ExternalInput, "start", "请分析");

            var step = await op.ExpandAsync(
                new ConversationFoldState("s1", "分析", null), signal, "collect", Ctx(), default);

            Assert.AreEqual("answer", provider.LastCalledTool);       // 经 MCP 调用 answer 工具
            Assert.IsNotNull(provider.LastArgsJson);
            Assert.IsFalse(llm.UsedTools);                            // 未走 SK 聊天
            Assert.AreEqual("answer", step.Vertex.FuncName);          // 执行端 = skill 声明的 executor
        }

        [TestMethod]
        public async Task ExpandAsync_SkillDeclaresExecutor_InProcessDirectCall_PrecedesMcp()
        {
            // Step 3 进程内直调：skill 声明 executor=answer → 优先进程内直调（零协议），不触 MCP / 不触 SK 聊天。
            var llm = new FakeLlmClient();
            var provider = new FakeToolProvider();
            var local = new FakeInProcessExecutor("answer");
            var op = new SkConversationFoldOperator(
                llm, toolProvider: provider, inProcessExecutors: new[] { local },
                skillSource: new[] { new SkillDefinition { Name = "collect", Executor = "answer", Body = "调用 answer 执行端" } });
            var signal = new FoldSignal<ConversationFoldVertex>(FoldSignalKind.ExternalInput, "start", "请分析");

            var step = await op.ExpandAsync(
                new ConversationFoldState("s1", "分析", null), signal, "collect", Ctx(), default);

            Assert.AreEqual("请分析", local.LastQuestion);            // 进程内直调收到问题（信号载荷）
            Assert.IsNull(provider.LastCalledTool);                   // 未走 MCP
            Assert.IsFalse(llm.UsedTools);                            // 未走 SK 聊天
            Assert.AreEqual("进程内直调产出", step.Vertex.OutputText);
            Assert.AreEqual("answer", step.Vertex.FuncName);
        }

        [TestMethod]
        public async Task ExpandAsync_SkillDeclaresExecutor_NoLocalExecutor_FallsBackToMcp()
        {
            // 进程内执行端未命中（只注入 cline，skill 声明 answer）→ 回退 MCP 调用。
            var llm = new FakeLlmClient();
            var provider = new FakeToolProvider();
            var op = new SkConversationFoldOperator(
                llm, toolProvider: provider, inProcessExecutors: new[] { new FakeInProcessExecutor("cline") },
                skillSource: new[] { new SkillDefinition { Name = "collect", Executor = "answer", Body = "调用 answer 执行端" } });
            var signal = new FoldSignal<ConversationFoldVertex>(FoldSignalKind.ExternalInput, "start", "请分析");

            var step = await op.ExpandAsync(
                new ConversationFoldState("s1", "分析", null), signal, "collect", Ctx(), default);

            Assert.AreEqual("answer", provider.LastCalledTool);       // 回退 MCP 调 answer 工具
            Assert.IsFalse(llm.UsedTools);
        }

        [TestMethod]
        public async Task ExpandAsync_SkillDeclaresExecutor_InProcess_WithoutMcpProvider()
        {
            // 无 MCP 工具提供端口，但有进程内执行端 → 仍进程内直调（Step 3 原逻辑会走 SK 聊天，新逻辑进程内优先）。
            var llm = new FakeLlmClient();
            var local = new FakeInProcessExecutor("answer");
            var op = new SkConversationFoldOperator(
                llm, inProcessExecutors: new[] { local },
                skillSource: new[] { new SkillDefinition { Name = "collect", Executor = "answer", Body = "x" } });
            var signal = new FoldSignal<ConversationFoldVertex>(FoldSignalKind.ExternalInput, "start", "请分析");

            var step = await op.ExpandAsync(
                new ConversationFoldState("s1", "分析", null), signal, "collect", Ctx(), default);

            Assert.AreEqual("进程内直调产出", step.Vertex.OutputText);
            Assert.IsFalse(llm.UsedTools);
        }

        [TestMethod]
        public async Task ExpandAsync_McpBackendSkill_StructuredRequest_InProcess()
        {
            // 三模块分层：MCP 本地确定性模块技能（create_node_reference）+ backend，payload 为结构化请求 → 进程内确定性直调（零绕行）。
            var llm = new FakeLlmClient();
            var det = new FakeDeterministicExecutor("create_node_reference", accepts: "{\"taskKey\":\"t1\"}");
            var op = new SkConversationFoldOperator(
                llm, deterministicExecutors: new[] { det },
                skillSource: new[] { new SkillDefinition
                {
                    Name = "create_node_reference", Module = SkillModule.Mcp, Backend = "TaskGraphService", Body = "确定性写图",
                } });
            var signal = new FoldSignal<ConversationFoldVertex>(FoldSignalKind.ExternalInput, "start", "{\"taskKey\":\"t1\"}");

            var step = await op.ExpandAsync(
                new ConversationFoldState("s1", "建任务", null), signal, "create_node_reference", Ctx(), default);

            Assert.IsTrue(det.Executed);              // 进程内确定性直调
            Assert.AreEqual("{\"taskKey\":\"t1\"}", det.LastPayload);
            Assert.IsFalse(llm.UsedTools);            // 未走 SK 聊天
            Assert.AreEqual("确定性产出", step.Vertex.OutputText);
            Assert.AreEqual("sk", step.Vertex.FuncName);
        }

        [TestMethod]
        public async Task ExpandAsync_McpBackendSkill_FreeTextPayload_FallsBackToChat()
        {
            // payload 非结构化（自然语言）→ 确定性执行端 CanExecute=false → 回退 SK 聊天（LLM 意图解析 + 确定性工具面）。
            var llm = new FakeLlmClient();
            var det = new FakeDeterministicExecutor("create_node_reference", accepts: "{\"taskKey\":\"t1\"}");
            var op = new SkConversationFoldOperator(
                llm, deterministicExecutors: new[] { det },
                skillSource: new[] { new SkillDefinition
                {
                    Name = "create_node_reference", Module = SkillModule.Mcp, Backend = "TaskGraphService", Body = "确定性写图",
                } });
            var signal = new FoldSignal<ConversationFoldVertex>(FoldSignalKind.ExternalInput, "start", "请创建任务");

            var step = await op.ExpandAsync(
                new ConversationFoldState("s1", "建任务", null), signal, "create_node_reference", Ctx(), default);

            Assert.IsFalse(det.Executed);             // 未进程内直调
            Assert.AreEqual("回复", step.Vertex.OutputText); // 走 SK 聊天
        }

        [TestMethod]
        public async Task ExpandAsync_StepSkillBinding_ForcesInjectionOfBoundSkill()
        {
            // 声明式 SkillKey（阶段 3）：stepKey="analyze" 绑定到 Rare 技能（OnDemand，关键词绝不自动匹配）。
            // 命中绑定表 → 显式注入该 skill（SelectWithRare 按名命中含 Rare），不走启发式 → system 含绑定 skill 名。
            var llm = new FakeLlmClient();
            var op = new SkConversationFoldOperator(
                llm,
                skillSource: new[] { new SkillDefinition { Name = "collect_skill", Module = SkillModule.Internal, Body = "收集" } },
                rareSkillSource: new[] { new SkillDefinition { Name = "rare_analyze", Module = SkillModule.Rare, OnDemand = true, Body = "分析专用" } },
                stepSkillBindings: new Dictionary<string, string> { ["analyze"] = "rare_analyze" });
            var signal = new FoldSignal<ConversationFoldVertex>(FoldSignalKind.ExternalInput, "start", "请分析");

            var step = await op.ExpandAsync(
                new ConversationFoldState("s1", "分析", null), signal, "analyze", Ctx(), default);

            // 绑定表命中 → Rare 被显式注入。新语义（Node 稳定前缀排布）：L0 稳定底座在 system 头，skill 在 user 尾
            // （多变内容置尾，不打断稳定前缀缓存）。故 system 不含 skill、user 含绑定 skill 名。
            Assert.IsNotNull(llm.LastSystem);
            Assert.IsFalse(llm.LastSystem!.Contains("rare_analyze"), "skill 不再拼入 system（稳定前缀 L0 置头）");
            Assert.IsNotNull(llm.LastUser);
            StringAssert.Contains(llm.LastUser, "rare_analyze");
            Assert.AreEqual("回复", step.Vertex.OutputText);
        }

        private sealed class FakeDeterministicExecutor : IDeterministicSkillExecutor
        {
            public FakeDeterministicExecutor(string skillName, string? accepts = null)
            {
                SkillName = skillName;
                _accepts = accepts;
            }

            public string SkillName { get; }
            private readonly string? _accepts;
            public string? LastPayload;
            public bool Executed;
            public string Result { get; set; } = "确定性产出";

            public bool CanExecute(string payload)
                => _accepts is null || string.Equals(payload, _accepts, System.StringComparison.Ordinal);

            public Task<string> ExecuteAsync(string payload, string stepKey, string stateKey, CancellationToken ct)
            {
                LastPayload = payload;
                Executed = true;
                return Task.FromResult(Result);
            }
        }

        private sealed class FakeInProcessExecutor : IFoldInProcessExecutor
        {
            public FakeInProcessExecutor(string name) => Name = name;
            public string Name { get; }
            public string? LastQuestion;
            public string Result { get; set; } = "进程内直调产出";

            public Task<string> ExecuteAsync(
                string question, string? system, string stepKey, string stateKey, CancellationToken ct, string? conversationContext = null)
            {
                LastQuestion = question;
                return Task.FromResult(Result);
            }
        }
    }
}
