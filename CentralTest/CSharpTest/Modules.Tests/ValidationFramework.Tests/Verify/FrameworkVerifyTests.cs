
using ValidationFramework.Verify;

namespace ValidationFramework.Tests.Verify;

[TestClass]
public class FrameworkVerifyTests
{
    [TestMethod]
    public void Run_应返回通过()
    {
        var passed = FrameworkVerify.Run(out var summary);

        Assert.IsTrue(passed, summary);
    }

    [TestMethod]
    public void Run_摘要应包含各组检查结果()
    {
        FrameworkVerify.Run(out var summary);

        Assert.IsTrue(summary.Contains("FrameworkVerify"));
        Assert.IsTrue(summary.Contains("引擎装配"));
        Assert.IsTrue(summary.Contains("契约规则"));
        Assert.IsTrue(summary.Contains("管线"));
        Assert.IsTrue(summary.Contains("反馈格式化"));
    }

    [TestMethod]
    public void Run_通过时_摘要应含PASS()
    {
        FrameworkVerify.Run(out var summary);

        Assert.IsTrue(summary.Contains("PASS"));
    }

    [TestMethod]
    public void Run_应可重复执行()
    {
        var first = FrameworkVerify.Run(out _);
        var second = FrameworkVerify.Run(out _);

        Assert.AreEqual(first, second);
    }
}