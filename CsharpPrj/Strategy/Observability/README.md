# Strategy.Observability — 通用语义观测内核（Batch 1）

> 依据《Agent架构语义观测标准.md》第 7 节 Batch 1 落地：通用内核下沉（Frame，Agent 无关）。
> 机制与语义分离：内核零 Agent 字符串（越界红线），应用只注册语义契约实例即可获得契约校验 / 作用域透传 / 时间线重建 / 指标聚合。
> 控制反转：内核不内嵌长期后台逻辑、不替你决定吞错——持久化由调用方显式 `FlushAsync`，错误一律在调用边界抛出，成败对调用方可见。

## 目录

| 编号 | 职责 | 位置 | 关键类型 |
|---|---|---|---|
| E1 | 实体模型 | `Entity/` | `EntityId` / `EntityIdFactory` / `IObservableEntity` |
| E2 | 事件信封 | `Envelope/` | `ObservabilityEvent` / `ObservabilityEventBuilder` |
| E3 | 契约注册表 | `Registry/` | `EventContract` / `ObservabilityContractRegistry` / `ObservabilitySemanticsJson` |
| E4 | 收集总线 | `Bus/` | `ObservabilityBus` / `Obs` / `Activity` / `ObsScope`(复用 AmbientSlot) / `EmitResult` / `ObservationResult` |
| E5 | 持久化 | `Persistence/` | `IEventStore` / `InMemoryEventStore` / `FileAppendEventStore` / `PersistentEventSink`(调用方驱动 flush) |
| E6 | 查询投影 | `Query/` | `ObservabilityQuery`(Timeline / EntityChain / Aggregate) |
| E7 | 度量聚合 | `Metrics/` | `Counter` / `Histogram` |
| 异常 | 类型化错误 | `Diagnostics/` | `ObservabilityException`(基) / `UnregisteredEventException` / `ContractValidationException` / `SinkWriteException` |

## 复用的 Frame 设施

- `Modules\Threading\Threading.csproj`：仅复用 `AmbientSlot<T>`(E4 作用域)。**不**复用 `BatchedAsyncSink`——持久化改为调用方驱动的显式 `FlushAsync`（无后台 DrainLoop、无内部静默重试）。
- `Share.TaskStatusPool`：原 `Share\HierarchySupport` 叶工程整项目并入本工程后引入（`RuntimeStage.FromStatus` 依赖 `TaskExecutionStatus`）。并入的 DTO 保留命名空间 `Share.HierarchySupport`，源文件位于 `HierarchySupport/` 子目录，属「agent/方法/策略 语义」红线例外区域，与 E1-E8 内核隔离。
- JSON：`System.Text.Json`（内置）。持久化事件区：append-only JSONL（`FileAppendEventStore`）；生产可对 `Operation.DataStoreDriven.IZoneStore` 适配成 ur_events 事件区。

## 反转与控制边界（设计要点）

- **错误一律在调用边界抛，不做内部降级**：Emit 遇未注册契约抛 `UnregisteredEventException`、契约校验失败抛 `ContractValidationException`；调用方显式 `FlushAsync()` 落库失败抛 `SinkWriteException`（缓冲保留可重放）。内核不再用 `ObservabilityBusOptions` / `ObservabilityFallback` 这类内部布尔或日志插槽替你决定吞错。
- **成败对调用方可见（两维）**：① 业务任务成败 = `Activity.Result` 产成的类型化 `ObservationResult`（`ObservationStatus.Succeeded/Failed` + 耗时 + 原因）；② 观测投递成败 = `Emit`/`Activity.Complete/Fail` 返回 `EmitResult`，持久化落库成败由 `FlushAsync` 的异常体现。
- **后台长期逻辑外置**：持久化无内建线程。每轮 / 批处理结束 `await bus.FlushAsync()`（或 `Obs.FlushAsync()`）即知是否落库；进程退出前 `await bus.ShutdownAsync()`（先 flush 再释放）。

## 通用性边界

- 内核出现 decision/tool/skill/llm 即越界 → 请把语义契约放在应用侧（参考 `Observability.Demo/observability_semantics.json`）。
- 应用不得自行实现校验 / ID 分配（都已有内核件）。

## 演示与验收（无 Agent）

```powershell
cd D:\Project\Main\Frame\CsharpPrj
# 内核独立编译（零 Agent 依赖）
dotnet build Strategy\Observability\Observability.csproj
# 聚合链回归
dotnet build Strategy\Strategy.csproj
dotnet build MainProj.sln
```

验收断言示例：契约未注册 Emit 抛 `UnregisteredEventException`、校验失败抛 `ContractValidationException`、`FlushAsync` 落库失败抛 `SinkWriteException`；`Activity.Complete/Fail` 回读 `ObservationResult`；`await bus.FlushAsync()` 后事件区可查 Timeline/EntityChain/Aggregate。
