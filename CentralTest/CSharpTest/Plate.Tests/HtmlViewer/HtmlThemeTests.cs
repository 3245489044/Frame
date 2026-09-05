using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plate.HtmlViewer;

namespace Plate.HtmlViewer.Tests;

[TestClass]
public class HtmlThemeTests
{
    private sealed class StubTheme(string css) : IHtmlTheme
    {
        public string Css => css;
    }

    [TestInitialize]
    public void Init() => HtmlTheme.Detach();

    [TestCleanup]
    public void Cleanup() => HtmlTheme.Detach();

    [TestMethod]
    public void ToHtml_WithoutTheme_FallsBackToDefaultCss()
    {
        var html = HtmlDocumentRenderer.ToHtml("<p>hi</p>");
        StringAssert.Contains(html, HtmlDocumentRenderer.DefaultCss);
        StringAssert.Contains(html, "<p>hi</p>");
    }

    [TestMethod]
    public void ToHtml_WithInjectedTheme_EmbedsThemeCss()
    {
        var html = HtmlDocumentRenderer.ToHtml("<p>x</p>", new StubTheme("body{color:red}"));
        StringAssert.Contains(html, "body{color:red}");
        Assert.IsFalse(html.Contains(HtmlDocumentRenderer.DefaultCss));
    }

    [TestMethod]
    public void ToHtml_NullThemeArg_FallsBackToDefault()
    {
        var html = HtmlDocumentRenderer.ToHtml("<p>x</p>", (IHtmlTheme?)null);
        StringAssert.Contains(html, HtmlDocumentRenderer.DefaultCss);
    }

    [TestMethod]
    public void Resolve_AfterAttach_ReturnsInjected_DetachReturnsDefault()
    {
        var t = new StubTheme("body{}");
        HtmlTheme.Attach(t);
        Assert.AreSame(t, HtmlTheme.Resolve());
        HtmlTheme.Detach();
        Assert.AreSame(HtmlTheme.Default, HtmlTheme.Resolve());
    }
}
