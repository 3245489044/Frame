using TConfig.Controls;
using Plate.HtmlViewer;
using UiBackend.Html;
using Modules.Observe;

namespace UiBackend.Tests;

[TestClass]
public class HtmlElementNodeTests
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
            return Task.FromResult(op == "get" ? "\"abc\"" : "OK");
        }

        public Task<string> ExecuteScriptAsync(string javaScript, CancellationToken ct = default)
            => Task.FromResult("OK");

        public Task<string> InvokeEventAsync(string elementName, string eventName, string? payload)
        {
            Calls.Add(("event", elementName, eventName));
            return Task.FromResult("OK");
        }
    }

    [TestMethod]
    public void GetValue_SendsGetOp_AndUnwrapsJsonString()
    {
        var bridge = new FakeBridge();
        var node = new HtmlElementNode("input", "textbox", bridge);

        var value = node.GetValue();

        Assert.AreEqual("abc", value);
        Assert.AreEqual(("get", "input", (string?)null), bridge.Calls[0]);
    }

    [TestMethod]
    public void SetValue_SendsSetOp_WithStringValue()
    {
        var bridge = new FakeBridge();
        var node = new HtmlElementNode("label", "label", bridge);

        node.SetValue(42);

        Assert.AreEqual(("set", "label", "42"), bridge.Calls[0]);
    }

    [TestMethod]
    public void Invoke_SendsEventOp()
    {
        var bridge = new FakeBridge();
        var node = new HtmlElementNode("send", "button", bridge);

        node.Invoke("click", null);

        Assert.AreEqual(("event", "send", "click"), bridge.Calls[0]);
    }

    [TestMethod]
    public void SetBounds_SendsBoundsOp_WithRectJson()
    {
        var bridge = new FakeBridge();
        var node = new HtmlElementNode("panel", "panel", bridge);

        node.SetBounds(10, 20, 100, 50);

        Assert.AreEqual("bounds", bridge.Calls[0].Op);
        Assert.AreEqual("panel", bridge.Calls[0].Name);
        StringAssert.Contains(bridge.Calls[0].Value!, "\"x\":10");
    }

    [TestMethod]
    public void DefaultSemantic_MapsTypes()
    {
        Assert.AreEqual(ValueSemantic.Text, HtmlElementNode.DefaultSemantic("textbox"));
        Assert.AreEqual(ValueSemantic.Selection, HtmlElementNode.DefaultSemantic("listview"));
        Assert.AreEqual(ValueSemantic.Selection, HtmlElementNode.DefaultSemantic("combobox"));
        Assert.AreEqual(ValueSemantic.Checked, HtmlElementNode.DefaultSemantic("checkbox"));
        Assert.AreEqual(ValueSemantic.Range, HtmlElementNode.DefaultSemantic("numericupdown"));
        Assert.AreEqual(ValueSemantic.Content, HtmlElementNode.DefaultSemantic("htmlviewer"));
    }

    [TestMethod]
    public void UnwrapString_HandlesJsonLiteral()
    {
        Assert.AreEqual("hello", HtmlElementNode.UnwrapString("\"hello\""));
        Assert.AreEqual("OK", HtmlElementNode.UnwrapString("OK"));
    }
}
