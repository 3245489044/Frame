# 引用关系自动维护方案：图引用不应手动维护

> **日期**：2026-08-17
> **定位**：确立一条存储层原则——**图的引用关系是可推导的派生数据，不是手写事实，必须由存储层自动维护**；并给出下一步重构方案（写透机制 + 全关联对账 + 悬空分类）。
> **配套决策**：节点库回迁 SQLite（`ur_nodes` 表，载荷为字符串/JSON）；节点关系（`node_to_node` / `keymapping_to_node` 边）继续留在 LiteDB 图（`graph-log.db`，模块 B 真源），**不迁移到 SQL `ur_resources`**。
> **对齐公理**：[CR: axioms:AX.StructuralGrounding@consume] · [CR: axioms:AX.ReferenceGuardedDeprecation@consume] · [CR: axioms:AX.StateRootSingleSource@consume] · [CR: formula:SM.SyncGuard@consume] · [CR: formula:SM.IdempotencyGuard@consume]

---

## 一、原则声明（本方案要立的规矩）

**图（引用层）里的资源镜像与节点关系边，必须由存储层自动维护，业务/执行代码不得手写建边。**

> **口径限定**：本条「不得手写」适用于**节点关系边**（`node_to_node` / `keymapping_to_node`，见 §2.1）与对应的**节点/任务图资源镜像**。通用资源端口（`ur_resources` 手工 `addResource`/`addReference`，如 `create-reference`/`connect-nodes` skill）属**意图作者关系**，保留既有 `IResourceReferenceStore` 语义，不纳入自动推导（见 §六）。

拆成三条不变式（存储级，非路径级）：

| 不变式 | 内容 | 反例（现状违规点） |
|--------|------|--------------------|
| **I1 写透（write-through）** | 每次 SQL 基础存储（`ur_nodes` / `ur_tasks`）写入/删除**业务实体**，引用层**自动联动**更新对应**图资源镜像**（Put/Delete 触发）；**节点关系边**由独立推导机制在节点执行后生成（见 §3.2 时序）。业务代码不再显式调用 `EnsureNodeAsync`。 | `NodeExecutionService.PersistNodeMetadataAsync` 手写 `EnsureNodeAsync` + `AddRelationAsync`（见 §2.1）。 |
| **I2 全关联（total association）** | 每个**业务实体**（node / output / keymapping / task）都应有**一个且仅一个**图资源镜像；引用边端点必须存在（fail-fast 已具备）。内部记录（config / checkpoint / zone）**不**建图资源（避免图膨胀，见 §3.2 范围界定）。 | tasks 目前**没有**图资源镜像（`EnsureTaskAsync` 不存在）。 |
| **I3 悬空分类（dangling taxonomy）** | 无任何入/出引用边的实体，明确归类为**「悬空」**（可查询、可清理），而非静默存在。 | 当前无 `ListDanglingAsync` 查询，早期 `__dangling` 哨兵已在重构中消失。 |

> **理由（为什么引用是派生数据）**：引用关系（依赖/组合/映射边）本质是**可重建的派生数据**——只要确定性产物（节点/任务/配置载荷）在库里，引用边随时可从它们重新推导。因此它不应作为"手写事实"沉淀，而应由存储层按来源自动生成、对账校验、可清理重建。这与 [docs/KnowLedge/Agent数据价值分类存储原则.md](../../../docs/KnowLedge/Agent数据价值分类存储原则.md) 中「引用关系属可重建派生数据、按需重建」的判据一致。

---

## 二、现状分析：引用关系目前是"手动/路径级"维护

### 2.1 手写维护点（要消除）

唯一维护**节点关系边**（KeyMapping↔节点 / 节点↔节点）的代码路径是 `Agent/src/Agent.Axioms/Nodes/NodeExecutionService.cs` 的 `PersistNodeMetadataAsync`（第 177-203 行）：

```csharp
// 节点关系 → 图库（节点资源 + KeyMapping↔节点 + 节点↔节点数据流组合边）
if (_relations != null)
{
    await _relations.EnsureNodeAsync(_workflowId, node.NodeKey, symbol: node.Symbol, ct: ct);
    if (node.InputMapping != null || node.OutputMapping != null)
        await _relations.AddRelationAsync(_workflowId,
            NodeRelationFactory.KeyMappingToNode(_workflowId, node.NodeKey, node.NodeKey), ct);
    foreach (var (paramKey, (srcNode, srcPort)) in input.Provenance)
        await _relations.AddRelationAsync(_workflowId,
            NodeRelationFactory.NodeToNode(_workflowId, srcNode, srcPort, node.NodeKey, paramKey), ct);
}
```

问题：
1. **路径专属**：只有"节点执行"这一条路径在建边。直接写 `ur_nodes` / `ur_tasks`（如任务入队 `SqliteTaskPoolStore.EnqueueAsync`、配置写入、三区写入）**都不会**自动建图资源——引用层与 SQL 事实脱节。
2. **手写易漏**：每新增一个需要落引用层的写入口，都要记得手动补 `EnsureNodeAsync`；漏一处即产生孤立记录/悬空边。
3. **非存储不变式**：没有任何机制保证"每个 SQL 实体都有图镜像 / 零边实体被标记"。

### 2.2 已具备的能力（复用，不重造）

| 能力 | 现状 | 落点 |
|------|------|------|
| 引用边端点存在性校验（fail-fast，防悬空**边**） | ✅ | `StoreResourceReferenceStore.AddReferenceAsync`（StructuralGrounding，端点缺失抛 `InvalidOperationException`） |
| 删资源级联删引用边（防悬空边残留） | ✅ | `RemoveResourceAsync` + `SqliteResourceStorage`（`DELETE WHERE SourceId=@k OR TargetId=@k`） |
| 节点资源镜像能力（节点 → 图资源） | ✅（点状） | `GraphNodeRelationStore.EnsureNodeAsync`（`ResourceType=node`，`GetResourceAsync` 已存在即跳过，幂等） |
| 幂等写边（去重） | ✅ | `AddRelationAsync`：先 `EnsureResourceAsync` → 删同端点同 refType 旧边 → `AddReferenceAsync` |
| 有引用实体不可删（废弃守卫） | ✅（公理层） | [CR: axioms:AX.ReferenceGuardedDeprecation@consume] |

### 2.3 缺口清单

1. **无写透机制**：SQL 写实体 → 图资源镜像，只做在节点执行路径，非存储级通用钩子。
2. **tasks 未进引用层**：`ur_resources` 语义是"连接 tasks ↔ nodes 的有向边"，但任务没有 `EnsureTaskAsync` 镜像。
3. **无悬空分类**：没有接口列出"图里有资源但零边"或"SQL 有记录但无图镜像"的实体。
4. **无自动边推导**：`KeyMappingToNode` / `NodeToNode` 边目前由执行代码手写调用，未封装为"从映射/溯源推导"的单一推导器。
5. **无对账**：没有启动/定时全量校验"SQL 实体 ↔ 图资源镜像"一致性。

---

## 三、目标模型：引用自动落地守卫（ReferenceGrounding）

> 新增一个存储级守卫组件，与既有 `SyncGuard` / `IdempotencyGuard` 同构，把"引用自动维护"固化为不变量，而不是依赖各处手写。

### 3.1 组件与职责

```
ReferenceGrounding（引用自动落地守卫）
 ├─ WriteThrough  : 包装 INodeStore / ITaskPoolPersistence；Put/Delete 后自动同步图资源镜像
 ├─ EdgeDeriver   : 从「映射 + 溯源」推导 KeyMappingToNode / NodeToNode 边（唯一推导器）
 ├─ Reconcile     : 全量对账 SQL 实体 ↔ 图资源镜像，报告漂移
 └─ DanglingQuery : ListDanglingAsync() 列出零引用边实体（分类 = 悬空）
```

> **关键约定（锁定落地不模糊）**：
> - **写透目标介质 = LiteDB `GraphIndexedResourceReferenceStore`（graph-log.db，模块 B 真源）**——即 `GraphNodeRelationStore` 包装的同一 `IResourceReferenceStore`，**不是** SQL `ur_resources`（与「关系留 LiteDB、不迁移 SQL」决策一致）。
> - **资源镜像 Id 命名空间 = `wf.node`（`NodeRelationFactory.NodeId`）**——与边端点（`wf.node` / `wf.key`）一致，禁止用裸 `nodeKey`，否则对账/悬空查询误判漂移。
> - **task 资源类型新增常量 `NodeResourceTypes.Task`**（现有仅 `Node`/`KeyMapping`）。

### 3.2 三条不变式的落点

**I1 写透（实体 ↔ 图资源镜像，Put/Delete 触发）**：包装存储端口，写后自动 `EnsureResourceAsync`，删后自动 `RemoveResourceAsync`（级联删边）。**只做镜像，不做边**——边由独立的边推导机制在节点执行后生成，二者触发时机不同。

```text
Put(node|task)  → EnsureResourceAsync(ResourceType=node|task, Id=wf.node|taskKey)  // 幂等，已存在即跳过
Delete(node|task) → RemoveResourceAsync(ResourceIdentifier(...))                    // 级联删其全部引用边
```

> **范围界定（写透镜像白名单）**：仅**业务实体**（`NodeType ∈ {node, output, keymapping}` 与 task）写透镜像；内部记录（`NodeType ∈ {config, checkpoint}`）与三区（zone）**不**建图资源，避免图膨胀。

**I2 全关联**：`ReconcileAsync` 遍历 SQL `ur_nodes`（仅业务实体 `NodeType ∈ {node, output, keymapping}`）/ `ur_tasks`，逐个断言存在图资源镜像；缺失记入一致性违规报告。装配期 `InitializeAsync` 后、以及宿主诊断入口各触发一次。

**I3 悬空分类**：`ListDanglingAsync()` 返回集合 `(ResourceType, Id, 边数=0)` 与 `(SQL实体, 无镜像)`；统一标记 `Status=悬空`，供 UI/清理脚本消费。

### 3.3 与既有能力的分工（不重复实现）

- **写边**仍经单一实现 `StoreResourceReferenceStore.AddReferenceAsync`（fail-fast + 幂等），`ReferenceGrounding` 只负责"何时写/推导出哪些边"，**不复制**写边逻辑（SingleImplementation / TH-12）。
- **端点镜像**复用 `EnsureResourceAsync` 的"已存在即跳过 + 唯一键冲突容错"幂等语义。
- **删除级联**沿用 `RemoveResourceAsync` + `SqliteResourceStorage` 级联，`ReferenceGrounding` 不重写。
- **零边枚举端口**：`ListDanglingAsync` 依赖图存储"枚举零入/出边资源"能力；若 `IResourceReferenceStore` 无此端口，需先新增**只读**查询方法（不新增写语义）。

---

## 四、下一步重构方案（分阶段）

> 核心目标：把「手写维护引用」替换为「存储级自动落地守卫」，并按既定决策把节点库回迁 SQLite、关系留 LiteDB 图。

### Phase 0 · 契约与原则对齐（文档 + 领域公理）

- **文档**：本方案确立 I1/I2/I3 三条不变式与 `ReferenceGrounding` 守卫为权威定义。
- **领域公理（归位产品公理库 `Agent/Axioms`，**非** clinerules）**：
  - **不改** `StructuralGrounding` 公理公式。
  - 新增领域公理 `DerivedDataAutoMaintenance`（L2，轴 infra）——「可重建派生数据由存储层自动维护（写透 + 级联清理 + 对账重建），非业务手写事实；rebuildable(d):=∃src⊆Facts:d=project(src)」。
  - 契约源：`Agent/Config/prompts/logic-axioms.system.md` §B5；经 `Axioms.Generator` 反转生成至 `Agent/Axioms/L2/DerivedDataAutoMaintenance.cs`，登记 `mapping-report.txt`。
  - 领域概念不写进 `.clinerules`（clinerules 为编码 agent 自身元规则）；`references.md` 无需 `-UpdateIndex`，保持 HEAD。
- **前置修正**（已落地）：`节点即存储单元` 中「KeyMapping 节点库 + 图关系边包进同一 SQLite 事务（单库原子写）」的**不实表述**改为「逐条幂等写透 + 最终一致 + 对账兜底」（引用层跨介质，无单库原子性，对齐领域公理 `DerivedDataAutoMaintenance`）。

### Phase 1 · 写透机制落地（替换手写路径）

> 写透目标 = **LiteDB `GraphIndexedResourceReferenceStore`（graph-log.db）**；资源 Id = **`wf.node`（NodeRelationFactory.NodeId）**；task 新增 `NodeResourceTypes.Task`。

1. **新增 `Core/ReferenceGrounding.cs`**（DataStoreDriven 层，已落地）：引用自动落地守卫组件
   - `EnsureNodeMirrorAsync/RemoveNodeMirrorAsync`：节点业务实体镜像（ResourceType=node，Id=wf.node），幂等（GetResource 已存在即跳过 / 唯一键冲突容错）。
   - `EnsureTaskMirrorAsync/RemoveTaskMirrorAsync`：task 镜像（ResourceType=task，Id=taskKey）。
   - `WriteRelationsAsync`：写边经 `GraphNodeRelationStore` 单一实现（幂等），不复制写边逻辑。
   - **只做镜像不做边**；删除级联沿用 `RemoveResourceAsync`，不重写。
2. **新增 `Core/NodeRelationDeriver.cs`**（边推导器，唯一权威，节点执行后触发，已落地）：复用 `NodeRelationFactory` 构造端点 Id（不新造语义）；`DeriveKeyMappingEdge` / `DeriveNodeToNodeEdges`。
3. **`Abstractions/NodeRelationStore.cs`**：`NodeResourceTypes` 新增 `Task`（已落地）。
4. **改造 `NodeExecutionService.PersistNodeMetadataAsync`**（待接线）：移除手写 `EnsureNodeAsync`/`AddRelationAsync`；**镜像由写透在 `PutAsync` 触发（需新增 `INodeStore.PutAsync(NodeType=node)`，决策 D2）、边由 `NodeRelationDeriver` 在此执行后推导后经 `ReferenceGrounding` 统一写入（两时机分离）**。
5. **装配（决策 D3/D4）**：
   - `AgentFrameworkAssembler.FrameworkResourceGraphStage` 统一装配 `ReferenceGrounding`（目标 = graph-log.db 的 `GraphIndexedResourceReferenceStore`），并回注 `NodeExecutionService`（经 `AgentRunnerAssemblyInput → AgentRunner → WorkflowRunner → RoundStepInvoker` 透传，与既有 `BeginTransaction` 同构）；**不在** `BuildSqliteDataStore` 拆写透包装（该处无图存储句柄）。
   - node 写透白名单 = **仅 `{node}`**（业务节点；workflowId 由装饰器注入）；`keymapping` 资源镜像由边写入的 `EnsureResourceAsync` 产生，zone（input/output/event）/config/checkpoint 不建图资源。
   - task 写透在 `FrameworkTaskPoolStage`（`SqliteTaskPoolStore.Enqueue/Delete`）装配。

### Phase 2 · 全关联对账 + 悬空分类

1. **`ReferenceGrounding.ReconcileAsync()`**：遍历 SQL `ur_nodes`（仅业务实体）/ `ur_tasks`，逐实体断言图资源镜像存在；缺失记 `一致性违规`。**增量/分页**遍历（`afterId` 游标），避免全量阻塞。
2. **`ReferenceGrounding.ListDanglingAsync()`**：返回零引用边资源 + 无镜像 SQL 实体，统一标记 `Status=悬空`。**前置**：若 `IResourceReferenceStore` 无"枚举零入/出边资源"只读端口，需先扩展该端口（只读，不新增写语义）。
3. **宿主诊断入口**：`AgentUi` / 诊断命令触发 `ReconcileAsync` + `ListDanglingAsync`，输出悬空清单；清理脚本对悬空实体执行 `RemoveResourceAsync`（级联删边）。**对账修复策略**：`ReconcileAsync` 返回缺失镜像清单后，由 `ReferenceGrounding.RepairAsync` 从 SQL 重投影重建镜像（幂等）；或明确"仅报告、由清理脚本处理"，**二选一并在实现期固化**。
4. **测试**：`Agent.Persistence.Tests` 增 ReferenceGrounding 用例（写透、删级联、对账漂移与修复、悬空分类）。

### Phase 3 · 节点库回迁 SQLite（配合既定决策）

> 节点本体回 SQLite `ur_nodes` 表（载荷为字符串/JSON，保留 NodeType 判别）；节点关系边**不**迁移，继续留 LiteDB `graph-log.db`。

1. **新增 `Sqlite/SqliteNodeStore.cs : INodeStore`**：`ur_nodes` 表 `(Id PK, NodeKey TEXT UNIQUE, NodeType, ThreadId, Key, Status, PayloadJson, CreatedAt, UpdatedAt)`；`PutAsync` 走 `INSERT ... ON CONFLICT(NodeKey) DO UPDATE`（幂等 UPSERT），`ListAsync` 按 NodeType/ThreadId 过滤 + afterId 分页。
2. **`SqliteSchema`**：恢复 `ur_nodes` 建表（当前只建 `ur_tasks` + `ur_resources`）。
3. **`DataStoreProviderFactory.BuildSqliteDataStore`**：`new LiteDBNodeStore(nodeDb,…)` → `new SqliteNodeStore(factory,…)`；移除 `DeriveNodeDbPath` / `LiteDatabase nodeDb` / `ownedNodeDb` 节点库连接管理（`nodes.db` 文件下线）。
4. **`LiteDBZoneStore` / `NodeConfigStore` / `NodeCheckpointStore` / `NodeKeyMappingStore`**：改用 `SqliteNodeStore`（共用 `INodeStore`），三区 / config / checkpoint / keymapping 仍以 NodeType 判别节点承载。
5. **`FrameworkResourceGraphStage`**：`ctx.DataStore?.DataStore.Nodes` 变为 SqliteNodeStore 后，`CreateNodeKeyMappings` 走 SQLite 节点库（NodeType=keymapping），回落逻辑保留。
6. **数据迁移**：`nodes.db` 现有 `ur_nodes` 文档 → SQLite `ur_nodes` 行；删除 `nodes.db` 文件与 `DeriveNodeDbPath`；废弃 `LiteDBNodeStore`。

### Phase 4 · 收敛与验证

- **反模式注册**：`@ ANTI-MANUAL-REFERENCE` 在 formula.md §8.2 挂自动修复 `ReferenceGrounding`。
- **文档/引用对齐**：`节点即存储单元`、`三库收敛方案`、`references.md` 更新为「节点库=SQLite、关系留 LiteDB 图、引用自动落地守卫」。
- **验证**：`Agent.sln` / `CsharpPrj/MainProj.sln` 0 错误 0 警告；`Agent.Persistence.Tests` 与 `StateManagement.Tests` 全绿（含 ReferenceGrounding、SqliteNodeStore、悬空分类）。

---

## 五、验收判据（完成定义）

| # | 判据 |
|---|------|
| 1 | **节点关系路径**中不再存在手写 `EnsureNodeAsync` / `AddRelationAsync` 建边；全部经 `ReferenceGrounding`（写透镜像 + 边推导）。通用 `ur_resources` 手工 `addReference` 端口不在此列（意图作者关系豁免）。 |
| 2 | 写一个 node / 入队一个 task 后，图里自动出现对应资源镜像（无需额外调用）。 |
| 3 | 删除一个 node / task 后，其全部引用边被级联删除（无悬空边残留）。 |
| 4 | `ReconcileAsync` 能报告 SQL↔图镜像漂移；`ListDanglingAsync` 能列出零引用实体并标记悬空。 |
| 5 | 节点库运行于 SQLite `ur_nodes`；节点关系边仍位于 LiteDB `graph-log.db`（未迁移到 SQL）。 |
| 6 | `Agent.sln` / `CsharpPrj/MainProj.sln` / `CsharpPrj/Operation/DataStoreDriven.csproj` 构建 0 错误 0 警告，存储相关测试全绿。 |

---

## 六、风险与边界

- **跨介质无原子性**：写透是 `SQL(ur_nodes) → LiteDB(graph-log.db)` 跨引擎联动，无法单库事务。**已接受**：逐条幂等（UPSERT + 唯一键）+ 最终一致，靠 `ReconcileAsync` 对账兜底；不再声称"单库原子写"覆盖引用层。
- **自动边推导的语义来源**：`KeyMappingToNode` 来自映射存在性、`NodeToNode` 来自溯源（`Provenance`）；推导器是唯一权威，业务只提供"来源"，不提供"边"。
- **`__dangling` 旧哨兵**：早期 LiteDB 图实现的历史哨兵不恢复；悬空统一由 `ListDanglingAsync` 派生，不落持久状态。
- **参考边到通用资源的边界**：`ReferenceGrounding` 只管「基础节点库/任务」的自动落地；通用资源（`ur_resources` 手工 `addResource`/`addReference` 端口）保留既有 `IResourceReferenceStore` 语义，不纳入自动推导。
- **删除语义与废弃守卫协调**：写透 `Delete → RemoveResourceAsync` 是"实体退役连带清边"，仅用于已被判定可退役的实体；`ReferenceGuardedDeprecation`（废弃流程中仍有引用则禁止物理删除）由删除调用方在其前触发引用检查。二者触发场景不同、顺序由调用方保证，不视为冲突。
- **task 资源生命周期**：task 只建一次图资源镜像（幂等）；状态/完成变化**不**重建镜像，仅更新边或状态，避免任务高频变化导致的图抖动。
