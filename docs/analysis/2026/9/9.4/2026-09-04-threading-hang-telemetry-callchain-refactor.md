# Modules.Threading 卡死取证 / 运行期遥测调用链 — 重构优化方案与实施基线

> **日期**：2026-09-04
> **性质**：调用链静态审计 → 重构优化（含逻辑错误清单 + 健康路径开销定位）。
> **分析范围**：`CsharpPrj/Modules/Threading/Hang/*`（HangDiagnostics / HangForensics / HangProbe / RingBuffer / StepWatchdog）、`Modules/Threading/Bounded/BoundedExecution.cs`、`Modules/Threading/Write/SerialWriteGate.cs`、`Modules/Threading/AsyncLock.cs`、`Operation/HierarchyMethod/Center/ExecutionScope.cs`、`Operation/HierarchyMethod/Center/WriteCoordinator.cs`、`Operation/BackEndDriven/CommandExecution/CommandCoordinator.cs`、`CommandTelemetry.cs`。
> **上游参考**：`docs/analysis/2026/9/9.3/2026-09-03-db-read-hang-arch-refactor.md`（S3 取证接线待办）、`docs/analysis/Agent/doc/线程自卫式卡死取证-*.md`。

***

## 〇、TL;DR

卡死取证/遥测当前存在**两条并行链**，一活一死、接口并存且职责重叠：

- **活的（命令级）**：`CommandCoordinator` → `ConsoleCommandTelemetry`（`RingBuffer` + `BackEndLog`），并逐命令启动 `MonitorSlowAsync`。**错误时**即时出声并落 `BackEndLog`（内存取证、不落 dump）；**健康路径**每命令仍有后台监视任务 + 定时器 + 带锁 Ring 写——在默认阈值下是真实拖累。

- **死的（步级）**：`HangForensics` → `StepWatchdog` → `HangEvidenceAssembler`。全仓**无任何实例化/接线**，`BoundedExecution.CreateRoot` 的 `hang:` 形参无人传入。是未生效的死代码，但对健康路径也零开销。

本方案给出：问题清单（§3）、取舍决策（§4）、分阶段重构（§5）、验收（§6）、待决（§7）。

***

## 1. 调用链全景

### 1.1 命令级（活着）

```
CommandCoordinator.Update / UpdateAsync
  ├─ per 命令: OnCommandBegin(meta...)            // CommandCoordinator.cs L182/L254
  │     └─ ConsoleCommandTelemetry.OnCommandBegin  // CommandTelemetry.cs L61
  │           └─ _commandTrace.Add(...)           // 无条件：lock + 入环
  ├─ per 命令: MonitorSlowAsync(...)              // L65；同步路径先 Task.Run 调度
  │     ├─ start: Task.Delay(SlowReport=15s)
  │     └─ 超时后: Task.Delay(HangReport=30s)
  │           └─ OnCommandSlowOrHang → PrintEvidenceBlock(进程快照, 内存, 不落盘)
  └─ 失败: MarkFailed → OnCommandFailure          // L314/L328
        ├─ BackEndLog.Log("...failure", ...)      // 持久化（错误路径才发生）
        └─ _commandTrace.Add(...)

命令内核: WriteCoordinator → SerialWriteGate → BoundedExecution.CreateRoot → RunBoundedCoreAsync
     └─ _hang?.Enter(subject)     // hang 恒 null → 直跳过（见 §1.2）
```

### 1.2 步级（已声明但未接线）

```
BoundedExecution.Run* (hang: 形参)
  └─ _hang?.Enter(subject)   // 无人传 hang → 空操作
       └─ HangForensics.Enter/_loop → StepWatchdog.Check → HangEvidenceAssembler.Assemble → onConfirmed
```

**核对事实**：全仓 `rg "new HangForensics|hang:|AddSource"` 仅命中 `ControlRegistryAwareFinder` 的同名 `ControlLookup.AddSource`（**非** `IHangEvidenceSource`）。`ExecutionScope.CreateRoot`、`WriteCoordinator`、`SerialWriteGate` 均未把 `hang` 传入 `BoundedExecution.CreateRoot`。故 `HangForensics` 链当前**完全未生效**。

***

## 2. 现状代码核对（关键节点）

| 节点                     | 位置                                                  | 健康路径行为                                                           | 代价量级                 |
| ---------------------- | --------------------------------------------------- | ---------------------------------------------------------------- | -------------------- |
| `OnCommandBegin`       | CommandTelemetry.cs L63                             | `_commandTrace.Add` **无条件执行**（与 `BeginTrace` 无关）                 | 每命令：1×lock + 1 字符串分配 |
| `MonitorSlowAsync` 启动  | CommandCoordinator.cs L193（同步）/ L259（异步）            | 每命令分配 `CTS` + 2×`Task.Delay` 定时器                                 | 每命令：2 定时器 + 1 CTS    |
| **同步路径**额外             | L193 `Task.Run(...)` + L217 `stallMonitor.Wait(2s)` | 每命令 1 次线程池调度                                                     | 线程池项                 |
| `BoundedExecution` 有预算 | BoundedExecution.cs L113                            | `Task.Delay(InfiniteTimeSpan)` + `Task.WhenAny` + linked CTS     | 每调用（仅设预算时）           |
| 写盘                     | 各                                                   | **健康路径零落盘**：`BackEndLog.Log` 仅 failure/slow/hang 触发；取证不写 dump 文件 | —                    |

> 结论：健康路径**无写盘浪费**，但有可观的 CPU/定时器/线程池冗余。

***

## 3. 问题清单

### 3.1 逻辑错误 / 名实不符

- **N1（最重要）**：`BoundedExecution.cs L41-43` 注释宣称"非空时发布段相位、由 HangForensics 宿主判卡"为**已集成行为**，但上、下游无人接线 → 能力被文档化、实为不可用。`ExecutionScope`/`WriteCoordinator`/`SerialWriteGate` 三个调用面均漏传 `hang`。

- **N2**：双系统重复判卡风险——`CommandTelemetry`（Stopwatch 轮询）与 `HangForensics`（心跳停更）都在"判卡 + 装配证据"。一旦为 N1 接线，同处卡死双份取证，违背 `HangDiagnostics.cs L43`"写入口单一、不双写"的自述。

- **N3**：`HangForensics._slot` + `_stack` 为共享单实例，**非并发安全**。`BoundedExecution.Derive` 面向嵌套，但同根上的兄弟并发 `Run*` 会交错 `Enter`/`Leave` 使相位栈错乱 → 错误证据/误判。隐含"串行调用"前提，代码/注释均未约束或防护。

### 3.2 错误信息处理

- 证据装配：`HangEvidenceAssembler.Assemble L72-73` 对每个 `source.Capture()` 包 try/catch，异常被**吞成字符串** `"<capture-failed:{ex.Message}>"` 入 context——丢堆栈、难定位，但不阻塞取证（可接受，仅记录）。

- 卡死取证：`PrintEvidenceBlock`（CommandTelemetry.cs L99）仅写控制台 + 内存进程快照（`Process.GetCurrentProcess()`/`GC.GetTotalMemory`），**明确不落 dump 文件**——昂贵 OS 调用仅卡死路径发生，符合"debug 不写盘"。

- 类型映射：`BoundedTimeoutException` → `StoreTimeoutException`/`ExecutionBoundaryException`（ExecutionScope.cs Translate）映射正确、取消原样上抛。

### 3.3 冗余性能浪费（健康路径）

- **R1**：`OnCommandBegin` 无条件写环，与 CommandTelemetry.cs L10"健康/快速命令零开销"注释**冲突**。

- **R2**：默认配置（`SlowReport=15s`、`HangReport=30s` 均 >0）下每命令一条 `MonitorSlowAsync`（同步路径 `Task.Run` + CTS + 2 定时器），命令毫秒秒回即被 `stallCts.Cancel()` 取消——纯损耗。

- **R3**：`BoundedExecution` 有预算时每次 `Run*` 分配 Infinite `Task.Delay` + `WhenAny` + linked CTS（默认 `budget=null` 走直跑不分配，仅显式设预算时成立）。

### 3.4 无错误时是否拖累性能

**会**（仅当 `_telemetry` 非空且阈值 >0，即默认）。每执行一条命令：1×带锁 Ring 写 + 1 条后台监视（同步路径含 `Task.Run` + `Wait(2s)`）+ 2 个定时器分配并取消。`_telemetry == null` 或阈值清零才真正零开销。

***

## 4. 目标架构与取舍

铁律：

```
① 报告型遥测与判卡取证分离：报告负责“出声”，判卡负责“证据”，单一归口、不双写。
② 健康路径零热开销：快命令不启动任何监视资源、不写环。
③ 监控惰性化：命令先跑，越过阈值才启动协程/定时器。
④ 判卡取证链二选一：要么接线启用（N1 落地），要么删除整套死代码与误导注释（不留半吊子）。
```

**取舍决策（关键）**：

- 判卡证据链只保留**一条**。推荐以 `CommandTelemetry` 为活着路径（已接线、已落诊断、上游已消费），**引入** **`HangEvidenceAssembler`** **作取证装配器**供其复用，而非启用整套 `HangForensics` 步级链。可选方向 B（启用 HangForensics、接 `BoundedExecution hang:`）见 §7，涉及跨树烘焙 `Threading.dll` 的成本与 N2/N3 风险，默认不选。

- 无论取舍，`RingBuffer` 保留（内存、有界、只增丢最旧），仅约束其写入时机（见 §5 R1）。

***

## 5. 重构方案（分阶段，每步独立编译）

| 阶段      | 内容                                                                                                                                                    | 改动文件                    | 验收信号                   |
| ------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------- | ---------------------- |
| Stage 1 | **R1·条件写环**：`OnCommandBegin` 仅当 `BeginTrace`（或显式追踪开关）才 `_commandTrace.Add`；失败/卡死写环不变                                                                  | `CommandTelemetry.cs`   | 编译通过；快命令不再写环           |
| Stage 2 | **R2·监控惰性化**：`ProcessEntity`(同步)/`ProcessEntityAsync` 不再预启动监视；改为在命令入口用轻量 `Stopwatch`，**仅当越过** **`SlowReport`** **才异步启动监视协程与定时器**；快命令零分配               | `CommandCoordinator.cs` | 编译通过；快命令 0 CTS/0 Timer |
| Stage 3 | **错误信息增强（可选）**：`HangEvidenceAssembler` 对 capture 失败保留 `ex.ToString()`（含堆栈）到诊断字段，而非仅 `ex.Message`                                                      | `HangDiagnostics.cs`    | 编译通过；取证信息更完整           |
| Stage 4 | **判卡归口收敛**：若采纳方向 A——`CommandTelemetry` 卡死路径复用 `HangEvidenceAssembler` 装配证据、删除 `HangForensics/StepWatchdog/HangProbeSlot` 死代码及其误导注释；若采纳方向 B——见 §7 接线清单 | 按 §4 取舍                 | 单一判卡归口；编译通过            |

> **N2/N3 处置**：Stage 4 若删 `HangForensics` 则 N2/N3 随死代码一并消除；若保留需补共享槽并发防护（如 per-run 实例 + 顺序执行约束注解）。

***

## 6. 验收

1. `Modules.Threading`、`Operation.BackEndDriven.CommandExecution` 项目编译 0 错误。
2. 复跑代表性命令脚本（含快命令批量），`_telemetry` 开启下对比：命令每百万次分配/时间增量收敛（预期消除逐命令 Timer/CTS/Task.Run）。
3. 构造慢/卡死命令（超 `SlowReport` / `HangReport`）：仍即时出声 + 落 `BackEndLog`，取证块可用、不落盘（回归无回归）。
4. 审计脚本新增判据：`OnCommandBegin` 不在健康路径写环；`MonitorSlowAsync` 仅越阈值启动。

***

## 7. 待决 / 后续接线（N1）

方向 A（推荐）与方向 B（启用 HangForensics）二选一：

- **A（默认）**：删死代码、以 CommandTelemetry + Assembler 归口。便宜、立即省健康路径开销；代价是放弃"执行段级"相位判卡能力（当前也没用上）。

- **B**：保留步级链，需完成 `ExecutionScope.CreateRoot`/`SerialWriteGate` 透传 `hang`，并先**重烘焙** **`Threading.dll`** 供各消费方引用（对齐 2026-09-03 db-read-hang §5.4 未完成的跨树接线）；同时需解决 N2（与 CommandTelemetry 划界）与 N3（并发栈防护）。

> 采纳任一方向前，先由调用方确认判卡归口，避免双写取证。

