using System.Threading;
using System.Threading.Tasks;
using TConfig;
using UiBackend.Html;

namespace UiBackend.Tests;

[TestClass]
public class TrConfigFunctionInvokerTests
{
    // ---- 最小 ITrConfigRegistry / ITrConfig / IParamConfig 桩（仅实现调用链所需成员）----

    private sealed class FakeRegistry : ITrConfigRegistry
    {
        private readonly Dictionary<string, ITrConfig> _map = new(StringComparer.OrdinalIgnoreCase);
        public void Register(string name, ITrConfig config) => _map[name] = config;
        public bool TryGetConfig(string name, out ITrConfig config) => _map.TryGetValue(name, out config!);
        public void Register(string name, Delegate function, string? description = null) => throw new NotSupportedException();
        public void Register(string name, Delegate function, IReadOnlyList<IParamConfig> parameters, string? description = null) => throw new NotSupportedException();
        public void Unregister(string name) => _map.Remove(name);
        public bool Contains(string name) => _map.ContainsKey(name);
        public IEnumerable<string> GetRegisteredNames() => _map.Keys;
        public void Clear() => _map.Clear();
        public Delegate? Find(string functionName) => null;
        public bool TryGetDescriptor(string name, out IFunctionDescriptor? descriptor) { descriptor = null; return false; }
        public bool TryGetDelegate(string name, out Delegate? function) { function = null; return false; }
    }

    private sealed class FakeTrConfig : ITrConfig
    {
        private readonly Func<object?[], object?> _func;
        private readonly IParamConfig[] _params;
        public FakeTrConfig(string name, bool isAsync, Func<object?[], object?> func, params IParamConfig[] ps)
        {
            Name = name;
            IsAsync = isAsync;
            _func = func;
            _params = ps;
        }
        public string Name { get; }
        public string Description => string.Empty;
        public Type InputType => typeof(object);
        public Type OutputType => typeof(object);
        public Type ReturnType => typeof(object);
        public bool IsAsync { get; }
        public IReadOnlyList<IParamConfig> Parameters => _params;
        public object? Invoke(params object?[] args) => _func(args);
    }

    private sealed class P : IParamConfig
    {
        public P(string name, Type type, bool isOptional = false, object? defaultValue = null)
        {
            Name = name; Type = type; IsOptional = isOptional; DefaultValue = defaultValue;
        }
        public string Name { get; }
        public Type Type { get; }
        public bool IsOptional { get; }
        public object? DefaultValue { get; }
    }

    // ---- 用例 ----

    [TestMethod]
    public async Task InvokeAsync_ConvertsStringArgs_ToDeclaredTypes()
    {
        var registry = new FakeRegistry();
        registry.Register("Sum", new FakeTrConfig("Sum", false,
            args => ((int)args[0]!) + ((int)args[1]!),
            new P("a", typeof(int)), new P("b", typeof(int))));
        var invoker = new TrConfigFunctionInvoker(registry);

        var result = await invoker.InvokeAsync("Sum", new Dictionary<string, object?> { ["a"] = "10", ["b"] = "32" });

        Assert.AreEqual("42", result);
    }

    [TestMethod]
    public async Task InvokeAsync_CancellationToken_IsBackfilled()
    {
        CancellationToken? received = null;
        var registry = new FakeRegistry();
        registry.Register("Probe", new FakeTrConfig("Probe", false,
            args => { received = (CancellationToken)args[0]!; return "ok"; },
            new P("ct", typeof(CancellationToken))));
        var invoker = new TrConfigFunctionInvoker(registry);

        var result = await invoker.InvokeAsync("Probe", new Dictionary<string, object?>());

        Assert.AreEqual("ok", result);
        Assert.IsTrue(received.HasValue);
        Assert.IsFalse(received.Value.IsCancellationRequested);
    }

    [TestMethod]
    public async Task InvokeAsync_AsyncTask_UnwrapsResultToString()
    {
        var registry = new FakeRegistry();
        registry.Register("Echo", new FakeTrConfig("Echo", true,
            args => Task.FromResult("hello"),
            new P("text", typeof(string))));
        var invoker = new TrConfigFunctionInvoker(registry);

        var result = await invoker.InvokeAsync("Echo", new Dictionary<string, object?> { ["text"] = "x" });

        Assert.AreEqual("hello", result);
    }

    [TestMethod]
    public async Task InvokeAsync_Unregistered_ReturnsDiagnostic()
    {
        var invoker = new TrConfigFunctionInvoker(new FakeRegistry());

        var result = await invoker.InvokeAsync("Nope", new Dictionary<string, object?>());

        StringAssert.Contains(result, "函数未注册");
        StringAssert.Contains(result, "Nope");
    }

    [TestMethod]
    public async Task InvokeAsync_ThrowingFunction_ReturnsDiagnostic_NotThrows()
    {
        var registry = new FakeRegistry();
        registry.Register("Boom", new FakeTrConfig("Boom", false,
            args => throw new InvalidOperationException("内幕")));
        var invoker = new TrConfigFunctionInvoker(registry);

        var result = await invoker.InvokeAsync("Boom", new Dictionary<string, object?>());

        StringAssert.Contains(result, "函数执行失败");
        StringAssert.Contains(result, "内幕");
    }

    [TestMethod]
    public async Task InvokeAsync_OptionalParam_DefaultsWhenAbsent()
    {
        var registry = new FakeRegistry();
        registry.Register("Greet", new FakeTrConfig("Greet", false,
            args => $"hi-{(args[0] ?? "n/a")}",
            new P("name", typeof(string), isOptional: true, defaultValue: "guest")));
        var invoker = new TrConfigFunctionInvoker(registry);

        var result = await invoker.InvokeAsync("Greet", new Dictionary<string, object?>());

        Assert.AreEqual("hi-guest", result);
    }

    [TestMethod]
    public async Task ToBridgeInvoker_AdaptsToHtmlFunctionInvoker()
    {
        var registry = new FakeRegistry();
        registry.Register("Echo", new FakeTrConfig("Echo", false, args => $"v:{args[0]}", new P("x", typeof(string))));
        var bridgeInvoker = TrConfigFunctionInvoker.ToBridgeInvoker(registry);

        var result = await bridgeInvoker("Echo", new Dictionary<string, object?> { ["x"] = "z" });

        Assert.AreEqual("v:z", result);
    }
}
