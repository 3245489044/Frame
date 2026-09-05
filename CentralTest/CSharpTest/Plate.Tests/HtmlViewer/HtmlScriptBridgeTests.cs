using Plate.HtmlViewer;
using UiBackend.Html;
using Modules.Observe;

namespace UiBackend.Tests;

[TestClass]
public class HtmlScriptBridgeTests
{
    private sealed class FakeBridge : IHtmlChannel
    {
        public List<(string Op, string Name, string? Value)> Calls { get; } = new();

        // 可配置 click 桥返回（默认 "OK"；可设为 JSON 字符串字面量 "\"NOT_FOUND\"" 以验证真实命中解包）。
        public string ClickResult { get; set; } = "OK";

        public Task RenderHtmlAsync(string html, CancellationToken ct = default) => Task.CompletedTask;

        public Task<ObserveOutcome> WriteAsync(string html, WriteOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new ObserveOutcome(Guid.NewGuid().ToString("N"), "html:document", 1, AckLevel.Converged, TimeSpan.Zero, ChangeResult.Committed));

        public event EventHandler<HtmlMessageReceivedEventArgs>? MessageReceived { add { } remove { } }
        public Task<bool> IsBridgeReadyAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> LoopbackProbeAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<string> ExecuteAsync(string op, string name, string? value, CancellationToken ct = default)
        {
            Calls.Add((op, name, value));
            return Task.FromResult(op == "get" ? "\"hi\"" : op == "click" ? ClickResult : "OK");
        }

        public Task<string> ExecuteScriptAsync(string javaScript, CancellationToken ct = default)
            => Task.FromResult("OK");

        public Task<string> InvokeEventAsync(string elementName, string eventName, string? payload)
        {
            Calls.Add(("event", elementName, eventName));
            return Task.FromResult("OK");
        }
    }

    private static HtmlScriptBridge NewBridge(FakeBridge bridge, List<(string Name, IReadOnlyDictionary<string, object?> Params)> invoked)
    {
        return new HtmlScriptBridge(bridge, (name, parameters) =>
        {
            invoked.Add((name, parameters));
            return Task.FromResult("exec-result");
        });
    }

    [TestMethod]
    public async Task ExecuteAsync_SetSendsDomWrite()
    {
        var bridge = new FakeBridge();
        var script = new HtmlScriptBridge(bridge, (n, p) => Task.FromResult(""));

        var result = await script.ExecuteAsync(new[]
        {
            new HtmlScriptCommand { Action = "set", Target = "inputBox", Value = "10" }
        });

        Assert.AreEqual(("set", "inputBox", "10"), bridge.Calls[0]);
        StringAssert.Contains(result.ToText(), "[set] inputBox = 10");
    }

    [TestMethod]
    public async Task ExecuteAsync_GetReadsBackAndUnwraps()
    {
        var bridge = new FakeBridge();
        var script = new HtmlScriptBridge(bridge, (n, p) => Task.FromResult(""));

        var result = await script.ExecuteAsync(new[]
        {
            new HtmlScriptCommand { Action = "get", Target = "inputBox" }
        });

        Assert.AreEqual(("get", "inputBox", (string?)null), bridge.Calls[0]);
        StringAssert.Contains(result.ToText(), "= hi");
    }

    [TestMethod]
    public async Task ExecuteAsync_ClickDispatchesEvent()
    {
        var bridge = new FakeBridge();
        var script = new HtmlScriptBridge(bridge, (n, p) => Task.FromResult(""));

        var result = await script.ExecuteAsync(new[]
        {
            new HtmlScriptCommand { Action = "click", Target = "sendButton" }
        });

        Assert.AreEqual(("click", "sendButton", (string?)null), bridge.Calls[0]);
        StringAssert.Contains(result.ToText(), "[click] sendButton");
        // 改动 A：click 返回桥的真实命中结果（不再无条件成功）；默认桥返回 "OK" → 输出带 → OK。
        StringAssert.Contains(result.ToText(), "→ OK");
    }

    [TestMethod]
    public async Task ExecuteAsync_ClickNotFound_ReportsHitInsteadOfFalseSuccess()
    {
        var bridge = new FakeBridge { ClickResult = "\"NOT_FOUND\"" };
        var script = new HtmlScriptBridge(bridge, (n, p) => Task.FromResult(""));

        var result = await script.ExecuteAsync(new[]
        {
            new HtmlScriptCommand { Action = "click", Target = "viewType-node" }
        });

        // 真实桥 ExecuteScriptAsync 返回 JSON 字符串字面量（"NOT_FOUND"），UnwrapString 解包为 NOT_FOUND —— 暴露假阳性，
        // 不再无条件 "[click] viewType-node" 假成功。
        StringAssert.Contains(result.ToText(), "[click] viewType-node → NOT_FOUND");
    }

    [TestMethod]
    public async Task ExecuteAsync_ExecInvokesFunction_AndWritesBackOutput()
    {
        var bridge = new FakeBridge();
        var invoked = new List<(string Name, IReadOnlyDictionary<string, object?> Params)>();
        var script = NewBridge(bridge, invoked);

        var result = await script.ExecuteAsync(new[]
        {
            new HtmlScriptCommand { Action = "exec", Target = "Ui_ListCatalogTree", Value = "keyword=ReLU", Output = "resultLabel" }
        });

        Assert.AreEqual(1, invoked.Count);
        Assert.AreEqual("Ui_ListCatalogTree", invoked[0].Name);
        Assert.AreEqual("ReLU", invoked[0].Params["keyword"]);
        // 结果写回 output 元素
        Assert.IsTrue(bridge.Calls.Any(c => c.Op == "set" && c.Name == "resultLabel" && c.Value == "exec-result"));
        StringAssert.Contains(result.ToText(), "exec-result");
    }

    [TestMethod]
    public async Task ExecuteAsync_UnknownAction_ReturnsWhitelistDiagnostic()
    {
        var bridge = new FakeBridge();
        var script = new HtmlScriptBridge(bridge, (n, p) => Task.FromResult(""));

        var result = await script.ExecuteAsync(new[]
        {
            new HtmlScriptCommand { Action = "delete", Target = "x" }
        });

        StringAssert.Contains(result.ToText(), "不支持");
        StringAssert.Contains(result.ToText(), "白名单");
    }

    [TestMethod]
    public void ParseKeyValues_SplitsByEqualsAndSpace()
    {
        var dict = HtmlScriptBridge.ParseKeyValues("keyword=ReLU paramStr=\"a b\" flag=true");

        Assert.AreEqual("ReLU", dict["keyword"]);
        Assert.AreEqual("a b", dict["paramStr"]);
        Assert.AreEqual("true", dict["flag"]);
    }

    [TestMethod]
    public void ParseKeyValues_NullOrEmpty_ReturnsEmpty()
    {
        Assert.AreEqual(0, HtmlScriptBridge.ParseKeyValues(null).Count);
        Assert.AreEqual(0, HtmlScriptBridge.ParseKeyValues("  ").Count);
    }

    [TestMethod]
    public void ParseCommands_ExpandsEchoSectionTemplate()
    {
        var cmds = HtmlScriptBridge.ParseCommands(
            "{\"commands\":[{\"template\":\"echo-section\",\"title\":\"视图切换\"},{\"action\":\"click\",\"target\":\"viewType-node\"}]}");

        Assert.AreEqual(2, cmds.Count);
        Assert.AreEqual("echo", cmds[0].Action);
        Assert.AreEqual("视图切换", cmds[0].Value);
        Assert.AreEqual("click", cmds[1].Action);
        Assert.AreEqual("viewType-node", cmds[1].Target);
    }

    [TestMethod]
    public void ParseCommands_ExpandsNamedTemplateToAction()
    {
        var cmds = HtmlScriptBridge.ParseCommands("{\"commands\":[{\"template\":\"do-something\",\"target\":\"x\"}]}");

        Assert.AreEqual(1, cmds.Count);
        Assert.AreEqual("do-something", cmds[0].Action);
        Assert.AreEqual("x", cmds[0].Target);
    }

    [TestMethod]
    public void ParseCommands_NonTemplateCommands_Untouched()
    {
        var cmds = HtmlScriptBridge.ParseCommands("{\"commands\":[{\"action\":\"set\",\"target\":\"t\",\"value\":\"v\"}]}");

        Assert.AreEqual(1, cmds.Count);
        Assert.AreEqual("set", cmds[0].Action);
        Assert.AreEqual("t", cmds[0].Target);
        Assert.AreEqual("v", cmds[0].Value);
    }

    [TestMethod]
    public void BridgeScript_JavaScriptBraceBalance_IsBalanced()
    {
        // 防回归：BridgeScript 是 JS 字符串，曾因「__domBridge 对象字面量少闭合一个 }」导致整段脚本无法被 JS 解析、
        // 点击桥永久缺失（__domBridgeInstalled=false → data-cmd 点击无效、postMessage 永不发出）。此处做括号配平检查，
        // 捕获「少/多一个 { } ( ) [ ]」这类结构回归（BridgeScript 无正则字面量/注释/模板串，跳过单引号字符串即可）。
        var script = UiBackend.Html.HtmlAssetRegistry.GetScript(UiBackend.Html.HtmlAssetKeys.BridgeScript);
        Assert.IsFalse(string.IsNullOrWhiteSpace(script), "BridgeScript 不应为空");

        var stack = new Stack<char>();
        bool inStr = false;
        for (var i = 0; i < script.Length; i++)
        {
            var c = script[i];
            if (c == '\'') { inStr = !inStr; continue; }
            if (inStr) continue;
            if (c is '{' or '(' or '[') { stack.Push(c); continue; }
            if (c is not ('}' or ')' or ']')) continue;
            Assert.IsTrue(stack.Count > 0, $"BridgeScript 多余右括号 '{c}' @{i}");
            var open = stack.Pop();
            var match = (open == '{' && c == '}') || (open == '(' && c == ')') || (open == '[' && c == ']');
            Assert.IsTrue(match, $"BridgeScript 括号不匹配 '{open}' 与 '{c}' @{i}");
        }
        Assert.AreEqual(0, stack.Count, $"BridgeScript 存在 {stack.Count} 个未闭合括号");
    }
}
