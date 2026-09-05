using TConfig;
using UiBackend.Html;

namespace UiBackend.Tests;

[TestClass]
public class HtmlControlFactoryTests
{
    private static ControlConfig Config(string type, string name, string text = "")
        => new() { Type = type, Name = name, Text = text };

    [TestMethod]
    public void Label_RendersSpan_WithDataName()
    {
        var html = HtmlControlFactory.RenderElement(Config("label", "title", "Hello"), "position:absolute;left:0px;");

        StringAssert.Contains(html, "<span");
        StringAssert.Contains(html, "data-name=\"title\"");
        StringAssert.Contains(html, ">Hello</span>");
    }

    [TestMethod]
    public void Button_WithClickEvent_RendersDataCmd()
    {
        var config = Config("button", "send", "发送");
        config.Events = new EventConfig();
        config.Events.EventsDictionary["click"] = new List<string> { "AgentUi_Send" };

        var html = HtmlControlFactory.RenderElement(config, "");

        // P5 单协议：事件声明统一为 data-cmd="event:{eventName}"（无 data-event 属性）
        StringAssert.Contains(html, "data-cmd=\"event:click\"");
        StringAssert.Contains(html, ">发送</button>");
    }

    [TestMethod]
    public void Button_WithoutEvent_NoDataCmd()
    {
        var html = HtmlControlFactory.RenderElement(Config("button", "plain"), "");

        Assert.IsFalse(html.Contains("data-cmd"));
    }

    [TestMethod]
    public void Textbox_Multiline_RendersTextarea()
    {
        var config = Config("textbox", "input");
        config.Multiline = true;

        var html = HtmlControlFactory.RenderElement(config, "");

        StringAssert.Contains(html, "<textarea");
        Assert.IsFalse(html.Contains("<input"));
    }

    [TestMethod]
    public void Textbox_SingleLine_RendersInput()
    {
        var html = HtmlControlFactory.RenderElement(Config("textbox", "input"), "");

        StringAssert.Contains(html, "<input");
    }

    [TestMethod]
    public void Combobox_RendersOptions_WithSelected()
    {
        var config = Config("combobox", "db");
        config.Items = new List<string> { "a", "b", "c" };
        config.SelectedIndex = 1;

        var html = HtmlControlFactory.RenderElement(config, "");

        StringAssert.Contains(html, "<select");
        StringAssert.Contains(html, ">b</option>");
        Assert.IsTrue(html.Contains("selected"));
    }

    [TestMethod]
    public void HtmlViewer_RendersHostDiv()
    {
        var html = HtmlControlFactory.RenderElement(Config("htmlviewer", "viewer"), "");

        StringAssert.Contains(html, "class=\"host\"");
        StringAssert.Contains(html, "data-name=\"viewer\"");
    }

    [TestMethod]
    public void Panel_RendersContainer_WithChildren()
    {
        var panel = Config("panel", "group");
        panel.Controls = new List<ControlConfig> { Config("label", "child") };

        var html = HtmlControlFactory.RenderElement(panel, "", "<span data-name=\"child\"></span>");

        StringAssert.Contains(html, "class=\"container\"");
        StringAssert.Contains(html, "data-name=\"child\"");
    }

    [TestMethod]
    public void UnknownType_FallsBackToDiv()
    {
        var html = HtmlControlFactory.RenderElement(Config("weirdcontrol", "x"), "");

        StringAssert.Contains(html, "<div");
    }

    [TestMethod]
    public void Text_IsHtmlEncoded()
    {
        var html = HtmlControlFactory.RenderElement(Config("label", "l", "<script>alert(1)</script>"), "");

        StringAssert.Contains(html, "&lt;script&gt;");
        Assert.IsFalse(html.Contains("<script>alert"));
    }

    [TestMethod]
    public void Name_IsHtmlEncoded()
    {
        var html = HtmlControlFactory.RenderElement(Config("label", "a\"b"), "");

        StringAssert.Contains(html, "data-name=\"a&quot;b\"");
    }

    [TestMethod]
    public void HtmlViewer_WithBackendWeb_RendersDataBackend()
    {
        var config = Config("htmlviewer", "viewer");
        config.Backend = "web";

        var html = HtmlControlFactory.RenderElement(config, "");

        StringAssert.Contains(html, "data-backend=\"web\"");
    }

    [TestMethod]
    public void HtmlViewer_WithoutBackend_NoDataBackend()
    {
        var html = HtmlControlFactory.RenderElement(Config("htmlviewer", "viewer"), "");

        Assert.IsFalse(html.Contains("data-backend"));
    }

    [TestMethod]
    public void Backend_DeserializesFromJsonDeclaration()
    {
        var cfg = HtmlDeclarationParser.Parse(
            "{\"views\":[{\"name\":\"v\",\"controls\":[{\"type\":\"htmlviewer\",\"name\":\"viewer\",\"backend\":\"web\"}]}]}");
        var index = HtmlDeclarationParser.BuildControlIndex(cfg!);

        Assert.AreEqual("web", index["viewer"].Backend);
    }
}
