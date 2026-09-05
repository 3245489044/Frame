using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Conversation.Adapters;

namespace Share.Conversation.Tests;

[TestClass]
public class ConversationNodeAdapterTests
{
    // ---- 节点 → 规范对话结构（ConversationNodeMapper）----

    [TestMethod]
    public void MapToConversation_Builds_User_ToolStages_Assistant()
    {
        var mapper = new ConversationNodeMapper();
        var contributions = new List<ConversationNodeContribution>
        {
            new("cline_execute", "cline", "已读取 source.data"),
            new("answer", "answer", "结论：{title,body}"),
        };

        var msgs = mapper.MapToConversation(contributions, userInput: "请分析", assistantAnswer: "完成", startRound: 1);

        Assert.AreEqual(4, msgs.Count);
        Assert.AreEqual(ConversationRole.User, msgs[0].Role);
        Assert.AreEqual("请分析", msgs[0].Text);
        Assert.AreEqual(ConversationRole.Tool, msgs[1].Role);
        Assert.AreEqual("cline_execute", msgs[1].Stage);
        Assert.AreEqual("cline", msgs[1].SubAgent);
        Assert.AreEqual("已读取 source.data", msgs[1].Text);
        Assert.AreEqual(ConversationRole.Tool, msgs[2].Role);
        Assert.AreEqual("answer", msgs[2].Stage);
        Assert.AreEqual(ConversationRole.Assistant, msgs[3].Role);
        Assert.AreEqual("完成", msgs[3].Text);
        // Round 对齐起始轮
        Assert.IsTrue(msgs.All(m => m.Round == 1));
    }

    [TestMethod]
    public void MapToConversation_Omits_EmptyUserAssistant()
    {
        var mapper = new ConversationNodeMapper();
        var msgs = mapper.MapToConversation(
            new List<ConversationNodeContribution> { new("answer", "answer", "仅产出") });

        Assert.AreEqual(1, msgs.Count, "无 user/assistant → 仅节点产出");
        Assert.AreEqual(ConversationRole.Tool, msgs[0].Role);
    }

    [TestMethod]
    public void MapToConversation_ExplicitContributionRound_Wins()
    {
        var mapper = new ConversationNodeMapper();
        var msgs = mapper.MapToConversation(
            new List<ConversationNodeContribution> { new("act", "cline", "产出", Round: 3) },
            userInput: "问题",
            startRound: 2);

        Assert.AreEqual(2, msgs[0].Round, "user 用起始轮");
        Assert.AreEqual(3, msgs[1].Round, "显式 Round 优先");
    }

    // ---- 反向：history → nodes（ToContributions）----

    [TestMethod]
    public void ToContributions_ExtractsToolMessages()
    {
        var mapper = new ConversationNodeMapper();
        var log = new ConversationLog();
        log.AppendUser("请分析");
        log.AppendSubAgent("cline_execute", "cline", "已读取");
        log.AppendSubAgent("answer", "answer", "结论");
        log.AppendAssistant("完成");

        var contributions = mapper.ToContributions(log.Messages);

        Assert.AreEqual(2, contributions.Count, "仅 Tool 消息 → 节点贡献");
        Assert.AreEqual("cline_execute", contributions[0].Stage);
        Assert.AreEqual("cline", contributions[0].SubAgent);
        Assert.AreEqual("已读取", contributions[0].Text);
        Assert.AreEqual("answer", contributions[1].Stage);
    }

    [TestMethod]
    public void ToContributions_Null_ReturnsEmpty()
    {
        var mapper = new ConversationNodeMapper();
        Assert.AreEqual(0, mapper.ToContributions(null).Count);
        Assert.AreEqual(0, new ConversationLog().ToContributions().Count, "空会话无贡献");
    }

    [TestMethod]
    public void NodesHistory_RoundTrip_IsStable()
    {
        var mapper = new ConversationNodeMapper();
        var contributions = new List<ConversationNodeContribution>
        {
            new("cline_execute", "cline", "已读取", Round: 1),
            new("answer", "answer", "结论", Round: 1),
        };

        // nodes → history
        var msgs = mapper.MapToConversation(contributions, userInput: "问题", assistantAnswer: "完成");
        // history → nodes
        var back = mapper.ToContributions(msgs);

        Assert.AreEqual(contributions.Count, back.Count, "双向同步：Tool 消息数一致");
        Assert.AreEqual(contributions[0].Stage, back[0].Stage);
        Assert.AreEqual(contributions[0].Text, back[0].Text);
        Assert.AreEqual(contributions[0].Round, back[0].Round, "Round 往返保留");
    }
}
