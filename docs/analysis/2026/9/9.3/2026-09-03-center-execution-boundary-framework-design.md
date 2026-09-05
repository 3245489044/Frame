# 中心化管理执行框架 + 贯穿式可取消执行边界 — 设计文档

> **日期**：2026-09-03
> **目标落点**：`CsharpPrj/Operation/HierarchyMethod`、`CsharpPrj/Operation/LoopFoldMethod`（通用框架构建地），再向消费方（`Agent.Core` / `Produce.Core` / `WorkFlowMethod`）应用。
> **范围**：① 收敛「多套执行/编排/存储/写门」为单一中心化管理框架，消除冗余与多套不协调；② 构建一条贯穿「引擎 → 编排 → 步骤 → 存储」的**可取消执行边界**；③ 治理强制化（把声明为不可强制的 `[Strategy]` 提回运行时断言）。
> **定位**：本文为**架构与设计文档**（阶段、模块、接口契约），不含逐行实现。落地按 §7 分阶段推进并各自验证。
> **依据**：源码核实（`NodeGraphOrchestrator` / `NodeRoundContext` / `INodeExecutor` / `INodeExecutorHooks` / `FoldEngine` / `ICutoffPolicy` / `NodeOutputStore` / 目录结构）+ 历史诊断（9.1/9.3 卡死分析、`Agent.Core` 读源/持久化复发）+ `.clinerules` 治理公理。

---

## 一、背景：为什么需要中心化框架

### 1.1 现象回顾（本次卡点）

同一测试（`tp_mcpinvoke_ctrl`、`agent_mcpinvoke_create_task_test.json`、GUI 模式）下，Agent 骨架五步在 `cline_execute` 步骤**开始后即静默停滞**：`步骤开始`（`RoundStepInvoker.cs:97`）已打，而 `入参解析完成`（cs:113）、`函数调用开始`（invokeStart）均未出现——**函数 handler 从未被调用**。进程随后被 `运行测试GUI模式.ps1` 的"连续 N 秒无输出"看门狗强杀（`$process.Kill()`）。即：**读源/取数路径静默阻塞 → 引擎无法在进程内收敛为 `TO` → 外部看门狗 Kill**。

这不是孤例。同一"骨架链的读/写/执行段"已在不同点反复复发：

| 日期 | 卡点 | 位置 |
|------|------|------|
| 09-01 | `guard_input` 的 `OnAfterStep` 同步持久化 | HierarchyMethod 编排热路径 |
| 09-02 | `rule_match` 的 `handler`（`InvokeAsync` 未返回） | Agent.Core 执行走廊 |
| 09-03 | `cline_execute` 的 `ResolveInputs` 读源 | Agent.Core 步骤调用器 |

**结构性判断**：修一个点、下一个点复发 → 根因不在个别行，而在**没有一条在框架层强制存在的可取消执行边界**，以及**多套平行实现互相不协调**。

### 1.2 直接根因（本卡点）

代码可证，`NodeGraphOrchestrator.RunRoundAsync`（`HierarchyMethod/Nodes/Execution/NodeGraphOrchestrator.cs`）：

```csharp
foreach (var step in spec.Steps)
{
    ct.ThrowIfCancellationRequested();                    // cs:66 —— 取消只在「每步之间」判定
    var record = await _executor.ExecuteStepAsync(...);   // cs:68 —— 步骤内部阻塞救不了
    if (_hooks != null)
        await _hooks.OnAfterStepAsync(...);               // cs:73-74 —— 持久化在编排热路径同步 await
}
```

- 取消令牌只在**循环步头**被检查；步骤内、读源、存储任意一处同步/异步卡死，引擎无法打断。
- 框架层 `INodeExecutor` / `INodeExecutorHooks` **不带任何有界/超时契约**；单步超时开关（Agent 侧 `RoundStepInvoker._stepTimeout`）**默认 `Zero`、未从轮超时派生接线**。
- 存储读（`ur_nodes` / LiteDB 引用图 / 共享 `DataSpace.Snapshot`）可能同步阻塞且**不响应 `CancellationToken`**。

### 1.3 架构层根因（前一轮分析归纳）

治理/契约、执行/终止、状态/存储、线程/进程、装配/端口、编排、可观测七类缺陷中，**与本次中心化框架直接相关的三条不变式从未在运行时被强制**：

1. **单一确定性写入口**（`SEP.DeterministicCoreDisjoint`）——多写门并存，靠编译期脚本 `Check-SingleWriter.ps1` 静态扫描，运行时无 `AssertSingleWriter`。
2. **状态单真源（SSOT）**（`StateRootSingleSource`）——`ur_nodes` / `ur_tasks` / LiteDB 引用图 / 共享 `DataSpace` 多介质，每步跨库拼读，无原子性。
3. **预算终止引擎内强制**（`AX.TerminationPriority`）——阻塞点不可取消、步超时未接线，终止语义退化到"进程存活"由外部脚本承载。

---

## 二、现状盘点：多套不协调与冗余清单

> 锚点基于 `CsharpPrj/Operation` 下方法项目群 + `Projects/{Agent,Produce}` 消费方。下表的每一项都是"覆盖其功能再应用"的收敛对象。

### 2.1 三套平行"循环/执行/编排"引擎并存（最大冗余源）

| 引擎 | 承载层 | 角色 | 锚点 |
|------|--------|------|------|
| `WorkFlowMethod` | Operation/WorkFlowMethod | 老工作流引擎：`WorkflowEngine` / `ExecutionManager` / `FunctionExecutionFramework` / `ExecutionPipelineRunner` / `CentralPipeline` / `GraphWorkflowConnector` | 目录 | 
| `NodeGraphOrchestrator.RunRoundAsync` | Operation/HierarchyMethod/Nodes/Execution | 节点图顺序编排（seed→步骤 execute+onAfterStep→harvest→终答），框架层零 LLM | `NodeGraphOrchestrator.cs` |
| `FoldEngine` + `SignalDrivenFoldEngine` + `DecisionFoldEngine` | Operation/LoopFoldMethod | 折叠（fold/unfold）驱动 + 信号驱动 + 决策引擎；底层复用 `Share.TaskStatusPool` 的 `SnapshotDrivenLoopEngine`（TH-12 唯一 while） | `Fold/FoldEngine.cs` |

同一"驱动一次多步循环 + 编排 + 计时 + 截断"的职责，WorkFlowMethod、HierarchyMethod、LoopFoldMethod 各自实现一套 → 取消/终止/错误语义三处不同。

### 2.2 执行走廊（函数调用入口）平行重复

- `WorkFlowMethodFunctionInvoker`：`Projects/Agent/src/Agent.Core/Nodes/WorkFlowMethodFunctionInvoker.cs` **与** `Projects/Produce/src/Produce.Core/WorkflowMethodFunctionInvoker.cs` **各一份** → 违反 `SingleImplementation`（TH-12：单一 canonical 消除平行分叉）。
- `INodeFunctionInvoker` 由 Agent 用上述平行 invoker 兜底装配。

### 2.3 存储/写路径分散、写门多套、跨介质无原子

| 写入对象 | 归属 | 写门/协调 | 介质 |
|----------|------|-----------|------|
| 业务节点 + 节点输出 `ur_nodes` | HierarchyMethod/Nodes (`NodeOutputStore.cs`) | `WriteGuardPort`（Operation/DataStoreDriven/Core）镜像 | SQLite |
| 节点元数据 KeyMapping 分类写 | Operation/DataStoreDriven | SQLite 事务（9.1：曾绕过进程级串行门） | SQLite |
| 引用图边（权威边/task 归属边） | Operation/LoopFoldMethod/Reference/Gateways（`NodeRelationGateway`/`HistoryRelationGateway`） | LiteDB `_gate` 单写门（9.1：存在泄漏/异常释放风险） | LiteDB |
| 历史容器/任务链 | HierarchyMethod/History + LoopFoldMethod/FoldHead/Reference 投影 | — | — |

`NodeOutputStore`（ur_nodes）已是**唯一一份**（收敛已起步），但写侧仍与 KeyMapping 分类写、LiteDB 图写各自为政。

### 2.4 取消/终止能力点状、层级间不接线

| 层 | 现状 | 缺口 |
|----|------|------|
| 引擎（LoopFoldMethod） | `ICutoffPolicy`（预算/收敛/取消→FoldCut）+ `CancellationCutoffPolicy`；`FoldEngine` 经 `SnapshotDrivenLoopEngine.budgetTerminate` | 有较完整**收敛**语义，但 `op.Expand` 内部 await 若卡死，引擎仍救不了（预算只在循环边界判） |
| 编排（HierarchyMethod） | `NodeGraphOrchestrator` foreach 步头 `ThrowIfCancellationRequested` | 步骤内阻塞不可打断；`OnAfterStepAsync` 同步持久化在热路径 |
| 步骤（Agent 实现） | Agent 侧 `RoundStepInvoker._stepTimeout`（默认 Zero） | 框架层 `INodeExecutor` 无有界契约；默认关 |
| 存储 | SQLite `Default Timeout`；LiteDB 无有界 | 同步阻塞不响应 `ct` |

### 2.5 装配"可缺席"导致同链静默分叉

`NodeGraphOrchestrator` 构造 `hooks/decisions/finalAnswer/statePort` 全部可空（no-op 兜底）；`NodeExecutionService` `_store/_grounding/_keyMappings` 可空一路 `??`/`if null`。→ **同一调用链因组合根装配差异走不同分支**（本次 guard_input 带 store 读源、provide/cline 可能无 store 取数），行为不可复现、无法归因。端口契约完整性未在装配期校验。

### 2.6 共享数据空间/线程无隔离

`NodeRoundContext.Vars`（`IDataSpace`，来源为 StateCenter.Transient 层或内存 DataSpace）多线程共享、`RunContext` 用 AsyncLocal 跨线程透传、与 GUI/WebView2/ECS 同进程交织 → 读源可能在 `Vars.Snapshot()` 这类**无日志点**被并发写持锁（9.1 D3）。

---

## 三、中心化管理执行框架：目标架构

### 3.1 设计原则

1. **不新造第三套引擎**。中心化=把既有收敛点为"主干"，覆盖其余；不是再建一个 `Operation.Center` 平行工程。用户指定的两处工程即归属：
   - `Operation/LoopFoldMethod` = **引擎与终止内核**（复用 `SnapshotDrivenLoopEngine` + `FoldEngine` + `ICutoffPolicy` 族，只补"每展开有界"）。
   - `Operation/HierarchyMethod` = **编排 + 节点存储中心**（`NodeGraphOrchestrator` + 唯一 `NodeOutputStore`），在此**新增 `Center/` 横切包**承载：可取消执行边界、单一写门、读源视图、唯一执行走廊、装配契约校验。
2. **WorkFlowMethod 降级为"被覆盖方"**：其"多步循环/编排/执行管道"不再作为并行引擎；`FunctionExecutionFramework`/`CentralPipeline` 只保留函数注册/仓储与对外消费面，循环收敛到 LoopFoldMethod+HierarchyMethod。
3. **单向依赖、消除环**（对齐 axioms `[CR: logic:P1]`）：`Domain → HierarchyMethod → LoopFoldMethod → Share.TaskStatusPool → 存储介质`；禁止反向引用。

### 3.2 依赖分层与模块职责

```
消费方（领域层）: Agent.Core / Produce.Core / WorkFlowMethod 消费面
   └─ 只保留「领域策略」：决策词判定、终答归集、guard_*、具体执行器（Cline/Answer/规则）
        ▼
HierarchyMethod（编排 + 节点存储中心）
   ├─ 既有：NodeGraphOrchestrator / NodeRoundContext / INodeExecutor(Hooks) / NodeOutputStore(ur_nodes 唯一)
   ├─ 新增 Center/
   │    ├─ ExecutionScope         可取消执行边界载体（CTS 根 + 层级派生 + 预算 + 强类型超时 + 栈转储）
   │    ├─ NodeWriteCoordinator   单一写门（ur_nodes + KeyMapping + LiteDB 图边，统一写序 + 有界）
   │    ├─ NodeReadView           读源单一视图（ur_nodes task 过滤 + 引用图 task 键，取代每步跨库拼读）
   │    ├─ INodeFunctionInvoker   唯一实现（收敛 Agent.Core 与 Produce.Core 平行 invoker 上移）
   │    └─ PortContractGuard      装配期端口契约校验（缺端口=启动失败，不再静默分叉）
   └─ 错误归一：ExecutionBoundaryException 族（Step/Round/StoreTimeout，自证 stage+step）
        ▼
LoopFoldMethod（引擎 + 终止内核）
   ├─ 既有：FoldEngine / ICutoffPolicy(预算/收敛/取消→FoldCut) / CancellationCutoffPolicy
   ├─ 既有：SignalDrivenFoldEngine / DecisionFoldEngine（作为折叠引擎的驱动策略，不是平行 while）
   └─ 补齐：FoldEngine 支持 per-step budget（op.Expand 纳入有界，同层级取消贯穿）
        ▼
Share.TaskStatusPool.SnapshotDrivenLoopEngine   （TH-12 唯一 while；execute/next/budgetTerminate 三端口）
        ▼
存储介质：SQLite ur_nodes / ur_tasks、LiteDB 引用图（读写全部经 NodeWriteCoordinator + 带 ct 的 async 接口）
```

### 3.3 模块关键契约（新增/增强接口，供 Phase 落地）

| 能力 | 接口 / 类型（新增设计） | 责任 |
|------|--------------------------|------|
| 可取消执行边界载体 | `ExecutionScope`（`CreateScope` / `Derive(stage)` / `Bound(budget)` / `TryRun`） | 单一 CTS 根；每 stage 派生子 CTS + 预算；任一 await 纳入边界 |
| 强类型终止/超时 | `ExecutionBoundaryException` 族：`RoundTimeout`/`StepTimeout`/`StoreTimeout` | 自证 `Stage`+`Step`+`Timeout`；区分「取消」与「超时」，编排不再误判为整轮取消 |
| 单步/单轮有界 | 增强 `INodeExecutor.ExecuteStepAsync` + `NodeGraphOrchestrator.RunRoundAsync` 签名带 `ExecutionScope` | 框架层强制有界，不再依赖 Agent 侧默认 Zero 的 `_stepTimeout` |
| 单一写门 | `NodeWriteCoordinator.WriteAsync(WriteWork)` | 所有 ur_nodes/KeyMapping/LiteDB 边写统一串行 + 有界超时 + 失败归类 |
| 读源收敛 | `NodeReadView.ResolveAsync(taskKey, ParameterKeys)` | 替代 `NodeExecutionService` 每步"跨库枚举+图解析+DataSpace 快照"的拼读 |
| 唯一执行走廊 | `INodeFunctionInvoker.InvokeAsync(fn, params, scope)` | 收 `WorkFlowMethodFunctionInvoker` 平行实现上移；单一 canonical（TH-12） |
| 装配契约 | `PortContractGuard.Validate(root)` | 启动即校验端口齐全，缺则 fail-fast，消灭 null 静默分叉 |
| 取消贯穿回调 | `ICutoffPolicy` 兼容层 + `ExecutionScope.ExternalToken` | 把存储/步骤阻塞的观察统一到取消/超时两条收敛路由 |

---



## 四、贯穿式可取消执行边界设计（核心）

### 4.1 单一载体：`ExecutionScope`（CTS 根 + 层级派生 + 预算）

不再让每层各自 new 一个 `CancellationToken`/计时。一条执行（一次任务/会话运行）只建一个根 `ExecutionScope`，向下按层级**派生**：

```
ExecutionScope.Run(root, budget: B)                       // 引擎层：任务/主循环一次运行
  ├─ scope.Derive("round", budget: BR)                    // 编排层：一轮（一次 RunRoundAsync）
  │    ├─ scope.Derive("step.guard_input",  budget: BS)   // 步骤层：一步（含读源+handler+return_spec）
  │    ├─ scope.Derive("step.provide_prompt", budget: BS)
  │    └─ scope.Derive("step.cline_execute", budget: BL)  // LLM/暂停门步骤：预算可放大，但必须走统一 Gate 通道（可观测、可取消）
  └─ scope.Derive("harvest", budget: BS)                  // 收割/终态信封
        └─ scope.Derive("store.write", budget: 5s)        // 存储写：固定有界（单一写门）
        └─ scope.Derive("store.read",  budget: 5s)        // 存储读：固定有界（读源视图）
```

属性：`Parent`（可上抛）、`Stage`/`Step`（自证）、`LinkedCts`、`Budget`（默认从上一层剩余预算派生）、`ExternalToken`（任务池暂停/用户取消在此注入，作为根外部取消）。取消/超时语义在此**唯一归一**。

### 4.2 贯穿链：引擎 → 编排 → 步骤 → 存储（逐层接入点）

| 层 | 现接入点 | 改造为（经 `ExecutionScope`） | 消除 |
|----|----------|------------------------------|------|
| 引擎 | `LoopFoldMethod/FoldEngine`（复用 `SnapshotDrivenLoopEngine`；`op.Expand` 目前只受循环边界预算） | `RunAsync` 收根 scope；**每次 `op.Expand` 用 `scope.Derive("step", budget)` 竞速包裹**（含空值守卫→熔断）；`budgetTerminate` 合并「预算+收敛+取消」 | `op.Expand` 内部卡死救不了；`SignalDrivenFoldEngine`/`DecisionFoldEngine` 平行 while 复用同一引擎不再各写循环 |
| 编排 | `NodeGraphOrchestrator.RunRoundAsync`（foreach 步头 `ct.ThrowIfCancellationRequested`） | 每步 `scope.Derive("step."+fn)` → 竞速包 `ExecuteStepAsync` **与** `OnAfterStepAsync`；超时转 `RoundTimeout/StepTimeout` 强类型上抛 | 步骤内阻塞不可打断；`OnAfterStep` 同步持久化冻结整轮（配合默认开启的 Outbox） |
| 步骤 | Agent 侧 `RoundStepInvoker.ExecuteStepAsync`（`_stepTimeout` 默认 Zero） | 框架 `INodeExecutor.ExecuteStepAsync` 签名带 `scope`；读源（`ResolveInputs`）**强制**在步内子边界；handler 的 LLM/暂停门**豁免步预算**但必须经 `CheckLlmGateAsync` 统一 Gate（可取消、可观测），不再裸 `await` | `_stepTimeout` 默认关的装配依赖；读源/取数无日志卡点 |
| 存储 | `NodeOutputStore`(ur_nodes) / 引用图 / KeyMapping 各自读写 | 全部经 `NodeWriteCoordinator` 写 + `NodeReadView` 读；接口带 `ct`，同步阻塞路径也置于子边界竞速 | 存储同步阻塞不响应取消；读源跨库拼读 |

**原则**：任何一步的"完成 = 返回结果"；任何一步的"不完成"要么被 `ct` 打断（取消），要么被子边界竞速在 `Budget` 后打断（超时 → 强类型）。杜绝"永不返回 + 不产日志"的裸 await。

### 4.3 取消 / 超时 / 终止的收敛路由

把历史散落的 CF/CF→TO、RoundTimeout、StepTimeout、操作 AEF、外部看门狗统一到两条收敛路由（引擎内）：

| 触发 | 语义 | 映射 | 引擎内产物 |
|------|------|------|-----------|
| 外部取消（用户/任务池 Pause-Cancel） | `Cancelled` | `ct` 传导 | `OperationCanceledException` → 编排 `Cancelled` |
| 存储写/读卡超（子边界） | `StoreTimeout` | 强类型 | `NodeWriteCoordinator` 归类 → 失败重试/降级，不冻结整轮 |
| 步骤读源/函数卡超（轮预算） | `StepTimeout`/`RoundTimeout` | 强类型 | 记 `RoundLoopDriver_round_timeout`，引擎进入 `next` 裁决 |
| 预算耗尽（引擎层） | 预算终止 | `budgetTerminate` | 依 `AX.TerminationPriority` → `TO`（引擎内强制，不可被 LLM 提议覆盖） |
| 折叠/循环语义（LoopFoldMethod） | `FoldCut` | 复用 | `FoldCutMapping` ↔ `LoopDecision`（引擎内核保持单一 while + 截断执法） |

> 对齐 axioms：`AX.TerminationBipartition`——探索终止（不可判定）外置，**预算终止（可判定）引擎内强制**。本设计的根 scope + 层级子边界即"引擎内可判定裁决"的运行时载体；外部看门狗（进程 Kill）降级为**最后防线**而非默认终止手段。

---


### 4.4 阻塞自证：每 await 竞速 + 到期线程栈事件

边界不只是"超时抛异常"，还要让卡死**一次运行即自证定位**（替代每次"日志缺失猜锁"）：

```csharp
// ExecutionScope.Bound —— 任一步 await 的确定性包裹（伪代码）
async Task<T> Bound<T>(string step, TimeSpan budget, Func<CancellationToken, Task<T>> body)
{
    using var linked = CreateLinked(ExternalToken);     // 外部取消优先
    linked.CancelAfter(budget);
    var work = body(linked.Token);
    var winner = await Task.WhenAny(work, Task.Delay(Infinite, linked.Token));
    if (ReferenceEquals(winner, work)) return await work;
    if (ExternalToken.IsCancellationRequested) ExternalToken.ThrowIfCancellationRequested();
    // 卡超：产出线程栈诊断事件（阻塞归属一次定），再抛强类型
    DumpStallEvent(stage, step, budget);                 // ← 栈顶 = SQLite/LiteDB/DataSpace → 定 Stage2/阻塞点
    throw new StepTimeoutException(step, budget);
}
```

配套：`NodeWriteCoordinator` 写门用 `SemaphoreSlim(1,1)` + 有界等待，超时抛 `StoreTimeoutException`（自证写对象），读源 `NodeReadView` 同理；取消令牌在存储 async 接口全程传导。

### 4.5 存储可响应：单一写门 + 带 ct 读

- **写**：`NodeWriteCoordinator.WriteAsync(work)` 作为 ur_nodes / KeyMapping 分类写 / LiteDB 引用图边的**唯一入口**；统一写序 + 有界超时 + 失败归类；`Check-SingleWriter.ps1` 静态清单之外，运行时 `AssertSingleWriter`（越权写 fail-fast）。
- **读**：`NodeReadView.ResolveAsync(taskKey, keys)` 取代 `NodeExecutionService` 每步"枚举 ur_nodes 全量 + 引用图解析 + DataSpace 快照"的**跨库拼读**；只读本 task 归属（task 过滤），消除跨 task 污染与无日志取数卡点。
- **持久化解耦**：`OnAfterStepAsync` 持久化默认走 Outbox（热路径只 append），落库由后台 writer 单条有界——`guard/provide/cline` 步骤推进与存储健康解耦（9.1 P1 结论固化，不再默认关）。

### 4.6 线程/UI 隔离（贯穿链的运行时纪律）

执行链全程 `.ConfigureAwait(false)`、纯后台 executor；UI/WebView2/ECS 的调用**只经显式有界 `IUiDispatcher`**，Agent 运行时不再隐式封送 UI 线程，移除 `RunContext.Enter` 的 AsyncLocal 跨后台/UI 透传。`Vars.Snapshot()` 只在"编排线程 + 无并写"的纪律下可读；跨线程写数据空间经 scope 内单一写者。→ 消除 `CoreWebView2 … UI thread` / sync-over-async 死锁（9.1 D3），根除"无日志点被并发写持锁"。

---


## 五、冗余消除 / 收敛映射（覆盖其功能再应用）

> 原则（对齐 `SingleImplementation`/`RedundancyElimination` 修复动作）：locate_canonical → verify_alignment → replace_with_canonical；不可判定项由中心化框架 + 运行时断言承担，不再只靠 `[Strategy]` 元级指导。

| # | 冗余 / 多套 | 收敛目标（canonical） | 落点 | 覆盖对象 |
|---|------------|----------------------|------|----------|
| R1 | 三套执行/编排引擎（WorkFlowMethod / NodeGraphOrchestrator / FoldEngine+Signal/Decision） | 执行面 = **HierarchyMethod 编排**；终止/循环内核 = **LoopFoldMethod + SnapshotDrivenLoopEngine** | 两主干 | WorkFlowMethod 的 `ExecutionPipeline`/`CentralPipeline` 平行循环降级为被覆盖（仅留函数注册/仓储） |
| R2 | `WorkFlowMethodFunctionInvoker` 双份（Agent.Core / Produce.Core） | 单一 `INodeFunctionInvoker`（Center 唯一实现） | HierarchyMethod/Center | 两处平行 invoker 引用同一 canonical |
| R3 | 多写门（DataStoreDriven / ur_nodes WriteGuardPort / LiteDB `_gate` / KeyMapping 事务） | `NodeWriteCoordinator` 单一写门 + `AssertSingleWriter` | HierarchyMethod/Center | 各介质写统一入口 |
| R4 | 每步跨库拼读（ur_nodes 全量枚举 + 引用图解析 + DataSpace 快照） | `NodeReadView`（task 作用域单一读源） | HierarchyMethod/Center | `NodeExecutionService.ResolveInputsAsync` 拼读 |
| R5 | 历史/引用投影分散（HierarchyMethod/History 与 LoopFoldMethod/FoldHead+Reference/Projection） | 单一关系/历史门面（`NodeRelationGateway`+`HistoryRelationGateway` 收敛） | LoopFoldMethod/Reference + HierarchyMethod/History | 重复投影实现 |
| R6 | 共享数据空间多份（AxiomContext.Data / StateCenter.Transient / WorkFlowMethod Context） | 轮内瞬态 = `NodeRoundContext.Vars`（`IDataSpace`）+ 持久真源 = ur_nodes | HierarchyMethod | 跨轮跨层数据双写 |
| R7 | 取消/终止点状（foreach 头 / 循环边界 / 默认 Zero 步超时 / 外部看门狗） | `ExecutionScope` 根 + 层级派生，统一收敛路由 | HierarchyMethod/Center | 各层裸 await |
| R8 | 错误归因分散（ErrorClass / ErrorStep / FirstFatalError / AEF） | `ExecutionBoundaryException` 族 + `ErrorClassifier` 单一 | HierarchyMethod/Center | 各层错误字符串降级 |
| R9 | Agent/Produce 各自自建骨架执行器（RoundStepInvoker/类似实现） | 上移为 HierarchyMethod 默认 `INodeExecutor`（读源+函数+RecordStep 三合一），Agent/Produce 只注入领域策略 | HierarchyMethod | 消费方骨架代码瘦身 |
| R10 | 装配可空 null 一路透传 | `PortContractGuard`（装配期 fail-fast） | HierarchyMethod/Center | 同链静默分叉 |

---


## 六、分阶段应用路径

> 每阶段独立可编译、可回退、可验证；阶段间以"一个复发点消失 + 0 回归"为门槛推进。优先修"本次卡死"，再渐进收敛冗余。

### Phase 0 — 取证固化（本次复发点的确定性复现）
- 在 `NodeGraphOrchestrator`/步骤调用器接入 `ExecutionScope.Bound` 埋点前，先按 9.3 判定口径跑通一次 `agent_mcpinvoke_create_task_test.json`：日志确认卡在 `cline_execute` 读源、无 timeout/无 TO → 固化基线。
- 交付：诊断日志 msgId 补齐（`ResolveInputs` 读源子边界 + `round_timeout` 全量覆盖 execute+next）。

### Phase 1 — 引擎层有界（LoopFoldMethod）
- `FoldEngine.RunAsync`：把每次 `op.Expand` 纳入 `scope.Derive("step")` 竞速；空值守卫→熔断执法保持；确认 `SignalDrivenFoldEngine`/`DecisionFoldEngine` 复用同一引擎（消除平行 while）。
- 验证：人为注入 `op.Expand` 卡死 → 单步超时确定性收敛，不再挂整循环。

### Phase 2 — 编排 + 步骤有界（HierarchyMethod 为主，Agent 应用）
- `NodeGraphOrchestrator.RunRoundAsync`：每步 `scope.Derive` 包 `ExecuteStepAsync` + `OnAfterStepAsync`；`OnAfterStep` 持久化默认走 Outbox。
- `INodeExecutor`/`INodeExecutorHooks` 契约带 scope；Agent `RoundStepInvoker` 读源强制子边界、`_stepTimeout` 从轮预算派生（不再默认 Zero）。
- 验证：`guard_input→provide_prompt→cline_execute→rule_match` 全程；注入读源卡死 → 单步超时转 `TO` + 栈自证；无外部看门狗介入。

### Phase 3 — 存储单一写门 + 读源收敛（HierarchyMethod/Center）
- `NodeWriteCoordinator`（ur_nodes+KeyMapping+LiteDB 边统一写序、有界）；`NodeReadView`（task 作用域读）取代 `ResolveInputsAsync` 跨库拼读；全部接口带 ct。
- 验证：并发写不互锁；读源不再无日志卡点；`Check-SingleWriter.ps1` + 运行时 `AssertSingleWriter` 双保险。

### Phase 4 — 冗余收敛（覆盖 WorkFlowMethod + 平行实现）
- R2 收敛 `WorkFlowMethodFunctionInvoker` 双份 → 单一 `INodeFunctionInvoker`；R9 消费方骨架上移为 HierarchyMethod 默认执行器；R5/R6 关系/历史/数据面收敛。
- 验证：Agent.sln / Produce.sln 跨 sln 0 警告 0 错误；行为回归（无重复逻辑路径）。

### Phase 5 — 装配契约 + 治理强制
- `PortContractGuard`（缺端口 fail-fast）；移除同链 null 静默分叉；把 `SingleImplementation`/`RedundancyElimination` 从 `[Strategy]` 提升为**运行时断言**的接口落地。
- 验证：漏装配一个端口 → 启动失败（显式），不再进入诡异路径。

## 七、治理与验证

### 7.1 治理对齐（`.clinerules`）
| 公理 | 本框架承载 | 强制手段 |
|------|-----------|----------|
| `SEP.DeterministicCoreDisjoint` | `NodeWriteCoordinator` 单一写 + `AssertSingleWriter` | 运行时断言 + `Check-SingleWriter.ps1` |
| `StateRootSingleSource` | ur_nodes 单真源 + `NodeReadView` | 装配契约 + 单写者纪律 |
| `AX.TerminationPriority` / `TerminationBipartition` | `ExecutionScope` 根 + 层级子边界；预算终止引擎内强制 | 强类型 `TO` + 收敛路由 |
| `InformationMonotonicity` | Outbox 只增不丢 + 有界窗口归档 | 持久化默认异步化 |
| `AX-CORE.EssentiallyUndecidable` | 探索终止（不可判定）外置，预算终止（可判定）引擎内 | scope 取消 + 边界竞速 |
| `LogicCorrectness`/`SingleImplementation`/`RedundancyElimination` | Center 唯一实现 + R1–R10 收敛映射 | 运行时断言（从 `[Strategy]` 提回） |

### 7.2 验收口径（每次复跑按此读日志）
```
任务入队 → 引擎根 scope 开始（混合调度 timeout 收敛为引擎内，不再靠 300s/外部看门狗）
 ├─ guard_input / provide_prompt 立即开始（存储健康无关）
 ├─ cline_execute 步骤开始
 │    ├─ 无「入参解析完成」+ 子边界到点 → 单步超时(读源自证) → 转 TO   ✅ 本次卡点根治
 │    └─ 有「入参解析完成」→ 函数开始/返回正常
 ├─ rule_match 完成 → 轮收敛
 └─ 全程无 CoreWebView2…UI thread / ECS 封送超时（线程隔离）
```

---

## 八、风险与待决

| # | 风险/待决 | 缓解 / 决策点 |
|---|-----------|---------------|
| 1 | 取消/超时误杀真实 LLM 长轮或合法暂停 | 步骤预算**默认从轮预算/函数元数据推导**；LLM/暂停门步骤预算可放宽，但必须走统一 `CheckLlmGateAsync`（可观测、可取消），且全局 `BUDGET_ALLOC` 对齐（axioms §9） |
| 2 | `NodeWriteCoordinator` 收敛改变既有写序/事务边界 | Phase 3 独立；保留单事务 Outbox 选项；逐介质回归（ur_nodes/KeyMapping/LiteDB 边） |
| 3 | Outbox 落库时序变化 → 读取侧读到旧值 | 读取侧已"节点输出优先"（`NodeReadView`）；时序断言进测试 |
| 4 | 收敛 WorkFlowMethod 影响面大（跨 sln） | Phase 4 独立 + 可回退；先只收敛"平行执行走廊"（R2），整引擎降级放最后 |
| 5 | 运行时 `AssertSingleWriter` 是否可落地（五套写门现状） | Phase 5 前完成写门盘点 + 脚本清单；不阻断 Phase 1-3 |
| 6 | 装配契约 fail-fast 可能暴露既有"缺端口也能跑"的隐性依赖 | 先对 Agent/Produce 两组合根做契约补全审计，再启用 fail-fast |
| 7 | 栈转储 `DumpStallEvent` 的平台实现（Windows dump 线程栈） | 复用仓库既有（P5 规划）线程栈事件通道；非阻塞实现，避免诊断自身卡死 |

---

## 附：本文与既有文档的承接

| 既有文档 | 本文承接 |
|----------|----------|
| `docs/analysis/2026/9/9.1/2026-09-01-framework-refactor-arch.md`（D1-D5 / R1-R5） | §3.2 依赖分层、§4 可取消边界、§5 收敛映射直接落实其 R1(写门)/R2(Outbox)/R3(线程)/R4(可取消)/R5(强制) |
| `docs/analysis/2026/9/9.3/2026-09-02-function-callchain-arch-optimization.md`（判定口径/Stage1-5） | §4.4 阻塞自证 = Stage1-d 落地；§4.2 编排层接入 = Stage2 落地 |
| `.clinerules/axioms.md` / `formula.md` | §7.1 治理对齐；R 收敛映射对齐 `SingleImplementation` 修复动作 |

*本文为设计文档；落地依 §6 各阶段独立推进并各自编译/回归验证。*

