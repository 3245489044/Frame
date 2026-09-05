using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using Operation.HierarchyMethod.Nodes;
using Share.Conversation;
using Share.Conversation.ContextBinding;

namespace Agent.Session.Tests;

/// <summary>三粒度标准上下文构建器单测（P2：node/task/history 各一用例；只依赖端口，注入内存假实现）。</summary>
[TestClass]
public class StandardContextBuilderTests
{
    [TestMethod]
    public async Task NodeScope_Builds_OutputPortsAsToolMessages()
    {
        var nodes = new FakeNodeStore();
        nodes.Put("agent-loop", "nodeA", "result", "输出A");
        var builder = new StandardContextBuilder(nodes, new FakeHistoryStore(), new FakeRefs());

        var ctx = await builder.BuildNodeAsync("agent-loop", "nodeA");

        Assert.AreEqual(StandardScope.Node, ctx.Scope);
        Assert.AreEqual("nodeA", ctx.ScopeId);
        Assert.AreEqual("agent-loop", ctx.WorkflowId);
        Assert.AreEqual(1, ctx.Messages.Count);
        Assert.AreEqual(ConversationRole.Tool, ctx.Messages[0].Role);
        Assert.AreEqual("nodeA", ctx.Messages[0].Stage);
        Assert.AreEqual("输出A", ctx.Messages[0].Text);
    }

    [TestMethod]
    public async Task TaskScope_Combines_ConversationThenNodeOutputs()
    {
        var history = new FakeHistoryStore();
        var log = new ConversationLog();
        log.AppendUser("请分析");
        log.AppendAssistant("初答");
        history.Put("task1", log);

        var refs = new FakeRefs();
        refs.AddTaskNode("task1", "agent-round", "nodeA");

        var nodes = new FakeNodeStore();
        nodes.Put("agent-round", "nodeA", "result", "节点产出");

        var builder = new StandardContextBuilder(nodes, history, refs);
        var ctx = await builder.BuildTaskAsync("task1");

        Assert.AreEqual(StandardScope.Task, ctx.Scope);
        // 对话（user+assistant）+ 节点产出（nodeA）= 3 条
        Assert.AreEqual(3, ctx.Messages.Count);
        Assert.AreEqual(ConversationRole.User, ctx.Messages[0].Role);
        Assert.AreEqual(ConversationRole.Assistant, ctx.Messages[1].Role);
        Assert.AreEqual(ConversationRole.Tool, ctx.Messages[2].Role);
        Assert.AreEqual("节点产出", ctx.Messages[2].Text);
    }

    [TestMethod]
    public async Task HistoryScope_Builds_UnionOfTaskContexts()
    {
        var history = new FakeHistoryStore();
        history.Put("task1", LogWith("t1-用户", "t1-终答"));
        history.Put("task2", LogWith("t2-用户", "t2-终答"));

        var refs = new FakeRefs();
        refs.AddHistoryTask("h1", "task1");
        refs.AddHistoryTask("h1", "task2");
        refs.AddTaskNode("task1", "agent-round", "nodeA");
        refs.AddTaskNode("task2", "agent-round", "nodeB");

        var nodes = new FakeNodeStore();
        nodes.Put("agent-round", "nodeA", "result", "A产出");
        nodes.Put("agent-round", "nodeB", "result", "B产出");

        var builder = new StandardContextBuilder(nodes, history, refs);
        var ctx = await builder.BuildHistoryAsync("h1");

        Assert.AreEqual(StandardScope.History, ctx.Scope);
        // task1(2) + nodeA(1) + task2(2) + nodeB(1) = 6
        Assert.AreEqual(6, ctx.Messages.Count);
        // 顺序：task 键升序（task1 先）
        Assert.AreEqual("t1-用户", ctx.Messages[0].Text);
        Assert.AreEqual("A产出", ctx.Messages[2].Text);
        Assert.AreEqual("B产出", ctx.Messages[5].Text);
    }

    [TestMethod]
    public async Task PortMiss_Returns_EmptyMessages()
    {
        var builder = new StandardContextBuilder(new FakeNodeStore(), new FakeHistoryStore(), new FakeRefs());
        var ctx = await builder.BuildNodeAsync("agent-loop", "missing");
        Assert.AreEqual(0, ctx.Messages.Count);
        Assert.AreEqual(StandardScope.Node, ctx.Scope);
    }

    [TestMethod]
    public async Task RegionScope_Builds_ActivatedRegionFullBody()
    {
        // 激活区域 task.proj-x 读完整正文（区域内有序）；其他区域节点不混入。
        var nodes = new FakeNodeStore();
        nodes.Put("agent-round", "nodeA", "result", "A产出");
        nodes.PutSignal("agent-round", "nodeA", "task.proj-x", freshness: 1);
        nodes.Put("agent-round", "nodeB", "result", "B产出");
        nodes.PutSignal("agent-round", "nodeB", "task.proj-x", freshness: 2);
        nodes.Put("agent-round", "nodePlan", "result", "plan产出");
        nodes.PutSignal("agent-round", "nodePlan", "plan.proj-x");

        var builder = new StandardContextBuilder(nodes, new FakeHistoryStore(), new FakeRefs());
        var ctx = await builder.BuildRegionAsync("task.proj-x");

        Assert.AreEqual(StandardScope.Region, ctx.Scope);
        Assert.AreEqual("task.proj-x", ctx.ScopeId);
        // 仅该区域 2 节点正文；plan 区域不混入
        Assert.AreEqual(2, ctx.Messages.Count);
        CollectionAssert.AreEquivalent(new[] { "A产出", "B产出" }, ctx.Messages.Select(m => m.Text).ToList());
        // 区域内按 freshness 升序（A 先）
        Assert.AreEqual("A产出", ctx.Messages[0].Text);
    }

    [TestMethod]
    public async Task RegionSignals_Builds_TopK_DigestPerRegion_NoFullBody()
    {
        // 非激活区域仅取 top-k 信号摘要（digest + valueScore），不读正文；多个区域各取。
        var nodes = new FakeNodeStore();
        // 区域 r1：3 节点，valueScore 各异 → topK=2 取高分 2 个
        nodes.PutSignal("agent-round", "r1a", "r1", valueScore: 0.9, digest: "r1a摘要");
        nodes.PutSignal("agent-round", "r1b", "r1", valueScore: 0.5, digest: "r1b摘要");
        nodes.PutSignal("agent-round", "r1c", "r1", valueScore: 0.3, digest: "r1c摘要");
        // 区域 r2：1 节点
        nodes.PutSignal("agent-round", "r2a", "r2", valueScore: 0.7, digest: "r2a摘要");

        var builder = new StandardContextBuilder(nodes, new FakeHistoryStore(), new FakeRefs());
        var ctx = await builder.BuildRegionSignalsAsync(new[] { "r1", "r2" }, topK: 2);

        Assert.AreEqual(StandardScope.Region, ctx.Scope);
        // r1 取 top2（r1a, r1b）+ r2 取 1 = 3 条摘要
        Assert.AreEqual(3, ctx.Messages.Count);
        var r1Texts = ctx.Messages.Where(m => m.Text!.Contains("区域 r1", StringComparison.Ordinal)).Select(m => m.Text).ToList();
        Assert.AreEqual(2, r1Texts.Count);
        Assert.IsTrue(r1Texts[0].Contains("r1a摘要", StringComparison.Ordinal), "高分节点应在前（valueScore 0.9）");
        Assert.IsTrue(r1Texts[0].Contains("0.90", StringComparison.Ordinal), "摘要应含 valueScore");
        Assert.IsFalse(r1Texts.Any(t => t.Contains("r1c摘要", StringComparison.Ordinal)), "topK=2 不应含低分 r1c");
    }

    [TestMethod]
    public void RegionScope_Empty_Regions_ReturnsEmptyMessages()
    {
        var builder = new StandardContextBuilder(new FakeNodeStore(), new FakeHistoryStore(), new FakeRefs());
        var ctx = builder.BuildRegionSignalsAsync(new List<string>(), topK: 3).GetAwaiter().GetResult();
        Assert.AreEqual(0, ctx.Messages.Count);
    }

    [TestMethod]
    public async Task RegionScopedView_Combines_ActiveFullBody_And_InactiveSignalDigest()
    {
        // 激活区域 task1 → 读正文；非激活区域 task2 → 仅信号摘要（不读正文）。
        var history = new FakeHistoryStore();
        history.Put("task1", LogWith("t1用户", "t1终答"));
        history.Put("task2", LogWith("t2用户", "t2终答"));
        var refs = new FakeRefs();
        refs.AddHistoryTask("h1", "task1");
        refs.AddHistoryTask("h1", "task2");

        var nodes = new FakeNodeStore();
        // task1 区域（激活）：节点带正文
        nodes.Put("agent-round", "n1a", "result", "t1节点产出");
        nodes.PutSignal("agent-round", "n1a", "task.task1", freshness: 1);
        // task2 区域（非激活）：节点仅信号摘要，无正文端口
        nodes.PutSignal("agent-round", "n2a", "task.task2", valueScore: 0.8, digest: "t2摘要");

        var builder = new StandardContextBuilder(nodes, history, refs);
        var view = new RegionScopedContextViewBuilder(builder, refs, _ => new[] { "task.task1" });
        var ctx = await view.BuildAsync("h1", "agent-round", CancellationToken.None);

        Assert.AreEqual(StandardScope.Region, ctx.Scope);
        // 激活区域正文 1 条 + 非激活区域摘要 1 条 = 2
        Assert.AreEqual(2, ctx.Messages.Count);
        Assert.AreEqual("t1节点产出", ctx.Messages[0].Text, "激活区域应含完整正文");
        Assert.IsTrue(ctx.Messages[1].Text!.Contains("t2摘要", StringComparison.Ordinal), "非激活区域应为信号摘要（含 digest）");
    }

    [TestMethod]
    public async Task RegionScopedView_NoActiveResolver_ReadsAllRegionBodies()
    {
        // 未注入激活区域解析器 → 全部任务区域均激活（读正文，退化全量语义）。
        var refs = new FakeRefs();
        refs.AddHistoryTask("h1", "task1");
        var nodes = new FakeNodeStore();
        nodes.Put("agent-round", "n1", "result", "产出");
        nodes.PutSignal("agent-round", "n1", "task.task1");

        var builder = new StandardContextBuilder(nodes, new FakeHistoryStore(), refs);
        var view = new RegionScopedContextViewBuilder(builder, refs); // 无解析器
        var ctx = await view.BuildAsync("h1", "agent-round", CancellationToken.None);

        Assert.AreEqual(1, ctx.Messages.Count);
        Assert.AreEqual("产出", ctx.Messages[0].Text);
    }

    [TestMethod]
    public async Task TaskScope_AttachesBindings_HistorySeqAndNodeAnchor()
    {
        // P1：标准上下文每条消息附加 MessageBinding（来源域/投喂目标/锚点键/文本哈希）——UI 与 LLM 同源的绑定契约。
        var history = new FakeHistoryStore();
        var log = new ConversationLog();
        log.AppendUser("请分析");
        log.AppendAssistant("初答");
        history.Put("task1", log);

        var refs = new FakeRefs();
        refs.AddTaskNode("task1", "agent-round", "nodeA");

        var nodes = new FakeNodeStore();
        nodes.Put("agent-round", "nodeA", "result", "节点产出");

        var builder = new StandardContextBuilder(nodes, history, refs);
        var ctx = await builder.BuildTaskAsync("task1");

        Assert.AreEqual(3, ctx.Messages.Count);
        // 历史消息 → history.{seq}（LlmHistory / History，哈希可复算）
        Assert.AreEqual(AnchorKey.History(1), ctx.Messages[0].Binding!.Key);
        Assert.AreEqual(AnchorKey.History(2), ctx.Messages[1].Binding!.Key);
        Assert.AreEqual(MessageOrigin.LlmHistory, ctx.Messages[0].Binding!.Origin);
        Assert.AreEqual(FeedTarget.History, ctx.Messages[0].Binding!.FeedTarget);
        Assert.AreEqual(PromptSnapshotBuilder.Hash("请分析"), ctx.Messages[0].Binding!.TextHash);
        // 节点产出 → 节点锚点（wf.{wf}.{node}，SplitWfKey 可解析）
        Assert.AreEqual(AnchorKey.Node("nodeA"), ctx.Messages[2].Binding!.Key);
        Assert.AreEqual(PromptSnapshotBuilder.Hash("节点产出"), ctx.Messages[2].Binding!.TextHash);
    }

    private static ConversationLog LogWith(string user, string assistant)
    {
        var log = new ConversationLog();
        log.AppendUser(user);
        log.AppendAssistant(assistant);
        return log;
    }
}
