using Modules.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Modules.Threading.Tests;

[TestClass]
public class TimeBudgetTests
{
    [TestMethod]
    public void From_Remaining_Decreases_WithClockAdvance()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var budget = TimeBudget.From(TimeSpan.FromSeconds(30), clock: clock);

        Assert.AreEqual(TimeSpan.FromSeconds(30), budget.Remaining(clock));

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.AreEqual(TimeSpan.FromSeconds(20), budget.Remaining(clock));

        clock.Advance(TimeSpan.FromSeconds(25));
        Assert.AreEqual(TimeSpan.Zero, budget.Remaining(clock));
        Assert.IsTrue(budget.IsExpired(clock));
    }

    [TestMethod]
    public void RemainingAfterMargin_SubtractsMargin()
    {
        var clock = new FakeClock();
        var budget = TimeBudget.From(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), clock);

        Assert.AreEqual(TimeSpan.FromSeconds(25), budget.RemainingAfterMargin(clock));
    }

    [TestMethod]
    public void FromRemaining_RejectsNonPositive()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            TimeBudget.FromRemaining(TimeSpan.Zero));
    }

    [TestMethod]
    public void From_RejectsNonPositive()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            TimeBudget.From(TimeSpan.Zero));
    }
}
