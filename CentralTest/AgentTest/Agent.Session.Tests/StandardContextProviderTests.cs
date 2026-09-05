using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.HierarchyMethod.Nodes;

namespace Agent.Session.Tests;

/// <summary>角色→粒度提供器单测（P3 重构：history 给主循环 / task 给子代理）。</summary>
[TestClass]
public class StandardContextProviderTests
{
    private static StandardContextProvider BuildProvider()
    {
        var nodes = new FakeNodeStore();
        nodes.Put("agent-loop", "nodeA", "result", "A产出");
        var history = new FakeHistoryStore();
        history.Put("task1", LogWith("t1-用户", "t1-终答"));
        var refs = new FakeRefs();
        refs.AddHistoryTask("h1", "task1");
        refs.AddTaskNode("task1", "agent-round", "nodeA");
        nodes.Put("agent-round", "nodeA", "result", "A产出");
        var builder = new StandardContextBuilder(nodes, history, refs);
        return new StandardContextProvider(builder);
    }

    private static Share.Conversation.ConversationLog LogWith(string user, string assistant)
    {
        var log = new Share.Conversation.ConversationLog();
        log.AppendUser(user);
        log.AppendAssistant(assistant);
        return log;
    }

    [TestMethod]
    public void DefaultScopeForRole_HistoryForMainLoop_TaskForSubAgent()
    {
        var p = BuildProvider();
        Assert.AreEqual(StandardScope.History, p.DefaultScopeForRole(StandardContextRole.MainLoop), "主循环看 history");
        Assert.AreEqual(StandardScope.Task, p.DefaultScopeForRole(StandardContextRole.SubAgent), "子代理看 task");
    }

    [TestMethod]
    public async Task BuildForRoleAsync_MainLoop_ProjectsHistory()
    {
        var p = BuildProvider();
        var ctx = await p.BuildForRoleAsync(StandardContextRole.MainLoop, "h1");
        Assert.AreEqual(StandardScope.History, ctx.Scope);
        Assert.IsTrue(ctx.Messages.Count > 0, "history 投影含 task 对话");
    }

    [TestMethod]
    public async Task BuildForRoleAsync_SubAgent_ProjectsTask()
    {
        var p = BuildProvider();
        var ctx = await p.BuildForRoleAsync(StandardContextRole.SubAgent, "task1");
        Assert.AreEqual(StandardScope.Task, ctx.Scope);
        Assert.AreEqual("task1", ctx.ScopeId);
    }

    [TestMethod]
    public async Task BuildForScopeAsync_ExplicitScope_WinsOverRole()
    {
        var p = BuildProvider();
        // 显式 node 粒度（即使 role=subagent 也投影 node）
        var ctx = await p.BuildForScopeAsync(StandardScope.Node, "nodeA", "agent-round");
        Assert.AreEqual(StandardScope.Node, ctx.Scope);
        Assert.AreEqual("nodeA", ctx.ScopeId);
    }
}
