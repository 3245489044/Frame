# Cline 复用与并行重构方案（daemon 池落地 + 预算收敛）

> 日期：2026-08-07 ｜ 模块：`Agent.ClineUsage` / `ClineApi` / `WorkFlowMethod`(Threading/LoopFramework) ｜ 状态：方案（待评审）
> 关联：
> - 《Cline传输层挂起根因分析与重构方案.md》（同目录，根因定位）
> - 《ClineDaemon池_InternalStorage可行性分析.md》（同目录，三层架构设计）

---

## 1. 背景与问题（修正版根因）

`FileDriven/DrivenConfig/Scripts/Cline/cline_browser_test.json`（GUI 模式）反复超时。经实证（`node build/cli.js plan --timeout 60 --trace`）修正此前"凭据/端点坏了"的误判：

```
实测：plan --timeout 60 --trace
  → exit=0, elapsed=67870ms, stdout { "ok": true, "sessionId": "..." }
  trace: load-sdk-start → load-sdk-ok(≈56s) → ensure-core-ok → phase2-core-start → phase2-core-start-ok
```

**请求是成功的**（`ok:true`、`phase2-core-start-ok`），但总耗时 67.9s。时间拆解：

| 阶段 | 耗时 | 性质 |
|------|------|------|
| `loadSdk()` / ClineCore 本地后端冷启动 | **≈ 56s**（实测 2.7s~56s 波动） | 确定性初始化，**每进程一次** |
| `core.start()` 模型生成 | **≈ 10s**（plan + thinking=medium） | 模型耗时 |
| 合计 | ≈ 68s | — |

### 1.1 为什么 GUI 里"还是超时"

1. **每次调用都付 56s 冷启动**：`build/daemon.js` **未构建**（`src/daemon.ts` 存在但没编译），`ClineDaemonPool` 的前置检查 `File.Exists(daemonScriptPath)` 失败 → 池 `StartAsync` 抛异常 → `ClineClient.RunTaskAsync` `catch` 降级到**单次 node 路径**（`RunNodeAsync`），每次都是全新进程 + 全新 ClineCore → 每次都吃 56s 冷启动。
2. **框架预算 vs node 超时竞速**：`Cline_Plan` 的 ECS 绝对预算 = 60s = 传入 node 的 `--timeout 60`；但 node 自报超时需 `60s + 5s 开销 ≈ 65s`。框架在 60s 先取消/杀 node（`ClineClient` 的 `totalCts` 链接外部 ct 同步触发）→ node 在 `phase2-core-start-ok` 前被杀死 → 结果框空 → 报"等待超时 FAIL"。
3. **"免 node"路径没落到运行态**：源码里 `version/config` 已免 node 直读文件，但 GUI 里 `configButton` 仍 35s 超时——与 init 里挂死的 `history`（单次 node 冷启动 56s 未完成）在单工作线程上排队、且框架绝对终止只能到进程收尾才落定，这两点吻合。
4. **复用 + 并行两空**：池路径因缺 `daemon.js` 从未生效；`ClineClient._nodeGate`（`SemaphoreSlim(1,1)`）还把单次降级路径整体串行化。

### 1.2 核心症结

**把一个"每进程一次、时长高度波动（2.7s~56s）"的冷启动放在每次请求的关键路径上，再用一个写死的限时参数去掐它。** 治本不是"调大超时"，而是：

- **复用**：常驻预热 warm ClineCore，把 56s 冷启动移出请求路径；
- **并行**：有界并发，多请求同时跑，不互相阻塞；
- **预算收敛**：单一 deadline + 活性检测，取代多个各自写死的限时常量。

---

## 2. 目标

| 目标 | 度量 |
|------|------|
| 复用 | 预热后单次 plan/ask ≈ 10s（模型耗时），不再付 56s 冷启动 |
| 并行 | `PoolSize=N` 个请求同时执行；超 `PoolSize` 排队；单个 daemon 挂起不拖垮其余 |
| 预算收敛 | 移除 `effectiveTotal=budget+15` 与 `--timeout 60` 的各自写死；统一由框架 deadline 推导 |
| 可观测 | 心跳→空闲看门狗判定"推进 vs 挂死"，不再靠固定总时长 |
| 回归 | `cline_browser_test.json` 在预算内给出明确结果，不再 ~100s 收尾空结果 |

---

## 3. 现状盘点（已具备 vs 缺失/错误）

### 3.1 已具备的"复用 + 并行"基元（无需重建）

| 组件 | 文件 | 承担的职责 | 状态 |
|------|------|-----------|------|
| 并发闸门 + 超时 + 看门狗 + 进程树终止 | `CsharpPrj/Modules/Threading/AsyncExecution/AsyncOperationScheduler.cs` | `SemaphoreSlim(maxConcurrency)` 并行上限；`AbsoluteTimeout`；空闲看门狗；取消归因 | ✅ 已有 |
| Worker 池状态/编排 | `CsharpPrj/Operation/StateHoldDriven/Pooling/Core/PoolInitializer.cs` + `WorkerStateStore` + `RequestTracker` + `PoolHealthRegistry` | 预热 N 个 worker、心跳探活、失败补位、状态机 | ✅ 已有 |
| daemon 池管理器 | `Agent/src/Agent.ClineUsage/Pool/ClineDaemonPool.cs` | 组合上面两者：并发闸门 + 复用池；`SendAsync` 分发空闲 worker | ✅ 已有 |
| 单 worker 进程 + IPC + 心跳 | `ClineDaemonProcess.cs` / `ClineDaemonIpc.cs` | spawn `node daemon.js`、JSON-Line 帧、心跳转发、进程树 kill | ✅ 已有 |
| TS 侧常驻 daemon | `ClineApi/src/daemon.ts` | 单 `SdkTransport`（单 ClineCore），JSON-Line 串行处理，10s 心跳 | ⚠️ 源码有，`build/daemon.js` 未编译 |

### 3.2 缺失 / 错误（本次重构要动的）

| 缺口 | 位置 | 影响 |
|------|------|------|
| **`build/daemon.js` 未构建** | `ClineApi/build/` | 池前置检查失败 → 全量降级单次 node，复用+并行全失效 |
| **池懒启动，首请求付冷启动** | `ClineClient.cs` `_pool`（`Lazy<ClineDaemonPool>`）→ `SendAsync` 内 `_state.StartAsync` | 首个 plan/ask 仍付 spawn N + 56s 冷启动，压在请求路径 |
| **预热只 spawn+ping，未触发 ClineCore init** | `PoolInitializer` 健康检查用 `ping`（不碰 ClineCore） | 即便池起来，首个真实请求仍吃 56s 冷启动 |
| **`_nodeGate` 串行化** | `ClineClient.cs:66` `SemaphoreSlim(1,1)` | 单次降级路径整体串行，无并行 |
| **预算各自写死且赛跑** | `RunNodeCoreAsync` `effectiveTotal=budget+15`；node `--timeout=budget` | 框架 60s 先杀 node，成功点 68s 被掐死 |
| **loop 用错层** | `WorkFlowMethod/LoopFramework` | 是单任务顺序循环引擎，不是资源池；复用/并行必须在池层 |

### 3.3 LoopFramework 定位（澄清，防用错）

`LoopEngine<TContext,TState>` 内部是单 `while` 顺序执行（`DecideAsync → ExecuteAsync → …`），共享单一 `_contextManager`/`_stateMachine`/`_cts`，**只服务一个任务**。它**不提供**资源复用或并发控制。

- 正确用途：**每个 Cline 任务 = 一个 `LoopEngine`（可选）**，其 `IActionExecutor`/`ILLMService` 实现去**调用池**。
- 复用/并行职责在池层（§3.1），不在 LoopFramework。

---

## 4. 目标架构（分层）

```
多个并发任务/请求
      │
      ▼
LoopEngine（每个任务一个，跑 agent 循环）        ← LoopFramework（每任务顺序；可选）
      │  IActionExecutor / ILLMService → 委托池
      ▼
ClineDaemonPool（复用 + 有界并行）                ← WorkerPool(复用) + AsyncOperationScheduler(并行)
      │  PoolSize=N 个预热 warm daemon；并发闸门=N
      ▼
ClineCore 本地后端（node daemon，单 ClineCore 串行复用）   ← daemon.ts
```

- **复用** = `PoolInitializer.StartAsync(PoolSize)` 启动预热 N 个 daemon，并**触发 ClineCore init**（不是仅 spawn+ping），把 56s 冷启动移到启动期后台。
- **并行** = `AsyncOperationScheduler(concurrency=PoolSize, idleWindow)` 的 `SemaphoreSlim` 闸门；`SendAsync` 经 `PickFreeWorkerAsync` 分发空闲 warm daemon → 最多 N 个请求同时跑。
- 单 daemon 内串行（`daemon.ts` `pending = pending.then(...)`），并行靠 **N 个 daemon**，避免多 ClineCore 争抢共享 `~/.cline/data`。


---

## 5. 重构方案（分阶段）

### Phase 0 — 前置验证：数据隔离实测（决定 DataDir 策略）

参照可行性文档 R1：`~/.cline/data` 共享下并发可行性未实测。

- **验证**：同起 2~3 个 `node build/daemon.js`，各发 `plan --trace`，观察是否 SQLite/锁竞争卡死。
- **决策**：
  - 可共享 → `DataDir=null`（少资源）；
  - 不可共享 → `ClineDaemonOptions.DataDir` 指定根，`ClineDaemonProcess` 已按 `workerId` 设 `CLINE_HOME`（`Path.Combine(dataDir, workerId)`）。

### Phase 1 — 复用落地（消灭 56s 冷启动）

**1.1 构建 `daemon.js`**
```
cd TypeScriptPrj/TsWorkspace/ClineApi && npm run build   # 产出 build/daemon.js
```

**1.2 池显式启动 + 预热 ClineCore（不在请求路径）**
- 当前 `_pool` 是 `Lazy<ClineDaemonPool>`，首请求才 `_state.StartAsync` → 首请求付冷启动。改为**应用启动期预热**。
- 预热 = spawn N 个 daemon + **触发 ClineCore init**（`PoolInitializer` 的 `ping` 不碰 ClineCore，仅做存活探活，无法预热冷启动）。
- 落点：给池加 `WarmupAsync()`——对每个 worker 发一个轻量请求（如 `history --limit 0` 或最小 `ask`），让 `loadSdk`+`ClineCore.create` 完成（约 56s，后台执行）；完成后该 worker 标记 warm，可进入分发。

**1.3 启动钩子接线**
- 在 FileDriven 启动序列（`PostInitHooks` / `MainForm OnLoad` 所在装配）注册：后台预热池，不阻塞 UI 主线程；预热期间请求仍可走单次降级路径兜底。

### Phase 2 — 并行落地

**2.1 放开池路径的并发**
- 池路径并发已由 `_slotGate` + `AsyncOperationScheduler(concurrency)` 管住；`ClineClient._nodeGate`（`SemaphoreSlim(1,1)`）**只保留给单次降级路径**，确保池路径不被它串行化。

**2.2 定 `PoolSize`（并发上限）**
- 默认 3（`ClineDaemonOptions.PoolSize=3`）。每 daemon ≈ 整棵 ClineCore，内存/成本不低；按机器内存与真实吞吐调整，`IdleTimeout` 缩容。

**2.3 心跳 → 空闲看门狗（活性替代固定总超时）**
- 链路已具备：`daemon.ts` 10s 心跳 → `ClineDaemonIpc.Frame` → `onProgress()` → `op.ReportProgress(null)` → `AsyncOperationScheduler` 看门狗"有推进即重置"。
- 验证该链路确实接线（`SendAsync` 已传 `() => op.ReportProgress(null)`）；确保长任务不被空闲看门狗误杀。

**2.4 DataDir 隔离（依 Phase 0 决策）**
- 需要时设 `ClineDaemonOptions.DataDir`，每 worker 独立 `CLINE_HOME`。

**2.5 背压 / 弹性**
- N 全忙 → `_slotGate.WaitAsync` 排队；或按需 `PickFreeWorkerAsync` → `RespawnAsync` 扩容。
- **禁止无界 spawn**：每个 daemon 是整棵 ClineCore（内存/56s 成本大），用 `IdleTimeout` 回收空闲 worker。

### Phase 3 — 预算收敛（去掉写死限时参数的赛跑）

**3.1 单一 deadline 传播**
- 以框架 `InvocationBudget` 的绝对 deadline 为唯一时钟，向下传"剩余时间"；各层不再各自写死常量。
- 内层 node/daemon 的 `timeoutSec` 由 `剩余 - margin` 推导（margin 覆盖启动开销），而非与框架预算相同导致赛跑。

**3.2 修正 `ClineClient` 兜底**
- 单次降级路径：`effectiveTotal = budgetSec + 15` 与 `--timeout = budgetSec` 改为由外层 deadline 推导，保证 node 自报超时先于框架 kill（`--timeout ≤ 剩余 - margin`）。

**3.3 心跳活性兜底**
- 对 daemon 路径以心跳判定"推进 vs 挂死"，替代平坦总时长；单次降级路径在 stream 化前保留 deadline 兜底。

### Phase 4 —（可选）集成 LoopFramework

- 若单个 Cline 任务需要多轮 ReAct/LLM 决策循环，用 `LoopEngineBuilder` 装配一个 `LoopEngine`，其 `IActionExecutor`/`ILLMService` 实现委托 `ClineDaemonPool.SendAsync`。
- LoopFramework 仅作"单任务内循环编排"，不承担复用/并发。


---

## 6. 关键设计决策

| 决策 | 选项 | 结论 / 理由 |
|------|------|-------------|
| 预热是否触发 ClineCore init | 仅 spawn+ping vs 发暖机请求 | **必须触发 ClineCore init**（ping 不碰 ClineCore）；否则首请求仍吃 56s |
| 并行模型 | 单后端多路复用 vs N daemon | **N daemon 有界并发**（`daemon.ts` 单 ClineCore 串行 + 共享 data 锁风险）；并行度 = PoolSize |
| 并发控制 | 自行 Semaphore vs 复用 `AsyncOperationScheduler` | **复用 AEF**：闸门 + 超时 + 看门狗 + 进程树 kill + 取消归因全具备 |
| 数据目录 | 共享 `~/.cline/data` vs 每 worker `CLINE_HOME` | **依 Phase 0 实测**；默认倾向独立（R1 高风险） |
| 超时语义 | 固定总时长 vs deadline+心跳 | **单一 deadline 传播 + 心跳活性**；去掉写死常量赛跑 |
| 池化 vs 单次降级 | 二选一 | **池化为主，单次降级兜底**（池未就绪/daemon 全挂时） |

---

## 7. 实施步骤与验收标准

| 步骤 | 动作 | 产出 |
|------|------|------|
| 1 | Phase 0：同起 2~3 个 daemon `plan --trace` 实测共享 data 并发 | DataDir 决策 |
| 2 | `npm run build` 产出 `build/daemon.js` | daemon 可运行 |
| 3 | `ClineDaemonPool` 加 `WarmupAsync()`（发暖机请求触发 ClineCore init） | 复用落地 |
| 4 | 启动序列接线：后台预热池，不阻塞 UI | 冷启动移出请求路径 |
| 5 | 放开 `_nodeGate` 对池路径的串行化；定 PoolSize；验证心跳→看门狗 | 并行落地 |
| 6 | 预算收敛：`ClineClient` 兜底由 deadline 推导（Phase 3） | 去掉赛跑 |
| 7 | 重建 `FileDriven.sln → BuildOutput`，回归 `cline_browser_test.json` | 验收 |

**验收判据**
| # | 判据 |
|---|------|
| 1 | 预热后单次 plan/ask ≈ 10s，且 `ok:true` 返回真实结果 |
| 2 | `PoolSize=N` 个请求并发执行；超 `PoolSize` 排队；单 daemon 挂起其余不受影响 |
| 3 | 冷启动仅预热期一次（56s 付在启动后台，不在请求路径） |
| 4 | `config/version` 毫秒级；`click planButton` 在预算内返回明确结果，不再 ~100s 空收尾 |
| 5 | 框架预算与 node/daemon 超时不再赛跑（`--timeout ≤ 剩余 - margin`） |

---

## 8. 风险与回滚

| 风险 | 缓解 | 回滚 |
|------|------|------|
| R1 共享 `~/.cline/data` 并发锁竞争 | Phase 0 实测后 DataDir 隔离（每 worker `CLINE_HOME`） | DataDir 参数可开关 |
| R2 daemon 崩溃/挂起 | `RespawnAsync` 补位自愈 + 池健康探活 | 保留单次 node 降级路径 |
| R3 预热增加启动时间（N×56s） | 后台并行预热 + 预热未完成时降级单次 | 预热开关可关 |
| R4 N×ClineCore 内存占用 | `PoolSize` 设上限 + `IdleTimeout` 缩容 | 调 PoolSize |
| R5 预算收敛改动引入回归 | 单次降级路径保留 deadline 兜底，逐层验证 | 分阶段提交可单独回退 |

---

## 9. 文件落点

| 文件 | 改动 |
|------|------|
| `TypeScriptPrj/TsWorkspace/ClineApi/build/daemon.js` | 构建产出（`npm run build`） |
| `Agent/src/Agent.ClineUsage/Pool/ClineDaemonPool.cs` | 新增 `WarmupAsync()`（触发 ClineCore init）；显式启动 API |
| `Agent/src/Agent.ClineUsage/Pool/ClinePoolState.cs` | 暴露/编排预热（给 worker 发暖机请求） |
| `Agent/src/Agent.ClineUsage/ClineClient.cs` | 池路径放开 `_nodeGate`；启动预热接线；`RunNodeCoreAsync` 兜底 deadline 推导 |
| `Agent/src/Agent.ClineUsage/Pool/ClineDaemonOptions.cs` | `PoolSize` / `DataDir` / `IdleTimeout` 实例化 |
| `FileDriven/FileDriven.Business/Registrations/Cline/ClineLibrary.cs` | （视需要）池预热启动钩子接入 |
| `FileDriven/` 启动装配（PostInitHooks/OnLoad） | 后台预热池 |
| `docs/analysis/2026/8/8.7/` | 本方案 + 更新可行性文档决策项 |

