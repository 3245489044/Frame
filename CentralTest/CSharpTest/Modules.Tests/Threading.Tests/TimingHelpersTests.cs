using Modules.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Modules.Threading.Tests;

[TestClass]
public class TimingHelpersTests
{
    [TestMethod]
    public async Task PollUntilAsync_ImmediateTrue_ReturnsTrue()
    {
        var budget = TimeBudget.From(TimeSpan.FromSeconds(5));
        var ok = await TimingHelpers.PollUntilAsync(
            () => true,
            budget,
            TimeSpan.FromMilliseconds(10));

        Assert.IsTrue(ok);
    }

    [TestMethod]
    public async Task PollUntilAsync_BecomesTrue_ReturnsTrue()
    {
        var calls = 0;
        var budget = TimeBudget.From(TimeSpan.FromSeconds(5));
        var ok = await TimingHelpers.PollUntilAsync(
            () => ++calls >= 2,
            budget,
            TimeSpan.FromMilliseconds(10));

        Assert.IsTrue(ok);
        Assert.IsTrue(calls >= 2);
    }

    [TestMethod]
    public async Task PollUntilAsync_BudgetExhausted_ReturnsFalse()
    {
        var budget = TimeBudget.From(TimeSpan.FromMilliseconds(20));
        var ok = await TimingHelpers.PollUntilAsync(
            () => false,
            budget,
            TimeSpan.FromMilliseconds(5));

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public async Task DelayAsync_Cancellation_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(
            () => TimingHelpers.DelayAsync(TimeSpan.FromMilliseconds(100), cts.Token));
    }
}
