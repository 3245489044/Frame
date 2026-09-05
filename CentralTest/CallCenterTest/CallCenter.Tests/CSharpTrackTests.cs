using CallCenter.Core.Discovery;
using CallCenter.Core.Tools;
using CallCenter.Core.Tracks;

namespace CallCenter.Tests;

/// <summary>锁定 CSharpTrack（Runtime 轨）：索引发现定义、JSON 入参 → 真实执行 → JSON 出参。</summary>
[TestClass]
public class CSharpTrackTests
{
    private static CSharpTrack BuildTrack()
        => new(CSharpFunctionLibrarySource.FromLibraryTypes(new[] { typeof(CSharpFunctionLibrarySourceTests.SampleHostLibrary) }).DiscoverDefinitions());

    [TestMethod]
    public async Task Call_Adds_Two_Doubles_Returns_Json()
    {
        var track = BuildTrack();
        var result = await track.CallAsync("sample_add", "{\"a\":2,\"b\":3}");

        Assert.IsTrue(result.Success);
        Assert.AreEqual("5", result.OutputJson);
    }

    [TestMethod]
    public async Task Call_Echo_Returns_Quoted_Json_String()
    {
        var track = BuildTrack();
        Assert.IsTrue(track.CanCall("sample_echo"));
        var result = await track.CallAsync("sample_echo", "{\"text\":\"hi\"}");

        Assert.IsTrue(result.Success);
        Assert.AreEqual("\"hi\"", result.OutputJson);
    }

    [TestMethod]
    public async Task Call_Missing_Required_Arg_Fails()
    {
        var track = BuildTrack();
        var result = await track.CallAsync("sample_add", "{}");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "缺少参数");
    }

    [TestMethod]
    public void CanCall_Only_For_Discovered_Executable_Tools()
    {
        var track = BuildTrack();
        Assert.IsTrue(track.CanCall("sample_add"));
        Assert.IsFalse(track.CanCall("does_not_exist"));
        Assert.IsFalse(track.CanCall("read_nodes"), "无 delegate 的种子能力不可经 C#Track 调用");
    }

    [TestMethod]
    public void Backend_Is_CSharp()
        => Assert.AreEqual(ToolBackend.CSharp, BuildTrack().Backend);

    [TestMethod]
    public async Task Custom_Invoker_Is_Used_Instead_Of_Direct()
    {
        // 阶段 A 执行缝：宿主注入自定义执行后端后，CSharpTrack 必须走它（而非缺省 DirectDelegateInvoker）。
        var recorder = new RecorderInvoker();
        var track = new CSharpTrack(
            CSharpFunctionLibrarySource.FromLibraryTypes(new[] { typeof(CSharpFunctionLibrarySourceTests.SampleHostLibrary) })
                .DiscoverDefinitions(),
            recorder);

        var result = await track.CallAsync("sample_add", "{\"a\":2,\"b\":3}");

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, recorder.Calls, "自定义执行后端应被调用一次（证明非缺省直调路径）");
        Assert.AreEqual("custom-ok", result.OutputJson);
        Assert.IsNotNull(recorder.LastArguments, "入参绑定应由 CSharpTrack 完成后交给执行后端");
        Assert.AreEqual(2d, recorder.LastArguments[0]);
        Assert.AreEqual(3d, recorder.LastArguments[1]);
    }

    [TestMethod]
    public async Task Default_Track_Falls_Back_To_Direct_Invoker()
    {
        // 未注入时构造即用 DirectDelegateInvoker.Instance，行为与原生直调一致（Name 可观测）。
        var defs = CSharpFunctionLibrarySource.FromLibraryTypes(new[] { typeof(CSharpFunctionLibrarySourceTests.SampleHostLibrary) })
            .DiscoverDefinitions();
        var track = new CSharpTrack(defs);
        var result = await track.CallAsync("sample_echo", "{\"text\":\"hi\"}");

        Assert.IsTrue(result.Success);
        Assert.AreEqual("\"hi\"", result.OutputJson);
    }

    [TestMethod]
    public async Task Direct_Invoker_Executes_And_Serializes_Result()
    {
        Delegate f = (Func<double, double, double>)((a, b) => a + b);

        var result = await DirectDelegateInvoker.Instance.InvokeAsync(f, new object?[] { 2d, 3d });

        Assert.IsTrue(result.Success);
        Assert.AreEqual("5", result.OutputJson);
    }

    [TestMethod]
    public async Task Direct_Invoker_Maps_Exception_To_Fail()
    {
        Delegate f = (Func<double, double>)(a => throw new InvalidOperationException("boom"));

        var result = await DirectDelegateInvoker.Instance.InvokeAsync(f, new object?[] { 1d });

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "boom");
    }

    /// <summary>测试用执行后端：记录一次调用与绑定后的位置实参，返回固定标记，用于证明 CSharpTrack 走了注入后端。</summary>
    private sealed class RecorderInvoker : ICSharpDelegateInvoker
    {
        public string Name => "test.recorder";

        public int Calls { get; private set; }

        public object?[]? LastArguments { get; private set; }

        public Task<ToolExecutionResult> InvokeAsync(Delegate function, object?[] arguments, CancellationToken ct = default)
        {
            Calls++;
            LastArguments = arguments;
            return Task.FromResult(ToolExecutionResult.Ok("custom-ok"));
        }
    }
}
