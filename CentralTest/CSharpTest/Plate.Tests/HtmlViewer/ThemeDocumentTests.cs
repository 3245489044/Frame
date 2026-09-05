using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plate.HtmlViewer;

namespace Plate.HtmlViewer.Tests;

/// <summary>主题文档（P4 主题单一源 IR）行为回归：token 浅/深 + 组件样式 + 兼容适配。</summary>
[TestClass]
public class ThemeDocumentTests
{
    [TestMethod]
    public void ToCss_EmitsTokens_LightDark_AndComponentCss()
    {
        var doc = new ThemeDocument(
            TokensLight: new Dictionary<string, string> { ["bg"] = "#fff", ["fg"] = "#111" },
            TokensDark: new Dictionary<string, string> { ["bg"] = "#000", ["fg"] = "#eee" },
            ComponentCss: "body{color:var(--fg)}",
            CssFiles: null);

        var css = doc.ToCss();

        StringAssert.Contains(css, ":root{--bg:#fff;--fg:#111;}");
        StringAssert.Contains(css, "@media (prefers-color-scheme: dark){:root{--bg:#000;--fg:#eee;}}");
        StringAssert.Contains(css, "body{color:var(--fg)}");
    }

    [TestMethod]
    public void ToCss_DeepColors_NestedCorrectly()
    {
        var doc = new ThemeDocument(
            TokensDark: new Dictionary<string, string> { ["bg"] = "#0d1117" },
            ComponentCss: ".msg{color:var(--fg)}",
            TokensLight: null, CssFiles: null);

        var css = doc.ToCss();
        // 深色块独立成对的花括号闭合，不串扰后置组件样式
        Assert.IsTrue(css.IndexOf(".msg", System.StringComparison.Ordinal) > css.IndexOf("prefers-color-scheme", System.StringComparison.Ordinal));
    }

    [TestMethod]
    public void FromCss_CarriesCssAsComponent()
    {
        var doc = ThemeDocument.FromCss("body{color:red}");
        Assert.AreEqual("body{color:red}", doc.ToCss());
    }

    [TestMethod]
    public void Empty_ToCss_IsEmpty()
    {
        Assert.AreEqual(string.Empty, ThemeDocument.Empty.ToCss());
    }

    [TestMethod]
    public void ConfigThemeProvider_Resolve_ReturnsInjectedDocument()
    {
        var provider = new ConfigThemeProvider(componentCss: "body{}");
        var doc = provider.Resolve(ThemeMode.Light, ThemeScope.Document);
        Assert.AreEqual("body{}", doc.ToCss());
    }
}
