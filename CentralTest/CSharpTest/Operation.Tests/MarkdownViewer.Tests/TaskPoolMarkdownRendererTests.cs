using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FrontEndDriven.Rendering;
using Share.TaskStatusPool.TaskPool;

namespace MarkdownViewer.Tests;

[TestClass]
public class TaskPoolMarkdownRendererTests
{
    private static TaskPoolRowView Row(string key, string? payload, int priority = 0, int retries = 0, string status = "Completed")
        => new(1, key, priority, retries, status, payload, "2026-08-16T08:17:53Z", "2026-08-16T08:17:54Z");

    [TestMethod]
    public void RenderPage_ProducesOverviewTable()
    {
        var md = TaskPoolMarkdownRenderer.RenderPage(
            new[] { Row("tp_1", """{"UserInput":"测试"}""") }, 1, 42);
        StringAssert.Contains(md, "# 任务池（第 1 页，共 42 条）");
        StringAssert.Contains(md, "| Id | TaskKey | Pri | Retry | Status | CreatedAt | UpdatedAt |");
        StringAssert.Contains(md, "`tp_1`");
        StringAssert.Contains(md, "```json");
    }

    [TestMethod]
    public void RenderPage_EmptyRows_RendersEmpty()
    {
        var md = TaskPoolMarkdownRenderer.RenderPage(Array.Empty<TaskPoolRowView>(), 1, 0);
        StringAssert.Contains(md, "_（空）_");
    }

    [TestMethod]
    public void Pretty_DecodesChinese_AndIndents()
    {
        var pretty = TaskPoolMarkdownRenderer.Pretty("""{"UserInput":"\u6700\u5C0F测试","Priority":0}""");
        StringAssert.Contains(pretty, "最小测试");
        Assert.IsFalse(pretty.Contains("\\u6700", StringComparison.Ordinal));
        StringAssert.Contains(pretty, "\n"); // 缩进
    }

    [TestMethod]
    public void Pretty_NonJson_ReturnsRaw()
    {
        Assert.AreEqual("not-json", TaskPoolMarkdownRenderer.Pretty("not-json"));
    }

    [TestMethod]
    public void Pretty_Null_ReturnsEmptyObject()
    {
        Assert.AreEqual("{}", TaskPoolMarkdownRenderer.Pretty(null));
    }
}
