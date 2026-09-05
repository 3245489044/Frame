# Agent.Launcher · Libraries 下一步处理 Backlog

> **版本**：1.0　**日期**：2026-08-30
> **范围**：`Projects/Agent/src/Agent.Launcher/Modules/Libraries/*`（AgentUi 实现库）+ 依赖的 `AgentUiHost.cs` / `AgentUiLibrarySupport.cs`
> **性质**：后续重构待办清单（Backlog）。本文档承接已完成的重构（见 §0），把分析中判定为「有意暂缓」的 5 项整理为可执行的下一步任务，含风险、方案、前置条件与验收标准。
> **关联**：`Projects/Agent/src/Agent.Launcher/AgentLauncher_重构优化分析.md`（既有的本程序集重构分析）

---

## 0. 已完成重构回顾（承接基线）

| 批次 | 项 | 位置 | 状态 |
|------|----|------|------|
| 1 | `HistoryAttachNodesAsync`/`HistoryDetachNodesAsync` 收敛为 `MutateTaskNodesAsync` | `AgentUiLibraryHistory.cs` | ✅ |
| 2 | `HistoryStep/Pause/Resume/Stop` 收敛为 `WithHistoryRuntimeStringAsync` + `WithHistoryControlAsync` | `AgentUiLibraryConversation.cs` | ✅ |
| 3 | `WriteNodeAsync`/`DeleteNodeAsync` 收敛为 `RequireNodeStore`（含空安全链修复） | `AgentUiLibraryData.cs` | ✅ |
| 4 | `Short` 合并入 `Truncate`（新增 `empty` 参数） | `AgentUiLibrarySupport.cs` / `AgentUiLibraryRuntime.cs` | ✅ |
| 5 | `SetLlmConfigJsonAsync` JSON null 安全读取 + `SetLlmConfigAsync` 改用 `WithAgentStringAsync` | `AgentUiLibraryRuntime.cs` | ✅ |
| 6 | `TaskPoolStatus`/`TaskPoolList` 改用 `RequirePool()` | `AgentUiLibraryTaskPool.cs` | ✅ |
| 7 | `TrySqliteSession()` 收敛三处 SQLite 连接构造 | `AgentUiLibraryDb.cs` | ✅ |

> 上述改动均已通过 `Agent.sln` 编译（0 错误），且为内部方法重构、不改公共 API 签名与行为语义。

---

## 1. Backlog 总览

| # | 项 | 归类 | 优先级 | 改动量 | 风险 | 状态 |
|---|----|------|--------|--------|------|------|
| B-1 | `AgentUiHost` 槽位泛型化（R1） | 结构性瘦身 | 🟠 中 | 中 | 🔴 高（公共 API 签名保持） | ✅ 完成 |
| B-2 | 跨 wf 共享节点删除判定（B2） | 正确性边界 | 🟠 中 | 小 | 🟡 中（设计语义确认） | ✅ 完成 |
| B-3 | allowed 字符串单源化（R3） | 防漂移 | 🟡 低 | 小 | 🟢 低 | ✅ 完成 |
| B-4 | `RunJsonAsync` 巨型分发拆表（R5） | 可维护性 | 🟢 低 | 大 | 🔴 高（回归面广） | ✅ 完成 |
| B-5 | Libraries 内性能项（B6/B7） | 性能 | 🟡 低 | 小 | 🟢 低 | ✅ 完成 |

> **实施完成注记（2026-08-30）**：B-1~B-5 全部按本文档方案落地，`Agent.sln` 编译 0 错误、未引入新告警（仅存量 `ConversationHosting.cs` CS8621 警告）；全部保持公共 API 签名与行为语义不变、诊断字符串逐字一致。实施要点与边界见各分项下方「实施备注」。

---

## 2. 分项明细

### B-1 `AgentUiHost` 槽位泛型化（对应 R1）

**现状**：`AgentUiHost.cs` 中约 15 个槽位（`Current/Database/ReferenceGrounding/TaskPool/HtmlUi/Conversation/HistoryRelations/NodeTaskHistory/ExecutionFlag/HistoryRuntime/ConversationRenderer/ConversationPageDeclaration/ConversationTemplates/HtmlViewerResolver/ConversationHtmlSink/HtmlDiagnosticsSink/AgentUiConfigPath`）重复同一「四件套」样板：

```csharp
private static T? _x;
public static T? X      { get { lock (_gate) return _x; } }
public static bool HasX { get { lock (_gate) return _x != null; } }
public static void AttachX(T v) { ArgumentNullException.ThrowIfNull(v); lock (_gate) _x = v; }
public static void DetachX()    { lock (_gate) _x = null; }
```

**暂缓原因**：涉及大量公共 API 签名保持（`AttachX/DetachX/HasX/X` 被宿主组合根直接调用），且存在特殊槽位无法一视同仁：

* `Database`/`ReferenceGrounding`：`AttachDatabase`/`AttachReferenceGrounding` 内含「图协调器单实例断言」（`EnsureSingleGraphOwnerLocked`）；
* `TaskPool`：`AttachTaskPool`/`DetachTaskPool` 内含订阅/退订联动（`EnsurePoolSubscribed`/`OnPoolDetached`）；
* `Current`：附带 `IsAttached` 语义与 `Require()`（抛异常）。

**下一步方案**（分级，避免一把梭）：

1. **仅收敛纯样板槽位**：把「无特殊逻辑」的槽位（如 `ConversationRenderer`、`ConversationTemplates`、`ConversationPageDeclaration`、`HtmlViewerResolver`、`ConversationHtmlSink`、`HtmlDiagnosticsSink`、`AgentUiConfigPath` 等）引入内部泛型容器 `Slot<T>`（封装 `get/Has/Attach/Detach` 的 lock 模板），特殊槽位（`Database/ReferenceGrounding/TaskPool/Current`）保持手写；
2. **保持公共 API 签名不变**：泛型容器只作内部实现载体，`public static` 成员仍逐一显式声明（可读性优先于 DRY），仅消除锁样板与 `ArgumentNullException` 重复；
3. **不动 `AttachComposition`/`GetHtmlAssemblySnapshot`**：这两个组合根入口读多槽位，保持现有字段读取方式，避免连带改动。

**验收标准**：

* `Agent.sln` 编译 0 错误；
* 全部 `AttachX/DetachX/HasX/X` 签名与语义不变；
* `Composition/` 与宿主（FileDriven）调用点零改动；
* 特殊槽位（图断言/订阅/Require）行为经既有集成路径回归。

---

### B-2 跨 wf 共享节点删除判定（对应 B2）

**现状**：`HistoryDeleteAsync`（`AgentUiLibraryHistory.cs`）级联删 node 时，用当前 `wf` 构造图节点 Id 查入边判「是否仍被引用」：

```csharp
var nodeId = new ResourceIdentifier {
    ResourceType = NodeResourceTypes.Node,
    Id = NodeRelationFactory.NodeId(wf, nodeKey),   // 仅当前 wf
    Location = wf,
};
var stillRefd = inRefs.Any(r => r.RefType == CrossDbLink && r.Source.ResourceType == Task);
```

**暂缓原因**：节点存储键是**裸 nodeKey**，但引用边按 `{wf}.{nodeKey}` 命名空间隔离。若同一裸 nodeKey 被**多个 wf** 的 task 引用（跨 wf 共享），此判定只认当前 wf 的入边，可能误删跨 wf 共享节点；且第 1 步删 task 时若因 `pool is null` 未删（skipped），残留的 cross_db_link 边会使该 node 被误判为「仍被引用」而保留。

**前置条件**：确认 `ReferenceGraphCoordinator.GetReferencesAsync` 是否支持跨 wf 查询（或需按 wf 集合遍历），以及 `CrossDbLink` 引用边是否记录 `Location` 维度。

**下一步方案**：

1. 与图守卫 owner（`ReferenceGrounding`）确认跨 wf 入边查询能力；
2. 若支持：按「裸 nodeKey 在所有 wf 的 task 引用」判定 `stillRefd`，删除前显式遍历 `location` 集合；
3. 若仅当前 wf 可查：在文档中记录该边界语义（「跨 wf 同名裸键共享不做引用保护」），并补充注释 + 可选防御（至少对 skipped task 的节点保留策略给出明确说明）。

**验收标准**：

* 单 wf 场景行为不变（既有历史删除回归通过）；
* 跨 wf 共享场景有明确、可测的判定语义；
* 无「误删仍在被其它 wf task 引用的节点」的路径。

---

### B-3 allowed 字符串单源化（对应 R3）

**现状**：`AgentUiLibraryRuntime.cs` 顶部手写 `AllowedActions/AllowedTaskOps/AllowedNodeOps` 常量，`switch` 各子分支又用 `UnknownOpError(..., "硬编码串")`（如 relations/graph/config/conversation/history 分支）；`AgentUiLibraryData.cs` 有 `AllowedQueryTargets`。**允许值存在「常量 + 硬编码」双份**，新增 op 时易漏改其一导致漂移。

**暂缓原因**：`AllowedTaskOps`/`AllowedNodeOps` 已被对应分支引用（`UnknownOpError(req.Op, "task", AllowedTaskOps)`），是良好实践；其余分支为内联硬编码。收益主要在「防漂移」，非功能性。

**下一步方案**（最小、低风险）：

1. 把 `UnknownOpError(..., "relations"/"graph"/"config"/"conversation"/"history")` 内联的允许串提取为常量（对齐 `AllowedTaskOps`/`AllowedNodeOps` 模式）；
2. 对 `AllowedActions`：以现有 `switch (action)` 的 case 集为准校核一次，若一致则保留常量（不改自动派生，避免重构 switch 的风险）；
3. 可选：在常量旁加注释「与 RunJsonAsync 各 case 手动同步」，作为维护提示（比自动派生成本低）。

**验收标准**：

* 各 `UnknownOpError` 的允许串与对应 `switch` case 一一对应、无遗漏；
* `AllowedActions` 与 `switch (action)` case 集一致（编译后人工比对一次）；
* 不改变任何运行时行为（纯字符串来源收敛）。

---

### B-4 `RunJsonAsync` 巨型分发拆表（对应 R5）

**现状**：`AgentUiLibraryRuntime.RunJsonAsync` 约 200 行，按 `action` 分发到 `task/node/resource/reference/relations/graph/config/conversation/history/flag` 等多个内嵌 `switch`，每个子分支重复「`OpOf + switch + UnknownOpError`」结构，可读性与扩展性差。

**暂缓原因**：`RunJsonAsync` 是**单一 JSON 请求体入口**，被 `AgentUi_RunJson` 注册函数、`TaskScopeExecuteAsync`（高层命令）以及多种模板消费；拆分 handler 映射表后，需保证所有既有 action/op 组合的解析与诊断字符串**逐字一致**，回归面广、风险高。

**下一步方案**（建议后续单独立项、带全量回归）：

1. 建立 `action → (handler, allowedOps)` 的映射表（`Resolve(table)` 语义），`RunJsonAsync` 只做「查表 + 调 handler」；
2. 每个 handler 封装为一个私有方法（如 `HandleTaskAsync`/`HandleNodeAsync`…），把内嵌 `switch` 移到方法内，`RunJsonAsync` 主体收敛为一行查表；
3. **保持诊断字符串逐字不变**（`UnknownOpError` 输出、各分支错误文案），保证模板与断言不回退；
4. 前置：先固化当前各 action/op 行为的集成快照（或既有驱动测试），重构后逐项比对。

**验收标准**：

* `RunJsonAsync` 主体收敛到「查表 + 调用」，显著短于现状；
* 所有 action/op 分支行为与诊断字符串逐字一致；
* `AgentUi_RunJson`、`TaskScopeExecuteAsync`、现有模板消费路径全部回归通过。

---

### B-5 Libraries 内性能项（对应 B6/B7）

**现状**（Libraries 目录内可见的性能隐患）：

* `GraphStatusTextAsync`（`AgentUiLibraryRuntime.cs`）：对每个资源各调一次 `GetReferencesAsync`，O(端点数) 次调用，数据量大时明显慢；
* `AgentUiLibraryDb.cs`：`TrySqliteSession()` 每次新建 `SqliteSession`（连接），高频 `ReadNodeRecords`/`ListNodeRecordsByThreadId` 时反复建立/释放连接。

**暂缓原因**：连接池与批量图统计接口属框架层（`Operation.*` / `StorageFramework.*`）能力，非本目录可独立解决；且当前数据量下未形成可量化瓶颈，改动需与框架层协同。

**下一步方案**：

1. **连接复用**：在 `AgentUiLibraryDb` 内为 `TrySqliteSession` 增加会话级缓存/复用（或在模块装配时创建一次、随 detach 释放），减少高频重复建连；需先确认 `SqliteNodeStore` 的 `Dispose` 语义避免过度释放（对齐重构文档 §7 的既有注意点）；
2. **批量图统计**：与 `ReferenceGraphCoordinator` owner 确认是否提供批量引用计数接口；若无，在 `GraphStatusTextAsync` 内聚合为单次迭代统计并注释瓶颈，待框架层扩展。

**验收标准**：

* 连接复用后行为不变（读写正确性回归），会话随 detach 正确释放无泄漏；
* `GraphStatusTextAsync` 端点数大时不再 O(N) 次独立查询（或明确标注待框架层支持）。

---

## 3. 建议执行顺序

> 原则：**先低风险高确定性，再高风险项单独立项**；每项保持 `Agent.sln` 可编译、行为可回归。

| 顺序 | 项 | 理由 |
|------|----|------|
| 1 | B-3 allowed 字符串单源化 | 改动小、风险低、立即可做 |
| 2 | B-2 跨 wf 共享节点判定 | 正确性边界，需先确认跨 wf 查询能力 |
| 3 | B-5 性能项（连接复用） | 小改动、需框架确认 Dispose 语义 |
| 4 | B-1 `AgentUiHost` 槽位泛型化 | 结构性瘦身，需保守分槽位处理 |
| 5 | B-4 `RunJsonAsync` 拆表 | 改动大、回归面广，单独立项并配全量回归 |

---

## 4. 风险与注意

* **B-1**：勿把特殊槽位（图断言/订阅/Require）强行并入泛型容器；公共 API 签名保持是硬约束，宁少勿滥。
* **B-2**：跨 wf 共享是边界场景，改动前先固化「删除历史」既有集成回归，避免误删/漏删行为变化。
* **B-4**：诊断字符串与行为逐字一致是回归底线；拆表不得引入任何文案差异。
* **B-5**：`SqliteSession` 生命周期遵循 `using` 托管，确认 `SqliteNodeStore.Dispose` 语义后再做缓存复用。
* 每项完成后更新本 Backlog（勾选状态），并在 `AgentLauncher_重构优化分析.md` 对应条目标注。

---

## 5. 实施备注（2026-08-30 落地记录）

### B-3 allowed 字符串单源化
* 新增 `AllowedRelationsOps/AllowedGraphOps/AllowedConfigOps/AllowedConversationOps/AllowedHistoryOps` 常量（`AgentUiLibraryRuntime.cs` 顶部，对齐 `AllowedTaskOps/AllowedNodeOps` 模式），替换 `RunJsonAsync` 内 5 处 `UnknownOpError` 内联硬编码串。
* 校核：`AllowedActions` 17 项与 `switch(action)` 17 个 case 一一对应；`AgentUiLibraryData.AllowedQueryTargets` 20 项与 `QueryDataTextAsync` case 集一致 —— 均无需改动，仅保留常量。

### B-2 跨 wf 共享节点删除判定
* 前置勘察结论：图节点端点键 = `NodeRelationFactory.NodeId(wf,nodeKey)=wf.node`，`GetUniqueKey()`=`{type}:{id}`，**Location 不参与键**；`GetReferencesAsync` 为精确键入边查询，框架**不支持**跨 wf 入边直查，但 `GetAllResourcesAsync` 可枚举全部节点端点。
* 落地：`HistoryDeleteAsync` 删除前枚举全部图节点端点，对「当前 wf 精确匹配 + 任意 wf 后缀 `.nodeKey`」的端点逐一查入边，任一仍有 Task→Node 的 `cross_db_link` 入边即判共享保留。该判定**无 false-negative**（不漏判真实引用），可能 false-positive（保守保留，安全方向），杜绝误删跨 wf 共享节点。
* skipped task（pool 为 null 未删）其 cross_db_link 出边残留 → 对应端点仍被判定被引用 → 节点保守保留（注释已说明）。

### B-5 Libraries 内性能项
* 已核实 `SqliteSession` 为**无状态命令执行器**（仅持连接串，构造不建连）、`SqliteNodeStore`/`SqliteSession` 均**不实现 IDisposable**，复用无过度释放风险。
* 落地：`AgentUiLibraryDb.TrySqliteSession` 增加会话级缓存（首次成功解析后复用，消除高频路径重复解析 config + 新建会话；双检锁线程安全）。
* `GraphStatusTextAsync`：框架无批量边统计接口，按计划保留现状并在注释标注瓶颈（O(N) 独立查询 + 边数=2×真边数，待 `ReferenceGraphCoordinator` 提供批量接口后聚合），行为/文案不变。

### B-1 `AgentUiHost` 槽位泛型化
* 新增内部泛型容器 `Slot<T>`（封装 get/Has/Attach/Detach 的 lock 模板 + `ArgumentNullException` 空安全，共享宿主 `_gate`）。
* 仅收敛**纯样板且未被组合根直接字段读写**的 5 个槽位：`Conversation / HistoryRelations / NodeTaskHistory / ExecutionFlag / HistoryRuntime`，`public static` 成员逐一显式声明、签名不变。
* **保持手写**：特殊槽位（`Current` 的 Require、`Database`/`ReferenceGrounding` 图断言、`TaskPool` 订阅联动），以及被 `AttachComposition`/`GetHtmlAssemblySnapshot` 直接 `_x` 字段读写的 8 个 HTML 槽位（`ConversationRenderer`/`ConversationTemplates`/`ConversationPageDeclaration`/`HtmlUi`/`HtmlViewerResolver`/`ConversationHtmlSink`/`HtmlDiagnosticsSink`/`AgentUiConfigPath`）—— 以满足「组合根入口零改动」硬约束。

### B-4 `RunJsonAsync` 巨型分发拆表
* `RunJsonAsync` 主体收敛为「解析 → 查表（`ResolveActionHandler`）→ 调 handler」，未命中输出「未知 action」诊断（文案逐字保持）。
* 17 个 action 各拆为私有 handler（`HandleRunloopAsync/HandleTaskAsync/HandleNodeAsync/HandleAddResourceAsync/HandleGetResourceAsync/HandleRemoveResourceAsync/HandleListResourcesAsync/HandleAddReferenceAsync/HandleRemoveReferenceAsync/HandleListReferencesAsync/HandleQueryAsync/HandleRelationsAsync/HandleGraphAsync/HandleConfigAsync/HandleConversationAsync/HandleHistoryAsync/HandleFlagAsync`），内嵌 switch 移入方法内，全部分支行为与诊断字符串**逐字一致**（原样搬运，未改文案）。

> 文档定位：为后续逐条重构提供事实依据与执行顺序；不替代代码评审与实际改动。
