using Strategy.Observability.Bus;
using Strategy.Observability.Diagnostics;
using Strategy.Observability.Envelope;
using Strategy.Observability.Entity;
using Strategy.Observability.Persistence;
using Strategy.Observability.Query;
using Strategy.Observability.Registry;

namespace Observability.Demo;

/// <summary>最小演示（Batch1 验收，无任何 Agent 依赖）：注册契约 -> 埋点 -> Timeline/EntityChain/Aggregate 查询。</summary>
internal static class Program
{
    private static async Task<int> Main()
    {
        // 1) 注册契约（语义实例，来自 observability_semantics.json）
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "observability_semantics.json"));
        var contracts = ObservabilitySemanticsJson.Load(json);
        var registry = new ObservabilityContractRegistry();
        foreach (var c in contracts) registry.Register(c);
        Console.WriteLine($"[demo] 注册契约 {registry.Contracts.Count} 个: {string.Join(", ", registry.Contracts.Select(x => x.EventKind))}");

        // 2) 事件区（E5）+ 诊断兜底（E8）
        var storeFile = Path.Combine(Path.GetTempPath(), $"obs-demo-{Environment.ProcessId}.jsonl");
        var store = new FileAppendEventStore(storeFile);
        await store.InitializeAsync();
        var fallback = new ObservabilityFallback();

        // 3) 收集总线（E4）：持久化批写 + 环形热查 + console
        var persistent = new PersistentEventSink(store, capacity: 256, drainBatch: 8, retryLimit: 2, log: fallback.AsSlot());
        var hot = new HotRingSink(1000);
        var bus = new ObservabilityBus(registry, new IObservabilitySink[] { persistent, hot }, fallback: fallback);
        Obs.Configure(bus);

        // 4) 关联键作用域透传（E4）
        using (Obs.EnterScope(CorrelationBag.New(traceId: "trc-1", sessionId: "sess-1", scopeKey: "run-1")))
        {
            var ids = new EntityIdFactory();
            var run = ids.New("run-1", "Run");

            Obs.Emit(run, "RunStarted");

            // 单个 Step 活动：Activity 在 Complete/Fail 时补发完成事件（elapsed_ms + 业务度量）
            for (var i = 1; i <= 3; i++)
            {
                var step = ids.New("run-1", "Step");
                if (i == 3)
                {
                    using var failed = new Activity(bus, step, "StepStarted", "StepFailed");
                    await Task.Delay(10);
                    failed.Fail($"step-{i} boom"); // 完成分类 StepFailed
                    continue;
                }
                using var activity = new Activity(bus, step, "StepStarted", "StepCompleted");
                await Task.Delay(10); // 模拟工作
                activity.Complete(b => b.Payload(new { workId = $"work-{i}" }).WithMetric("score", i * 10.0));
            }

            // 用 Obs.Begin 门面演示一次（completion 用同名事件分类，仅观察其自动补全）
            using (Obs.Begin(ids.New("run-1", "Step"), "StepStarted"))
            {
                await Task.Delay(5);
            }

            Obs.Emit(run, "RunCompleted", b => b.WithMetric("elapsed_ms", 123.4));
        }

        // 5) 优雅关闭：排空批写器（保证事件不丢批）
        await bus.ShutdownAsync();
        await Task.Delay(100);

        // 6) 查询投影（E6）：Timeline / EntityChain / Aggregate
        var query = new ObservabilityQuery(store);
        Console.WriteLine("\n[demo] Timeline(run-1):");
        await foreach (var e in query.TimelineAsync("run-1"))
            Console.WriteLine($"  #{e.EventSeq,-2} {e.EventKind,-12} {e.Entity.EntityType,-4} metrics=[{Format(e.Metrics)}]");

        Console.WriteLine("\n[demo] EntityChain(Run):");
        var runId = new EntityId("run-1", "Run", 1);
        await foreach (var e in query.EntityChainAsync(runId))
            Console.WriteLine($"  #{e.EventSeq,-2} {e.EventKind}");

        Console.WriteLine("\n[demo] Aggregate(run-1, [StepCompleted], [score, elapsed_ms]):");
        var agg = await query.AggregateAsync("run-1", new[] { "StepCompleted" }, new[] { "score", "elapsed_ms" });
        foreach (var a in agg)
            Console.WriteLine($"  {a.MetricKey,-10} count={a.Count} sum={a.Sum:0.###} avg={a.Average:0.###}");

        // 7) 回归断言（Batch1 验收）：链路齐备 + 持久化文件已生成
        var runChain = new List<ObservabilityEvent>();
        await foreach (var e in query.EntityChainAsync(runId)) runChain.Add(e);
        var persisted = File.Exists(storeFile) ? await File.ReadAllLinesAsync(storeFile) : Array.Empty<string>();
        var ok = runChain.Count(e => e.EventKind == "RunStarted") == 1
                 && runChain.Count(e => e.EventKind == "RunCompleted") == 1
                 && persisted.Length > 0;
        Console.WriteLine(ok
            ? "\n[demo] 验收通过：Run 链齐备、事件区已落盘。"
            : "\n[demo] 验收失败：链路或持久化不满足。");
        return ok ? 0 : 1;
    }

    private static string Format(IReadOnlyDictionary<string, double>? m)
        => m is null ? "" : string.Join(",", m.Select(kv => $"{kv.Key}={kv.Value:0.###}"));
}



