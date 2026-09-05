using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using Operation.HierarchyMethod.Nodes;
using Share.Conversation;
using Agent.Core.Tests.TestSupport;

namespace Agent.Core.Tests;

/// <summary>
/// 标准对话格式回归护栏（SingleImplementation / TH-12）：
/// 主对话框（ConversationViewService）默认经 IStandardContextProvider 以 Agent.Session 标准对话格式渲染任务/历史，
/// 本测试锁定该格式契约 = 「任务累积对话（ur_histories）+ 其节点产出（ur_nodes Tool 消息）」的统一消息序列，
/// 使右侧历史气泡与 cline_execute 结构化投喂的上下文同源。
/// </summary>
[TestClass]
public class StandardContextBuilderTests
{
    [TestMethod]
    public async Task BuildTaskAsync_ReturnsDialoguePlusNodeOutputs_StandardFormat()
    {
        // 任务对话（ur_histories）
        var log = new ConversationLog();
        log.AppendUser("分析数据");
        log.AppendAssistant("结论");
        var history = new FakeHistoryStore(log);

        // 任务关联的节点键（cross_db_link 边派生，workflowId=agent-round）
        var hierarchy = new FakeHierarchy(new[] { "source", "result" });

        // 节点输出（ur_nodes NodeType=output）
        var nodes = new FakeNodeOutputStore();
        nodes.Add("agent-round", "source", "data", "原始数据内容");
        nodes.Add("agent-round", "result", "data", "结构化结论");

        var builder = new StandardContextBuilder(nodes, history, hierarchy);
        var ctx = await builder.BuildTaskAsync("t1", ct: CancellationToken.None).ConfigureAwait(false);

        // 粒度与作用域
        Assert.AreEqual(StandardScope.Task, ctx.Scope);
        Assert.AreEqual("t1", ctx.ScopeId);
        Assert.AreEqual("agent-round", ctx.WorkflowId);

        // 任务对话在标准序列中
        Assert.IsTrue(ctx.Messages.Any(m => m.Role == ConversationRole.User && m.Text == "分析数据"));
        Assert.IsTrue(ctx.Messages.Any(m => m.Role == ConversationRole.Assistant && m.Text == "结论"));

        // 节点产出投影为 Tool 消息（Stage=nodeKey、Round=1）
        var source = ctx.Messages.Single(m => m.Role == ConversationRole.Tool && m.Stage == "source");
        Assert.AreEqual("原始数据内容", source.Text);
        Assert.AreEqual(1, source.Round);
        var result = ctx.Messages.Single(m => m.Role == ConversationRole.Tool && m.Stage == "result");
        Assert.AreEqual("结构化结论", result.Text);
    }

    [TestMethod]
    public async Task BuildTaskAsync_NoNodeEdges_ReturnsOnlyDialogue()
    {
        var log = new ConversationLog();
        log.AppendUser("仅对话");
        var history = new FakeHistoryStore(log);

        // 任务无关联节点（GetTaskNodeKeysAsync 返回空 = 无 cross_db_link 边）
        var hierarchy = new FakeHierarchy(Array.Empty<string>());
        var nodes = new FakeNodeOutputStore(); // 无节点输出

        var builder = new StandardContextBuilder(nodes, history, hierarchy);
        var ctx = await builder.BuildTaskAsync("t2", ct: CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(ctx.Messages.Any(m => m.Role == ConversationRole.User && m.Text == "仅对话"));
        Assert.IsFalse(ctx.Messages.Any(m => m.Role == ConversationRole.Tool));
    }

    [TestMethod]
    public async Task BuildHistoryAsync_UnionsTaskDialogues()
    {
        var log = new ConversationLog();
        log.AppendUser("A 的历史");
        var history = new FakeHistoryStore(log);
        var hierarchy = new FakeHierarchy(Array.Empty<string>(), new[] { "ta", "tb" });
        var nodes = new FakeNodeOutputStore();

        var builder = new StandardContextBuilder(nodes, history, hierarchy);
        var ctx = await builder.BuildHistoryAsync("h1", ct: CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(StandardScope.History, ctx.Scope);
        // 两个任务共享同一历史存储：每次 GetAsync 返回同一对话 → 并集（同内容重复计）
        Assert.AreEqual(2, ctx.Messages.Count(m => m.Role == ConversationRole.User && m.Text == "A 的历史"));
    }


    // ---- 最小端口替身（仅承载本测试所需读取面）----

    private sealed class FakeHistoryStore : IHistoryStore
    {
        private readonly ConversationLog _log;
        public FakeHistoryStore(ConversationLog log) => _log = log;

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertAsync(string sessionId, ConversationLog log, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ConversationLog?> GetAsync(string sessionId, CancellationToken ct = default)
            => Task.FromResult<ConversationLog?>(_log);
        public Task<IReadOnlyList<string>> ListHistoryIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(new List<string>());
        public Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(false);
    }
}
