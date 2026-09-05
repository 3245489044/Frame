using System;
using Agent.Contract;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Conversation;

namespace Agent.Core.Tests;

/// <summary>P4 标准上下文契约单测：AgentExecutorRequest 新增的 Context/ContextScope/ContextKey 经中立契约（NodeExecutionContract）双向往返不丢损。</summary>
[TestClass]
public class StandardContextContractTests
{
    [TestMethod]
    public void ToNodeRequest_ToAgentRequest_RoundTrips_StandardContext()
    {
        var msgs = new[]
        {
            new ConversationMessage(ConversationRole.User, null, null, "hi", 1, DateTimeOffset.UtcNow),
        };
        var req = new AgentExecutorRequest("sys", new[] { "l1" },
            ConversationContext: "old-context", Context: msgs, ContextScope: "node", ContextKey: "nodeA");

        var node = NodeExecutionContract.ToNodeRequest(req);
        var back = NodeExecutionContract.ToAgentRequest(node);

        Assert.AreEqual("sys", back.System);
        Assert.AreEqual(1, back.UserContents.Count);
        Assert.AreEqual("old-context", back.ConversationContext, "文本 ConversationContext 保留（回退路径）");
        Assert.AreEqual("node", back.ContextScope);
        Assert.AreEqual("nodeA", back.ContextKey);
        Assert.IsNotNull(back.Context);
        Assert.AreEqual(1, back.Context!.Count);
        Assert.AreEqual("hi", back.Context[0].Text);
        Assert.AreEqual(ConversationRole.User, back.Context[0].Role);
    }

    [TestMethod]
    public void EmptyStandardContext_NotCarried_InExtras()
    {
        var req = new AgentExecutorRequest("sys", new[] { "l1" }, ContextScope: "task", ContextKey: "t1");
        var node = NodeExecutionContract.ToNodeRequest(req);
        var back = NodeExecutionContract.ToAgentRequest(node);
        Assert.AreEqual("task", back.ContextScope);
        Assert.AreEqual("t1", back.ContextKey);
        Assert.IsNull(back.Context, "无 Context 消息序列 → 不写入 Extras（回退原 prompt 路径）");
    }
}
