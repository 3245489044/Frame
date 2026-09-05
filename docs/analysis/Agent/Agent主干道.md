# Agent 主干道（Main Pipeline）介绍

> **定位**：本文档描述 `Agent.Core` 当前的主干道——一条从输入到终止、把所有逻辑（装载 / 装配 / 调度 / 循环 / 单轮五步 / 决策 / 收割 / 持久化 / 运行时发布 / 暂停）显式串起来的**唯一控制流**。
> 本结构是 `AgentRunner` 一次 run 的完整流转，也是理解整个 Agent 运行逻辑的**单一入口**。
> 阅读本文档后，应能回答"一次 Agent 运行，代码是怎么从头走到尾的"。

---

## 一、为什么需要一条主干道

重构前，Agent 的主循环逻辑散落在**一个约 80 行的跳转式大方法**里，真实数据流被三处机制打散、难以跟踪：

| 分散点 | 问题 |
|--------|------|
| **隐式 AsyncLocal 透传** | token / budget / taskKey / schedulingMode 等业务状态藏在 `Functor`（AsyncLocal）里，方法签名不体现，深层代码无参读取，主控制流不可见 |
| **双执行路径** | LLM 路径与零 LLM 规则路径各自装配、各自决策，主干在入口分叉 |
| **可变组合根** | `AgentRunner` 暴露运行期可变 setter 属性（`CancellationCheck`/`LlmGate`/`TaskPoolPersistence`），实例"看似可变"，装配态与运行态混在一起 |
| **决策多类重复** | 决策词解释在 `DecisionWordMapper` 逐方法转发到 `DecisionWordClassifier`，让"词汇逻辑"看起来有两个实现 |

本次重构（Step1/3/4/5）收敛为**一条显式主干道**：

- 主控制流 = `RunMainPipelineAsync`，按 `[A]→[B]→[C]→[E]→[F]` 五阶段显式组织，从头读到尾即懂全部流转。
- 中间产物 = `AgentRunEnvironment`（单根状态 / 决策枢纽 / 调度器 / 取消源 / run 作用域 / 运行期注入）随主干显式承载。
- 业务状态回归主干显式承载；AsyncLocal 仅保留真正的横切量。
- 运行期注入收敛为 `AgentRunHooks` 显式参数；决策词解释单点到 `DecisionWordClassifier`。

---

## 二、主干道总览

```
RunAsync(input)
 │
 ├─[A] LoadState            装载/播种单根状态（AgentTaskItem）
 ├─[B] BuildRunEnvironment  装配运行环境（决策枢纽 + 调度器 + 取消源 + run 作用域 + hooks）
 ├─[C] RunMainLoop          循环主干（SnapshotDrivenLoopEngine 唯一 while）
 │        └─ 每轮：RoundLoopDriver → RoundLifecycle 五步
 ├─[E] FinalizeRunAsync     终止收敛 + 终态发布
 └─[F]                      边界持久化 + 完成日志
```

实现入口（`Agent.Core/Loops/AgentRunner.cs`）：

```csharp
private async Task<AxiomLoopResult> RunMainPipelineAsync(
    AgentTaskItem item, TimeSpan timeout, CancellationToken ct, AgentRunHooks? hooks)
{
    // [A] 装载/播种单根状态
    var state = LoadState(item);

    // [B] 装配运行环境（决策枢纽 + 调度器 + 取消源 + run 作用域）
    using var env = BuildRunEnvironment(state, timeout, ct, hooks);

    // [C] 循环主干（快照驱动引擎唯一 while）
    var loopResult = await RunMainLoopAsync(env, state, ct).ConfigureAwait(false);

    // [E][F] 终止收敛 + 终态发布 + 边界持久化
    return await FinalizeRunAsync(env, state, loopResult, ct).ConfigureAwait(false);
}
```

### 阶段总览

| 阶段 | 方法 | 职责 | 关键产物 |
|------|------|------|----------|
| **[A] 装载** | `LoadState` | 校验输入、播种默认策略集、同步执行器选择视图 | 就绪的 `AgentTaskItem`（单根状态） |
| **[B] 装配** | `BuildRunEnvironment` | 启动日志、发布 started、建决策枢纽/调度器/取消源/run 作用域 | `AgentRunEnvironment`（一次性，`using` 持有） |
| **[C] 循环** | `RunMainLoopAsync` | 快照驱动引擎唯一 while：单轮五步 + 轮后混合调度/发布/持久化 | `AxiomLoopResult` |
| **[E] 收敛** | `FinalizeRunAsync` | 终止收敛（引擎承载终值）、终态发布 | 最终决策 |
| **[F] 终态** | `PersistRuntimeAsync` | 唯一持久根写回 `PayloadJson`、完成日志 | 落库结果 |

---

## 三、阶段详解

### [A] 装载单根状态（LoadState）

**职责**：把传入的任务快照校验、播种为就绪的循环 TState。

```csharp
private static AgentTaskItem LoadState(AgentTaskItem item)
{
    if (item is null) throw new ArgumentNullException(nameof(item));
    if (string.IsNullOrWhiteSpace(item.UserInput))
        throw new ArgumentException("user_input 不能为空", nameof(item));

    SeedStrategyDefaults(item);   // 幂等（去重）
    SyncActiveStrategyView(item);
    return item;
}
```

- **单根原则（AX.StateRootSingleSource）**：`AgentTaskItem` 既是循环 TState，也是唯一持久根（任务 = 快照）。`AxiomContext` 仅作每轮派生的执行租约，不在此恢复/新建平行快照。
- **统一播种**：任务池/反序列化路径的 item 可能未经 `CreateItem` 播种，此处统一播种默认策略集并同步执行器选择视图，保证 `CurrentStrategyId` / `AvailableStrategyIds` 非空（解析不歧义、SwitchStrategy 守卫可接受），且首轮不静默回退 cline。
- **幂等**：`SeedStrategyDefaults` 去重，重复调用安全。

---

### [B] 装配运行环境（BuildRunEnvironment）

**职责**：构造 `AgentRunEnvironment`——一条主干道 [B]→[F] 期间共享的决策枢纽、调度器、取消源、run 作用域、运行期注入。一次装配，实例装配后不可变。

```csharp
private AgentRunEnvironment BuildRunEnvironment(AgentTaskItem state, TimeSpan timeout, CancellationToken ct, AgentRunHooks? hooks)
{
    AgentLog.Log("AgentRunner_RunHybridInternalAsync_start", ...);   // 启动日志
    _runtimeState?.Publish(state.ToRuntimeSnapshot("started"));       // 运行时状态发布：run 启动边界

    var decisionPoint = new DecisionPoint(_mapper);                   // 决策枢纽（唯一决策点）

    // 调度器：导演按任务调度模式选择
    IStateDirector director = state.SchedulingMode switch
    {
        SchedulingMode.Config => new ConfigStateDirector(_configStore, state.SchedulerRulesKey),
        SchedulingMode.Deterministic => NoopStateDirector.Instance,
        _ => new LlmStateDirector(llmClient: _llmClient, jsonGenerator: _llmJsonGenerator),
    };
    var scheduler = new AiSchedulerStateMachine<AgentTaskItem>(director, new TransitionGuard(), state);

    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);    // 取消源（超时 + 外部令牌）
    cts.CancelAfter(timeout);
    var scope = Functor.Begin("agent-run");                            // run 作用域（AsyncLocal 透传载体）

    // 运行期注入（hooks）→ run 作用域
    AgentRunCancellation.Set(scope, cts.Token, timeout);
    AgentRunCancellation.SetTaskKey(scope, state.TaskKey);
    AgentRunCancellation.SetCancellationCheck(scope, hooks?.CancellationCheck);
    AgentRunCancellation.SetLlmGate(scope, hooks?.LlmGate);

    return new AgentRunEnvironment(state, decisionPoint, scheduler, cts, scope, hooks);
}
```

要点：
- **决策枢纽**：`DecisionPoint` 纯函数化，每轮以 `EvaluateRound(lease, ...)` 显式传入当前租约，不持有可变状态指针。
- **调度器**：`AiSchedulerStateMachine`（LLM + 计算混合），导演按任务 `SchedulingMode` 选择——Hybrid（LLM 提议）/ Config（ur_config 规则表，零 LLM）/ Deterministic（纯规则迭代）；三者均经 `TransitionGuard` + `ApplyGuarded` 校验施加，落在同一混合调度框架内。
- **业务状态显式承载**：`SchedulingMode`/`SchedulerRulesKey` 由 `AgentRunEnvironment.State` 显式承载，**不写入 AsyncLocal**（消除死通道，见 §五）。
- **运行期注入**：`hooks?.CancellationCheck` / `hooks?.LlmGate` 注入 run 作用域，供深层执行端横切读取。


---

### [C] 循环主干（RunMainLoop → RoundLoopDriver → RoundLifecycle）

**职责**：快照驱动引擎唯一 while，注入单轮动作（管线）、评估（DecisionPoint）、轮后钩子（混合调度 + 运行时发布 + 边界持久化）。

```csharp
private async Task<AxiomLoopResult> RunMainLoopAsync(AgentRunEnvironment env, AgentTaskItem state, CancellationToken ct)
    => await RoundLoopDriver.RunSingleRootAsync(
        item: state,
        taskKey: state.TaskKey,
        executeStep: (lease, token) => ExecutePipelineRoundAsync(lease, token),       // 单轮动作（五步管线）
        evaluate: (lease, r, errCls) => env.DecisionPoint.EvaluateRound(lease, r, errCls), // 决策单点
        onRoundCompleted: (s, token) => NextHybridRoundAsync(env, s, token),          // 轮后钩子
        ct: env.RunToken).ConfigureAwait(false);
```

#### 循环驱动的分层（自下而上，单一载体）

| 层 | 载体 | 职责 |
|----|------|------|
| 引擎 | `SnapshotDrivenLoopEngine<TState,TResult>` | 唯一 while 引擎（非限定快照驱动） |
| 驱动 | `LoopDriver`（Agent.Operation） | lastDecision 闭包 + 预算终止收敛 + 结果收敛，TState 解耦 |
| 单根包裹 | `RoundLoopDriver`（Agent.Core） | `AgentTaskItem` 专属薄外观：租约派生/收割 + 五步包裹 + 结果转换 |
| 五步 | `RoundLifecycle`（WorkFlowMethod） | Execute→Collect→Evaluate→Decide→Adjust 单一载体 |

> LLM 路径与零 LLM 规则路径**共用** `RoundLoopDriver` + `RoundLifecycle`（消除两套循环脚手架），区别仅在单轮动作 `executeStep` 与评估来源。

#### 单轮五步（RoundLifecycle）

```
①Execute → ②Collect → ③Evaluate → ④Decide → ⑤Adjust
```

- **①Execute**：执行单轮动作（骨架五步管线 pre→lease→run→post→wrap，产出 `AxiomActionResult`）。
- **②Collect**：归一错误分类（显式优先，否则按文本分类）。
- **③Evaluate**：进度（`ProgressUpdater`）与熔断（`BudgetEscalator`）分别走单一入口更新 `ProgressToken` / `FailureBudget`。
- **④Decide**：终止优先（`TerminationDecider`：halt / token 耗尽 → 预算终止）；否则成功即 TS、未成功则 Continue。
- **⑤Adjust**：推进轮次号、生成下一步提示。

#### 单轮编排管线（executeStep 内部）

`StaticAsyncPipeline` 声明式五阶段：`PreRound(空间边界) → LeaseDerive(租约颁发) → RunRound(骨架执行+错误分类) → PostRound(逻辑边界) → Wrap(动作结果)`，任一阶段 `IsFatal=true` 即中止后续阶段。

#### 轮后钩子（NextHybridRoundAsync）

每轮收割后：
1. **混合调度**：`env.Scheduler.AdvanceAsync`（LLM 提议 + 计算守卫）变更整体状态（只改策略/执行状态，不触碰确定性核心）；回写 ActiveStrategy / ExplicitExecutor。
2. **运行时发布**：`_runtimeState.Publish(state.ToRuntimeSnapshot("running"))`。
3. **边界持久化**：`PersistRuntimeAsync` 把统一任务快照写回 `PayloadJson`。


---

### [E][F] 终止收敛 + 终态发布 + 边界持久化（FinalizeRunAsync）

```csharp
private async Task<AxiomLoopResult> FinalizeRunAsync(AgentRunEnvironment env, AgentTaskItem state, AxiomLoopResult loopResult, CancellationToken ct)
{
    _runtimeState?.Publish(state.ToRuntimeSnapshot("completed"));       // 终态发布
    await PersistRuntimeAsync(state, ct, env.Hooks).ConfigureAwait(false); // 唯一持久根写回 PayloadJson

    AgentLog.Log("AgentRunner_RunHybridInternalAsync_completed", ...);  // 完成日志
    return loopResult;
}
```

- **终止二分（AX.TerminationBipartition）**：预算终止（TS/TO/TM，可判定）由引擎内 `budgetTerminate` + `LoopDecisionConvergence` 裁决、阈值到达即触发；探索终止（不可判定）外置为取消令牌。
- **唯一持久根**：`AgentTaskItem`（不再持久化 `AxiomContext` 快照，避免双持久根）。
- **与持久化无耦合**：运行时终态发布独立于持久化。

---

## 四、关键载体

### 1. AgentRunEnvironment（主干中间产物载体）

一次 run 的 [B]→[F] 共享环境，`using` 持有，`Dispose` 释放取消源与 run 作用域：

| 成员 | 类型 | 职责 |
|------|------|------|
| `State` | `AgentTaskItem` | 单根状态（唯一持久根；含业务状态 SchedulingMode/RulesKey） |
| `DecisionPoint` | `DecisionPoint` | 决策枢纽（主循环唯一决策点） |
| `Scheduler` | `AiSchedulerStateMachine<AgentTaskItem>` | 混合调度状态机（LLM + 计算；路由/策略单一裁决） |
| `Cts` | `CancellationTokenSource` | 取消源（超时 + 外部令牌） |
| `Scope` | `IFunctorScope` | run 作用域（AsyncLocal 透传载体） |
| `Hooks` | `AgentRunHooks?` | 运行期注入（取消检查 / LLM 门 / 任务池持久化） |
| `RunToken` | `CancellationToken` | 取消令牌（探索终止信号之一） |

### 2. AgentRunHooks（运行期注入，显式参数）

```csharp
public sealed record AgentRunHooks(
    System.Action? CancellationCheck,
    Func<CancellationToken, Task>? LlmGate,
    ITaskPoolPersistence<AgentTaskItem>? TaskPoolPersistence);
```

- **装配态（`AgentRunnerAssemblyInput`）与运行态（`AgentRunHooks`）分离**：`AgentRunner` 实例装配后完全不可变。
- 运行期注入随 `RunAsync(item, ct, hooks)` 显式传入，不再作为实例可变 setter 属性。
- `null = 未注入`，对应 no-op（直跑 / 非任务池驱动）。

### 3. AgentRunCancellation（run 作用域横切量透传中枢）

只保留真正的**横切量**（跨深层调用链、免参读取合理）：

| 通道 | 载体 | 读取方 |
|------|------|--------|
| 取消令牌 | `RunCancellationToken` | `ClineAgentExecutor` / `ClineExecutorFunction` |
| 整体预算 | `RunBudget` | `ClineAgentExecutor` |
| 任务键 | `RunTaskKey` | 暂停门（`AgentTaskPool`） |
| 取消检查 | `RunCancellationCheck` | `AgentLoopFunction.CheckCancellation` |
| LLM 门 | `RunLlmGate` | `AgentLoopFunction.CheckLlmGateAsync` |
| 调度导演上下文 | `RunSchedulerContext` | SK 辅助工具（`SchedulerAssistantTools`） |

> **Step3 消除**：`SchedulingMode` / `SchedulerRulesKey` 原为 AsyncLocal 死通道（`GetSchedulingMode` 无消费方），已移除——业务状态由主干 `AgentRunEnvironment.State` 显式承载。

---

## 五、三条数据流（主干道如何"显式"）

| 数据 | 承载方式 | 说明 |
|------|----------|------|
| **业务状态** | `AgentRunEnvironment.State`（`AgentTaskItem`）显式承载 | SchedulingMode / RulesKey / token / budget / 策略 等，随主干阶段显式传递，不藏 AsyncLocal |
| **横切量** | `AgentRunCancellation`（AsyncLocal） | 取消令牌 / 预算 / 任务键 / 取消检查 / LLM 门 / 调度上下文，跨深层调用链免参读取 |
| **运行期注入** | `AgentRunHooks`（RunAsync 显式参数） | 取消检查 / LLM 门 / 任务池持久化，随 env 在 [B] 注入 |

三条流各归其位：**业务状态显式、横切量最小化透传、运行期注入显式参数**——这是主干道"从代码可读"的关键。


---

## 六、重构前后对比

| 维度 | 重构前 | 重构后（当前主干道） |
|------|--------|----------------------|
| 主控制流 | `RunHybridInternalAsync` 约 80 行跳转式大方法 | `RunMainPipelineAsync` 五阶段显式（[A]→[F]） |
| 中间产物 | 局部变量散落 | `AgentRunEnvironment` 单一载体 |
| 业务状态 | AsyncLocal 隐式透传（含死通道） | `AgentRunEnvironment.State` 显式承载 |
| 运行期注入 | 实例可变 setter 属性（每次运行前赋值） | `AgentRunHooks` 显式参数（实例不可变） |
| 决策词解释 | `DecisionWordMapper` 逐方法转发 | 单点到 `DecisionWordClassifier` |
| 循环脚手架 | LLM / 规则两套 | `RoundLoopDriver` + `RoundLifecycle` 单一载体 |

### 涉及文件

| 文件 | 角色 |
|------|------|
| `Agent.Core/Loops/AgentRunner.cs` | 主干实现（`RunMainPipelineAsync` + 阶段方法 + `AgentRunEnvironment` + `AgentRunHooks`） |
| `Agent.Core/Loops/RoundLoopDriver.cs` | 单根循环驱动（AgentTaskItem 专属薄外观） |
| `Agent.Operation/Loops/LoopDriver.cs` | 通用单根循环驱动（TState 解耦） |
| `WorkFlowMethod/LoopFramework/Axioms/RoundLifecycle.cs` | 单轮五步单一载体 |
| `Agent.Core/Loops/DecisionPoint.cs` | 主循环唯一决策枢纽 |
| `Agent.Operation/Loops/DecisionWordClassifier.cs` | 决策词解释唯一权威 |
| `Agent.Core/Orchestration/AgentOrchestrator.cs` | 组合根（装配 `AgentRunnerAssemblyInput` + 组装 `AgentRunHooks`） |
| `Agent.Runtime/State/Loops/AgentRunCancellation.cs` | run 作用域横切量透传中枢 |

---

## 七、总结

**Agent 主干道 = 一条从输入到终止、所有环节显式挂载的单一控制流。**

- 读代码从 `RunMainPipelineAsync` 开始，五阶段 [A]→[F] 即完整流转。
- 业务状态经 `AgentRunEnvironment` 显式承载，横切量经 AsyncLocal 最小化透传，运行期注入经 `AgentRunHooks` 显式传入。
- 决策经 `DecisionPoint` 单点、调度经 `Scheduler` 显式注入、词汇解释单点到 `DecisionWordClassifier`。
- 循环脚手架收敛到 `RoundLoopDriver` + `RoundLifecycle` 单一载体，LLM / 零 LLM 共用同一主干。

