using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.Observe;

namespace Modules.Observe.Tests;

[TestClass]
public sealed class ObserveSessionManagerTests
{
    private static Task<ObserveOutcome> WriteAsync(IObserveCoordinator c, string key, string value, WriteOptions? options = null)
        => c.WriteAsync(key, value, options, default);

    [TestMethod]
    public async Task WriteAsync_ConvergedAck_ReturnsCommittedWithHighestAckConverged()
    {
        using var transport = new InMemoryTransport(); // SendAccepted/Applied/Converged 默认全开
        using var coordinator = new ObserveSessionManager(transport);

        var outcome = await WriteAsync(coordinator, "k1", "v1");

        Assert.AreEqual(ChangeResult.Committed, outcome.Result);
        Assert.AreEqual(AckLevel.Converged, outcome.HighestAck);
        Assert.AreEqual("k1", outcome.Key);
        Assert.IsTrue(outcome.Version >= 1);
        Assert.IsTrue(outcome.Window >= TimeSpan.Zero);
        Assert.IsFalse(string.IsNullOrWhiteSpace(outcome.ChangeId));
    }

    [TestMethod]
    public async Task WriteAsync_VersionsAreMonotonic()
    {
        using var transport = new InMemoryTransport();
        using var coordinator = new ObserveSessionManager(transport);

        var a = await WriteAsync(coordinator, "k1", "v1");
        var b = await WriteAsync(coordinator, "k1", "v2");

        Assert.IsTrue(b.Version > a.Version);
    }

    [TestMethod]
    public async Task WriteAsync_NoConvergedWithinTimeout_ReturnsDegraded()
    {
        using var transport = new InMemoryTransport { SendConverged = false }; // 只回 Applied
        using var coordinator = new ObserveSessionManager(transport);

        var outcome = await WriteAsync(coordinator, "k1", "v1",
            new WriteOptions(BarrierMode.Timeout, WindowTimeout: TimeSpan.FromMilliseconds(80)));

        Assert.AreEqual(ChangeResult.Degraded, outcome.Result);
        Assert.AreEqual(AckLevel.Applied, outcome.HighestAck);
    }

    [TestMethod]
    public async Task WriteAsync_NoAckAtAllWithinTimeout_ReturnsDegraded()
    {
        using var transport = new InMemoryTransport { Acknowledge = false }; // 黑盒，完全不回执
        using var coordinator = new ObserveSessionManager(transport);

        var outcome = await WriteAsync(coordinator, "k1", "v1",
            new WriteOptions(BarrierMode.Timeout, WindowTimeout: TimeSpan.FromMilliseconds(60)));

        Assert.AreEqual(ChangeResult.Degraded, outcome.Result);
    }

    [TestMethod]
    public async Task WriteAsync_FireAndForget_ReturnsImmediatelyWithoutWaitingConvergence()
    {
        // 永不收敛的传输（不回执）：FireAndForget 仍立即返回，HighestAck 保持投递初值 Accepted，证明不等待收敛。
        using var transport = new InMemoryTransport { Acknowledge = false };
        using var coordinator = new ObserveSessionManager(transport);

        var outcome = await WriteAsync(coordinator, "k1", "v1", new WriteOptions(BarrierMode.FireAndForget));

        Assert.AreEqual(ChangeResult.Committed, outcome.Result); // 已投递，不承诺收敛
        Assert.AreEqual(AckLevel.Accepted, outcome.HighestAck);
    }

    [TestMethod]
    public async Task WriteAsync_StrictWaitsForConvergedEvenIfAppliedLate()
    {
        using var transport = new InMemoryTransport { AckDelay = TimeSpan.FromMilliseconds(40) };
        using var coordinator = new ObserveSessionManager(transport);

        // Strict 不因窗口超时降级，等收敛
        var outcome = await WriteAsync(coordinator, "k1", "v1", new WriteOptions(BarrierMode.Strict));

        Assert.AreEqual(ChangeResult.Committed, outcome.Result);
        Assert.AreEqual(AckLevel.Converged, outcome.HighestAck);
        Assert.IsTrue(outcome.Window >= TimeSpan.FromMilliseconds(30));
    }

    [TestMethod]
    public async Task WriteAsync_CancellationBeforeConverge_ReturnsFailed()
    {
        using var transport = new InMemoryTransport { Acknowledge = false };
        using var coordinator = new ObserveSessionManager(transport);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var outcome = await coordinator.WriteAsync("k1", "v1", new WriteOptions(BarrierMode.Strict), cts.Token);

        Assert.AreEqual(ChangeResult.Failed, outcome.Result);
    }
}
