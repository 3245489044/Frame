using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Contract.Observability;
using Agent.Core.DataStream;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.ComponentData.Abstractions;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using TestInfrastructure;
using WorkFlowMethod.LoopFramework.Axioms;

namespace Agent.Core.Tests;

[TestClass]
[TestCategory(TestCategories.Concurrency)]
public class ReactiveTriggerTests
{
    private static readonly ReactiveTriggerOptions DefaultOptions = new();

    [TestMethod]
    public async Task ProcessPending_RunsEachInputAndWritesOutput()
    {
        var zones = new ReactiveZoneBridge(new InMemoryZoneStore());
        var calls = new List<string>();
        var trigger = new ReactiveTrigger(zones,
            (input, thread, key, ct) => { calls.Add($"{thread}:{input}"); return Task.FromResult(Ok()); },
            DefaultOptions);

        var id1 = await zones.AppendInputAsync(new InputRecord { ThreadId = "t1", InputKey = "k1", PayloadJson = "p1" });
        var id2 = await zones.AppendInputAsync(new InputRecord { ThreadId = "t1", InputKey = "k2", PayloadJson = "p2" });

        var processed = await trigger.ProcessPendingAsync();

        Assert.AreEqual(2, processed);
        Assert.AreEqual(2, calls.Count);
        Assert.AreEqual(id2, trigger.Cursor);
        var outputs = await zones.ListOutputsAsync();
        Assert.AreEqual(2, outputs.Count);
        Assert.AreEqual("Success", outputs[0].Status);

        await trigger.DisposeAsync();
    }

    [TestMethod]
    public async Task ProcessPending_IsIdempotentByCursor()
    {
        var zones = new ReactiveZoneBridge(new InMemoryZoneStore());
        var calls = 0;
        var trigger = new ReactiveTrigger(zones,
            (input, thread, key, ct) => { calls++; return Task.FromResult(Ok()); },
            DefaultOptions);

        await zones.AppendInputAsync(new InputRecord { ThreadId = "t1", InputKey = "k1", PayloadJson = "p" });
        Assert.AreEqual(1, await trigger.ProcessPendingAsync());
        // 游标已推进 → 无新输入。
        Assert.AreEqual(0, await trigger.ProcessPendingAsync());
        Assert.AreEqual(1, calls);

        await trigger.DisposeAsync();
    }

    [TestMethod]
    public async Task ProcessPending_OnErrorWritesErrorOutputAndAdvances()
    {
        var zones = new ReactiveZoneBridge(new InMemoryZoneStore());
        var trigger = new ReactiveTrigger(zones,
            (input, thread, key, ct) => throw new InvalidOperationException("boom"),
            DefaultOptions);

        var id = await zones.AppendInputAsync(new InputRecord { ThreadId = "t1", InputKey = "k1", PayloadJson = "p" });
        await trigger.ProcessPendingAsync();

        var outputs = await zones.ListOutputsAsync();
        Assert.AreEqual(1, outputs.Count);
        Assert.AreEqual("error", outputs[0].Status);
        Assert.IsTrue(outputs[0].PayloadJson.Contains("boom"));
        Assert.AreEqual(id, trigger.Cursor);

        await trigger.DisposeAsync();
    }

    [TestMethod]
    public async Task EmitsEventsWhenSinkProvided()
    {
        var zones = new ReactiveZoneBridge(new InMemoryZoneStore());
        var events = new List<RunEvent>();
        var trigger = new ReactiveTrigger(zones,
            (input, thread, key, ct) => Task.FromResult(Ok()),
            DefaultOptions, new CollectingSink(events));

        await zones.AppendInputAsync(new InputRecord { ThreadId = "t1", InputKey = "k1", PayloadJson = "p" });
        await trigger.ProcessPendingAsync();

        // 触发层仅发 InputAccepted；run 级事件（RunStarted/RunCompleted/RunFailed）由 run 端点 AgentOrchestrator.RunAsync 统一发出。
        Assert.IsTrue(events.Exists(e => e.EventType == EventType.InputAccepted));
        Assert.IsFalse(events.Exists(e => e.EventType == EventType.RunStarted)); // 不在此重复发

        await trigger.DisposeAsync();
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Start_BackgroundLoopProcessesInputsUntilStopped()
    {
        var zones = new ReactiveZoneBridge(new InMemoryZoneStore());
        var calls = 0;
        var trigger = new ReactiveTrigger(zones,
            (input, thread, key, ct) => { calls++; return Task.FromResult(Ok()); },
            DefaultOptions);

        trigger.Start();
        await zones.AppendInputAsync(new InputRecord { ThreadId = "t1", InputKey = "k1", PayloadJson = "p" });

        await AsyncTestHelper.WaitUntilOrThrowAsync(
            () => calls >= 1 && trigger.Cursor > 0,
            timeoutMs: 3000, pollIntervalMs: 20,
            description: "触发处理未在超时内完成");
        await trigger.StopAsync();
        var snapshot = calls;
        await Task.Delay(50);
        Assert.AreEqual(snapshot, calls); // 停止后不再处理
        await trigger.DisposeAsync();
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Start_ProcessesPushWithoutPolling()
    {
        // 纯事件订阅（ECS 写即触发），无任何周期轮询。
        var zones = new ReactiveZoneBridge(new InMemoryZoneStore());
        var calls = 0;
        var trigger = new ReactiveTrigger(zones,
            (input, thread, key, ct) => { calls++; return Task.FromResult(Ok()); },
            new ReactiveTriggerOptions { BackfillOnStart = false, MinTriggerIntervalMs = 0 });

        trigger.Start();
        await zones.AppendInputAsync(new InputRecord { ThreadId = "t1", InputKey = "k1", PayloadJson = "p" });

        await AsyncTestHelper.WaitUntilOrThrowAsync(
            () => calls >= 1 && trigger.Cursor > 0,
            timeoutMs: 3000, pollIntervalMs: 20,
            description: "触发处理未在超时内完成");
        Assert.AreEqual(1, calls);

        await trigger.DisposeAsync();
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Start_BackfillsInputsWrittenBeforeStart()
    {
        // 启动回填：处理订阅前/重启遗留输入（重放，at-least-once），随后走事件订阅。
        var zones = new ReactiveZoneBridge(new InMemoryZoneStore());
        var calls = 0;
        await zones.AppendInputAsync(new InputRecord { ThreadId = "t1", InputKey = "k1", PayloadJson = "p" });

        var trigger = new ReactiveTrigger(zones,
            (input, thread, key, ct) => { calls++; return Task.FromResult(Ok()); },
            new ReactiveTriggerOptions { BackfillOnStart = true, MinTriggerIntervalMs = 0 });
        trigger.Start();

        await AsyncTestHelper.WaitUntilOrThrowAsync(
            () => calls >= 1,
            timeoutMs: 3000, pollIntervalMs: 20,
            description: "回填处理未在超时内完成");
        Assert.AreEqual(1, calls);

        await trigger.DisposeAsync();
    }

    [TestMethod]
    public async Task AppendInput_PublishesEcsDataChangedForInputKey()
    {
        // ECS 反应式基底：写入输入即发布 ComponentDataStore.DataChanged（input.* 键，写即触发信号源）。
        var zones = new ReactiveZoneBridge(new InMemoryZoneStore());
        var changed = new List<string>();
        using var sub = zones.DataChanged.Subscribe(e => changed.Add(e.Key));

        var id = await zones.AppendInputAsync(new InputRecord { ThreadId = "t1", InputKey = "k1", PayloadJson = "p" });

        Assert.IsTrue(changed.Contains($"input.{id}"));
    }

    private static AxiomLoopResult Ok() => new() { FinalDecision = LoopDecision.Success };

    private sealed class CollectingSink : IObservabilitySink
    {
        private readonly List<RunEvent> _events;
        public CollectingSink(List<RunEvent> events) => _events = events;
        public void Emit(RunEvent runEvent) => _events.Add(runEvent);
    }
}
