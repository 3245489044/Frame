# 数据驱动反应式 Agent 架构设计（双库 · 数据流即观测 · 叠加门面）

> **日期**：2026-08-10
> **定位**：架构设计文档——**模块化分两大板块**：**第一部分·基础设计理念**（愿景 / 设计原则 / 核心机制 / 架构基准）；**第二部分·实施步骤**（分层落地 / 分阶段实施 / 风险与验收 / 决策记录）。基础理念与实施步骤**彻底分离**：理念部分不讲"分几步做"，实施部分不复述"为什么这样设计"。
> **目标**：把 Agent 框架演进为**以数据库为中枢的反应式数据处理系统**——在指定数据区写入数据即自动触发 AI 处理、处理结果写回数据库、外部 API 完全基于数据处理实现；同时**保留全部现有 API 作为底座**，数据层为**叠加门面**，不删不改、可开关可回退。
> **前置依据**：`docs/Introduction/Universal/06_数据库使用逻辑.md`（双库设计理念）、`CsharpPrj/Share/UniversalReference/`（数据/关系分库、图引用层）、`Agent/docs/2026-08-09-Agent存储项目重构方案.md`（Agent.Persistence 收敛）、`2026-08-10-hybrid-scheduler-status.md`（非限定快照驱动循环）。

---

## 目录（两大板块）

> 本设计**模块化分两大板块**：基础设计理念与实施步骤**彻底分离**——理念部分不混入"分几步做"，实施部分不复述"为什么这样设计"。实施时以第一部分的设计为唯一依据。

### 第一部分 · 基础设计理念（What / Why）
| 节 | 内容 |
|----|------|
| §〇 | 架构基准：复用 `ComponentData` 构建 ECS 基础（映射 + §0.6 存储基础设施：循环外的存储后盾） |
| §一 | 背景与最终目的（数据库为中枢的愿景闭环） |
| §二 | 设计原则（不变式） |
| §三 | 总体架构（叠加门面分层） |
| §四 | 双数据库角色与同步 |
| §五 | 数据区模型（三区契约） |
| §六 | 反应式触发闭环（写入即触发） |
| §七 | 数据流观测体系（以数据替代日志） |
| §八 | 动态更新维护（数据驱动） |
| §九 | 使用效率（唯一量化门槛） |
| §十 | 外部 API 数据化适配器 |

### 第二部分 · 实施步骤（How / When）
| 节 | 内容 |
|----|------|
| §十一 | 分层落地（映射到现有模块） |
| §十二 | 分阶段实施（P0→P4） |
| §十三 | 风险与权衡 |
| §十四 | 验收标准（Done Definition） |
| §十五 | 决策记录与价值复审（缺陷修正 / 落地建议 / 结论 / ECS 价值评估） |

---

## 第一部分 · 基础设计理念（What / Why）

> **本部分回答"设计什么、为什么这样设计"**：愿景闭环、设计原则（不变式）、总体架构、双库机制、数据区模型、核心触发机制、观测体系、数据驱动维护、效率基准、外部适配，以及 ECS 架构基准（含 §〇.6 InternalStorage 内存聚拢点）。本部分**不含**分步实施清单。

| 节 | 板块 | 内容 | 性质 |
|----|------|------|------|
| §〇 | 架构基准 | 复用 `ComponentData` 构建 ECS 基础（映射）+ §0.6 存储基础设施：InternalStorage + SQLite（循环外的存储后盾） | 基准 |
| §一 | 背景与目的 | 现状、最终目的（数据库为中枢的愿景闭环）、关键约束 | 愿景 |
| §二 | 设计原则 | 叠加门面 / 数据即真相 / 单写入口 / 契约即扩展 / 效率为门槛 | 不变式 |
| §三 | 总体架构 | 外部世界 → 数据层（叠加门面）→ 现有 API 底座 | 架构 |
| §四 | 双库角色与同步 | SQLite（数据本体/事实源） vs 关系层（只读投影） | 机制 |
| §五 | 数据区模型 | 输入区 / 输出区 / 事件区三区契约 | 模型 |
| §六 | 反应式触发闭环 | "写入即触发"（write-as-trigger）核心机制 | 核心机制 |
| §七 | 数据流观测体系 | 以数据替代日志（`IObservabilitySink`/事件行） | 机制 |
| §八 | 动态更新维护 | Schema 版本化、配置即数据行、契约驱动、治理回流 | 机制 |
| §九 | 使用效率 | KPI 定义 + 效率化设计（唯一量化门槛） | 原则 |
| §十 | 外部适配器 | Cline / Guardrails / MCP / 观测 → 数据化门面 | 机制 |

---

## 〇、架构基准：复用 Modules/ComponentData 构建 ECS 基础（重大修订）

> **本设计的基础不再是"自建事件/outbox/触发机制"，而是复用 `CsharpPrj/Modules/ComponentData` 的 ECS 框架**（Entity-Component-System）。
> **ECS 是本设计原生的抽象层**——反应式循环（写入即触发 → 反应式管线 → 写回）整体运行在 ECS 抽象上；该框架已经内建"写入即触发 + 反应式处理 + 数据/关系分域"三大机制，无需自造。
> **循环边界**：专注于**数据库处理**的模块（InternalStorage / SQLite / 关系库）是循环**之外**的存储基础设施——只经**端口（PersistencePort）**在循环边界被调用（checkpoint / 提交 / 启动重建），**不介入循环内部，也不映射为 ECS 实体/组件**。以下为 ECS 化映射与架构基准，凡与本设计后续章节冲突处，以本节为基准。

### 0.1 复用 ComponentData 的现成能力

| ComponentData 组件 | 能力 | 对应本设计 |
|---|---|---|
| `ComponentDataStore` / `IComponentDataStore` | 键值存储 + `DataChanged`（`IObservable<ComponentDataChangedEventArgs>`）+ `ObserveKey`/`ObserveType<T>` | **数据本体 + "写入即触发"信号源**：`Set(key, value)` 即提交即发布，订阅者即触发 |
| `EcsComponentReactorPipeline` / `ReactorSystemBase` | 带优先级的反应式管线（`IEcsComponentReactor<T>`，`PriorityChain`） | **反应式 AI 响应**：组件数据变化 → 按优先级派发到 reactor |
| `EntityManager` / `EcsWorld`（`ECS.Core`） | 实体 + 组件（struct `IComponent`）+ System 管理 | **ECS 世界**：Agent 状态 = 实体 + 组件 |
| `RelationManager` / `RelationIndex` / `RelationKey` | 实体间关系（图边，前向/反向索引） | **数据关系层**（对应双库的"图"一侧） |
| `EcsContext` | World + Relations + Indexes + Notifications + 后台更新循环 | **非限定处理循环**：`SignalUpdate`/后台线程驱动 |
| `NotificationCenter` / `ComponentLifecycleManager` | 组件 Added/Removed 订阅，`ExecutionMode`（Sync/ThreadPool/UI） | **事件订阅 + 并发调度** |
| `CommandSystem` | 命令队列，`Update` 内消费 | **有序命令缓冲/背压** |

### 0.2 ECS 化映射（Agent 概念 → ECS）

| Agent 概念 | ECS 落点 |
|---|---|
| 输入区 / 输出区 / 事件区 | `ComponentDataStore` 的键（如 `input.*`/`output.*`/`event.*`），写键即触发 |
| SchedulerState / 确定性核心 / 执行状态 | 实体（Entity）+ 组件（Component），如 `BudgetComponent`/`TokenComponent`/`RoundComponent`/`StatusComponent` |
| 运行观测（时间线/决策迹） | 事件组件 + `DataChanged` 流 + 关系索引 |
| 数据/关系持久化 | **不在 ECS 映射内**——由循环边界外的存储层（SQLite 数据本体 + 关系库）经 `PersistencePort` 落盘/重建；存储不介入循环内部、不映射为 ECS 实体/组件（见 §〇.6 边界） |
| 外部 API 适配器 | ECS System：读组件 → 处理 → 写组件（写即触发下游） |
| AI 响应（LLM/混合调度） | reactor/system 订阅组件变化 → 转调现有 `AgentRunner.RunAsync` → 写回循环内组件（写即触发）；**落库由框架 PersistencePort 在循环边界决定，非 AI** |

### 0.3 与"双库"的关系（修订——关系与数据彻底分库）

> **模块化决策（用户确定）：关系管理与实际数据储存分离，使用两个数据库。** 数据本体与关系管理不合并、各自独立持久化。

- **数据本体**（组件/事实）：ECS 内存世界承载，经持久化端口落 **SQLite**（`agent.db`）——**库一**。
- **数据关系**（`RelationManager` 图边）：运行时由 `IUniversalReferenceManager`（`ReferenceManagerBase` 内存关系层）承载，持久化经 `PersistentUniversalReferenceManager` 落 **JSON 文件**（或升级 `GraphDatabaseStorage`）——**库二**，**不并入 SQLite 的关系表**（代码级映射见 §0.5）。
- ECS 世界是**运行时唯一真相**；SQLite 是**数据本体的持久化落盘**；`IUniversalReferenceManager` 是**关系管理的独立承载**（内存 + JSON 落盘，非独立图数据库进程）。二者模块化分离：数据 CRUD 走库一，关系/图查询走库二。

### 0.4 本设计后续章节的适用说明

- §五"数据区"：即 `ComponentDataStore` 的键命名空间，无需新表结构定义（表 = ECS 持久化的序列化形式）。
- §六"写入即触发"：由 `ComponentDataStore.DataChanged` + `EcsComponentReactorPipeline` **现成实现**，无需自建 outbox/事件。
- §四"双库"：数据/关系分域由 ECS 原生（组件 vs 关系索引）承载，**持久化始终用两个独立数据库**（组件→SQLite、关系→图库），不合并。
- 剩余真正要做的，是**"InternalStorage 内存存储后盾 ⇄ SQLite 长期落盘后盾"的持久化端口（PersistencePort）**与**存量 API 到 ECS 的适配**（见 §〇.6 与 §十五修订）。

### 0.5 复用 UniversalReference 的现成能力（关系/图层 + 反应式 + 上下文）

> 关系/图层、反应式通知、上下文传播、状态机、批次、规则路由这些基础设施**已在 `CsharpPrj/Share/UniversalReference/` 中实现**，本设计直接复用而非自造。以下为**代码级映射清单**（文件 = 实际实现，非愿景）。其中 `IUniversalReferenceManager` 关系层 + `ObservableReferenceManager` 反应式通知 + `EventBus` 事件总线，恰好覆盖本设计"库二 + 写入即触发 + 观测事件"三大需求。

| 设计需求 | 实际代码（文件） | 关键 API / 能力 |
|---|---|---|
| **关系/图层（库二）** | `IUniversalReferenceManager`（`Core/Interfaces/IUniversalReferenceManager.cs`） | 资源 CRUD + 引用增删查；`GetReferencesAsync(includeIncoming)`、`GetDependenciesAsync(maxDepth)`、`GetDependentsAsync(maxDepth)`、`FindPathsAsync`、`FindShortestPathAsync`、`GetResourcesByTypeAsync`、`CleanupNonExistentResourcesAsync`、`ContainsResource` |
| 关系层内存基座 | `ReferenceManagerBase`（`Core/Managers/ReferenceManagerBase.cs`） | 抽象基类：`Dictionary<string,Resource>` + `List<ReferenceRecord>` + `lock`；DFS 实现依赖/被依赖/路径/最短路径 |
| 关系层内存实现 | `DefaultUniversalReferenceManager`（`Core/Managers/`） | `InitializeAsync` 空操作，纯内存 |
| 关系层持久化实现 | `PersistentUniversalReferenceManager`（`Core/Managers/`） | JSON 文件落盘（`resources.json`/`references.json`，PascalCase + 缩进 + `DynamicObjectConverter` 修复元数据值类型）；每次变更全量 `SaveToDiskAsync` |
| 关系层工厂 | `UniversalReferenceManagerFactory`（`Core/Interfaces/`） | `SetDataDirectory` + `Create()`（有目录→持久化，无→内存）；`CreatePersistent(dir)` |
| **写入即触发（关系侧）** | `ObservableReferenceManager`（`EventDriven/ObservableReferenceManager.cs`） | 装饰器包 `IUniversalReferenceManager`；`OnResourceAdded/Updated/Removed` 事件在写资源后触发——关系侧"写即触发"现成实现 |
| **通用事件总线（观测）** | `EventBus`（`Core/EventBus.cs`） | `Subscribe/SubscribeSync/Unsubscribe`、`PublishAsync`（并行）、`PublishSequentialAsync`（串行）；`ConcurrentDictionary<Type,List<Delegate>>` |
| **上下文传播（并发模型）** | `ContextManager<T>`（`Core/ContextManager.cs`） | `AsyncLocal<T>` 跨异步流传播；`Enter/Exit/Execute/ExecuteAsync` 嵌套上下文 |
| 状态机 | `StateMachine<TState>`（`Core/StateMachine.cs`） | 枚举状态机；`Register/TryTransitionTo(条件)/OnStateChanged/CountByState/GetIdsByState` |
| 批次管理 | `BatchManager`（`BatchIntegration/BatchManager.cs`） | 单例；组合 `ContextManager<string>` + `ConcurrentDictionary`；`Register/Complete/Fail/Cancel/CleanupBatch`、`GetSummary`；**注：`CreateBatchPipeline` 已作死代码移除，收敛到 `Modules.Pipeline`** |
| 规则路由（ReactiveTrigger 候选） | `RelationshipDispatcher<TInput,TOutput>`（`Reference/Relationship/RelationshipDispatcher.cs`）+ `IRuleSet`/`IMatchingStrategy`/`ITargetResolver`/`RoutingContext` | `DispatchAsync`（单播）/`BroadcastAsync`（广播）；`InMemoryRuleSet`/`BestMatchStrategy`/`DelegateTargetResolver`；表达式规则（递归下降解析） |
| 资源模型 | `Resource`/`ResourceIdentifier`（`Core/Models/`） | `{ResourceType, Id, Name, Location}`；`GetUniqueKey()` = `"{ResourceType}:{Id}"`（`UniqueKeySeparator=":"`） |
| 引用类型枚举 | `ReferenceType`（`Reference/Models/Enums.cs`） | `Dependency/Includes/Uses/Extends/Requires/Other` |
| Agent 侧端口 | `IResourceReferenceStore`（`Agent/src/Agent.Persistence/Abstractions/`） | 复用 UniversalReference 的 `Resource`/`ResourceIdentifier`；引用类型用 `string` |
| **存量桥接（已存在）** | `UniversalReferenceResourceReferenceStore`（`Agent/src/Agent.Persistence/Core/`） | 把 `IUniversalReferenceManager` 适配为 `IResourceReferenceStore`；可选注入 `IReferenceTypeRegistry` 写入前 fail-fast（未声明/已废弃→`InvalidOperationException`） |

> **重要修正（对齐实际代码）**：
> 1. 设计中的"库二 = 独立图数据库"**对当前实现并不成立**——`IUniversalReferenceManager` 是**内存关系层**，持久化经 `PersistentUniversalReferenceManager` **落 JSON 文件**（非图数据库进程）。真正的图数据库路径是 Storage 层 `GraphDatabaseStorage` / `IGraphReferenceManager`（`DataAccess/GraphStorage/GraphStorageRepositories.cs` 的 `BaseGraphRepository` 系列使用 `IGraphReferenceManager`，与 `IUniversalReferenceManager` 是**两套图抽象**）。落地时二选一：轻量走 `PersistentUniversalReferenceManager`（JSON），重图查询走 `GraphDatabaseStorage`。
> 2. §四"关系分库"的意图保留，但"库二"的实际承载 = `IUniversalReferenceManager`（内存 + JSON 落盘）或升级 `GraphDatabaseStorage`，**不是新建的独立图数据库进程**。

### 0.6 存储基础设施：InternalStorage + SQLite（循环外的存储后盾）

> **修订（本用户决策）**：**ECS 是原生抽象层**——反应式循环整体运行在 ECS 抽象上。**InternalStorage 与 SQLite 是专注于数据库处理的存储基础设施，位于循环边界之外**：二者只经**一条持久化端口（PersistencePort）**在循环边界被调用（checkpoint / 提交 / 启动重建），**不介入循环内部，也不映射为 ECS 实体/组件**；其余代码不得越过端口直连另一边。

**定位（修订）**：InternalStorage 是通用运行时内存存储管理框架（csproj 描述：覆盖并统一现有 4 套独立的内存存储体系），作为循环的**内存存储后盾**（三层键值 + 分层快照 + 索引 / 关系 / 映射原语）；SQLite 是**长期落盘后盾**。二者都是**基础设施**，不是循环的触发源，也不是 ECS 的一部分——**循环的触发 / 反应式语义全部由 ECS 抽象承载（§〇.1），存储模块只负责在边界落盘与重建**。

**项目落位（模块化构建）**：上述存储基础设施（内存后盾 + 落盘后盾 + `PersistencePort` 适配器）**单独构建为一个项目**，置于 `CsharpPrj\Operation` 下——**完全 Agent 无关**（不引用任何 Agent 项目、不知晓 Agent 概念）；**Agent 项目（如 `Agent.Persistence` / `Agent.Core`）通过 `ProjectReference` 引用**该 Operation 项目（**单向依赖：Agent → Operation 存储项目**；存储项目不反向依赖 Agent）。由此数据库本身与 Agent 彻底解耦：Agent 只依赖 Operation 提供的存储抽象，存储实现独立演进、可被任意宿主复用。

**循环外的存储基础设施（两后盾 + 一端口）**：

| 设施 | 唯一入口 | 职责 | 边界 |
|---|---|---|---|
| **内存存储后盾** | `IStorageEngine` + `IStorageCell` + `IStorageRegistry`（InternalStorage） | 循环外承载内存数据：状态、组件、索引、关系边、会话作用域、ID | 只经 PersistencePort 在边界被读写，不向循环内部暴露 |
| **长期落盘后盾** | SQLite（`agent.db`） | 跨进程 / 重启保留的数据：事实、事件、快照、checkpoint、配置 | 唯一写入口 + 事实源，循环不直连 |
| **耦合通道** | `PersistencePort`（新增契约） | 序列化 cell 的 Config/Session 层 → SQLite；启动从 SQLite 重放重建 cell | 单向：内存 ⇄ SQLite，仅边界调用 |

**StorageLayer 三分层 = 内存/长期分离的天然切分规则**（InternalStorage 带给本设计最有价值的边界）：

| 层 | 语义 | 归属 | 持久化 |
|---|---|---|---|
| `Config` | 静态声明 / 配置（配置即状态，对齐 §八 `ur_config`） | 长期 | ✅ 序列化进 SQLite |
| `Session` | 运行时状态（快照语义，`SnapshotManager` 只快照本层） | 内存为主 | ✅ 按快照 / 增量经 PersistencePort 落 SQLite（可重建） |
| `Transient` | 瞬态 / 临时（TTL 自动回收） | 纯内存 | ❌ 永不落盘 |

**存储基础设施内部能力清单（循环外，仅供 PersistencePort 使用——非 ECS 映射，不介入循环）**：

| 能力 | 存储层承载 |
|---|---|
| 内存键值 / 状态 / 组件承载 | `StorageCell.Set<T>/Get<T>`（Session 层） |
| 写入即触发 | **由 ECS 抽象层承载（§〇.1 `ComponentDataStore.DataChanged`），非存储模块**——存储不得成为循环触发源 |
| 循环内 ECS 世界的落盘载体（基础设施内部） | `ECSStorage`（entity↔archetype、chunk 列、entity 版本）+ `EcsSessionManager.SyncComponentData`（组件 ⇄ cell）——仅供 PersistencePort 在边界读写，不向循环暴露 |
| 会话 / 线程 / round 作用域隔离 | `StorageEngine.CreateSession(scope)` + `StorageCell.Parent` 级联 |
| 类型安全注册 / 唯一 ID | `StorageRegistry.Register/Resolve` + `NextId` |
| 内存关系图边（运行时） | `StorageRelationExtensions`（`relations:*` 前向 / 反向集合）→ 经 PersistencePort 投影到长期 `IUniversalReferenceManager` |
| 双向映射（threadId↔leaseId、session↔round 等） | `StorageBidirectionalExtensions`（MapForward / GetBackward） |
| 索引（按属性定位实体） | `StorageIndexExtensions`（唯一 / 非唯一） |
| 状态快照 / 恢复 / 差异 | `SnapshotManager` / `LayeredSnapshot` / `CalculateDiff` |

**PersistencePort 契约（新增）**：

```
Persist(session)  : 读 cell 的 Config/Session 层 → 序列化（全量快照 或 SnapshotManager.CalculateDiff 增量）→ SQLite 事务写
RebuildCell(db)   : 启动 / 恢复时从 SQLite 读事实 / 快照 → 重建 StorageCell（幂等，可全量可增量重放）
```

- 触发时机：checkpoint（对齐 `SyncProtocolTriple` Commit→Compensate→Observe）与崩溃恢复。
- 治理：内存侧遵守 `StateRootSingleSource`（单 `IStorageEngine` 根）+ `DeterministicCoreDisjoint`（确定性核心单一写者，不做越权写预算）；长期侧遵守 SQLite 唯一写入口。
- **写回数据库 = 框架决定，非 AI 决定**：AI 只写循环内组件（内存）；落库（`Persist(session)`）由框架在 commit / checkpoint 边界**确定性执行**，AI 不直接触发任何数据库写入（对齐 `DeterministicCoreDisjoint`）。
- **Transient 层永不进入 PersistencePort**，避免脏数据落盘。

**与既有决策的关系**：
- 不替代 `ComponentData` ECS，也不反客为主：**ECS 是原生抽象层（循环），InternalStorage / SQLite 是其背后的存储基础设施（循环外）**。循环触发 / 反应式处理全走 ECS；存储模块经 `PersistencePort` 在边界落盘 / 重建——**不介入循环内部、不映射为 ECS 实体/组件**。
- **数据库与 Agent 完全解耦（使用层实现）**：数据库（SQLite / InternalStorage / 关系库）**不是 Agent 的一部分**——Agent 核心只声明 `PersistencePort` 抽象（端口），**零数据库依赖**；具体落库实现由**使用层（消费方 / 宿主）**提供并注入（见 §三）。
- "数据/关系双库"叙事保留，但**内存关系先聚拢到 cell**（`StorageRelationExtensions`），长期关系聚拢到 `IUniversalReferenceManager`，切分更干净。
- 文档 §0.4 / §15 自认的剩余真活——"内存 ECS 世界 ⇄ SQLite 同步通道"——**具体化为"InternalStorage 会话 ⇄ SQLite 的 PersistencePort"**。

---

## 一、背景与最终目的

### 1.1 现状

- Agent 主循环由**非限定快照驱动引擎**（`SnapshotDrivenLoopEngine`）驱动，`SchedulerState` 为唯一持久根；状态变更 = LLM + 计算混合调度。
- 持久化收敛到**单一 SQLite**（`agent.db`，`ur_resources/references/checkpoints/writes/snapshots` 五表）；"数据/关系分库"的双库设计（数据本体 → SQLite，关系 → 图数据库）被合并进单库。
- 运行观测依赖传统日志（`AgentLog.Log`，数十处埋点），不可结构化查询、不可关联、不可聚合。
- 外部交互经 `AgentRunner.RunAsync` 显式调用（Cline / MCP / Guardrails 作为执行器）。

### 1.2 最终目的（愿景）

> **数据库 = 系统的中枢与唯一事实源**：在指定区域写入数据 → 自动反应式触发 AI 处理 → AI 把处理结果写回数据库 → 一切外部 API 都退化为"读写数据的适配器"。

核心闭环：

```
外部/任何来源
   │ 写入数据到"指定输入区"
   ▼
【数据库】输入区（ur_inputs / 事件表）      ← 唯一事实源 + 触发信号源
   │ 反应式触发（事件订阅 / 变更通知）
   ▼
【Agent 循环】非限定快照引擎 + 混合调度      ← 读快照/上下文，AI 处理
   │ 处理结果结构化写回
   ▼
【数据库】输出区 / 快照 / 资源 / 引用        ← 写回，形成闭环
   │ 外部 API 只做：写输入、读输出、登记事件
   ▼
外部消费方
```

### 1.3 关键约束（用户明确）

> **已有 API 不需要删除**。数据层是**叠加在现有 API 之上的门面/总线**，不是替代品——只读现有 API 暴露的端口，把"调用 API"包装成"读写数据"，不改、不删、可回退。

---

## 二、设计原则（不变式）

1. **叠加门面**：现有 API 是引擎、是底座；数据层只做"数据读写 ⇄ API 调用"的桥接，不复制业务逻辑，关闭数据层系统仍可原 API 直接可用。
2. **数据即真相**：运行时事实只存在于 DB；日志降级为"DB 写入失败时的兜底"，不承担观测职责。
3. **单写入口 + 可重建投影**：SQLite 是唯一事实源与唯一写入口；图库只是从 SQLite 派生、可幂等重建的只读投影，永不作权威。
4. **契约即扩展**：每个数据/事件项都有一份 `DataContract`（复用现有 `DataContractRegistry` / `DataContractBuilder`），新增观测点 = 注册契约，零改动生产/消费方。
5. **效率为门槛**：任何结构决策（建表、索引、投影、压缩、保留、批写）都必须以效率 KPI 通过为前置，禁止"为设计而设计"。
6. **数据库与 Agent 解耦（使用层实现）**：数据库（SQLite / InternalStorage / 关系库）**完全与 Agent 无关**——Agent 核心只声明 `PersistencePort` 抽象（端口），不感知任何数据库实现；数据库由**使用层（消费方 / 宿主）**实现并注入，Agent 对数据库**零依赖**。

---

## 三、总体架构（分层，使用层实现数据库 + Agent 框架底座）

```
【外部世界】
   └─ 数据适配器（只读写"数据区"，不碰业务 API）
        ▼
【使用层（宿主 / 消费方，与 Agent 完全解耦）】────── 数据库在这里实现
   · 数据库实现：SQLite（agent.db）+ InternalStorage + 关系库 —— 完全 Agent 无关，Agent 不知其存在
   · PersistencePort 适配器：实现 Agent 声明的抽象，落库 / 重建
   · 触发 / 编排：使用 Agent（写输入区 → Agent 反应式处理 → 读结果落库）
        │  内部只调用（不绕过、不复制）Agent 框架 / 现有 API
        ▼
【Agent 框架（核心，不含数据库，仅声明 PersistencePort 抽象）】
   · ECS 反应式循环（原生抽象层）：写即触发 → 反应式管线 → 写回组件
   · 现有 API 层（底座，原样保留）：AgentRunner.RunAsync / AgentOrchestrator
   · IPersistenceStore / ICheckpointStore / ISnapshotStore / DataContract / DataBus / IDataSpace
   · IAgentExecutor（Cline / MCP / Guardrails）· 非限定引擎 + 混合调度（SchedulerState 根）
```

**关键点**：① **Agent 框架不感知数据库**——只声明 `PersistencePort` 抽象（端口）；② **数据库（SQLite / InternalStorage / 关系库）是「使用层」实现，与 Agent 完全解耦**；③ 使用层只做"数据读写 → 转调 Agent 框架 / 现有 API → 结果写回数据"的编排，不复制业务逻辑。

**项目落位（模块化构建）**：「使用层」**单独构建为一个项目，置于 `CsharpPrj\Operation` 下**——完全 Agent 无关（不引用任何 Agent 项目）；**Agent 项目通过 `ProjectReference` 引用**该 Operation 项目（单向：Agent → Operation 存储项目，存储项目不反向依赖 Agent）。

---

## 四、双数据库角色与同步（恢复设计理念）

### 4.1 角色分库

| 库 | 角色 | 内容 | 权威 |
|---|---|---|---|
| **SQLite**（`agent.db`） | **数据本体 / 事实源** | 数据区（输入/输出/事件）、快照、checkpoint、资源、配置、策略 | 唯一 truth + 唯一写入口 |
| **关系层**（`IUniversalReferenceManager`，内存 `ReferenceManagerBase` + `PersistentUniversalReferenceManager` JSON 落盘；重图查询可升级 `GraphDatabaseStorage`/`IGraphReferenceManager`） | **数据关系 / 计算层** | 引用边、因果链、血缘、决策迹、依赖图 | 只读投影，可重建 |

对齐 `06_数据库使用逻辑.md` 原始设计："SQLite 作为持久化存储，图数据库作为内存/关系计算层；从 SQLite 加载→图分析→结果回写 SQLite"。当前把关系合并进单一 SQLite，本方案将"关系"从事实表拆出、放回关系层（作为投影，不改变事实源）。代码级映射见 §0.5。

> **（ECS + 实际代码修订）**：本节的"数据本体/关系"在运行时由 ECS 承载——**组件（数据）** 与 **`RelationManager`（关系图索引）**；持久化分两库：组件落 **SQLite**（库一），关系落**库二**（`IUniversalReferenceManager`，经 `PersistentUniversalReferenceManager` 落 JSON；重查询升级 `GraphDatabaseStorage`），**不合并、不写入 SQLite 的关系表**。分域是 ECS 原生结构，持久化同样分库。详见 §〇 0.3 / 0.5。

### 4.2 数据流闭环

```
写入（唯一入口，追加+快照）──▶ SQLite（事实/事件）──▶ 增量 reindex ──▶ 图库（关系投影）
       ▲                                                              │
       └────────── 观测/查询/决策（读 SQLite 事实 + 图库关系） ◀────────┘
```

### 4.3 一致性策略（避免"双真相"）

- SQLite 提交即 truth；图库投影异步/惰性重建，允许短暂滞后。
- 投影重建幂等（可全量可增量），崩溃后可一键重投影，不丢失事实。
- 复用现有 `SyncProtocolTriple`（Commit→Compensate→Observe）与 `ur_checkpoints/ur_writes` 保证写侧原子与崩溃恢复。

---

## 五、数据区模型（三区契约）

数据层定义三个数据区，全部复用 `DataContract`/`IDataSpace` 注册与承载。

| 数据区 | 表 | 用途 | 生产者 | 消费者 |
|---|---|---|---|---|
| **输入区** | `ur_inputs` | 外部写入待处理任务/数据；写入即触发信号 | 外部适配器 | 反应式触发器 → Agent 循环 |
| **输出区** | `ur_outputs` | AI 处理结果、完成状态 | Agent 循环（写回） | 外部适配器 |
| **事件区** | `ur_events` | 运行时结构化事件（观测 + 触发信号源） | `IObservabilitySink` | 观测/报告/审计 |

**数据区契约示例（注册式）**：

```
DataContract("input.zone",   typeof(...),      DataDirection.Seed,    Scope=Root, Consumer="ReactiveTrigger")
DataContract("output.zone",  typeof(...),      DataDirection.Harvest, Producer="AgentLoop", Consumer="external")
DataContract("event.stream", typeof(RunEvent), DataDirection.Harvest, Producer="IObservabilitySink")
```

---

## 六、反应式触发闭环（核心新增）——"写入即触发"

> **核心机制 = 数据写入的过程本身就是触发**（write-as-trigger）。
> 数据库是 **append-only 事件库**：一次数据写入 = 原子提交一个事件 = 触发信号。触发不是"轮询/检测写入"，而是**写入这一动作本身即提交即发布**。观测与触发同源——同一行既是事实记录又是事件。
> 现有 `RunAsync` 是显式调用，本方案让"输入区写入"经此机制自动转调现有 `RunAsync`。

```
写入者（外部 / 任意来源）
   │ 写 ur_inputs：原子提交 = 发布事件 = 触发信号（写与触发同一步，无二次检测）
   ▼
事件提交 → 反应式订阅者被唤醒 → 转调 AgentRunner.RunAsync(input, timeout, ct)   // 复用现有 API，非复制
   ▼
AxiomLoopResult → 写循环内输出组件（写即触发，链式反应）→ 框架 PersistencePort 在循环边界落库 ur_outputs（框架决定，非 AI）
   ▼
写回本身就是下一个写入 → 触发下一个消费方（链式反应）
```

### 6.1 "写入即触发"的四条设计要点

1. **原子性**：事件行（含输入内容 + 状态标记）在**一次事务**中提交；提交即发布，不存在"写了一半/触发了没记"的中间态。
2. **同源**：触发与观测共享同一事件行，**无第二通道**——低延迟（写入即触发）与持久化（写入即记录）天然合一。
3. **可重放**：订阅者处理失败不影响已提交的事件行；可依行重放/重试（配合幂等键去重，见 §15.2 ⑤）。
4. **写回数据库 = 框架决定，非 AI 决定**：AI 只把处理结果写入**循环内的 ECS 组件**（写即触发，链式反应）；**何时落库（写回 SQLite / 关系库）由框架在循环边界决定**（commit / checkpoint 时经 `PersistencePort` 落盘），AI **不直接触发任何数据库写入**——落库是确定性的框架动作，不依赖 AI 的"是否保存"判断（对齐 `DeterministicCoreDisjoint`：确定性写入只归框架，LLM 提议只改策略/执行，不碰持久化）。

### 6.2 触发器机制（可配置）

- **唤醒方式**：进程内事件订阅（同一进程，写入提交后同步/异步唤醒）为主；跨进程可用 outbox 派发器或轻量订阅。**不采用"轮询 DB 表"作为主机制**（避免检测延迟与表扫描成本）。
- **现成实现**：关系侧"写即触发"直接复用 `ObservableReferenceManager` 的 `OnResourceAdded/Updated/Removed` 事件；通用事件发布复用 `EventBus`（`PublishAsync` 并行 / `PublishSequentialAsync` 串行）；规则化路由可复用 `RelationshipDispatcher`（`IRuleSet`+`IMatchingStrategy`+`ITargetResolver`）做"输入区→handler"派发（§0.5）。ECS 侧数据变化触发仍由 `ComponentDataStore.DataChanged` + `EcsComponentReactorPipeline` 承载。
- **并发与背压**：订阅者队列有界缓冲；处理不阻塞写入者（写入成功即返回，处理异步推进）。
- **失败不阻断**：处理失败保留事件行 + 状态字段（pending/failed），可重放，不阻塞后续写入。
- **不绕过现有 API**：触发闭环内部完全调 `AgentRunner.RunAsync`，不改主循环。

---

## 七、数据流观测体系（以数据替代日志）

### 7.1 现状问题

观测全靠 `AgentLog.Log`（数十处：`RoundStateMapper_Seed/Harvest`、`RoundStepInvoker`、`WorkflowRunner`、`DecisionPoint`、`AgentRunner` 各阶段、`LlmStateDirector`、`DefaultBoundaryFramework`……），读日志文本，不可查询/关联/聚合。

### 7.2 方案：日志点升级为事件行

新增观测端口 `IObservabilitySink.Emit(RunEvent)`，逐步收敛现有 `AgentLog.Log` 为事件（仅新增，日志保留兜底）。

- **事件 schema（注册式契约）**：`RunEvent{ ThreadId, SessionId, RoundId, LeaseId, EventType, Phase(execute|next|budgetTerminate), Stage, PayloadJson, CreatedAt, Version }`。
- **事件表 `ur_events`（append-only）**：每条日志点 → 一条结构化事件行（批量异步写入）。
- **映射关系（表→图库边）**：`event→round`、`round→decision`、`decision→strategy`、`executor→status`、因果链。

### 7.3 可观测视角（全部是数据查询）

- **时间线**：某 `thread/session/round` 完整事件序列（替代读日志）。
- **单轮报告**：`execute→next` 全阶段耗时、决策词、评估、token 用量。
- **决策迹**：`budgetTerminate→TS/TO/TM`、`switch_strategy` 的因果链。
- **效率指标**：每轮延迟 / token 成本 / 资源消耗，直接 SQL 聚合。

### 7.4 兜底

`AgentLog` 保留为 DB 写入失败时的 fallback，但不再是观测源。

---

## 八、动态更新维护（数据驱动）

1. **Schema 版本化 + 幂等迁移**：新增 `ur_schema_version` 表 + 迁移链；图库投影在 schema 演进时可全量重建一次。
2. **配置/策略即数据行**：`ur_config` / `ur_strategy` 表，把阈值、策略、`ExecutorHint`、PromptPolicy、熔断参数做成行 + 热加载——改数据库即改框架行为，无需部署（对齐 `DataContractRegistrar` "一处声明全链路生效"）。
3. **契约驱动扩展**：新增数据/事件 = 注册契约 + 迁移，生产/消费方零改动。
4. **治理回流**：观测数据（效率指标）反向驱动，把"某策略 token 成本高"这类事实写回配置/策略建议表，形成"数据→行为"闭环。

---

## 九、使用效率：唯一基准

### 9.1 KPI 定义

| 指标 | 含义 |
|---|---|
| 触发→处理→回写端到端延迟 | 写输入区 → AI 处理 → 结果落输出区的总耗时 |
| 单轮延迟 | execute+next+budgetTerminate 端到端耗时 |
| 每轮 token 成本 | Cline token 用量聚合（`TokenUsage` 已有） |
| 事件写吞吐 | 事件入 SQLite 速率 / 对主循环拖累 |
| 查询读延迟 | 观测/报告查询延迟 |
| 图投影重建成本 | reindex 耗时（全量/增量） |
| 存储增长率 | 事件/快照增长 + 保留/压缩后净值 |

### 9.2 效率化设计（以 KPI 为门槛）

- **异步批写**：事件进有界缓冲队列，批量事务落 SQLite；失败不阻断主循环（降级丢事件或进 fallback）。
- **append-only + 索引 + 保留/压缩**：事件表只增；按 round/时间分区，`CheckpointReducer` 做 compaction；旧事件归档到 `ur_resources` 或冷区。
- **投影增量**：图库只重建增量事件，不做全量（除非 schema 变更）。
- **读模型缓存**：高频观测报告用物化读模型，避免每次重扫全表。
- **基准对照**：对比 harness——"日志观测 vs 数据流观测"开销差、事件写入对主循环拖累，作为是否保留某结构的裁决依据。

---

## 十、外部 API 数据化适配器

外部不再调"业务 API"，只通过数据层读写。

| 现有执行器 | 数据化门面 |
|---|---|
| Cline（`ClineAgentExecutor`） | 输入区写入任务 → 触发 → 结果写输出区；内部复用 `IAgentExecutor` |
| Guardrails（`guard_input/output`） | 输入/输出区的安全过滤适配器 |
| MCP（`mcp_call_tool/list_tools`） | 工具调用/列表的数据适配器 |
| 观测（`AgentLog`） | `IObservabilitySink` 事件化 |

**不做**：删除或绕过现有 API；执行器仍是底层，只是外部经数据区间接触发。

---

## 第二部分 · 实施步骤（How / When）

> **本部分回答"怎么落地、分几步"**：分层落地（映射到现有模块）、分阶段实施（P0→P4）、风险与权衡、验收标准（Done Definition），以及决策记录与价值复审（§十五）。本部分**不复述**设计理念，只给可执行的落地清单与评审；实施时以第一部分的设计为唯一依据。

| 节 | 板块 | 内容 | 性质 |
|----|------|------|------|
| §十一 | 分层落地 | 各层（InternalStorage/Contract/Persistence/Core/State/Axioms/Observability）改动映射 | 落地清单 |
| §十二 | 分阶段实施 | P0 双库基底 → P1 数据区+触发 → P2 事件化观测 → P3 数据驱动维护 → P4 效率治理 | 实施计划 |
| §十三 | 风险与权衡 | 写放大 / 双真相 / 迁移 / 增长 / 改动面 / 触发器复杂度 的缓解 | 风险评估 |
| §十四 | 验收标准 | Done Definition（API 保留、数据即触发、观测=查数据、可回退、KPI） | 验收清单 |
| §十五 | 决策记录与复审 | 缺陷修正建议（15.1–15.2）、落地建议（15.3）、结论（15.4）、ECS 价值复审（15.5） | 评审/附录 |

---

## 十一、分层落地（映射到现有模块）

| 层 | 改动（全部叠加） | 实施状态（2026-08-10） |
|---|---|---|
| **Operation/DataStoreDriven**（新建，Agent 无关使用层） | 数据存储驱动框架：`Abstractions`（`IDataStore`/`IPersistenceStore`/`IPersistencePort`/`IZoneStore`/`ICheckpointStore`/`ISnapshotStore`/`IResourceReferenceStore`/`IConfigStore`/`ITransaction`/`IDbCommandExecutor`）+ 后端（`Sqlite/` 三区+骨干+config、`LiteDB/`、`Local/`、`Graph/`）+ 门面（`PersistenceStoreFacade`/`SqliteDataStore`/`FacadeDataStore`）+ 配置（`PersistenceOptions`/`ConfigBinder`/`Validator`）+ 解析（`PersistenceProviderFactory`/`DataStoreProviderFactory`）+ 内存占位（`InMemoryZoneStore`/`InMemoryConfigStore`/`SnapshotPersistencePort`）。内存后盾复用 `InternalStorage`；`SqliteDataStore`/工厂支持 `sharedBackbone` 复用（单一写入口） | ✅ 已实现 |
| **Agent.Persistence**（收敛为薄适配层） | 通用数据库处理已下沉 DataStoreDriven；本层仅保留 `DataStoreEngine`（InternalStorage 内存 ⇄ IDataStore 落盘，PersistSessionAsync/RebuildAsync）+ `Core/SnapshotSerializer`（AxiomContext↔快照） | ✅ 已实现 |
| **Agent.Contract** | 新增观测契约 `RunEvent`/`EventType` + `IObservabilitySink`（`Observability/`） | ✅ 已实现 |
| **Agent.Core** | 新增 `DataStream/`：`ReactiveTrigger`（写输入→RunAsync→写回输出，幂等+有界并发）+ `EventSink`（有界通道+批写→事件区）+ `ReactiveZoneBridge`（ECS 写即触发信号源）；`AgentOrchestrator` run 级事件（`SetObservabilitySink`）+ P3 热读（`SetConfigStore`/`LoopConfigHotReload`）；`AgentLog.Log` 埋点收敛为 `Emit(RunEvent)` | ✅ run 级事件已收敛（`AgentOrchestrator.RunAsync` 统一发出）；散落静态类埋点按「仅新增、日志保留兜底」延后 |
| **Agent.Launcher**（组合根） | 新增 `DataStoreHosting`（装配 `IDataStore`+`DataStoreEngine`，可选 `RegisterReactive` 装配 ReactiveTrigger/EventSink）；`AgentHost`/`AgentLaunchStages` 统一接线（`EnableReactiveTrigger` 开关，默认关，可回退） | ✅ 已实现 |
| **Agent.State** | `SchedulerState` 事件化；会话/线程/租约相关键关联（`threadId/sessionId/roundId/leaseId`） | ○ 待办 |
| **Agent.Axioms** | `LlmStateDirector`/`TransitionGuard`/混合调度决策写事件；`DataContractRegistrar` 扩观测契约与配置表播种 | ○ 待办 |
| **Agent.Observability**（新增可选） | 读取模型/查询/报告/效率指标，只消费 DB，不含业务 | ○ 待办 |

---

## 十二、分阶段实施

| 阶段 | 内容（全部叠加，不动底座） | 验收 | 实施状态（2026-08-10） |
|---|---|---|---|
| **P0 双库基底 + 内存聚拢点** | 内存聚拢点接入（`StorageEngine`/`StorageCell`/`ECSStorage`/`SnapshotManager`）+ `PersistencePort` 序列化/重放；关系从 `ur_references` 拆出 → 关系层投影（`IUniversalReferenceManager`，复用 `UniversalReferenceResourceReferenceStore` 桥接；落盘 `PersistentUniversalReferenceManager` 或升级 `GraphDatabaseStorage`）；SQLite 保留事实/快照/配置；`ur_schema_version` + 幂等迁移 | 内存经 PersistencePort 落库/重建；关系查询走关系层；重建幂等；快照/恢复不回归 | ✅ 基底已建：`CsharpPrj/Operation/DataStoreDriven`（Agent 无关使用层）+ `Agent.Persistence/DataStoreEngine`。`IDataStore`/`IPersistenceStore`/`IPersistencePort`/`IZoneStore` 四端口 + Sqlite/LiteDB/Graph/Local 后端 + `PersistenceProviderFactory`/`DataStoreProviderFactory`（支持 `sharedBackbone` 单一骨干复用） |
| **P1 数据区 + 反应式触发** | 定义输入/输出/事件三区契约（复用 `DataContract`），落 `ur_inputs`/`ur_outputs`/`ur_events`；`ReactiveTrigger` 写输入 → 转调现有 `RunAsync` → 写回输出区 | 写数据即触发处理，全程无新业务逻辑；API 未动 | ✅ 已实现：`Agent.Core/DataStream/ReactiveTrigger`（ECS 反应式：`ReactiveZoneBridge` 桥接 `ComponentDataStore` 写即触发 → `ReactiveTrigger` 订阅 `DataChanged`，push 无轮询主循环 + InputKey 幂等 + 启动回填 + 串行/有界并发 + 失败保留可重放）；三区 `IZoneStore` + `SqliteZoneStore`/`InMemoryZoneStore`；组合根 `DataStoreHosting` 装配（`EnableReactiveTrigger` 开关，默认关，可回退） |
| **P2 事件化观测** | `IObservabilitySink` + 事件表 + 批写；迁移现有 `AgentLog.Log` 埋点为事件（仅新增，日志保留兜底） | 单轮完整事件序列可从 DB 重建，不再依赖日志 | ✅ 已实现：`Agent.Contract/Observability`（`RunEvent`/`EventType`/`IObservabilitySink`）+ `Agent.Core/DataStream/EventSink`（有界通道 + 批写 → `ur_events`）。**run 级事件已收敛至 `AgentOrchestrator.RunAsync`**（`SetObservabilitySink` 注入，RunStarted/RunCompleted/RunFailed 统一发出，ReactiveTrigger 仅发 InputAccepted 避免重复）；触发层/事件源已写事件。其余散落静态类埋点按设计「仅新增、日志保留兜底」延后 |
| **P3 数据驱动维护** | `ur_config`/`ur_strategy` 表 + 热加载；契约驱动扩展 | 改 DB 行即改策略/阈值，无需部署 | ✅ 已实现：`IConfigStore` + `SqliteConfigStore`（`ur_config` 表）+ `InMemoryConfigStore`，接入 `IDataStore.Config`。**热加载已闭环**：`AgentOrchestrator.SetConfigStore` + `LoopConfigHotReload.Apply`（每轮 `GetAllAsync` 热读，键存在才覆盖，避免与组合根覆写冲突）；改行即生效。`ur_strategy`/契约驱动扩展仍按需延后 |
| **P4 效率基准与治理** | KPI 采集、保留/压缩、基准对照、读模型；效率数据回流 → 策略/执行器/阈值自动调整建议 | 数据层不拖慢主循环（基准通过）；增长率受控 | ○ 未开工（设计 §15.3 明确「主链路稳定后再叠加」；需真实运行指标） |

每个阶段为纯增量：做完一阶段 API 照旧可用，数据层能力逐步增强，任何阶段可回退。**图例**：✅ 已实现 · ◑ 部分（基底已建，待闭环）· ○ 未开工。

---

## 十三、风险与权衡

| 风险 | 缓解 |
|---|---|
| 事件/输入写放大拖慢主循环 | 异步批写 + 有界缓冲 + 失败不阻断 + P4 基准验证 |
| 双库一致性/双真相 | 图库只读投影、幂等重建、容忍滞后；SQLite 单一写入口 |
| 迁移复杂度 | Schema 版本化 + 幂等迁移链 + 全量重投影一次 |
| 观测数据无限增长 | append-only + 分区 + `CheckpointReducer` 压缩 + 归档 |
| 改动面大（数十处日志点） | P2 分阶段迁移，每批跑通再下一批 |
| 反应式触发器复杂度 | 监听方式可配置、并发度/队列有界、失败不阻断 |

---

## 十四、验收标准（Done Definition）

1. 现有 API（`AgentRunner.RunAsync`/`AgentOrchestrator`/`IPersistenceStore`/`IAgentExecutor` 等）**原样保留、可独立运行**。
2. 外部可在 `ur_inputs` 写入数据即触发 AI 处理，结果回写 `ur_outputs`，全程不直接调业务 API。
3. 运行观测 = 查询 DB 数据流（时间线/单轮报告/决策迹/效率指标），不再依赖日志文本。
4. 配置/策略为数据行，改数据即改行为，无需重新部署。
5. 数据层关闭后系统回退为原 API 直接可用。
6. 效率 KPI（触发→处理→回写端到端、单轮 token 成本、事件写吞吐）有基准且不拖慢主循环。

---

> **一句话**：现有 API 是发动机，数据层是给它装上"数据读写的外壳总线"——外部写输入区 → 触发器内部调现有 `RunAsync` → AI 处理 → 结果写回输出区 → 外部读数据。所有原 API 原样保留，数据层是叠加门面，可开关、可回退。

---

## 十五、框架设计缺陷与修正建议（诚实评估）

> 本节记录本设计被指出/自审出的**内在缺陷**与**设计空白**，作为后续实现前的必读纠偏清单。目的不是否定方案，而是防止"理念很对、落地互相拉扯"。

### 15.1 三处内在张力（最重要）

**① （已消解）"写数据即触发" 与 "异步批写事件" 的自相矛盾 —— 被"写入即触发"修正**

- 初版曾担心：若触发信号与观测事件来自同一条异步批写流，则触发会被批量延迟——反应性与效率在内部打架。
- **修正（写入即触发）**：正确设计让**数据写入的过程本身就是触发**——数据库是 append-only 事件库，一次写入 = 原子提交一个事件 = 触发信号。触发与观测**同源**（同一行既是事实记录又是事件），**无第二通道**，低延迟（写入即触发）与持久化（写入即记录）天然合一。
- **剩余问题不再是"触发/观测二选一"，而是**：
  1. **写入路径本身的效率**：写入者不应被订阅者阻塞（写成功即返回，处理异步推进）；写入路径用批量/异步落库仅影响"观测落盘"，不影响"触发即时性"。
  2. **订阅者失败的可重放/幂等**：处理失败保留事件行 + 状态，依行重放，配幂等键去重（见 §15.2 ⑤）。
- **不再需要**把触发与观测拆成两条独立通道（初版建议作废）。

**② "以使用效率为唯一基准" 过于绝对（过度纠正）**

- 效率是**必要的首要门槛，但不是唯一基准**。只以效率为准，会为吞吐丢掉一致性/幂等性，而反应式触发最怕"丢事件"或"重复处理"。
- **修正**：效率为**首要量化门槛**，同时守住三条硬约束——**至少一次投递（幂等）、背压不崩、正确性不降**。

**③ "外部 API 完全基于数据处理" 有学习与延迟成本，不能做成强制通路**

- 若每个外部调用都必经"DB 写 + DB 读"往返，且数据层是**唯一路径**，效率基准会把它越做越慢（为纯粹牺牲性能）。
- **修正**：数据层是**门面/投影**，非必经路径——外部仍可直调原 API；数据层只是**另一个可选的、数据化的入口**。

### 15.2 三处设计空白（需补齐）

**④ 运行并发模型未定义**

- 现有循环是"一次 `RunAsync` = 一个任务 = 一个 `SchedulerState` 根"；而"写数据即触发"意味着**多并发输入 → 多并发 run**。
- **需明确**：一个输入行映射一个 run？并发上限？如何按 `session/thread` 关联？否则高写入时触发器雪崩。
- **现成复用**：`session/thread` 关联可直接复用 `ContextManager<T>`（`AsyncLocal<T>` 跨异步流传播，`Enter/Exit/ExecuteAsync` 嵌套上下文）承载"当前 run 的 session/threadId/roundId/leaseId"；批次生命周期可复用 `BatchManager`（内部即组合 `ContextManager<string>` + 状态管理）。一个输入行映射一个 run、并发上限由有界队列/背压策略约束（§6.2）。

**⑤ 触发幂等/去重缺失**

- DB 触发天然是 **at-least-once**，需要每个输入行的**幂等键**去重（可复用现有 `IdempotencyGuard`）。

**⑥ （已决策）双库：关系管理与数据储存彻底分离，使用两个数据库**

- **用户确定**：关系管理与实际数据储存分离，模块化使用两个独立数据库——**数据本体 → SQLite（库一）**，**关系管理 → 独立图数据库（库二）**，不合并、不把关系写进 SQLite 关系表。
- ECS 运行时：`RelationManager`/`RelationIndex`（内存关系索引）作为关系层；其持久化对到**图数据库**（`IUniversalReferenceManager` / 图存储），而非 SQLite。
- 一致性：数据库（库一）为组件事实源；关系库（库二）可重建（从组件/事件推导），允许短暂滞后、崩溃后可重放。
- 代价（诚实）：两个库带来备份/迁移/运维翻倍 → 需在 §九效率与备份策略中治理；但因关系查询是本框架的高频路径（决策迹/因果/血缘），分库的收益 > 成本。

**⑦ （已消解）不要再自建事件/outbox/触发机制 —— 直接复用 ECS**

- 初版曾计划自建 `IObservabilitySink`/事件表/outbox；现确认 **`Modules/ComponentData` ECS 已内建"写入即触发 + 反应式管线 + 关系索引"**（`ComponentDataStore.DataChanged` + `EcsComponentReactorPipeline` + `RelationManager`），无需自造（见 §〇）。
- **真正剩余的工作收敛为两件**：
  1. **"InternalStorage 内存存储后盾 ⇄ SQLite 长期落盘后盾"的持久化端口（PersistencePort）**（把 Session/Config 层增量落盘，崩溃恢复重放，见 §〇.6）；
  2. **存量 API（`AgentRunner.RunAsync`/`IAgentExecutor`/`IPersistenceStore`）到 ECS 的适配**（复用为 ECS system/reactor 的实现体）。
- 不再新建独立的触发/观测事件设施，避免与 ECS 双份真相。

### 15.3 落地建议（降低执行风险）

- **先做最小验证，不一上来全量**：优先跑通 **P1 最小闭环**"写输入 → 触发 → `RunAsync` → 写回输出"（进程内 outbox 触发），验证端到端延迟与并发模型。
- **双库、观测体系、效率治理等主链路稳定后再叠加**。
- 每阶段保留"数据层可开关、可回退"，避免一次性大改造成不可逆。

### 15.4 结论

> **结论（修订）**：数据库作为 **append-only 事件库（总线）** 是正确设计——**数据写入的过程本身就是触发**（write-as-trigger），触发与观测同源，低延迟与持久化天然合一；因此"把数据库当总线"不再是缺陷，而是本设计的核心机制。整体理念（数据中枢、叠加门面、契约驱动、投影可重建）站得住，是能长期演进的好骨架。**执行风险集中在剩余纠偏**：写入路径本身的效率（不阻塞写入者）、订阅者失败的可重放/幂等（§15.2 ⑤）、运行并发模型（§15.2 ④）、以及"效率唯一"需辅以一致性/背压/正确性硬约束（②）。

### 15.5 价值复审：ECS 架构设计是否有意义（基于源码核实）

> 本节记录对"复用 `Modules/ComponentData` 作为 ECS 基础"（§〇）这一决策的价值评估。结论基于**对底层源码的实际核实**，而非愿景假设。

**核实结论：文档声称的 ECS 能力真实存在。**

| 声称能力 | 源码核实 | 结论 |
|---|---|---|
| 写入即触发（`ComponentDataStore`） | `Set<T>(key,value)` 写入后立即 `PublishEvent` 推 `DataChanged`；提供 `ObserveKey(key)`/`ObserveType<T>()` 按需订阅 | ✅ 实打实 |
| 反应式管线（`EcsComponentReactorPipeline`） | `ConnectTo` 订阅 `DataChanged`，按 `Type` 分派到 `PriorityChain`，按优先级短路执行 | ✅ 实打实 |
| 非限定循环（`EcsContext`） | World + Relations + Indexes + Notifications + 后台更新线程 + `SignalUpdate` | ✅ 实打实 |
| 关系索引（`RelationManager`/`IndexManager`） | 图关系索引真实存在 | ✅ 实打实 |

**核心判断：有意义的不是"ECS"三个字母，而是其背后的三个可复用事实——可观测键值库（写入即触发）、优先级反应式管线、图关系索引。** 它们已在 `Modules/ComponentData` / `Share/UniversalReference` 中真实存在，且确实能降低数据传递耦合。

**① 有价值、可放心投入的部分**

1. **可观测数据中枢 / 单一写入**：状态进组件（`BudgetComponent`/`TokenComponent`/`RoundComponent`），配合 `DataChanged` 可观测流，天然满足"数据即真相 + 单写入口"。
2. **数据/关系分域**：组件（数据本体）+ `RelationManager`（关系图索引）在运行时天然两域，对应双库理念，无需自造。
3. **按需订阅解耦**：`ObserveKey`/`ObserveType` 让消费方只订阅关心数据，生产者/消费者解耦——这正是回答案"数据传递复杂性"的核心机制（数据不"传递"，而是"落共享库 + 按需读/订阅"）。
4. **契约式扩展**：加新观测点 = 注册契约，生产/消费方零改动，可扩展性有真实支撑。

**② 装饰性 / 需泼冷水的部分**

- **ECS 是游戏引擎"多同构实体 + 每帧 System 迭代"模式**，核心价值在 cache 友好紧凑布局、archtype 批量遍历、data-oriented 性能；而 Agent 只有极少数异构、重度有状态、LLM 驱动的"实体"，本质是**流水线/事件驱动**而非**仿真循环**，ECS 引以为傲的性能/组合优势基本用不上。
- **"ECS"名头下真正干活的是通用反应式原语**：§0.5 罗列的实际承载（`ObservableReferenceManager` 写即触发、`EventBus` 观测、`ContextManager` 并发、`RelationshipDispatcher` 路由）属通用事件/反应式框架，套不套"ECS"标签都成立——ECS 是标签化表述，非机制本身。
- **§0.4 已自认**：剩余真活是"内存 ECS 世界 ⇄ SQLite 同步通道 + 存量 API 适配"；ECS 只解决"数据在哪存、怎么通知"，不解决流水线序/持久化/并发。

**③ 落地纠偏（与本设计 §15 呼应）**

- **同步反应式管线 ↔ 异步 LLM 是明显错配**：`ReactorSystemBase.ExecuteCore` 在订阅上下文同步 `ForEach` 跑 reactor，一旦 reactor 内 `await RunAsync`，同步分发即失效，须落到 `CommandSystem`/有界队列做异步化与背压。"写入即触发"≠"即处理"，中间仍需一层队列编排。
- **防退化为全局可变字典**：组件库须加单一写者/所有权约束（呼应 `StateRootSingleSource` / `DeterministicCoreDisjoint`），否则"数据即真相"变成"数据即混乱"。
- **可扩展性的真正来源不在 ECS**：横向靠有界队列 + `ContextManager`（AsyncLocal）并发模型，纵向靠 SQLite 批写/投影/压缩（§七、§九）——预算别全押在 ECS 上。

**定位修正（一句话）**：把它当"**可观测数据中枢 + 反应式分发**"架构用，而非严格意义的 ECS 仿真；前者有价值且底层已有现成实现，后者对 Agent 场景意义有限。此定位与 §〇 复用决策不冲突，仅澄清"复用的是机制、不是形态"。

### 15.6 实施落地记录（2026-08-10）

> 本节记录本设计实际落地的代码位置、阶段状态与「理念 vs 实现」的诚实校准，作为后续迭代的基线。实施主体在 `CsharpPrj/Operation/DataStoreDriven`（Agent 无关使用层）+ `Agent/src` 的薄适配/装配层；全部为叠加门面、可开关可回退。

**① 已落地（代码）**

| 阶段 | 实际落点 | 说明 |
|---|---|---|
| P0 基底 | `CsharpPrj/Operation/DataStoreDriven` | Agent 无关使用层：四端口 + 后端（Sqlite/LiteDB/Graph/Local）+ 门面 + 双工厂。`SqliteDataStore`/`DataStoreProviderFactory` 支持传入共享骨干（`sharedBackbone`），复用组合根已注入的同一 `IPersistenceStore`（单一写入口/避免双真相） |
| P0 适配 | `Agent/src/Agent.Persistence` | 收敛为薄适配层：`DataStoreEngine`（InternalStorage 内存 ⇄ IDataStore 落盘，PersistSessionAsync/RebuildAsync）+ `Core/SnapshotSerializer` |
| P0 接线 | `Agent/src/Agent.Launcher/DataStoreHosting.cs` + `AgentHost`/`AgentLaunchStages` | 组合根装配 IDataStore+DataStoreEngine；可选 `RegisterReactive` 装配 ReactiveTrigger/EventSink |
| P1 触发 | `Agent/src/Agent.Core/DataStream/ReactiveTrigger.cs` + `ReactiveZoneBridge.cs` | **ECS 反应式**：`ReactiveZoneBridge` 把 `IZoneStore`（持久化端口，循环边界外）桥接为 `ComponentDataStore` 写即触发信号源（input.* 键）→ `ReactiveTrigger` 订阅 `DataChanged`（push，无轮询主循环）→ 转调 `AgentOrchestrator.RunAsync` → 写回 `ur_outputs`；`InputKey` 幂等（at-least-once）+ 启动回填；失败保留错误输出行可重放；串行/有界并发 |
| P2 观测 | `Agent/src/Agent.Contract/Observability/` + `Agent.Core/DataStream/EventSink.cs` + `Agent.Core/Orchestration/AgentOrchestrator.cs` | `RunEvent`/`EventType`/`IObservabilitySink` 契约；EventSink 有界通道 + 批写 → `ur_events`；**run 级事件收敛至 `AgentOrchestrator.RunAsync`**（RunStarted/RunCompleted/RunFailed；ReactiveTrigger 仅发 InputAccepted） |
| P3 热加载 | `Agent/src/Agent.Core/Orchestration/LoopConfigHotReload.cs` + `AgentOrchestrator.SetConfigStore` | 每轮 `GetAllAsync` 热读 `ur_config` → `Apply` 到 `AgentLoopConfig`（键存在才覆盖）；改行即生效 |
| P3 基底 | `DataStoreDriven`（`IConfigStore`/`SqliteConfigStore`/`InMemoryConfigStore`，`ur_config` 表） | 配置即数据行基底；接入 `IDataStore.Config` |

**② 理念 vs 实现的诚实校准**

1. **数据层触发已升级为 ECS 反应式（替代 P1 轮询）**：P1 原本是轮询式 `ReactiveTrigger`（`afterId` 游标 + `Task.Delay(PollInterval)`）。现已按 §15.6②.2 预留的升级路径落地——新增 `ReactiveZoneBridge`（`Agent.Core/DataStream`）把 `IZoneStore`（持久化端口，循环边界外）桥接为 ECS `ComponentDataStore` **写即触发信号源**（写 input.* 键即发布 `DataChanged`），`ReactiveTrigger` 改订阅 `DataChanged`（push），删除以 `Task.Delay(PollInterval)` 为主机制的轮询主循环；`IZoneStoreReactive.Writes` 作为机制层写信号（InMemory/Sqlite 三区提交即发布）。
2. **「写即触发」即时性 = 进程内事件发布延迟（近即时）**：`ReactiveTrigger` 订阅 ECS `ComponentDataStore.DataChanged`，写入提交即发布即触发；已**完全移除**周期轮询（`PollInterval` 选项与补漏循环已删），纯事件订阅；启动回填 `BackfillOnStart` 处理订阅前/重启遗留输入，维持 at-least-once。
3. **并发模型按 §15.2④ 保守落地**：`MaxConcurrency` 默认 1（串行）——当前循环引擎含全局静态取消 / 共享 `SchedulerState`；并发 >1 需先落地宿主并发模型，未强行放宽。
4. **存量 `AgentLog.Log` 埋点迁移（P2）已收敛到 run 级事件**：`IObservabilitySink` 契约与 `EventSink` 已建；**run 级事件已统一收敛至 `AgentOrchestrator.RunAsync`**（`SetObservabilitySink` 注入，RunStarted/RunCompleted/RunFailed 按成功/超时/取消/异常发出；ReactiveTrigger 仅发 InputAccepted，避免重复）；ReactiveTrigger 已写事件。其余散落静态类埋点（PromptCatalog/RoundStateMapper 等）按设计「仅新增、日志保留兜底」延后。
5. **P3 热加载已接入主循环**：`AgentOrchestrator.SetConfigStore` + `LoopConfigHotReload.Apply`——每轮 `RunAsync` 前 `GetAllAsync` 热读 `ur_config`，键存在才覆盖（`max_progress`/`request_timeout_seconds`/`retry_limit`/`loop_scope`），避免与组合根覆写冲突；「改行即生效」已闭环。

**③ 明确延后（设计 §15.3「主链路稳定后再叠加」）**

- **P4 效率 KPI / 保留压缩 / 读模型 / 治理回流**：需真实运行指标采集，主链路稳定后再叠加。
- **散落静态类 `AgentLog.Log` 埋点逐点迁移（P2 后半）**：run 级事件已收敛；PromptCatalog/RoundStateMapper 等内部辅助埋点仍走日志兜底，按需再迁移。

以上均须真实运行支撑，强行落地会产生不可验证的大改，故按 §15.3 落地建议延后。

