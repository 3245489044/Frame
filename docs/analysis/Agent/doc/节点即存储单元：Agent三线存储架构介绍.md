# 节点即存储单元：Agent 三线存储架构介绍

> **定位**：本文介绍 WorkFlowPrj 中「把 WorkFlowMethod 的工作流节点提升为 Agent 基础存储单元」的当前实现状态。
> **范围**：节点存储单元、图关系存储（LiteDB 图）、KeyMapping 节点化、存储收敛（nodes/tasks + LiteDB 图；SQLite `ur_resources` 已下线）——各落点、装配链、执行链与验证结果。
> **阅读对象**：需理解 Agent 存储/执行架构的开发者。

---

## 一、核心思想

**一切逻辑以 workflow 节点为主。** 一个工作流节点（`AgentNode`）既是**执行单元**（注册函数 `FuncName`），又是**数据容器**（输入端口 `ParameterKeys` / 输出端口 `ResultKeys`）。所有 Agent 输出以节点端口形态落盘，节点间经 KeyMapping 自由组合上下文与逻辑。

存储按三条线分离，抽为**三个职责单一的存储模块**（节点即存储单元）：

| 模块 | 存储线 | 存什么 | 物理数据库 | 载体 | 权威位置 | 数据语义 |
|------|--------|--------|-----------|------|----------|----------|
| **模块 A** | ① 节点存储单元 | 节点的输出端口（`nodes.{wf}.{node}` 记录 + 端口 JSON 载荷） | **基础节点库 ur_nodes**（SQLite `agent.db`，`NodeType=output`，与模块 C 同库，已并入） | `NodeOutputStore`（`INodeOutputStore`） | `Agent.Contract.Nodes` + `Agent.Axioms.Nodes` | 数据载荷（节点=存储基底，持久化） |
| **模块 B** | ② 图关系存储 | 节点之间的**关系**（两类边） | **LiteDB `graph-log.db`**（集合 `ur_references` + CoreGraph 热邻接） | `IResourceReferenceStore`（`ReferenceGraphCoordinator`） | `Operation.DataStoreDriven.Graph` | 拓扑关系（图资源+引用边） |
| **模块 C** | ③ 基础节点库 | **KeyMapping**（节点逻辑，分类） | **SQLite `ur_nodes`**（表：键名 `NodeKey` + 一个 JSON + 入库时间，`NodeType=keymapping`）；未装配节点库回落 **SQLite `ur_keymappings`** | 节点库 `INodeStore`（`SqliteNodeStore`） | `Operation.DataStoreDriven.Core.NodeKeyMappingStore` | 逻辑载荷（映射 JSON） |

> 三模块语义互补不重叠：A 存**结果数据**、B 存**节点拓扑**、C 存**节点逻辑**（KeyMapping）；各模块细节见「二、模块化分类」，SQL 侧与任务见「三、SQL 侧储存与任务」，装配/落盘链路见「四、装配链」「五、执行链」。

> **物理落点速览（对齐实际代码）**：
> - **模块 A（节点输出）= 基础节点库 `ur_nodes`（已并入模块 C）**：`NodeOutputStore` 背靠 `INodeStore`，每个节点一条 `NodeType=output` 记录（NodeKey=`nodes.{wf}.{node}`，端口以 JSON 载荷承载、值类型保真往返）；数据载荷**持久化**，重启可恢复（不再依赖 StateCenter Session 层内存，已消除「存储基底不落盘」缺陷）。
> - **模块 B（图关系）= LiteDB `graph-log.db`**：`ReferenceGraphCoordinator`，节点/引用持久化到 `ur_references` 集合 + CoreGraph 热邻接；由 `DatabaseBootstrapper.CreateGraphStoreAsync` 装配（与 SQLite 骨干同目录）。
> - **模块 C（KeyMapping）= SQLite `ur_nodes` 表**：`SqliteNodeStore` 极简「键名 NodeKey + 一个 JSON」（全部元数据随 Json），`NodeType=keymapping` 判别，由 `DataStoreProviderFactory.BuildSqliteDataStore` 装配（与 SQLite 骨干同库 `agent.db` 同连接）；未装配节点库（DB-only/无 DataStore）时回落 SQLite `ur_keymappings` 表。
> - **SQLite 骨干 `agent.db`**：承载状态集合（`ur_tasks`，任务=状态根 + Agent 状态快照）与**基础节点库 `ur_nodes`**（`SqliteNodeStore`，纯「键名+一个 JSON」，NodeType 判别承载 input/output/event/config/checkpoint/keymapping/resource）；**不再建 SQL `ur_resources`**（该表已彻底下线，见 §3.4）。
> - **存储收敛（2026-08-17 方案 + 2026-08-18 节点库回迁 SQLite，已落地）**：DataStoreDriven 层逻辑存储 —— ① 基础节点库（**SQLite `ur_nodes`**，纯「键值 + 一个 JSON」，承载 input/output/event/checkpoint/config/keymapping/resource，NodeType 判别，NodeType/ThreadId 过滤由 JSON 表达式索引 `json_extract` 支撑）· ② 状态集合 tasks（SQL `ur_tasks`，任务=状态根）· **引用关系只留 LiteDB 图库 `graph-log.db`**（`ReferenceGraphCoordinator`，模块 B 真源）。SQL `ur_resources` 已删除（`SqliteResourceStorage` / `IPersistenceStore.Resources` 下线，`SqliteSchema` 仅建 `ur_tasks`；`ur_nodes` 由 `SqliteNodeStore.InitializeAsync` 幂等建表）。

---

## 二、三线存储模块（模块化分类）

> 每模块统一模板：**定位职责 → 契约（端口）→ 实现 → 键与读写路径**；模块间依赖单向 —— 模块 A 无外部依赖（Agent 层），模块 B/C 依赖 DataStoreDriven，模块 C 在未装配节点库（DB-only/无 DataStore）时回落 `ur_keymappings` 表（同为 SQLite）。

### 模块 A：节点存储单元（Agent 层 · ur_nodes 基础节点库）

> **定位**：一切 Agent 输出以「节点端口」为基底持久化与检索，节点即存储单元；数据载荷落基础节点库 ur_nodes（`NodeType=output`，已并入模块 C），跨轮/跨任务可按节点复用，并经 KeyMapping 供下游节点组合。
>
> **物理落点**：**基础节点库 ur_nodes（SQLite `agent.db`）**——`NodeOutputStore` 背靠 `INodeStore`（经 `NodeStorageStoreFactory` 装配的 `SqliteNodeStore`），每个节点一条 `NodeType=output` 记录（NodeKey=`nodes.{wf}.{node}`），端口以 JSON 载荷承载、值经 `NodeOutputCodec` 类型保真往返。**数据载荷持久化、重启可恢复**（模块 A 并入模块 C，消除「存储基底不落盘」与结果双写介质分裂）；无节点库（DB-only 装配）时节点输出不落库，作为增强路径不阻断运行。

契约（`Agent/src/Agent.Contract/Nodes/`）：

| 类型 | 职责 |
|------|------|
| `AgentNode` | 节点定义：`NodeKey/WorkflowId/CallKey/Symbol/FuncName/ParameterKeys/ResultKeys/InputMapping/OutputMapping` |
| `NodeOutput` | 单节点持久化输出（`NodeKey + OutputPorts`） |
| `INodeOutputStore` | 节点输出存储端口（异步写/读/枚举/清理；存储收敛：背靠 `ur_nodes`） |
| `NodeOutputKeys` | 节点输出记录键方案 `nodes.{workflowId}.{nodeKey}`（模块 A 并入模块 C 后权威） |

实现（`Agent/src/Agent.Axioms/Nodes/`）：

| 类型 | 职责 |
|------|------|
| `NodeOutputStore` | 基础节点库 ur_nodes 落地（`NodeType=output`，每节点一条记录）；跨轮/跨任务按节点端口检索、重启可恢复 |
| `NodeOutputCodec` | 端口值「类型名 + JSON」类型保真编解码（类型不可解析/反序列化失败回退 JsonElement） |
| `NodeExecutionService` | 节点执行核心：`BuildNode` / `ResolveInputsAsync`（含溯源，展平源键空间的唯一权威）/ `ExecuteAndPersistAsync` |

**端口方法面（`INodeOutputStore`，逐条对齐实现）**：

| 方法 | 语义（对齐 `NodeOutputStore` 实现） |
|------|------|
| `WriteOutputAsync(workflowId, nodeKey, port, value)` | `null` 值直接忽略（不产生空槽位）；按 `NodeOutputKeys.NodeKey` upsert 节点记录（读-改-写端口 JSON），`NodeType=output` |
| `TryGetNodeOutputAsync` / `EnumerateOutputsAsync` | 按 `NodeType=output + ThreadId` 表达式索引（`json_extract`）过滤 + `afterId` 分页拉取，`NodeKey` 前缀还原 `nodeKey`，聚为 `NodeOutput.OutputPorts`（端口值经 Codec 解码） |
| `EnumerateNodeKeysAsync` | 过滤 `NodeType=output + ThreadId` 后按前缀去重（`Distinct`） |
| `ClearNodeAsync` / `ClearWorkflowAsync` | 按记录键 `DeleteAsync` / 批量 `DeleteManyAsync` |

**存储键助手（`NodeOutputKeys`，模块 A 并入模块 C 后权威）**：`Prefix(wf)=nodes.{wf}.` · `NodeKey(wf,node)=nodes.{wf}.{node}`（唯一键，与业务节点 `{node}`、KeyMapping `keymapping.*` 命名空间隔离）；`DefaultWorkflowId="agent-round"`，`workflowId` 空串一律归一为默认值（`NormalizeWf`）。

### 模块 B：图关系存储（DataStoreDriven 层 · IResourceReferenceStore/CoreGraph）

> **定位**：存节点之间的**拓扑关系**（两类边：KeyMapping↔节点、节点↔节点）；以「图资源 + 引用边」持久化，NodeToNode 的端口组合信息编码进 refType 可还原。
>
> **物理落点**：**LiteDB 图库 `graph-log.db`**（与 SQLite 骨干同目录）——`FrameworkResourceGraphStage` 经 `DatabaseBootstrapper.CreateGraphStoreAsync` → `ReferenceGraphCoordinator`：节点/引用持久化到 `ur_references` 集合（LiteDB durable）+ CoreGraph 热邻接引擎（`GetNeighbors/FindPathsAsync/GetDependenciesAsync`）。当前运行时图真源在 LiteDB，非 SQLite。

`Agent/src` 之外由 `Operation.DataStoreDriven` 承载：

- `Abstractions/NodeRelationStore.cs`：`NodeRelationType`（**KeyMappingToNode / NodeToNode** 两类）、`NodeRelation`、`NodeResourceTypes`、`NodeRelationFactory`、`INodeRelationStore` 端口。
- `Graph/ReferenceGraphCoordinator.cs`：把两类关系以「图资源 + 引用边」持久化到 `IResourceReferenceStore`（LiteDB + CoreGraph）。NodeToNode 的端口组合信息编码进 refType 可还原；`AddRelationAsync` 幂等（同端点同 refType 先删后加）。

**实现细节（`ReferenceGraphCoordinator`）**：

- **refType 编码（`Ref.Encode`）**：`KeyMappingToNode → "keymapping_to_node"`（固定）；`NodeToNode → "node_to_node:{fromPort}>{toPort}[?km={keyMappingRef}]"`。解析侧 `Ref.ParseType` / `Ref.ParseNodeToNode` 还原 `FromPort/ToPort/KeyMappingRef`。
- **端点资源三元组**：`Node` / `KeyMapping` 各自映射为 `ResourceIdentifier{ ResourceType, Id, Location=workflowId }`；资源 Id 经 `NodeRelationFactory.NodeId(wf,node)=wf.node`、`KeyMappingId(wf,key)=wf.key` 以 workflowId 命名空间隔离。
- **`AddRelationAsync` 幂等**：先 `EnsureResourceAsync`（源/目标节点资源镜像，`GetResourceAsync` 已存在即跳过；`AddResourceAsync` 命中「唯一键/资源已存在」类异常时容错吞掉）→ `RemoveReferenceAsync`（删同端点同 refType 旧边）→ `AddReferenceAsync`，避免跨轮累积重复持久引用记录。
- **检索**：`GetRelationsAsync`（可限定类别 / 含入边）/ `GetRelatedNodesAsync`（outgoing 取下游、false 取上游，仅返回 `NodeResourceTypes.Node` 邻接，去重）。

**镜像语义（为什么叫「镜像」）**：

> 图库中的 node/task/keymapping 端点资源统称「图资源镜像」，命名承载四层语义（对齐 `ReferenceGraphCoordinator` 设计原则——**LiteDB 的唯一作用 = 维护 SQL 中 task/node 的关系，且数据与 SQL 完全不同**）：

| 语义 | 含义 | 代码/事实落点 |
|------|------|--------------|
| ① 派生性 | 镜像由 SQL 实体**派生**而非原创：SQL 先写，写透守卫随后同步建镜像；本体删除则镜像级联消失 | `WriteThroughTaskPoolStore.EnqueueAsync` 先写 SQL `ur_tasks` → `EnsureTaskMirrorAsync`；`ReferenceGrounding.EnsureNodeMirrorAsync` / `EnsureTaskMirrorAsync` / `RemoveNodeMirrorAsync` / `RemoveTaskMirrorAsync` |
| ② 轻量性 | 镜像**不复制实体数据**，仅存 `Resource{ResourceType, Id, Symbol, Metadata}`（标识 + 符号名 + 元数据）；`PayloadJson` / `SnapshotJson` 等载荷全部留在 SQL | 协调器注释："SQL 拥有实体（ur_tasks / ur_nodes 存 task/node 本体与载荷），本协调器**不在 LiteDB 复制实体数据**（无 ur_resources）"；`ToResource` 仅四字段 |
| ③ 非权威性 | 镜像为**只读投影、永不作权威**；不一致时以 SQL 为准，经对账收敛 | `ReferenceGraphCoordinator.ReconcileNodeRelationsAsync`：以 SQL 推导的权威边集 diff，删残留 + 补缺失 |
| ④ 可重建性 | 镜像可从事实源**幂等重建**：标识 + 边可由 `ur_nodes` / `ur_tasks` 实体 + `NodeRelationDeriver`（映射 + 溯源）重推 | `NodeRelationDeriver.DeriveEdgesFromRelationsJson` 对账重推导；`Initialize()` 从持久边重建内存邻接 |

> **更进一步（代码事实）**：端点镜像实际只存在于**会话内存**（`ReferenceGraphCoordinator._endpoints` 字典 + `_graph` 热邻接，注释"不落盘"）；LiteDB 真正持久化的只有 `ur_references` 边集合（`SourceId/TargetId/RefType/Strength`）——**LiteDB 没有存镜像的集合**，镜像只是边的端点内存注册表。
> **一句话**：镜像 = 从 SQL 事实源派生、只带标识不携带数据、非权威可重建的投影；权威永远在 SQLite，LiteDB 只负责让"关系"有图可算。

**引用自动落地守卫（`Core/ReferenceGrounding.cs` + `Core/NodeRelationDeriver.cs`，对齐领域公理 `DerivedDataAutoMaintenance`，见 `Agent/Axioms/L2`）**：

- **职责**：把「业务实体（node/output/keymapping/task）↔ 图资源镜像 + 节点关系边」的维护固化为存储级职责，替代各写入口手写建边。
- **写透守卫（存储级 I1 不变式）**：`WriteThroughNodeStore`（包装 `INodeStore`，`Put/Delete` 命中 `NodeType=node` 业务节点 → 自动同步 `wf.node` 镜像）与 `WriteThroughTaskPoolStore`（包装 `ITaskPoolPersistence`，`Enqueue/Delete` → task 镜像；状态/载荷变化**不**重建镜像，避免任务高频变化导致的图抖动）。二者由 `FrameworkResourceGraphStage` / `FrameworkTaskPoolStage` 装配，业务/执行代码不再手写 `EnsureNodeAsync`。
- **只做镜像不做边**：`EnsureNodeMirrorAsync/RemoveNodeMirrorAsync`（Id=wf.node）、`EnsureTaskMirrorAsync/RemoveTaskMirrorAsync`（Id=taskKey）；边由 `NodeRelationDeriver`（`DeriveKeyMappingEdge` / `DeriveNodeToNodeEdges`）从「映射 + 溯源」推导后经 `WriteRelationsAsync`（`ReferenceGraphCoordinator` 单一实现）统一写入，幂等。
- **写边仍经单一实现** `ReferenceGraphCoordinator.AddReferenceAsync`（幂等 upsert），守卫只负责"何时写/推导出哪些边"，不复制写边逻辑（SingleImplementation / TH-12）；删除级联沿用 `RemoveResourceAsync`，不重写。
- **范围界定**：node 写透镜像白名单 = 仅 `{node}`（业务节点；workflowId 由 `WriteThroughNodeStore` 构造时注入）；`keymapping` 资源镜像由边写入的 `EnsureResourceAsync` 产生，zone（input/output/event）/config/checkpoint 不建图资源。

**资源类型常量（`NodeResourceTypes`，模块 B 内）**：

| 常量 | 值 | 语义 |
|------|-----|------|
| `Node` | `node` | 工作流节点在关系图中的资源类型（`ReferenceGrounding.EnsureNodeMirrorAsync` 写透镜像，Id=wf.node） |
| `KeyMapping` | `keymapping` | KeyMapping 逻辑在关系图中的资源类型（`KeyMappingToNode` 边的源端） |
| `Task` | `task` | task 在关系图中的资源类型（`ReferenceGrounding.EnsureTaskMirrorAsync`，Id=taskKey） |

### 模块 C：基础节点库承载 KeyMapping（DataStoreDriven 层 · ur_nodes）

> **定位**：KeyMapping 属于节点逻辑，经基础节点库承载分类（`NodeType=keymapping`，`NodeKey=keymapping.{category}.{key}`）；未装配节点库（DB-only/无 DataStore）时回落 SQL `ur_keymappings`（兼容）。
>
> **物理落点**：**SQLite `ur_nodes` 表**（`agent.db`）——`ctx.DataStore?.DataStore.Nodes` = `SqliteNodeStore`（`DataStoreProviderFactory.BuildSqliteDataStore` 装配，与 SQLite 骨干同库同连接），存储形态为纯「键名 `NodeKey` + 一个 JSON」——节点全部元数据（`NodeType/ThreadId/Key/Status/PayloadJson/RelationsJson/CreatedAt/UpdatedAt`）随 JSON 持久化（真单源、节点自包含），表仅 `Id/NodeKey/Json` 三列，NodeType/ThreadId 过滤由 JSON 表达式索引（`json_extract`）支撑。`NodeKeyMappingStore.SaveAsync` 写 `NodeType=keymapping` 节点。**未装配节点库（DB-only/无 DataStore）回落 SQLite `ur_keymappings` 表**（`SqliteKeyMappingStore`，UPSERT 幂等）。

- `Abstractions/KeyMappingStore.cs`：`KeyMappingCategory`（**NodeInput / NodeOutput**）、`KeyMappingRecord`、`IKeyMappingStore` 端口（`InitializeAsync` + `SaveAsync`，读/删/Global 已收缩，当前写路径无消费方）。
- `Core/NodeKeyMappingStore.cs`：KeyMapping 逻辑经节点库承载（`NodeType=keymapping`，`NodeKey=keymapping.{category}.{key}`、`PayloadJson=映射 JSON`），省去独立 `ur_keymappings` 表。装配期由 `NodeStorageStoreFactory.CreateNodeKeyMappings(nodes)` 创建；未装配节点库（DB-only/无 DataStore）时回落 `CreateSqliteKeyMappings`（SQL `ur_keymappings` 兼容）。

**节点库承载（`NodeKeyMappingStore`，存储收敛主路径）**：

| 字段 | 取值 |
|------|------|
| `NodeKey` | `"keymapping." + category + "." + key`（`Prefix="keymapping."`） |
| `NodeType` | `NodeTypes.KeyMapping` |
| `Key` | `category + "." + key` |
| `PayloadJson` | 映射 JSON（null → `"{}"`） |

写入 = `_nodes.PutAsync(NodeRecord)`（读写共用数据驱动引擎 `INodeStore`，即 `ur_nodes` 节点库）；`SqliteNodeStore.InitializeAsync` 幂等建表（表仅 `Id/NodeKey/Json` 三列 + JSON 表达式过滤索引）。

**SQL 回落（`SqliteKeyMappingStore`，未装配节点库兼容）**：表 `ur_keymappings`，`(Id INTEGER PK, Category, Key, ValueJson, UpdatedAt, UNIQUE(Category,Key))`；`InitializeAsync` 幂等建表，`SaveAsync` 走 `INSERT ... ON CONFLICT(Category,Key) DO UPDATE`（UPSERT 幂等写）；经 `AmbientTransactionAwareCommandExecutor` 包入当前 SQLite 事务。

### 跨模块：链合并（复用 WorkFlowMethod 复杂逻辑）

- `WorkFlowMethod/GraphUsing/Deduplication/NodeChainMergeFacade.cs`：复用 LogicComposition 的 `DefaultChainComposer<NodeConfig>`，按 `Callkey` 链合并 + 去重（`DuplicateHandling.Skip/Overwrite/Merge`）。
- `Agent.Core/DynamicWorkflow/DynamicWorkflowMerger.cs`：`FragmentNode ↔ NodeConfig` 桥接 + `MergeFragments(...)`，多动态片段合并改走该门面，替代扁平拼接。

---

## 三、SQL 侧储存与任务（ur_tasks）

> **定位**：SQLite 骨干 `agent.db`（`SqliteConnectionFactory`，表前缀 `ur_`）是唯一持久的「结构 + 状态」介质；三线中 **A 落 SQL（ur_nodes 节点库，`NodeType=output`，已并入模块 C）**、**C 落 SQL（ur_nodes 节点库）**、**B 走 LiteDB 图库**。SQL 承载 `ur_tasks`（状态集合）与 `ur_nodes`（基础节点库）两个逻辑存储（引用关系不落 SQL，见 §3.4），分别由 `SqliteSchema.InitializeAsync` 与 `SqliteNodeStore.InitializeAsync` 幂等建表。

### 3.1 当前 SQL 储存了什么（表总览）

| 表 | 类别 | 建表来源 | 承载内容 |
|----|------|----------|----------|
| `ur_nodes` | ① 基础节点库 | `SqliteNodeStore.InitializeAsync` | 全部逻辑/记录节点（input/output/event/checkpoint/config/keymapping/resource，NodeType 判别；纯「键名 + 一个 JSON」，全元数据随 Json） |
| `ur_tasks` | ② 状态集合（任务=状态根） | `SqliteSchema` + `SqliteTaskPoolStore` | 任务池条目（`AgentTaskItem` 全量快照）+ Agent 状态快照（`ISnapshotStore`） |

> 存储收敛后 `SqliteSchema` **不再创建** `ur_resources` 及旧 `ur_keymappings` / `ur_config` / `ur_checkpoints` / `ur_snapshots` / `ur_references` / `ur_inputs` / `ur_outputs` / `ur_events` 表 —— config/keymapping/checkpoint 与三区（input/output/event）均经 **SQLite 节点库 `ur_nodes`**（`NodeType` 判别，纯「键名 + 一个 JSON」）承载，引用关系只留 **LiteDB 图库 `graph-log.db`**，SQL 仅余 `ur_nodes` + `ur_tasks` 两表。

### 3.2 `ur_tasks` 表结构（任务 = 状态根，权威）

| 列 | 类型 | 语义 |
|----|------|------|
| `Id` | INTEGER PK AUTOINCREMENT | 自增主键 |
| `ThreadId` | TEXT NOT NULL | 命名空间（任务池固定 `"taskpool"`，保证 TaskKey 全局唯一） |
| `SessionId` | TEXT NOT NULL | 会话/任务键（任务池 = `TaskKey`） |
| `SnapshotJson` | TEXT NOT NULL | 整条任务载荷（`AgentTaskItem`）序列化 —— **任务即快照 / 状态根**；Status/Priority/MaxRetries 为载荷顶层属性、CreatedAt/UpdatedAt 以 `__createdAt/__updatedAt` 顶层键注入，全随 SnapshotJson 持久化 |
| `UNIQUE(ThreadId, SessionId)` | 约束 | 同任务幂等（UPSERT） |

> `ur_tasks` 与 `ur_nodes` 同构收敛为「业务键 + 一个 JSON」：仅 `Id/ThreadId/SessionId/SnapshotJson` 四列；Status/Priority 过滤由 JSON 表达式索引（`json_extract`，`idx_tasks_status`/`idx_tasks_priority`）支撑。

### 3.3 任务（ur_tasks）语义澄清

- **「任务」= 统一持久状态根**：整条 `AgentTaskItem` 全量序列化进 `SnapshotJson`（任务即快照，重启恢复全量状态）；`Status/Priority/MaxRetries` 为载荷顶层属性、`CreatedAt/UpdatedAt` 以 `__createdAt/__updatedAt` 顶层键随 `SnapshotJson` 持久化（表仅 `Id/ThreadId/SessionId/SnapshotJson` 四列），Status 过滤由 JSON 表达式索引（`json_extract`）支撑。
- **写即激活**：`SqliteTaskPoolStore.EnqueueAsync`（TaskKey 幂等 UPSERT）写 `ur_tasks` → Share 层 TaskPool 条目式状态机按 `Priority` 出队 → `AgentOrchestrator.RunAsync` 驱动一轮 AI 循环 → 结果写回输出。删除任务 = `DELETE` 该状态根行。
- **task 写透镜像**：任务池持久化端口经 `WriteThroughTaskPoolStore` 包裹（`FrameworkTaskPoolStage` 装配）——入队自动建 `task` 图资源镜像（Id=taskKey，幂等）、删除自动级联清边（`RemoveResourceAsync`）；任务**只建一次**镜像，状态/载荷变化（UpdateStatus/UpdatePayload）不重建，避免图抖动。
- **同一表双角色**：`ur_tasks` 同时是 `IPersistenceStore.Snapshots`（`SqliteSnapshotStore`）落点，承载 Agent 状态快照（`AxiomContext` 序列化，崩溃恢复 / 跨会话恢复）。
- **调用模型**：任务定义（入口、调用方式）+ 运行时状态（`Status` / 循环）在同一状态根上表达。

### 3.4 SQLite `ur_resources` 已下线（引用关系只留 LiteDB 图）

> 本轮收敛把 SQLite `ur_resources`（原「关系库」目标表）**彻底下线**：
> - `SqliteSchema` 不再建 `ur_resources`，SQL 仅 `ur_nodes`（基础节点库，`SqliteNodeStore` 自建）+ `ur_tasks`（状态集合）两表。
> - `SqliteResourceStorage` / `IPersistenceStore.Resources`（`StoreResourceReferenceStore` → SQLite）已移除。
> - 资源/引用关系**唯一真源 = LiteDB 图库 `graph-log.db`**（`ReferenceGraphCoordinator`，模块 B），SQL 侧不再承载任何关系边。
>
> 通用资源/引用端口仍存在（`IResourceReferenceStore`，供 `db.Resources` / 存储桥 / 技能 `addResource`/`store.resource` 调用），但其**底层落点就是 LiteDB 图库**，不再有任何 SQL 表承载。

### 3.5 事务

- `SqliteTransaction`（`IPersistenceStore.BeginTransactionAsync`）提供单库原生事务；`SqliteNodeStore` 经 `AmbientTransactionAwareCommandExecutor` 纳入同一 SQLite 环境事务——**节点库（ur_nodes）与 KeyMapping/任务（ur_tasks）同库原子**，`PersistNodeMetadataAsync` 把 KeyMapping 与节点写包进同一 SQLite 事务（单库原子，跨介质隐患消除）。
- **图关系边（LiteDB `graph-log.db`）仍跨介质，无单库原子性**——引用层按逐条幂等写透 + 最终一致 + 对账兜底处理（对齐 `DerivedDataAutoMaintenance`），不再声称"单库原子写覆盖引用层"。

---

## 四、装配链（组合根 → 执行器）

> 装配链一次性实例化三模块：模块 B（图关系）+ 模块 C（KeyMapping）经 `NodeStorageStoreFactory` 创建并 `InitializeAsync`（装配期建图/建表唯一入口）；模块 A（节点输出）与模块 C 同源（ur_nodes `NodeType=output`），由 `RoundStepInvoker` 以 `_nodeStore` 构造 `NodeOutputStore` 一次性注入。

| 环节 | 模块 | 位置 | 动作 |
|------|------|------|------|
| 存储工厂 | B + C | `DataStoreDriven.Resolvers.NodeStorageStoreFactory` | `ReferenceGraphCoordinator`（经 `DatabaseBootstrapper.CreateGraphStoreAsync` 装配，B）/ `CreateNodeKeyMappings(nodes)`（C 有节点库）或 `CreateSqliteKeyMappings(options)`（C 回落）/ `InitializeAsync(relations, keyMappings)` |
| 完整框架装配 | B + C | `Agent.Launcher.AgentFrameworkAssembler` | `FrameworkResourceGraphStage` 建两存储 + `InitializeAsync` → `orchestrator.SetNodeStores(...)` |
| 编排器注入 | B + C | `Agent.Core.AgentOrchestrator` | `SetNodeStores` → Runner 装配输入透传 |
| 运行器 | A 读取侧 | `Agent.Core.AgentRunner` | 构造 `WorkflowRunner` 透传存储；暴露 `NodeRelations`（B）+ `GetNodeOutputsAsync(sessionId)`（A，经 `IWorkflowRunner` 端口） |
| 步骤调用器 | A + B + C | `Agent.Axioms.Loops.RoundStepInvoker`（实例） | 由 `WorkflowRunner` 持有单实例，节点存储（B 图关系 + C 分类 KeyMapping + A 节点输出 ur_nodes）构造期注入一次；`NodeOutputStore`（A，背靠 `_nodeStore`）构造集中于此 |

`Agent.Axioms.csproj` 新增对 `DataStoreDriven.csproj` 的引用（承载两个存储端口）。

**装配链关键接线（对齐实现）**：

- **`AgentRunnerAssemblyInput` 单一透传**：`AgentOrchestrator.Runner`（惰性 `AgentRunnerFactory.Create(...)`）把 `NodeRelations` / `KeyMappings` / `BeginTransaction` / `NodeStore`（写透守卫包装后）/ `Grounding`（引用守卫）一起包进装配输入；`AgentRunner` 构造时透传 `WorkflowRunner(keyMappings, beginTransaction, nodeStore, grounding)`。
- **写透与最终一致（`BeginTransaction`）**：装配了持久化骨干（`_state.PersistenceStore`）时，`AgentOrchestrator` 注入 `async ct => p.BeginTransactionAsync(ct)`，使 SQL 侧 KeyMapping 写入包进同一 SQLite 事务；**KeyMapping(SQL) 与图关系边(LiteDB graph-log.db) 跨介质，无单库原子性**——引用层按逐条幂等写透 + 最终一致 + 对账兜底处理（对齐领域公理 `DerivedDataAutoMaintenance`，见 `Agent/Axioms`），不再声称"单库原子写覆盖引用层"；未装配持久化时 `null`（逐条直写，兼容无持久化路径）。
- **`SetNodeStores` 失效重建**：注入节点存储后调用 `InvalidateRunner()`，使下一轮按当前节点存储重建运行器（避免缓存冻结）。
- **`FrameworkResourceGraphStage` 的 KeyMapping 选择**：`ctx.DataStore?.DataStore.Nodes` 存在 → `CreateNodeKeyMappings(nodeStore)`（存储收敛主路径）；否则 → `CreateSqliteKeyMappings(ctx.Config.Persistence)`（回落 SQL 兼容）。
- **`FrameworkResourceGraphStage` 装配引用守卫**：统一构造 `ReferenceGrounding`（目标 = graph-log.db 的 `ReferenceGraphCoordinator`），把节点库换为 `WriteThroughNodeStore`（写透白名单仅 `{node}`），并经 `SetReferenceGrounding(grounding, nodeStore)` 回注编排器（链路透传到 `NodeExecutionService`）；**不在** `BuildSqliteDataStore` 拆写透包装（该处无图存储句柄）。任务写透在 `FrameworkTaskPoolStage` 经 `RegisterTaskPool(grounding: ...)` 包 `WriteThroughTaskPoolStore` 装配。

---

## 五、执行链（节点运行时自动落三线）

`WorkflowRunner → RoundStepInvoker → NodeExecutionService`：

1. **入参解析（读模块 A/C）**：数据空间快照 + 全节点输出端口（模块 A 读取侧）→ `KeyProcessor.Mapper.MapBatch` 经 KeyMapping（模块 C）映射到 `ParameterKeys`，同时记录**溯源**（参数 ← 上游节点输出端口）。
2. **执行**：构造 `WfContext` → `WorkFlowMethodUsage.CallFunctionAsync(FuncName, ctx)` → 提取结果端口。
3. **节点存储（写模块 A）**：结果写入 `INodeOutputStore`（ur_nodes `NodeType=output`，NodeKey=`nodes.{wf}.{node}`，节点为存储基底，持久化）。
4. **KeyMapping → 节点库（写模块 C）**：`InputMapping/OutputMapping` 经 `IKeyMappingStore.SaveAsync(NodeInput/NodeOutput, wf.nodeKey, json)`，落为 `NodeType=keymapping` 节点（未装配节点库时回落 SQL `ur_keymappings`）。
5. **关系 → 图库（写模块 B）**：`ReferenceGrounding` 自动维护——节点资源镜像经写透（`INodeStore.PutAsync` 触发 `EnsureNodeMirrorAsync`）、`KeyMappingToNode` 边由 `NodeRelationDeriver.DeriveKeyMappingEdge`（有任一映射时）、`NodeToNode` 边由 `NodeRelationDeriver.DeriveNodeToNodeEdges`（依**溯源**）推导后经 `WriteRelationsAsync` 统一写入（对齐领域公理 `DerivedDataAutoMaintenance`，不再手写 `EnsureNodeAsync`/`AddRelationAsync`）。

**执行细节（对齐 `NodeExecutionService` / `RoundStepInvoker`）**：

- **入参解析优先级（`ResolveInputsAsync`）**：① 全节点输出端口 `{node}.{port}` 恒有；裸 `{port}` 仅在**尚未被任一节点输出占用**时加入（避免跨节点裸端口互覆盖，溯源随之填充保证 NodeToNode 边一致）→ ② 数据空间快照兜底，仅填未被节点输出占用的键。返回值 `NodeInputResolution(Parameters, Provenance)`，`Provenance` 为「参数键 ← (源节点, 源端口)」映射。
- **KeyMapping 合并**：`DataContainerBinder.MergeMappings(node.InputMapping, globalMapping)` 合并节点级与全局映射；`KeyProcessor.Mapper.MapBatch` 只补填 `parameters` 尚未覆盖的目标键。
- **执行与结果提取**：构造 `WfContext(parameters, data, executionId, nodeKey, workflowId)` → `WorkFlowMethodUsage.CallFunctionAsync(FuncName, ctx)` → `ExtractProps`：`ClineResult` 强类型定向（`ResultKeys` 全写同一值），其余按公开属性名反射。
- **双写输出**：结果扁平兼容写数据空间（原键，供后续步骤直读）；同时以节点输出端口写 `INodeOutputStore`（节点为基底）。
- **节点元数据持久化（`PersistNodeMetadataAsync`）**：KeyMapping 键 = `{workflowId}.{nodeKey}`，`InputMapping` → `SaveAsync(NodeInput,...)`、`OutputMapping` → `SaveAsync(NodeOutput,...)`（注入事务工厂时仅 **SQL 侧**包进同一 SQLite 事务，`CommitAsync` 后返回）；引用层自动落地——业务节点 `PutAsync(NodeType=node)` 经 `WriteThroughNodeStore` 写透同步 `wf.node` 图镜像，`KeyMappingToNode` 边由 `NodeRelationDeriver.DeriveKeyMappingEdge`（有任一映射时）、`NodeToNode` 边由 `DeriveNodeToNodeEdges`（依 `Provenance`）推导后经 `ReferenceGrounding.WriteRelationsAsync` 统一写入（**不手写 `EnsureNodeAsync`/`AddRelationAsync`**）。**图关系(LiteDB `graph-log.db`) 与 KeyMapping(SQL) 跨介质，无单库原子性，引用层逐条幂等写透 + 最终一致 + 对账兜底**；未注入则逐条直写。
- **步骤输入/输出映射阶段（`RoundStepInvoker`）**：执行前 `BindStepInputPhase`（从 Transient 读源键 → MapBatch → 补填 parameters，不写 StateCenter）；执行后 `BindStepOutputPhase`（结果 → 映射 → 写回 Transient，P3 拷贝语义）。

---

## 六、验证结果

| 构建目标 | 结果 |
|----------|------|
| `Agent.sln` | 0 错误 0 警告 |
| `CsharpPrj/MainProj.sln` | 0 错误 0 警告 |
| `WorkFlowMethod.csproj` | 0 错误 0 警告 |

> 节点库回迁 SQLite 后存储层验证：`StateManagement.Tests` 47/47（含快照图、任务池持久化）、`Agent.Persistence.Tests` 52/52（含 `SqliteNodeStore` 纯「键名+JSON」`ur_nodes` 的 NodeKey 幂等 / JSON 表达式索引（`json_extract`）过滤分页 / 时间戳随 Json 持久化 / 仅三列 schema、config 单库原子事务、数据驱动装配；`ur_resources` 相关用例已随下线移除）。

---

## 七、现状边界与后续

- **已接入**：模块 A（节点存储单元）、模块 B（图关系存储，两类）、模块 C（KeyMapping 节点化 `NodeType=keymapping`，未装配节点库回落 SQL）、跨模块链合并（`DefaultChainComposer`）；节点存储装配期 `InitializeAsync`（建图/建表）已收敛至 `NodeStorageStoreFactory`；`RoundStepInvoker` 实例化并集中模块 A 的 `NodeOutputStore` 构造；**引用自动落地守卫**（`ReferenceGrounding` + `NodeRelationDeriver` + `WriteThroughNodeStore` / `WriteThroughTaskPoolStore`）经 `FrameworkResourceGraphStage` / `FrameworkTaskPoolStage` 装配接线（节点/任务写透镜像 + 边推导统一写入，不再手写 `EnsureNodeAsync`/`AddRelationAsync`）。
- **装配边界（D3）**：节点存储仅在完整框架路径（`AgentFrameworkAssembler`）装配并 `SetNodeStores`；DB-only 路径（`DatabaseBootstrapper`/`DatabaseModule`）不再创建节点存储（无消费方，`DatabaseModule.NodeRelations/KeyMappings` 已移除）。
- **未接入**：`ConfigDeduplicationProcessor`（基于**标识符归一化**的跨链重命名去重）——依赖 `IChainNodeHandler`/`INodeRepository`/`INameGenerator` 默认实现，尚未定位装配；如需可接入 `NodeChainMergeFacade` 作为「去重阶段」。
- **后续可选**：跨轮复用上游节点输出的声明式组合。（`NodeToSnapshotTaskPool` 关联能力已按 SingleImplementation 收缩移除，未实现。）
