using TConfig;
using UiBackend.Html;

namespace UiBackend.Tests;

[TestClass]
public class HtmlPageRendererTests
{
    private static UIConfiguration Declaration()
        => new()
        {
            Form = new FormConfig { Width = 1280, Height = 940 },
            Views = new List<ViewConfig>
            {
                new()
                {
                    Name = "ConversationView",
                    Type = "Panel",
                    Controls = new List<ControlConfig>
                    {
                        new()
                        {
                            Type = "htmlviewer", Name = "taskListViewer", Text = "{taskList}",
                            Layout = new LayoutPosition { Left = 0, Top = 0.07, Width = 1, Height = 0.93 },
                        },
                        new()
                        {
                            Type = "htmlviewer", Name = "conversationHistoryViewer", Text = "{history}",
                            Layout = new LayoutPosition { Left = 0.25, Top = 0.02, Width = 0.74, Height = 0.96 },
                        },
                    },
                },
            },
        };

    [TestMethod]
    public void Project_OutputsLayoutShell_WithDeclaredDataNames()
    {
        var result = new HtmlPageRenderer(Declaration()).Project();

        StringAssert.Contains(result.BodyHtml, "class=\"ui-root\"");
        StringAssert.Contains(result.BodyHtml, "data-name=\"taskListViewer\"");
        StringAssert.Contains(result.BodyHtml, "data-name=\"conversationHistoryViewer\"");
        Assert.IsTrue(result.Css.Length > 0);
    }

    [TestMethod]
    public void Render_WrapsCssAndBody_AndInjectsData()
    {
        var html = new HtmlPageRenderer(Declaration()).Render(
            new Dictionary<string, string>
            {
                ["taskList"] = "<button data-cmd=\"selectTask\">a</button>",
                ["history"] = "<div class=\"msg user\">hi</div>",
            });

        StringAssert.StartsWith(html, "<style>");
        StringAssert.Contains(html, "</style><div class=\"ui-root\"");
        StringAssert.Contains(html, "<button data-cmd=\"selectTask\">a</button>");
        StringAssert.Contains(html, "<div class=\"msg user\">hi</div>");
        // 占位已替换，不再残留 {taskList}/{history}
        Assert.IsFalse(html.Contains("{taskList}"));
        Assert.IsFalse(html.Contains("{history}"));
    }

    [TestMethod]
    public void Render_FromHostHtmlPage_ProjectsInternalDeclaration()
    {
        var host = new ControlConfig
        {
            Type = "htmlviewer",
            Name = "conversationHistoryViewer",
            HtmlPage = new HtmlPageDeclaration
            {
                Name = "conversationPage",
                Width = 1280,
                Height = 940,
                Controls = new List<ControlConfig>
                {
                    new()
                    {
                        Type = "htmlviewer", Name = "taskListViewer", Text = "{taskList}",
                        Layout = new LayoutPosition { Left = 0, Top = 0, Width = 1, Height = 1 },
                    },
                },
            },
        };

        var html = new HtmlPageRenderer(host, "conversationPage").Render(
            new Dictionary<string, string> { ["taskList"] = "<li>x</li>" });

        StringAssert.Contains(html, "data-name=\"taskListViewer\"");
        StringAssert.Contains(html, "<li>x</li>");
    }

    [TestMethod]
    public void Ctor_HostWithoutHtmlPage_Throws()
    {
        var host = new ControlConfig { Type = "htmlviewer", Name = "viewer" };

        Assert.ThrowsExactly<ArgumentException>(() => new HtmlPageRenderer(host, "conversationPage"));
    }
}
