using Modules.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Modules.Threading.Tests;

[TestClass]
public class IdleWatchdogTests
{
    private sealed class Item
    {
        public DateTimeOffset Heartbeat { get; set; }
    }

    [TestMethod]
    public void CheckNow_NoIdle_WithinWindow()
    {
        var clock = new FakeClock();
        var item = new Item { Heartbeat = clock.UtcNow };
        var idle = new List<string>();

        using var wd = new IdleWatchdog<Item>(i => i.Heartbeat, _ => idle.Add("idle"), clock);
        wd.Track(item, TimeSpan.FromSeconds(10));

        clock.Advance(TimeSpan.FromSeconds(5));
        wd.CheckNow();

        Assert.AreEqual(0, idle.Count);
    }

    [TestMethod]
    public void CheckNow_Idle_WhenHeartbeatStale()
    {
        var clock = new FakeClock();
        var item = new Item { Heartbeat = clock.UtcNow };
        var idle = new List<string>();

        using var wd = new IdleWatchdog<Item>(i => i.Heartbeat, _ => idle.Add("idle"), clock);
        wd.Track(item, TimeSpan.FromSeconds(10));

        clock.Advance(TimeSpan.FromSeconds(11));
        wd.CheckNow();

        Assert.AreEqual(1, idle.Count);
    }

    [TestMethod]
    public void Untrack_StopsDetection()
    {
        var clock = new FakeClock();
        var item = new Item { Heartbeat = clock.UtcNow };
        var idle = new List<string>();

        using var wd = new IdleWatchdog<Item>(i => i.Heartbeat, _ => idle.Add("idle"), clock);
        wd.Track(item, TimeSpan.FromSeconds(10));
        wd.Untrack(item);

        clock.Advance(TimeSpan.FromSeconds(11));
        wd.CheckNow();

        Assert.AreEqual(0, idle.Count);
    }
}
