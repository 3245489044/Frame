
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.DiagnosticContext.Core;

namespace DiagnosticContext.Tests.Core;

[TestClass]
public class EventEntryTests
{
    [TestMethod]
    public void 创建事件条目_应设置属性()
    {
        var entry = new EventEntry("test.event", 100, new { Value = 42 });

        Assert.AreEqual("test.event", entry.EventName);
        Assert.AreEqual(100, entry.RelativeMs);
        Assert.IsNotNull(entry.DataJson);
        Assert.IsTrue(entry.DataJson!.Contains("42"));
    }

    [TestMethod]
    public void 创建事件条目_无数据_DataJson应为Null()
    {
        var entry = new EventEntry("test.event", 100);

        Assert.AreEqual("test.event", entry.EventName);
        Assert.AreEqual(100, entry.RelativeMs);
        Assert.IsNull(entry.DataJson);
    }

    [TestMethod]
    public void Timestamp_应格式化正确()
    {
        var entry = new EventEntry("test", 123456);

        Assert.AreEqual("02:03.456", entry.Timestamp);
    }

    [TestMethod]
    public void Timestamp_零毫秒_应显示00_00_000()
    {
        var entry = new EventEntry("test", 0);

        Assert.AreEqual("00:00.000", entry.Timestamp);
    }

    [TestMethod]
    public void DataDisplay_有数据_应返回Json()
    {
        var entry = new EventEntry("test", 0, new { Name = "hello" });

        Assert.IsNotNull(entry.DataDisplay);
        Assert.IsTrue(entry.DataDisplay.Contains("hello"));
    }

    [TestMethod]
    public void DataDisplay_无数据_应返回_null占位()
    {
        var entry = new EventEntry("test", 0);

        Assert.AreEqual("(null)", entry.DataDisplay);
    }

    [TestMethod]
    public void 事件名称null_应抛出ArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() => new EventEntry(null!, 0));
    }

    [TestMethod]
    public void 事件名称空字符串_应允许()
    {
        var entry = new EventEntry("", 0);
        Assert.AreEqual("", entry.EventName);
    }
}
