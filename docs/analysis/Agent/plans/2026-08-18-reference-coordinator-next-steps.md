# 引用关系协调器（ReferenceGraphCoordinator）—— 下一步收尾逻辑

> **日期**：2026-08-18
> **范围**：本会话已完成"把散装的 LiteDB 引用读写收拢为单一 `ReferenceGraphCoordinator`，每次 SQL 更新同步 LiteDB，LiteDB 只存关系、数据与 SQL 完全不同"的主体重构；本文件记录**尚未落地的收尾逻辑**，供后续按序执行。
> **对齐约束**：
> 1. **只在一处读写 LiteDB 的引用关系** —— 唯一 owner = `ReferenceGraphCoordinator`（`CsharpPrj/Operation/DataStoreDriven/Graph/ReferenceGraphCoordinator.cs`）。
> 2. **每次 SQL 更新都更新 LiteDB** —— 所有 SQL 变更经 `WriteThroughNodeStore` / `WriteThroughTaskPoolStore` → `ReferenceGrounding` → coordinator 同步。
> 3. **LiteDB 只维护 SQL 中 task/node 的关系，数据与 SQL 完全不同** —— LiteDB 仅持久化 `ur_references`（引用边），不写 `ur_resources`；实体（task/node 本体与载荷）以 SQL 为准。

---

## 一、已完成（本会话落地）

- 新增 `ReferenceGraphCoordinator`（唯一 LiteDB owner；只存边；唯一读写面；写透收敛面 `ReconcileNodeRelationsAsync` / `ReconcileTaskAsync` / `RemoveTaskAsync`）。
- `ReferenceGrounding` 改为薄封装 coordinator（编排层，不再持有 LiteDB）。
- 装配收敛：`DatabaseBootstrapper.CreateGraphStoreAsync` 返回 coordinator；`AgentFrameworkAssembler` 单例装配；降级路径 `TaskPoolHosting` 也接入写透。
- `WriteThroughTaskPoolStore` 在 Enqueue/Delete/UpdateStatus/UpdatePayload 全量同步。
- UI 读路径收敛：`AgentUiLibraryDb` 移除 `LiteDatabaseBrowser`/`LiteDbPath()`，一律经 `AgentUiHost.Database.Resources`（coordinator）读取。
- 验证：`Agent.sln` / `FileDriven.sln` / `CsharpPrj/MainProj.sln` 0 错误；`Agent.Persistence.Tests` 53/53 通过。

---

## 二、下一步逻辑

### 步骤 1：物理删除遗留死类，并把 `ReferenceEdgeData` 迁入 coordinator

**现状**：`GraphIndexedResourceReferenceStore`、`GraphNodeRelationStore`、`LiteDatabaseBrowser`、`ReferenceStoreGuard`、`NodeStorageStoreFactory.CreateGraph` 已不被生产装配，仅测试/兼容保留；`ReferenceEdgeData` 仍定义在 `GraphIndexedResourceReferenceStore.cs`（coordinator 引用它）。

**动作**：
1. 把 `ReferenceEdgeData` record 从 `LiteDB/GraphIndexedResourceReferenceStore.cs` 移到 `Graph/ReferenceGraphCoordinator.cs`（或独立文件 `Graph/ReferenceEdgeData.cs`）。
2. 删除 `LiteDB/GraphIndexedResourceReferenceStore.cs`、`Graph/GraphNodeRelationStore.cs`、`Browser/LiteDatabaseBrowser.cs`、`Core/ReferenceStoreGuard.cs`。
3. `NodeStorageStoreFactory.CreateGraph` 改为返回 coordinator（或删除该方法，装配已直接用 coordinator）。
4. 全库检索并清除对这些类的引用（含 `PersistenceStoreFacade` / `ResourceStoreFactory` 注释、docs 里的旧引用文案）。

**注意**：`docs/` 与 `Agent/` 内的 `.md` 对旧类名的描述（如 `节点即存储单元：Agent三线存储架构介绍.md`、`Agent/docs/plans/2026-08-14-存储重构-Phase0盘点与对账报告.md`）一并改为 coordinator，避免文档与代码漂移。

---

### 步骤 2：迁移 `GraphIndexedResourceReferenceStoreTests` → coordinator

**现状**：`CentralTest/AgentTest/Agent.Persistence.Tests/GraphIndexedResourceReferenceStoreTests.cs` 仍测旧类。

**动作**：把该文件测试目标改为 `ReferenceGraphCoordinator.CreateAsync(_dbPath)`，并按 coordinator 语义调整：
- 资源往返：`AddResource/GetResource` 走**会话内存端点注册表**（不再持久化实体）。
- **重载重建**：改为断言「边在重载后仍在」（LiteDB 只持久边），不再断言资源持久。
- 图算法 / 级联：`GetNeighbors/FindPathsAsync/GetDependenciesAsync`、`RemoveResourceAsync` 级联清边，语义不变。
- `AddReferenceAsync` 端点 fail-fast：coordinator 已**移除**该校验（端点存在性由 SQL 保证），对应断言改为「未注册端点仍可写边」或删除。

> 目标：删除步骤 1 的旧类后，测试库不残留对死类的引用；`ReferenceGroundingWriteThroughTests` 已迁移，无需再动。

### 步骤 3（核心补全）：task 创建时真正写 task→node 引用边

**现状**：`WriteThroughTaskPoolStore.EnqueueAsync` 只注册 task 端点（内存），运行时 `RunRequest` 无"关联节点"字段，故运行时创建的 task 不产生任何引用边。

**动作**：

1. **`RunRequest` 增可选 `RelatedNodes` 字段**
   `Agent/src/Agent.Launcher/Modules/AgentUiLibraryRuntime.cs`（`internal sealed class RunRequest`，约 line 131）：
   ```csharp
   public List<string>? RelatedNodes { get; set; }   // task 创建时声明的关联 nodeKey 列表（缺省空）
   ```

2. **`RunJsonAsync` 的 `case "task"/"enqueue"` 透传**
   把 `req.RelatedNodes` 传给 `TaskPoolEnqueueAsync` 新增形参 `relatedNodes`。

3. **`TaskPoolEnqueueAsync` 接入写透**
   `Agent/src/Agent.Launcher/Modules/AgentUiLibraryTaskPool.cs`（line 209 起）：
   - 新增形参 `IReadOnlyCollection<string>? relatedNodes = null`。
   - `pool.EnqueueAsync(item)` 之后（入队成功），若装配了图协调器（`AgentUiHost.Database?.Resources as ReferenceGraphCoordinator`）：
     ```csharp
     await coordinator.ReconcileTaskAsync(key, relatedNodes ?? Array.Empty<string>(), ct: CancellationToken.None);
     ```
   - 或改经 `TaskGraphService.CreateTaskWithRelationsAsync`（传入 `RelatedNodes`），该服务已可收 coordinator。

4. **`ReferenceGrounding.EnsureTaskMirrorAsync` 语义升级**
   若希望"缺省 task 也有可展示的图条目"，可在 `WriteThroughTaskPoolStore.EnqueueAsync` 侧改为 `coordinator.ReconcileTaskAsync(taskKey, item 携带的相关节点)`；`AgentTaskItem` 可视需要增 `RelatedNodes` 承载（随 PayloadJson 快照持久）。

5. **模板/文档同步**
   `FileDriven/DrivenConfig/Templates/AgentRun/task/*.json` 与 `README.md` 增 `relatedNodes` 字段说明；`Agent/skills/create-task/SKILL.md` 的 `CreateTaskGraphRequest.RelatedNodes` 对齐运行时入口。

---

### 步骤 4（可选）：自检 / 验证脚本

- 在 `PowerShellScript/TaskPool/Test-AgentTaskPool.ps1` 或新增脚本补一条：`action=task op=enqueue` 带 `relatedNodes` → 断言 `graph-log.db` 出现 `task:taskKey → node:...` 边（`AgentUi_DbReadTableMarkdown db=graph` 可见）。
- 补单测：`WriteThroughTaskPoolStore.EnqueueAsync` 带相关节点 → coordinator 出现 task→node 边；删除 → 级联清边。

---

## 三、改动文件清单（下一步）

| 类型 | 文件 |
|------|------|
| 迁移 | `Graph/ReferenceGraphCoordinator.cs`（内联 `ReferenceEdgeData`） |
| 删除 | `LiteDB/GraphIndexedResourceReferenceStore.cs`、`Graph/GraphNodeRelationStore.cs`、`Browser/LiteDatabaseBrowser.cs`、`Core/ReferenceStoreGuard.cs` |
| 改造 | `Resolvers/NodeStorageStoreFactory.cs`、`AgentUiLibraryRuntime.cs`（RunRequest）、`AgentUiLibraryTaskPool.cs`（TaskPoolEnqueueAsync）、`Agent.Core/TaskPool/AgentTaskItem.cs`（可选）、`Agent.Core/TaskPool/TaskGraphService.cs`（收 coordinator 并接线） |
| 测试 | `CentralTest/AgentTest/Agent.Persistence.Tests/GraphIndexedResourceReferenceStoreTests.cs`（迁移）+ 新增 task→node 写透单测 |
| 文档 | `Agent/docs/plans/*`、`Agent/节点即存储单元*.md`、`FileDriven/DrivenConfig/Templates/AgentRun/*`、`Agent/skills/create-task/SKILL.md` |

## 四、验证方式

```bash
dotnet build Agent/Agent.sln -c Debug            # 0 错误 0 警告
dotnet build FileDriven/FileDriven.sln -c Debug
dotnet test CentralTest/AgentTest/Agent.Persistence.Tests -c Debug   # 全绿
```
- 功能：运行时 `task op=enqueue` 带 `relatedNodes` → `AgentUi_DbReadTableMarkdown db=graph` 可见 `task→node` 引用边；删除 task → 边级联消失。

## 五、风险与边界

- **步骤 3 的 `relatedNodes` 是运行时语义缺口**：若业务不改请求体，task 仍只有端点、无引用边——需与调用方（脚本/即时指令/LLM 技能）约定必须显式声明关联节点。
- **步骤 1 删除死类**：删除前须全库 grep 确认无生产引用（含 `ReferenceEdgeData` 迁移）；`LiteDBPersistenceStore`（Provider=LiteDB 的通用持久化骨干）属另一职责，**不在**删除范围。
- **内存端点注册表**：coordinator 的 `_endpoints` 为会话级（不落盘），重载后实体端点需由 SQL 写透重新注册；LiteDB 上只持久边，故重启后图关系仍完整、实体端点名需 SQL 侧补齐。

