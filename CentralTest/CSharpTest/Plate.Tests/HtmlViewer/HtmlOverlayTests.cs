using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UiBackend.Html;

namespace UiBackend.Tests;

/// <summary>通用浮层构建器 <see cref="HtmlOverlay"/> 测试：验证类名/关闭语义/附加 data 均经 <see cref="HtmlOverlayOptions"/> 参数化（不硬编码领域命名）。</summary>
[TestClass]
public class HtmlOverlayTests
{
    [TestMethod]
    public void Build_Default_UsesNeutralClassAndNoCloseButton()
    {
        var html = HtmlOverlay.Build("<div>b</div>", "标题");
        Assert.IsTrue(html.Contains("class=\"floating-window\""));
        Assert.IsTrue(html.Contains("data-name=\"floatingWindow\""));
        Assert.IsTrue(html.Contains("标题"));
        Assert.IsTrue(html.Contains("<div>b</div>"));
        Assert.IsFalse(html.Contains("data-cmd=")); // 未配置 CloseCmd → 无关闭按钮
    }

    [TestMethod]
    public void Build_WithOptions_InjectsCloseCmdAndDataAttributes()
    {
        var html = HtmlOverlay.Build("<div>b</div>", "任务对话 · t1",
            new HtmlOverlayOptions
            {
                CloseCmd = "closeTask",
                CloseData = new Dictionary<string, string> { ["session"] = "t1" },
                CssClass = "task-window",
                DataName = "taskWindow",
            });
        Assert.IsTrue(html.Contains("class=\"task-window\""));
        Assert.IsTrue(html.Contains("data-name=\"taskWindow\""));
        Assert.IsTrue(html.Contains("data-cmd=\"closeTask\""));
        Assert.IsTrue(html.Contains("data-session=\"t1\""));
    }

    [TestMethod]
    public void Build_EncodesTitle_AndKeepsBodyRaw()
    {
        var html = HtmlOverlay.Build("<pre>已encode</pre>", "<script>alert(1)</script>");
        Assert.IsFalse(html.Contains("<script>alert(1)</script>"));
        Assert.IsTrue(html.Contains("&lt;script&gt;"));
        Assert.IsTrue(html.Contains("<pre>已encode</pre>")); // body 按契约已 encode，原样透传
    }
}
