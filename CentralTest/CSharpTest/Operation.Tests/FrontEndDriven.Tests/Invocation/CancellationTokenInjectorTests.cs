using Microsoft.VisualStudio.TestTools.UnitTesting;
using ExecutionFramework.Engine;

namespace FrontEndDriven.Tests.Invocation;

/// <summary>
/// 取消令牌注入器回归测试：验证只对声明 CancellationToken 的委托注入 ct（其余零影响）。
/// </summary>
[TestClass]
public class CancellationTokenInjectorTests
{
    [TestMethod]
    public void 未声明ct的委托_原样返回()
    {
        Func<double, double, double> fn = (a, b) => a + b;
        var args = new object[] { 1.0, 2.0 };

        var result = CancellationTokenInjector.Inject(fn, args, CancellationToken.None);

        Assert.AreEqual(2, result.Length);
        Assert.AreSame(args, result);   // 未声明 ct → 返回原数组，零影响
    }

    [TestMethod]
    public void 末位声明ct的委托_注入到末位()
    {
        Func<string, CancellationToken, Task<string>> fn = (s, ct) => Task.FromResult(s);
        var args = new object[] { "x" };

        var result = CancellationTokenInjector.Inject(fn, args, CancellationToken.None);

        Assert.AreEqual(2, result.Length);
        Assert.AreEqual("x", result[0]);
        Assert.AreEqual(CancellationToken.None, result[1]);
    }

    [TestMethod]
    public void 含其他参数且ct在末位_注入到正确位置()
    {
        Func<string, int, CancellationToken, Task<string>> fn = (s, n, ct) => Task.FromResult(s);
        var args = new object[] { "x", 42 };

        var result = CancellationTokenInjector.Inject(fn, args, CancellationToken.None);

        Assert.AreEqual(3, result.Length);
        Assert.AreEqual("x", result[0]);
        Assert.AreEqual(42, result[1]);
        Assert.AreEqual(CancellationToken.None, result[2]);
    }

    [TestMethod]
    public void 数组委托_包装为单参不注入()
    {
        Func<object[], Task<object>> fn = arr => Task.FromResult<object>(arr);
        var args = new object[] { "a", "b" };

        var result = CancellationTokenInjector.Inject(fn, args, CancellationToken.None);

        Assert.AreEqual(1, result.Length);
        CollectionAssert.AreEqual(args, (object[])result[0]!);
    }

    [TestMethod]
    public void 无参委托_返回空数组()
    {
        Func<string> fn = () => "ok";

        var result = CancellationTokenInjector.Inject(fn, Array.Empty<object>(), CancellationToken.None);

        Assert.AreEqual(0, result.Length);
    }

    [TestMethod]
    public void FunctionDescriptor_从委托排除CancellationToken参数_保持参数管线可用()
    {
        // 委托声明 ct（末位）→ FromDelegate 的 Parameters 必须排除 ct，
        // 使 ParameterTransformer.ForConfig 不会要求绑定字典提供 "ct"（否则抛"参数未找到"）。
        Func<string, int, CancellationToken, Task<string>> fn = (s, n, ct) => Task.FromResult(s);

        var desc = TConfig.FunctionDescriptor.FromDelegate(fn);

        Assert.AreEqual(2, desc.Parameters.Count);
        Assert.AreEqual("s", desc.Parameters[0].Name);
        Assert.AreEqual("n", desc.Parameters[1].Name);

        // 注入后 args 补位 ct → 与委托真实参数数一致（DynamicInvoke 不越界）。
        var bound = CancellationTokenInjector.Inject(fn, new object[] { "x", 42 }, CancellationToken.None);
        Assert.AreEqual(3, bound.Length);
    }
}
