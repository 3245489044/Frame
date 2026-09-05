# nodes / tasks / history 关联体系介绍

> 说明：本文档介绍 Agent 体系中 **node（节点）/ task（任务）/ history（历史）** 三者的关联模型、边类型、主链路与职责分层。所有边类型常量唯一权威见 `Operation.DataStoreDriven.Abstractions.CrossDbReferenceTypes` / `NodeRelationStore.ReferenceTypes`；编排门面统一收敛于 **`Operation.HierarchyMethod`**（`HierarchyMethod.Nodes.Relations/Discovery/TaskGraph` + `HierarchyMethod.History`，SingleImplementation / TH-12）。
>
> **实施状态（2026-08-25）**：三层契约与编排已迁入 `CsharpPrj/Operation/HierarchyMethod`（详见 `nodes-task-history三层通用解耦设计.md` 附录 B）；`WorkflowReferenceStaticizer` / `WorkflowResultDeriver` 已迁入 `Operation.HierarchyMethod.Nodes.Relations`（`WorkflowResultDeriver` 的 `ResolveFinalAnswer` 副本仍留 `Agent.Core/Nodes`，依赖 `Agent.Contract` 类型，待收敛）。**8.25 对齐 C# 的「引用关系 = 编译期类型化契约」增量**：新增 `WorkflowCompiler`（静态编译检查，复用 DslMethod/UR 校验器）、`WorkflowTemplateResolver`（模板继承成员查找链）、`OutputScopeResolver` + `NodeOutput.Visibility`（作用域可见性）、`WorkflowReferenceTypes.Exports/Inherits/Implements/DependsOn` 边词汇（详见 `docs/analysis/2026/8/8.25/HierarchyMethod/HierarchyMethod优化设计-CSharp模型对齐.md` §9）。

---

## 1. 一句话总览

> **实体真源在 SQL（`ur_histories` / `ur_tasks` / `ur_nodes`），三者之间的关联关系统一投影到 LiteDB 引用图（`graph-log.db`）的引用边上。** 任何成员集（历史归拢任务/节点、任务归属节点）都由引用边推导，可 `graph.rebuild` 幂等重建，不冗余存储成员。

- **History = Task 更上层的 class 容器**：一个历史归拢多个任务，每个任务下挂多个节点。
- **Task = 一次可入队执行的工作单元**（写 `ur_tasks` 即激活 AI 循环）。
- **Node = 一次步骤执行的产出单元**（Node 即 Result，节点即存储单元）。

---

## 2. 实体与真源

| 实体 | SQL 真源 | 图端点 Id | ResourceType | 图端点数 |
|------|----------|-----------|--------------|----------|
| **History** | `ur_histories`（内容） | `history.{historyId}`（`HistoryNodeId`） | `history` | 1 个根端点 |
| **Task** | `ur_tasks` | `{taskKey}` | `task` | 1 个任务端点 |
| **Node（业务）** | `ur_nodes`（NodeType=node） | `{workflowId}.{nodeKey}`（`NodeRelationFactory.NodeId`） | `node` | 每业务节点 1 个 |
| **Node（输出）** | `ur_nodes`（NodeType=output） | NodeKey = `nodes.{workflowId}.{nodeKey}`（`NodeOutputKeys`） | `node` | 每输出记录 1 个 |

> 关键约定：业务节点图端点 Id = `{wf}.{node}`；输出记录 NodeKey = `nodes.{wf}.{node}`。读侧还原裸 `nodeKey` 时剥离 `{wf}.` 前缀（`NodeOutputKeys` / `NodeRecordResolver` 统一处理两种键形）。

---

## 3. 关联模型：关系投影

- **实体**（内容）在 SQLite：历史内容、任务行、节点载荷。
- **关系**（关联/归属/时序）在 LiteDB 引用图：成员集由引用边推导，图即真源，`graph.rebuild` 幂等重建。
- **写边唯一 owner**：图协调器 `ReferenceGraphCoordinator`（幂等 upsert、`ApplyRelationEdgesAsync` 增量对账）；守卫 `ReferenceGrounding` 与各编排门面**只决定何时写/推导哪些边**，不复制写边逻辑。

---

## 4. 边类型总表（唯一权威）

### 4.1 跨库关联边 —— `CrossDbReferenceTypes`

| 常量 | 语义 | 建立处 |
|------|------|--------|
| `cross_db_link` | **task → node 运行时产出归属**；task 作用域寻址根除跨 task 污染 | `ReferenceGrounding.EnsureTaskNodeLinkAsync`（经 `Operation.HierarchyMethod.Nodes.Relations.TaskNodeRelationService`） |
| `session_owns` | `session.{name}` → 会话消息/产出节点（会话图子图挂载） | 会话装配侧 |
| `round_sequence` | 消息节点 → 下一消息节点（round 时序链，历史顺序由引用边决定） | `ReferenceGrounding.AddRoundSequenceEdgeAsync`（经 `Operation.HierarchyMethod.History.HistoryRelationManager.AddSequenceAsync`） |
| `history_owns_task` | **history → task**（历史归拢任务） | `ReferenceGrounding.AttachHistoryTaskAsync`（经 `Operation.HierarchyMethod.History.HistoryRelationManager.AttachTaskAsync`） |
| `history_owns_node` | **history → wf.node**（历史归拢节点，跨多任务） | `ReferenceGrounding.AttachHistoryNodeAsync`（经 `Operation.HierarchyMethod.History.HistoryRelationManager.AttachNodeAsync`） |

### 4.2 任务声明关注边 —— 图协调器

| 常量 | 语义 | 建立处 |
|------|------|--------|
| `node_to_node` | task → node 声明关注边（任务入队时声明的关联节点） | `ReferenceGraphCoordinator.ReconcileTaskAsync`（经 `Operation.HierarchyMethod.Nodes.TaskGraph.TaskGraphService` / `ReferenceGrounding.ReconcileTaskAsync`） |

### 4.3 数据流/组合边 —— `NodeRelationDeriver`

| 边类型 | 语义 | 建立处 |
|--------|------|--------|
| `KeyMappingToNode` / `NodeToNode` | 节点入参映射/上游端口溯源 → 权威边集 | `NodeExecutionService` → `Operation.HierarchyMethod.Nodes.Relations.TaskNodeRelationService.WriteRelationsAsync`（沿 `NodeRelation.DeriveEdgesForNode`） |

### 4.4 工作流控制流静态边 —— `NodeRelationStore.ReferenceTypes`

| 常量 | 语义 | 建立处 |
|------|------|--------|
| `Owns` | 工作流包含步骤 | `WorkflowReferenceStaticizer`（经 `Operation.HierarchyMethod.Nodes.Relations.NodeRelation.DeriveWorkflowControlFlowEdges`） |
| `Calls` | 上一步骤调用下一步骤（主路径线性序） | 同上 |
| `Exports` | 跨工作流可见性边界（对齐 public）：workflow → node，声明节点导出 | `NodeRelationDeriver.DeriveExportsEdge` |
| `Inherits` | 工作流模板继承（对齐 is-a）：派生 → 基类 workflow | `NodeRelationDeriver.DeriveInheritsEdge` |
| `Implements` | 接口实现（对齐 C# interface）：workflow → contract | `NodeRelationDeriver.DeriveImplementsEdge` |
| `DependsOn` | 编译期依赖（对齐 using）：workflow → resource | `NodeRelationDeriver.DeriveDependsOnEdge` |

> 8.25 增量：`WorkflowCompiler`（`Operation.HierarchyMethod.Nodes.Relations`）在静态化前对工作流做静态编译（图完整性 / 调用环 / 孤立未使用 / 端口类型 CS0029，全部复用 DslMethod + UR 校验器，内存执行、不产生产物），已接入 `Agent.Core/Nodes/WorkflowRunner.EnsureWorkflowCompiledAsync`（每工作流一次，best-effort 不阻断本轮）。

---

## 5. 三条主链路

### 链路 A：History → Task → Node（历史容器归拢）

```
HistoryRelationManager（Operation.HierarchyMethod.History）
 ├─ CreateAsync(historyId)      → 幂等建 history.{id} 根端点（EnsureHistoryMirrorAsync）
 ├─ AttachTaskAsync(hid, task)  → history_owns_task 边（历史归拢任务）
 ├─ AttachNodeAsync(hid, wf, n) → history_owns_node 边（历史归拢节点产出）
 ├─ AddSequenceAsync(wf, a, b)  → round_sequence 时序边（消息顺序）
 └─ GetTaskKeysAsync / GetNodeKeysAsync / ListHistoryIdsAsync
        → 沿出边推导成员集（图即真源，不冗余存储）
```

- **装配**：`Agent.Launcher/Composition/ConversationHosting.cs` 在装配引用守卫时 `new HistoryRelationManager(grounding, grounding.Graph)`，经 `AgentUiHost.AttachHistoryRelations` 暴露到 UI 门面，并注入 `ConversationPipeline`。
- **读侧**：UI / 会话按历史枚举归拢的任务与节点（`AgentUiHost.HistoryRelations`）。

### 链路 B：Task → Node（任务关联 + 运行时归属）

```
入队声明关注（TaskGraphService，Operation.HierarchyMethod.Nodes.TaskGraph）
 └─ CreateTaskWithRelationsAsync → ReconcileTaskAsync → task → node（node_to_node 边，声明关注的节点）

运行时实际产出归属（执行期，NodeExecutionService → Operation.HierarchyMethod.Nodes.Relations.TaskNodeRelationService）
 └─ EnsureTaskNodeLinkAsync(taskKey, wf, node) → task → node（cross_db_link 边，实际产出归属）

读侧 task 作用域寻址
 ├─ ReferenceGrounding.GetTaskNodeKeysAsync(task, wf)   → 本 task 归属节点键集
 └─ NodeProjectionService.ProjectAsync(task, wf, limit) → 沿 cross_db_link 挂载 + NodeToNode 组合投影候选节点索引
```

- `cross_db_link`（运行时归属）与 `node_to_node`（入队声明关注）**互补**：前者记录"实际产出归属"，后者记录"任务声明关注的节点"。
- **端口解耦**：`TaskGraphService` 只依赖 `ITaskGraphTaskStore`（`Operation.HierarchyMethod.Nodes.TaskGraph` 端口）+ `IResourceReferenceStore`；任务入队经 `Agent.Core/TaskPool/AgentTaskGraphStoreAdapter`（`ITaskGraphTaskStore` → `AgentTaskPoolAdapter` → `AgentTaskItem`）适配，保持 `Agent.Core → Operation.HierarchyMethod` 单向无环。

### 链路 C：工作流内 Node 间控制流

```
WorkflowCompiler（Operation.HierarchyMethod.Nodes.Relations —— 静态编译检查，复用 DslMethod/UR 校验器）
 └─ Compile(spec) → 图完整性 / 调用环 / 孤立未使用 / 端口类型(CS0029)（best-effort，不阻断本轮）
     接入 WorkflowRunner.EnsureWorkflowCompiledAsync（每工作流一次，编译→日志→再静态化）

WorkflowReferenceStaticizer（Operation.HierarchyMethod.Nodes.Relations）
 └─ Derive(spec) / StaticizeAsync → Owns + Calls 静态引用边（一次常驻）

WorkflowResultDeriver（Operation.HierarchyMethod.Nodes.Relations；ResolveFinalAnswer 副本留 Agent.Core/Nodes，待收敛）
 └─ ResolveFinalStepIndex(spec) → 沿 Calls 链尾（出度 0）推导终答步骤

WorkflowTemplateResolver（Operation.HierarchyMethod.Nodes.Relations）
 └─ MergeSteps(derived, base) → 成员查找链（派生覆写同名 NodeId + 基类补足，对齐 override）

NodeExecutionService（执行期）
 └─ Operation.HierarchyMethod.Nodes.Relations.TaskNodeRelationService.SerializeRelations / DeriveEdgesForNode
        → KeyMappingToNode / NodeToNode 权威边集（守卫 WriteRelationsAsync 收敛）
```

- 静态化（Calls/Owns）与链尾推导（出度 0）**同源**（共用 `NodeRelation.DeriveWorkflowControlFlowEdges`），不另起 FinalNode/FinalPort 字符串契约。

---

## 6. 职责分层与组件映射

| 层 | 组件 | 职责 |
|----|------|------|
| **契约/编排**（Operation.HierarchyMethod） | Nodes：`NodeDefinition`/`NodeOutput`/`NodeOutputKeys`/`NodeKeys`/`INodeOutputStore`/`NodeExecutionRecord`/`NodeOutputCodec`、`Relations`（`NodeRelation`/`TaskNodeRelationService`/`ResourceRelationService`）、`Discovery`（`NodeProjectionService`/`NodeInjectionService`）、`TaskGraph`（`TaskGraphService`/`TaskGraphTaskSpec`/`ITaskGraphTaskStore`）；Tasks：`TaskRecord`/`ExecutionNode`/`AgentLoopState`/`IAgentLoopState`/端口；History：`HistoryRecord`/`HistoryTaskLink`/`IHistoryRelationManager`/`HistoryRelationManager`；**Reference**：`NodeTaskHistoryService`（统一「建历史 + 归拢 task + 关联 node」高层编排门面，收敛 UI 层散落的 CreateAsync+AttachTaskAsync 重复） | 契约/编排（何时建边/推导/查询），不复制写边 |
| **守卫/执行**（Operation.DataStoreDriven） | `ReferenceGrounding`（IReferenceMaintenance，决策编排）、`ReferenceGraphCoordinator`（唯一写边执行者） | 写边原语幂等 upsert / 增量对账 |
| **适配/LLM 触发**（Agent.Core） | `AgentTaskGraphStoreAdapter`（ITaskGraphTaskStore → AgentTaskPoolAdapter）、`NodeExecutionService`（执行期引用写经 TaskNodeRelationService；纯执行段待迁 Nodes） | 宿主适配 + 执行触发 |
| **组合根**（Agent.Launcher） | `ConversationHosting`（装配 HistoryRelationManager / ConversationPipeline）、`AgentUiHost`（UI 门面槽位） | 装配与暴露 |

> **依赖方向（单向无环）**：`Operation.HierarchyMethod.Nodes → Tasks → History`（LLM-free）→ `Projects/Agent`；`Agent.Core → Agent.Reference`（消费编排 + 适配）、`Agent.Core → Operation.HierarchyMethod`（消费节点/历史编排），无反向引用。

---

## 7. 关键代码定位

| 关注点 | 文件 |
|--------|------|
| 历史归拢任务/节点 | `CsharpPrj/Operation/HierarchyMethod/History/HistoryRelationManager.cs` |
| 统一「建历史 + 归拢 task + 关联 node」编排门面（UI 层唯一归拢入口，Id 规则 historyId=taskKey） | `CsharpPrj/Operation/HierarchyMethod/Reference/NodeTaskHistoryService.cs` |
| 创建 task + 引用边复合服务 | `CsharpPrj/Operation/HierarchyMethod/Nodes/TaskGraph/TaskGraphService.cs` |
| 节点引用推导门面 | `CsharpPrj/Operation/HierarchyMethod/Nodes/Relations/NodeRelation.cs` |
| 执行期引用写（cross_db_link + 权威边） | `CsharpPrj/Operation/HierarchyMethod/Nodes/Relations/TaskNodeRelationService.cs` |
| 工作流控制流静态化 / 编译 / 链尾 / 继承 | `CsharpPrj/Operation/HierarchyMethod/Nodes/Relations/{WorkflowReferenceStaticizer, WorkflowCompiler, WorkflowResultDeriver, WorkflowTemplateResolver}.cs`（`ResolveFinalAnswer` 副本留 `Projects/Agent/src/Agent.Core/Nodes/WorkflowResultDeriver.cs`，待收敛） |
| task 作用域投影 / 预算注入 | `CsharpPrj/Operation/HierarchyMethod/Nodes/Discovery/NodeProjectionService.cs` / `NodeInjectionService.cs` |
| 任务入队端口适配 | `Projects/Agent/src/Agent.Core/TaskPool/AgentTaskGraphStoreAdapter.cs` |
| 边类型常量（跨库 / 控制流 / 数据流） | `CsharpPrj/Operation/DataStoreDriven/Abstractions/CrossDbReferenceTypes.cs`、`NodeRelationStore.cs` |
| 写边守卫 / 图协调器 | `CsharpPrj/Operation/DataStoreDriven/Core/ReferenceGrounding.cs`、`Graph/ReferenceGraphCoordinator.cs` |
| 装配 / UI 门面 / 会话消费 | `Projects/Agent/src/Agent.Launcher/Composition/ConversationHosting.cs`、`Modules/AgentUiHost.cs`、`Projects/Agent/src/Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs` |

---

## 8. 设计与一致性要点

- **图即真源**：历史/任务的成员集一律由引用边推导，`graph.rebuild` 幂等重建，无冗余成员存储。
- **统一归拢入口 + Id 规则**：UI 层（`ConversationSend` / `ConversationAttachTask` / `TaskPoolEnqueue`）一律经 `NodeTaskHistoryService.EnsureTaskHistoryBoundAsync` 归拢，**historyId == taskKey（会话名）**，与 `ConversationPipeline` 的归拢语义一致——消除旧「UI 层 default 历史 vs pipeline 层 sessionName 历史」的双容器矛盾（同一 task 不再同时出现在两个 history 下）。
- **写边单一实现**：所有写边最终落到 `ReferenceGraphCoordinator`（幂等），门面/守卫只编排（SingleImplementation / TH-12）。
- **可空守卫**：未装配守卫/关系库时，各门面返回空集/诊断（`-1` / false），不消耗 AI 额度、不阻断 DB-only 路径。
- **两种 task→node 边语义分工**：入队 `node_to_node`（声明关注）vs 运行时 `cross_db_link`（实际产出归属），读侧 task 作用域寻址按 `cross_db_link` 过滤，根除跨 task 污染。
- **三种容器归属语义**：`history_owns_task`（历史归拢任务）、`history_owns_node`（历史归拢节点，跨任务）、`session_owns`（会话图子图挂载），三者正交。
