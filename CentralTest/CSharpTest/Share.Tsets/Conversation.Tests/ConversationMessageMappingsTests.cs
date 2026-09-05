using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Conversation;

namespace Share.Conversation.Tests;

[TestClass]
public class ConversationMessageMappingsTests
{
    [TestMethod]
    public void ToDictList_RoundTrip_PreservesRoleAndContent()
    {
        var log = new ConversationLog();
        log.AppendSystem("你是助手");
        log.AppendUser("请分析 source.data");
        log.AppendSubAgent("cline_execute", "cline", "已读取 source.data");
        log.AppendAssistant("完成");

        var dicts = log.Messages.ToDictList();
        Assert.AreEqual(log.Messages.Count, dicts.Count);
        Assert.AreEqual("system", dicts[0]["role"]);
        Assert.AreEqual("你是助手", dicts[0]["content"]);
        Assert.AreEqual("tool", dicts[2]["role"]);

        var restored = ConversationMessageMappings.FromDictList(dicts);
        Assert.AreEqual(log.Messages.Count, restored.Count);
        Assert.AreEqual(ConversationRole.User, restored[1].Role);
        Assert.AreEqual("请分析 source.data", restored[1].Text);
    }

    [TestMethod]
    public void ToDictList_Null_ReturnsEmpty()
    {
        Assert.AreEqual(0, ConversationMessageMappings.ToDictList(null).Count);
    }

    [TestMethod]
    public void FromDictList_Null_Empty_Invalid_ReturnsEmpty()
    {
        Assert.AreEqual(0, ConversationMessageMappings.FromDictList(null).Count);
        Assert.AreEqual(0, ConversationMessageMappings.FromDictList(new object[0]).Count);
        // 缺 role/content 的条目跳过
        Assert.AreEqual(0, ConversationMessageMappings.FromDictList(new object[]
        {
            new System.Collections.Generic.Dictionary<string, object> { ["role"] = "user" },
        }).Count);
    }

    [TestMethod]
    public void RoleName_ParseRole_RoundTrip()
    {
        foreach (var role in new[]
                 {
                     ConversationRole.System, ConversationRole.User, ConversationRole.Assistant,
                     ConversationRole.Tool, ConversationRole.Error,
                 })
        {
            var name = ConversationMessageMappings.RoleName(role);
            Assert.AreEqual(role, ConversationMessageMappings.ParseRole(name), $"角色名往返应一致: {name}");
        }
        Assert.AreEqual(ConversationRole.User, ConversationMessageMappings.ParseRole("unknown"), "未知角色宽容为 User");
        Assert.AreEqual(ConversationRole.Assistant, ConversationMessageMappings.ParseRole("ASSISTANT"), "大小写不敏感");
    }
}
