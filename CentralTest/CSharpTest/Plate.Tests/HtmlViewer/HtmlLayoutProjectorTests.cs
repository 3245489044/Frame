using TConfig;
using UiBackend.Html;

namespace UiBackend.Tests;

[TestClass]
public class HtmlLayoutProjectorTests
{
    private static readonly HtmlLayoutProjector Projector = new();

    private static ControlConfig Config(string type, string name, LayoutPosition layout, List<ControlConfig>? children = null)
        => new() { Type = type, Name = name, Layout = layout, Controls = children };

    [TestMethod]
    public void Proportional_RatioTimesContainer()
    {
        var rect = Projector.ResolveRect(
            new LayoutPosition { Left = 0.01, Top = 0.04, Width = 0.22, Height = 0.92 },
            1000, 800);

        Assert.AreEqual((10, 32, 220, 736), (rect.X, rect.Y, rect.Width, rect.Height));
    }

    [TestMethod]
    public void Child_CoordinatesRelativeToParent()
    {
        // 父 panel：0.1,0.1,0.5,0.5 → 1000x800 中 (100,80,500,400)
        var child = Config("button", "send",
            new LayoutPosition { Left = 0.1, Top = 0.1, Width = 0.5, Height = 0.5 });
        var parent = Config("panel", "group",
            new LayoutPosition { Left = 0.1, Top = 0.1, Width = 0.5, Height = 0.5 },
            new List<ControlConfig> { child });

        var result = Projector.Project(new[] { parent }, 1000, 800);

        // 子按钮相对父容器（500x400）换算 → (50,40,250,200)
        StringAssert.Contains(result.BodyHtml, "left:50px;top:40px;width:250px;height:200px;");
    }

    [TestMethod]
    public void BottomRightAnchor_ConvertsToPositiveTopLeft()
    {
        // {bottom:0.05, right:0.05, width:0.2, height:0.2}，Top/Left=-1 哨兵
        var rect = Projector.ResolveRect(
            new LayoutPosition { Left = -1, Top = -1, Width = 0.2, Height = 0.2, Bottom = 0.05, Right = 0.05 },
            1000, 800);

        // top = 1 - 0.05 - 0.2 = 0.75；left = 1 - 0.05 - 0.2 = 0.75
        Assert.AreEqual((750, 600, 200, 160), (rect.X, rect.Y, rect.Width, rect.Height));
    }

    [TestMethod]
    public void FixedIntent_UsePixelDirectly()
    {
        var rect = Projector.ResolveRect(
            new LayoutPosition { Intent = "fixed", X = 10, Y = 20, Width = 100, Height = 50 },
            1000, 800);

        Assert.AreEqual((10, 20, 100, 50), (rect.X, rect.Y, rect.Width, rect.Height));
    }

    [TestMethod]
    public void NullLayout_FillsParent()
    {
        var rect = Projector.ResolveRect(null, 1000, 800);

        Assert.AreEqual((0, 0, 1000, 800), (rect.X, rect.Y, rect.Width, rect.Height));
    }

    [TestMethod]
    public void DockIntent_FillsParent()
    {
        var rect = Projector.ResolveRect(
            new LayoutPosition { Intent = "dock", Dock = "Fill" },
            1000, 800);

        Assert.AreEqual((0, 0, 1000, 800), (rect.X, rect.Y, rect.Width, rect.Height));
    }

    [TestMethod]
    public void Project_RendersRootWithAbsolutePositions()
    {
        var label = Config("label", "title",
            new LayoutPosition { Left = 0.02, Top = 0.01, Width = 0.5, Height = 0.05 });
        var button = Config("button", "run",
            new LayoutPosition { Left = 0.02, Top = 0.93, Width = 0.2, Height = 0.05 });
        var result = Projector.Project(new[] { label, button }, 1000, 800);

        StringAssert.Contains(result.BodyHtml, "class=\"ui-root\"");
        StringAssert.Contains(result.BodyHtml, "data-name=\"title\"");
        StringAssert.Contains(result.BodyHtml, "left:20px;top:8px;width:500px;height:40px;");
        StringAssert.Contains(result.BodyHtml, "left:20px;top:744px;width:200px;height:40px;");
        Assert.IsTrue(result.Css.Length > 0);
    }

    [TestMethod]
    public void Project_ContainerNesting_CoordinatesInParent()
    {
        // 容器 0.5x0.5 → 500x400；容器内 label 0.5,0.5 → 250x200 相对容器
        var inner = Config("label", "inner",
            new LayoutPosition { Left = 0.5, Top = 0.5, Width = 0.5, Height = 0.5 });
        var outer = Config("panel", "outer",
            new LayoutPosition { Left = 0, Top = 0, Width = 0.5, Height = 0.5 },
            new List<ControlConfig> { inner });

        var result = Projector.Project(new[] { outer }, 1000, 800);

        StringAssert.Contains(result.BodyHtml, "left:250px;top:200px;width:250px;height:200px;");
    }

    [TestMethod]
    public void UiProjector_TakesFirstViewAndFormSize()
    {
        var config = new UIConfiguration
        {
            Form = new FormConfig { Width = 1000, Height = 800 },
            Views = new List<ViewConfig>
            {
                new()
                {
                    Name = "Main",
                    Controls = new List<ControlConfig>
                    {
                        Config("label", "title", new LayoutPosition { Left = 0.1, Top = 0.1, Width = 0.2, Height = 0.1 })
                    }
                }
            }
        };

        var result = HtmlUiProjector.Project(config);

        StringAssert.Contains(result.BodyHtml, "width:1000px;height:800px;");
        StringAssert.Contains(result.BodyHtml, "left:100px;top:80px;width:200px;height:80px;");
    }

    [TestMethod]
    public void ProjectFlow_EmitsSemanticLayout_NoAbsolutePixels()
    {
        var label = Config("label", "title",
            new LayoutPosition { Left = 0.02, Top = 0.01, Width = 0.5, Height = 0.05, Intent = "proportional" });
        var result = Projector.ProjectFlow(new[] { label });

        // 流式：ui-root.flow + data-name + data-layout（语义 intent），不产绝对像素
        StringAssert.Contains(result.BodyHtml, "class=\"ui-root flow\"");
        StringAssert.Contains(result.BodyHtml, "data-name=\"title\"");
        StringAssert.Contains(result.BodyHtml, "data-layout=\"proportional\"");
        Assert.IsFalse(result.BodyHtml.Contains("position:absolute"));
        Assert.IsFalse(result.BodyHtml.Contains("left:20px"));
        Assert.IsTrue(result.Css.Contains(".ui-root.flow"));
    }
}
