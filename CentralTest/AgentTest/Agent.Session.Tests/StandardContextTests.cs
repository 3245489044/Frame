using Agent.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Conversation;

namespace Agent.Session.Tests;

/// <summary>StandardContext / StandardScope 纯数据模型单测（P1：纯数据、零 LLM）。</summary>
[TestClass]
public class StandardContextTests
{
    [TestMethod]
    public void Model_Captures_ScopeAndScopeId()
    {
        var ctx = new StandardContext(StandardScope.Node, "nodeA", "agent-loop", Array.Empty<ConversationMessage>());
        Assert.AreEqual(StandardScope.Node, ctx.Scope);
        Assert.AreEqual("nodeA", ctx.ScopeId);
        Assert.AreEqual("agent-loop", ctx.WorkflowId);
        Assert.AreEqual(0, ctx.Messages.Count);
    }

    [TestMethod]
    public void Empty_Factory_Returns_ZeroMessages_WithGivenScope()
    {
        var ctx = StandardContext.Empty(StandardScope.History, "h1");
        Assert.AreEqual(StandardScope.History, ctx.Scope);
        Assert.AreEqual("h1", ctx.ScopeId);
        Assert.IsNull(ctx.WorkflowId);
        Assert.AreEqual(0, ctx.Messages.Count);
    }

    [TestMethod]
    public void Messages_Reuse_ConversationMessage_Type()
    {
        var msg = new ConversationMessage(ConversationRole.Tool, "nodeA", "nodeA", "输出", 1, DateTimeOffset.UtcNow);
        var ctx = new StandardContext(StandardScope.Node, "nodeA", null, new[] { msg });
        Assert.AreSame(msg, ctx.Messages[0]);
        Assert.AreEqual("输出", ctx.Messages[0].Text);
    }

    [TestMethod]
    public void Scope_Enum_FourGranularities()
    {
        Assert.AreEqual(4, Enum.GetValues<StandardScope>().Length);
        Assert.IsTrue(Enum.IsDefined(StandardScope.Node));
        Assert.IsTrue(Enum.IsDefined(StandardScope.Task));
        Assert.IsTrue(Enum.IsDefined(StandardScope.History));
        Assert.IsTrue(Enum.IsDefined(StandardScope.Region));
    }
}
