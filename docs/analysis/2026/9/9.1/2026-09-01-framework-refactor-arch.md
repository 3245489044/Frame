# 框架级彻底重构方案 — 消除 Agent 循环持久化卡死（架构根治）

> **日期**：2026-09-01
> **定位**：承接 `2026-09-01-agent-loop-persistence-hang-refactor.md`（症状与补丁层处置），本文件为**架构层根治设计**。
> **方式**：框架级重构（非打补丁），对齐 `.clinerules`（axioms/formula/logic）治理原则，把「声明式不变式」落成「机制强制」。
> **一句话**：本次卡死不是单个 bug，而是**确定性内核写入口不唯一 + 持久化内联热路径 + 线程模型混杂 + 终止不可作用于阻塞 I/O** 四个架构缺口叠加的必然结果。本方案以 6 项重构（R1–R6）根治，分 P0–P5 落地。

---

## 〇、问题定性：为什么补丁治标不治本

| # | 架构缺陷 | 卡死证据 | 补丁为何失效 |
|---|---------|---------|-------------|
| **D1** | **多写门 ≠ 单一确定性核心写入入口** | SQLite 非事务写用 `DataUniversal.lock(Gate)`、事务写用 `SqliteWriteGate`、LiteDB 图写用 `ReferenceGraphCoordinator._gate`，三套互不协调 | 各自加 5s 超时，只是每个水龙头装减压阀，没有消除"多门 + 跨介质无全局写序"的病根 |
| **D2** | **持久化内联在轮次执行热路径**（无 Outbox/解耦） | `NodeGraphOrchestrator` 的 foreach 步内直接 `await OnAfterStepAsync → PersistAsync`，任一存储卡顿冻结整轮 → `provide_prompt` 永不开始 | P1 用"超时放弃"兜底，但后台线程/锁仍泄漏，且默认关闭 |
| **D3** | **线程模型混杂：后台计算循环与 UI/WebView2/ECS 交织** | 日志 `CoreWebView2 can only be accessed from the UI thread`、ECS 5s 封送超时；`RunContext.Enter` 用 AsyncLocal 跨线程透传 | 不隔离线程，靠取消令牌救不了同步封送死锁 |
| **D4** | **终止语义在阻塞点不可执行** | 引擎只在循环头判定取消；阻塞的 SQLite/LiteDB 同步写不响应取消令牌；P1 用 `Task.WhenAny` 放弃而非取消 | 公理宣称"预算终止引擎内强制"，但存储层是同步阻塞的，强制不了 |
| **D5** | **声明式治理与运行时强制断裂** | `SingleImplementation`/`RedundancyElimination` 降级为 `[Strategy]`（元级指导，不强制）→ 平行实现必然再出现（三个写门即证据） | `.clinerules` 校验器只查符号引用，不查运行时写入口是否唯一 |
| **D6** | **`MaxConcurrency=1` 掩盖而非解决并发模型** | 写门竞争看似不激烈，实为串行掩盖共享状态 | 一旦并发>1，三套门 + 共享 `SchedulerState` + AsyncLocal 立即暴露锁序问题 |

**结论**：P0/P1/C3 是在四个独立水龙头上各装减压阀；只要还保持"多写门 + 内联持久化 + 后台/UI 交织 + 同步阻塞 I/O"，同样的卡死换存储/路径即复发。必须从架构层收敛。

---

## 一、目标架构（Target Architecture）

```
                 ┌────────────────────────────────────────────────────┐
                 │              Agent 运行时（纯后台 worker）           │
                 │  Deterministic Core（单根 AgentTaskItem / Context）  │
                 │  · 执行只改内存确定性状态 + 追加 Outbox（快，不落盘）   │
                 │  · 单步/单轮可取消超时（linked CTS，真正中断 I/O）      │
                 └────────────────────────────────────────────────────┘
                          │ 消费 Outbox（后台 writer，可取消异步）
                          ▼
                 ┌────────────────────────────────────────────────────┐
                 │         单一写协调器 WriteCoordinator（全局唯一）      │
                 │         · 全局单写门（SQLite + LiteDB 同一写序）       │
                 │         · 所有 store 实例由此构造（无旁路写）           │
                 └────────────────────────────────────────────────────┘
                          │ 可取消异步 I/O
              ┌───────────┼───────────────┐
              ▼           ▼               ▼
         SQLite(agent.db)  LiteDB(graph)   KeyMapping(SQL)
         ur_nodes/ur_tasks                ur_keymappings
              │  UI 封送仅经 IUiDispatcher（显式、超时、失败隔离，禁止 sync-over-async）
              ▼
         WinForms / WebView2 / ECS（UI 线程）
```

**核心设计原则（每条对应消除一个 D）**：
- **单一写入口**：任何存储写必经 `WriteCoordinator`（消除 D1、堵死 D5）。
- **持久化解耦**：执行热路径只 append Outbox；落库由后台 writer 消费（消除 D2）。
- **线程隔离**：运行时无同步上下文依赖；UI 交互仅经 `IUiDispatcher`（消除 D3）。
- **可取消 I/O**：存储层全面异步 + 取消令牌贯穿；超时即取消而非放弃（消除 D4）。
- **机制强制**：写路径审计守卫，平行实现 fail-fast（消除 D5、D6）。

---

## 二、重构设计（R1–R6）

### R1 单一写协调器 `WriteCoordinator`（消除 D1/D5/D6）

**现状**：三套互不协调的写门（`DataUniversal.lock(Gate)` / `SqliteWriteGate` / `ReferenceGraphCoordinator._gate`），无全局写序 → 死锁/悬挂第一性来源。

**设计**：
```text
IWriteCoordinator（唯一写入口，进程级）
  ├─ Run<T>(Func<T> action, ct)         // 全局单写门 SemaphoreSlim(1,1)，有界等待 + finally 释放
  ├─ RunGraph<T>(Func<T> action, ct)    // LiteDB 写，与 SQLite 写共用同一门（固定写序，杜绝锁序反转）
  └─ 构造工厂：所有 store（SqliteStore / SqliteTransaction / NodeStore / KeyMappingStore /
                 ReferenceGraphCoordinator）一律由协调器构造，注入同一门实例
```

**落地**：
1. 新建 `WriteCoordinator`（收敛 `SqliteWriteGate` 的语义，升级为全存储唯一门）；
2. `SqliteTransaction.TransactionalSession` 与 `DataUniversal` 统一改经 `WriteCoordinator.Run`（消除"事务写/非事务写两套门"）；
3. `ReferenceGraphCoordinator` 注入同一门实例，`MutateAsync/ReadLocked` 改经协调器（消除实例 `_gate`）；
4. 加**固定写序**：任何跨介质写（SQLite + LiteDB）按 `SQLite → LiteDB` 顺序持门，禁止反向，杜绝锁序反转。

**消除**：D1（多门归一）、D5（平行实现无立足点，写路径唯一）、D6（并发>1 时写仍串行正确，只是低并行，工作量可接受）。

**验收**：全库搜索写入口，仅 `WriteCoordinator.Run*` 一种；删除 `SqliteWriteGate`/`ReferenceGraphCoordinator._gate`/`DataUniversal.lock(Gate)` 三套旧门。

---

### R2 持久化 Outbox 解耦（消除 D2，直接根治卡死）

**现状**：`NodeGraphOrchestrator` 的 foreach 步内同步 `await OnAfterStepAsync → PersistAsync`（SQLite 事务 + KeyMapping + LiteDB 图写），任一存储卡顿冻结整轮。

**设计**：
```text
执行热路径（guard_input/provide_prompt/...）
  └─ ExecuteStepAsync → 改内存确定性状态 → AppendOutbox(step产物)     // 内存追加，O(1)，不落盘
       └─ 循环立即进入下一步（provide_prompt 立刻开始）★根治卡点
后台 writer（独立 worker，可取消异步）
  └─ DrainOutbox → 逐条经 WriteCoordinator 落 ur_nodes + KeyMapping + LiteDB 图写
       └─ 单条有界超时 + 可取消；失败入重试/死信队列，绝不阻塞主循环
```

**关键决策**：
- **逻辑结果即时、存储最终一致**：执行只保证"内存确定性根 + Outbox 追加"的即时性；跨介质落库是最终一致（报告已声明 graph 为可重建派生数据，`DerivedDataAutoMaintenance` 语义天然支持）。
- **Outbox 载体**：默认内存队列（进程内单 writer）；如需跨崩溃，落单条 SQLite 事务（仍远快于原多介质同步写）。
- **读取侧**：`ResolveInputsAsync` 已"节点输出优先"，Outbox 消费前从内存根读；已消费后从存储读，两侧一致（对齐既有"节点即存储单元"）。
- **可回退**：`IWriteCoordinator` 未装配 / Outbox 关闭时，退回原同步持久化（保回归）。

**消除**：D2（持久化不再内联热路径）。这是**本次卡死的最直接根治**：`guard_input` 完成后 `provide_prompt` 立即开始，与存储健康无关。

**验收**：人为制造存储写阻塞（如挂起 LiteDB）时，`guard_input → provide_prompt` 仍瞬时推进，任务不卡 `Running`。

---

### R3 线程隔离 + UI 封送隔离（消除 D3）

**现状**：Agent 后台循环（task pool 线程）与 UI 线程/WebView2/ECS 交织；`RunContext.Enter` 用 AsyncLocal 跨线程透传；持久化/渲染路径可隐式封送 UI 线程 → sync-over-async 死锁（`CoreWebView2 … UI thread`、ECS 5s 超时）。

**设计**：
```text
Agent 运行时：纯后台 worker，无 SynchronizationContext 依赖（ConfigureAwait(false) 全覆盖）
UI 交互：唯一经 IUiDispatcher（显式、超时、失败隔离）
  ├─ InvokeAsync(uiAction) with timeout   // 后台→UI，超时抛异常、失败隔离，不阻塞运行时
  └─ 禁止从运行时 .GetAwaiter().GetResult() 等待 UI 任务（SyncAsyncBridge 统一收敛）
跨线程上下文：RunContext 仅在同一异步流内透传（AsyncLocal 不跨后台/UI 边界）
```

**落地**：
1. 审计全部 `CoreWebView2`/WinForms/ECS 访问点，抽出 `IUiDispatcher`；
2. 运行时所有 UI 交互改经 `IUiDispatcher.InvokeAsync`（带超时 + 失败归类为 `UI_MARSHAL`，非致命）；
3. `SyncAsyncBridge` 统一收敛所有 sync-over-async 等待点（现状有 `GetAwaiter().GetResult()` 风险）；
4. 移除跨线程 AsyncLocal 依赖：`RunContext.Current` 仅在同一 async flow 有效，跨后台/UI 边界显式传参。

**消除**：D3。运行时与 UI 彻底解耦，`CoreWebView2 … UI thread` / ECS 封送超时从根上消失；后台卡死不再波及 UI，UI 卡死也不冻结后台循环。

**验收**：运行时在后台线程全程无 UI 封送日志；注入一个挂起的 `IUiDispatcher` 时，运行时超时抛 `UI_MARSHAL` 归类并继续，不死锁。

---

### R4 可取消异步存储 I/O + 单步可取消超时（消除 D4）

**现状**：存储层为同步阻塞（SQLite/LiteDB 同步 API + `.Wait()/.Result()`）；取消令牌只在循环头判定；P1 用 `Task.WhenAny` 放弃而非取消 → 后台线程/锁泄漏。

**设计**：
```text
存储层全面异步化：所有 I/O 方法改 async，CancellationToken 贯穿
  ├─ SQLite：SQLiteConnection 异步 API / WriteCoordinator.WaitAsync(ct)（可取消，非 Wait(timeout)）
  └─ LiteDB：写经 WriteCoordinator.RunGraph + 可取消等待门
单步/单轮可取消超时（替代 P1 的"放弃"）：
  └─ linked CTS：StepTimeout 触发 → CancelAfter → 阻塞 I/O 收到取消 → 抛 OperationCanceledException → 归类 TO
       └─ 真正中断在途 I/O，而非 WhenAny 后遗弃
```

**落地**：
1. 存储接口 `IPersistenceStore`/`INodeOutputStore`/`IKeyMappingStore` 全面 async + `CancellationToken` 参数（补缺省 `= default` 保兼容）；
2. `SemaphoreSlim.Wait(timeout)` 全部改 `WaitAsync(timeout, ct)`（可取消等待）；
3. `RoundLoopDriver` 的 `WhenAny` 兜底升级为 **linked-CTS 取消**：超时即取消在途执行体，确保线程/锁回收；
4. 新增**单步超时**（非仅单轮）：每步 `ExecuteStepAsync`/`OnAfterStepAsync` 都带取消窗口，卡超即终止本轮记 `TO`。

**消除**：D4。"预算终止引擎内可判定/阈值到达即触发"由宣言变为可执行——取消令牌真正到达阻塞点。

**验收**：挂起某个存储写 → 单步超时取消在途 I/O，线程数不增长（无泄漏）；任务确定性转 `TO`，不永久 `Running`。

---

### R5 机制强制治理：写路径审计守卫（消除 D5/D6）

**现状**：`SingleImplementation`/`RedundancyElimination` 降级为 `[Strategy]`（元级指导，不强制）→ 平行写实现（三套门）必然再出现；`.clinerules` 校验器只查符号引用，不查运行时写入口唯一性。

**设计**：
```text
机制强制（把不变式从「文档/约定」提升为「编译期/运行期检查」）：
  1. 写路径单一注册：所有 store 实例仅经 WriteCoordinator 构造，构造即登记；
  2. 运行期写路径审计守卫（对齐既有 WriteGuardPort 思路，升格到整库）：
       任意 store 写若绕过 WriteCoordinator → fail-fast（抛/断言），平行实现无立足点；
  3. .clinerules 校验器扩展「写入口检查」：扫描写调用，断言仅命中 WriteCoordinator.Run*（对齐
     检查 1 悬空引用的机制，把「单一写入口」做成可机器校验的不变式）；
  4. 把 `[Strategy] SingleImplementation` 的相关关键实例（写入口/存储构造）从元级指导提升为强制。
```

**落地**：
1. `WriteCoordinator` 提供 `AssertSingleWriter(store)`：注册表 + 双重构造（绕过即抛）；
2. 新增脚本/静态分析 `Check-SingleWriter.ps1`（对齐 `Check-ClinerulesReferences.ps1`）扫描写入口，纳入 CI；
3. 文档治理同步：`axioms.md` 把「确定性核心单一写入入口」从描述性宣言补强为「运行时强制」标注（`enf:"codegen_constraint: 写路径仅经 WriteCoordinator"`）。

**消除**：D5（平行实现被 fail-fast 扼杀）、D6（并发正确性由单写机制保证，不再押在 MaxConcurrency=1）。

**验收**：新增一个绕过 WriteCoordinator 直写存储的 store → 运行期/编译期被审计守卫拦截；`Check-SingleWriter.ps1` 全库扫描 0 绕过。

---

### R6 内建观测与取证（卡死一次定位，消除"运行时取证"盲区）

**现状**：本次排查最大瓶颈是无法抓卡死线程栈，只能靠推断 + 多次重跑；文档 C3/C4 标注"待运行时取证"始终未闭环。

**设计**：
```text
内建步骤遥测（对齐既有 AgentLog 埋点，结构化）：
  ├─ 每步/每轮计时：start/end/elapsed，落 ur_events（可重建单轮完整序列）
卡死自取证（stall detector）：
  └─ 单步超过阈值 → 自动 dump 当前线程栈 + 锁持有者 + Outbox 队列深度
       → 写 ur_events + 日志（一次卡死即可定位，无需手动抓栈）
```

**落地**：
1. 复用 `IObservabilitySink` + `EventSink`（已有 P2 事件化观测基建），补步骤级事件；
2. 新增 `StallDetector`：步骤级 watchdog，超阈自动取栈快照（对齐 C2 的"持久化失败不阻塞主循环"——取证也不阻塞主循环）；
3. 把 `agent_log_messages.json` 步骤日志升格为结构化事件，纳入 ur_events 重建链。

**消除**：排查盲区——以后任何卡死（存储写/UI 封送/锁等待）在**同一次运行**即自证定位，不再需要多次重跑或手动 dump。

**验收**：注入挂起点 → 单步超阈 → 自动产出含线程栈/锁持有者的诊断事件；任务照常转 `TO`，不额外卡死。

---

## 三、实施阶段（P0–P5，增量、可回退）

| 阶段 | 对应 | 内容 | 交付物 | 回归/回退 |
|------|------|------|--------|-----------|
| **P0** | R1 | 收敛为 `WriteCoordinator` 单一写门（SQLite 事务/非事务 + LiteDB 同门、固定写序） | `WriteCoordinator`；删除三套旧门 | `DataStoreDriven`+`Agent.sln` 0 警告 0 错误；旧行为等价 |
| **P1** | R2 | 持久化 Outbox 解耦：执行热路径只 append，后台 writer 落库 | `IOutbox` + `OutboxWriter`；`OnAfterStep` 改 append | 卡死直接消失；`round_timeout_seconds` 从兜底降级为可关；可回退同步持久化 |
| **P2** | R3 | 线程隔离：`IUiDispatcher` 收敛 UI 封送，移除运行时 sync-over-async / 跨线程 AsyncLocal | `IUiDispatcher` + `SyncAsyncBridge` 收敛 | 无 `CoreWebView2 … UI thread`；注入挂起 dispatcher 不死锁 |
| **P3** | R4 | 存储层可取消异步化 + 单步/单轮 linked-CTS 取消超时 | 存储接口 async + ct；`RoundLoopDriver` 升级取消 | 卡超真正中断 I/O；线程不泄漏；任务确定性 `TO` |
| **P4** | R5 | 机制强制：写路径审计守卫 + `Check-SingleWriter.ps1` + 治理文档补 `enf` 标注 | `AssertSingleWriter` + CI 脚本 | 绕过写被拦截；全库 0 绕过 |
| **P5** | R6 | 内建观测：步骤级事件 + `StallDetector` 卡死自取证 | 结构化步骤事件 + 自动线程栈 dump | 一次运行即定位卡死点 |

> **依赖**：P0 先行（写门归一是一切写改造的地基）；P1 依赖 P0；P2/P3 可与 P1 并行（线程与 I/O 异步化相互独立）；P4 依赖 P0；P5 全程可并行。

---

## 四、与既有治理（axioms/formula）对齐

| 既有规则 | 本方案落地 | 对齐方式 |
|----------|-----------|---------|
| `[CR: axioms:SEP.DeterministicCoreDisjoint@align]` | R1 单一写入口 + R5 强制守卫 | 从"确定性核心单一写入"宣言升级为**运行时/编译期强制** |
| `[CR: axioms:AX.StateRootSingleSource@consume]`（StateSnapshot 单一根） | R2 Outbox 后单一内存根为即时权威，存储最终一致 | 增强"单根"即时一致性，降低存储耦合 |
| `[CR: axioms:AX-CORE.EssentiallyUndecidable@consume]`（探索终止外置） | R4 预算终止引擎内可取消 | 预算终止"可判定/可强制"真正落地（不再押在放弃线程） |
| `[CR: axioms:Strategy.SingleImplementation@consume]` | R5 写路径单一注册 + fail-fast | 关键实例从 `[Strategy]` 元级指导提升为机制强制 |
| `[CR: formula:SM.SyncGuard@consume]` / `StateRecovery` | R2/R4 | 存储恢复与主循环解耦后，对账/恢复不阻塞执行 |
| `[CR: formula:Pattern.TaskPoolAgent@consume]`（MaxConcurrency=1） | R1 单写机制 | 并发正确性由单写保证，MaxConcurrency 可安全提升 |

---

## 五、验收与回归基准

重跑 `agent_mcpinvoke_create_task_test.json`（及 `agent_taskpool_test.json`）应观测：
1. `guard_input` 完成后 `provide_prompt` **立即开始**（与存储健康无关）；✅ P1 直接覆盖
2. 人为挂起存储写时，任务不卡 `Running`，单步超时确定性转 `TO`；✅ P1/P3
3. 全程无 `CoreWebView2 … UI thread` / ECS 封送超时（来自运行时）；✅ P2
4. 若仍卡死，单步超阈自动 dump 线程栈，**一次运行即定位**；✅ P5
5. `Check-SingleWriter.ps1` 全库扫描 0 绕过；✅ P4
6. `FileDriven.sln` / `Agent.sln` 0 警告 0 错误。✅ 全阶段

---

## 六、风险与回退

| 风险 | 缓解 |
|------|------|
| Outbox 改变落库时序（逻辑即时、存储最终一致） | 保留单条 SQLite 事务 Outbox 选项保跨崩溃；读取侧已"节点输出优先"；可回退同步持久化 |
| R1 全局单写门降低写并行度 | 当前 `MaxConcurrency=1`，无感知；并发>1 时写串行正确（只是低并行），可后续分库优化 |
| R2/R3 改动面大 | 各阶段独立、可回退；`IWriteCoordinator`/`IUiDispatcher` 未装配时走原路径（零回归） |
| 存储异步化引入 API 变更 | 接口补 `CancellationToken = default` 缺省参数保兼容；逐接口迁移 |

---

## 七、里程碑与后续

- **M1（P0）**：`WriteCoordinator` 收敛三套门 → 先根治"多写门"这一死锁/悬挂第一性来源。
- **M2（P1）**：Outbox 解耦 → **本次卡死直接消失**（提供_prompt 不再依赖存储健康）。
- **M3（P2+P3）**：线程隔离 + 可取消异步 I/O → 消除 UI 死锁与线程泄漏，终止语义真正可执行。
- **M4（P4+P5）**：机制强制 + 内建观测 → 平行实现不再复现、卡死一次自证定位。

> **优先级建议**：先做 **P1（Outbox 解耦）**——它是本次卡死的最直接、最独立的根治，改动集中在 `NodeGraphOrchestrator`/`RoundStepInvoker`/`NodeExecutionService` 一带，风险可控、验收清晰（挂起存储写仍瞬时推进）。P0（单一写门）是后续可取消异步化的地基，紧随其后。

---

*本方案基于 2026-09-01 会话对架构缺陷（D1–D6）的分析与两次运行日志（22:08 修复前 / 22:25 修复后仍卡）整理。*


