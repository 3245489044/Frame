# Cline 池与 CSG 对接 — 剩余修复清单

> 日期：2026-08-08 ｜ 范围：`Agent.ClineUsage` / `Operation.StateHoldDriven.Daemon` / `ClineApi` / `Protocol/CSG`
> 依据：对当前（重构后未提交）代码与实测日志（`warm=0`、180s 静默就绪失败）的分析。
> 状态：CSG 跨语言协议**双端对接已完成**（见 §0）；本清单为**尚未修复**的项。

---

## §0 已完成（勿重复实施）

| 项 | 内容 |
|---|---|
| TS 侧 CSG 协议 | `TypeScriptPrj/TsWorkspace/Protocol/CSG`（`csg-protocol`）：CanonicalForm / Builder / Protocol / 状态机适配器 / 验证 / URI；已注册 pnpm workspace，构建通过 |
| ClineApi 接入 | `ClineApi/src/csg/state-machine.ts`；`engine.ts` 发 `sendCsg("ready"/"api")`；`daemon.ts` 中继 `type:"csg"` 帧 |
| C# 对称化 | `CSG.Unified/CanonicalForm.cs` 的 `ToJson/FromJson` 启用 `JsonStringEnumConverter`（字符串枚举） |
| C# 消费 csg | `DaemonResponse.Form` 解析、`csg→Trace` 路由（`ClineDaemonIpc.cs`）、`DaemonCsg` 诊断（`DaemonPoolState.cs`）、`ClineDaemonCsg` 日志模板 |
| 验证 | 双端 CanonicalForm 往返、C# 解析 csg 帧、三项目（`csg-protocol`/`cline-api`/`Agent.ClineUsage`）构建通过 |

---

## §1 剩余修复项

### S1 — 探活缺陷（P0，直接导致 `warm=0`）

- **位置**：`Operation/StateHoldDriven/Daemon/DaemonFrame.cs`（`IsTerminal`）、`Agent.ClineUsage/Pool/ClineDaemonHost.cs:77`（`ProbeAsync`）、`ClineDaemonIpc.cs:66`（`SendAsync`）
- **问题**：daemon 对 ping 回 `{"id":"ping-…","type":"pong"}`；`DaemonResponse.IsTerminal => Type is null && (Ok || Error is not null)`，pong 的 `Type` 非空 → 判为非终结帧；`ProbeAsync` 经 `SendAsync` 只等终结帧 → TCS 永不 resolve；`ProbeTimeout` 缺省 `null` → 无超时兜底 → `StartAsync` 在探活处**无限挂起**，warmup 永不开始。
- **影响**：`Cline_PoolReady` 180s 静默 → 就绪失败，`warm=0`、`末次预热错误:(无)`。
- **修复**：探活用专用 pong 终结关联（`ProbeAsync` 经 `Frame` 事件或独立 TCS 结算 pong），或 `ClineDaemonIpc` 对 `type=="pong"` 且请求为 ping 时 resolve 对应 TCS；并给 `ProbeAsync` 注入**非空 `ProbeTimeout`** 兜底。
- **验收**：池能探活通过 → warmup 正常启动；探活失败在 `ProbeTimeout` 内报错，而非无限挂起。

### S2 — warmup 单一时钟（P1，消除竞速归因遮蔽）

- **位置**：`ClineDaemonTransport.CreateWarmRequest`（`ClineDaemonTransport.cs:26`）与 `WarmWorkerAsync`（`DaemonPool.cs:195`，`AbsoluteTimeout=budget`）、`daemon.ts` watchdog（`budgetMs`）
- **问题**：warmup 内层 `warmupTimeoutSec = (int)budget.TotalSeconds`（=180s），AEF `AbsoluteTimeout=180s`，node watchdog `180s`——**三处同一时间约束竞速**；普通请求已走 `ComputeInnerTimeoutSec = deadline - margin`（`ClineClient.cs:208`），warmup 未走。
- **影响**：超时归因被竞速遮蔽（C# 先抛"池未就绪"，node 的"engine 超时"来不及回 → 归因空）。
- **修复**：`CreateWarmRequest` 用 `budget - margin`（复用 `ComputeInnerTimeoutSec` 语义）。
- **验收**：warmup 超时时归因带 node 阶段而非空；无三方竞速。

### S3 — 先快照后清理再抛（P1，修复 `InformationMonotonicity` 违例）

- **位置**：`DaemonPool.EnsureReadyAsync`（`DaemonPool.cs:90`，`KillAllDaemons()` 在 `DescribeWorkers()` 之前）
- **问题**：预算耗尽先 `KillAllDaemons()` 清空 `_runtime`，再 `DescribeWorkers()` → 报错时"各 worker 状态/阶段"恒空。
- **影响**：失败归因零诊断价值。
- **修复**：`snapshot = DescribeWorkers()` → `KillAllDaemons()` → `throw(snapshot)`。
- **验收**：就绪失败日志带真实每 worker 状态/阶段。

### S4 — worker 状态收敛为单一状态机（P2，根治"多处持有状态"）

- **位置**：`ClineDaemonHost._isReady`、`DaemonPoolState.WorkerRuntime.State`、`_started`
- **问题**：就绪(`bool`)+生命周期(`enum`)+已启动(`bool`)三处拼装，无显式迁移表，任一拍可漂移。
- **修复**：收敛为一个显式状态机 `Spawned → Running → Ready → InFlight → Dead`，探活/预热/补位/退出均为**迁移动作**；非法迁移拒绝。
- **验收**：状态转移经迁移表唯一路径；`IsReady` 与 `runtime.State` 不再分叉。

### S5 — lastStage / pending 源-投影声明 + 协议帧 schema 化（P2）

- **位置**：`ClineDaemonHost._lastStage` 与 `daemon.ts EngineHost.lastStage` 双份镜像；`ClineDaemonIpc._pending` 与 `daemon.ts pending` 两层各持一份。
- **问题**：lastStage 无权威归属（Node/C# 各存一份）；请求关联在两层各持一份，任一层丢帧即悬空。
- **修复**：声明 lastStage 单一源（Node 上报、C# 只读投影）；pending 两侧用同一 deadline 原子化取消；协议帧（`csg`/`trace`/`heartbeat`/`pong`/`result`）用单一 schema 约束（可对齐 CSG schema）。
- **验收**：无重复状态归属；任一层丢帧有兜底归因。

---

## §2 附带修复项（CSG 相关）

### A — `CSG.Unified.Tests` 引用坏项目（P1，测试不可运行）

- **位置**：`CentralTest/CSharpTest/Protocol.Tests/CSG.Unified.Tests/CSG.Unified.Tests.csproj`
- **问题**：引用不存在的 `CSG.Unified.Abstractions` / `Core` / `Adapters` 项目（旧多项目布局残留）→ `dotnet test` 编译失败（`CS0234`）。
- **修复**：改引单一 `CsharpPrj/Protocol/CSG/CSG.Unified/CSG.Unified.csproj`，或重建测试项目引用。
- **验收**：`dotnet test` 通过。

### B — CSG schema 与 C# 序列化不一致（P2）

- **位置**：`schemas/csg-canonical-form.schema.json`（顶层 `additionalProperties:false`，仅 `nodes/edges/metadata`）vs `CanonicalForm.State` 会序列化 `state` 字段。
- **问题**：C# 序列化输出含 `state`，按 schema 校验会失败。
- **修复**：更新 schema 允许 `state`（或对齐定义），保证单一 schema 源与双端一致。
- **验收**：C#/TS 序列化均通过 schema 校验。

---

## §3 建议实施顺序

| 阶段 | 项 | 说明 |
|---|---|---|
| P0 止血 | **S1** | 探活终结关联 + 探活超时兜底 → 恢复 `warm=0`、池可就绪 |
| P1 | **S2、S3、A** | 单一时钟、先快照后清理、修复测试引用 |
| P2 治本 | **S4、S5、B** | 状态机收敛、源/投影声明 + schema 化 |

> 说明：S1/S2/S3 改动面小、相互独立、可单独回退；S4/S5 为状态管理架构收敛，建议在 P0/P1 稳定后实施。
