using System;
using System.Threading;
using System.Threading.Tasks;
using Agent.Launcher;
using Agent.Modules;
using Agent.Modules.StatelessAnswer;
using Agent.Orchestration.SessionWorkflow;
using Agent.Session;
using Agent.Contract.Llm;
using Agent.Core.Nodes;
using Agent.Core.Signal;
using Agent.Core.TaskPool;
using Agent.State;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Conversation;
using Share.StateManagement;
using Share.TaskStatusPool.Loops;
using Share.TaskStatusPool.TaskPool;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 会话端到端复现（与 GUI 完全一致）—— 真实 collect-analyze-act 模板 + 真实 cline_execute/answer 函数
    /// + 真实 WorkflowRunner + LlmHosting 本地装配。直接回答「本地 LLM 返回非空（resp 有内容）但终答 textLen=0」这一根因：
    /// 若此处 assistant 空，即定位到 WorkflowRunner 执行/seed/终答组装环节；若非空，则 GUI 断点在运行态差异。
    /// </summary>
    [TestClass]
    public class EndToEndConversationLocalTests
    {
        [TestMethod]
        public async Task RunSignalAsync_CollectAnalyzeAct_LocalLlm_ReturnsAssistantText()
        {
            var config = new AgentConfig();
            config.ApplyExternalConfig();
            config.ApplyLlmDefaults();
            LlmHosting.Configure(config);
            LlmMode.LocalOnly = true;
            var serverLocal = LlmClientFactory.Instance.Get("local") as LocalLlmClient;
            var host = LocalLlmHttpModule.Start(config.LocalLlmHttp, serverLocal);
            // cline_execute 薄壳已删除（阶段三：执行面归 ISignalDrivenOperator）；cline 由 ClineFoldExecutor 经折叠算子承担。
            AnswerWorkflowFunction.Register();
            try
            {
                var workflow = RoundSpecLoader.LoadWorkflow("collect-analyze-act.json");
                Assert.IsNotNull(workflow, "真实 collect-analyze-act 模板应加载");
                var persistence = new InMemoryTaskPoolPersistence<AgentTaskItem>();
                var pool = new AgentTaskPoolAdapter(persistence, Array.Empty<ITaskExecutor>());
                // P5·单内核：便捷构造 runWorkflow 必填无兜底（骨架 cline_execute 的 BuildDefaultRunner 兜底已删）。
                // 会话执行端口显式注入真实折叠 runner（LlmGateway.Current 本地路由 LocalLlm），与会话生产路径
                // （ConversationHosting → FoldConversationRunner.Build）同源，复现「本地 LLM 终答非空」。
                var runner = FoldConversationRunner.Build(workflow!, new SkConversationFoldOperator());
                var pipeline = new ConversationPipeline(pool, workflow!, runWorkflow: runner);

                var reply = await pipeline.RunSignalAsync("s1",
                    new ConversationSignal(ConversationSignalKind.ExternalInput,
                        "GUI 测试：统一 LLM 网关本地路由，请用一句话回复", null), CancellationToken.None);

                Assert.AreEqual(TaskExecutionStatus.Completed, reply.Status, $"会话应 Completed，实际={reply.Status}");
                Assert.IsFalse(string.IsNullOrWhiteSpace(reply.AssistantText),
                    $"本地 LLM 返回非空（resp 有内容）时 assistant 终答应非空，实际 AssistantText='{reply.AssistantText}'");
            }
            finally
            {
                LlmMode.LocalOnly = false;
                host.Dispose();
            }
        }
    }
}
