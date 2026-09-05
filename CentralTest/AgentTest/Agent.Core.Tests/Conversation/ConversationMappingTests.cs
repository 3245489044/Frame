using Agent.Contract.Llm;
using Agent.Orchestration.SessionWorkflow;
using Agent.Session;
using Agent.Session.Wires;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Conversation;
using System.Collections.Generic;
using System.Text.Json;

namespace Agent.Core.Tests;

[TestClass]
public class ConversationNodeSyncTests
{
    [TestMethod]
    public void ToStages_ExtractsToolBubbles_FromLog()
    {
        var log = new ConversationLog();
        log.AppendUser("请分析");
        log.AppendSubAgent("cline_execute", "cline", "已读取");
        log.AppendSubAgent("answer", "answer", "结论");
        log.AppendAssistant("完成");

        var stages = log.ToStages();

        Assert.AreEqual(2, stages.Count, "仅 Tool 气泡 → 分层节点");
        Assert.AreEqual("cline_execute", stages[0].StepName);
        Assert.AreEqual("cline", stages[0].SubAgent);
        Assert.AreEqual("已读取", stages[0].Text);
        Assert.AreEqual("answer", stages[1].StepName);
    }

    [TestMethod]
    public void ToStages_NullOrEmpty_ReturnsEmpty()
    {
        Assert.AreEqual(0, ((ConversationLog?)null).ToStages().Count);
        Assert.AreEqual(0, new ConversationLog().ToStages().Count, "无 Tool 消息 → 无分层节点");
    }
}

[TestClass]
public class ConversationLlmProjectorTests
{
    [TestMethod]
    public void ToLlmMessages_MapsRoleAndContent()
    {
        var log = new ConversationLog();
        log.AppendUser("问题");
        log.AppendAssistant("回答");

        var llm = log.Messages.ToLlmMessages();
        Assert.AreEqual(2, llm.Count);
        Assert.AreEqual("user", llm[0].Role);
        Assert.AreEqual("问题", llm[0].Content);
        Assert.AreEqual("assistant", llm[1].Role);
        Assert.AreEqual("回答", llm[1].Content);
    }

    [TestMethod]
    public void ToLlmMessages_Null_ReturnsEmpty()
    {
        Assert.AreEqual(0, ConversationLlmProjector.ToLlmMessages(null).Count);
    }

    [TestMethod]
    public void Partition_ExtractsLastSystemUser_AndKeepsOrderedHistory()
    {
        var messages = new List<LlmMessage>
        {
            LlmMessage.System("底座"),
            LlmMessage.User("旧问题"),
            LlmMessage.Assistant("旧回答"),
            LlmMessage.User("新问题"),
        };

        var (system, user, history) = ConversationLlmProjector.Partition(messages);

        Assert.AreEqual("底座", system, "抽取最后一条 system");
        Assert.AreEqual("新问题", user, "抽取最后一条 user");
        Assert.AreEqual(2, history.Count);
        Assert.AreEqual("user", history[0].Role, "history 保留原序（system 槽位被抽出）");
        Assert.AreEqual("旧问题", history[0].Content);
        Assert.AreEqual("assistant", history[1].Role);
    }

    [TestMethod]
    public void Partition_Null_ReturnsEmptySlots()
    {
        var (system, user, history) = ConversationLlmProjector.Partition(null);
        Assert.AreEqual("", system);
        Assert.AreEqual("", user);
        Assert.AreEqual(0, history.Count);
    }
}

[TestClass]
public class ConversationAgentWireTests
{
    [TestMethod]
    public void OpenAiWire_Renders_RoleContent()
    {
        var wire = new OpenAiConversationWire();
        var msgs = BuildSample();

        var json = wire.Render(msgs);
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement;
        Assert.AreEqual(4, arr.GetArrayLength());
        Assert.AreEqual("user", arr[0].GetProperty("role").GetString());
        Assert.AreEqual("tool", arr[1].GetProperty("role").GetString());
        Assert.AreEqual("assistant", arr[3].GetProperty("role").GetString());
        Assert.AreEqual("完成", arr[3].GetProperty("content").GetString());
    }

    [TestMethod]
    public void ClaudeWire_Renders_SystemPlusAlternatingTurns()
    {
        var wire = new ClaudeConversationWire();
        var log = new ConversationLog();
        log.AppendSystem("你是助手");
        log.AppendUser("问题");
        log.AppendSubAgent("answer", "answer", "中间产出");
        log.AppendAssistant("完成");

        var json = wire.Render(log.Messages);
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("你是助手", doc.RootElement.GetProperty("system").GetString());
        var turns = doc.RootElement.GetProperty("messages");
        Assert.AreEqual(2, turns.GetArrayLength(), "user + assistant（tool 并入 assistant）交替");
        Assert.AreEqual("user", turns[0].GetProperty("role").GetString());
        Assert.AreEqual("assistant", turns[1].GetProperty("role").GetString());
        Assert.IsTrue(turns[1].GetProperty("content")[0].GetProperty("text").GetString()!.Contains("中间产出"),
            "tool 产出并入 assistant 轮");
    }

    [TestMethod]
    public void ClineWire_Renders_ToolAsAssistant_WithStageLabel()
    {
        var wire = new ClineConversationWire();
        var log = new ConversationLog();
        log.AppendUser("请分析");
        log.AppendSubAgent("cline_execute", "cline", "已读取");

        var json = wire.Render(log.Messages);
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement;
        Assert.AreEqual(2, arr.GetArrayLength());
        Assert.AreEqual("assistant", arr[1].GetProperty("role").GetString());
        Assert.AreEqual("[cline_execute] 已读取", arr[1].GetProperty("content").GetString());
    }

    [TestMethod]
    public void Registry_RegisterDefaults_ResolveByName()
    {
        ConversationAgentWireRegistry.RegisterDefaults(); // 幂等
        Assert.IsTrue(ConversationAgentWireRegistry.Names.Contains("openai"), "含 openai");
        Assert.IsTrue(ConversationAgentWireRegistry.Names.Contains("claude"), "含 claude");
        Assert.IsTrue(ConversationAgentWireRegistry.Names.Contains("cline"), "含 cline");

        var cline = ConversationAgentWireRegistry.Resolve("cline");
        Assert.IsNotNull(cline);
        Assert.AreEqual("cline", cline!.Name);

        Assert.IsNull(ConversationAgentWireRegistry.Resolve("unknown"), "未注册名 → null");
    }

    [TestMethod]
    public void Registry_Register_CustomWire()
    {
        var custom = new TestWire();
        ConversationAgentWireRegistry.Register(custom);
        Assert.AreSame(custom, ConversationAgentWireRegistry.Resolve("test-wire"));
    }

    private static IReadOnlyList<ConversationMessage> BuildSample()
    {
        var log = new ConversationLog();
        log.AppendUser("请分析 source.data");
        log.AppendSubAgent("cline_execute", "cline", "已读取 source.data");
        log.AppendSubAgent("answer", "answer", "结论：{title,body}");
        log.AppendAssistant("完成");
        return log.Messages;
    }

    private sealed class TestWire : Share.Conversation.Adapters.IConversationAgentWire
    {
        public string Name => "test-wire";
        public string Render(IReadOnlyList<ConversationMessage> messages) => "[]";
    }
}

