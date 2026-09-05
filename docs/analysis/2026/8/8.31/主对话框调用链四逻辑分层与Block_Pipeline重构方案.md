# 主对话框调用链：四逻辑分层与 Block/Pipeline 重构方案

> 目标：把主对话框历史主循环（发消息 → 折叠 → 子智能体 → 节点写入）中**前端逻辑、节点逻辑、调用逻辑、循环逻辑**
> 四者耦合的问题，按职责分层拆解，引入 `CsharpPrj\BasicMethod\Map\Block` 与 `CsharpPrj\Modules\Pipeline` 两个框架承载。
> 最终形态：`RunSignalAsync` 巨型方法退化为一个小编排门面；四类逻辑各自落到独立组件，可独立测试、可插拔装配。
>
> 本文档为优化重构方案（非 PR 记录）：定位耦合点 → 框架能力对照 → 分层目标架构 → 组件拆分（含接口签名）
> → 分阶段路线与验收标准。

***

## 1. 现状：一条链路里塞了四类逻辑

主对话框发送一条消息的完整调用链（已取证）：

```
【前端逻辑】UIRunJson → RunJsonAsync(action查表) → ConversationSendStandardAsync
    ① action 分发查表 + 会话归拢(EnsureHistoryTaskAsync) + 回显(RenderViewAsync)
        ↓ HistoryRuntime.StartAsync
【调用逻辑】AgentHistoryRuntimePort.StartAsync → ConversationSessionService.SendAsync
    ② 运行端口适配 + 会话串行锁(send)
        ↓ ConversationPipeline.RunSignalAsync   ★耦合重灾区
【循环逻辑】spec断言 → SeedWorkflow(种子) → runnerFactory(spec) → FoldConversationRunner.Build
    → SkConversationFoldOperator → answer/cline 执行器 → ExtractStages → MapToConversation
【节点逻辑】EnsureSession/CreateSession/LoadLog/PersistHistory/SyncHistoryRelations/LinkTaskNodes
【循环逻辑】FinalizeConversationState(Round+/状态) + 内联日志/审计
```

`ConversationPipeline.RunSignalAsync`（[ConversationPipeline.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs#L146-L292)，
约 150 行）是典型**长方法**，四类逻辑全部内联在一个方法体内。

### 1.1 四类逻辑的定义与现状杂糅点

| 逻辑       | 定义                                                            | 现状位置                                                                                | 耦合表现                                    |
| -------- | ------------------------------------------------------------- | ----------------------------------------------------------------------------------- | --------------------------------------- |
| **前端逻辑** | UI 事件 → 命令解析 → 响应式回显                                          | `AgentUiLibraryRuntime/Conversation`、`ConversationViewService`                      | action 用 if/Switch 硬编码查表；回显与发送耦合在同一入口方法 |
| **调用逻辑** | 谁去调、调哪个实现（spec 解析、端口委托、执行端选择）                                 | `_specResolver`/`_runnerFactory` 注入 + `ConversationHosting.TryBuildFoldOperator` 装配 | 调用解析与业务流程混在 pipeline 构造 & 生命周期阶段里       |
| **节点逻辑** | 历史/任务/节点 的读写与关系边（ur\_histories / ur\_nodes / cross\_db\_link） | 内联 `EnsureSessionAsync`\~`LinkTaskNodesAsync` 一组私有方法                                | 与主循环编排同一类，未独立成可插拔单元                     |
| **循环逻辑** | 多轮推进、信号折叠、Round/Status 状态机                                    | `SeedWorkflow` + outer fold runner + `FinalizeConversationStateAsync` + 异常信封        | 完成信封与异常上报、取消语义纠缠；子智能体步骤推进无显式编排          |

> 根因：**职责没有对应的载体**。四类逻辑被「一条消息 = 一次编排」这一个方法 / 入口天然压在一起，
> 缺的是把它们各自沉降为独立组件、并允许框架接管「流程控制」的抽象。

***

## 2. 待引入框架能力对照

### 2.1 BasicMethod.Block —— 分段函数执行块（分块执行 + 隔离检测）

数学基础 = 分段函数 `f(x)={f₁(x),...,fₙ(x)}`；每块独立定义域/值域、独立 try/catch 隔离。

| 能力   | 类型                                                                | 对四逻辑的用途                          |
| ---- | ----------------------------------------------------------------- | -------------------------------- |
| 块接口  | `IBlock{Name, ExecuteAsync(BlockContext)}`                        | 把**有边界的确定性单元**（节点持久化基元）封装为块      |
| 块上下文 | `BlockContext{Data, Diagnostics}`                                 | 块间传参 + 诊断累积（对齐「诊断数据内存态」约定）       |
| 顺序管线 | `ExecutionPipeline.RunAsync`（条件/重试/诊断/超时/序列）                      | 编排**节点写入基元序列**（保证隔离 + 条件跳过 + 时序） |
| 内置块  | `ConditionalBlock/TimeoutBlock/RetryBlock/SequenceBlock/LogBlock` | 每块可独立条件/重试/诊断，不污染业务              |
| 抽象基类 | `BlockBase/RetryBlockBase/DiagnosticBlockBase`                    | 子智能体单元可按块封装，带重试+前后诊断             |

**Block 定位 = 单元执行层（Unit Execution）**：负责「单个有边界的动作」及其隔离/诊断，
不做跨单元的流程决策。

### 2.2 Modules.Pipeline —— 通用管线框架（编排）

三体系 + 组合子 + 中央生命周期：

| 能力     | 类型                                                                                      | 对四逻辑的用途                         |
| ------ | --------------------------------------------------------------------------------------- | ------------------------------- |
| 链式传参   | `IPipeline<TIn,TOut>` / `PipelineBase` / `DelegatePipeline` / `.Then()`                 | 串接「调用链」：解析→种子→运行→映射→回写          |
| 编排器    | `Pipeline<TIn,TOut>` / `StagePipeline<TContext>`                                        | 会话发送的**阶段化编排**                  |
| 中央生命周期 | `CentralPipeline<TContext>`（Initialize→Load→Validate→Process→Map→Save→Cleanup→Complete） | 把 `RunSignalAsync` 显式切成生命周期阶段   |
| 组合子    | `ForEach/Parallel/Branch/Switch/TryCatch/Retry/Conditional` + `Loop()/Then()/Compose()` | spec.Steps 推进、多轮循环、异常/取消降级      |
| 策略注册   | `StrategyRegistry<TKey,TIn,TOut>`                                                       | 前端 **action 分发表**（替换 if/switch） |
| 上下文    | `PipelineContext` / `Guard` / `ErrorCollector`                                          | 全程共享上下文 + 完成信封聚合                |

**Pipeline 定位 = 编排层（Orchestration）**：负责「流程控制」——阶段顺序、分支、循环、命令分发、结果聚合，**不做具体动作**。

### 2.3 两框架职责边界（一图）

```
Pipeline（编排：怎么走）          Block（执行：每个动作怎么干）
─────────────────────           ─────────────────────────
选择阶段/命令/Spec               确定执行一个动作
组合(顺序/分支/循环)              隔离异常/重试/超时
汇总结果/完成信封                 读写节点/落库一个基元
```

> 选择准则：**有跨步骤的顺序/分支/循环决策 → 交给 Pipeline；单个有边界动作及其隔离 → 交给 Block。**
> 「节点逻辑」-> Block 每条基元独立块；「循环/调用」-> Pipeline 编排「子智能体执行块」。

***

## 3. 分层目标架构

四层各司其职，依赖单向向下：

```
┌──────────────────────────────────────────────────────────────┐
│  前端逻辑层  UI Adapter（只管「消息↔命令」映射 + 回显）            │
│  AgentUiLibraryConversation / ConversationViewService          │
│  ActionDispatcher: StrategyRegistry<action,RunRequest,Task>    │
└───────────────────────────┬──────────────────────────────────┘
                            │ 仅依赖窄契约 IConversationUiPort
┌───────────────────────────▼──────────────────────────────────┐
│  编排层  Modules.Pipeline（流程控制：阶段/命令/组合/汇聚）         │
│  ConversationCentralPipeline: CentralPipeline<SessionCtx>      │
│     Initialize→Load→Validate→Process→Map→Save→Finalize         │
│     Process[: 子智能体步骤组合 Then/Branch/Loop; 异常/取消降级]    │
│  ActionDispatcher(StrategyRegistry)  ← 命令表下沉至此            │
└───────┬──────────────────────┬────────────────────────────────┘
        │ 解析出 spec+runner    │ Process 阶段委托
┌───────▼─────────┐   ┌─────────▼──────────────────────────────┐
│ 调用逻辑层        │   │ 循环逻辑层  折叠/子智能体推进                 │
│ SpecResolver    │   │ FoldConversationRunner.Build(spec,...)  │
│ RunnerFactory   │   │ SkConversationFoldOperator → 执行器       │
│ (装配组合根注入)  │   │ Pipeline 组合子编排 spec.Steps 推进          │
└─────────────────┘   └─────────────────────────────────────────┘
        │
┌───────▼──────────────────────────────────────────────────────┐
│  节点逻辑层  BasicMethod.Block（单元执行：每个写/读一个隔离块）       │
│  NodeWritePipeline: ExecutionPipeline                         │
│   EnsureSession / CreateSession / LoadLog                     │
│   PersistHistory / SyncRelations / LinkTaskNodes               │
│  AgentExecutorBlock(sumUp)/ClineExecutorBlock(子智能体单元)     │
└──────────────────────────────────────────────────────────────┘
```

依赖规则：

* **上层只见下层的端口接口，不见实现**（对齐「All functions must pass parameters through FunctionContext / 端口注入」约定）。

* **Pipeline 不写库、不做 LLM 动作**；具体动作由 Block 执行端承担，Pipeline 只编排。

* **Block 不做跨步决策**；是否执行由条件块 / 外层 Pipeline 决定。

***

## 4. 四类逻辑的组件拆分（含目标接口签名）

### 4.1 前端逻辑层 —— ActionDispatcher 命令表（替换 if/switch）

现状：`RunJsonAsync` 内 `action` 用 if/分支查 handler，`conversation` 与 `runloop` 等硬编码并列。

改：把「action → handler」下沉为 `StrategyRegistry<ActionKey, RunRequest, Task>`，UI 入口只做「分发 + 收尾回显」。

```csharp
// 编排层 Modules.Pipeline.Registry.StrategyRegistry 充当命令表
public static class UiActionDispatcher
{
    // 注册：UIRunJson/runJson、conversation/send、conversation/reset、conversation/clear、runloop/... 各一个 strategy
    public static StrategyRegistry<string, RunRequest, Task> Build(ActionBacking backing);
}

// 前端入口退化为一句话
public static async Task Handle(ActionKey key, RunRequest req, CancellationToken ct)
    => await _dispatcher.TryExecute(key, req, ct);   // 未命中 → 返回明确诊断，不静默
```

`HeaderlessView` 回显（`ConversationViewService.RenderViewAsync`）保持在 UI 层，由运行结果 `ConversationReply` 驱动——**回显不参与编排**。

### 4.2 编排层 —— ConversationCentralPipeline（把 RunSignalAsync 切成 PHP 阶段）

用 `CentralPipeline<SessionContext>` 表达「一条消息 = 一次会话中央管线.run」。`SessionContext` 承载 statement 全过程：
`item / log / spec / lease / result / stages / comments`。

```csharp
public sealed class ConversationCentralPipeline
{
    public IPipeline<ConversationSignal, ConversationReply> Build(
        SpecResolvePort specResolver,        // ★调用逻辑依赖，经端口注入
        RunnerPort runner,                    // ★调用逻辑依赖（fold runner）
        NodeWritePipeline nodeWrite,          // ★节点逻辑（Block 序列）
        IConversationNodeMapper mapper);      // 终答归并

    // 装配为 CentralPipeline 生命周期：
    //  Initialize  : EnsureSession / CreateSession
    //  Load        : LoadLog / RenderPriorWire / AppendUser
    //  Validate    : spec!=null（空 → 明确诊断，不落库不费额度）
    //  Process     : SeedWorkflow + runner(spec)   ★循环逻辑
    //  Map         : ExtractStages + MapToConversation + 终答 append
    //  Save        : NodeWritePipeline.Persist + nodeMapper 输出
    //  Finalize    : 完成信封（Round/Status/诊断/审计，统一兜底令牌）
}
```

与现状 `RunSignalAsync#L146-L292` 的差异：

1. `EnsureSession~CreateSession / LoadLog / PersistHistory` 从方法体抽出 → **节点逻辑层**（4.3）。
2. `spec 断言 / seed / 运行 / extract / map / finalizing` → 各自 pipeline 阶段，`PipelineExecutor` 按阶段缓存结果。
3. **异常/取消信封**由 `PipelineTryCatch` + `WithTimeout` 组合子接管，不再内联 `catch` 三分类手写。

### 4.3 节点逻辑层 —— NodeWritePipeline（Block 隔离序列）

现状内联的六个私有方法（`EnsureSessionAsync/CreateSessionAsync/LoadLogAsync/PersistHistoryAsync/
SyncHistoryRelationsAsync/LinkTaskNodesAsync`，[ConversationPipeline.cs#L376-L469](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs#L376-L469)）
各自封装为独立 `IBlock`，用 `ExecutionPipeline` 串联。

```csharp
public sealed class NodeWritePipeline
{
    public IBlockPipeline Build(
        IConversationStore store, IHistoryStore? history,
        IHistoryRelationManager? relations,
        INodeTaskLinker? nodeTaskLinker)
    {
        return new ExecutionPipeline("NodeWrite")
            .AddBlock(new EnsureSessionBlock(store))          // IBlock：定位/创建（纯落库）
            .AddBlock(new CreateSessionBlock(store))           // 仅 EnsureSession 不存在分支执行（条件块）
            .AddBlock(new LoadLogBlock(history))               // ur_histories 真源 → 空日志兜底
            .AddBlock(new PersistHistoryBlock(store, history, relations))
            .AddBlock(new SyncRelationsBlock(relations, nodeTaskLinker))  // cross_db_link + round_sequence
        ;   // 每块带 RetryBlockBase/DiagnosticBlockBase：独立重试/前诊断/后诊断，不污染业务
    }
}
```

节点写入块要点：

* **每块 try/catch 隔离**，失败按块诊断（对齐 `BlockDiagnostic` + 记 `ur_nodes` 内存诊断契约）。

* **条件跳过**落空（如 `CreateSession` 仅在已存在时 skip）用 `ConditionalBlockBase.ShouldExecute`。

* 顺序/production 时序由 `sequenceBlock` 保证，失败不阻断后续（对齐「每阶段含未装配降级」）。

### 4.4 调用逻辑层 —— Spec/端口解析（保持注入，收权给编排）

现状：`_specResolver`/`_runnerFactory` 已在构造注入，但 `SkConversationFoldOperator` 的「执行端选择」在 `ConversationHosting.TryBuildFoldOperator`（[ConversationHosting.cs#L187-L222](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Composition/ConversationHosting.cs#L187-L222)）硬编码 answer/cline/deterministic 三执行端。

改：把「runner 工厂」从「装配期写死」提为**可按 spec 动态装配**端口，并让 Process 阶段经它取执行设置，保持「换 spec 即换主循环」语义。

```csharp
/// 调用逻辑端口：由装配层实现，编排层只见接口
public interface IConversationRunnerPort
{
    Func<AxiomContext, CancellationToken, Task<WorkflowRoundResult>> Resolve(RoundSpec spec);
}

public sealed class FoldRunnerPort : IConversationRunnerPort   // 实现下沉/保留在 Agent.Launcher
{
    public Func<...> Resolve(RoundSpec spec)
        => _foldOperator is not null
             ? FoldConversationRunner.Build(spec, _foldOperator)   // ★循环逻辑：折叠算子进入
             : DiagnosticConversationRunner(spec);                  // 未装配 → 明确诊断（R2，不静默回退语义漂移引擎）
}
```

划分：判定「用哪条 runner」= **调用逻辑**；runner 内部「沿 spec.Steps 折叠推进一步」= **循环逻辑**。

### 4.5 循环逻辑层 —— 折叠推进用 Pipeline 组合子显式化

现状：`SkConversationFoldOperator` 沿 `spec.Steps` 内联推进，`SubAgentCompleted` 触下一步的语义隐藏在算子内部。

改：在循环逻辑层用 Pipeline 组合子把「沿链推进」显式建模（不推翻折叠引擎，只是给外层一个可观测的编排容器）：

```csharp
// 循环逻辑层：spec.Steps 归一化为管线阶段
var chain = new StagePipeline<FoldStepContext>(spec.Name);
foreach (var step in spec.Steps)
    chain.Register(new FoldStepStage(step, operator));       // 每步 = 一个折叠阶段
// 组合：分支(agent 类型) / 循环(多轮 up to MaxRounds) / 异常降级(取消/失败)
var loop = chain
    .Branch(ctl => ctl.AgentKind == AgentKind.Cline, clinePipeline, answerPipeline)
    .Loop(ctl => ctl.Round < ctl.MaxRounds && !ctl.IsComplete)   // ForEach/Loop 显式
    .TryCatch(onCancel: ctl => ctl.Status = Cancelled, onError: ctl => ctl.Status = Faulted);
await loop.ExecuteAsync(stepCtx, ct);
```

完成信封（`FinalizeConversationStateAsync`）保持独立块，但被 Pipeline 的 `Finally/Complete` 阶段收口，取消/异常一律经 `CancellationToken.None` 兜底写回（对齐既有 D1/E1 语义，不重复实现）。

***

## 5. 依赖与装配调整（组合根）

`ConversationHosting.TryAttach`（[ConversationHosting.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Composition/ConversationHosting.cs#L46-L129)）改为**组装四层**：

```
TryAttach:
  1. 装配 portResolver / runnerPort(FoldRunnerPort)     → 调用逻辑层
  2. Build NodeWritePipeline(store,history,relations,linker) → 节点逻辑层
  3. Build FoldRunnerPort / 折叠算子(SkConversationFoldOperator) → 循环逻辑层
  4. Build ConversationCentralPipeline(pipeline)        → 编排层（依赖 1/2/3 端口）
  5. Build ActionDispatcher + AgentUiLibraryConversation  → 前端逻辑层
  → AttachConversation(pipeline 门面) / AttachHistoryRuntime(...)
```

新增项目引用：`Agent.Orchestration`（或下沉编排门面所在层）加 `BasicMethod.Block` 与 `Modules.Pipeline` 两个 ProjectReference（经 `PipelineUsage` Facade 访问，见其 Readme）。

***

## 6. 分阶段实施路线

### P0 —— 先拆节点逻辑到 Block（风险收敛点，纯搬移）

目标：把 `RunSignalAsync` 里六个私有方法抽为 `ExecutionPipeline` 块，行为零变化。

* [ ] 建 `NodeWritePipeline`，封装六个方法为一组 `IBlock`（加 `DiagnosticBlockBase` 前/后诊断，落内存）。

* [ ] `RunSignalAsync` 原位置改调 `NodeWritePipeline.RunAsync(ctx)`，其余逻辑不动。
  验收：`ur_histories`/`ur_nodes`/`cross_db_link` 落库与插入顺序与重构前逐字节等价（行为回归矩阵）。

### P1 —— 引入编排层 CentralPipeline（把 RunSignalAsync 切成生命周期阶段）

目标：四阶段（Initialize/Load/Process/Save/Finalize）显式化，完成信封收口。

* [ ] 建 `ConversationCentralPipeline`（`CentralPipeline<SessionContext>` 生命周期阶段）。

* [ ] 把 spec 断言/seed/运行/extract/map/final 放入各自阶段；用 `PipelineTryCatch`+`WithTimeout` 接管异常/取消信封。

* [ ] `ConversationPipeline` 退化为薄门面，委托 `ConversationCentralPipeline`。
  验收：成功/取消/异常三态下的 Round/Status/审计与 P0 前等价（E1/D1 行为矩阵）。

### P2 —— 前端命令表 + 循环推进组合子化

目标：`action` 分发走 `StrategyRegistry`；spec.Steps 推进走组合子。

* [ ] `ActionDispatcher` 注册各 action strategy，`RunJsonAsync` 删 if/switch。

* [ ] 循环逻辑层用 `StagePipeline`+组合子包装折叠推进（不推倒 `SignalDrivenFoldEngine`，仅给外层可观测容器）。
  验收：action 命中/未命中诊断一致；多轮折叠步数与截断路径与重构前一致（折叠度量回归）。

> 每阶段含「端口未装配降级」验证：Block/Pipeline 端口未注入保持 no-op/空集，零回归（对齐 fail-silent 约定）。

***

## 7. 验收标准（总体）

1. **四层单测可达**：前端（命令表）、节点（Block 序列）、调用（端口解析）、循环（组合子推进）可独立单测，互不依赖实现。
2. **`RunSignalAsync`** **瘦身**：方法体不再直接内联六个节点私有方法与异常三分类；改由 `ConversationCentralPipeline` 编排。
3. **框架无 Agent 命名依赖**：`BasicMethod.Block`/`Modules.Pipeline` 不引用任何 `Agent.*`/`SkillDefinition`（保持纯通用）。
4. **行为回归**：P0–P2 各阶段对 ur\_histories/ur\_nodes/边/状态/完成信封的功能回归全部通过。
5. **零新建重复模块**：复用现有 `ExecutionPipeline`/`CentralPipeline`/组合子，不新建第三套管线框架。

***

## 8. 风险与边界

* **编排过度**：Pipeline 只做**流程控制**，禁止在阶段内写库/调 LLM（动作一律下沉 Block）。否则又造一层「编排+动作」杂糅。

* **折叠引擎不动**：`SignalDrivenFoldEngine` 是既有唯一推进机制，P2 只在**外层**加可观测组合容器，不重构折叠内部。

* **行为对齐线**：P0 是纯搬移必须零变化；P1 完成信封/取消语义（E1/D1）已精确收敛，改动只涉及**承载位置**而非语义。

* **向后兼容**：所有新抽象走「端口未注入降级」守护，未迁移调用方（如测试假 runner 直测编排）无感知。

***

## 附：与既有 8.31 方案的关系

* 本方案（Block/Pipeline 四逻辑分层）与 [Agent通用逻辑框架提取与收敛重构方案.md](./Agent通用逻辑框架提取与收敛重构方案.md)
  （LoopFoldMethod/HierarchyMethod 提取）**互补不冲突**：

  * 既有方案管「**把通用机制上收/下沉**」到 `Operation.LoopFoldMethod/HierarchyMethod`。
    \*本方案管「**在主对话框调用链内部，用 Block/Pipeline 把四类耦合逻辑分层**」。

* 叠加建议：P1 编排层建好后，`ConversationPipeline` 薄门面的「循环驱动」即可对齐既有方案的 P2
  （复用到框架 `SignalDrivenFoldEngine`）作为后续，不在本文档重复。

