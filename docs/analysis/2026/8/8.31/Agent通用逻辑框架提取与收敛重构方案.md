# Agent 通用逻辑框架提取与收敛重构方案

> 目标：把 `Projects/Agent` 中 task/history 的 L0 与 skill 装配逻辑里**通用、可复用、原因散落/重复**的部分，下沉到
> `CsharpPrj\Operation\LoopFoldMethod` 与 `CsharpPrj\Operation\HierarchyMethod` 两个框架项目。
> 最终形态：Agent 成为这两个框架的**薄消费者**（只注入领域语义：skill 来源、L0 承载、决策器），不再自造第二套
> 折叠/装配/编排。
>
> 本文档为优化重构方案（非 PR 记录）：给出问题定位、框架能力盘点、提取映射、目标抽象签名、分阶段路线与验收标准。

***

## 1. 背景与现状

### 1.1 现状：两条并行且各自完整的装配/编排链

当前 Agent 内存在**两套互不归属同一框架的自洽链路**，围绕 item "L0 + skill 装配"与"task/history 编排"：

**路径 A —— 任务主循环（round loop，LLM/cline 子代理）+ provide\_prompt 统一装配**

```
HistoryMainLoop.RunAsync                       Agent.Core/TaskPool/HistoryMainLoop.cs
  → HistoryLoopDecider.NextAsync               (AiSchedulerStateMachine + HistoryDecisionMapper)
  → ITaskOrchestrator                          Agent.Core/TaskPool/TaskOrchestrator.cs
      CreateTaskAsync: EnsureHistoryAsync + AttachHistoryTaskAsync(history→task 边)
  → AgentTaskPool 消费 (WriteStatusAsync/激活)
  → round loop step → provide_prompt
      PromptFrameworkFunction → PromptAssembler.AssembleAsync(ctx, IAssemblySource)
          L0  = PromptCatalog.L0SystemPrompt       (DSL 承载技能 format/l0-* / L0Projector 装配)
          Skill= SkillInjector.Inject              (守卫 + SkillSelector.SelectWithRare)
          L1/L2= source.ResolveL1Async/L2Async     (生成+校验+缓存)
```

**路径 B —— 历史/对话折叠（signal fold loop）+ SK 算子手写分层**

```
ConversationSessionService.SendAsync
  → ConversationPipeline.RunSignalAsync         Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs
      EnsureSessionAsync → LoadLogAsync(ur_histories) → SeedWorkflow
  → FoldConversationRunner.RunAsync → ConversationFoldRunner
  → SkConversationFoldOperator.ExpandAsync      Agent.Core/Signal/SkConversationFoldOperator.cs
      L0  = state.System ?? AgentSystemPrompts.ConversationAssistant   (★与路径 A 不同源)
      Skill= SkillInjector.Inject
      PromptNodeProjector.Build([l0(base), step(dynamic), skill(tail)])  ← 自搭分层
      DispatchAsync → Rare/Mcp+backend/Mcp-executor/Internal 三模块分发
```

### 1.2 零散混乱的具体表现（根因）

1. **L0 两套来源**：路径 A 用 `PromptCatalog.L0SystemPrompt`（DSL 装配产物），路径 B 用
   `state.System ?? ConversationAssistant`。同一 "L0" 概念两种真相，DSL 承载技能（`format/l0-system/l0-loop/l0-logic`）
   在路径 B 缺省不注入。
2. **装配编排双轨**：`PromptAssembler` 声称"单一真相"，但 `SkConversationFoldOperator` 未复用它，用
   `PromptNodeProjector` 自搭 L0→step→skill 三层；两条路径仅在 `SkillInjector` 这一**选择/装配点**上收敛。
3. **task/history 编排重复**：`TaskOrchestrator` 自行实现"history→task→node 归属"适配
   （`IHierarchyReferenceManager`），而 `HierarchyMethod` 已有 `NodeTaskHistoryService` / `IHierarchyRelationManager`；
   `HistoryMainLoop` 的批量编排与 `HierarchyMethod.RelatedNamespace.HistoryRuntimeService` 能力重叠。
4. **任务状态机两套**：Agent `AgentTaskItem` 的激活/暂停/恢复/终止状态逻辑，与 `HierarchyMethod.TaskRecord` +
   `ExecutionFlagService` + `AgentLoopState` 执行状态机重复。
5. **折叠驱动重复**：`ConversationFoldRunner`（Agent 侧）内联驱动，而 `LoopFoldMethod.Signal.SignalDrivenFoldEngine`
   已是唯一推进机制。

> 结论：**通用机制大多已存在**于两个框架，Agent 侧主要问题是"重复实现"与"两路径未收敛"。提取工作 = 去重 + 上收
> 已有框架能力 + 补齐唯一缺失的"装配编排"框架抽象。

***

## 2. 目标架构

```
                     ┌───────────────────────────────────────────┐
                     │            Agent（薄消费者）               │
                     │  领域语义：SkillDefinition 源 / L0 DSL 承载  │
                     │  / 决策器(IHistoryLoopDecider) / 执行端    │
                     └──────────────┬────────────────────────────┘
                                    │ 只依赖抽象端口
        ┌───────────────────────────┴───────────────────────────┐
        │                                                       │
┌───────▼─────────────┐                         ┌───────────────▼────────┐
│  LoopFoldMethod     │                         │     HierarchyMethod     │
│  折叠/信号/节点装配   │                         │  三层归属/任务状态/编排    │
│  Node/* Signal/*    │                         │  History/* Reference/*  │
│  Fold/* Operator/*  │                         │  Nodes/* Tasks/*        │
└─────────────────────┘                         └─────────────────────────┘
   新增：统一提示词带装配抽象 + L0 装配源端口             新增：history-task 编排门面（若非侵入）+ 决策端口驻留
```

设计原则（对齐工程约定）：

* 框架只承载**领域无关的通用机制**，不引入 Agent 命名与 skill/prompt 语义。

* Agent 经**端口/接口**消费框架；未装配降级 no-op/空集（对齐"可空守卫"约定）。

* **不新建重复模块**，优先增强/上收已有环。

***

## 3. 框架能力盘点（已有内容）

### 3.1 LoopFoldMethod（折叠/信号/节点装配）

| 目录              | 关键类型                                                                                                                         | 已提供通用机制                               |
| --------------- | ---------------------------------------------------------------------------------------------------------------------------- | ------------------------------------- |
| `Node/Model`    | `NodeItem`, `NodeCategory`, `NodeSlot`                                                                                       | 通用动态节点模型（Category/Order/Payload/Slot） |
| `Node/Assemble` | `NodeBuilder`, `CacheStableSlotPolicy`, `INodeSlotPolicy`, `NodeAssembly`                                                    | 按槽位分 Head/Body/Tail 并稳定排序的通用排布        |
| `Node/Source`   | `INodeSource`, `AggregateNodeSource`                                                                                         | 节点收集与聚合入口                             |
| `Node/Project`  | `INodeProjector<T>`                                                                                                          | 节点装配 → 领域产物投影接口                       |
| `Signal`        | `SignalDrivenFoldEngine`, `ChainSignalResolver`, `RuleSignalResolver`, `ISignalDrivenOperator`, `FoldSignal`, `FoldDecision` | 信号驱动折叠唯一推进机制 + 声明式链/规则信号解析            |
| `Fold`          | `FoldEngine`, `FoldResult`, `FoldCut`, `FoldBudget`                                                                          | 状态带展开/截断/收敛                           |
| `Operator`      | `IFoldOperator`, `FoldContext`, `FoldStep`                                                                                   | 外部算子契约（Agent/规则/人审皆可实现）               |

### 3.2 HierarchyMethod（三层归属/任务状态/编排）

| 目录                | 关键类型                                                                            | 已提供通用机制                                                    |
| ----------------- | ------------------------------------------------------------------------------- | ---------------------------------------------------------- |
| `History`         | `IHistoryRelationManager`, `HistoryRelationManager`, `HistoryProjector`         | history/task/node 三层归属、时序边、历史重建                            |
| `History/Runtime` | `HistoryRuntimeService`, `IHistoryRuntimePort`                                  | history 持有 task 的批量展开、运行态聚合、start/pause/resume/cancel/step |
| `Reference`       | `NodeTaskHistoryService`, `EntityKind`, `ExecutionFlagService`, `FlagConductor` | 三层归属统一写入口 + 执行标识符状态机                                       |
| `Nodes/Execution` | `NodeGraphOrchestrator`                                                         | 按 RoundSpec 顺序执行/横切持久化/终答归集                                |
| `Nodes/Workflows` | `WorkflowSpecLoader`, `RoundSpec`                                               | 声明式工作流 → 线性 RoundSpec                                      |
| `Nodes/TaskGraph` | `TaskGraphService`, `ITaskGraphTaskStore`                                       | 建 task+关联节点+引用边+入队  复合服务                                   |
| `Tasks`           | `TaskRecord`, `TaskScope`, `ExecutionNode`, `Loops/AgentLoopState`              | 任务簿记 + 状态机运行时容器                                            |

***

## 4. 提取映射表（Agent 散落逻辑 → 目标归属）

处置约定：**上收**= 已有框架机制，Agent 改为消费并删除重复实现；**下沉**= 新增框架抽象，Agent 移入；
**保留**= 领域相关、留在 Agent。

| # | Agent 侧逻辑（现状文件）                                                                                                                         | 框架已有 / 待建                                                                                                    | 处置                                                                       |
| - | --------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------ |
| 1 | 分层投影 `PromptNodeProjector.Build`（[Agent.Core/Prompts/PromptNodeProjector.cs](../src/Agent.Core/Prompts/PromptNodeProjector.cs)）         | `LoopFoldMethod.Node/*` 已有 NodeBuilder/NodeProjector                                                         | **上收**：两路径统一走框架嵌套，删除"路径 B 自建分层"的歧义                                       |
| 2 | L0 单一来源 `PromptCatalog.L0SystemPrompt`（[Agent.Axioms/Prompts/PromptCatalog.cs](../src/Agent.Axioms/Prompts/PromptCatalog.cs)）           | 框架无 L0 装配源概念                                                                                                 | **下沉**：新增框架 `PromptBandSource` 端口，路径 B 经端口读同一 L0                         |
| 3 | 装配编排 `PromptAssembler`（L0→Skill→L1→L2）([Agent.Assembly/Assembly/PromptAssembler.cs](../src/Agent.Assembly/Assembly/PromptAssembler.cs)） | 部分待建                                                                                                         | **下沉**：把"带装配 → 分层 prompt"协议沉入 `LoopFoldMethod.Node`；L1/L2 生成保持 Agent（领域） |
| 4 | skill 选择/注入 `SkillInjector/SkillSelector/SkillPromptAssembler`（`Agent.Skill`）                                                           | 无（强依赖 SkillDefinition）                                                                                       | **保留**：属 Agent 领域；框架只暴露"技能块装配"端口                                         |
| 5 | history→task→node 归属 `TaskOrchestrator.CreateTaskAsync`（[TaskOrchestrator.cs](../src/Agent.Core/TaskPool/TaskOrchestrator.cs)）          | `HierarchyMethod.Reference.NodeTaskHistoryService.EnsureTaskHistoryBoundAsync` + `IHierarchyRelationManager` | **上收**：删除 Agent 侧 `IHierarchyReferenceManager` 适配，直连框架归属门面               |
| 6 | 任务状态机（激活/暂停/恢复/终止落库）`AgentTaskPool`/`AgentTaskItem`                                                                                     | `HierarchyMethod.TaskRecord` + `ExecutionFlagService` + `AgentLoopState`                                     | **上收**：状态跃迁对齐执行标志/任务状态机，落库职责归框架                                          |
| 7 | 历史主循环编排 `HistoryMainLoop.RunAsync`（[HistoryMainLoop.cs](../src/Agent.Core/TaskPool/HistoryMainLoop.cs)）                                 | `HierarchyMethod.HistoryRuntimeService` + `HistoryRuntimePort`                                               | **上收**：循环驱动归框架；Agent 仅注入决策端口 `IHistoryLoopDecider`                       |
| 8 | 对话折叠驱动 `ConversationFoldRunner`（[Agent.Core/Signal](../src/Agent.Core/Signal/ConversationFoldRunner.cs)）                                | `LoopFoldMethod.Signal.SignalDrivenFoldEngine`                                                               | **上收**：复用框架引擎，删除内联驱动                                                     |

> 说明 #1/#7/#8 本质是"**已有机制、用法重复**"，非新增抽象；提取的重点与风险都在 #2/#3（装配编排）与
> \#5/#6（归属+状态）的**入口统一**上。

***

## 5. 目标框架新增 / 调整抽象（接口签名）

### 5.1 LoopFoldMethod —— 统一提示词带装配（新增，吸收 #2/#3）

**5.1.1 提示词带装配引擎**（把 `NodeBuilder + INodeProjector` 封装为领域无关的"带→分层"装配）

```csharp
// LoopFoldMethod.Node — 提示词带装配（泛化 PromptNodeProjector，去 Agent 依赖）
namespace Operation.LoopFoldMethod.Node.Prompt;

public enum PromptBand { Base, Step, Skill }        // 稳定/动态/技能

public interface IPromptBandBlock                       // 一个 word band 的数据源
{
    PromptBand Kind { get; }
    int Order { get; }
    object? Payload { get; }                             // 不解释语义，可空（空则剔除）
}

public interface IPromptBandSource                       // 装配源端口（Agent 实现）
{
    IEnumerable<IPromptBandBlock> Collect(PromptBandContext ctx, string stepKey);
}

public readonly record struct PromptBandContext(string SystemL0, string? ConversationContext);

public sealed class PromptAssembler                      // 引擎（引用 NodeBuilder/CacheStableSlotPolicy）
{
    public PromptLayeredResult Assemble(IPromptBandSource source, PromptBandContext ctx, string stepKey);
}
```

要点：引擎只排序/分层（复用 `CacheStableSlotPolicy`：Base→头、Skill→尾、Step→中），**不**碰 L0 怎么来、
skill 怎么选——那由 Agent 的 `IPromptBandSource` 注入。两路径改为同一引擎装配，消除 L0 双源。

**5.1.2 L0 装配源端口**（吸收 #2）

```csharp
public interface IL0AssemblySource                            // 唯一 L0 真相来源
{
    string L0SystemPrompt { get; }                            // 稳定底座文本
}
```

Agent 默认实现 = `PromptCatalog.L0SystemPrompt`；路径 B 折叠算子与路径 A 装配均读此端口，不再各自兜底。
（`AgentSystemPrompts.ConversationAssistant` 仅作端口未装配时的空载降级。）

### 5.2 HierarchyMethod —— history-task 编排门面（上收 #5/#6/#7）

**5.2.1 复用既有归属门面**（不再新增，Agent 侧 `TaskOrchestrator` 直连）

```csharp
// 已有：HierarchyMethod.Reference.NodeTaskHistoryService
EnsureTaskHistoryBoundAsync(historyId, taskKey);
AttachNodeToTaskAsync(taskKey, workflowId, nodeKey);
```

处置：删除 Agent `IHierarchyReferenceManager` 适配层，改为注入 `NodeTaskHistoryService`。

**5.2.2 历史主循环决策端口**（Agent 注入，框架驱动；#7）

```csharp
namespace Operation.HierarchyMethod.History.Runtime;
public interface IHistoryLoopDecider
{
    Task<TaskOrchestrationRequest?> NextAsync(object historyView, int enacted, CancellationToken ct);
    void OnEnacted(TaskOrchestrationRequest request);          // 落地后同步调度影子
}
```

`HistoryRuntimeService` 承担循环体（已有批量展开/控制），Agent 仅提供"下一步"决策实现。
`TaskOrchestrationRequest/TaskOrchestrationAction` 下沉至框架（对齐 `HierarchyMethod` 语义），Agent 删除同名重复定义。

**5.2.3 任务状态跃迁契约**（#6；对齐现有 `ExecutionFlag`/`TaskRecord`）

```csharp
public interface ITaskLifecyclePort        // 迁移为 HierarchyMethod 契约（原在 Agent.Core/TaskPool）
{
    Task CreateTaskAsync(string taskKey, string? input, ct = default);
    Task ActivateTaskAsync(...);   Task PauseTaskAsync(...);
    Task ResumeTaskAsync(...);     Task TerminateTaskAsync(...);
}
```

实现下沉为框架对 `TaskRecord` + `ExecutionFlagService` 的封装；Agent 停止 `AgentTaskItem` 独立状态机。

### 5.3 保留在 Agent 的领域实现（不强求下沉）

* `SkillInjector/SkillSelector/SkillPromptAssembler`（依赖 `SkillDefinition`，属 Agent 领域）。

* L1/L2 生成器 `PromptLlmGenerator` + `PromptValidator`（依赖 LLM/校验，领域相关）。

* 折叠算子 `SkConversationFoldOperator`（实现 `ISignalDrivenOperator`，是 Agent 对框架算子的实现）。

* 执行端 `AnswerFoldExecutor / ClineFoldExecutor / TaskGraphFoldExecutor`。

> 提取边界判断准则：*能否不引用任何 Agent 命名（Agent.* / SkillDefinition / prompt 语义）而描述其行为？\*
> 能 → 下沉/上收到框架；不能 → 保留为领域实现。

***

## 6. 分阶段实施路线

### P0 —— 收敛装配编排（风险收敛点）

目标：消除 L0 双源 + 装配双轨；引入 `IPromptBandSource` 与引擎；两路径改为同一装配引擎。

* [ ] 在 `LoopFoldMethod.Node.Prompt` 新增 `PromptAssembler`（复用 `NodeBuilder/CacheStableSlotPolicy`）。

* [ ] 新增 `IL0AssemblySource`；Agent 默认实现返回 `PromptCatalog.L0SystemPrompt`。

* [ ] 改造路径 B `SkConversationFoldOperator.ExpandAsync`：L0 改读 `IL0AssemblySource`，分层改走框架引擎。

* [ ] 改造路径 A `PromptAssembler`（Agent.Assembly）：带装配部分委托框架引擎，L1/L2 保持生成。
  验收：两路径在**同一 L0 文本、同一分层排布**下，用固定输入对比 output 快照一致（基准回归）。

### P1 —— 上收归属与状态机

目标：Agent 停止重复实现三层归属与任务状态机。

* [ ] `TaskOrchestrator` 注入 `NodeTaskHistoryService` / `IHierarchyRelationManager`，删除 `IHierarchyReferenceManager` 适配。

* [ ] `TaskOrchestrationRequest/Action` 下沉框架；Agent 删同名定义。

* [ ] `ITaskLifecyclePort` 迁移为框架契约，实现落 `TaskRecord + ExecutionFlagService`；`AgentTaskItem` 状态机改为其薄投影。
  验收：task 创建/激活/暂停/恢复/终止的 `ur_tasks` 状态变化与 P0 前逐项等价（行为回归矩阵）。

### P2 —— 上收循环驱动

目标：历史主循环与对话折叠统一由框架引擎驱动。

* [ ] `HistoryMainLoop` 改为驱动 `HierarchyMethod.HistoryRuntimeService`，Agent 仅注入 `IHistoryLoopDecider`。

* [ ] `ConversationFoldRunner` 复用到框架 `SignalDrivenFoldEngine`，删除 Agent 内联驱动。
  验收：多 task 编排与对话折叠端到端行为回归 + 两路径共用引擎度量（折叠步数、截断路径一致）。

> 每阶段含"未装配降级"验证：框架端口未注入时 Agent 保持可空载运行（no-op/空集），零回归。

***

## 7. 验收标准（总体）

1. **单一真相**：全仓只存在一个 `L0SystemPrompt` 来源、一套"带→分层"装配引擎、一套三层归属门面。
2. **路径收敛**：`grep SkillInjector` 只有一个装配入口且两路径共用；`SkConversationFoldOperator` 不再自build分层。
3. **框架无 Agent 依赖**：两个 Operation 框架项目不引用任何 `Agent.*` / `SkillDefinition` / prompt 语义命名。
4. **行为回归**：P0–P2 各阶段对既有 task/history/L0/skill 交互的功能回归全部通过。
5. **零重复**：`IHierarchyReferenceManager`、`TaskOrchestrationRequest`、Agent 侧折叠驱动、独立任务状态机均被删除
   且无替代复制。

***

## 8. 风险与边界

* **循环依赖**：框架不得反向引用 Agent。凡触碰 `SkillDefinition/prompt` 的都取"端口注入"，框架侧只见接口。

* **领域语义外泄**：`NodeCategory.Skill` 等枚举保留通用语义，具体 skill 匹配逻辑留在 Agent 注入实现。

* **行为漂移风险主区**：路径 B 从 `ConversationAssistant` 切到 `PromptCatalog.L0SystemPrompt` 属**语义变更**（非纯重构），
  需在 P0 显式决策"行为对齐线"（默认对齐路径 A 的 DSL L0）。

* **向后兼容**：所有新抽象经"端口未装配降级"守护，未迁移调用方无感知。

***

## 附：原始调研文件索引（去重后的保留面）

* Agent 侧（保留/领域）：`Agent.Modules/Skill/*`、`Agent.Axioms/Prompts/PromptLlmGenerator*`、`Agent.Core/Signal/SkConversationFoldOperator.cs`、
  `Agent.Modules/Signal/AgentFoldExecutors.cs`

* 框架侧（消费/实现下沉目标）：`CsharpPrj/Operation/LoopFoldMethod/Node/*`、`Signal/*`、`Fold/*`；
  `CsharpPrj/Operation/HierarchyMethod/{History,Reference,Nodes,Tasks}/*`

