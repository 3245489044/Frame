using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plate.HtmlViewer;

namespace Plate.HtmlViewer.Tests;

[TestClass]
public class ThemeModeTests
{
    [TestMethod]
    public void Map_Light_Dark_System()
    {
        Assert.AreEqual(
            Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light,
            ThemeMode.Light.Map());
        Assert.AreEqual(
            Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark,
            ThemeMode.Dark.Map());
        Assert.AreEqual(
            Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Auto,
            ThemeMode.System.Map());
    }

    [TestMethod]
    public void ToHtml_ExplicitMode_EmitsColorSchemeMeta()
    {
        var light = HtmlDocumentRenderer.ToHtml("<p>x</p>", null, ThemeMode.Light);
        var dark = HtmlDocumentRenderer.ToHtml("<p>x</p>", null, ThemeMode.Dark);
        var system = HtmlDocumentRenderer.ToHtml("<p>x</p>", null, ThemeMode.System);

        StringAssert.Contains(light, "content=\"light\"");
        StringAssert.Contains(dark, "content=\"dark\"");
        StringAssert.Contains(system, "content=\"light dark\"");
    }
}
