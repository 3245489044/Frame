using System.Text.Json;
using Agent.Session.Wires;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Conversation;

namespace Agent.Session.Tests;

/// <summary>标准上下文渲染器单测（P3：StandardContext → cline 历史 JSON [{role, content}]；复用 wire 注册表）。</summary>
[TestClass]
public class StandardContextRendererTests
{
    [TestInitialize]
    public void Init() => ConversationAgentWireRegistry.RegisterDefaults(); // 幂等

    [TestMethod]
    public async Task RenderToCline_Produces_JsonArray_OfRoleContent()
    {
        var ctx = new StandardContext(
            StandardScope.Node, "nodeA", null,
            new[]
            {
                new ConversationMessage(ConversationRole.User, null, null, "请分析", 1, System.DateTimeOffset.UtcNow),
                new ConversationMessage(ConversationRole.Tool, "nodeA", "nodeA", "已读取", 1, System.DateTimeOffset.UtcNow),
            });

        var renderer = new StandardContextRenderer();
        var json = await renderer.RenderToClineAsync(ctx);

        Assert.IsNotNull(json);
        using var doc = JsonDocument.Parse(json!);
        var arr = doc.RootElement;
        Assert.AreEqual(2, arr.GetArrayLength());
        Assert.AreEqual("user", arr[0].GetProperty("role").GetString());
        Assert.AreEqual("assistant", arr[1].GetProperty("role").GetString());
        Assert.AreEqual("[nodeA] 已读取", arr[1].GetProperty("content").GetString(), "content 前置 [stage] 保留分层");
    }

    [TestMethod]
    public async Task RenderToWire_By_RegisteredName_And_NullForUnknown()
    {
        var ctx = StandardContext.Empty(StandardScope.Task, "task1");
        var renderer = new StandardContextRenderer();

        var cline = await renderer.RenderToWireAsync(ctx, "cline");
        Assert.AreEqual("[]", cline, "空上下文渲染为 []");

        var unknown = await renderer.RenderToWireAsync(ctx, "no-such-wire");
        Assert.IsNull(unknown, "未注册 wire → null");
    }
}
