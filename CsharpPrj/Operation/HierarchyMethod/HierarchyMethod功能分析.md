# HierarchyMethod 功能分析文档

> 模块路径：`CsharpPrj/Operation/HierarchyMethod`
> 程序集：`Operation.HierarchyMethod`（net10.0-windows，类库）
> 模块标题：层次方法通用框架（HierarchyMethod）
> 分析日期：2026-08-25

---

## 1. 模块定位

`HierarchyMethod` 是 **nodes / task / history 三层通用、LLM-free 的基础框架**，为上层 Agent 应用提供确定性的层次化编排骨架。

**核心设计原则：框架本体零 LLM 依赖，所有 LLM 知识（执行器 / 导演 / 提示词）只能存在于 Agent 应用层，经注入端口提供。**

| 项 | 值 |
|----|----|
| 模块层级 | Layer 4（模块规则） |
| 输出类型 | Library |
| 根命名空间 | `Operation.HierarchyMethod` |
| 三层 | `Nodes` → `Tasks` → `History`（单向依赖） |
| 关键词 | NodeRecord / TaskRecord / HistoryRecord / 端口注入 / 可空守卫 |

---

## 2. 架构总览

```
┌─────────────────────────────────────────────────────────────┐
│                        Agent 应用层                          │
│   （执行器 / 导演 / 提示词 / RuleSetLoader 等 LLM 知识）       │
└──────────────────────────┬──────────────────────────────────┘
                           │ 经注入端口提供（P3 端口注入）
┌──────────────────────────▼──────────────────────────────────┐
│                 HierarchyMethod 框架本体（零 LLM）            │
│                                                              │
│   ┌─────────────┐    ┌─────────────┐    ┌─────────────────┐  │
│   │  Nodes 层   │ →  │  Tasks 层   │ →  │  History 层      │  │
│   │ 节点存储/输出 │    │ 簿记/执行节点 │    │ 历史容器/关系编排  │  │
│   │ 关系编排/发现 │    │ 循环快照根    │    │                  │  │
│   └─────────────┘    └─────────────┘    └─────────────────┘  │
│                                                              │
│   ┌─────────────────┐    ┌─────────────────┐                 │
│   │   Rules 层       │    │  Abstractions 层 │                 │
│   │  确定性规则引擎   │    │  诊断/安全 JSON   │                 │
│   └─────────────────┘    └─────────────────┘                 │
└──────────────────────────────────────────────────────────────┘
```

- **三层单向依赖**：`Nodes` → `Tasks` → `History`，Agent 消费、不反向依赖。
- **框架零 LLM**：LLM 知识（执行器 / 导演 / 提示词）只能存在于 Agent 应用层，经注入端口提供。
- **写边原语唯一收敛**：所有写边操作委托 `DataStoreDriven.ReferenceGraphCoordinator`（SingleImplementation / TH-12），编排只编排不复制写边。
- **可空守卫**：未装配端口 / 关系库返回空集 / 诊断（`UnavailableDiagnostics`），不消耗 AI 额度、不阻断 DB-only 路径。

---

## 3. 各层详细功能

### 3.1 Nodes 层（最丰富的一层）

**定位：节点存储、输出持久化、关系编排、发现注入。**

#### 3.1.1 节点即存储单元

| 类型 | 职责 |
|------|------|
| `NodeDefinition` | 节点定义：`NodeKey`（唯一键）、`WorkflowId`（命名空间）、`FuncName`（执行函数）、`ParameterKeys`（输入端口）、`ResultKeys`（输出端口）、`InputMapping`/`OutputMapping`（节点级键映射） |
| `NodeOutput` | 单个节点的持久化输出视图（`NodeKey` + 端口值字典） |
| `NodeOutputStore` | 节点输出存储默认实现：所有输出按 `nodes.{workflowId}.{nodeKey}` 为维度持久化到基础节点库（ur_nodes, NodeType=output），**每个节点一条记录**，端口以 JSON 载荷承载，值经 `NodeOutputCodec` 类型保真往返，**重启可恢复**（不再依赖 StateCenter Session 层内存） |
| `NodeOutputCodec` | 端口值编解码：任意 object → 「类型名（AssemblyQualifiedName）+ JSON」；类型不可解析/反序列化失败回退为 `JsonElement`（不抛异常、不中断读取） |
| `NodeKeys` / `NodeOutputKeys` | 存储键约定唯一权威：`nodes.{workflowId}.{nodeKey}.{port}`；空 workflowId 归一化单一权威 |

**存储收敛说明**：节点输出落点 = 基础节点库 ur_nodes（NodeType=output），不再依赖 StateCenter Session 层内存，数据载荷持久化、重启可恢复。

#### 3.1.2 关系编排（Relations 子目录）

| 类型 | 职责 |
|------|------|
| `NodeRelation` | **静态门面**：节点关系推导统一入口（`NodeId` / `DeriveEdgesForNode` / `SerializeRelations` / `DeriveWorkflowControlFlowEdges`），消费方不再直接依赖 Operation 原实现 |
| `TaskNodeRelationService` | 任务节点引用关系服务：执行期（B 类）「引用写」编排，写边幂等原语委托 `IReferenceMaintenance`（唯一守卫 → 图协调器纯写边），未装配守卫 no-op |
| `ResourceRelationService` | 资源/引用关系维护门面：资源节点增删 + 引用边增删 + 关系查询的编排，供 UI 层等消费方统一委托；只读查询未装配返回空集，写操作未装配抛清晰异常 |
| `WorkflowResultDeriver` | 工作流结果推导器（纯链尾推导，LLM-free）：从控制流引用链（Calls 边）推导终答步骤索引 = **链尾**（出度 0 的步骤）；线性链模板下即最后一步，将来演化为图（分支/合并）时出度 0 推导仍正确 |
| `WorkflowReferenceStaticizer` | 工作流控制流引用静态化器：把声明式 `RoundSpec` 的「组合 + 控制流」结构静态化为可维护的引用关系（Owns：工作流包含步骤；Calls：上一步调用下一步），模板粒度一次静态化常驻，供图算法/影响分析消费 |

#### 3.1.3 发现注入（Discovery 子目录）

| 类型 | 职责 |
|------|------|
| `NodeProjectionService` | 选择层（projection）：给定任务 + 工作流，从引用图投影候选节点，逐节点生成摘要 + 估算 token，返回候选清单 + 累计预算外露 |
| `NodeInjectionService` | 选择层（selection，预算内注入）：按 select 节点键集加载载荷、逐节点压缩、按 token_budget 限额注入（默认 6000 tokens），超预算节点按序 dropped，实现「给得更少但更对」 |
| `NodeRecordResolver` | 节点记录解析：按「输出记录 → 业务节点记录」顺序解析两种键形 |
| `NodeSummaryBuilder` | 一句话摘要：端口取前 3 个（key=截断值），非文档载荷取首 120 字符 |
| `TokenEstimator` | token 估算：4 字符 ≈ 1 token（字符近似） |

#### 3.1.4 任务图复合服务（TaskGraph 子目录）

| 类型 | 职责 |
|------|------|
| `TaskGraphService` | 「创建 task + 维护引用关系」的确定性复合服务（Agent 无关）：把任务创建与节点/引用边维护收拢为**原子复合能力**（写节点 + 建引用边 + 入队任务），支持创建/查询/删除/维护引用边；依赖均可选注入，未装配返回明确诊断而非抛错 |
| `ITaskGraphTaskStore` | 任务入队/状态/删除抽象端口（由 Agent.Core 的 `AgentTaskPoolAdapter` 适配实现），未装配返回 -1/false 诊断 |
| `CreateTaskGraphRequest` / `NodeSpec` / `ReferenceEdge` / `TaskGraphResult` / `TaskGraphSnapshot` | 复合服务的数据契约 |

#### 3.1.5 工作流骨架（Workflows 子目录）

| 类型 | 职责 |
|------|------|
| `RoundSpec` | 一轮骨架：有序步骤列表 + 端口契约 + 全局键映射（声明式数据，非散落硬编码逻辑）；含 `Source`（声明式加载 / 内置默认回退）、`SuccessCriterion`（DecisionWord / RunToEnd 两类成功判定语义） |
| `RoundStep` | 骨架步骤：按名字调用注册函数，从数据空间解析入参、回写结果键；`NodeId` 为**稳定节点实例 ID**（缺失回退 FuncName），杜绝「同名函数多次出现 → 节点覆盖」 |
| `BindingContainer` / `BindingStep` | 工作流容器/单节点的绑定契约（端口 + 全局映射），供共享 `DataContainerBinder` 消费，解耦静态/动态两条路径的绑定入口 |

#### 3.1.6 节点执行相关

| 类型 | 职责 |
|------|------|
| `NodeExecutionRecord` | 节点执行快照（Node 即 Result）：聚合输出端口、入参溯源（Provenance，与图 NodeToNode 边同源）、执行元数据（成败/归因/后端/耗时）；`Ok` = 无 Error 即成功（单一判定点） |
| `IExecutorTrigger` | 执行触发回调端口：纯节点执行层在「该节点执行」时回调，由 Agent 编排决定是否触发 LLM 子智能体；**框架只声明回调契约，不承载任何 LLM 知识**，未装配 → false（纯确定性执行） |

### 3.2 Tasks 层

**定位：纯簿记 + 执行节点 + 循环快照根 + 注入端口。**

| 类型 | 职责 |
|------|------|
| `TaskRecord` | **纯任务簿记实体**（任务池 ur_tasks 载荷，task 层唯一操作对象）：TaskKey（业务幂等键）/ Status（生命周期状态机）/ Priority / MaxRetries / HistoryId（归拢容器）/ UserInput / RelatedNodes（关联节点引用）/ ExecutionNode（执行节点属性）。**只承载簿记字段，不感知执行器 / LLM / 循环快照** |
| `ExecutionNode` | 执行节点：把 LLM/执行逻辑封装为自包含节点，由 task 作为**属性**直接携带（task 不调用 LLM，节点由执行器消费时解析执行）。含 ExecutorId / DirectorMode（Hybrid/Deterministic）/ RulesKey / WorkflowConfigKey（提示词等 LLM 细节外部来源）/ Payload（执行器专属载荷） |
| `AgentLoopState` | **循环快照根**（运行时确定性核心）：承载 Budget（熔断等级 level∈0..3）/ Token（轮次进度）/ Flag（控制位掩码）/ Round / LastEval / DataSpace（类型化数据空间），以及 Agent 扩展字段（Status / Scope / ContinuousFailures / 策略视图 / 运行中执行状态 / 转移历史）；实现 `IAgentLoopState`（在算法层 `ILoopState` 之上的 Agent 扩展契约），可投影为只读运行时快照 `AgentRuntimeSnapshot`；`FromTaskRecord` 从任务簿记投影生成 |
| `ITaskExecutor` | 任务执行器最小契约（LLM-free）：一个 `ExecutionNode` 的执行单元句柄，执行一次返回结果文本 |
| `ITaskExecutorResolver` | 执行器解析端口：按 TaskRecord 的 ExecutionNode 解析执行器句柄；未注入/未命中返回 null → 走确定性路径 |
| `IStateDirector` / `IStateDirectorProvider` | 状态导演契约/提供端口：决定本轮整体状态变更由谁提议（LLM 自由探索 / 规则表驱动 / 纯规则迭代）；缺省 = 确定性导演（零 LLM） |
| `IConfigKeyResolver` / `ResolvedWorkflowConfig` | 工作流配置键解析端口：把 `WorkflowConfigKey` 解析为执行所需配置视图（提示词计划/轮数/停止规则等 LLM 细节的外部来源）；未装配/未命中 → null |

### 3.3 History 层

**定位：历史容器 / 消息日志 / 关系编排。History = Task 更上层的 class 容器，一个历史归拢多个任务（每个任务下挂多个 node）。**

| 类型 | 职责 |
|------|------|
| `HistoryRecord` | 历史 class 容器视图（HistoryId / Name / TaskKeys）；成员（归拢任务键集 / 挂载节点）由图引用边推导，**本模型仅为查询视图，不冗余存储成员** |
| `HistoryTaskLink` | 历史归拢任务的链接记录（History → Task 的 history_owns_task 引用边投影） |
| `IHistoryRelationManager` | 历史关系管理端口（纯抽象，零实现依赖）：历史创建/删除、历史归拢任务、历史挂载节点、消息时序（round_sequence 边）、图查询 |
| `HistoryRelationManager` | 历史关系管理实现：**只做编排**（何时建历史/归拢任务/挂节点/时序/查询），写边原语委托 `IReferenceMaintenance`（唯一守卫 → 图协调器纯写边），读边经 `IResourceReferenceStore`；实体真源在 SQL（ur_histories / ur_tasks / ur_nodes），引用图只存关系投影，可 graph.rebuild 幂等重建 |

### 3.4 Rules 层

**定位：确定性规则评估引擎（零 LLM）。**

| 类型 | 职责 |
|------|------|
| `RuleEngine` | 确定性规则评估引擎：对规则集首匹配评估，条件经 `JsonRuleEngine`（Modules.Parsing.Rules）求值；未知 op 经 `onUnknownOperator` 回调暴露配置错误 |
| `RuleSet` / `LoopRule` / `GuardConfig` / `RuleResult` | 规则集数据模型：Version / Guards（max_steps 守卫）/ Rules（Name + Condition + Action + Reason + NextPromptHint）；RuleResult 含路由决策（stop / continue） |
| `IsWithinGuard` | 步数守卫判定单一权威（E3/E5 包含式语义）：stepCount 在 1..MaxSteps 内为"在守卫内" |

**关键语义**：达到最大步数守卫 ≠ 任务完成。返回 continue（非 stop 成功），由循环继续直至预算终止（TO）裁决，避免「未完成被静默记为成功（TS）」。

### 3.5 Abstractions 层

| 类型 | 职责 |
|------|------|
| `UnavailableDiagnostics` | 可空守卫诊断：未装配端口/关系库时的返回占位常量（-1 / false / 空集），使框架在未注入时返回诊断而不阻断 DB-only / 确定性路径（不消耗 AI 额度） |
| `JsonElementRead` | 安全 JSON 读取与写入工具：读取均带 `JsonValueKind` 类型守卫，缺失/类型不符时回退默认值（不抛） |

### 3.6 Hierarchy 层（三层层级统一管理门面，多 nodes ↔ 多 tasks ↔ 单 history）

> 定位：**模块化分离的收敛点** —— 把「LiteDB 引用管理」与「三层实例」正式切开后的统一编排门面。

| 类型 | 职责 |
|------|------|
| `IHierarchyManager` | 三层层级统一管理端口（消费方唯一入口）：建/删历史、注册/删除任务、task↔node M:N 关联（Link/Unlink）、派生查询（GetTasks/GetNodes/GetHistoryNodes）、节点输出持久化；**以及层次处理与查询**（`ProcessHistoryAsync` / `ProcessTaskAsync` / `ProcessTaskTreeAsync` / `DescendantsAsync` / `AncestorsAsync`，见 §3.7；其中 `ProcessTaskTreeAsync` 用 `task_owns_task` 自环 schema 展开不定深度子任务树） |
| `HierarchyManager` | 默认实现（LLM-free 组合根）：注入 `IHierarchyReferenceManager`（引用面，实现=DataStoreDriven.ReferenceGrounding）+ `INodeOutputStore`（节点）+ 任务簿记委托 + 可选运行态/图读端口；只编排不复制写边原语（SingleImplementation / TH-12）。**同时是层次处理引擎的组合根**：懒构 `HierarchyProcessFacade`（层结构 = `HierarchySchema` 数据），端口全可空 |

**引用/实例分离边界**：
- **LiteDB 引用管理** = `IHierarchyReferenceManager`（`DataStoreDriven.Abstractions`，唯一中立端口）→ `ReferenceGrounding` → `ReferenceGraphCoordinator`（唯一 LiteDB 写边 owner）。三层实例与门面**只依赖此接口、不触碰任何 LiteDB 具体类型**。
- **三层实例**（NodeRecord/TaskRecord/HistoryRecord）零 LiteDB 依赖，实例内冗余引用字段（TaskRecord.HistoryId/RelatedNodes、HistoryRecord.TaskKeys）标注为"权威承载在引用图，实例仅为冗余视图"。

**数据模型**：单 history（顶层 class 容器）→ 多 task；task ↔ node 为**真多对多**（`cross_db_link` 边，一条 (task,workflowId,nodeKey) 一条边，同一节点可被多 task 共享引用，`LinkTaskNodeAsync`/`UnlinkTaskNodeAsync`）。历史节点集**派生** = 其全部任务的节点并集（`history_owns_node` 已降级为派生视图，不再独立存边）。

**层次处理引擎接线（框架真正生效点）**：
- **门面即入口**：`IHierarchyManager.ProcessHistoryAsync` / `ProcessTaskAsync` / `ProcessTaskTreeAsync` / `DescendantsAsync` / `AncestorsAsync` 直接落 `HierarchyProcessFacade`（§3.7），消费方拿到 `IHierarchyManager` 即可用引擎，**无需自行 new 框架类型**；
- **查询分工**：成员集查询（`GetHistoryNodesAsync` 等）为**无界**契约（不施加展开预算、只返回键集），其遍历实现已统一到声明式引擎；引擎无法表达该查询时（如任务自递归 schema 下以 history 为起点）回退 owner 侧直读端口，保持 **schema 无关**。引擎查询/处理带**显式预算**，截断以 `IsTruncated` 在结论上可观测 —— 二者语义互补，不互相替代；
- **运行期装配（Agent 侧）**：`ConversationHosting.TryAttach` 装配 `HierarchyManager`（grounding + NodeOutputStore + `AgentHistoryRuntimePort` + `HistoryRuntimeService` + `grounding.Graph`）→ `AgentUiHost.AttachHierarchy` 槽位；UI 库 `AgentUi_HistoryTree`（`AgentUiLibraryHistory.HistoryTreeAsync`，history → task → node）与 `AgentUi_TaskTree`（`TaskTreeAsync`，task → sub-task → node 自递归树）为只读消费入口，输出层遍历 + 处理结论；
- **分发前提**：Agent/Produce 经 `Projects/BuildFramework/CsharpBakeRefs.props` 烘焙清单消费 Frame 程序集，已登记 `Modules.DeclarativeDataflow`（新增程序集在该清单改一处）。

### 3.7 Hierarchy.Dataflow 层（层次处理：复用 DeclarativeDataflow 引擎）

> 定位：把「层数/层名/包含关系」外置为数据、把「遍历与终止」交给框架，本模块只提供**数据真源与读端口**。
> 层次结构不再硬编码遍历：`Nodes` → `Tasks` → `History` 的三层语义经 `HierarchySchema` 声明，由
> `Modules.DeclarativeDataflow` 的递归引擎（TopDown/BottomUp + 环守卫/深度预算/节点预算三重护栏）解释。

| 类型 | 职责 |
|------|------|
| `HierarchySchemas` | 层次 schema 唯一声明点：标准三层（history → task → node）与任务自递归 `TaskTree(CrossDbReferenceTypes.TaskOwnsTask)`（不定深度 sub-task）；包含边类型直接引用 `CrossDbReferenceTypes` 常量。**层次外置**：同一 schema 提供 JSON 形态（`HistoryTaskNodeJson` / `TaskTreeJson` / `SerializeDefault`）与配置装载（`FromJson` / `LoadOrDefault`，非法配置回退内置默认并给诊断），改层次 = 改数据 |
| `HierarchyLevelRefs` | 三层实体 ↔ `LevelRef` 的唯一转换点（作用域承载 node 层的 workflowId 命名空间，与 ur_nodes 寻址同源） |
| `ReferenceHierarchyRelationReader` | `IHierarchyRelationReader` 适配器：正向读取**领域端口优先**（`history_owns_task` → `GetHistoryTaskKeysAsync`、`cross_db_link` → `GetTaskNodeKeysAsync`），未覆盖 / 为空时**回退引用图出边**并按包含声明的子层投影端点（故任意已声明包含边、含自递归 `task_owns_task` 同构可跑）；反向（子 → 父）经引用图入边实现；全部**只读**、未装配即返回空集 / null |
| `LevelProcessors/` | 层处理器（`ILevelProcessor`）：`HistoryOverviewLevelProcessor`（历史运行态概览）/ `TaskStatusLevelProcessor`（任务状态投影）/ `NodeOutputLevelProcessor`（节点输出端口投影），全部只读投影既有端口、零 LLM |
| `HierarchyProcessFacade` | 组合根（LLM-free）：装配 schema + 读适配器 + 三层处理器为一台引擎，暴露 `ProcessHistoryAsync` / `ProcessTaskAsync` / `ProcessAsync`，以及层次查询 `AncestorsAsync`（node → task → history，需 `referenceStore`）/ `DescendantsAsync`（按 schema 展开、按实体去重） |

**边界与语义**：
- **只读不写边**：写边唯一 owner 仍是 `DataStoreDriven.ReferenceGraphCoordinator`（SingleImplementation / TH-12），本层不触碰；
- **端点 Id 约定唯一权威**：层级资源 Id 的构造与还原收敛于 `DataStoreDriven.Abstractions.HierarchyResourceKeys`（`history.{id}` / taskKey / `{workflowId}.{nodeKey}`），适配器不自行拼接前缀；
- **反向查询可选**：子 → 父需引用图入边，故经可选 `IResourceReferenceStore`（运行期可传 `ReferenceGrounding.Graph`）；以端点相等判定入边、以父层 Id 消歧，未装配即返回 null（正向遍历不依赖）；
- **全局去重**：同一实体在单次处理中至多处理一次（`LevelRef` 值相等去重），故被多 task 共享的 node 只处理一次（接受该语义，避免每 task 重复处理）；
- **诊断而非失败**：未装配端口 / 尚无产出 / 未命中任务均记为成功 + `available=false`；只有业务失败（终态 Faulted/Cancelled/Error）才置 `Success=false`；
- **截断可观测**：预算截断在根结论上以 `IsTruncated=true` 暴露，消费方据此区分「展开未穷尽」与「业务失败」；
- **自递归可跑（不定深度）**：`task_owns_task` 归属边由 `ReferenceGrounding.LinkSubTaskAsync` / `UnlinkSubTaskAsync` / `GetSubTaskKeysAsync` 维护（写边仍唯一收敛到图协调器），`HierarchySchemas.TaskTree(CrossDbReferenceTypes.TaskOwnsTask)` + 装配 `referenceStore` 即可逐层展开任务树，深度由 `ProcessBudget.MaxDepth` / 节点预算收口（信任反转：不信任层次数据自终止）；
- **不改变既有职责**：`IHierarchyManager`（三层实例编排）与本门面并列，互不侵入。

---

## 4. 核心设计约束

1. **三层单向依赖 + 统一门面**：`Nodes` → `Tasks` → `History`，统一编排经 `Hierarchy.HierarchyManager`（通用、LLM-free）；Agent 消费、不反向依赖。
2. **框架本体零 LLM 依赖**：LLM 知识（执行器 / 导演 / 提示词）只能存在于 Agent 应用层，经注入端口提供。
3. **引用管理与三层实例分离**：LiteDB 引用管理收敛于 `IHierarchyReferenceManager`（DataStoreDriven，唯一中立端口）；三层实例零 LiteDB 依赖，只依赖该接口。
4. **写边原语唯一收敛**：所有写边操作委托 `DataStoreDriven.ReferenceGraphCoordinator`（SingleImplementation / TH-12），编排只编排不复制写边。
5. **可空守卫**：未装配端口 / 关系库返回空集 / 诊断（`UnavailableDiagnostics`），不消耗 AI 额度、不阻断 DB-only 路径。
6. **节点即存储单元**：一次步骤执行 = 一个节点的一次产出；输出按节点维度持久化、跨轮/跨任务可检索复用（存储收敛：模块 A 并入模块 C）。
7. **确定性核心与簿记解耦**：循环快照（Budget/Token/Flag/Round）独立于任务簿记（TaskRecord），任务不感知执行器 / LLM / 循环快照。
8. **层次外置（层次是数据不是类型）**：层数/层名/包含关系声明为 `HierarchySchema`，遍历与终止由 `DeclarativeDataflow` 引擎执法；新增层 / 改层数只改数据，不改编排代码。

---

## 5. 源文件清单映射

```
HierarchyMethod/
├── HierarchyMethod.csproj              模块规则 + 项目引用（TaskStatusPool / Observability /
│                                       Conversation / DataStoreDriven / AiScheduling /
│                                       WorkFlowMethod / Parsing / LoopFoldMethod /
│                                       JsonMethod / DeclarativeDataflow）
├── Abstractions/
│   ├── HierarchyMethodNamespaces.cs    三层 + 抽象命名空间占位标记
│   ├── UnavailableDiagnostics.cs       未装配诊断占位常量
│   └── JsonElementRead.cs              安全 JSON 读取/写入工具
├── Nodes/
│   ├── NodeDefinition.cs               节点定义（端口 + 映射）
│   ├── NodeOutput.cs / NodeOutputKeys.cs / NodeKeys.cs
│   ├── INodeOutputStore.cs / NodeOutputStore.cs / NodeOutputCodec.cs
│   ├── NodeExecutionRecord.cs          节点执行快照
│   ├── IExecutorTrigger.cs             LLM 触发回调端口
│   ├── Discovery/                      NodeProjectionService / NodeInjectionService /
│   │                                   NodeDiscoveryModels / NodeDiscoveryHelpers
│   ├── Relations/                      NodeRelation / TaskNodeRelationService /
│   │                                   ResourceRelationService / WorkflowResultDeriver /
│   │                                   WorkflowReferenceStaticizer
│   ├── TaskGraph/                      TaskGraphService / TaskGraphTaskSpec /
│   │                                   ITaskGraphTaskStore
│   ├── Circuit/                        电路接入面（P1 下沉，全消费方唯一函数调用链）：
│   │                                     RoundFlowSpecMapper（RoundSpec → CircuitRecipeSpec 投影）
│   │                                     RoundFlowCircuitRegistry（内容指纹缓存 + 唯一编译入口）
│   │                                     RoundStepCircuitNode（步骤语义 → ICircuitNode 黑盒体）
│   └── Workflows/                      RoundSpec / BindingSpec
├── Tasks/
│   ├── TaskRecord.cs                   纯任务簿记实体
│   ├── ExecutionNode.cs                执行节点（task 携带）
│   ├── Ports/TaskExecutionPorts.cs     执行器/导演/配置键注入端口
│   └── Loops/                          IAgentLoopState / AgentLoopState
├── History/
│   ├── HistoryRecord.cs / HistoryTaskLink.cs
│   ├── IHistoryRelationManager.cs / HistoryRelationManager.cs
├── Hierarchy/
│   ├── IHierarchyManager.cs              三层层级统一管理端口（多 nodes↔多 tasks↔单 history）
│   ├── HierarchyManager.cs               LLM-free 组合根（引用面 + 三层实例编排，零 LiteDB）
│   └── Dataflow/                          层次处理（复用 Modules.DeclarativeDataflow 引擎）
│       ├── HierarchySchemas.cs            层次 schema 外置唯一声明点（三层 / 任务自递归）
│       ├── HierarchyLevelRefs.cs          三层实体 ↔ LevelRef 唯一转换点
│       ├── ReferenceHierarchyRelationReader.cs  引用图 → 父/子事实的只读适配器
│       ├── HierarchyProcessFacade.cs      组合根（schema + 读适配器 + 处理器 → 引擎）
│       └── LevelProcessors/               history / task / node 层处理器（只读投影，零 LLM）
└── Rules/
    └── RuleEngine.cs                   确定性规则评估引擎
```

---

## 6. 上下游依赖

| 依赖 | 用途 |
|------|------|
| `Share/TaskStatusPool` | 任务池/状态机/循环引擎/确定性核心（TaskPool / Loops / Routing） |
| `Share/HierarchySupport` | 支撑 DTO（SchedulingMode / DataSpace / DataContract / RuntimeSnapshot，LLM-free 纯数据） |
| `Share/Conversation` | 会话消息日志纯逻辑（ConversationLog / ConversationMessage / IHistoryStore） |
| `Operation/DataStoreDriven` | 节点库/关系库/写边原语（NodeStore / NodeRelationStore / ReferenceGraphCoordinator） |
| `Share/AiScheduling` | AI 调度状态门面（IStateFacet 等共享状态视图） |
| `Operation/WorkFlowMethod` | 工作流上下文/循环框架（WorkflowContext / LoopFramework 领域内核类型） |
| `Modules/Parsing` | 通用 JSON 条件判定内核（JsonRuleEngine）：Rules 规则评估引擎委托 |
| `Modules/DeclarativeDataflow` | 声明式多层处理框架（层次外置 schema + 递归引擎 + 三重护栏）：`Hierarchy/Dataflow` 的层遍历 / 终止 / 子结论收敛复用，不硬编码层遍历；框架零项目引用 → 单向无环 |
| `Share/Frameworks/CircuitFramework` | 声明式电路框架（`Hierarchy/Dataflow` 的对偶——「谁调谁」进电路）：`Nodes/Circuit` 把轮骨架 `RoundSpec` 投影为电路配方（`RoundFlowSpecMapper`）、内容指纹缓存（`RoundFlowCircuitRegistry`）、步骤语义封入 `ICircuitNode`（`RoundStepCircuitNode`）；`RoundSpec` 中立原语（DependsOn/Branch/Switch/Call/Region/When/Guard）的唯一消费面，编译经唯一入口 `CircuitCompiler.TryCompile`、执行经 `CircuitEngine` 拓扑驱动 |
| `DataStoreDriven.HierarchyResourceKeys` | 三层层级引用图端点 Id 约定的唯一权威（构造 + 还原）：`ReferenceGrounding` 与 `Hierarchy/Dataflow` 适配器共用，消除前缀字符串分叉 |
| `DataStoreDriven.CrossDbReferenceTypes.TaskOwnsTask` | 不定深度任务树的归属边常量（`task_owns_task`）：`ReferenceGrounding.LinkSubTaskAsync` / `GetSubTaskKeysAsync` 读写，`HierarchySchemas.TaskTree` 声明为包含边 |

---

## 7. 总结

`HierarchyMethod` 是 AI Agent 工作流编排的**确定性基础设施骨架**：

- **它管**：节点存储、任务簿记、循环状态、历史归拢、规则评估、引用关系编排。
- **它不管**：LLM 调用本身（执行器 / 导演 / 提示词均通过注入端口由 Agent 应用层提供）。

这种「框架零 LLM + 端口注入 + 可空守卫」的设计，使得框架本体可以独立测试和复用，在不装配任何 LLM 能力的 DB-only 路径下依然安全可运行，同时为上层 Agent 提供了完整的层次化编排能力。
