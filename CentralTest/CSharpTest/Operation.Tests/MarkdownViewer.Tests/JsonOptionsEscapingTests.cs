using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace MarkdownViewer.Tests;

[TestClass]
public class JsonOptionsEscapingTests
{
    [TestMethod]
    public void PersistenceHelper_JsonOptions_KeepsChineseLiteral()
    {
        var json = JsonSerializer.Serialize(new { Text = "最小测试" }, PersistenceHelper.JsonOptions);
        StringAssert.Contains(json, "最小测试");
        Assert.IsFalse(json.Contains("\\u6700", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PersistenceHelper_JsonOptions_RoundTrips()
    {
        var obj = new { Text = "最小测试", Priority = 0 };
        var json = JsonSerializer.Serialize(obj, PersistenceHelper.JsonOptions);
        var back = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        Assert.IsNotNull(back);
        Assert.AreEqual("最小测试", back["Text"].ToString());
    }
}
