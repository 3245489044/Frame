# 节点发现与精炼 MCP：协同式上下文组装架构设计

> 定位：本设计把「如何可靠地把相关节点选择性组装进每轮上下文」及「跨介质引用边一致性」收敛为**一个独立 MCP 服务器**，其过滤/精炼/一致性逻辑以**抽象层**形式承载，**不修改现有 Agent(C#) 框架**。配套现有架构见《节点即存储单元：Agent 三线存储架构介绍》与 `TypeScriptPrj/TsWorkspace/MCPBridge`。

---

## 1. 背景与问题

现有存储模型 = `ur_tasks`(薄状态根) + `ur_nodes`(数据节点) + LiteDB(引用边/图)。Task 文本有界、数据外置。但「每轮给 LLM 什么」的**发现机制**与「引用边跨介质一致性」存在两处结构性缺口：

### 1.1 发现机制 = 全量透传 + 静态键过滤，而非精炼

现状 `ResolveInputsAsync` 的组装路径：

1. `EnumerateOutputsAsync(workflowId)` 拉取**整个命名空间**的全部节点输出；
2. `vars.Snapshot()` 再并入数据空间；
3. 按节点声明的 `ParameterKeys` 静态过滤后交给模型。

结构缺陷：

- **无 task 作用域**：节点存储键为 `nodes.{workflowId}.{nodeKey}`，`RoundStepInvoker` 把各 task 钉死在同一 `WorkflowId`，所有 task 的节点混在同一命名空间 —— 一次枚举把**别的 task 的节点**也纳入，属跨 task 污染。
- **无相关性/预算裁剪**：`ParameterKeys` 是静态声明，「给什么」不由相关性、时效、预算决定，框架只「透传全量 + 过滤」。
- **LLM 无「取用/变更」界面**：模型不能声明「本轮要哪些节点、要更新哪些」，无「框架与 LLM 共同决定节点更新」的分工。

### 1.2 引用边 = 跨介质最终一致，读侧不设防

LiteDB 边与 SQLite 实体无单库原子性，依赖「写透同步 + `diff(删残留/补缺失)` + `RebuildAllAsync` 全量重建」。**读侧**（正是组装路径）不校验边的新鲜度/存在性，崩溃窗口期会拿「旧边/缺边」构图。

---

## 2. 设计目标与原则

1. **独立 MCP，不进框架**：过滤/精炼/构图/一致性作为**抽象层**置于 MCP 服务器内；`ResolveInputsAsync` 等现有代码**不改动**，仅通过**适配器**把「我要组装上下文」的调用方接到本 MCP。
2. **框架给「最精炼」，LLM 决定「取用」**：框架做图投影与预算注入，模型做结构化取用声明；节点更新 = 「LLM 提议 + 框架裁决落地」。
3. **读时自愈取代对账兜底**：内存图先行「读即准」，边/节点不一致在读入口就地修复；全量重建仅留作崩溃兜底。
4. **任务有界性保持**：task 文本仍有界，相关数据经本 MCP 按需 `project → select → compose` 注入。

---

## 3. 总体架构：MCP 在「存储」与「LLM」之间

```
                      ┌────────────────────────────────────┐
   LLM (Agent 循环)    │      NodeDiscovery MCP (抽象层)     │
     每轮需要上下文 ──► │ ┌──────────┐ ┌──────────┐ ┌──────┐ │
                      │ │ projection│ │ selection│ │feedback││
                      │ └────▲─────┘ └────▲─────┘ └──▲───┘ │
                      │      │  read 边    │ 注入预算  │ 校验  │
                      └──────┼────────────┼──────────┼──────┘
                             │            │          │
                   ┌─────────▼────────────▼──────────▼──────┐
                   │ 存储层：ur_nodes(SQLite) + 引用图(LiteDB) │
                   └─────────────────────────────────────────┘
```

- MCP 只对**存储层**做读/写，不感知 Agent 循环范式（配置驱动 / 自驱同等适用）。
- 现有 C# 组装路径保持不动；需要时经适配器把本 MCP 作为「上下文组装」的唯一入口替换原枚举。

---

## 4. 抽象层契约：三层流水线

> 抽象层 = MCP 内部的一组**纯接口** + 默认实现。接口稳定、实现可替换（换存储介质/换投影策略不触及框架）。

### 4.1 projection —— 投影层（框架，确定性）

输入：`taskKey` + 当前任务状态（Round / 意图指针 / 最近引用的节点）。
输出：**候选清单（不携带载荷）**，每项仅含：

```
nodeKey | port | dir(input/output) | 类型 | 大小 | 版本(rev) | 一句话摘要
```

规则（纯函数，可测）：
- **task 作用域寻址**：候选只取「本 task 经引用边可达」的节点（`task→node` + `node→node`），根除跨 task 污染。节点寻址升级为 `nodes.{wf}.{task}.{node}` 或由 `task→node` 边做主索引。
- **相关性裁剪**：按边 strength、时效(rev 新旧)、类型过滤，舍弃无关分支。
- **预算外露**：候选带累计 token 估算，供后续注入限额。

### 4.2 selection —— 选择层（LLM，动态）

输入：投影层候选清单（仅索引，不含载荷）。
输出：**结构化取用声明**：

```jsonc
{
  "select": ["n1", "n2"],          // 本轮要加载哪个节点的载荷
  "compose": { "prompt": ["n1", "n2"] }, // 按端口组装为模板槽
  "drop": ["n3"]                    // 本轮明确不需要，降低注入
}
```

框架据此：
- **预算内注入**：只加载命中的载荷，经**压缩**后注入上下文；受每轮 `token_budget`（框架侧强制）约束。
- 实现「框架给最精炼」：**精选 + 限额**，而非全量。

### 4.3 feedback —— 反馈层（框架 + LLM 共同决定更新）

输入：本轮执行结果 + LLM 变更意图。
输出：落库 + 图边更新 + 版本递增。

- LLM **不直接写库**，只提交变更意图；框架做校验、落地、仲裁。
- 变更语义：`upsert(node, value)` / `relate(from,to,strength)` / `disconnect(a,b)`。
- 版本号递增，驱动一致性收敛（见 §6）。

### 4.4 节点状态机（双正交）

`NodeRecord.Status` 在原设计中是裸字符串，无状态机约束，导致：
- `partial`（部分写入）能否参与 selection 未定义；
- `faulted` 后是否可重试未定义；
- `archived`/`expired` 等终态与 `completed` 混淆，污染候选清单。

引入**双正交状态机**：业务归档状态（ArchiveStatus）决定是否参与 selection，工作流执行状态（ExecStatus）独立流转。

**ArchiveStatus 取值**：

| 状态 | 含义 | 是否参与 selection |
|---|---|---|
| `active` | 写入中或待归档 | 是（限定场景） |
| `completed` | 归档完成 | 是（默认） |
| `faulted` | 归档失败 | 否 |
| `partial` | 部分写入（IPC 直采/重跑中） | 否 |
| `archived` | 已被合并到 archive 节点，仅保留索引 | 否 |
| `expired` | 超期，待 purge | 否 |

**允许迁移路径**：

```
active ──► completed ──► archived (终态)
  │           │
  ├─► partial ─► completed
  │      │
  │      └─► faulted ──► active (重试)
  │
  └─► faulted ──► active (重试)

completed ──► expired ──► purged
```

**状态变迁留痕**：每次迁移通过 `status_succession` 边（见 §6.5）记录 `from → to` 的源端 Rev 与目标端 Rev，便于回溯故障路径与重试历史。

**与 ExecStatus 的关系**：ExecStatus（Pending/Running/Completed/Failed/Skipped）描述工作流执行进度，与 ArchiveStatus 正交——执行完成 ≠ 归档完成。projection 同时过滤两个状态机。

### 4.5 版本号机制（__rev + sourceRev/targetRev）

原设计第 6 节提到 `__rev` 与 `sourceRev/targetRev`，但未给出递增规则与冲突解决设计。本节补全。

**节点版本号**：

- `NodeRecord.Rev`（int）：每次 upsert 递增；幂等覆写也递增，以驱动增量对账。
- 旧节点 Rev=0 视为"未版本化"，首次对账时强制重写为 1。
- Rev 仅递增不回退；archive 节点的 Rev 从被合并节点最大 Rev + 1 起算。

**边版本号**：

- `ReferenceRecord.SourceRev` / `TargetRev`：写边时绑定源端/目标端当前 Rev。
- 对账规则升级：
  - **原**：全量 diff（删残留/补缺失），见 `ReferenceGrounding.ReconcileAsync`。
  - **升级后**：按 `__rev` 差异增量重放；`sourceRev/targetRev` 与节点当前 `Rev` 不一致才需要校验，未变更节点跳过。
  - 版本冲突按"后写赢"+ 诊断事件 `Node_VersionConflict`。

**冲突解决**：

- 同一 NodeKey 并发 upsert：取 `UpdatedAt` 最晚者，丢失方写入 `node_derived_from` 边保留可追溯性。
- 边的 sourceRev/targetRev 落后于节点当前 Rev > 2：触发 `node_validate` 自愈，重写边绑定最新 Rev。

**与 §6 一致性的关系**：版本号是增量收敛的基础；`RebuildAllAsync` 全量重建后所有节点 Rev 重置为 1，边版本号同步重写。

---

## 5. MCP 工具集设计

对齐 MCPBridge 约定：`tools-definitions/*.json` 声明，`src/tools/*.ts` 实现，`mcp-bridge.config.json` 注册。本 MCP 名 `node-discovery`。

### 5.1 工具定义

| 工具名 | 作用 | 层 |
|---|---|---|
| `node_project` | 生成候选清单（索引，含摘要与预算） | projection |
| `node_select` | 提交 LLM 取用声明，返回预算内注入的压缩载荷 | selection |
| `node_compose` | 把选定节点按端口组织为提示词模板槽 | selection |
| `node_upsert` | 提交变更意图，框架校验后落库并递增版本 | feedback |
| `node_relate` | 写/改引用边（strength、时效） | feedback |
| `node_drop` / `node_disconnect` | 断开关联，标记弃用 | feedback |
| `node_validate` | 读入口自愈校验（边↔节点对账） | 一致性 |
| `node_project_with_budget` | 带显式 token 上限的投影 + 注入一体 | projection |

示例 def `node_project.json`：

```jsonc
{
  "name": "node_project",
  "handler": "node-project",
  "template": "with-args",
  "inputSchema": {
    "type": "object",
    "properties": {
      "taskKey": { "type": "string" },
      "round": { "type": "number" }
    },
    "required": ["taskKey"]
  }
}
```

### 5.2 handler 分层

`src/tools/` 下实现统一走**抽象层接口**，不直接读写仓储：

- `context/`：投影选择器（Strategy 化，默认 RelevanceProjector）
- `inject/`：预算注入器 + 压缩器（Compressor）
- `consistency/`：读时校验器 + 增量收敛器
- 仓储访问经 `ports/`（见 §7），换 SQLite/LiteDB 只换 port 实现。

### 5.3 生命周期治理工具集

原设计 §5.1 只有 8 个工具，覆盖 projection/selection/feedback/一致性四类，但**生命周期治理空白**——长会话累积膨胀、归档合并、超期清理均无工具承载。新增 4 个工具：

| 工具名 | 作用 | 关键边操作 |
|---|---|---|
| `node_archive` | 将一批旧轮次节点合并到单条 `archive` 节点 | 写 `archive_of` 边（archive → 每个原始节点）；原节点置 `archived` 状态 |
| `node_compact` | 按会话总大小阈值触发压缩（封装 archive + purge） | 同上 + 调用 `node_purge` 清理 archived 节点 |
| `node_purge` | 按 `UpdatedAt` 清理 `expired` 节点 | 删节点 + `ApplyRelationEdgesAsync` 增量收敛边（删 dangling 边） |
| `node_lifecycle` | 查询节点生命周期视图（轮次链 + 状态变迁链 + archive 关系） | 只读图遍历，按 `round_sequence + status_succession + archive_of` 三类边组合查询 |

**`node_archive` 工具定义示例**：

```jsonc
{
  "name": "node_archive",
  "handler": "node-archive",
  "template": "with-args",
  "inputSchema": {
    "type": "object",
    "properties": {
      "threadId": { "type": "string" },
      "roundsBefore": { "type": "number", "description": "归档 N 轮之前的所有轮次" },
      "archiveNodeKey": { "type": "string", "description": "归档节点 NodeKey，缺省自动生成" }
    },
    "required": ["threadId", "roundsBefore"]
  }
}
```

**handler 关键逻辑**（伪码）：

```typescript
class NodeArchiveHandler {
  async handle(args: ArchiveArgs): Promise<ArchiveResult> {
    // 1) 走 LiteDB 边查"threadId 下 round_sequence 链中 round < N 的 completed 节点"
    const oldRounds = await this.graph.queryRoundsBefore(args.threadId, args.roundsBefore);

    // 2) 合并载荷到 archive 节点
    const archiveKey = args.archiveNodeKey ?? `archive.${args.threadId}.before.${args.roundsBefore}`;
    const mergedPayload = await this.mergePayloads(oldRounds);
    const archiveRev = await this.nodes.upsert({
      nodeKey: archiveKey,
      nodeType: "archive",
      threadId: args.threadId,
      status: "completed",
      payloadJson: mergedPayload,
    });

    // 3) 写 archive_of 边（archive → 每个原节点），绑定 sourceRev
    for (const r of oldRounds) {
      await this.graph.addReference({
        from: archiveKey, to: r.nodeKey,
        refType: "archive_of",
        sourceRev: archiveRev, targetRev: r.rev,
      });
      // 4) 原节点置 archived
      await this.nodes.updateStatus(r.nodeKey, "archived");
    }

    // 5) 增量收敛：清理 round_sequence 链中 dangling 边
    await this.graph.reconcile(archiveKey);
    return { archivedCount: oldRounds.length, archiveNodeKey: archiveKey };
  }
}
```

**与原 §5.1 工具集的关系**：4 个新工具与原 8 个工具平级注册到 `tools-definitions/`，handler 实现位于 `src/tools/lifecycle/` 子目录，复用 `ports/` 仓储适配器。

---

## 6. 引用边一致性的 MCP 化

延续「内存图先行 + 读时自愈 + 增量收敛」，由 MCP 内部承担：

1. **内存图先行（读即准）**：业务成功返回前，边先在内存热邻接就位；LiteDB 持久边靠写后重放/WAL 异步落。`node_select/node_compose` 读到的内存边**永远正确**。
2. **读时校验自愈**：`node_validate` 在「边→载荷」落地时做存在性检查；发现「边在节点缺 / 节点在边旧」就地幂等 upsert 修复，**不等后台对账**。
3. **版本增量收敛**：实体带递增 `__rev`，边带 `sourceRev/targetRev`；对账从「全量 diff」降为「按版本差异增量重放」，落 checkpoint 记忆收敛位。`RebuildAllAsync` 仅作崩溃兜底，正常运转始终增量。
4. **扇出分层**：task→node（低频引用，仅集合变化时写）与 node→node（运行频次写）**分两层**，避免高频执行抖动重写全图。

### 6.5 RefType 扩展体系

原设计中 RefType 仅有 `keymapping_to_node` 与 `node_to_node` 两类数据流边，加上工作流静态边 `Owns/Calls`。Cline 落库方案引入的复杂节点控制逻辑（轮次时序、跨库链接、归档合并、状态变迁、版本派生）需要新的边类型承载。

**新增 6 类 RefType**：

| RefType | 语义 | 端点 | 写入时机 |
|---|---|---|---|
| `round_sequence` | 轮次时序：round.N → round.N+1 | node → node | 每轮归档时写前一轮 → 本轮 |
| `resume_source` | 本轮 → resume 来源轮次 | node → node | 故障重试 resume 时留痕 |
| `archive_of` | archive 节点 → 被合并的原始节点 | archive → node | `node_archive` 工具触发 |
| `status_succession` | 状态变迁：partial → completed 的来源 | node → node | 状态机迁移时留痕 |
| `node_derived_from` | 派生：IPC 直采版本 ← 文件版本 | node → node | 阶段 3 事件流直采时 |
| `cross_db_link` | ur_nodes ↔ ur_tasks 跨库链接 | task → node | 跨库关联建立时 |

**端点 ResourceType 扩展**（在 `NodeResourceTypes` 中）：

```csharp
public static class NodeResourceTypes
{
    public const string Node = "node";
    public const string Workflow = "workflow";
    public const string KeyMapping = "keymapping";
    // 新增
    public const string Task    = "task";       // ur_tasks 端点（跨库链接用）
    public const string Archive = "archive";    // 归档节点（合并产物）
    public const string Round   = "round";      // 轮次端点（语义清晰，可单独寻址）
}
```

**与已有实现的关系**：

- 复用 [ReferenceGraphCoordinator.AddReferenceAsync](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Graph/ReferenceGraphCoordinator.cs#L194-L220) 的幂等 upsert 机制（先删后加），新 RefType 零成本接入。
- 复用 [ApplyRelationEdgesAsync](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Graph/ReferenceGraphCoordinator.cs#L393-L415) 增量对账收敛，archive/purge 时连带收敛新边类型。
- 扩展 [NodeRelationDeriver.ResolveRelationType](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/NodeRelationDeriver.cs#L176-L184) 支持新 RefType 解析。
- 工作流静态边 `Owns/Calls` 已证明独立 RefType 写路径存在（[NodeRelationDeriver.cs#L145-L174](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/NodeRelationDeriver.cs#L145-L174)），新边类型走相同模式。

**RefType 分类**：

```
数据流边（已有）：keymapping_to_node, node_to_node
工作流静态边（已有）：Owns, Calls
时序与生命周期边（新增）：round_sequence, resume_source, archive_of, status_succession, node_derived_from
跨库边（新增）：cross_db_link
```

---

## 7. 与现有框架的对接（不改框架）

抽象层通过 **port（适配器）** 暴露，对接方式二选一：

- **方案 A（默认，零侵入）**：现有 `ResolveInputsAsync` 不碰。新增一个**旁路适配器**，当配置 `nodeDiscovery.enabled=true` 时，AgENT 循环经 `McpBridge` 调用 `node_project → node_select → node_compose` 取得组装上下文；否则回退原逻辑。改造只在**调用方**，不触框架内部。
- **方案 B（收敛入口）**：将来把「组装」统一收口到本 MCP，`ResolveInputsAsync` 内部的枚举改为「一次 `node_select`」，其余框架代码不动。

C# 侧仅需一个轻量 `INodeDiscoveryPort`（TaskKey → 注入文本 + 更新意图回执），由 `McpBridge` 的 `invoke-ps-script` 同构本地工具或 HTTP/SSE 转发承载。

### 7.1 LiteDB 引用边作为关系权威

原设计中节点关系的存储职责未明确划分，`NodeRecord.RelationsJson` 与 LiteDB 边存在重复语义。本节确立**存储职责硬分工**，消除歧义。

**存储职责硬分工**：

| 介质 | 集合 | 承载内容 | 不承载 |
|---|---|---|---|
| SQLite | `ur_nodes` | 节点载荷（PayloadJson）、状态（Status）、版本（Rev）、时间戳 | 任何关系语义 |
| LiteDB | `ur_references` | 所有节点关系（时序/引用/归属/合并/跨库/状态变迁） | 节点载荷 |
| LiteDB | `ur_endpoints` | 端点注册（task/node/workflow/archive 等资源类型） | — |
| NodeRecord.RelationsJson | — | **仅作声明式快照备份**（崩溃恢复用） | 运行时权威 |

**核心原则**：节点关系**唯一权威在 LiteDB**；RelationsJson 退化为"快照备份"，仅用于 `RebuildAllAsync` 全量重建场景。这与项目公理 `DerivedDataAutoMaintenance`（节点关系边 = project(映射, 溯源)）一致——推导来源在 SQL，权威边在 LiteDB。

**与已有实现的对齐**：

- [ReferenceGraphCoordinator](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Graph/ReferenceGraphCoordinator.cs#L19-L35) 已实现 LiteDB 边集合 `ur_references` + 端点集合 `ur_endpoints` + 内存热邻接图 `_graph`，本设计直接复用。
- [NodeRelationDeriver](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/NodeRelationDeriver.cs#L75-L95) 的 `RelationsJson` 序列化/反序列化保留，但定位调整为"崩溃恢复快照"。
- [ReferenceGrounding.RebuildAllAsync](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/ReferenceGrounding.cs#L205-L226) 全量重建流程不变，作为崩溃兜底。
- 运行时所有关系查询（projection/selection/feedback）一律走 LiteDB 边，不再解析 RelationsJson。

**Cline 落库方案的对齐**：

[Cline对话数据落库重构方案.md](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/doc/Cline对话数据落库重构方案.md) 中 `RelationsJson` 字段携带 `{"taskKey":"{sessionId}","conversationLogArchive":"__conversation"}` 的设计，升级为通过 `cross_db_link` 边声明跨库关系，不再依赖字符串嵌入。详见 §11.5。

---

## 8. 配置与部署

`mcp-bridge.config.json` 增加 `servers:[{name:"node-discovery", ...}]`，本地工具经 `tools-definitions/` 声明即自动注册（复用 `localToolRegistry`）。默认 `docker.enabled=false`，本 MCP 以宿主进程 stdio 常驻，无容器依赖。

配置项：

```jsonc
{
  "nodeDiscovery": {
    "enabled": true,
    "defaultBudgetTokens": 6000,
    "compressor": "tiktoken-truncate",
    "projection": "relevance",
    "readVerify": true,
    "incrementalReconcile": true
  }
}
```

---

## 9. 落地路线

| 阶段 | 内容 | 说明 |
|---|---|---|
| P1 | task 作用域寻址 + 候选投影 | 根除跨 task 污染；MCP 只读，不动框架 |
| P1 | 预算内注入 + 压缩 | 让框架「给得更少但更对」 |
| P2 | selection 取用声明协议 | 对齐「框架+LLM 共同决定」分工 |
| P2 | node_validate 读时自愈 | 一致性从兜底升级为自愈 |
| P3 | 版本增量收敛 + checkpoint | 替换全量 diff 对账 |
| P3 | 方案 B 收口 | 统一组装入口，仍不改框架内部 |

---

## 10. 风险与边界

- **失忆风险守恒**：数据外置后，若投影候选不完备 →「数据在库里、模型看不到」。缓解：task→node 边必须可靠、投影不应裁剪掉意图指针指向的节点。
- **双执行路径**：方案 A 下「MCP 组装」与「原枚举」并存，需以配置开关保证二选一，避免同一轮混用。
- **跨介质原子性**：无法单库原子，靠「内存图先行 + 读时自愈」把不一致暴露面压到读路径并就地修复；不承诺强原子。
- **顺序与注入时点**：`node_select` 需在模型调用前完成；并发/中断下的取用声明冲突按最近 rev 收敛。
- **开销**：图投影/内存热邻接带来查询成本，需缓存候选与摘要；预算注入限制每轮载荷。

---

## 11. 结论

把「节点发现与精炼」沉淀为一个独立 MCP（抽象层），既把两个命门（发现机制、引用边一致性）从「改框架」解耦为「换能力」，又让**存储向外的能力**可在 C# 之外演进、可独立测试、可配置开关。配合「task 作用域寻址 + 候选投影 + LLM 取用 + 预算注入 + 读时自愈」五件套，实现目标态：**框架给最精炼，模型决定取用，框架与 LLM 共同决定节点更新**。

### 11.5 Cline 落库方案对齐

[Cline对话数据落库重构方案.md](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/doc/Cline对话数据落库重构方案.md) 中的复杂节点控制逻辑（轮次时序、跨库联查、resume 回灌、状态流转、归档治理）当前以「字符串解析 + 全量枚举 + 隐式关联」承载。本节给出用 LiteDB 边替代字符串解析的对齐路径。

**对齐点 1：NodeKey 中的 round → LiteDB `round_sequence` 边**

原方案 `LoadLatestAsync` 通过 `ListAsync` 全量枚举 + `ParseRound` 字符串解析找最近一轮：

```csharp
// 原方案（NodeStoreClineMessageArchive.cs）
var list = await _nodes.ListAsync(nodeType: NodeType, threadId: sessionId, limit: 1000);
var maxRound = list.Select(r => ParseRound(r.Key)).Where(...).Max();
```

升级为图遍历：

```csharp
// 升级后：走 LiteDB 边查询 sessionId 端点 → 沿 round_sequence 边找终点
var roundNodes = await _graph.QueryTerminalNodesAsync(
    endpoint: sessionId,
    refType: "round_sequence",
    statusFilter: "completed",
    ct: ct);
return roundNodes.FirstOrDefault()?.PayloadJson;
```

归档时同步写边（`NodeStoreClineMessageArchive.ArchiveAsync` 增强）：

```csharp
public async Task ArchiveAsync(string sessionId, int round, string messagesJson, string status, CancellationToken ct = default)
{
    var record = new NodeRecord { /* ... */ };
    var rev = await _nodes.PutAsync(record, ct);

    // 写 round_sequence 边：上一轮 → 本轮
    if (round > 1)
    {
        var prevKey = BuildNodeKey(sessionId, round - 1);
        await _graph.AddReferenceAsync(
            source: NodeEndpoint(sessionId, prevKey),
            target: NodeEndpoint(sessionId, record.NodeKey),
            refType: "round_sequence",
            strength: 1.0,
            sourceRev: await _nodes.GetRevAsync(prevKey, ct),
            targetRev: rev,
            ct: ct);
    }

    // 写 cross_db_link 边：ur_tasks.{sessionId} → ur_nodes 本轮节点（替代 RelationsJson 中的 taskKey）
    await _graph.AddReferenceAsync(
        source: TaskEndpoint(sessionId),
        target: NodeEndpoint(sessionId, record.NodeKey),
        refType: "cross_db_link",
        strength: 1.0,
        sourceRev: 0,    // ur_tasks 不参与 __rev
        targetRev: rev,
        ct: ct);
}
```

**对齐点 2：resume 回灌控制流明确化**

原方案"回灌最近一轮"语义模糊（最大 round？最新 UpdatedAt？状态过滤？）。升级语义：

| 场景 | 选择策略 | 边查询 |
|---|---|---|
| 正常 resume | 最近一轮 `completed` | `round_sequence` 链终点 + Status=completed |
| 故障重试 | 最近一轮 `faulted`/`partial` + 写 `resume_source` 边 | 同上，状态放宽 + 留 resume_source 痕迹 |
| 跨进程恢复 | 沿 `status_succession` 边找最终态 | BFS over `status_succession` |

**对齐点 3：跨库联查升级**

原方案 SQL JOIN 跨库联查替换为 LiteDB 图遍历：

```typescript
// 原：SQLite JOIN（性能差，跨库事务问题）
// SELECT ... FROM ur_tasks t LEFT JOIN ur_nodes c ON json_extract(c.Json,'$.ThreadId')=t.NodeKey ...

// 升级：LiteDB 图遍历
async function queryClineRoundsForTask(taskKey: string): Promise<RoundView[]> {
  // task 端点 → cross_db_link 边 → ur_nodes 节点
  const rounds = await graph.outgoing(taskKey, ["cross_db_link"]);
  // 每轮节点 → round_sequence 边 → 时序链
  return graph.bfs(rounds, ["round_sequence"], { sorted: true });
}
```

**对齐点 4：阶段 3「事件流直采」对齐**

原方案阶段 3 改造时，新增 `node_derived_from` 边：

```
IPC 直采版本 ← node_derived_from ← 文件版本
```

让两版本可追溯，回滚时通过边找到原文件版本。

**对齐落地路线**：

| 阶段 | 内容 | 验证标准 |
|---|---|---|
| P3.0 | `NodeStoreClineMessageArchive` 写 `round_sequence` + `cross_db_link` 边 | 归档后 LiteDB 出现对应边 |
| P3.1 | `LoadLatestAsync` 替换为图遍历 | 同等结果；性能对比 > 30% 提升（单会话 100 轮） |
| P3.2 | 跨库联查 SQL JOIN → 图遍历 | 联查响应 < 50ms（原 SQL JOIN 基线对比） |

**与 Cline 落库方案阶段 1 的并行关系**：

Cline 落库方案阶段 1.7 归档逻辑可先落地（不带边写入），本设计 P3.0 时再补边写入，向后兼容。两方案的阶段划分互不冲突，可并行推进。