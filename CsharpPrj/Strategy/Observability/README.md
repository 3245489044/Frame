# Strategy.Observability — 通用语义观测内核（Batch 1）

> 依据《Agent架构语义观测标准.md》第 7 节 Batch 1 落地：通用内核下沉（Frame，Agent 无关）。
> 机制与语义分离：内核零 Agent 字符串（越界红线），应用只注册语义契约实例即可免费获得批写 / 契约校验 / 作用域透传 / 时间线重建 / 指标聚合。

## 目录（E1-E8）

| 编号 | 职责 | 位置 | 关键类型 |
|---|---|---|---|
| E1 | 实体模型 | `Entity/` | `EntityId` / `EntityIdFactory` / `IObservableEntity` |
| E2 | 事件信封 | `Envelope/` | `ObservabilityEvent` / `ObservabilityEventBuilder` |
| E3 | 契约注册表 | `Registry/` | `EventContract` / `ObservabilityContractRegistry` / `ObservabilitySemanticsJson` |
| E4 | 收集总线 | `Bus/` | `ObservabilityBus` / `Obs` / `Activity` / `ObsScope`(复用 AmbientSlot) |
| E5 | 持久化 | `Persistence/` | `IEventStore` / `InMemoryEventStore` / `FileAppendEventStore` / `PersistentEventSink`(复用 BatchedAsyncSink) |
| E6 | 查询投影 | `Query/` | `ObservabilityQuery`(Timeline / EntityChain / Aggregate) |
| E7 | 度量聚合 | `Metrics/` | `Counter` / `Histogram` |
| E8 | 诊断兜底 | `Diagnostics/` | `ObservabilityFallback`(Action 插槽，对齐 StallDetector/OutboxWriter 范本) |

## 复用的 Frame 设施

- `Modules\Threading\Threading.csproj`：`AmbientSlot<T>`(E4 作用域) / `BatchedAsyncSink<T>`(E5 批写) / `FlowBoundary` / `StallDetector` 插槽范本。唯一外部引用。
- JSON：`System.Text.Json`（内置）。持久化事件区：append-only JSONL（`FileAppendEventStore`）；生产可对 `Operation.DataStoreDriven.IZoneStore` 适配成 ur_events 事件区。

## 通用性边界

- 内核出现 decision/tool/skill/llm 即越界 → 请把语义契约放在应用侧（参考 `Observability.Demo/observability_semantics.json`）。
- 应用不得自行实现批写 / 校验 / ID 分配（都已有内核件）。

## 演示与验收（无 Agent）

```powershell
cd D:\Project\Main\Frame\CsharpPrj
# 内核独立编译（零 Agent 依赖）
dotnet build Strategy\Observability\Observability.csproj
# 最小演示：注册契约 + 埋点 + Timeline/EntityChain/Aggregate 查询跑通
dotnet run --project Strategy\Observability.Demo\Observability.Demo.csproj
# 聚合链回归
dotnet build Strategy\Strategy.csproj
dotnet build MainProj.sln
```

演示输出含 Timeline 时序、Run 实体链、StepCompleted 度量聚合，并以「RunStarted/RunCompleted 各 1 条 + 事件区落盘」作验收断言。
