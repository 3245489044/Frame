# nodes / task / history 三层通用解耦设计（HierarchyMethod 通用框架）

> **版本**：1.1
> **日期**：2026-08-25
> **状态**：设计定稿（已部分实施，见附录 B 实施状态；task 层细化见 `Task纯簿记彻底重构设计.md`；关联模型见 `NodesTasksHistory_关系介绍.md`）
> **目标**：把 **nodes / task / history 三层**彻底与 LLM 解耦，提取为 **`CsharpPrj/Operation/HierarchyMethod`** 这一**通用基础框架**（`HierarchyMethod.Nodes / Tasks / History` 三子命名空间，全部 **LLM-free**）；Agent 层（`Projects/Agent/src`）只保留 LLM 执行器 / 导演 / 组合根，经端口注入三层。
> **本次优化**：从 v1.0「迁移动态总览」升级为「**框架设计蓝图**」——① 明确 HierarchyMethod 项目结构与 csproj 落地方式；② 逐子命名空间给出类型 / 端口 / 职责设计；③ 定义 LLM 解耦注入端口（执行器解析 / 导演提供 / 提示词外置）；④ 补齐兼容性迁移与每阶段交付物/验证/风险。

---

## 〇、一句话总览

> **三层通用化**：`nodes`（节点存储/输出/关系）、`task`（任务簿记/状态机/循环快照根）、`history`（历史容器/消息日志/关系）各为 **`CsharpPrj/Operation/HierarchyMethod`** 中**通用、LLM-free** 的逻辑单元（`HierarchyMethod.Nodes / Tasks / History`）；Agent 层把三层 + LLM 执行器/导演组装成运行时。task 携带执行节点（`ExecutionNode`）作为**属性**，但 task 不亲自调用 LLM——执行器在**消费时**经注入端口解析执行，LLM 提示词经 `WorkflowConfigKey` **外置**到配置。

---

## 一、目标与设计原则

### 1.1 为什么要 HierarchyMethod（框架动机）

三层通用机制多数已在 `CsharpPrj`（`Share/TaskStatusPool`、`Operation/DataStoreDriven`、`Share/Conversation`），但存在三处阻塞通用化：

| # | 阻塞点 | 现状 |
|---|--------|------|
| ① | 模型契约散在 Agent | `Agent.Contract/Nodes/*`、`Agent.Contract/History/*` 是纯数据/端口，却放在 Agent 契约项目 |
| ② | `AgentTaskItem` 三重融合 | 一条 `ur_tasks.PayloadJson` 同时装簿记（R1）+ 执行策略/LLM（R2）+ 循环快照根（R3），默认 `Hybrid`=LLM |
| ③ | 编排留在 Agent | `Agent.Reference/*`（`TaskGraphService`/`NodeRelation`/`HistoryRelationManager` 等）是 **LLM-free** 的，却留在 Agent 层 |

**结论**：真正需要做的是「把确定性的、可复用的通用逻辑**收拢为一个独立通用框架**」，而不是新造轮子。HierarchyMethod 就是这一收拢的容器。

### 1.2 框架设计原则（P1–P6）

| # | 原则 | 语义 |
|---|------|------|
| P1 | **LLM-free** | 框架本体零 LLM 依赖；LLM 知识（执行器/导演/提示词）只能存在于 Agent 应用层，经端口注入 |
| P2 | **单向依赖** | `HierarchyMethod.Nodes → HierarchyMethod.Tasks → HierarchyMethod.History`（通用、LLM-free）→ `Projects/Agent`；Agent 消费、不反向依赖 |
| P3 | **端口注入** | 执行器解析 / 导演提供 / 提示词键经接口注入；**默认零 LLM**（`Hybrid→Deterministic` 语义，见 `Task纯簿记彻底重构设计.md`） |
| P4 | **单一实现** | 编排门面只编排（何时建边/推导/查询），不复制写边原语——写边唯一收敛 `ReferenceGraphCoordinator`（SingleImplementation / TH-12） |
| P5 | **可空守卫** | 未装配端口/关系库时返回空集/诊断（`-1`/false），不消耗 AI 额度、不阻断 DB-only 路径 |
| P6 | **状态根唯一** | 确定性核心（`ProgressToken`/`FailureBudget`/`ControlFlag`）单一写入（`SEP.DeterministicCoreDisjoint`），循环快照根 = `AgentLoopState` 独立于 task 簿记 |

---

## 二、现状三层盘点（对应当前代码）

| 层 | ✅ 已在 `CsharpPrj`（通用、LLM-free） | ⚠️ 仍在 `Projects/Agent/src`（含 LLM 融合） |
|----|----|----|
| **task** | `Share/TaskStatusPool`（`TaskPool/TaskStateMachine/Loops`：`TaskPool`/`TaskStateMachine`/`SnapshotDrivenLoopEngine`/`DeterministicCoreCopier`/`TaskRouter`/`IRuleBackend`）、`Operation/SqliteTaskPoolStore`、`Operation/WriteThroughTaskPoolStore` | `Agent.Core/TaskPool/AgentTaskItem`（**融合 LLM**：`SchedulingMode`/`ExecutorId`/`AnswerSpec`/`CurrentStrategyId`/`ExplicitExecutor`/`AgentStatus`）、`AgentTaskItemJsonConverter`、`LlmTaskExecutor`/`RuleTaskExecutor`/`StatelessAnswerTaskExecutor`、`AgentTaskPoolAdapter` |
| **nodes** | `Operation/DataStoreDriven`（`SqliteNodeStore`/`NodeZoneStore`/`NodeRelationDeriver`/`ReferenceGraphCoordinator`/`NodeKeyMappingStore`/`NodeStore`/`NodeRelationStore`/`CrossDbReferenceTypes`） | `Agent.Contract/Nodes/*`（`AgentNode`/`NodeOutput`/`INodeOutputStore`/`NodeOutputKeys`/`NodeExecutionRecord`/`NodeOutputCodec`）、`Agent.Reference/*`（`TaskGraphService`/`NodeRelation`/`TaskNodeRelationService`/`NodeProjectionService`/`NodeInjectionService`/`WorkflowReferenceStaticizer`/`WorkflowResultDeriver`）、`Agent.Core/NodeExecutionService` |
| **history** | `Share/Conversation`（`ConversationLog`/`ConversationMessage`/`IHistoryStore`，纯消息日志 LLM-free） | `Agent.Contract/History/*`（`HistoryClass`/`HistoryTaskLink`/`IHistoryRelationManager`）、`Agent.Reference/Core/HistoryRelationManager`、`Agent.Orchestration/ConversationPipeline` |

> **关键洞察**：三层通用机制**多数已在 `CsharpPrj`**。真正缺的是 ① 模型契约（`TaskRecord`/`NodeRecord`/`HistoryRecord`）还散在 `Agent.Contract`，② `AgentTaskItem` 融合 LLM，③ `Agent.Reference` 的编排（`TaskGraphService`/`HistoryRelationManager`/`NodeRelation`）是 **LLM-free** 但仍留在 Agent 层。HierarchyMethod 的落地 = 把这三块迁入 + 定义注入端口。

---

## 三、HierarchyMethod 框架结构

### 3.1 项目落地方式（csproj）

**推荐**：单项目多命名空间 `HierarchyMethod.csproj`（`RootNamespace=Operation.HierarchyMethod`），内含 `Nodes / Tasks / History` 三个命名空间。理由：

- 与 `Operation.csproj` 现有聚合模式一致（每个功能一个子项目，被聚合 `ProjectReference` 引用）；
- 三层天然按 `Nodes→Tasks→History` 单向依赖，同项目内用命名空间约束即可，无需拆多 csproj 引入工程管理开销；
- 一次引用全量，Agent 只需一个 ProjectReference。

> **可选拆分**：若未来需要「仅引用某一层」的独立交付（如其他宿主只消费 History），可拆为 `HierarchyMethod.Nodes/Tasks/History.csproj` 三项目并保持相同依赖方向。本版默认单项目，拆分点已按目录预留。

**操作落地**：
1. 在 `CsharpPrj/Operation/` 新建 `HierarchyMethod/HierarchyMethod.csproj`（`net10.0-windows`，`RootNamespace=Operation.HierarchyMethod`）；
2. 在 `Operation.csproj` 聚合加入 `<ProjectReference Include="HierarchyMethod\HierarchyMethod.csproj" />` + `<Compile Remove="HierarchyMethod\**\*.cs" />`（对齐现有子项目模式）；
3. `HierarchyMethod.csproj` 引用通用基础：`Share/TaskStatusPool`、`Share/Conversation`、`Operation/DataStoreDriven`、`Share/AiScheduling`、`WorkFlowMethod`（`WorkflowContext`/`LoopFramework`）；**零 Agent 引用**。

### 3.2 目录结构（类型布局）

```
CsharpPrj/Operation/HierarchyMethod/
└─ HierarchyMethod.csproj            RootNamespace=Operation.HierarchyMethod
   ├─ Nodes/
   │  ├─ NodeRecord.cs               ← 迁 AgentNode（更通用命名，见 §4.2 别名）
   │  ├─ NodeOutput.cs               ← 迁 Agent.Contract/Nodes/NodeOutput
   │  ├─ NodeOutputKeys.cs           ← 迁 NodeOutputKeys（键方案单一权威）
   │  ├─ INodeOutputStore.cs         ← 迁 INodeOutputStore（端口）
   │  ├─ NodeExecutionRecord.cs      ← 迁 NodeExecutionRecord
   │  ├─ Relations/
   │  │  ├─ NodeRelation.cs          ← 迁 Agent.Reference/Relations/NodeRelation
   │  │  └─ TaskNodeRelationService.cs ← 迁 TaskNodeRelationService
   │  └─ Discovery/
   │     ├─ NodeProjectionService.cs ← 迁 NodeProjectionService
   │     └─ NodeInjectionService.cs  ← 迁 NodeInjectionService
   ├─ Tasks/
   │  ├─ TaskRecord.cs               ← R1 簿记（纯，见 §5.2）
   │  ├─ ExecutionNode.cs            ← R2 执行节点（task 属性，LLM 句柄）
   │  ├─ Loops/
   │  │  └─ AgentLoopState.cs        ← R3 循环快照根（IAgentLoopState）
   │  └─ Ports/
   │     ├─ ITaskExecutorResolver.cs ← 执行器解析端口（Agent 注入）
   │     └─ IStateDirectorProvider.cs← 导演提供端口（可选注入）
   ├─ History/
   │  ├─ HistoryRecord.cs            ← 迁 HistoryClass（更通用命名）
   │  ├─ HistoryTaskLink.cs          ← 迁 HistoryTaskLink
   │  ├─ IHistoryRelationManager.cs  ← 迁端口
   │  ├─ HistoryRelationManager.cs   ← 迁 Agent.Reference/Core 编排
   │  ├─ ConversationLog.cs          ← 并入 Share/Conversation
   │  └─ ConversationMessage.cs      ← 并入 Share/Conversation
   └─ Abstractions/
      ├─ UnavailableDiagnostics.cs   ← 可空守卫诊断常量（-1/false/空集）
```

### 3.3 依赖方向（单向无环）

```
Operation.HierarchyMethod（通用、LLM-free）
  └─ 消费 Operation.DataStoreDriven（节点库/关系库/写边原语）
     └─ 消费 Share.TaskStatusPool（任务池/状态机/循环引擎/确定性核心）
        └─ 消费 Share.Conversation（消息日志）
           └─ 消费 Share.AiScheduling / WorkFlowMethod（仅数据/上下文类型）
                  ▼
Projects/Agent（LLM 应用 + 组合）
  ├─ 实现注入端口：ITaskExecutorResolver（→ LlmTaskExecutor/RuleTaskExecutor/StatelessAnswerTaskExecutor）
  ├─ 实现注入端口：IStateDirectorProvider（→ LlmStateDirector/ConfigStateDirector）
  ├─ 组合根：AgentFrameworkAssembler（三层通用 + LLM 组装成运行时）
  └─ 宿主适配：AgentUiHost / DataStoreHosting / TaskPoolHosting
```

### 3.4 LLM 解耦注入端口（通用框架与 Agent 的唯一耦合面）

| 端口（HierarchyMethod 定义） | 语义 | Agent 实现 |
|------------------------------|------|-----------|
| `ITaskExecutorResolver` | 按 `TaskRecord.ExecutionNode`（`ExecutorId`/`WorkflowConfigKey`）解析出执行器句柄；未命中 → 诊断 | `AgentTaskPoolAdapter` 注入 `LlmTaskExecutor`/`RuleTaskExecutor`/`StatelessAnswerTaskExecutor` 注册表 |
| `IStateDirectorProvider` | 按 `AgentLoopState` 提供导演（Hybrid 混合调度时）；缺省 = 确定性导演（零 LLM） | `LlmStateDirector` / `ConfigStateDirector` |
| `IExecutorTrigger` | 节点执行「触发 LLM 回调」（`NodeExecutionService` 剥离出的执行层） | `NodeExecutionService` 的 LLM 触发段 |
| `IConfigKeyResolver` | `WorkflowConfigKey` → 提示词/轮数/停止规则（外置提示词，框架不内嵌任何提示词） | `ur_config` / `Config/workflows/*.json` 读取 |

> **默认零 LLM**：任一端口未注入时，框架走确定性路径（纯规则/簿记/节点执行），不阻塞、不消耗 AI 额度（P3/P5）。


---

## 四、HierarchyMethod.Nodes —— 节点通用框架

### 4.1 职责
节点 = 存储 + 执行 + 关系的统一单元：声明节点（`NodeRecord`）→ 解析入参（KeyMapping）→ 调用注册函数 → 输出端口持久化（`INodeOutputStore`）→ 关系推导（数据流/归属/控制流边）。**节点本身无 LLM**。

### 4.2 类型与命名迁移

| 新类型（HierarchyMethod.Nodes） | 源 | 说明 |
|--------------------------------|----|------|
| `NodeRecord` | `Agent.Contract/Nodes/AgentNode.cs` | 字段不变（`NodeKey/WorkflowId/CallKey/Symbol/FuncName/ParameterKeys/ResultKeys/InputMapping/OutputMapping`）。**通用命名**；迁入时保留 `AgentNode` 类型别名兼容，避免大规模调用点改写 |
| `NodeOutput` | `NodeOutput.cs` | 不变 |
| `NodeOutputKeys` | `NodeOutputKeys.cs` | 不变（`nodes.{wf}.{node}` 键方案单一权威，`NodeKeys.Normalize` 归一化并入） |
| `INodeOutputStore` | `INodeOutputStore.cs` | 端口不变 |
| `NodeExecutionRecord` / `NodeOutputCodec` | `NodeExecutionRecord.cs` / `NodeOutputCodec.cs` | 不变 |

### 4.3 关系编排迁入（LLM-free）

从 `Agent.Reference` 迁入（只编排、不复制写边原语，P4）：

| 组件 | 迁移目标 | 职责 |
|------|----------|------|
| `NodeRelation` | `HierarchyMethod.Nodes.Relations.NodeRelation` | 节点引用推导门面（`DeriveEdgesForNode`/`DeriveWorkflowControlFlowEdges`） |
| `TaskNodeRelationService` | `Relations.TaskNodeRelationService` | 执行期引用写（`cross_db_link` + `KeyMappingToNode`/`NodeToNode` 权威边集，写经 `ReferenceGraphCoordinator`） |
| `WorkflowReferenceStaticizer` | `Relations.WorkflowReferenceStaticizer` | 工作流控制流静态化（`Owns`/`Calls`） |
| `WorkflowResultDeriver` | `Relations.WorkflowResultDeriver` | 链尾推导（出度 0 → 终答步骤） |
| `NodeProjectionService` | `Discovery.NodeProjectionService` | task 作用域投影 / 组合投影候选节点索引 |
| `NodeInjectionService` | `Discovery.NodeInjectionService` | 预算/上下文注入 |

### 4.4 拆分点：`NodeExecutionService`

`Agent.Core/Nodes/NodeExecutionService.cs` 当前把「纯节点执行」与「执行触发 LLM」耦合。拆分：

| 拆分段 | 归属 | 说明 |
|--------|------|------|
| `BuildNode` / 入参解析（KeyMapping）/ 调用注册函数 / 输出持久化 / 关系写 | `HierarchyMethod.Nodes`（纯执行部分） | 确定性、LLM-free |
| 「该节点执行是否触发 LLM 子智能体」回调 | Agent（`IExecutorTrigger` 端口实现） | 由 Agent 编排决定 |

> 注：`NodeExecutionService` 依赖 `WorkFlowMethod.Usage`（`CallFunctionAsync`）与 `ParameterResolver.KeyMapping`，均为通用基础，引用合法。


---

## 五、HierarchyMethod.Tasks —— 任务簿记通用框架

### 5.1 职责
task = **纯簿记**（入队/状态/删除/提交/回填 + 幂等 + 优先级/重试 + 引用边），只关心「任务簿记」。执行策略封装为 `ExecutionNode` 由 task 作为属性携带；循环快照根 = `AgentLoopState`。task **不亲自调用 LLM**（`Task纯簿记彻底重构设计.md` §一–§八的落地归属）。

### 5.2 三实体拆分（R1 / R2 / R3）

| 实体 | 对应旧 `AgentTaskItem` 角色 | 字段 |
|------|---------------------------|------|
| `TaskRecord`（纯簿记，R1） | 任务簿记 | `TaskKey/Status/Priority/MaxRetries/HistoryId/ThreadId/InputKey/UserInput/RelatedNodes/CreatedAt/UpdatedAt` + `ExecutionNode`（属性） |
| `ExecutionNode`（执行节点，R2） | 执行策略/LLM | `ExecutorId`（句柄）/`DirectorMode`（可空）/`SchedulerRulesKey`/`WorkflowConfigKey`/`Payload`（不透明，供 `AnswerSpec` 等）、`ExplicitExecutor`。**纯数据、无 LLM 调用**；提示词经 `WorkflowConfigKey` 外置 |
| `AgentLoopState`（循环快照根，R3） | 循环快照 | `Budget`/`Token`/`Flag`/`Round`/`LastEval`/`LastResultText`/`Data`/`Scope`/`ContinuousFailures`/`CurrentStrategyId`/`AgentStatus`/`RecentTransitions` + `TaskKey/UserInput/ThreadId/InputKey`（从任务派生）；`SnapshotDrivenLoopEngine`/`RoundLoopDriver` 消费 |

**拆分映射（相对 `AgentTaskItem.cs` 字段）**：

| `AgentTaskItem` 字段 | 迁移目标 |
|----------------------|----------|
| `TaskKey/HistoryId/Priority/MaxRetries/UserInput/ThreadId/InputKey/RelatedNodes` | `TaskRecord` |
| `Status` | `TaskRecord`（生命周期状态机 `TaskExecutionStatus`，复用 `TaskStatusPool.TaskPool`） |
| `SchedulingMode/SchedulerRulesKey/ExecutorId/AnswerSpec/ExplicitExecutor` | `ExecutionNode` |
| `Budget/Token/Flag/Round/LastResultText/LastEval/Data/Scope/ContinuousFailures/CurrentStrategyId/AvailableStrategyIds/AgentStatus/RecentTransitions` | `AgentLoopState` |

### 5.3 端口与注入

| 端口 | 语义 | Agent 注入 |
|------|------|-----------|
| `ITaskExecutorResolver` | `TaskRecord` → 执行器（按 `ExecutionNode` 解析）；未注入 → 确定性执行 | `AgentTaskPoolAdapter` |
| `IStateDirectorProvider` | `AgentLoopState` → 导演（Hybrid 时）；缺省确定性 | `LlmStateDirector`/`ConfigStateDirector` |
| `IConfigKeyResolver` | `WorkflowConfigKey` → 提示词/轮数/停止规则 | `ur_config`/`Config/workflows/*.json` |

**任务池 `Resolve` 改造**：`AgentTaskPoolAdapter` 的 `Resolve` 改调用注入的 `ITaskExecutorResolver`（外部选择器），**默认零 LLM**（不注入即确定性）；删除 `AgentTaskItem` 内嵌的路由默认 `Hybrid`。

### 5.4 持久化与序列化迁移

- 旧 `ur_tasks.PayloadJson` 整份序列化 `AgentTaskItem`（`AgentTaskItemJsonConverter`）。
- 新布局：`TaskRecord` 持久化为簿记列 + `PayloadJson` 内 `ExecutionNode` 与 `AgentLoopState` 分节（见 §九兼容性，向后读取旧 `PayloadJson`）。
- `AgentLoopState` 持久化沿用 `DeterministicCoreCopier`（确定性核心对象推进还原，不直写私有 setter）与 `LoopStateSnapshot`（Data 还原）。
- 执行器（`LlmTaskExecutor`/`RuleTaskExecutor`/`StatelessAnswerTaskExecutor`）留在 Agent，实现通用端口，不进入 HierarchyMethod。


---

## 六、HierarchyMethod.History —— 历史容器通用框架

### 6.1 职责
history = **History = Task 更上层的 class 容器**（一个历史归拢多 task，每 task 下挂多 node）。管理历史创建/删除、历史归拢任务、历史挂载节点、消息时序、图查询。实体真源在 SQL（`ur_histories`/`ur_tasks`/`ur_nodes`），关系投影到 LiteDB 引用图；成员集由引用边推导，`graph.rebuild` 幂等重建（`NodesTasksHistory_关系介绍.md`）。

### 6.2 类型与命名迁移

| 新类型（HierarchyMethod.History） | 源 | 说明 |
|-----------------------------------|----|------|
| `HistoryRecord` | `Agent.Contract/History/HistoryClass.cs` | 查询视图不变（`HistoryId/Name/TaskKeys`）。**通用命名**，迁入时保留 `HistoryClass` 别名 |
| `HistoryTaskLink` | `HistoryTaskLink.cs` | 不变 |
| `IHistoryRelationManager` | `IHistoryRelationManager.cs` | 端口不变（`CreateAsync`/`DeleteAsync`/`AttachTaskAsync`/`AttachNodeAsync`/`AddSequenceAsync`/`GetTaskKeysAsync`/`GetTasksAsync`/`GetNodeKeysAsync`/`ListHistoryIdsAsync`） |
| `HistoryRelationManager` | `Agent.Reference/Core/HistoryRelationManager.cs` | 编排实现迁入（只编排，写边委托 `IReferenceMaintenance`→图协调器，P4/P5） |
| `ConversationLog` / `ConversationMessage` | `Share/Conversation` | 并入或对齐（纯消息日志 LLM-free）；`IHistoryStore` 保留 |

### 6.3 编排迁入（LLM-free）

| 组件 | 迁移目标 | 职责 |
|------|----------|------|
| `HistoryRelationManager` | `HierarchyMethod.History.HistoryRelationManager` | 历史归拢/时序/查询编排（幂等建根端点、`history_owns_task`/`history_owns_node`/`round_sequence`/`session_owns` 边） |
| `ConversationLog`/`ConversationMessage`/`IHistoryStore` | `HierarchyMethod.History` | 消息日志纯逻辑 |

> **留在 Agent**：`Agent.Orchestration/ConversationPipeline`（会话编排 + 「沿工作流调子智能体（LLM）」）保留 Agent——它消费 History 端口并触发 LLM，是应用层组合，不迁入通用框架。


---

## 七、Agent 应用层收口

迁移完成后，Agent 只保留：

| 类别 | 组件 | 说明 |
|------|------|------|
| LLM 执行器 | `LlmTaskExecutor` / `RuleTaskExecutor` / `StatelessAnswerTaskExecutor` | 实现 `ITaskExecutorResolver` 注入的注册项 |
| LLM 导演 | `LlmStateDirector` / `AgentRunner` / `AgentOrchestrator`（混合调度） | 实现 `IStateDirectorProvider` |
| LLM 组合根 | `AgentFrameworkAssembler` | 三层通用 + LLM 端口组装成运行时 |
| 宿主适配 | `AgentUiHost` / `DataStoreHosting` / `TaskPoolHosting` / `AgentUiLibraryTaskPool` | 对外暴露 + 入队门面（入队端默认确定性，不再内嵌 `AnswerSpec` 组装） |
| 会话编排 | `ConversationPipeline` | 消费 History 端口 + 触发 LLM |

**入队侧改造**（对应 `AgentUiLibraryTaskPool.TaskPoolEnqueueAsync`）：不再直接组装 `AnswerSpec`，改经 `ExecutionNode` 承载 `WorkflowConfigKey`，提示词由 `IConfigKeyResolver` 在消费时解析。

---

## 八、分阶段落地计划（每阶段独立编译 + 测试）

### Stage A — 建框架骨架 + task 层通用化（承接 `Task纯簿记彻底重构设计.md` Phase 1–6）
- 新建 `HierarchyMethod.csproj` + 三命名空间骨架；`Operation.csproj` 聚合引用。
- 落地 `HierarchyMethod.Tasks`：`TaskRecord`（纯簿记）+ `ExecutionNode` + `AgentLoopState`（Loops）。
- `AgentTaskItem` 解耦：R2 字段移入 `ExecutionNode`、R3 移入 `AgentLoopState`；`AgentTaskItemJsonConverter` 改分节序列化（向后兼容旧 `PayloadJson`）。
- 任务池 `Resolve` 改注入 `ITaskExecutorResolver`；默认零 LLM。
- **交付物**：`HierarchyMethod.Tasks` 可独立编译/单测；task 三场景（纯簿记 / stateless-answer / Hybrid）测试全绿。
- **验证**：`dotnet build` 0 错误；`TaskStatusPool` 相关测试全绿；手动 task 零 LLM 入队+执行。

### Stage B — nodes 层提取
- `HierarchyMethod.Nodes` 迁入 `NodeRecord`/`NodeOutput`/`INodeOutputStore`/`NodeOutputKeys`/`NodeExecutionRecord`。
- `Agent.Reference` 编排迁入 `HierarchyMethod.Nodes.Relations`/`Discovery`（LLM-free，零 LLM 依赖）。
- `NodeExecutionService` 拆：纯执行留 Nodes，LLM 触发回调留 Agent（`IExecutorTrigger`）。
- **验证**：节点/关系测试（`StateManagement.Tests`/`Agent.Persistence.Tests`）迁入后全绿；`graph.rebuild` 幂等重建通过。

### Stage C — history 层提取
- `HierarchyMethod.History` 迁入 `HistoryRecord`/`HistoryTaskLink`/`IHistoryRelationManager` + `HistoryRelationManager`；并入 `Share/Conversation`。
- `ConversationPipeline` 保留 Agent。
- **验证**：历史归拢/时序/枚举测试全绿；可空守卫（未装配图）返回诊断不阻断。

### Stage D — 端口注入收口
- 全量接通 `ITaskExecutorResolver`/`IStateDirectorProvider`/`IConfigKeyResolver`/`IExecutorTrigger`。
- `AgentFrameworkAssembler` 组装三层通用 + LLM。
- **验证**：Agent 全量编译 + 三层零 LLM 依赖独立单测（注入假执行器/假导演）。

### Stage E — 清理与兼容收尾
- 删除旧 `Agent.Contract/Nodes`、`Agent.Contract/History`、`Agent.Reference/*` 迁移源（保留别名过渡期后清理）。
- `NodesTasksHistory_关系介绍.md` 职责分层表同步（契约/编排归 HierarchyMethod，Agent 只留 LLM 组合）。
- **验证**：全量构建 + 回归 + `Check-ClinerulesReferences` 引用校验绿。

> **风险与缓解**：① 存量 `ur_tasks.PayloadJson` 需向后兼容读取（Stage A 分节序列化 + 兼容反序列化）；② `AgentNode`/`HistoryClass` 更名冲击调用面（保留别名过渡）；③ 编排迁入需保持依赖无环（P4 写边单一实现）。



---

## 九、兼容性与迁移策略

| 关注点 | 策略 |
|--------|------|
| 存量 `ur_tasks.PayloadJson` | 旧整份 `AgentTaskItem` 快照 → 新反序列化器按「若含 `ExecutionNode`/`AgentLoopState` 分节则读分节，否则读旧平铺字段并归位」兼容读取；确定性核心仍经 `DeterministicCoreCopier` 还原 |
| `AgentNode` / `HistoryClass` 更名 | 迁入时保留类型别名（`using AgentNode = Operation.HierarchyMethod.Nodes.NodeRecord;` 或 `#if`），过渡期后清理，避免一次性大规模改写 |
| 写边原语 | 不变：唯一收敛 `ReferenceGraphCoordinator`（幂等 upsert / `ApplyRelationEdgesAsync` 增量对账）；编排迁入只改命名空间、不改语义 |
| 键方案 | `NodeOutputKeys` / `NodeKeys.Normalize` 单一权威随迁，键形不变（`nodes.{wf}.{node}` / `{wf}.{node}`），`graph.rebuild` 幂等重建不受影响 |
| 边类型常量 | 唯一权威仍 `Operation.DataStoreDriven.Abstractions.CrossDbReferenceTypes` / `NodeRelationStore.ReferenceTypes`，HierarchyMethod 消费不改 |
| 依赖无环 | 端口注入 + P4 单一实现保证；`Check-ClinerulesReferences` 检查 4（循环依赖）持续校验 |

---

## 十、验证

1. `dotnet build Agent.sln` + `dotnet build CsharpPrj` → 0 错误（`CsharpPrj/MainProj.sln`、`CsharpPrj/FileDriven/FileDriven.sln`）。
2. `Agent.Core.Tests` / `StateManagement.Tests` / `Agent.Persistence.Tests`（节点/关系/历史/任务池）全绿。
3. 三层零 LLM 依赖可独立单测（注入假执行器/假导演/未装配守卫 → 诊断路径）。
4. 手动三场景：纯 task 零 LLM（`ExecutionNode` 空 → 确定性） / stateless-answer 走 LLM / Hybrid 显式走 LLM。
5. `graph.rebuild` 幂等重建 + `Check-ClinerulesReferences` 引用校验绿。

---

## 十一、文档关系

| 文档 | 职责 |
|------|------|
| 本文档 | nodes/task/history 三层通用解耦总览 + **HierarchyMethod 通用框架设计蓝图**（结构/端口/迁移/兼容） |
| `Task纯簿记彻底重构设计.md` | task 层深度细化（§一–§八 R1/R2/R3 拆分）+ 三层扩展（§九） |
| `NodesTasksHistory_关系介绍.md` | 三层关联模型（边类型/真源/职责分层）——需同步：契约/编排归 HierarchyMethod，Agent 只留 LLM 组合 |
| `Task即对话·对话驱动主循环外置设计.md` | 主循环外置与对话驱动（与 task 纯簿记互补） |

---

## 附录 A：v1.0 → v1.1 变更摘要

| 变化 | v1.0 | v1.1 |
|------|------|------|
| 定位 | 迁移动态总览 | 框架设计蓝图（结构/端口/兼容/阶段交付物） |
| csproj 落地 | 仅「三子命名空间」一句话 | 明确单项目 + `Operation.csproj` 聚合步骤 + 可选拆分 |
| LLM 解耦 | 仅归属表 | 定义 4 个注入端口（`ITaskExecutorResolver`/`IStateDirectorProvider`/`IExecutorTrigger`/`IConfigKeyResolver`） |
| 类型命名 | 沿用 Agent 名 | 通用命名（`NodeRecord`/`HistoryRecord`）+ 别名兼容 |
| 落地阶段 | A–D | A–E（新增兼容收尾）+ 每阶段交付物/验证/风险 |
| 兼容性 | 无 | §九 数据/命名/键/依赖四类兼容策略 |

---

## 附录 B：实施状态（2026-08-25）

### B.1 已实施范围

| 阶段 | 内容 | 状态 |
|------|------|------|
| Phase 0 | 框架骨架：`CsharpPrj/Operation/HierarchyMethod/HierarchyMethod.csproj`（`RootNamespace=Operation.HierarchyMethod`）+ Nodes/Tasks/History/Abstractions 命名空间 + `Operation.csproj` 聚合 | ✅ 已完成 |
| Phase 1 | 支撑 DTO 下沉：新建 `Share/HierarchySupport`，迁入 13 个纯 LLM-free DTO（SchedulingMode/StrategyDescriptor、DataSpace/IDataSpace/DataScope/PortSpec、DataContract 簇、AgentRuntimeSnapshot/ExecutorRunStatus/RuntimeStage） | ✅ 已完成 |
| Phase 2 | `HierarchyMethod.Tasks`：`TaskRecord`(R1)+`ExecutionNode`(R2)+`Loops/AgentLoopState`+`IAgentLoopState`(R3) + `Ports/ITaskExecutorResolver`+`IStateDirectorProvider` + `Abstractions/UnavailableDiagnostics` | ✅ 已完成 |
| Phase 3 | `HierarchyMethod.Nodes`：契约（`NodeDefinition`/`NodeOutput`/`NodeOutputKeys`/`NodeKeys`/`INodeOutputStore`/`NodeExecutionRecord`/`NodeOutputCodec`）+ `Relations`（`NodeRelation`/`TaskNodeRelationService`/`ResourceRelationService`）+ `Discovery`（`NodeProjectionService`/`NodeInjectionService`/`NodeDiscoveryModels`/`Helpers`）+ `TaskGraph`（`TaskGraphService`/`TaskGraphTaskSpec`/`ITaskGraphTaskStore`） | ✅ 已完成（Workflow 静态化/链尾推导除外，见 B.4） |
| Phase 4 | `HierarchyMethod.History`：`HistoryRecord`/`HistoryTaskLink`/`IHistoryRelationManager`/`HistoryRelationManager`（对齐 `Share/Conversation`） | ✅ 已完成 |

### B.2 关键偏差说明

- **`NodeRecord` 命名冲突修正**：设计文档 §4.2 拟将 `AgentNode` 通用更名为 `NodeRecord`，但发现 `Operation.DataStoreDriven.Abstractions.NodeRecord`（持久化存储记录：NodeType/Status/PayloadJson）**已存在**，与节点**定义**（FuncName/端口/映射）语义不同。故迁入的节点定义类型命名为 **`NodeDefinition`**，保留 `AgentNode` 别名指向它；`NodeRecord` 仍指 DataStoreDriven 存储记录。`HistoryClass`→`HistoryRecord` 更名无冲突，正常落地。

### B.3 桥接机制（兼容过渡）

- 迁入/下沉类型经 `GlobalUsings.cs` **全局类型别名**桥接（Agent src 11 项目 + 测试项目各一份）。
- 被清空的旧命名空间（`Agent.Contract.{History,Nodes,TaskGraph,Tasks}`、`Agent.Reference.Core`）以**哨兵类型**（`NamespaceCompatibilitySentinel`）保持对跨程序集 `using` 可解析（空命名空间不导出到元数据，直接移除会导致 CS0246）。
- 少量消费方 `using` 直接更新（`NodeExecutionService`/`AgentUiLibraryData`/`Agent.StorageBridge.Program`/`ConversationPipeline`/相关测试）。

### B.4 剩余工作

| 项 | 说明 |
|----|------|
| `WorkflowReferenceStaticizer`/`WorkflowResultDeriver` 迁入 Nodes.Relations | 依赖 `Agent.Core.Nodes.RoundSpec/RoundStep`（定义于 `RoundSpecLoader.cs`），需先解耦；且 `WorkflowResultDeriver` 存在**双源**（`Agent.Core/Nodes` 与 `Agent.Reference/Relations`），SingleImplementation 待收敛 |
| `NodeExecutionService` 拆分（§4.4） | 纯执行段迁 Nodes、LLM 触发段留 Agent（`IExecutorTrigger` 端口） |
| Phase 5 端口注入收口 | Agent 实现 `ITaskExecutorResolver`/`IStateDirectorProvider`/`IExecutorTrigger`/`IConfigKeyResolver`；`AgentTaskPoolAdapter.Resolve` 改注入执行器解析器，默认零 LLM |
| Phase 5 `AgentTaskItem` 拆分 | R2→`ExecutionNode`、R3→`AgentLoopState`；`AgentTaskItemJsonConverter` 改**分节序列化**（向后兼容旧 `PayloadJson`） |
| Phase 5 `AgentFrameworkAssembler` | 组装三层通用 + LLM 端口 |
| Phase 6 清理与兼容收尾 | 删除旧迁移源、同步 `NodesTasksHistory_关系介绍.md`（已同步 §6）、`Check-ClinerulesReferences` 校验 |

### B.5 验证结果

| 项 | 结果 |
|----|------|
| `Operation.csproj` / `Share.csproj` / `Agent.sln` 构建 | ✅ 0 错误 |
| `HierarchyMethod.Tests`（三层自包含单测） | ✅ 21/21（Tasks 11 + Nodes 7 + History 3） |
| `Agent.Core.Tests` / `Agent.Persistence.Tests` / `StateManagement.Tests` | ✅ 213 / 73 / 83 |
| 三层 LLM-free 独立可编译 | ✅ `HierarchyMethod.csproj` 零 Agent 依赖编译通过 |

### B.6 节点执行编排抽取（2026-08-25 三阶段落地）

> 本附录（B.1-B.5）为「**DTO/实体下沉**」实施状态；「**节点执行与上下文组装通用抽取**」（中立契约 / 函数调用端口 / NodeGraphOrchestrator 编排器）为**独立方案**，实施状态与设计差异见 [《节点执行与上下文组装通用抽取方案.md》](../../CsharpPrj/Operation/HierarchyMethod/节点执行与上下文组装通用抽取方案.md) 第 0 节与文末「实施记录」。

三阶段已完成要点：
- `HierarchyMethod.Nodes.Execution` 新增中立编排抽象：`NodeGraphOrchestrator` + `NodeRoundContext` + `NodeExecutionKeys` + 端口（`INodeExecutor` / `INodeExecutorHooks` / `IWorkflowDecisionPolicy` / `IFinalAnswerPolicy` / `IRoundStatePort` / `INodeFunctionInvoker`）+ 契约（`NodeExecutionRequest` / `NodeExecutionResult` / `NodeRoundResult` / `NodeRoundInput` / `EndpointProgress`）+ `IExecutionEndpoint`。
- **无适配器接入**：`RoundStepInvoker` 直接实现 `INodeExecutor`+`INodeExecutorHooks`；`WorkflowRunner` 直接实现决策/终答/状态策略并委托 `NodeGraphOrchestrator`；`RoundPipelineContext` → 框架 `NodeRoundContext`（主代码停用，文件保留供测试引用）。
- **验证**：`HierarchyMethod` / `Agent.Launcher` / `Agent.Core.Tests` / `Agent.Axioms.Tests` 全编译通过；`RoundStepInvokerTests` 4/4 通过。

****