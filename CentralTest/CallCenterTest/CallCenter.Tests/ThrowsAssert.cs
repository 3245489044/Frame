using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CallCenter.Tests;

/// <summary>本地断言助手：捕获指定异常类型并断言抛出（规避不同 MSTest 版本 Assert API 差异）。</summary>
internal static class ThrowsAssert
{
    public static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            Assert.Fail($"期望抛出 {typeof(T).Name}，实际抛出 {ex.GetType().Name}: {ex.Message}");
        }

        Assert.Fail($"未抛出期望的 {typeof(T).Name}");
        throw new InvalidOperationException("不可达");
    }
}
