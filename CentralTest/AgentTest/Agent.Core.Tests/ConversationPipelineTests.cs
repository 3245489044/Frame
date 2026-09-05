using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Agent.Axioms;
using Agent.Contract;
using Agent.Session;
using Agent.Session.Wires;
using Agent.Orchestration.SessionWorkflow;
using Agent.Orchestration.SessionWorkflow.NodeWrite;
using Agent.Orchestration.SignalDriven;
using Agent.Core.Nodes;
using Agent.Core.TaskPool;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using Operation.HierarchyMethod.Nodes;
using Share.Conversation;
using Share.StateManagement;
using Share.TaskStatusPool.Loops;
using Share.TaskStatusPool.TaskPool;
using WorkFlowMethod.LoopFramework.Axioms;
using Agent.Core.Tests.TestSupport;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 会话工作流编排器 <see cref="ConversationPipeline"/> 定向测试（注入假 runner，直测编排，不依赖 LLM/函数注册表）：
    /// ① 发消息 → task 快照含 user+各层 sub-agent+assistant、Round 推进、Status 正确；
    /// ② 同会话第二条追加不覆盖历史；③ 换两步/四步工作流，主循环行为随之变化且编排器零改动。
    /// </summary>
    [TestClass]
    public class ConversationPipelineTests
    {
        /// <summary>两步会话工作流（collect → analyze），ResultKeys 各步唯一。</summary>
        private static RoundSpec TwoStepWorkflow() => new("conversation-2", new RoundStep[]
        {
            new("collect", new[] { "user_input" }, new[] { "collect_result" }),
            new("analyze", new[] { "collect_result" }, new[] { "analyze_result" }),
        });

        /// <summary>四步会话工作流（collect → analyze → act → verify），验证换工作流零改动。</summary>
        private static RoundSpec FourStepWorkflow() => new("conversation-4", new RoundStep[]
        {
            new("collect", new[] { "user_input" }, new[] { "collect_result" }),
            new("analyze", new[] { "collect_result" }, new[] { "analyze_result" }),
            new("act", new[] { "analyze_result" }, new[] { "act_result" }),
            new("verify", new[] { "act_result" }, new[] { "verify_result" }),
        });

        /// <summary>假 runner：按工作流各步产出固定文本（funcName + 轮次），链终点文本 = 最后一步产出。</summary>
        private static Func<AxiomContext, CancellationToken, Task<WorkflowRoundResult>> FakeRunner(RoundSpec spec)
            => (ctx, ct) =>
            {
                var outputs = new Dictionary<string, object>();
                string? lastText = null;
                foreach (var step in spec.Steps)
                {
                    if (step.ResultKeys is { Count: > 0 })
                    {
                        var text = $"{step.FuncName} 产出[{ctx.Round}]";
                        outputs[step.ResultKeys[0]] = text;
                        lastText = text;
                    }
                }
                return Task.FromResult(new WorkflowRoundResult(true, null, lastText, null, null, outputs));
            };

        private static (ConversationPipeline pipeline, AgentTaskPoolAdapter pool, InMemoryTaskPoolPersistence<AgentTaskItem> persistence)
            Build(RoundSpec workflow, Func<AxiomContext, CancellationToken, Task<WorkflowRoundResult>>? runner = null)
        {
            var persistence = new InMemoryTaskPoolPersistence<AgentTaskItem>();
            var pool = new AgentTaskPoolAdapter(persistence, Array.Empty<ITaskExecutor>());
            var pipeline = new ConversationPipeline(pool, workflow, runWorkflow: runner ?? FakeRunner(workflow), history: new InMemoryHistoryStore());
            return (pipeline, pool, persistence);
        }

        [TestMethod]
        public async Task SendAsync_CreatesSession_AppendsUserStagesAssistant()
        {
            var (pipeline, _, persistence) = Build(TwoStepWorkflow());

            var reply = await pipeline.RunSignalAsync(
                "s1", new ConversationSignal(ConversationSignalKind.ExternalInput, "请分析 source.data", null), CancellationToken.None);

            Assert.AreEqual("s1", reply.TaskKey);
            Assert.AreEqual(1, reply.Round, "第一条消息 → Round=1");
            Assert.AreEqual(TaskExecutionStatus.Completed, reply.Status);
            Assert.AreEqual("analyze 产出[0]", reply.AssistantText, "链终点终答 = 最后一步产出");
            Assert.AreEqual(2, reply.Stages.Count, "两步工作流 → 两个分层步骤");
            Assert.AreEqual("collect", reply.Stages[0].StepName);
            Assert.AreEqual("analyze", reply.Stages[1].StepName);

            // 快照含 user + assistant 终答（节点产出由 ur_nodes 承载、不入对话历史——见 ConversationPipeline 写侧收敛）
            var log = await pipeline.GetLogAsync("s1", CancellationToken.None);
            Assert.AreEqual(1, log.NextRound);
            Assert.AreEqual(2, log.Messages.Count, "历史仅含 user + assistant 终答（节点产出不入 ur_histories，避免双投影重复）");
            Assert.AreEqual(ConversationRole.User, log.Messages[0].Role);
            Assert.AreEqual("请分析 source.data", log.Messages[0].Text);
            Assert.AreEqual(ConversationRole.Assistant, log.Messages[1].Role, "链终点终答为 Assistant");
            Assert.AreEqual("analyze 产出[0]", log.Messages[1].Text, "终答文本");
        }

        [TestMethod]
        public async Task SendAsync_SecondMessage_AppendsWithoutOverwritingHistory()
        {
            var (pipeline, _, persistence) = Build(TwoStepWorkflow());

            await pipeline.RunSignalAsync("s1", new ConversationSignal(ConversationSignalKind.ExternalInput, "第一条", null), CancellationToken.None);
            var reply2 = await pipeline.RunSignalAsync("s1", new ConversationSignal(ConversationSignalKind.ExternalInput, "补充：忽略空项", null), CancellationToken.None);

            Assert.AreEqual(2, reply2.Round, "第二条消息 → Round=2");
            var log = await pipeline.GetLogAsync("s1", CancellationToken.None);
            var item = await persistence.GetAsync("s1", CancellationToken.None);
            Assert.AreEqual(2, log.NextRound, "会话累计轮次 = 用户消息数");
            Assert.AreEqual(2, log.Messages.Count(m => m.Role == ConversationRole.User), "两条 user 消息均保留，未覆盖历史");
            Assert.AreEqual("第一条", log.Messages[0].Text);
            // 历史 = [user0, assistant0, user1, assistant1]（节点产出经 ur_nodes 承载，不入对话历史）
            Assert.AreEqual(ConversationRole.Assistant, log.Messages[1].Role, "第一条 assistant 终答");
            Assert.AreEqual("补充：忽略空项", log.Messages[2].Text, "第二条 user 消息在第一条之后");
            Assert.AreEqual(2, item!.Round, "任务 Round 推进到 2");
        }

        [TestMethod]
        public async Task SendAsync_NodesPopulated_ExtractsStageBubblesFromNodeSnapshots()
        {
            // P1 正向：假 runner 返回填充 Nodes 的 result（Node 即 Result —— 节点执行快照 = 结果一等载体）。
            // 会话层从节点快照（OutputPorts）提取分层气泡，而非旧 outputs 字典启发式。
            var node1 = new NodeExecutionRecord(
                "collect", "wf",
                new Dictionary<string, object?> { ["collect_result"] = "collect 文本" },
                new Dictionary<string, (string Node, string Port)>());
            var node2 = new NodeExecutionRecord(
                "analyze", "wf",
                new Dictionary<string, object?> { ["analyze_result"] = new AgentExecutorResult(true, "analyze 文本", null) },
                new Dictionary<string, (string Node, string Port)>());
            var runner = (AxiomContext ctx, CancellationToken ct)
                => Task.FromResult(new WorkflowRoundResult(true, null, "analyze 文本", null, null,
                    new Dictionary<string, object>(), Nodes: new[] { node1, node2 }));

            var (pipeline, _, _) = Build(TwoStepWorkflow(), runner);
            var reply = await pipeline.RunSignalAsync(
                "s-nodes", new ConversationSignal(ConversationSignalKind.ExternalInput, "分析", null), CancellationToken.None);

            Assert.AreEqual(2, reply.Stages.Count, "从节点快照提取两个分层气泡");
            Assert.AreEqual("collect", reply.Stages[0].StepName);
            Assert.AreEqual("collect 文本", reply.Stages[0].Text, "普通字符串端口直接取文本");
            Assert.AreEqual("analyze", reply.Stages[1].StepName);
            Assert.AreEqual("analyze 文本", reply.Stages[1].Text, "AgentExecutorResult 端口取 .Text");
        }

        [TestMethod]
        public async Task ChangingWorkflow_ChangesBehavior_ZeroPipelineChanges()
        {
            // 两步工作流
            var (pipeline2, _, _) = Build(TwoStepWorkflow());
            var reply2 = await pipeline2.RunSignalAsync("s2", new ConversationSignal(ConversationSignalKind.ExternalInput, "分析", null), CancellationToken.None);
            Assert.AreEqual(2, reply2.Stages.Count);
            Assert.AreEqual("analyze 产出[0]", reply2.AssistantText);

            // 四步工作流（同一编排器，仅换 RoundSpec）→ 行为随之变化，零改动
            var (pipeline4, _, _) = Build(FourStepWorkflow());
            var reply4 = await pipeline4.RunSignalAsync("s4", new ConversationSignal(ConversationSignalKind.ExternalInput, "分析", null), CancellationToken.None);
            Assert.AreEqual(4, reply4.Stages.Count, "四步工作流 → 四个分层步骤");
            Assert.AreEqual("verify", reply4.Stages[^1].StepName, "链终点 = 最后一步");
            Assert.AreEqual("verify 产出[0]", reply4.AssistantText);
        }

        [TestMethod]
        public async Task GetHistoryAsync_ReturnsFullLog_And_NonExistentReturnsEmpty()
        {
            var (pipeline, _, _) = Build(TwoStepWorkflow());
            await pipeline.RunSignalAsync("s1", new ConversationSignal(ConversationSignalKind.ExternalInput, "你好", null), CancellationToken.None);

            var history = await pipeline.GetLogAsync("s1", CancellationToken.None);
            Assert.AreEqual(1, history.NextRound);
            Assert.IsTrue(history.Messages.Count >= 2, "全量历史（user+assistant 终答；节点产出经 ur_nodes 承载）");

            var empty = await pipeline.GetLogAsync("not-exist", CancellationToken.None);
            Assert.AreEqual(0, empty.Messages.Count, "不存在会话 → 空日志");
        }

        [TestMethod]
        public async Task NewAsync_ResetsConversation()
        {
            var (pipeline, _, persistence) = Build(TwoStepWorkflow());
            await pipeline.RunSignalAsync("s1", new ConversationSignal(ConversationSignalKind.ExternalInput, "旧内容", null), CancellationToken.None);

            var key = await pipeline.ResetAsync("s1", CancellationToken.None);
            Assert.AreEqual("s1", key);

            var log = await pipeline.GetLogAsync("s1", CancellationToken.None);
            Assert.AreEqual(0, log.Messages.Count, "重置后对话清空");
            var item = await persistence.GetAsync("s1", CancellationToken.None);
            Assert.AreEqual(0, item!.Round, "重置后 Round 归零");
        }

        [TestMethod]
        public async Task RunSignalAsync_NoDecisionTemplate_ErrorNull_IsSuccess()
        {
            // 模拟骨架无决策（Ok=false, Error=null，decision==null）：M1 判定解耦后应判成功，诊断为空，
            // 不再落入「未装配函数或步骤异常」兜底（本方案 D1/D2 修复的回归锁定）。
            Func<AxiomContext, CancellationToken, Task<WorkflowRoundResult>> noDecisionRunner =
                (_, _) => Task.FromResult(new WorkflowRoundResult(
                    Ok: false, Error: null, Text: "跑完", DecisionWord: null, Decision: null,
                    Outputs: new Dictionary<string, object> { ["analyze_result"] = "跑完" }));

            var (pipeline, _, _) = Build(TwoStepWorkflow(), noDecisionRunner);
            var reply = await pipeline.RunSignalAsync(
                "s1", new ConversationSignal(ConversationSignalKind.ExternalInput, "请分析", null), CancellationToken.None);

            Assert.AreEqual(TaskExecutionStatus.Completed, reply.Status);
            Assert.IsNull(reply.Diagnostics, "无 error 即成功 → 诊断为空，不再报「未装配函数或步骤异常」");
        }

        [TestMethod]
        public async Task RunSignalAsync_StepError_AttributedInDiagnostics()
        {
            // 有 error 时诊断明确归因到出错步骤（M4），而非误导性兜底文本。
            Func<AxiomContext, CancellationToken, Task<WorkflowRoundResult>> errorRunner =
                (_, _) => Task.FromResult(new WorkflowRoundResult(
                    Ok: false, Error: "collect 阶段异常", Text: null, DecisionWord: null, Decision: null,
                    Outputs: new Dictionary<string, object>()));

            var (pipeline, _, _) = Build(TwoStepWorkflow(), errorRunner);
            var reply = await pipeline.RunSignalAsync(
                "s1", new ConversationSignal(ConversationSignalKind.ExternalInput, "请分析", null), CancellationToken.None);

            Assert.IsNotNull(reply.Diagnostics, "有 error → 诊断非空");
            StringAssert.Contains(reply.Diagnostics![0], "collect 阶段异常", "诊断应明确归因到出错步骤，而非误导性兜底文本");
        }

        [TestMethod]
        public async Task RunSignalAsync_StepError_ClassifiedAndAttributed()
        {
            // D4 错误归一化：有 error 时诊断应携带强类型 ErrorClass 分类 + ErrorStep 归因，
            // 而非仅有裸字符串（替换旧「步骤异常: {message}」）。
            Func<AxiomContext, CancellationToken, Task<WorkflowRoundResult>> errorRunner =
                (_, _) => Task.FromResult(new WorkflowRoundResult(
                    Ok: false, Error: "collect 调用超时", Text: null, DecisionWord: null, Decision: null,
                    Outputs: new Dictionary<string, object>(),
                    ErrorClass: ErrorClass.Timeout, ErrorStep: "collect"));

            var (pipeline, _, _) = Build(TwoStepWorkflow(), errorRunner);
            var reply = await pipeline.RunSignalAsync(
                "s1", new ConversationSignal(ConversationSignalKind.ExternalInput, "请分析", null), CancellationToken.None);

            Assert.IsNotNull(reply.Diagnostics, "有 error → 诊断非空");
            StringAssert.Contains(reply.Diagnostics![0], "Timeout", "诊断应携带强类型 ErrorClass 分类");
            StringAssert.Contains(reply.Diagnostics![0], "collect", "诊断应归因到出错步骤 ErrorStep");
            StringAssert.Contains(reply.Diagnostics![0], "collect 调用超时", "诊断仍保留原始错误消息");
        }

        [TestMethod]
        public async Task RunSignalAsync_WritesBackendAuditToArchive()
        {
            // M5：后端审计写入会话归档（独立 key __backend_audit，不污染 __conversation 对话历史 / LLM 上下文）。
            var (pipeline, _, _) = Build(TwoStepWorkflow());
            await pipeline.RunSignalAsync(
                "s1", new ConversationSignal(ConversationSignalKind.ExternalInput, "请分析", null), CancellationToken.None);

            var audit = await pipeline.GetBackendAuditAsync("s1", CancellationToken.None);
            StringAssert.Contains(audit ?? "", "统一网关", "后端审计应写入会话归档（未装配网关时归因为「统一网关未装配」）");
        }

        [TestMethod]
        public async Task RunSignalAsync_RunnerCancelled_StatusPersistedCancelled()
        {
            // E1 取消语义收敛：run 抛取消 → 会话状态回写 Cancelled（不再状态悬置）、OCE 不再向上传播。
            Func<AxiomContext, CancellationToken, Task<WorkflowRoundResult>> cancelRunner =
                (_, _) => Task.FromException<WorkflowRoundResult>(new OperationCanceledException());

            var (pipeline, _, persistence) = Build(TwoStepWorkflow(), cancelRunner);
            var thrown = false;
            try
            {
                await pipeline.RunSignalAsync("s1", new ConversationSignal(ConversationSignalKind.ExternalInput, "请分析", null), CancellationToken.None);
            }
            catch (OperationCanceledException) { thrown = true; }
            Assert.IsFalse(thrown, "E1：取消不向上传播（由外层调用链沿取消令牌自然传播）");

            var item = await persistence.GetAsync("s1", CancellationToken.None);
            Assert.IsNotNull(item);
            Assert.AreEqual(TaskExecutionStatus.Cancelled, item.Status, "取消 → 会话状态回写 Cancelled（完成信封，不再悬置）");
            Assert.IsNotNull(await pipeline.GetBackendAuditAsync("s1", CancellationToken.None), "取消路径仍应写入后端审计（E1 可观测）");
        }

        [TestMethod]
        public async Task RunSignalAsync_RunnerFaulted_StatusPersistedFaulted()
        {
            // D1 完成信封：run 抛普通异常 → 会话状态回写 Faulted，异常仍按原语义上抛。
            Func<AxiomContext, CancellationToken, Task<WorkflowRoundResult>> faultRunner =
                (_, _) => Task.FromException<WorkflowRoundResult>(new InvalidOperationException("collect 崩溃"));

            var (pipeline, _, persistence) = Build(TwoStepWorkflow(), faultRunner);
            var thrown = false;
            try
            {
                await pipeline.RunSignalAsync("s1", new ConversationSignal(ConversationSignalKind.ExternalInput, "请分析", null), CancellationToken.None);
            }
            catch (InvalidOperationException) { thrown = true; }
            Assert.IsTrue(thrown, "异常应仍按原语义上抛");

            var item = await persistence.GetAsync("s1", CancellationToken.None);
            Assert.IsNotNull(item);
            Assert.AreEqual(TaskExecutionStatus.Faulted, item.Status, "异常 → 会话状态回写 Faulted（完成信封，不再悬置）");
        }

        [TestMethod]
        public void ExtractProps_AgentExecutorResult_ResultKeysDirected()
        {
            // M3：AgentExecutorResult（answer 步产出）按 ResultKeys 定向写 analyze_result，消除属性名反射错配。
            var step = new RoundStep("answer", new[] { "question" }, new[] { "analyze_result" });
            var node = NodeExecutionService.BuildNode(step);
            var exec = new AgentExecutorResult(Ok: true, Text: "分析结果", Error: null);

            var props = NodeExecutionService.ExtractProps(node, exec);

            Assert.IsTrue(props.ContainsKey("analyze_result"), "AgentExecutorResult 按 ResultKeys 定向写入 analyze_result");
            Assert.AreSame(exec, props["analyze_result"], "ResultKeys 全部写同一强类型值");
        }

        [TestMethod]
        public async Task ExtractStages_UsesAgentExecutorResultText_NotRecordToString()
        {
            // D1 文本提取统一回归：ExtractStages 现经 ExecResultDocument.Text 提取（不再本地 switch）。
            // 对 AgentExecutorResult 必须取其 .Text（纯文本），而非 record 默认 ToString()（字段序列化串）。
            Func<AxiomContext, CancellationToken, Task<WorkflowRoundResult>> execRunner =
                (_, _) => Task.FromResult(new WorkflowRoundResult(
                    Ok: true, Error: null, Text: "分析结果",
                    DecisionWord: null, Decision: null,
                    Outputs: new Dictionary<string, object>
                    {
                        ["collect_result"] = new AgentExecutorResult(Ok: true, Text: "收集文本", Error: null),
                        ["analyze_result"] = "分析结果",
                    }));

            var (pipeline, _, _) = Build(TwoStepWorkflow(), execRunner);
            var reply = await pipeline.RunSignalAsync(
                "s1", new ConversationSignal(ConversationSignalKind.ExternalInput, "请分析", null), CancellationToken.None);

            Assert.AreEqual("收集文本", reply.Stages[0].Text, "AgentExecutorResult 步骤产出应取 .Text，而非 record ToString()");
            Assert.AreEqual("分析结果", reply.Stages[1].Text, "字符串步骤产出应原样提取");
        }


        [TestMethod]
        public async Task RunSignalAsync_Agent_ResolvesSingleStepSpec_AndRunsOnlyThatStep()
        {
            // 单步投喂：agent 经 specResolver 选单一 spec → runner 只跑该步（取代固定三步链 collect-analyze-act）。
            var persistence = new InMemoryTaskPoolPersistence<AgentTaskItem>();
            var pool = new AgentTaskPoolAdapter(persistence, Array.Empty<ITaskExecutor>());
            Func<string, RoundSpec?> resolver = agent => agent switch
            {
                "answer" => new RoundSpec("session-answer", new RoundStep[]
                    { new("answer", new[] { "question" }, new[] { "analyze_result" }) }),
                "cline"  => new RoundSpec("session-cline", new RoundStep[]
                    { new("cline_execute", new[] { "l1" }, new[] { "exec_result" }) }),
                _ => null,
            };
            Func<RoundSpec, Func<AxiomContext, CancellationToken, Task<WorkflowRoundResult>>> factory = spec =>
                (ctx, ct) => Task.FromResult(new WorkflowRoundResult(
                    Ok: true, Error: null, Text: $"{spec.Steps[0].FuncName} 产出",
                    DecisionWord: null, Decision: null,
                    Outputs: new Dictionary<string, object> { [spec.Steps[0].ResultKeys[0]] = $"{spec.Steps[0].FuncName} 产出" }));
            var pipeline = new ConversationPipeline(pool, resolver, factory, history: new InMemoryHistoryStore());

            var reply = await pipeline.RunSignalAsync(
                "s1", new ConversationSignal(ConversationSignalKind.ExternalInput, "请分析", null), CancellationToken.None,
                agent: "answer");

            Assert.AreEqual(1, reply.Stages.Count, "agent=answer 仅单步 answer，不跑三步链");
            Assert.AreEqual("answer", reply.Stages[0].StepName);
            Assert.AreEqual("answer 产出", reply.AssistantText, "单步终答 = 该 agent 步骤产出");
        }

        [TestMethod]
        public async Task RunSignalAsync_UnknownAgent_ReturnsDiagnostic_NoHistoryNoConsume()
        {
            // 未识别 agent → 明确诊断；不追加历史、不推进轮次（不消耗额度）。
            var persistence = new InMemoryTaskPoolPersistence<AgentTaskItem>();
            var pool = new AgentTaskPoolAdapter(persistence, Array.Empty<ITaskExecutor>());
            Func<string, RoundSpec?> resolver = _ => null; // 未识别 agent
            Func<RoundSpec, Func<AxiomContext, CancellationToken, Task<WorkflowRoundResult>>> factory =
                _ => throw new InvalidOperationException("未识别 agent 不应调用 runner");
            var pipeline = new ConversationPipeline(pool, resolver, factory, history: new InMemoryHistoryStore());

            var reply = await pipeline.RunSignalAsync(
                "s1", new ConversationSignal(ConversationSignalKind.ExternalInput, "请分析", null), CancellationToken.None,
                agent: "bogus");

            Assert.IsNotNull(reply.Diagnostics, "未识别 agent → 诊断非空");
            StringAssert.Contains(reply.Diagnostics![0], "未知 agent: bogus", "诊断应明确提示未知 agent");
            Assert.AreEqual(0, reply.Stages.Count, "无工具气泡");
            var log = await pipeline.GetLogAsync("s1", CancellationToken.None);
            Assert.AreEqual(0, log.Messages.Count, "非法 agent 不追加任何历史（不消耗额度）");
        }

        [TestMethod]
        public void ResolveSessionSpec_MapsAgentsToSingleStepSpecs()
        {
            // 选项 A：agent → 单步 spec（声明式文件优先，内置单步兜底）。断言 Name/单步/函数名，对声明与兜底均成立。
            var cline = RoundSpecLoader.ResolveSessionSpec("cline");
            Assert.IsNotNull(cline);
            Assert.AreEqual("session-cline", cline!.Name);
            Assert.AreEqual(1, cline.Steps.Count, "agent=cline 单步");
            Assert.AreEqual("cline_execute", cline.Steps[0].FuncName);

            var answer = RoundSpecLoader.ResolveSessionSpec("answer");
            Assert.IsNotNull(answer);
            Assert.AreEqual("session-answer", answer!.Name);
            Assert.AreEqual(1, answer.Steps.Count, "agent=answer 单步");
            Assert.AreEqual("answer", answer.Steps[0].FuncName);

            Assert.IsNull(RoundSpecLoader.ResolveSessionSpec("bogus"), "未识别 agent → null");
            Assert.IsNull(RoundSpecLoader.ResolveSessionSpec(""), "空 agent → null");
        }


        [TestMethod]
        public void WorkflowRoundResult_IsSuccessful_SingleTruthContract()
        {
            // E2：成败判定单一契约——IsSuccessful 仅以 Error 为唯一权威（Ok 仅供模板决策，不参与会话层判断）。
            var success = new WorkflowRoundResult(Ok: false, Error: null, Text: null, DecisionWord: null, Decision: null,
                Outputs: new Dictionary<string, object>());
            var failed = new WorkflowRoundResult(Ok: true, Error: "collect 异常", Text: null, DecisionWord: null, Decision: null,
                Outputs: new Dictionary<string, object>());

            Assert.IsTrue(success.IsSuccessful, "Error==null → IsSuccessful（不以 Ok 为判据）");
            Assert.IsFalse(failed.IsSuccessful, "Error!=null → 非成功（Ok=true 不掩盖错误，单一 truth 契约）");
        }

        [TestMethod]
        public async Task SendAsync_ReplyMessages_MatchesHistory()
        {
            // Y1/H：Send 顺带返回完整历史快照（reply.Messages），与 GetHistoryAsync（GetLogAsync）结果一致，
            // 供 UI 直接渲染气泡（省二次 store 读）。
            var (pipeline, _, _) = Build(TwoStepWorkflow());
            var reply = await pipeline.RunSignalAsync(
                "s1", new ConversationSignal(ConversationSignalKind.ExternalInput, "请分析", null), CancellationToken.None);
            var history = await pipeline.GetLogAsync("s1", CancellationToken.None);

            Assert.IsNotNull(reply.Messages, "Send 应返回历史快照");
            Assert.AreEqual(history.Messages.Count, reply.Messages!.Count, "Send 返回消息数应与 GetHistoryAsync 一致");
            for (var i = 0; i < history.Messages.Count; i++)
            {
                Assert.AreEqual(history.Messages[i].Text, reply.Messages[i].Text, $"第 {i} 条消息文本应一致");
                Assert.AreEqual(history.Messages[i].Role, reply.Messages[i].Role, $"第 {i} 条消息角色应一致");
            }
        }

        [TestMethod]
        public async Task SendAsync_WithHistoryStore_PersistsToStore_NotTaskBlob()
        {
            // 弃用 __conversation blob：注入 IHistoryStore（SQLite ur_histories 的内存替身）后，历史以 ConversationLog
            // 为真源，task 快照 Data 不再承载对话历史；同会话第二条追加不覆盖历史。
            var persistence = new InMemoryTaskPoolPersistence<AgentTaskItem>();
            var pool = new AgentTaskPoolAdapter(persistence, Array.Empty<ITaskExecutor>());
            var store = new InMemoryHistoryStore();
            var pipeline = new ConversationPipeline(pool, TwoStepWorkflow(),
                runWorkflow: FakeRunner(TwoStepWorkflow()), history: store);

            await pipeline.RunSignalAsync("s1", new ConversationSignal(ConversationSignalKind.ExternalInput, "第一条", null), CancellationToken.None);
            var reply2 = await pipeline.RunSignalAsync("s1", new ConversationSignal(ConversationSignalKind.ExternalInput, "第二条", null), CancellationToken.None);

            Assert.AreEqual(2, reply2.Round, "第二条消息 → Round=2");
            Assert.AreEqual(4, reply2.Messages!.Count, "两次发送 × (user+assistant 终答=2) → 历史完整（节点产出不入历史）");
            Assert.AreEqual(2, reply2.Messages.Count(m => m.Role == ConversationRole.User), "两条 user 均保留，未覆盖历史");

            // 历史以 ConversationLog 为真源：GetLogAsync 从 store 读到完整历史
            var log = await pipeline.GetLogAsync("s1", CancellationToken.None);
            Assert.AreEqual(2, log.NextRound, "ConversationLog 重建的会话累计轮次 = 用户消息数");
            Assert.AreEqual(4, log.Messages.Count, "ConversationLog 重建历史与 Send 返回一致");

            // ConversationLog 确已写入 store（会话 ID 维度 upsert）
            var history = await store.GetAsync("s1", CancellationToken.None);
            Assert.IsNotNull(history);
            Assert.AreEqual(4, history!.Messages.Count, "store 承载全部消息");
            Assert.AreEqual(2, history.NextRound, "store 承载累计轮次");

            // task 快照 Data 不再承载对话历史（__conversation blob 弃用）
            var item = await persistence.GetAsync("s1", CancellationToken.None);
            Assert.IsNotNull(item);
            Assert.IsTrue(string.IsNullOrWhiteSpace(((IStateFacet)item!).ReadArchive("__conversation")),
                "弃用后 task 快照 Data 不再承载对话历史");
        }

        [TestMethod]
        public async Task SendAsync_AutoRendersWire_FromHistory()
        {
            // 双向同步自动产物：注册默认 wire 后，回复自动携带 cline wire（nodes→历史→wire，无需手动转换）。
            // 历史仅承载 user + assistant 终答（节点产出经 ur_nodes 承载、不入 ur_histories——见 ConversationPipeline 写侧收敛），
            // 故 wire 为 2 段（分层气泡不再由历史承载）。
            ConversationAgentWireRegistry.RegisterDefaults(); // 幂等装配
            var persistence = new InMemoryTaskPoolPersistence<AgentTaskItem>();
            var pool = new AgentTaskPoolAdapter(persistence, Array.Empty<ITaskExecutor>());
            var store = new InMemoryHistoryStore();
            var pipeline = new ConversationPipeline(pool, TwoStepWorkflow(),
                runWorkflow: FakeRunner(TwoStepWorkflow()), history: store);

            var reply = await pipeline.RunSignalAsync("s1",
                new ConversationSignal(ConversationSignalKind.ExternalInput, "请分析 source.data", null), CancellationToken.None);

            Assert.IsNotNull(reply.WireJson, "回复自动携带默认智能体 wire（cline）");
            using var doc = JsonDocument.Parse(reply.WireJson!);
            Assert.AreEqual(2, doc.RootElement.GetArrayLength(), "user + assistant（节点产出经 ur_nodes 承载，不入历史）");
            Assert.AreEqual("assistant", doc.RootElement[1].GetProperty("role").GetString());
        }

        [TestMethod]
        public async Task GetNodesAsync_AutoReverse_FromNodeOutputs()
        {
            // 双向同步自动反查（node 产出真源 = ur_nodes）：GetNodesAsync 经注入的 IStandardContextBuilder.BuildTaskAsync
            // 从 ur_nodes 重建分层节点（历史不再承载 Tool 气泡——见 ConversationPipeline 写侧收敛 / 消除节点双写）。
            // ur_nodes 按逻辑节点键（collect/analyze）去重，故两分层节点，无需手动转换。
            var persistence = new InMemoryTaskPoolPersistence<AgentTaskItem>();
            var pool = new AgentTaskPoolAdapter(persistence, Array.Empty<ITaskExecutor>());
            var store = new InMemoryHistoryStore();
            var pipeline = new ConversationPipeline(pool, TwoStepWorkflow(),
                runWorkflow: FakeRunner(TwoStepWorkflow()), history: store);

            // StandardContextBuilder 依赖三源（nodes/history/references）：种子 ur_nodes 节点输出 + 任务→节点归属。
            var nodes = new FakeNodeOutputStore();
            nodes.Add(NodeKeys.DefaultWorkflowId, "collect", "collect_result", "collect 产出");
            nodes.Add(NodeKeys.DefaultWorkflowId, "analyze", "analyze_result", "analyze 产出");
            var hierarchy = new FakeHierarchy(new[] { "collect", "analyze" });
            var builder = new StandardContextBuilder(nodes, store, hierarchy);

            var svc = new ConversationSessionService(pipeline, contextBuilder: builder);

            var stages = await svc.GetNodesAsync("s1", CancellationToken.None);
            Assert.AreEqual(2, stages.Count, "ur_nodes 按逻辑节点去重 → 两分层节点");
            // ur_nodes 经 StandardContextBuilder 按节点键升序合成（analyze < collect）
            Assert.AreEqual("analyze", stages[0].StepName);
            Assert.AreEqual("analyze 产出", stages[0].Text);
            Assert.AreEqual("collect", stages[^1].StepName);
        }

        [TestMethod]
        public async Task RunSignal_SeedsConversationContext_ForClineResume()
        {
            // 双向同步自动投喂：首轮无先前会话 → conversation_context 空；第二轮自动种子先前会话 wire（nodes→历史→wire），
            // 经 RoundStateMapper.SessionSeedKeys 透传给 cline_execute → ClineDaemonEndpoint 前置投喂，无需手动。
            ConversationAgentWireRegistry.RegisterDefaults();
            var captured = new List<string?>();
            Func<AxiomContext, CancellationToken, Task<WorkflowRoundResult>> captureRunner = (ctx, ct) =>
            {
                captured.Add(ctx.Data.TryGetValue(RoundKeys.ConversationContext, out var v) ? v?.ToString() : null);
                return FakeRunner(TwoStepWorkflow())(ctx, ct);
            };
            var persistence = new InMemoryTaskPoolPersistence<AgentTaskItem>();
            var pool = new AgentTaskPoolAdapter(persistence, Array.Empty<ITaskExecutor>());
            var store = new InMemoryHistoryStore();
            var pipeline = new ConversationPipeline(pool, TwoStepWorkflow(), runWorkflow: captureRunner, history: store);

            await pipeline.RunSignalAsync("s1",
                new ConversationSignal(ConversationSignalKind.ExternalInput, "第一条", null), CancellationToken.None);
            await pipeline.RunSignalAsync("s1",
                new ConversationSignal(ConversationSignalKind.ExternalInput, "第二条", null), CancellationToken.None);

            Assert.AreEqual(2, captured.Count);
            Assert.IsTrue(string.IsNullOrWhiteSpace(captured[0]), "首轮无先前会话 → conversation_context 空");
            Assert.IsFalse(string.IsNullOrWhiteSpace(captured[1]), "第二轮自动种子先前会话 wire");
            using var doc = JsonDocument.Parse(captured[1]!);
            Assert.IsTrue(doc.RootElement.GetArrayLength() > 0, "先前会话 wire 为非空数组");
        }

        [TestMethod]
        public async Task RunSignal_WithStandardContext_UsesCanonicalConversationContext_IncludingNodeOutputs()
        {
            // P1：注入 StandardContextBuilder → conversation_context 由 canonical（BuildTaskAsync = ur_histories + ur_nodes output）产出，
            // 与 UI 侧 BuildTaskAsync 同源（修复 G2 / TH-12）；未注入仍回退 RenderWire(log)（既有测试锁定，零回归）。
            ConversationAgentWireRegistry.RegisterDefaults();

            // 预置任务历史（ur_histories）+ 节点产出（ur_nodes NodeType=output）
            var store = new InMemoryHistoryStore();
            var log = new ConversationLog();
            log.AppendUser("先前问题");
            log.AppendAssistant("先前结论");
            await store.UpsertAsync("s1", log, CancellationToken.None).ConfigureAwait(false);

            var hierarchy = new FakeHierarchy(new[] { "nodeA" });
            var nodes = new FakeNodeOutputStore();
            nodes.Add("agent-round", "nodeA", "result", "节点产出文本");

            var captured = new List<string?>();
            Func<AxiomContext, CancellationToken, Task<WorkflowRoundResult>> captureRunner = (ctx, ct) =>
            {
                captured.Add(ctx.Data.TryGetValue(RoundKeys.ConversationContext, out var v) ? v?.ToString() : null);
                return FakeRunner(TwoStepWorkflow())(ctx, ct);
            };

            var persistence = new InMemoryTaskPoolPersistence<AgentTaskItem>();
            var pool = new AgentTaskPoolAdapter(persistence, Array.Empty<ITaskExecutor>());
            var builder = new StandardContextBuilder(nodes, store, hierarchy);
            var pipeline = new ConversationPipeline(pool, TwoStepWorkflow(), runWorkflow: captureRunner, history: store, standardContext: builder);

            await pipeline.RunSignalAsync("s1",
                new ConversationSignal(ConversationSignalKind.ExternalInput, "新问题", null), CancellationToken.None);

            Assert.AreEqual(1, captured.Count);
            Assert.IsFalse(string.IsNullOrWhiteSpace(captured[0]), "注入 StandardContextBuilder → conversation_context 由 canonical 产出");
            var json = captured[0]!;
            using var doc = JsonDocument.Parse(json);
            // STJ 默认把非 ASCII 转义为 \uXXXX，故解析后按 content 断言（修复 G2：LLM history 含 ur_nodes output，与 UI 同源）
            var contents = doc.RootElement.EnumerateArray()
                .Select(e => e.TryGetProperty("content", out var c) ? c.GetString() : null)
                .ToList();
            Assert.IsTrue(contents.Any(c => c is not null && c.Contains("节点产出文本", StringComparison.Ordinal)),
                "canonical 含 ur_nodes output（修复 G2：LLM history 与 UI 同源）");
            Assert.IsTrue(doc.RootElement.GetArrayLength() >= 3, "canonical = 历史(2) + 节点产出(1)");
        }

        /// <summary>会话信号 NodeProduced → 框架折叠信号映射：StepKey=节点键、载荷=摘要、顶点=节点信号文本化。</summary>
        [TestMethod]
        public void ConversationSignalFoldAdapter_NodeProduced_Maps_To_FoldSignal_NodeProduced()
        {
            var nodeSignal = new NodeSignal("nodes.agent-round.conv_note_1", "agent-round",
                Module: "task", Region: "task.proj-x", NodeType: "task_node",
                Freshness: 1712345678, ValueScore: 0.92, OutputDigest: "摘要文本", Sticky: true, Priority: 3);

            var fold = ConversationSignalFoldAdapter.ToFoldSignal(
                new ConversationSignal(ConversationSignalKind.NodeProduced, NodeSignal: nodeSignal));

            Assert.AreEqual(global::Operation.LoopFoldMethod.Signal.FoldSignalKind.NodeProduced, fold.Kind);
            Assert.AreEqual("nodes.agent-round.conv_note_1", fold.StepKey, "NodeProduced StepKey 应为节点键");
            Assert.AreEqual("摘要文本", fold.Payload, "NodeProduced 载荷应为输出摘要");
            Assert.IsNotNull(fold.Vertex);
            Assert.AreEqual("node", fold.Vertex.FuncName, "顶点执行端应为 node");
            Assert.AreEqual("摘要文本", fold.Vertex.OutputText);
        }

        /// <summary>NodeWritePipeline 运行后落库发 NodeProduced 信号：装配 emitter 时每节点产出 NodeSignal（module/region/nodeType 标注）。</summary>
        [TestMethod]
        public void NodeWritePipeline_PersistAfterRun_EmitNodeSignals()
        {
            var emitted = new List<NodeSignal>();
            var store = new InMemoryTaskPoolPersistence<AgentTaskItem>();
            var pool = new AgentTaskPoolAdapter(store, Array.Empty<ITaskExecutor>());
            var pipeline = new NodeWritePipeline(
                pool, null, null, null,
                signalEmitter: (wf, taskKey, signal) => emitted.Add(signal));

            // 运行后持久化：nodes = 本轮节点快照。
            var nodes = new[]
            {
                new NodeExecutionRecord("n1", "agent-round", new Dictionary<string, object?> { ["out"] = "产出A" }, new Dictionary<string, (string, string)>()),
                new NodeExecutionRecord("n1", "agent-round", new Dictionary<string, object?> { ["out"] = "产出A2" }, new Dictionary<string, (string, string)>()), // 重复 NodeKey 去重
                new NodeExecutionRecord("n2", "agent-round", new Dictionary<string, object?> { ["out"] = "产出B" }, new Dictionary<string, (string, string)>()),
            };
            var item = new AgentTaskItem { TaskKey = "s1", HistoryId = "s1" };
            pipeline.PersistAsync(TwoStepWorkflow(), "s1", item, new ConversationLog(), nodes, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(2, emitted.Count, "重复 NodeKey 应去重，仅发射唯一节点");
            var n1 = emitted.Single(s => s.NodeKey == "n1");
            Assert.AreEqual("task", n1.Module);
            Assert.AreEqual("task.s1", n1.Region);
            Assert.AreEqual("task_node", n1.NodeType);
            Assert.IsTrue(n1.Freshness > 0, "freshness 应为落库时间戳");
            Assert.AreEqual("产出A", n1.OutputDigest);
            Assert.AreEqual("s1", n1.WorkflowId == "agent-round" ? "s1" : n1.WorkflowId, "节点 workflow 由记录承载");
        }

        /// <summary>测试用内存历史容器存储（IHistoryStore 桩；真实实现为 SQLite ur_histories，以 ConversationLog 为模型）。</summary>
        private sealed class InMemoryHistoryStore : IHistoryStore
        {
            private readonly ConcurrentDictionary<string, ConversationLog> _map = new(StringComparer.Ordinal);

            public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

            public Task UpsertAsync(string sessionId, ConversationLog log, CancellationToken ct = default)
            {
                ArgumentNullException.ThrowIfNull(log);
                _map[sessionId] = log;
                return Task.CompletedTask;
            }

            public Task<ConversationLog?> GetAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult(_map.TryGetValue(sessionId, out var h) ? h : null);

            public Task<IReadOnlyList<string>> ListHistoryIdsAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<string>>(_map.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList());

            public Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult(_map.TryRemove(sessionId, out _));
        }
    }
}

