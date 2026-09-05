using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Conversation;

namespace Share.Conversation.Tests;

[TestClass]
public class ConversationLogTests
{
    [TestMethod]
    public void AppendUser_RoundMonotonic_And_NextRoundAdvances()
    {
        var log = new ConversationLog();

        log.AppendUser("第一条");
        log.AppendUser("第二条");

        Assert.AreEqual(2, log.NextRound, "会话累计轮次 = 用户消息数");
        Assert.AreEqual(2, log.Messages.Count);
        Assert.AreEqual(ConversationRole.User, log.Messages[1].Role);
        Assert.AreEqual(1, log.Messages[0].Round);
        Assert.AreEqual(2, log.Messages[1].Round);
        Assert.IsTrue(log.Messages.All(m => m.Round > 0), "user Round 自 1 起单调");
    }

    [TestMethod]
    public void AppendAssistant_Aligns_LastUserRound()
    {
        var log = new ConversationLog();
        log.AppendUser("问题");
        log.AppendAssistant("回答");

        Assert.AreEqual(1, log.Messages[^1].Round, "assistant 与最近 user 对齐");
        Assert.AreEqual(ConversationRole.Assistant, log.Messages[^1].Role);
        Assert.AreEqual(1, log.NextRound, "assistant 不推进累计轮次");
    }

    [TestMethod]
    public void AppendSubAgent_Aligns_LastUserRound_And_CarriesStageSubAgent()
    {
        var log = new ConversationLog();
        log.AppendUser("请分析");
        log.AppendSubAgent("cline_execute", "cline", "已收集 12 条");
        log.AppendSubAgent("answer", "answer", "结论：{title,body}");
        log.AppendAssistant("完成");

        Assert.AreEqual(4, log.Messages.Count);
        // 中层气泡 Round 与 user 对齐，Stage/SubAgent 透传
        Assert.AreEqual(1, log.Messages[1].Round);
        Assert.AreEqual("cline_execute", log.Messages[1].Stage);
        Assert.AreEqual("cline", log.Messages[1].SubAgent);
        Assert.AreEqual(ConversationRole.Tool, log.Messages[1].Role);
        Assert.AreEqual("answer", log.Messages[2].Stage);
        // assistant 仍在同一轮
        Assert.AreEqual(1, log.Messages[^1].Round);
    }

    [TestMethod]
    public void AppendSystem_RoundZero_AndExcludedFromWindowCount()
    {
        var log = new ConversationLog();
        log.AppendSystem("你是助手");
        log.AppendUser("问题");

        Assert.AreEqual(0, log.Messages[0].Round);
        Assert.AreEqual(ConversationRole.System, log.Messages[0].Role);
    }

    [TestMethod]
    public void ToPromptText_WindowTruncates_ToRecentN()
    {
        var log = new ConversationLog();
        for (var i = 1; i <= 5; i++) log.AppendUser($"第{i}条");

        var prompt = log.ToPromptText(window: 3, includeSystem: false);
        Assert.IsFalse(prompt.Contains("第1条"), "窗口截断：最旧消息不入上下文");
        Assert.IsFalse(prompt.Contains("第2条"), "窗口截断：第2条不入上下文");
        Assert.IsTrue(prompt.Contains("第5条"), "最近消息保留");
        Assert.IsTrue(prompt.StartsWith("user:"), "角色前缀渲染");
    }

    [TestMethod]
    public void ToPromptText_IncludeSystem_PrintsSystemFirst()
    {
        var log = new ConversationLog();
        log.AppendSystem("系统底座");
        log.AppendUser("问题");

        var prompt = log.ToPromptText(window: 2, includeSystem: true);
        Assert.IsTrue(prompt.StartsWith("系统底座"), "includeSystem 前置系统消息");
    }

    [TestMethod]
    public void Serialize_Deserialize_RoundTrip_PreservesAllFields()
    {
        var log = new ConversationLog();
        log.AppendSystem("你是助手");
        log.AppendUser("请分析 source.data");
        log.AppendSubAgent("cline_execute", "cline", "已读取 source.data");
        log.AppendSubAgent("answer", "answer", "结论：{title,body}");
        log.AppendAssistant("任务已完成");

        var json = log.Serialize();
        var restored = ConversationLog.Deserialize(json);

        Assert.AreEqual(log.Messages.Count, restored.Messages.Count);
        Assert.AreEqual(log.NextRound, restored.NextRound, "累计轮次往返一致");
        for (var i = 0; i < log.Messages.Count; i++)
        {
            Assert.AreEqual(log.Messages[i].Role, restored.Messages[i].Role);
            Assert.AreEqual(log.Messages[i].Stage, restored.Messages[i].Stage);
            Assert.AreEqual(log.Messages[i].SubAgent, restored.Messages[i].SubAgent);
            Assert.AreEqual(log.Messages[i].Text, restored.Messages[i].Text);
            Assert.AreEqual(log.Messages[i].Round, restored.Messages[i].Round);
        }
    }

    [TestMethod]
    public void Deserialize_OldDataWithoutStageSubAgent_IsBackwardCompatible()
    {
        // 旧数据：仅 Role/Text/Round（无 Stage/SubAgent）——反序列化不得抛异常，字段缺省为 null。
        const string oldJson =
            "[{\"Role\":\"User\",\"Text\":\"旧消息\",\"Round\":1}," +
            "{\"Role\":\"Assistant\",\"Text\":\"旧回答\",\"Round\":1}]";

        var restored = ConversationLog.Deserialize(oldJson);

        Assert.AreEqual(2, restored.Messages.Count);
        Assert.AreEqual(ConversationRole.User, restored.Messages[0].Role);
        Assert.AreEqual("旧消息", restored.Messages[0].Text);
        Assert.AreEqual(1, restored.Messages[0].Round);
        Assert.IsNull(restored.Messages[0].Stage, "旧数据无 Stage → null");
        Assert.IsNull(restored.Messages[0].SubAgent, "旧数据无 SubAgent → null");
        Assert.AreEqual(1, restored.NextRound, "旧数据重建累计轮次");
    }

    [TestMethod]
    public void Deserialize_Null_Empty_Invalid_ReturnsEmptySession()
    {
        Assert.AreEqual(0, ConversationLog.Deserialize(null).Messages.Count);
        Assert.AreEqual(0, ConversationLog.Deserialize("").Messages.Count);
        Assert.AreEqual(0, ConversationLog.Deserialize("   ").Messages.Count);
        Assert.AreEqual(0, ConversationLog.Deserialize("not json {").Messages.Count, "非法 JSON → 空会话，不抛异常");
    }

    [TestMethod]
    public void ToPromptText_SingleRound_WithTool_ShowsStageLabel()
    {
        var log = new ConversationLog();
        log.AppendUser("分析");
        log.AppendSubAgent("answer", "answer", "结论体");

        var prompt = log.ToPromptText(window: 10, includeSystem: false);
        Assert.IsTrue(prompt.Contains("tool(answer): 结论体"), "Tool 消息带 Stage 标签渲染");
    }

    [TestMethod]
    public void Append_SingleEntry_DerivesRoundPerRole()
    {
        var log = new ConversationLog();
        log.Append(ConversationRole.System, null, null, "底座");       // Round 0
        log.Append(ConversationRole.User, null, null, "问题");         // Round 1
        log.Append(ConversationRole.Tool, "cline_execute", "cline", "收集"); // 对齐 Round 1
        log.Append(ConversationRole.Assistant, null, null, "回答");     // 对齐 Round 1

        Assert.AreEqual(0, log.Messages[0].Round);
        Assert.AreEqual(1, log.Messages[1].Round);
        Assert.AreEqual(1, log.Messages[2].Round);
        Assert.AreEqual(1, log.Messages[3].Round);
        Assert.AreEqual(1, log.NextRound, "仅 User 推进累计轮次");
    }

    [TestMethod]
    public void GetRound_GroupByRound_CurrentRound()
    {
        var log = new ConversationLog();
        log.AppendUser("第1问");
        log.AppendAssistant("第1答");
        log.AppendUser("第2问");

        Assert.AreEqual(2, log.GetRound(1).Count, "第 1 轮含 user+assistant");
        Assert.AreEqual(1, log.GetRound(2).Count, "第 2 轮含 user");
        Assert.AreEqual(0, log.GetRound(99).Count);

        var groups = log.GroupByRound();
        Assert.AreEqual(2, groups.Count);
        Assert.AreEqual(1, groups[0].Key);
        Assert.AreEqual(2, groups[1].Key);

        Assert.AreEqual(2, log.CurrentRound, "最近活跃轮次");
    }

    [TestMethod]
    public void NeedsCompaction_Threshold()
    {
        var log = new ConversationLog();
        for (var i = 0; i < 11; i++) log.AppendUser($"第{i}条");
        Assert.IsTrue(log.NeedsCompaction(), "非系统消息 11 条 > 阈值 10");
        Assert.IsFalse(log.NeedsCompaction(threshold: 20), "自定义阈值不触发");
    }

    [TestMethod]
    public void Compress_SummaryPlusRecentKeep()
    {
        var log = new ConversationLog();
        for (var i = 1; i <= 7; i++) log.AppendUser($"第{i}条");

        var compacted = log.Compress(keep: 5);
        Assert.AreEqual(6, compacted.Count, "摘要 + 最近 5 条");
        Assert.AreEqual(ConversationRole.System, compacted[0].Role);
        Assert.IsTrue(compacted[0].Text.Contains("上下文摘要"), "前置摘要");
        Assert.IsFalse(compacted.Any(m => m.Text == "第1条"), "最旧消息不入压缩窗口");
        Assert.IsTrue(compacted.Any(m => m.Text == "第7条"), "最近消息保留");
    }

    [TestMethod]
    public void Window_ReturnsRecentNonSystem()
    {
        var log = new ConversationLog();
        log.AppendSystem("底座");
        for (var i = 1; i <= 4; i++) log.AppendUser($"第{i}条");

        var win = log.Window(2);
        Assert.AreEqual(2, win.Count);
        Assert.IsFalse(win.Any(m => m.Role == ConversationRole.System), "窗口排除系统消息");
        Assert.AreEqual("第4条", win[^1].Text);
    }
}
