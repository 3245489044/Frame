using System.Collections.Generic;
using UiBackend.Html;

namespace UiBackend.Tests;

[TestClass]
public class HtmlTemplateEngineTests
{
    private static IReadOnlyDictionary<string, string> Templates(params (string k, string v)[] xs)
    {
        var d = new Dictionary<string, string>();
        foreach (var (k, v) in xs) d[k] = v;
        return d;
    }

    private static Dictionary<string, object?> Model(params (string k, object? v)[] xs)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in xs) d[k] = v;
        return d;
    }

    [TestMethod]
    public void Binding_EncodesHtml()
    {
        var tpl = "<b>{text}</b>";
        var html = HtmlTemplateEngine.RenderTemplate(tpl, null, Model(("text", "<script>alert(1)</script>")));
        StringAssert.Contains(html, "&lt;script&gt;alert(1)&lt;/script&gt;");
        Assert.IsFalse(html.Contains("<script>"));
    }

    [TestMethod]
    public void Binding_Raw_InsertsTrustedHtml()
    {
        var tpl = "<div>{tree|raw}</div>";
        var html = HtmlTemplateEngine.RenderTemplate(tpl, null, Model(("tree", "<button>x</button>")));
        Assert.AreEqual("<div><button>x</button></div>", html);
    }

    [TestMethod]
    public void UnknownBinding_RendersEmpty()
    {
        var tpl = "a{bogus}b";
        var html = HtmlTemplateEngine.RenderTemplate(tpl, null, Model(("other", "x")));
        Assert.AreEqual("ab", html);
    }

    [TestMethod]
    public void Each_LoopsOverRows()
    {
        var tpl = "<ul>{#each items}<li>{name}</li>{/each}</ul>";
        var items = new List<object?>
        {
            Model(("name", "a")),
            Model(("name", "b")),
        };
        var html = HtmlTemplateEngine.RenderTemplate(tpl, null, Model(("items", items)));
        Assert.AreEqual("<ul><li>a</li><li>b</li></ul>", html);
    }

    [TestMethod]
    public void Each_ScalarItems_ResolveThis()
    {
        // {#each} 遍历纯字符串/标量列表时，{this} 应指向该项本身（节点标签等场景）。
        var tpl = "{#each tags}<span class=\"node-tag\">{this}</span>{/each}";
        var tags = new List<object?> { "agent-round.a", "agent-round.b" };
        var html = HtmlTemplateEngine.RenderTemplate(tpl, null, Model(("tags", tags)));
        Assert.AreEqual(
            "<span class=\"node-tag\">agent-round.a</span><span class=\"node-tag\">agent-round.b</span>",
            html);
    }

    [TestMethod]
    public void Conditional_IfElse()
    {
        var tpl = "<x{?active} active{/}>{?mode}a{:else}b{/}</x>";
        Assert.AreEqual("<x active>a</x>", HtmlTemplateEngine.RenderTemplate(tpl, null, Model(("active", "1"), ("mode", "1"))));
        Assert.AreEqual("<x>b</x>", HtmlTemplateEngine.RenderTemplate(tpl, null, Model(("active", ""), ("mode", ""))));
    }

    [TestMethod]
    public void NamedTemplate_ResolvesWithCurrentModel()
    {
        var templates = Templates(
            ("item", "<button data-cmd=\"go\" data-key=\"{key}\">{label}</button>"),
            ("list", "{#each rows}{>item}{/each}"));
        var rows = new List<object?> { Model(("key", "k1"), ("label", "一")), Model(("key", "k2"), ("label", "二")) };
        var html = HtmlTemplateEngine.Render(templates, "list", Model(("rows", rows)));
        Assert.IsTrue(html.Contains("data-key=\"k1\">一</button>"));
        Assert.IsTrue(html.Contains("data-key=\"k2\">二</button>"));
    }

    [TestMethod]
    public void Render_MissingTemplate_ReturnsFallback()
    {
        var html = HtmlTemplateEngine.Render(Templates(("a", "x")), "nope", Model(("k", "v")), "FALLBACK");
        Assert.AreEqual("FALLBACK", html);
    }
}
