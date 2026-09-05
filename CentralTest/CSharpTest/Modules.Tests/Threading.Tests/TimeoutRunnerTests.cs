using Modules.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Modules.Threading.Tests;

[TestClass]
public class TimeoutRunnerTests
{
    [TestMethod]
    public async Task RunAsync_Completed_ReturnsValue()
    {
        var budget = TimeBudget.From(TimeSpan.FromSeconds(5));
        var result = await TimeoutRunner.RunAsync(
            "ok",
            ct => Task.FromResult(42),
            budget);

        Assert.AreEqual(TimeoutOutcome.Completed, result.Outcome);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(42, result.Value);
    }

    [TestMethod]
    public async Task RunAsync_TimedOut_WhenBudgetExhausted()
    {
        var budget = TimeBudget.From(TimeSpan.FromMilliseconds(20));
        var result = await TimeoutRunner.RunAsync(
            "slow",
            ct => Task.Delay(200, ct).ContinueWith(_ => 1, ct),
            budget);

        Assert.AreEqual(TimeoutOutcome.TimedOut, result.Outcome);
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(result.Error);
        StringAssert.Contains(result.Error!.Message, "超过预算");
    }

    [TestMethod]
    public async Task RunAsync_Cancelled_WhenExternalTokenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var budget = TimeBudget.From(TimeSpan.FromSeconds(30));
        var result = await TimeoutRunner.RunAsync(
            "cancelled",
            ct => Task.Delay(200, ct).ContinueWith(_ => 1, ct),
            budget,
            cts.Token);

        Assert.AreEqual(TimeoutOutcome.Cancelled, result.Outcome);
        // 外部取消优先于预算耗尽（归因修正）。
        Assert.AreNotEqual(TimeoutOutcome.TimedOut, result.Outcome);
    }

    [TestMethod]
    public async Task RunAsync_Failed_WhenWorkThrows()
    {
        var budget = TimeBudget.From(TimeSpan.FromSeconds(5));
        var result = await TimeoutRunner.RunAsync(
            "boom",
            ct => Task.FromException<int>(new InvalidOperationException("boom")),
            budget);

        Assert.AreEqual(TimeoutOutcome.Failed, result.Outcome);
        Assert.IsNotNull(result.Error);
    }
}
