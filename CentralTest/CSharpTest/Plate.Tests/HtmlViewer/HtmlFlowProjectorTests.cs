using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UiBackend.Html;

namespace UiBackend.Tests;

/// <summary>通用会话页布局壳投影器 <see cref="HtmlFlowProjector"/> 测试（模板经构造注入，等价 AgentRuntimeUI.json 的 flowShell/viewSelector/selectorItem 结构）。</summary>
[TestClass]
public class HtmlFlowProjectorTests
{
    private static IReadOnlyDictionary<string, string> Templates() => new Dictionary<string, string>
    {
        ["flowShell"] = "<div class=\"conv-layout-wrap\"><div class=\"view-switch\">{selector|raw}</div><div class=\"conv-layout\"><div class=\"session-bar\"><div class=\"bar-title\">{title}</div>{tree|raw}</div><div class=\"conv-main\"><div class=\"conv-title\">{subtitle}</div>{history|raw}</div></div></div>",
        ["viewSelector"] = "{#each items}{>selectorItem}{/each}",
        ["selectorItem"] = "<button type=\"button\" class=\"view-switch-item{?active} active{/}\" data-name=\"viewType-{type}\" data-cmd=\"selectType\" data-type=\"{type}\">{label}</button>",
    };

    [TestMethod]
    public void RenderSelector_OutputsProvidedItems_WithActive()
    {
        var items = new (string, string)[] { ("node", "节点"), ("task", "任务"), ("history", "历史") };
        var html = HtmlFlowProjector.RenderSelector(Templates(), items, "history");
        Assert.IsTrue(html.Contains("data-type=\"node\""));
        Assert.IsTrue(html.Contains("data-type=\"task\""));
        Assert.IsTrue(html.Contains("data-type=\"history\""));
        Assert.IsTrue(html.Contains("view-switch-item active"));
        Assert.IsTrue(html.Contains("data-cmd=\"selectType\""));
    }

    [TestMethod]
    public void AppendOverlay_AppendsWhenProvided()
    {
        Assert.AreEqual("A", HtmlFlowProjector.AppendOverlay("A", null));
        Assert.AreEqual("A<overlay/>", HtmlFlowProjector.AppendOverlay("A", "<overlay/>"));
    }

    [TestMethod]
    public void RenderShell_ProjectsFlowShellWithTreeAndHistory()
    {
        var html = HtmlFlowProjector.RenderShell(Templates(), "SEL", "历史 / 任务", "主循环对话", "TREE", "HIST");
        Assert.IsTrue(html.Contains("conv-layout"));
        Assert.IsTrue(html.Contains("历史 / 任务"));
        Assert.IsTrue(html.Contains("主循环对话"));
        Assert.IsTrue(html.Contains("TREE"));
        Assert.IsTrue(html.Contains("HIST"));
    }
}
