# Cline 复用与并行重构 — 实施与迭代记录

> 日期：2026-08-08 ｜ 模块：`Agent.ClineUsage` / `ClineApi` / `FileDriven` ｜ 状态：**实施中（存在待修复项，见 §7）**
> 关联：
> - 《Cline复用与并行重构方案.md》（同目录，方案权威）
> - 《Cline传输层挂起根因分析与重构方案.md》（同目录，根因定位）
> - 《ClineDaemon池_InternalStorage可行性分析.md》（同目录，三层架构设计）

---

## 1. 背景与目标

`FileDriven/DrivenConfig/Scripts/Cline/cline_browser_test.json`（GUI 模式）反复超时。根因是把「每进程一次、时长高度波动（2.7s~56s）」的 ClineCore 冷启动放在每次请求的关键路径上，再用写死的限时参数去掐它。

治本三原则（源自方案）：
- **复用**：常驻预热 warm ClineCore，把 56s 冷启动移出请求路径；
- **并行**：有界并发（`PoolSize`），多请求同时跑、不互相阻塞、单个 daemon 挂起不拖垮其余；
- **预算收敛**：单一 deadline + 活性检测（心跳→看门狗），取代多个各自写死的限时常量，消除「框架 vs node 超时竞速」。

### 目标与度量
| 目标 | 度量 |
|------|------|
| 复用 | 预热后单次 plan/ask ≈ 10s（模型耗时），不再付 56s 冷启动 |
| 并行 | `PoolSize=N` 请求并发；超 N 排队；单 daemon 挂起不影响其余 |
| 预算收敛 | 移除 `effectiveTotal=budget+15` 与 `--timeout 60` 各自写死；统一由框架 deadline 推导 |
| 可观测 | 心跳→空闲看门狗判定「推进 vs 挂死」 |
| 回归 | `cline_browser_test.json` 在预算内给出明确结果，不再 ~100s 空收尾 |

---

## 2. 现状盘点（已具备 vs 缺失）

### 2.1 已具备的「复用 + 并行」基元（无需重建）
| 组件 | 文件 | 职责 | 状态 |
|------|------|------|------|
| 并发闸门 + 超时 + 看门狗 + 进程树终止 | `CsharpPrj/Modules/Threading/AsyncExecution/AsyncOperationScheduler.cs` | `SemaphoreSlim` 并行上限、`AbsoluteTimeout`、空闲看门狗、取消归因 | ✅ 已有 |
| Worker 池状态/编排 | `CsharpPrj/Operation/StateHoldDriven/Pooling/Core/PoolInitializer.cs` 等 | 预热 N worker、心跳探活、失败补位、状态机 | ✅ 已有 |
| daemon 池管理器 | `Agent/src/Agent.ClineUsage/Pool/ClineDaemonPool.cs` | 组合并发闸门 + 复用池；`SendAsync` 分发空闲 worker | ✅ 已有 |
| 单 worker 进程 + IPC + 心跳 | `ClineDaemonProcess.cs` / `ClineDaemonIpc.cs` | spawn `node daemon.js`、JSON-Line 帧、心跳转发、进程树 kill | ✅ 已有 |
| TS 侧常驻 daemon | `ClineApi/src/daemon.ts` | 单 `SdkTransport`（单 ClineCore）、JSON-Line 串行、10s 心跳 | ⚠️ 源码有，`build/daemon.js` 未编译 |

### 2.2 缺失 / 错误（重构要动的）
| 缺口 | 位置 | 影响 |
|------|------|------|
| `build/daemon.js` 未构建 | `ClineApi/build/` | 池前置检查失败 → 全量降级单次 node，复用+并行全失效 |
| 池懒启动，首请求付冷启动 | `ClineClient._pool`（`Lazy`）→ `SendAsync` 内 `StartAsync` | 首个请求仍付 spawn N + 56s 冷启动 |
| 预热只 spawn+ping，未触发 ClineCore init | `PoolInitializer` health-check 用 `ping` | 池起来但首真实请求仍吃 56s |
| `ClineClient._nodeGate` 串行化 | 单次 node 路径 | 单次降级路径整体串行 |

---
## 3. 实施内容（分阶段）

### Phase 0 — DataDir 决策（并发冷启动共享 data 实测）
- 同起 2~3 daemon 并发 `warmup`，观察 SQLite/锁竞争。
- **实测**：共享 `~/.cline/data` 下 2 daemon 并发 warmup 均 `ok:true`，无锁竞争 → 默认共享可行。
- **落点**：`ClineDaemonOptions.DataDir` 可开关（隔离机制备好，未默认启用）。

### Phase 1 — 复用落地
| 改动 | 文件 | 内容 |
|------|------|------|
| 构建 `daemon.js` | `ClineApi/build/daemon.js`（`npm run build`） | 修复 `daemon.ts:85` 的 `payload.limit` 类型错误后编译产出 |
| `warmup()` | `ClineApi/src/transport/sdk-transport.ts` | 新增仅初始化 ClineCore、不发起 API、超时 90s 覆盖 56s 冷启动的方法 |
| `warmup` 命令 | `ClineApi/src/daemon.ts` | 新增 `Cmd="warmup"` 分支触发 ClineCore init |
| 池预热 | `Pool/ClineDaemonPool.cs` | 新增 `WarmupAsync`/`WarmWorkerAsync`；`PickFreeWorkerAsync` 优先已预热 worker |
| warm 状态 | `Pool/ClinePoolState.cs` | 新增 `MarkWarm`/`IsWarm`/`WarmWorkerCount`；respawn 后清 warm |
| 客户端预热 | `Agent.ClineUsage/ClineClient.cs` | 新增 `PrewarmPoolAsync`；构造支持 `poolOptions`/`enableWarmup` |
| 启动接线 | `FileDriven.Business/.../ClineLibrary.cs` | 静态构造里 `Task.Run` 后台预热 + 接线池诊断钩子 |

### Phase 2 — 并行落地
- 确认池路径不经 `_nodeGate`（仅单次降级路径串行）。
- `IdleTimeout` 显式默认 60s（> 10s 心跳），激活心跳→空闲看门狗。

### Phase 3 — 预算收敛（去掉写死赛跑）
- `ClineClient.cs` 新增 `ComputeInnerTimeoutSec(int? budgetSec, int marginSec)`：内层自报超时 = 框架 deadline - margin，确保内层先于框架 kill 返回干净 JSON。
- 去掉 `effectiveTotal = budgetSec + 15`，统一 `框架 deadline = 唯一时钟`。

### Phase 4 —（可选）LoopFramework 集成
- 未实施（单次 ask/plan 为一次 LLM 调用，无需套 LoopEngine；复用/并行已在池层承担）。

---

## 4. 验证结果（截至当前）

| 项 | 结果 |
|----|------|
| `npm run build`（ClineApi） | ✅ 0 错误，产出 `build/daemon.js` |
| `Agent.ClineUsage` / `FileDriven.sln` / `MainProj.sln` 编译 | ✅ 0 错误 |
| 单元测试（ClineUsage_Unit，含新增预算推导/配置默认值） | ✅ 21/21 |
| daemon ping 协议 | ✅ `{"type":"pong"}` |
| daemon warmup 命令 | ✅ `{"ok":true,"result":{"warm":true}}` |
| 2 daemon 并发 warmup（共享 data） | ✅ 均 ok，无 data 竞争 |

### 已落地文件（本轮全部）
`ClineClient.cs`、`Pool/ClineDaemonPool.cs`、`Pool/ClineDaemonOptions.cs`、`Pool/ClinePoolState.cs`、`ClineLibrary.cs`、`ClineBrowser.json`、`cline_browser_test.json`、`daemon.ts`、`sdk-transport.ts`、`build/daemon.js`、单测 `ClineClientTests.cs` / `ClineDaemonOptionsTests.cs`。

---

## 5. 回归迭代记录（3 次 GUI 运行）

### 迭代 1（重构前基线，~100s 空收尾）
- 启动 `exec Cline_History`（单次 node 冷启动 56s）阻塞 GUI；
- `configButton` 35s 超时、`planButton` 65s 超时；
- AEF 绝对时限触发后仍拖到进程收尾（elapsedMs=100530）才落定；
- 结论：启动 history 冷启动 + 框架只能进程收尾兜底。

### 迭代 2（第一轮重构后：构建 daemon + 池预热 + 预算收敛）
- `history` 仍走**单次 node**（`HistoryAsync` 未走池）→ 启动 init 冷启动阻塞仍在；
- `WarmupAsync` **串行**预热（3×56s=168s）→ 池未及时就绪；
- `Cline_Plan` 首请求就地预热 56s > 60s 预算 → 超时；
- 共享 data 并发冷启动（history+3 daemon）→ ~100s 挂死；
- 结论：`HistoryAsync` 未走池、预热串行、共享 data 竞争、首请求预算不够。

### 迭代 3（全套修复后：history 走池 + 删启动 history + 并行预热 + Cline_PoolReady + DataDir 隔离）
- ✅ 启动已无 `[ClineNode] history` 单次冷启动；`version` 毫秒级；
- ❌ `configButton` 仍 35s 超时（免 node 却卡）；
- ❌ `Cline_PoolReady` 卡死 ~4 分钟（池从不就绪 → plan 链式失败）；
- 结论：背景 3-daemon 并行预热抢占资源拖慢 config；**C1 DataDir 隔离（空 CLINE_HOME）未验证、疑似破坏预热** → 池恒不就绪。

---

## 6. 当前根因诊断（迭代 3 遗留）

### 6.1 `configButton` 35s 超时（免 node 却卡）
- `ConfigAsync` 是免 node 直读 `cline.config.json`，理论上毫秒级；`version`（同样免 node）实测毫秒级成功。
- 因此 config 超时**不是 config 自身**，而是函数执行被**启动即背景 3-daemon 并行预热**抢占/阻塞。
- 触发点：`ClineLibrary` 静态构造里的 `_ = Task.Run(() => Client.PrewarmPoolAsync())`。

### 6.2 池从不就绪（`Cline_PoolReady` 卡 ~4 分钟）
- `C1 DataDir 隔离`：每 worker 独立空 `CLINE_HOME`。此前 Phase 0 预热实测用的是**共享 `~/.cline`**，从未验证空 `CLINE_HOME` 下 ClineCore 能否预热。
- 隔离极可能使 `warmup` 失败/挂起 → `WarmWorkerCount` 恒 0 → `Cline_PoolReady` 等满超时，plan 再链式失败，共约 4 分钟。

### 6.3 时间线（迭代 3）
```
00:28:09 启动 → ClineLibrary 静态构造 → 后台 Task.Run 预热（3 daemon，C1 隔离）
00:28:17 version 毫秒 ✓（免 node）
00:28:17→00:28:52 configButton 35s 超时 ✗（被背景预热抢占）
00:28:52 Cline_PoolReady 开始 → 池恒不就绪 → 卡 ~4 分钟 → plan 失败
```

---

## 7. 待修复方案（建议）

| # | 改动 | 文件 | 理由 |
|---|------|------|------|
| 1 | **回退 C1**：默认 `DataDir` 改回 `null`（共享）；隔离机制保留为可选参数 | `Pool/ClinePoolState.cs` | 共享并发预热已被 Phase 0 实测通过；隔离空目录未验证、疑似破坏预热 |
| 2 | **移除启动自动预热**（删静态构造里的 `Task.Run`；保留 `PrewarmPoolAsync` 方法） | `ClineLibrary.cs` | 停止背景 3-daemon 抢占，config/version 恢复毫秒级；改为经 `Cline_PoolReady` 按需预热 |
| 3 | **重排测试**：`Cline_PoolReady` 提前到 version 之后、config 之前 | `cline_browser_test.json` | 先干净完成一次预热（56s 阻塞），config/plan 之后均秒级 |
| 4 | **config 诊断**：`Cline_Config` 包裹 entry/elapsed 计时日志 | `ClineLibrary.cs` | 确认移除背景预热后 config 是否恢复；若仍慢则暴露真实卡点 |
| 5 | 构建 + 单测 + 回归 | — | 验证 config 毫秒、plan 预算内出真结果 |

**验收判据（回归）**
1. 预热后单次 plan/ask ≈ 10s 且 `ok:true` 真实结果；
2. `PoolSize=N` 并发、超 N 排队、单 daemon 挂起隔离；
3. 冷启动仅预热期一次（56s 付在启动/`Cline_PoolReady`，不在 plan 路径）；
4. `config/version` 毫秒级；`planButton` 预算内明确结果，不再 ~100s 空收尾；
5. 框架预算与 node/daemon 超时不再赛跑（`--timeout ≤ 剩余 - margin`）。

---

## 8. 风险与回滚

| 风险 | 缓解 | 回滚 |
|------|------|------|
| R1 共享 `~/.cline/data` 并发锁竞争 | 先实测后 DataDir 隔离（届时需先验证隔离 CLINE_HOME 预热） | `DataDir` 参数可开关 |
| R2 daemon 崩溃/挂起 | `RespawnAsync` 补位 + 池健康探活 | 保留单次 node 降级路径 |
| R3 预热增加启动时间 | 后台/`Cline_PoolReady` 一次性预热；预热未完成时降级单次 | 预热开关可关（`EnableWarmup`） |
| R4 N×ClineCore 内存占用 | `PoolSize` 上限 + `IdleTimeout` 缩容 | 调 `PoolSize` |
| R5 预算收敛改动回归 | 单次降级路径保留 deadline 兜底；分阶段提交可单独回退 | 各提交独立 |

---

> 附注：工作区中存在先前会话遗留的未提交改动（`PoolInitializer.cs`/`RequestTracker.cs`/`WorkerStateStore.cs`/`WorkerStateInfo.cs`/各 `.csproj` 等），与本次重构文件不重叠；提交时请区分。

