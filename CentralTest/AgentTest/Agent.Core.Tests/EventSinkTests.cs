using System;
using System.Threading.Tasks;
using Agent.Contract.Observability;
using Agent.Core.DataStream;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using TestInfrastructure;

namespace Agent.Core.Tests;

[TestClass]
[TestCategory(TestCategories.Concurrency)]
public class EventSinkTests
{
    [TestMethod]
    [Timeout(15000)]
    public async Task Emit_EventuallyWritesToZone()
    {
        var zones = new InMemoryZoneStore();
        await using var sink = new EventSink(zones, capacity: 16, drainBatch: 4);
        sink.Emit(new RunEvent { ThreadId = "t1", InputKey = "k1", EventType = EventType.RunCompleted, Stage = "x" });

        System.Collections.Generic.IReadOnlyList<EventRecord>? observed = null;
        await AsyncTestHelper.WaitUntilOrThrowAsync(async () =>
        {
            var events = await zones.ListEventsAsync();
            if (events.Count == 0)
                return false;
            observed = events;
            return true;
        }, timeoutMs: 3000, pollIntervalMs: 20,
            description: "事件未在超时内写入事件区");

        Assert.AreEqual("RunCompleted", observed![0].EventType);
        Assert.AreEqual("k1", observed[0].EventKey);
    }
}
