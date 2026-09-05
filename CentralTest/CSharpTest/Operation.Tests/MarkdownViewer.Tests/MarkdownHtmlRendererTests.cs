using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plate.MarkdownViewer;

namespace MarkdownViewer.Tests;

[TestClass]
public class MarkdownHtmlRendererTests
{
    [TestMethod]
    public void ToMarkdownHtml_RendersHeading_ToH1()
    {
        var html = MarkdownHtmlRenderer.ToMarkdownHtml("# 标题");
        StringAssert.Contains(html, "<h1");
        StringAssert.Contains(html, "标题");
    }

    [TestMethod]
    public void ToMarkdownHtml_RendersTable()
    {
        var html = MarkdownHtmlRenderer.ToMarkdownHtml("| A | B |\n|---|---|\n| 1 | 2 |");
        StringAssert.Contains(html, "<table");
    }

    [TestMethod]
    public void ToMarkdownHtml_DisablesRawHtml_ForXss()
    {
        var html = MarkdownHtmlRenderer.ToMarkdownHtml("<script>alert(1)</script>");
        // DisableHtml 关闭原始 HTML 通道：标签被转义为文本实体，脚本不进入 DOM
        Assert.IsFalse(html.Contains("<script>alert", StringComparison.Ordinal));
        StringAssert.Contains(html, "&lt;script&gt;");
    }

    [TestMethod]
    public void ToMarkdownHtml_KeepsChineseLiteral()
    {
        var html = MarkdownHtmlRenderer.ToMarkdownHtml("你好\n\n---\n最小测试");
        StringAssert.Contains(html, "你好");
        StringAssert.Contains(html, "最小测试");
    }
}
