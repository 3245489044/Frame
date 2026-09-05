# WorkerPool 状态与编排框架（StateHoldDriven.Pooling）

> 依据：《Cline 传输层 daemon 池重构 — 基于 InternalStorage 的可行性分析与设计》（docs\analysis\2026\8\8.7\ClineDaemon池_InternalStorage可行性分析.md §5/§7/§9.4）。
> 定位：把文档中的 **Cline 专用状态层**泛化为**通用 WorkerPool 状态与编排框架**，落在 StateHoldDriven，**保持传输无关**（R2）。

---

## 1. 职责边界

| 层次 | 归属 | 本框架 |
|------|------|--------|
| **状态/编排层**（本框架） | `Share.StateManagement.Pooling`（契约）+ `StateHoldDriven.Pooling`（实现） | 状态机、每 worker 元数据、池启动编排、请求关联、健康统计 |
| **执行层** | `Modules.Threading`（AEF：AsyncOperationScheduler / WatchdogService / IProcessHandle） | ❌ 并发闸门/超时/看门狗/进程树终止 |
| **传输/编排层** | `ClineClient`（ClineDaemonPool） | ❌ spawn/kill、JSON-Line IPC、数据隔离 |

本框架**不做**：进程 spawn/kill、IPC、请求分发、超时看门狗、跨进程持久化（见 §5 风险）。

---

## 2. 泛化映射表（可行性文档 → 本框架）

| 可行性文档（Cline 专用） | 本框架（通用） |
|--------------------------|----------------|
| `ClineDaemonState` 枚举 | `PoolWorkerState`（Starting/Running/Busy/Hung/Stopped） |
| `ClineDaemonStateStore` | `WorkerStateStore`（包装 `IStorageEngine`+每 worker 一 `StorageCell`） |
| `ClineDaemonPoolInitializer` | `PoolInitializer`（包装 `InitializationOrchestrator`） |
| `ClineRequestTracker` | `RequestTracker`（沿用 `EcsSessionManager` 模式） |
| 池健康/统计（StorageRegistry） | `PoolHealthRegistry` / `PoolHealthSnapshot` |

---

## 3. 目录与组件

```
CsharpPrj/Share/StateManagement/Pooling/         契约层（R1，仿 IScopeLifecycle 模式）
    PoolWorkerState.cs       枚举
    WorkerStateInfo.cs       record（pid/启动时间/busy/lastError/timeoutCount）
    WorkerHandle.cs          传输句柄（纯数据载体）
    WorkerStartSpec.cs       启动规格
    PoolInitResult.cs        启动结果
    PoolHealthSnapshot.cs    健康快照
    IWorkerStateStore.cs     每 worker 会话 + 带守卫 Transition + StateChanged
    IRequestTracker.cs       请求↔worker 关联
    IPoolInitializer.cs      池启动编排
    IPoolHealth.cs           健康聚合
CsharpPrj/Operation/StateHoldDriven/Pooling/     实现层（R2）
    Core/WorkerStateStore.cs
    Core/PoolInitializer.cs
    Core/RequestTracker.cs
    Core/PoolHealthRegistry.cs
    WorkerStateStoreExtensions.cs   MarkBusy/MarkFree/MarkHung/StopWorker 便捷转移
CentralTest/.../StateHoldDriven.Tests/Pooling/    MSTest 单测（测试桩委托，不起真进程）
```

---

## 4. 传输委托契约（ClineClient 接线点）

框架保持进程无关的关键：所有传输行为经**委托注入**，仿 `ScopeLifecycleStateMachine._renderDelegate` 模式。

| 委托 | 含义 | 由谁实现 |
|------|------|----------|
| `IPoolInitializer.RegisterPrecondition(() => Task<bool>)` | 启动前置检查（如 cli.js/可执行文件存在） | ClineClient |
| `IPoolInitializer.RegisterWorkerFactory((spec, ct) => Task<WorkerHandle>)` | spawn 单个 worker | ClineClient（spawn node daemon） |
| `IPoolInitializer.RegisterProbe((handle) => Task<bool>)` | 心跳探活 | ClineClient（ping/探活 IPC） |
| （运行期）`WorkerStateStore.MarkBusy/MarkFree/MarkHung/StopWorker` | 请求进出/挂起/杀进程 | ClineClient + AEF 看门狗 |

> `WorkerHandle` 为纯数据（Id/Pid/StartedAtUtc），底层进程句柄由 ClineClient 持有。启动成功后 worker 处于 `Running`；请求期间 `MarkBusy`，结束 `MarkFree`；看门狗判挂起 `MarkHung`；超时/重启 `StopWorker`。

**接线示例（示意，Cline 侧实现）**：
```csharp
var engine  = new StorageEngine("cline-daemon-pool");
var store   = new WorkerStateStore(engine);
var init    = new PoolInitializer(engine, store);
init.RegisterPrecondition(async () => File.Exists(cliJsPath));
init.RegisterWorkerFactory((spec, ct) => spawnNodeDaemonAsync(spec, ct)); // ClineClient
init.RegisterProbe(h => PingDaemonAsync(h, ct));
var result  = await init.StartAsync(N);      // 启动 N 个 daemon 并探活
var health  = new PoolHealthRegistry(store); // 池健康/统计
```

---

## 5. 已知边界与风险（对齐可行性文档）

- **R2 持久化**：`WorkerStateStore` 元数据落 `StorageCell`（进程内内存），**不解决跨进程重启恢复**；daemon 池重启后的状态恢复需 ClineClient 自行持久化到文件。→ 待定，本框架不实现。
- **R3 串行编排**：`PoolInitializer` 复用 `InitializationOrchestrator` 做**启动编排**（串行），**不做运行期并发请求分发**（那是池管理器 + AEF 的职责）。
- 状态机 `Transition` 仅做 **from 守卫**（防非法/竞态转移），不做完整合法性矩阵——合法转移语义由调用方（传输层）保证。

---

## 6. 验证

- `dotnet build CsharpPrj\Operation\StateHoldDriven\StateHoldDriven.csproj`
- `dotnet test CentralTest\CSharpTest\Operation.Tests\StateHoldDriven.Tests\StateHoldDriven.Tests.csproj`（含 Pooling 单测，59 项全绿）
- `dotnet test CentralTest\CSharpTest\Modules.Tests\InternalStorage.Tests\InternalStorage.Tests.csproj`（207 项全绿，后端未回归）
