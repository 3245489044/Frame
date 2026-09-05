using CallCenter.Adapters;
using CallCenter.Core.Discovery;
using CallCenter.Core.Tools;
using CallCenter.Core.Tracks;
using ExecutionFramework.Abstractions;
using ExecutionFramework.Engine;
using TConfig;

namespace CallCenter.Tests;

/// <summary>
/// 阶段 A 宿主接线等价验证：CSharpTrack 注入 <see cref="ExecutionFrameworkDelegateInvoker"/>（真实
/// ExecutionFramework.FunctionExecutor 后端）后，与函数库捕获的注册表同源、同执行栈，结果与原生一致，
/// 且异常经 ExecutionFramework 统一映射（消除直调与执行栈的平行分叉，对齐 SingleImplementation/TH-12）。
/// </summary>
[TestClass]
public class CSharpTrackExecutionFrameworkTests
{
    private static (CSharpTrack Track, ITrConfigRegistry Registry) BuildExecutionFrameworkTrack(IReadOnlyList<Type> libraryTypes)
    {
        var source = CSharpFunctionLibrarySource.FromLibraryTypes(libraryTypes);
        var defs = source.DiscoverDefinitions();

        Assert.IsNotNull(source.Registry, "发现源应暴露 RegisterAll 所用注册表（供宿主建统一执行栈）");
        var executor = new FunctionExecutor(
            source.Registry!,
            new NullParameterProcessor(),
            new FallbackTypeConverter());

        var track = new CSharpTrack(defs, new ExecutionFrameworkDelegateInvoker(executor));
        return (track, source.Registry!);
    }

    [TestMethod]
    public async Task ExecutionFramework_Backed_Execution_Equals_Direct_For_Simple_Function()
    {
        var (track, _) = BuildExecutionFrameworkTrack(new[] { typeof(CSharpFunctionLibrarySourceTests.SampleHostLibrary) });

        var result = await track.CallAsync("sample_add", "{\"a\":2,\"b\":3}");

        Assert.IsTrue(result.Success);
        Assert.AreEqual("5", result.OutputJson, "经 ExecutionFramework 执行应产生与原生一致的结果");
    }

    [TestMethod]
    public async Task ExecutionFramework_Backed_Execution_Handles_Echo_String()
    {
        var (track, _) = BuildExecutionFrameworkTrack(new[] { typeof(CSharpFunctionLibrarySourceTests.SampleHostLibrary) });

        var result = await track.CallAsync("sample_echo", "{\"text\":\"hi\"}");

        Assert.IsTrue(result.Success);
        Assert.AreEqual("\"hi\"", result.OutputJson);
    }

    [TestMethod]
    public async Task ExecutionFramework_Backed_Execution_Maps_Function_Exception_To_Fail()
    {
        var (track, _) = BuildExecutionFrameworkTrack(new[] { typeof(ThrowingHostLibrary) });

        var result = await track.CallAsync("sample_boom", "{\"a\":1}");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "boom-fx", "ExecutionFramework 应把 TargetInvocation 内层异常解包为错误信息");
    }

    [TestMethod]
    public void Registry_Exposed_After_Discovery_Holds_Registered_Functions()
    {
        var (_, registry) = BuildExecutionFrameworkTrack(new[] { typeof(CSharpFunctionLibrarySourceTests.SampleHostLibrary) });

        Assert.IsTrue(registry.GetRegisteredNames().Contains("sample_add"));
        Assert.IsTrue(registry.Contains("sample_echo"));
    }

    /// <summary>阶段 A 用测试库：抛异常的委托，验证经 ExecutionFramework 的异常映射。</summary>
    public static class ThrowingHostLibrary
    {
        public static void RegisterAll(ITrConfigRegistry registry)
            => registry.Register("sample_boom", (Func<double, double>)(a => throw new InvalidOperationException("boom-fx")), "抛异常样例");
    }
}
