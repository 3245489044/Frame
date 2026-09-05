using System.Collections.Generic;
using TConfig;
using UiBackend.Html;

namespace UiBackend.Tests;

[TestClass]
public class HtmlControlRendererTests
{
    private static ControlConfig Config(string type, string name, LayoutPosition layout, List<ControlConfig>? children = null)
        => new() { Type = type, Name = name, Layout = layout, Controls = children };

    [TestMethod]
    public void Render_SingleControl_AbsolutePosition()
    {
        var renderer = new HtmlControlRenderer();
        var html = renderer.Render(
            Config("button", "run", new LayoutPosition { Left = 0.1, Top = 0.1, Width = 0.2, Height = 0.1 }),
            new LayoutRect(0, 0, 1000, 800));

        StringAssert.Contains(html, "data-name=\"run\"");
        StringAssert.Contains(html, "left:100px;top:80px;width:200px;height:80px;");
    }

    [TestMethod]
    public void Render_Children_RelativeToParentRect()
    {
        var renderer = new HtmlControlRenderer();
        var inner = Config("label", "inner", new LayoutPosition { Left = 0.5, Top = 0.5, Width = 0.5, Height = 0.5 });
        var outer = Config("panel", "outer", new LayoutPosition { Left = 0, Top = 0, Width = 0.5, Height = 0.5 }, new List<ControlConfig> { inner });

        var html = renderer.Render(outer, new LayoutRect(0, 0, 1000, 800));

        StringAssert.Contains(html, "left:250px;top:200px;width:250px;height:200px;");
    }

    [TestMethod]
    public void Render_ButtonWithClickEvent_EmitsDataEvent()
    {
        var renderer = new HtmlControlRenderer();
        var btn = new ControlConfig
        {
            Type = "button",
            Name = "send",
            Layout = new LayoutPosition { Left = 0, Top = 0, Width = 1, Height = 1 },
            Events = new EventConfig
            {
                EventsDictionary = new Dictionary<string, List<string>>
                {
                    ["click"] = new() { "AgentUi_Send" },
                },
            },
        };

        var html = renderer.Render(btn, new LayoutRect(0, 0, 100, 50));

        StringAssert.Contains(html, "data-cmd=\"event:click\"");
        StringAssert.Contains(html, "data-name=\"send\"");
    }
}
