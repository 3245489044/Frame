using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Conversation;
using Share.Conversation.Rendering;
using UiBackend.Html;

namespace UiBackend.Tests;

/// <summary>
/// 通用消息流渲染器 <see cref="HtmlStreamRenderer"/> 测试。
/// 模板经构造注入（Func 延迟源），等价于 AgentRuntimeUI.json 的 round/message/emptyP 结构（不依赖 Agent 静态槽位）。
/// 断言与既有 HtmlConversationRendererTests 对齐（零回归）。
/// </summary>
[TestClass]
public class HtmlStreamRendererTests
{
    private static List<ConversationRenderItem> Items(params ConversationRenderItem[] xs)
        => new List<ConversationRenderItem>(xs);

    /// <summary>等价 AgentRuntimeUI.json 的 round/message/emptyP 模板集（与 HTMLConversationRendererTests 同一契约）。</summary>
    private static IReadOnlyDictionary<string, string> Templates() => new Dictionary<string, string>
    {
        ["emptyP"] = "<p class=\"empty\">{text}</p>",
        ["round"] = "<section class=\"round\" data-round=\"{round}\"><header class=\"round-header\" data-cmd=\"selectHistory\" title=\"点击定位本轮历史\">{roundTitle}</header>{#each messages}{>message}{/each}</section>",
        ["message"] = "<div class=\"msg{?role=user} user{/}{?role=assistant} agent{/}{?role=tool} agent{/}{?role=error} error{/}{?role=system} system{/}{?tool} tool{/}\"><span class=\"badge\">{round} {role}{?stage}:{stage}{/}</span>{?prompt}<details class=\"tool-layer prompt-layer\"><summary class=\"tool-summary\">{stage}{?subagent} · {subagent}{/}</summary><div class=\"bubble\">{text}</div></details>{:else}{?tool}<details class=\"tool-layer\" open><summary class=\"tool-summary\">{stage}{?subagent} · {subagent}{/}</summary><div class=\"bubble\">{text}</div></details>{:else}<div class=\"bubble\">{text}</div>{/}{/}</div>",
    };

    private static HtmlStreamRenderer Renderer(HtmlStreamRendererOptions? opts = null)
        => new HtmlStreamRenderer(() => Templates(), opts);

    [TestMethod]
    public void DefaultRender_User_RightBlueBubble()
    {
        var html = Renderer().Render(Items(new ConversationRenderItem(ConversationRole.User, null, null, "你好", 1)));
        Assert.IsTrue(html.Contains("msg user"));
        Assert.IsTrue(html.Contains("1 user"));
        Assert.IsTrue(html.Contains("你好"));
    }

    [TestMethod]
    public void DefaultRender_Assistant_LeftGreyBubble()
    {
        var html = Renderer().Render(Items(new ConversationRenderItem(ConversationRole.Assistant, null, null, "回复", 1)));
        Assert.IsTrue(html.Contains("msg agent"));
        Assert.IsTrue(html.Contains("1 assistant"));
    }

    [TestMethod]
    public void DefaultRender_ToolWithStage_BadgeHasStage()
    {
        var html = Renderer().Render(Items(new ConversationRenderItem(ConversationRole.Tool, "cline_execute", "cline", "产出", 1)));
        Assert.IsTrue(html.Contains("msg agent"));
        Assert.IsTrue(html.Contains("1 tool:cline_execute"));
    }

    [TestMethod]
    public void DefaultRender_Empty_ReturnsEmptyState()
    {
        var html = Renderer().Render(Items());
        Assert.IsTrue(html.Contains("empty"));
        Assert.IsTrue(html.Contains("空会话"));
    }

    [TestMethod]
    public void DefaultRender_HtmlEncodes_Text()
    {
        var html = Renderer().Render(Items(new ConversationRenderItem(ConversationRole.User, null, null, "<script>alert(1)</script>", 1)));
        Assert.IsFalse(html.Contains("<script>"));
        Assert.IsTrue(html.Contains("&lt;script&gt;"));
    }

    [TestMethod]
    public void Options_OverrideEmptyText()
    {
        var empty = Renderer(new HtmlStreamRendererOptions { EmptyText = "（暂无）" }).Render(Items());
        Assert.IsTrue(empty.Contains("（暂无）"));
    }

    [TestMethod]
    public void DefaultRender_GroupsByRound_WithRoundHeaders()
    {
        var html = Renderer().Render(Items(
            new ConversationRenderItem(ConversationRole.User, null, null, "第一句", 1),
            new ConversationRenderItem(ConversationRole.Assistant, null, null, "第一答", 1),
            new ConversationRenderItem(ConversationRole.User, null, null, "第二句", 2)));

        Assert.IsTrue(html.Contains("data-round=\"1\""));
        Assert.IsTrue(html.Contains("data-round=\"2\""));
        Assert.IsTrue(html.Contains("第 1 轮"));
        Assert.IsTrue(html.Contains("第 2 轮"));
    }

    [TestMethod]
    public void DefaultRender_ToolLayer_CollapsibleDetails_WithStageSubAgent()
    {
        var html = Renderer().Render(Items(new ConversationRenderItem(ConversationRole.Tool, "analyze", "stateless-answer", "中间产出", 1)));

        Assert.IsTrue(html.Contains("msg agent tool"));
        Assert.IsTrue(html.Contains("class=\"tool-layer\""));
        Assert.IsTrue(html.Contains("<summary class=\"tool-summary\">"));
        Assert.IsTrue(html.Contains("analyze"));
        Assert.IsTrue(html.Contains("stateless-answer"));
        Assert.IsTrue(html.Contains("中间产出"));
    }

    [TestMethod]
    public void DefaultRender_ErrorRole_UsesErrorClass()
    {
        var html = Renderer().Render(Items(new ConversationRenderItem(ConversationRole.Error, null, null, "出错", 1)));
        Assert.IsTrue(html.Contains("msg error"));
    }

    [TestMethod]
    public void DefaultRender_RoundHeader_CarriesSelectHistoryCmd()
    {
        var html = Renderer().Render(Items(new ConversationRenderItem(ConversationRole.User, null, null, "hi", 1)));
        Assert.IsTrue(html.Contains("data-cmd=\"selectHistory\""));
    }

    [TestMethod]
    public void PromptStagePrefix_WhenConfigured_MarksPromptLayer()
    {
        var r = new HtmlStreamRenderer(() => Templates(), new HtmlStreamRendererOptions { PromptStagePrefix = "prompt:" });
        var html = r.Render(Items(new ConversationRenderItem(ConversationRole.Tool, "prompt:skill.x", null, "指南", 1)));
        Assert.IsTrue(html.Contains("tool-layer prompt-layer"));
    }

    [TestMethod]
    public void PromptStagePrefix_NotConfigured_RendersAsPlainTool()
    {
        // 通用实现不硬编码领域前缀：未配置 PromptStagePrefix 时，同 stage 消息按普通 tool 渲染（不启用 prompt-layer 分支）。
        var html = Renderer().Render(Items(new ConversationRenderItem(ConversationRole.Tool, "prompt:skill.x", null, "指南", 1)));
        Assert.IsFalse(html.Contains("tool-layer prompt-layer"));
        Assert.IsTrue(html.Contains("class=\"tool-layer\""));
    }
}
