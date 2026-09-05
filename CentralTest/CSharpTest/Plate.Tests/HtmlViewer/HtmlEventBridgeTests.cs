using TConfig;
using Plate.HtmlViewer;
using UiBackend.Html;
using Modules.Observe;

namespace UiBackend.Tests;

[TestClass]
public class HtmlEventBridgeTests
{
    private sealed class FakeBridge : IHtmlChannel
    {
        public List<(string Op, string Name, string? Value)> Calls { get; } = new();

        public Task RenderHtmlAsync(string html, CancellationToken ct = default) => Task.CompletedTask;

        public Task<ObserveOutcome> WriteAsync(string html, WriteOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new ObserveOutcome(Guid.NewGuid().ToString("N"), "html:document", 1, AckLevel.Converged, TimeSpan.Zero, ChangeResult.Committed));

        public event EventHandler<HtmlMessageReceivedEventArgs>? MessageReceived { add { } remove { } }
        public Task<bool> IsBridgeReadyAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> LoopbackProbeAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<string> ExecuteAsync(string op, string name, string? value, CancellationToken ct = default)
        {
            Calls.Add((op, name, value));
            // get 返回 JSON 字符串字面量（元素名即其值，供断言装配结果）
            if (op == "get") return Task.FromResult($"\"{name}\"");
            return Task.FromResult("OK");
        }

        public Task<string> ExecuteScriptAsync(string javaScript, CancellationToken ct = default)
            => Task.FromResult("OK");

        public Task<string> InvokeEventAsync(string elementName, string eventName, string? payload)
        {
            Calls.Add(("event", elementName, eventName));
            return Task.FromResult("OK");
        }
    }

    private static ControlConfig HostViewerWithMessageBinding()
    {
        var host = new ControlConfig { Type = "htmlviewer", Name = "conversationHistoryViewer" };
        host.Events = new EventConfig();
        host.Events.EventsDictionary["message"] = new List<string> { "AgentUi_ConversationMessageDispatch" };
        host.Events.Bindings = new EventBinding
        {
            Parameters = new List<ParameterBinding>
            {
                new() { Target = "payload", Source = "conversationHistoryViewer", SourceType = "eventPayload" }
            },
            Output = "conversationHistoryViewer"
        };
        return host;
    }

    private static ControlConfig ButtonWithBindings()
    {
        var button = new ControlConfig { Type = "button", Name = "sendButton", Text = "发送" };
        button.Events = new EventConfig();
        button.Events.EventsDictionary["click"] = new List<string> { "AgentUi_Send" };
        button.Events.Bindings = new EventBinding
        {
            Parameters = new List<ParameterBinding>
            {
                new() { Target = "sessionName", Source = "default", SourceType = "literal" },
                new() { Target = "text", Source = "inputBox", SourceType = "control" }
            },
            Output = "resultLabel"
        };
        return button;
    }

    [TestMethod]
    public async Task HandleAsync_AssemblesParameters_CallsFunction_WritesBackOutput()
    {
        var bridge = new FakeBridge();
        var button = ButtonWithBindings();
        var declaration = new Dictionary<string, ControlConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["sendButton"] = button,
            ["inputBox"] = new ControlConfig { Type = "textbox", Name = "inputBox" }
        };

        (string FuncName, IReadOnlyDictionary<string, object?> Params)? invoked = null;
        var invoker = new HtmlEventBridge.HtmlFunctionInvoker((name, parameters) =>
        {
            invoked = (name, parameters);
            return Task.FromResult("已发送: hello");
        });
        var eventBridge = new HtmlEventBridge(declaration, bridge, invoker);

        var result = await eventBridge.HandleAsync("{\"event\":\"click\",\"name\":\"sendButton\",\"payload\":\"p\"}");

        Assert.AreEqual("已发送: hello", result);
        Assert.IsNotNull(invoked);
        Assert.AreEqual("AgentUi_Send", invoked!.Value.FuncName);
        Assert.AreEqual("default", invoked.Value.Params["sessionName"]);
        Assert.AreEqual("inputBox", invoked.Value.Params["text"]);
        // 结果写回 output 元素
        Assert.IsTrue(bridge.Calls.Any(c => c.Op == "set" && c.Name == "resultLabel" && c.Value == "已发送: hello"));
    }

    [TestMethod]
    public async Task HandleAsync_UnknownControl_ReturnsNotFound()
    {
        var bridge = new FakeBridge();
        var eventBridge = new HtmlEventBridge(
            new Dictionary<string, ControlConfig>(StringComparer.OrdinalIgnoreCase),
            bridge,
            (name, p) => Task.FromResult(""));

        var result = await eventBridge.HandleAsync("{\"event\":\"click\",\"name\":\"missing\"}");

        StringAssert.Contains(result, "未找到控件");
    }

    [TestMethod]
    public async Task HandleAsync_NoBinding_ReturnsDiagnostic()
    {
        var bridge = new FakeBridge();
        var button = new ControlConfig { Type = "button", Name = "plain" };
        var eventBridge = new HtmlEventBridge(
            new Dictionary<string, ControlConfig>(StringComparer.OrdinalIgnoreCase) { ["plain"] = button },
            bridge,
            (name, p) => Task.FromResult(""));

        var result = await eventBridge.HandleAsync("{\"event\":\"click\",\"name\":\"plain\"}");

        StringAssert.Contains(result, "未声明事件绑定");
    }

    [TestMethod]
    public void ParsePayload_ExtractsEventNameNameAndPayload()
    {
        var (evt, name, payload) = HtmlEventBridge.ParsePayload(
            "{\"event\":\"taskSelected\",\"name\":\"taskList\",\"payload\":\"sess1\"}");

        Assert.AreEqual("taskSelected", evt);
        Assert.AreEqual("taskList", name);
        Assert.AreEqual("sess1", payload);
    }

    [TestMethod]
    public void ParsePayload_NonObjectPayload_FallsBackToRaw()
    {
        var (evt, name, payload) = HtmlEventBridge.ParsePayload("not-json");

        Assert.AreEqual(string.Empty, evt);
        Assert.IsNull(name);
        Assert.AreEqual("not-json", payload);
    }

    [TestMethod]
    public async Task HandleHostMessageAsync_AssemblesEventPayload_WritesToHostWriter()
    {
        var bridge = new FakeBridge();
        var declaration = new Dictionary<string, ControlConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["conversationHistoryViewer"] = HostViewerWithMessageBinding()
        };

        (string FuncName, IReadOnlyDictionary<string, object?> Params)? invoked = null;
        var invoker = new HtmlEventBridge.HtmlFunctionInvoker((name, parameters) =>
        {
            invoked = (name, parameters);
            return Task.FromResult("<div>渲染结果</div>");
        });
        var eventBridge = new HtmlEventBridge(declaration, bridge, invoker);

        var hostWritten = new List<string>();
        eventBridge.RegisterHostWriter("conversationHistoryViewer",
            html => { hostWritten.Add(html); return Task.CompletedTask; });

        var payload = "{\"cmd\":\"selectType\",\"type\":\"node\"}";
        var result = await eventBridge.HandleHostMessageAsync("conversationHistoryViewer", payload);

        Assert.AreEqual("<div>渲染结果</div>", result);
        Assert.IsNotNull(invoked);
        Assert.AreEqual("AgentUi_ConversationMessageDispatch", invoked!.Value.FuncName);
        Assert.AreEqual(payload, invoked.Value.Params["payload"]);
        // output 双通道：命中宿主写回端口 → SetHtmlAsync（而非 DOM set）
        CollectionAssert.AreEqual(new[] { "<div>渲染结果</div>" }, hostWritten);
        Assert.IsFalse(bridge.Calls.Any(c => c.Op == "set"));
    }

    [TestMethod]
    public async Task HandleHostMessageAsync_UnknownHost_ReturnsNotFound()
    {
        var eventBridge = new HtmlEventBridge(
            new Dictionary<string, ControlConfig>(StringComparer.OrdinalIgnoreCase),
            new FakeBridge(),
            (name, p) => Task.FromResult(""));

        var result = await eventBridge.HandleHostMessageAsync("missing", "{}");

        StringAssert.Contains(result, "未找到控件");
    }

    [TestMethod]
    public async Task HandleHostMessageAsync_NoMessageEvent_ReturnsDiagnostic()
    {
        var plain = new ControlConfig { Type = "htmlviewer", Name = "viewer" };
        var eventBridge = new HtmlEventBridge(
            new Dictionary<string, ControlConfig>(StringComparer.OrdinalIgnoreCase) { ["viewer"] = plain },
            new FakeBridge(),
            (name, p) => Task.FromResult(""));

        var result = await eventBridge.HandleHostMessageAsync("viewer", "{}");

        StringAssert.Contains(result, "未声明事件绑定");
    }

    [TestMethod]
    public async Task HandleHostMessageAsync_NoHostWriter_FallsBackToDomSet()
    {
        var bridge = new FakeBridge();
        var host = HostViewerWithMessageBinding();
        host.Events!.Bindings!.Output = "resultLabel"; // 指向页内元素（未注册宿主写回）
        var eventBridge = new HtmlEventBridge(
            new Dictionary<string, ControlConfig>(StringComparer.OrdinalIgnoreCase) { ["conversationHistoryViewer"] = host },
            bridge,
            (name, p) => Task.FromResult("ok"));

        await eventBridge.HandleHostMessageAsync("conversationHistoryViewer", "{}");

        Assert.IsTrue(bridge.Calls.Any(c => c.Op == "set" && c.Name == "resultLabel" && c.Value == "ok"));
    }

    [TestMethod]
    public async Task ScriptBridge_RealClick_DispatchesRealClickOp()
    {
        var bridge = new FakeBridge();
        var scriptBridge = new HtmlScriptBridge(bridge, (n, p) => Task.FromResult(""));
        var result = await scriptBridge.ExecuteAsync(new[]
        {
            new HtmlScriptCommand { Action = "realclick", Target = "viewType-node" }
        });

        StringAssert.Contains(result.ToText(), "[realclick] viewType-node → OK");
        Assert.IsTrue(bridge.Calls.Any(c => c.Op == "realclick" && c.Name == "viewType-node"));
    }

    [TestMethod]
    public async Task HandleAsync_EmitsLinkDiagnostics()
    {
        // P2 观测：事件桥链路每段（route/invoke/writeback）经 Diagnostics 出口发结构化诊断
        var bridge = new FakeBridge();
        var button = ButtonWithBindings();
        var declaration = new Dictionary<string, ControlConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["sendButton"] = button,
            ["inputBox"] = new ControlConfig { Type = "textbox", Name = "inputBox" }
        };
        var eventBridge = new HtmlEventBridge(declaration, bridge, (n, p) => Task.FromResult("ok"));
        var diag = new List<string>();
        eventBridge.Diagnostics = (category, message) => diag.Add($"{category}: {message}");

        await eventBridge.HandleAsync("{\"event\":\"click\",\"name\":\"sendButton\",\"payload\":\"p\"}");

        Assert.IsTrue(diag.Any(d => d.StartsWith("EventBridge_route:") && d.Contains("sendButton")));
        Assert.IsTrue(diag.Any(d => d.StartsWith("EventBridge_invoke:") && d.Contains("AgentUi_Send")));
        Assert.IsTrue(diag.Any(d => d.StartsWith("EventBridge_writeback:") && d.Contains("resultLabel")));
    }
}
