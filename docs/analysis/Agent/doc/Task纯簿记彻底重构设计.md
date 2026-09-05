# Task 纯簿记 · 彻底重构设计文档

> **版本**：1.0
> **日期**：2026-08-25
> **状态**：设计中（Phase 1 + 动作层默认零 LLM 已落地并编译/测试验证；`TaskRecord`/`ExecutionNode`/`AgentLoopState` 实体已迁入 `Operation.HierarchyMethod.Tasks`（见 `nodes-task-history三层通用解耦设计.md` 附录 B，全局别名桥接）；Phase 2–6 为待实施的深层迁移蓝图）
> **目标**：把 `task` 从「任务 + 执行策略（LLM）+ 循环快照」三重融合实体，彻底重构为**纯任务簿记**，使 task 层完全不感知执行器 / LLM / 循环。
> **范围**：`Agent.Contract / Agent.Core / Agent.State / Agent.Operation / Agent.Modules / Agent.Launcher / Share / Operation.DataStoreDriven` 跨层（实体权威已移至 `CsharpPrj/Operation/HierarchyMethod`）。

---

## 〇、一句话结论

> **task（`TaskRecord`）= 纯簿记**（入队/状态/删除/提交 + 幂等 + 优先级/重试 + 引用边），**LLM/执行相关逻辑封装为执行节点（`ExecutionNode`），由 task 作为属性直接携带**（相当于任务的一个属性），**循环快照根（`AgentLoopState`）= 运行时确定性核心**。task 不亲自调用 LLM——节点由执行器在消费时解析执行；LLM 细节经节点 `WorkflowConfigKey` 外置提示词。

---

## 一、背景与问题

### 1.1 现状：`AgentTaskItem` 一个类承担三个角色

`AgentTaskItem`（`Agent.Core/TaskPool/AgentTaskItem.cs`）同时实现
`ITaskPoolItem + ILoopState + IAgentLoopState + IStateFacet + IStatusAwareTaskPoolItem + IConversationEntry`，
并把三个互不相干的关切融合在**单条 `ur_tasks.PayloadJson`**（`AgentTaskItemJsonConverter` 整份序列化）里：

| 角色 | 字段 | 归属 |
|------|------|------|
| **R1 任务簿记** | `TaskKey/Status/Priority/MaxRetries/HistoryId/ThreadId/InputKey/UserInput/RelatedNodes` | task 动作层唯一该碰的 |
| **R2 执行策略（LLM 知识）** | `SchedulingMode`(默认 Hybrid=LLM)、`ExecutorId`、`AnswerSpec`(roundPromptPlan/systemPrompt/refineInstruction/stopRuleSetKey)、`SchedulerRulesKey`、`CurrentStrategyId/AvailableStrategyIds/AgentStatus/ExplicitExecutor` | **LLM 提示词/导演模式/精化指令直接内嵌在任务里** ← 本次剥离核心 |
| **R3 循环快照根** | `Budget/Token/Flag/Round/LastEval/DataSpace`、`Scope/ContinuousFailures/RecentTransitions` | 确定性核心（`SEP.DeterministicCoreDisjoint`），本应独立 |

### 1.2 问题

1. **task 动作层夹带 LLM**：`AgentUiLibraryTaskPool.TaskPoolEnqueueAsync` 直接组装 `AnswerSpec`（提示词计划/系统提示/精化指令），`schedulingMode` 默认 `Hybrid`（LLM 导演）。
2. **task 公开契约混入执行细节**：`task/*.json` 模板直接暴露 `executorId`/`roundPromptPlan` 等 LLM 概念。
3. **默认即 LLM**：普通入队（未指定执行策略）默认走 LLM 导演，违反"task=纯簿记"。
4. **持久化里含 LLM**：`ur_tasks.PayloadJson` 持久化整份 `AgentTaskItem`（含提示词），任务与 LLM 配置同根。

### 1.3 目标

> **task = 纯任务逻辑操作**：入队/状态/删除/提交/回填，只关心"任务簿记"。
> **执行 = 节点，由 task 作为属性携带**：是否调 LLM / 调哪个 / 用什么提示词 / 精化几轮，封装在执行节点（`ExecutionNode`）内；task 携带该节点但调用发生在执行器。
> **循环快照 = 运行时确定性核心**：`AgentLoopState`，任务/执行解耦后的独立运行状态。

---

## 二、目标架构

```
┌─ task 层（纯簿记，L1 控制流，不感知执行器/LLM）─────────────────────┐
│  TaskRecord (ur_tasks 载荷)    TaskKey/Status/Priority/MaxRetries/ │
│                                HistoryId/ThreadId/InputKey/       │
│                                UserInput/RelatedNodes/CreatedAt/   │
│                                UpdatedAt + ExecutionNode(属性)      │
│  操作: 入队/状态/删除/提交/回填 = 纯簿记 (AgentTaskPoolAdapter)       │
└───────────────┬───────────────────────────────────────────────────┘
                │ 消费时: TaskRecord.ExecutionNode → 解析执行器 + 派生 AgentLoopState
                ▼
┌─ 执行节点层（LLM 逻辑所在, 由 task 携带, L2/L3）───────────────────┐
│  ExecutionNode (task 属性) ExecutorId(句柄)/DirectorMode(可空)/     │
│                        RulesKey/WorkflowConfigKey/Payload(不透明)  │
│  ExecutorRegistry + IBackendRouter(端口)                           │
│  执行器: LlmTaskExecutor / RuleTaskExecutor / StatelessAnswer...   │
│  提示词/轮数/停止规则 → Config/workflows/*.json / ur_config(按 key)  │
└───────────────┬───────────────────────────────────────────────────┘
                ▼
┌─ 循环快照根（确定性核心, 每轮派生租约）───────────────────────────┐
│  AgentLoopState (IAgentLoopState) Budget/Token/Flag/Round/LastEval/│
│                DataSpace + TaskKey/UserInput/ThreadId/InputKey     │
│  SnapshotDrivenLoopEngine + RoundLoopDriver + AgentRunner          │
└────────────────────────────────────────────────────────────────────┘
```

**单向依赖**：`TaskRecord`（簿记）**携带** `ExecutionNode`（执行节点，task 的一个属性）；消费时 `ExecutionNode` → 解析执行器 → 派生 `AgentLoopState`（循环快照根）运行。task 不亲自调用 LLM（调用发生在执行器）。

---

## 三、新实体设计（Agent.Contract）

### 3.1 `TaskRecord` — 纯簿记任务（ur_tasks 载荷）
路径：`Agent.Contract/Tasks/TaskRecord.cs`（**已创建**）
实现：`ITaskPoolItem` + `IStatusAwareTaskPoolItem`
字段：`TaskKey/Status/Priority/MaxRetries/HistoryId/ThreadId/InputKey/UserInput/RelatedNodes/CreatedAt/UpdatedAt` + **`ExecutionNode?`（执行节点属性）**
**约束**：不含任何执行器 / LLM / 循环字段；`ExecutionNode` 为 task 携带的执行节点属性（task 不调用 LLM，节点由执行器消费时解析执行）。

### 3.2 `ExecutionNode` — 执行节点（LLM 逻辑封装，task 携带）
路径：`Agent.Contract/Tasks/ExecutionNode.cs`（**已创建**，替代原 `TaskExecutionSpec`）
字段：`ExecutorId?/DirectorMode?(SchedulingMode?)/RulesKey?/WorkflowConfigKey?/Payload?`
**语义**：把"该任务如何执行 / 是否调 LLM / 用什么提示词 / 精化几轮"封装为自包含节点，由 `TaskRecord` 作为**属性**直接携带（相当于任务的一个属性，随 ur_tasks 持久化）。提示词/轮数/停止规则经 `WorkflowConfigKey` 引用外部工作流/配置（`Config/workflows/*.json`、ur_config），节点保持轻量、可序列化。

### 3.3 `AgentLoopState` — 循环快照根
路径：`Agent.Contract/Runtime/AgentLoopState.cs`（**已创建**）
实现：`IAgentLoopState`
字段：`TaskKey/UserInput/ThreadId/InputKey` + 确定性核心 `Budget/Token/Flag/Round/LastEval/DataSpace` + Agent 扩展 `Status/Scope/ContinuousFailures/CurrentStrategyId/AvailableStrategyIds/AgentStatus/RecentTransitions`
工厂：`AgentLoopState.FromTaskRecord(TaskRecord)`（投影 UserInput/ThreadId/InputKey/TaskKey/Status，确定性核心从零起）。

### 3.4 契约补充
`IAgentLoopState` 需补 `string TaskKey { get; }`（供持久化/取消/暂停定位；`AgentTaskItem` 与 `AgentLoopState` 均已具备，低风险）。

---

## 四、分阶段实施计划

### Phase 1 — 契约层新增实体（✅ 已完成并验证）
- 新增 `TaskRecord`（含 `ExecutionNode` 属性）/ `ExecutionNode` / `AgentLoopState`。
- 验证：`Agent.Contract` 编译 **0 警告 0 错误**。

### Phase 1.5 — 循环租约派生泛化（✅ 已完成并验证）
- `LeaseContextFactory.Create(IConversationEntry)` → `Create(IAgentLoopState)`（`AgentTaskItem` 与 `AgentLoopState` 均可用作循环根）。
- 验证：`Agent.Core` + `Agent.Orchestration` 编译 **0/0**。

### Phase 1.6 — task 动作层默认零 LLM（✅ 已完成并验证）
| 文件 | 改动 |
|------|------|
| `Agent.Launcher/Modules/AgentUiLibraryTaskPool.cs` | `TaskPoolEnqueueAsync` 默认 `schedulingMode` `"Hybrid"→"Deterministic"`；回退 `?? Hybrid → ?? Deterministic` |
| `Agent.Core/TaskPool/AgentTaskPool.cs` | `WriteStatusAsync` 新建任务默认 `Hybrid → Deterministic` |
| `DrivenConfig/Templates/AgentRun/task/README.md` | `mode` 默认 `Hybrid → Deterministic`，标注"task 默认不调用 LLM" |

- 验证：`Agent.Launcher` 编译 **0/0**；任务池/路由相关测试 **23/23 通过**。

### Phase 2 — 循环 TState 切换到 `AgentLoopState`
| 步骤 | 文件 | 改动 |
|------|------|------|
| 2a | `Agent.Contract/Runtime/IAgentLoopState.cs` | 补 `string TaskKey { get; }` |
| 2b | `Agent.Core/Loops/RoundLoopDriver.cs` | `RunSingleRootAsync` / `CreateLeaseContext` / `HarvestDeterministicCore` 的 TState 从 `AgentTaskItem` 泛化到 `IAgentLoopState` |
| 2c | `Agent.Core/Loops/AgentRunner.cs` | 4 个 `RunAsync` 重载 + `RunInternalAsync/RunMainPipelineAsync/NextHybridRoundAsync/PersistRuntimeAsync` TState 改 `AgentLoopState`；`SchedulingMode/RulesKey/ExecutorId` 改从 `ExecutionNode` 实参读取（不再读 task item） |
| 2d | `Agent.Core/Orchestration/AgentOrchestrator.cs` | `RunAsync(item,…)` → `RunAsync(AgentLoopState, ExecutionNode?, …)` |
| 2e | 三个执行器 | `ITaskExecutor.ExecuteAsync(AgentLoopState, ExecutionNode?, taskKey, ct)`（节点由 `TaskRecord.ExecutionNode` 透传） |

### Phase 3 — 持久化分拆 + 迁移
| 步骤 | 文件 | 改动 |
|------|------|------|
| 3a | `SqliteTaskPoolStore<TItem>` / `ITaskPoolPersistence<TItem>` | 泛型参数 `AgentTaskItem` → `TaskRecord`；ur_tasks 存纯簿记 |
| 3b | 循环快照落点 | `AgentLoopState` 每轮边界独立落点（按 TaskKey 单列/独立表），与簿记解耦 |
| 3c | `AgentTaskItemJsonConverter` | 拆分 `TaskRecord` + `AgentLoopState` 两序列化器；旧格式兼容读取 + 一次性迁移 |
| 3d | `WriteThroughTaskPoolStore` / `AgentTaskGraphStoreAdapter` / `TaskGraphService` | 消费端改 `TaskRecord` |


### Phase 4 — 路由与默认值彻底去 LLM 化
| 文件 | 改动 |
|------|------|
| `Agent.Core/TaskPool/AgentRuleBackend.cs` | 从读 `AgentTaskItem.SchedulingMode` → 读 `ExecutionNode.DirectorMode`；默认（无节点）→ 零 LLM |
| `Agent.Core/TaskPool/AgentTaskPool.cs` `Resolve` | 外部注入选择器 `Func<TaskRecord, ExecutionNode?, ITaskExecutor?>`（节点 = `TaskRecord.ExecutionNode`） |
| `SchedulingModeAutonomy.cs` | 保留（供 `DirectorMode` 派生），默认不再倾向 Hybrid |

### Phase 5 — action 层、模板、clinerules 对齐
| 位置 | 改动 |
|------|------|
| `AgentUiLibraryTaskPool.TaskPoolEnqueueAsync` | 只构造 `TaskRecord` + 可选 `ExecutionNode`（执行节点属性）；删除 `AnswerSpec/roundPromptPlan/systemPrompt/refineInstruction` 组装与 `using Agent.Contract.Answer`；relatedNodes 引用边维护保留 |
| `task/*.json` + `README.md` | 收敛为纯簿记字段；`answer.json` 的 `roundPromptPlan` → `configKey` 引用执行器配置 |
| `.clinerules/formula.md` `Pattern.TaskPoolAgent` | 对齐为"task=纯簿记（L1）+ 执行策略（L2/L3）" |

### Phase 6 — 测试与文档收尾
- 迁移 `Agent.Core.Tests` / `StateManagement.Tests` 中引用 `AgentTaskItem` 融合字段的用例（`TaskRouterTests/AgentRuleBackendTests/RuleTaskExecutorTests/TaskPoolParallelTests/SnapshotSerializerTests` 等）。
- 更新 `docs/analysis/Agent/*` 链路文档与 `Agent/README.md` 数据流章节。
- 回归：`dotnet build Agent.sln` 0 错误 + 全测试绿 + 手动三场景（纯 enqueue 零 LLM / stateless-answer 走 LLM / Hybrid 显式走 LLM）。

---

## 五、影响面与消费方清单

`AgentTaskItem` 各融合字段的 C# 消费方（Phase 2–5 需逐一改参）：

| 消费方 | 位置 | 相关字段 |
|--------|------|----------|
| `AgentRunner` | `Agent.Core/Loops/` | `SchedulingMode/SchedulerRulesKey/ExecutorId/AnswerSpec/CurrentStrategyId/Data(TaskKey)` |
| `AgentOrchestrator` | `Agent.Core/Orchestration/` | `RunAsync(item, …)` 全参 |
| `RoundLoopDriver` | `Agent.Core/Loops/` | TState + `CreateLeaseContext/HarvestDeterministicCore` |
| `AgentTaskPoolAdapter` | `Agent.Core/TaskPool/` | `TaskPool<AgentTaskItem,…>` + `Resolve` + `IConversationStore` |
| `LlmTaskExecutor/RuleTaskExecutor` | `Agent.Core/TaskPool/` | `SchedulingMode/SchedulerRulesKey` |
| `StatelessAnswerTaskExecutor` | `Agent.Modules/Answer/` | `AnswerSpec/RelatedNodes`（提示词消费点） |
| `AgentRuleBackend` | `Agent.Core/TaskPool/` | `SchedulingMode` |
| `SchedulingModeAutonomy/TaskRouting` | `Agent.Operation/Routing/` | `SchedulingMode` |
| `SqliteTaskPoolStore` | `Operation.DataStoreDriven/Sqlite/` | `TItem=AgentTaskItem` 序列化 |
| `AgentTaskItemJsonConverter` | `Agent.Core/TaskPool/` | 整份 PayloadJson |
| `WriteThroughTaskPoolStore`/`AgentTaskGraphStoreAdapter` | `Agent.Core/TaskPool/`、`Agent.Reference/TaskGraph/` | `taskKey/status` 关系 |
| 会话编排 `ConversationPipeline` | `Agent.Orchestration/SessionWorkflow/` | `IConversationEntry`(即 AgentTaskItem) |

---

## 六、风险与缓解

| 风险 | 缓解 |
|------|------|
| 泛型 TState 切换牵动 `AgentRunner/AgentOrchestrator/全部执行器` | Phase 2 先泛化 `RoundLoopDriver`/`LeaseContextFactory`（已验证安全）再改 Runner，每步独立编译 |
| 持久化格式变更破坏存量库 | Phase 3 旧格式兼容读取 + 一次性迁移；`SqliteTaskPoolStoreTests` 覆盖重启回填 |
| 违反既有"任务=快照"设计原则（`AX.StateRootSingleSource`） | 明确三根单向引用：`TaskRecord`=持久簿记（携带 `ExecutionNode` 属性）、`AgentLoopState`=循环快照根（唯一 `ILoopState`）、`ExecutionNode`=task 携带的执行节点；更新 clinerules 对齐 |
| `IConversationStore` 契约耦合 `AgentTaskItem` | Phase 2d/3 同步泛化 `IConversationEntry`/`IConversationStore`，或降级为只读簿记适配 |
| 默认值改动影响存量测试 | 既有模板均已显式指定 `mode`（`Config/Hybrid/Deterministic`），动作层默认改动仅影响未指定 mode 的调用，低风险（已 23/23 验证） |

---

## 七、验证方式

1. **编译**：`dotnet build Agent.sln -c Debug` → 0 警告 0 错误。
2. **单测**：`Agent.Core.Tests` / `StateManagement.Tests`（含 `SqliteTaskPoolStoreTests` / `TaskRouterTests` / `AgentRuleBackendTests` / `RuleTaskExecutorTests` / `TaskPoolParallelTests` / `SnapshotSerializerTests`）全绿。
3. **手动三场景**：
   - 纯 `task enqueue`（无执行策略）→ **不调用 LLM**（零 LLM 规则 / 纯簿记完成）；
   - `executorId=stateless-answer` → 走 LLM（answer 显式循环）；
   - `schedulingMode=Hybrid` → 显式走 LLM（LlmStateDirector）。
4. **模板回归**：`db_localdb_answer_test.json` 等自检脚本按新契约调整后跑通。

---

## 八、已落地提交点（当前状态）

| 提交点 | 内容 | 验证 |
|--------|------|------|
| P1 | 实体（`TaskRecord` 含 `ExecutionNode` 属性 / `ExecutionNode` / `AgentLoopState`） | `Agent.Contract` 0/0 |
| P1.5 | `LeaseContextFactory` 泛化 `IAgentLoopState` | `Agent.Core`+`Orchestration` 0/0 |
| P1.6 | task 动作默认零 LLM（`Hybrid→Deterministic`）+ 模板/README | `Agent.Launcher` 0/0；测试 23/23 |

> **待办**：Phase 2–6（深层迁移，见 §四），为一次性不可安全验证的架构逆转，须分阶段逐块构建/测试推进。


---

## 九、扩展：nodes / task / history 三层全部 LLM 解耦，提取通用逻辑到 `HierarchyMethod`

> 本层为总览层；task 层细化见 §一–§八。目标：把 **nodes / task / history 三层**都彻底与 LLM 解耦，作为**通用逻辑提取到 `CsharpPrj/Operation/HierarchyMethod`**（Operation 通用基础层项目，当前为空占位、需新建 `HierarchyMethod.Nodes/Tasks/History` 三子命名空间），Agent 层（`Projects/Agent/src`）只保留 LLM 应用与组合。

### 9.1 现状三层盘点（通用机制 vs Agent 专属 / LLM 融合）

| 层 | ✅ 已在 `CsharpPrj`（通用、LLM-free） | ⚠️ 仍在 `Projects/Agent/src`（含 LLM 融合） |
|----|----|----|
| **task** | `Share/TaskStatusPool`（泛型任务状态机）、`Operation/SqliteTaskPoolStore`、`Operation/WriteThroughTaskPoolStore` | `Agent.Core/TaskPool/AgentTaskItem`（**融合 LLM**）、`AgentTaskPoolAdapter`、`LlmTaskExecutor/RuleTaskExecutor/StatelessAnswerTaskExecutor` |
| **nodes** | `Operation/DataStoreDriven`：`SqliteNodeStore`/`NodeZoneStore`/`NodeRelationDeriver`/`ReferenceGraphCoordinator`/`NodeKeyMappingStore` | `Agent.Contract/Nodes/*`（AgentNode/NodeOutput/INodeOutputStore/NodeOutputKeys）、`Agent.Reference/*`（TaskGraphService/NodeRelation/TaskNodeRelationService/NodeProjectionService/NodeInjectionService）、`Agent.Core/NodeExecutionService` |
| **history** | `Share/Conversation`（ConversationLog/ConversationMessage/IHistoryStore，纯消息日志 LLM-free） | `Agent.Contract/History/*`（HistoryClass/HistoryTaskLink/IHistoryRelationManager）、`Agent.Reference/HistoryRelationManager`、`Agent.Orchestration/ConversationPipeline` |

> **关键洞察**：三层里 task/nodes/history 的**通用机制多数已在 `CsharpPrj`**；真正缺的是 ① 模型类型（`TaskRecord`/`AgentNode`/`HistoryClass`）还散在 `Agent.Contract`，② `AgentTaskItem` 融合 LLM，③ `Agent.Reference` 的编排（TaskGraphService/HistoryRelationManager/NodeRelation）是 LLM-free 的但仍留在 Agent 层。

### 9.2 目标：三层通用逻辑提取到 HierarchyMethod，全部 LLM 解耦

```
CsharpPrj/Operation/HierarchyMethod（通用基础层项目，全部 LLM-free）
├─ HierarchyMethod.Nodes   : NodeRecord / NodeOutput / INodeOutputStore / NodeOutputKeys + 节点关系推导
├─ HierarchyMethod.Tasks   : TaskRecord(纯簿记) + ExecutionNode(执行节点属性) + 任务状态机
└─ HierarchyMethod.History : HistoryRecord / HistoryTaskLink / IHistoryRelationManager + HistoryRelationManager 编排
        ▲ 消费时解析执行器
Projects/Agent（应用层，LLM 知识唯一所在）
├─ LLM 执行器 : LlmTaskExecutor / RuleTaskExecutor / StatelessAnswerTaskExecutor
├─ LLM 导演   : LlmStateDirector / AgentRunner / AgentOrchestrator（混合调度）
└─ LLM 组合   : AgentFrameworkAssembler（三层通用 + LLM 组装成运行时）
```

### 9.3 各层 LLM 解耦点

| 层 | LLM 耦合点（剥离） | 归属变化 |
|----|----|----|
| **task** | `AgentTaskItem.SchedulingMode/ExecutorId/AnswerSpec/CurrentStrategyId` → 移入 `ExecutionNode`（纯数据、无 LLM 调用）；`LlmTaskExecutor` 留 Agent | `TaskRecord` → HierarchyMethod.Tasks；`ExecutionNode` → HierarchyMethod.Tasks（数据对象，见 §三） |
| **nodes** | 节点本身无 LLM（NodeOutput 是纯数据）；`NodeExecutionService` 的"执行触发 LLM"属执行层，剥离到 Agent | `AgentNode/NodeOutput/INodeOutputStore/NodeOutputKeys` → HierarchyMethod.Nodes；`Agent.Reference/*` 编排 → HierarchyMethod.Nodes.Relations（LLM-free） |
| **history** | `ConversationPipeline` 的"沿工作流调子智能体（LLM）"属 Agent 编排；`Share/Conversation`/`HistoryRelationManager` 本身纯关系逻辑 | `HistoryClass/HistoryTaskLink/IHistoryRelationManager` + `HistoryRelationManager` → HierarchyMethod.History；`ConversationPipeline` 留 Agent |

### 9.4 依赖方向（单向无环）
`HierarchyMethod.Nodes → HierarchyMethod.Tasks → HierarchyMethod.History`（通用、LLM-free）→ `Projects/Agent`（LLM 应用 + 组合）。Agent 层只消费三层端口 + 注入 LLM 执行器/导演。

### 9.5 文档更新计划
- **本文档**：聚焦 task 层细化（§一–§八），作为三层之一的深度设计。
- **新增 companion**：`Projects/Agent/doc/nodes-task-history三层通用解耦设计.md` 汇总三层矩阵、提取边界、LLM 解耦点、依赖方向。
- **更新** `NodesTasksHistory_关系介绍.md` 的职责分层表（§6）：契约/编排归 `HierarchyMethod`（Operation 通用层），Agent 只留 LLM 组合与宿主适配。

