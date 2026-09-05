using TConfig;
using UiBackend.Html;

namespace UiBackend.Tests;

[TestClass]
public class HtmlDeclarationParserTests
{
    [TestMethod]
    public void Parse_CaseInsensitive_BindsJsonToPascalCase()
    {
        // 小写 JSON 属性需绑上 PascalCase CLR 属性（PropertyNameCaseInsensitive）——空控件树陷阱的回归防护
        const string json = """
            { "form": { "width": 1000, "height": 800 },
              "views": [ { "name": "Main", "type": "Panel",
                "controls": [ { "type": "button", "name": "runBtn", "text": "运行" } ] } ] }
            """;

        var config = HtmlDeclarationParser.Parse(json);

        Assert.IsNotNull(config);
        Assert.AreEqual(1000, config!.Form.Width);
        Assert.AreEqual(1, config.Views.Count);
        Assert.AreEqual("runBtn", config.Views[0].Controls[0].Name);
        Assert.AreEqual("button", config.Views[0].Controls[0].Type);
    }

    [TestMethod]
    public void Parse_StyleStringNamedReference_DoesNotThrow_AndCapturesStyleReference()
    {
        // 回归：AgentRuntimeUI.json 用字符串命名样式 `"style": "readonly-bg"`，
        // 此前 HtmlDeclarationParser 裸反序列化抛 "The JSON value could not be converted to TConfig.StyleConfig"，
        // 导致 AgentUi_HtmlAttach 装配失败。字符串形态应落到 StyleReference（对象形态仍按默认）。
        const string json = """
            { "views": [ { "name": "Main", "type": "Panel",
                "controls": [ { "type": "textbox", "name": "resultTextBox", "style": "readonly-bg" },
                               { "type": "textbox", "name": "styledBox",
                                 "style": { "fontSize": 11, "foreColor": "#1F2937", "backColor": "#F8FAFC" } } ] } ] }
            """;

        var config = HtmlDeclarationParser.Parse(json);

        Assert.IsNotNull(config);
        var result = config!.Views[0].Controls[0];
        Assert.AreEqual("readonly-bg", result.Style!.StyleReference);
        // 对象形态仍按默认反序列化（内联属性绑定，StyleReference 为空）
        var styled = config.Views[0].Controls[1];
        Assert.IsNotNull(styled.Style);
        Assert.IsNull(styled.Style!.StyleReference);
        Assert.AreEqual(11f, styled.Style.FontSize);
        Assert.AreEqual("#1F2937", styled.Style.ForeColor);
        Assert.AreEqual("#F8FAFC", styled.Style.BackColor);
    }

    [TestMethod]
    public void Parse_NullOrEmpty_ReturnsNull()
    {
        Assert.IsNull(HtmlDeclarationParser.Parse(""));
        Assert.IsNull(HtmlDeclarationParser.Parse("   "));
        Assert.IsNull(HtmlDeclarationParser.Parse(null!));
    }

    [TestMethod]
    public void BuildControlIndex_IndexesAllViewsRecursively_CaseInsensitive()
    {
        var config = new UIConfiguration
        {
            Views = new List<ViewConfig>
            {
                new()
                {
                    Name = "V1",
                    Controls = new List<ControlConfig>
                    {
                        new() { Type = "panel", Name = "Group",
                            Controls = new List<ControlConfig> { new() { Type = "button", Name = "RunBtn" } } },
                        new() { Type = "label", Name = "title" },
                    },
                },
                new() { Name = "V2", Controls = new List<ControlConfig> { new() { Type = "button", Name = "saveBtn" } } },
            },
        };

        var index = HtmlDeclarationParser.BuildControlIndex(config);

        Assert.AreEqual(4, index.Count);
        Assert.IsTrue(index.ContainsKey("runbtn")); // 大小写不敏感
        Assert.IsTrue(index.ContainsKey("GROUP"));
        Assert.IsTrue(index.ContainsKey("title"));
        Assert.IsTrue(index.ContainsKey("saveBtn"));
        var panel = config.Views[0]!.Controls[0]!;
        var inner = panel.Controls![0]!;
        Assert.AreSame(inner, index["RunBtn"]);
    }

    [TestMethod]
    public void BuildControlIndex_EmptyViews_ReturnsEmpty()
    {
        var index = HtmlDeclarationParser.BuildControlIndex(new UIConfiguration());
        Assert.AreEqual(0, index.Count);
    }

    [TestMethod]
    public void ExtractHtmlPage_ReturnsWrappedDeclaration_WithContainerSize()
    {
        var page = new HtmlPageDeclaration
        {
            Name = "conversationPage",
            Width = 1280,
            Height = 940,
            Controls = new List<ControlConfig> { new() { Type = "panel", Name = "sidebar" } },
        };
        var host = new ControlConfig { Type = "htmlviewer", Name = "viewer", HtmlPage = page };

        var extracted = HtmlDeclarationParser.ExtractHtmlPage(host, "conversationPage");

        Assert.IsNotNull(extracted);
        Assert.AreEqual(1280, extracted!.Form.Width);
        Assert.AreEqual(940, extracted.Form.Height);
        Assert.AreEqual("sidebar", extracted.Views[0].Controls[0].Name);
    }

    [TestMethod]
    public void ExtractHtmlPage_NameMismatch_ReturnsNull()
    {
        var host = new ControlConfig
        {
            Type = "htmlviewer",
            Name = "viewer",
            HtmlPage = new HtmlPageDeclaration { Name = "other", Controls = new List<ControlConfig> { new() { Type = "label" } } },
        };

        Assert.IsNull(HtmlDeclarationParser.ExtractHtmlPage(host, "conversationPage"));
    }

    [TestMethod]
    public void ExtractHtmlPage_NoDeclaration_ReturnsNull()
    {
        Assert.IsNull(HtmlDeclarationParser.ExtractHtmlPage(new ControlConfig { Type = "htmlviewer", Name = "v" }, "page"));
        Assert.IsNull(HtmlDeclarationParser.ExtractHtmlPage(null!, "page"));
    }

    [TestMethod]
    public void InjectData_ReplacesPlaceholders_OnlyProvidedKeys()
    {
        var body = "<div>{taskList}</div><div>{history}</div><div>{missing}</div>";
        var injected = HtmlDeclarationParser.InjectData(
            body, new Dictionary<string, string> { ["taskList"] = "<b>t</b>", ["history"] = "<p>h</p>" });

        Assert.AreEqual("<div><b>t</b></div><div><p>h</p></div><div>{missing}</div>", injected);
    }

    [TestMethod]
    public void InjectData_NullOrEmpty_ReturnsOriginal()
    {
        Assert.AreEqual("abc", HtmlDeclarationParser.InjectData("abc", null));
        Assert.AreEqual("abc", HtmlDeclarationParser.InjectData("abc", new Dictionary<string, string>()));
    }

    [TestMethod]
    public void BuildDomainControlIndex_IsolatesHostAndPageDomains()
    {
        // P4 域隔离：外层物理控件（host:）与 htmlPage 内同名元素（page:）按域索引互不覆盖，
        // output/脚本 target 以 HostKey/PageKey 显式带域消歧（消除 8.26 R4 同名含混）。
        var host = new ControlConfig
        {
            Type = "htmlviewer",
            Name = "conversationHistoryViewer",
            HtmlPage = new HtmlPageDeclaration
            {
                Name = "conversationPage",
                Controls = new List<ControlConfig>
                {
                    new() { Type = "htmlviewer", Name = "conversationHistoryViewer" }, // 与物理控件同裸名
                    new() { Type = "label", Name = "sidebarTitle" },
                },
            },
        };
        var config = new UIConfiguration
        {
            Views = new List<ViewConfig>
            {
                new() { Name = "Main", Controls = new List<ControlConfig> { host } },
            },
        };

        var index = HtmlDeclarationParser.BuildDomainControlIndex(config);

        // host 域：物理控件
        Assert.IsTrue(index.ContainsKey(HtmlDeclarationParser.HostKey("conversationHistoryViewer")));
        // page 域：htmlPage 内同名元素 + 普通页内元素
        Assert.IsTrue(index.ContainsKey(HtmlDeclarationParser.PageKey("conversationHistoryViewer")));
        Assert.IsTrue(index.ContainsKey(HtmlDeclarationParser.PageKey("sidebarTitle")));
        // 两域同名互不覆盖（都能取到且指向不同实例）
        Assert.AreSame(host, index[HtmlDeclarationParser.HostKey("conversationHistoryViewer")]);
        Assert.AreNotSame(host, index[HtmlDeclarationParser.PageKey("conversationHistoryViewer")]);
        // 兼容索引（裸名）不含 htmlPage 内部元素：物理控件唯一，写回目标确定
        var bare = HtmlDeclarationParser.BuildControlIndex(config);
        Assert.AreEqual(1, bare.Count);
        Assert.IsTrue(bare.ContainsKey("conversationHistoryViewer"));
    }
}
