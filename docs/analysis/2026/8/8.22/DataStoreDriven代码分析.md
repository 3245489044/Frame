# DataStoreDriven 模块代码分析

> 分析范围：`CsharpPrj\Operation\DataStoreDriven`（66 个 .cs 文件）
> 分析方法：通读全部实现 + 对照组拍摄（Sqlite / Local / Graph / Core-InMemory），按「重复逻辑 / 冗余逻辑 / 错误逻辑」分类。
> 结论先行：该模块的三库收敛（ur_nodes 单一 + 节点库承载 config/checkpoint/keymapping/zone）与「SingleImplementation / RedundancyElimination」拆分（`NodePaging` / `IdGenerator` / `PersistenceJson` / `ZoneNodeAdapter` / `WorkflowIds`）整体质量较高，重复多为「多后端不可避免」；真正的缺陷集中在 *workflowId 解析不一致*、*图库读面无锁*、*KeyMapping 双后端并存* 三处。

---

## 一、重复逻辑（重复实现 / 契约复制）

### D1. KeyMapping 双后端并存（最值得收敛）
- **Sqlite**: [SqliteKeyMappingStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Sqlite/SqliteKeyMappingStore.cs) 用独立 `ur_keymappings` 表承载。
- **Core**: [NodeKeyMappingStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/NodeKeyMappingStore.cs) 经 `ur_nodes`（NodeType=keymapping）承载。
- **装配入口**: [NodeStorageStoreFactory.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Resolvers/NodeStorageStoreFactory.cs#L26-L38) 同时暴露 `CreateSqliteKeyMappings` 与 `CreateNodeKeyMappings` 两条路。
- 同一业务域（KeyMapping）存在两套承载与两类 `NodeKey`（`keymapping.{category}.{wf}.{node}` vs `ur_keymappings` 的 (Category,Key)），属「三库收敛」过程中的留存重灾区。若收敛到位应仅保留节点库承载，独立 `ur_keymappings` 路径即重复。
- **建议**：确认组合根实际采用 `CreateNodeKeyMappings` 后，标记 `SqliteKeyMappingStore` 与新表为废弃并逐步移除，避免双真相。

### D2. 多后端契约实现的同构重复（合理但可再抽）
同一接口各后端各写一套高相似 CRUD：
- `INodeStore`: [SqliteNodeStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Sqlite/SqliteNodeStore.cs)（SQL）vs [InMemoryNodeStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/InMemoryNodeStore.cs)（List+lock）。
- `IZoneStoreReactive`: [NodeZoneStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/NodeZoneStore.cs) vs [InMemoryZoneStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/InMemoryZoneStore.cs)。
- `IConfigStore`: [NodeConfigStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/NodeConfigStore.cs) vs [InMemoryConfigStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/InMemoryConfigStore.cs)。

这是多后端架构的固有成本，非缺陷，但 `InMemoryConfigStore` 与 `NodeConfigStore` 的语义（增删改查）几乎完全可互代，若测试/原型不再需要内存占位可删除其一。

### D3. WriteThroughTaskPoolStore 双入口写透
[WriteThroughTaskPoolStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/WriteThroughTaskPoolStore.cs#L50-L61) 存在 `UpdateAndMirrorAsync<T>` 与 `UpdateAndMirrorAsync` 两个重载（一个返回 `Task<bool>`、一个返回 `Task`），仅因 `UpdateStatusAsync`/`UpdatePayloadAsync` 返回值类型不同而并存。轻微重复，可将非泛型版委托给泛型版收拢。
- 另注：`UpdateAndMirrorAsync` 中图镜像用 `CancellationToken.None`，见 B4。

### D4. 转发式薄层（职责重复）
- [ReferenceGrounding.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/ReferenceGrounding.cs#L95-L97) `WriteRelationsAsync` → 一行转发到 `_graph.ApplyRelationEdgesAsync`。
- [NodeGraphReconciler.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/NodeGraphReconciler.cs#L32-L36) `ReconcileAllAsync` → 整体转发到 `_grounding.ReconcileAsync`。

这两处是「组件分工」编排层，有 DI/职责价值，但因贴近纯转发，与 `WriteGuardPort`（负责写透联动）职责有重叠。`NodeGraphReconciler` 仅额外承担「悬空节点报告」，二者可评估合并。

---

## 二、冗余逻辑（无用 / 恒真 / 多余包装）

### R1. Graph 与 Local 门面近同构
- [GraphPersistenceStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Graph/GraphPersistenceStore.cs) 
- [LocalPersistenceStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Local/LocalPersistenceStore.cs)

两者构造完全一致（`base(checkpointStore, schema:null, NoopTransaction.Factory, snapshotStore)`），均为「内存 checkpoint/快照占位 + noop 事务」的空壳，仅类名不同。[PersistenceProviderFactory.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Resolvers/PersistenceProviderFactory.cs#L34-L43) 也仅为两个 Provider 各配一个空壳。若 Local 与 Graph 后端当前无任何差异化行为，可合并为一个后端或一个共享实现。

### R2. NodeZoneStore 列表方法的冗余强转
[NodeZoneStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/NodeZoneStore.cs#L55-L68) 三个 `ListXxxAsync` 均形如 `.Select(n => new X{...} as X).ToList() as IReadOnlyList<X>`。`ToList()` 本就返回 `List<T>`（实现 `IReadOnlyList<T>`），`as X` 与 `as IReadOnlyList<X>` 均为冗余，且掩盖了「Record 可能为 null」的语义，应改为直接 `Select(...).ToList()` 并返回。

### R3. 两份「管线初始化编排」
- [FacadeDataStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/FacadeDataStore.cs#L73-L92)
- [PersistenceStoreFacade.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/PersistenceStoreFacade.cs#L40-L68)

两处各自维护一份 `StaticAsyncPipeline` 步骤编排与 `StepExecutionException`→`ExceptionDispatchInfo` 解包样板。相似度高，可抽一个「管线初始化 + 异常解包」辅助，消除重复样板。

### R4. 恒真 / 冗余分支
- [NodeRelationDeriver.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/NodeRelationDeriver.cs#L50-L60)：`DeriveNodeToNodeEdges` 顶部 `if (provenance is null) yield break` 后 `foreach` 内又判 `srcNode` 空值，空集合与空元素的双重空值语义可归一（`null` 与空集合本就等价）。
- `InMemoryZoneStore` 的 `AppendTo/Paged` 已收敛为统一骨架（正向，非缺陷），仅提示 `InMemoryNodeStore.PutAsync` 与 `SqliteNodeStore.PutAsync` 的「覆写保留 CreatedAt」逻辑各自手写，可统一夹具。

### R5. 快照读面容错偏保守
[SqliteSnapshotStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Sqlite/SqliteSnapshotStore.cs#L53-L56)`LoadAsync` 对 `DBNull` 只判 `v is not null` 即 `v.ToString()`（DBNull→空串），应显式排除 `DBNull`，否则 「表里为 NULL」与「真正空快照」混为一谈（见 B3）。

---

## 三、错误逻辑（潜在缺陷）

### B1（高）workflowId 解析在 `ReconcileAsync` 内不一致 → 图镜像 id 漂移
- 现象：[ReferenceGrounding.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/ReferenceGrounding.cs#L354-L379)：
  - `Node` 作用域（L358-364）用调用方传入的 `workflowId` 直接调 `WriteRelationsAsync`；
  - `All` 作用域（L374-376）用 `WorkflowIds.Resolve(node)`。
- 而 [WriteGuardPort.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/WriteGuardPort.cs#L45) 也统一用 `WorkflowIds.Resolve(node)`。
- 后果：若调用方以与节点 `ThreadId`（缺省为 `agent-round`）不符的 `workflowId` 调用单节点对账，会对同一节点用 `{wf}.node` / `{node.ThreadId}.node` 两套端点 id 建边，恰好复现注释所声称要「消除」的 E 类漂移。
- **修复**：Node 作用域改为先读节点再 `WorkflowIds.Resolve(node)`，与 All 作用域/写透守卫保持同一权威。

### B2（中）`NodeConfigStore.GetAllAsync` 键前缀切片无防护
[NodeConfigStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/NodeConfigStore.cs#L44)：
```csharp
map[string.IsNullOrWhiteSpace(n.Key) ? n.NodeKey[NodeKeyPrefix.Config.Length..] : n.Key] = ...
```
当某 config 节点 `NodeKey` 为 `null` 或长度 < `Config.Length`（如绕过 `BuildConfig` 直写 `NodeKeyPrefix.Config + ""`，或历史脏数据）时，`[7..]` 抛 `ArgumentOutOfRangeException`，`null` 则抛 NRE。回退分支只护 `n.Key` 未护 `n.NodeKey`。
- **修复**：取 `n.NodeKey` 前判断是否以 `NodeKeyPrefix.Config` 开头且足够长，否则回退 `n.Key` 或跳过。

### B3（中）`SqliteSnapshotStore.LoadAsync` DBNull → 空串
见 R5。`SnapshotJson` 为空会得到 `""` 而非 `null`，调用方若以「null 表示无快照」判定会失准。
- **修复**：`v is DBNull` 或 `SqliteRows.Str` 规范化后判空返回 null。

### B4（中）写透镜像忽略取消令牌
[WriteThroughTaskPoolStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/WriteThroughTaskPoolStore.cs#L50-L61) `UpdateAndMirrorAsync` 用 `CancellationToken.None`，屏蔽了 `UpdateStatusAsync`/`UpdatePayloadAsync` 传入的 `ct`。长取消会中断 SQL 更新但镜像仍全量执行。
- **修复**：把调用方 `ct` 透传给 `EnsureTaskMirrorAsync`。

### B5（中）图库读面无 `_gate` 保护 → 并发读写的潜在竞态
[ReferenceGraphCoordinator.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Graph/ReferenceGraphCoordinator.cs#L240-L265)：`GetReferencesAsync`/`GetRelationsAsync`/`GetRelatedNodesAsync` 直接枚举内存 `_graph`，不取 `_gate`；而写操作 `AddReferenceAsync/RemoveReferenceAsync/ClearAllAsync` 均在 `_gate` 下变更 `_graph`（CoreGraph 无并发保证）。并发写图时读侧枚举可能读到不一致中间态或触发集合变更异常。
- **修复**：读面（尤其 `GetRelationsAsync`、`ProjectTaskNodesAsync` 的多次查询）也经 `_gate` 串行化，或对 `_graph` 使用快照副本读取（`CoreGraph` 若支持则上层一致性更佳）。

### B6（低）`NodeCheckpointStore.GetAsync` 步进取最大 Step 依赖 STEP 数值语义
[NodeCheckpointStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/NodeCheckpointStore.cs#L43-L48)：未指定 checkpointId 时以 `cp.Step > best.Step` 比较取最新。此处 Step 由 `BuildCheckpoint(..., step.ToString())` 以 `int` 语义写入并反序列化为数字比对，逻辑自洽；但若某源把 `Step` 写成非数字文本，`Deserialize` 后数值可比性失效（次要，视 Checkpoint.Step 类型而定）。

---

## 四、已做好的收敛（值得保持，勿动）

- **[NodePaging.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/NodePaging.cs)**：统一 afterId 游标分页循环，消除 Reconciler/Grounding 重复遍历。
- **[IdGenerator.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/IdGenerator.cs)**、**[WorkflowIds.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/WorkflowIds.cs)**、**[PersistenceJson.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/PersistenceJson.cs)**、**[ZoneNodeAdapter.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/ZoneNodeAdapter.cs)**、**[NodeKeyPrefix.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Abstractions/NodeKeyPrefix.cs)**：单一权威的骨架，正确消解了「pos/推导/序列化/键前缀」的重复。
- **协调器写面串行**：`MutateAsync` 只由内层 Add/Remove 各自取锁，避免「持锁内同步重入 SemaphoreSlim」自死锁——此设计是修正而非隐患（B5 是**读**侧未加锁的独立问题）。

---

## 五、建议处置优先级

| 优先级 | 编号 | 问题 | 动作 |
|---|---|---|---|
| 高 | B1 | `ReconcileAsync(Node)` workflowId 不一致 | 改为 `WorkflowIds.Resolve(node)` |
| 高 | D1 | KeyMapping 双后端并存 | 确认组合根后废弃独立 `ur_keymappings` 路径 |
| 中 | B5 | 图库读面无锁 | 读面经 `_gate` 或快照读取 |
| 中 | B2 / B3 | 键切片越界 / DBNull→空串 | 加防护 / 显式判 DBNull |
| 中 | B4 | 写透镜像忽略取消令牌 | 透传 `ct` |
| 中 | R3 | 两份管线初始化样板 | 抽公共辅助 |
| 低 | R1 | Graph/Local 门面近同构 | 评估合并 |
| 低 | R2 / D3 | 列表冗余强转 / 双重载 | 顺手清理 |
| 低 | R4 / R5 | 冗余分支 / DBNull 容错 | 归一化 |