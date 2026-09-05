using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.Observe;
using Modules.Observe.Transports;
using Plate.HtmlViewer;
using UiBackend.Html;

namespace UiBackend.Tests;

[TestClass]
public sealed class WebView2TransportTests
{
    private sealed class FakeHtmlChannel : IHtmlChannel
    {
        public int RenderCount;
        public string? LastHtml;
        public bool BridgeReady = true;
        public List<string> Rendered = new();

        public Task RenderHtmlAsync(string html, CancellationToken ct = default)
        {
            RenderCount++;
            LastHtml = html;
            Rendered.Add(html);
            return Task.CompletedTask;
        }

        public Task<ObserveOutcome> WriteAsync(string html, WriteOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new ObserveOutcome(Guid.NewGuid().ToString("N"), "html:document", 1, AckLevel.Converged, TimeSpan.Zero, ChangeResult.Committed));

        public Task<string> ExecuteAsync(string op, string name, string? value, CancellationToken ct = default)
            => Task.FromResult("OK");

        public Task<string> ExecuteScriptAsync(string javaScript, CancellationToken ct = default)
            => Task.FromResult("true");

        public Task<bool> IsBridgeReadyAsync(CancellationToken ct = default)
            => Task.FromResult(BridgeReady);

        public Task<bool> LoopbackProbeAsync(CancellationToken ct = default) => Task.FromResult(true);

        public event EventHandler<HtmlMessageReceivedEventArgs>? MessageReceived { add { } remove { } }
    }

    [TestMethod]
    public async Task WriteAsync_ViaWebView2Transport_WhenBridgeReady_ReturnsCommittedConverged()
    {
        var channel = new FakeHtmlChannel { BridgeReady = true };
        using var transport = new WebView2Transport(channel);
        using var coordinator = new ObserveSessionManager(transport);

        var html = "<p>hello</p>";
        var outcome = await coordinator.WriteAsync("html:conversation", html,
            new WriteOptions(BarrierMode.Timeout, WindowTimeout: TimeSpan.FromSeconds(5)));

        Assert.AreEqual(ChangeResult.Committed, outcome.Result);
        Assert.AreEqual(AckLevel.Converged, outcome.HighestAck);
        Assert.AreEqual(1, channel.RenderCount);
        Assert.AreEqual(html, channel.LastHtml);
    }

    [TestMethod]
    public async Task WriteAsync_ViaWebView2Transport_WhenBridgeNeverReady_TimesOutDegraded()
    {
        var channel = new FakeHtmlChannel { BridgeReady = false };
        using var transport = new WebView2Transport(channel);
        using var coordinator = new ObserveSessionManager(transport);

        var outcome = await coordinator.WriteAsync("html:conversation", "<p>x</p>",
            new WriteOptions(BarrierMode.Timeout, WindowTimeout: TimeSpan.FromMilliseconds(120)));

        Assert.AreEqual(ChangeResult.Degraded, outcome.Result);
        Assert.AreEqual(AckLevel.Applied, outcome.HighestAck); // 已投递(Applied)，未收敛 → 显式降级
    }
}
