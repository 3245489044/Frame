# FsSink — 通用 Agent SDK 接入框架化设计（从 Cline 专用走向多 Agent 通用）

> **版本**：v0.1（框架化设计稿）　**日期**：2026-08-23
> **现状**：FsSink 已完成 Phase 1（Cline 专用内存落盘 + 磁盘零残留 + 回传归档），本文档分析其**通用化演进路径**，
> 目标让后续接入 Claude / Codex / Trae 等 Agent SDK 时**规范、完善、低侵入**。
> **落位**：实现于 `TypeScriptPrj/TsWorkspace/FsSink/`；本文档放 `docs/analysis/2026/8/8.23/`

---

## 1. 现状盘点：什么已通用、什么仍是 Cline 专用

### 1.1 已经通用的核心（可复用骨架）

| 模块 | 通用性 | 说明 |
|------|--------|------|
| `FsRouter`（`core/fs-router.ts`） | ✅ 完全通用 | patch `node:fs`/`fs/promises`，按路径分发；与具体 SDK 无关 |
| `PathRouter` + `RouteSpec`（`core/route.ts`） | ✅ 完全通用 | prefix / glob / re 三种匹配器，路径归一（Windows） |
| `MemoryVolume`（`core/memory-volume.ts`） | ✅ 完全通用 | 内存卷实现 `Sink`，`mkdir→write→read→stat` 闭环 |
| `Sink` 接口（`core/sink.ts`） | ✅ 完全通用 | 拦截目标抽象，含写/读/镜像三组能力 |
| `DbMirror`（`mirror/db-mirror.ts`） | ⚠️ 半通用 | 已抽象 `DbBackend`，但**解析逻辑（messages/manifest 形状）仍是 Cline 假设** |
| `DbBackend` / `InMemoryDbBackend`（`backend/`） | ✅ 完全通用 | 可插拔存储 SPI，Phase 2 对接 C# 持久化 |
| `FsSink` 门面（`index.ts`） | ✅ 完全通用 | `patchFs`/`unpatchFs`/`getMessages`/`flushAll` |

### 1.2 仍是 Cline 专用（需要"适配器化"的部分）

| 位置 | 现状 | 通用化痛点 |
|------|------|-----------|
| `cline/cline-route.ts` | `clineRoutes({home})` 硬编码 Cline 的 `data/sessions/` 布局、`*.messages.json`/`*.manifest.json`/`*.compaction.json` | 每个 SDK 数据布局不同，需**声明式路由**而非写死 |
| `db-mirror.ts` `extractSessionIdFromPath` | 硬编码 `/sessions/<id>/` 正则 | 会话 ID 提取规则因 SDK 而异 |
| `db-mirror.ts` `mirror()` | 假设 messages.json 是 `{messages:[...]}`、manifest 是会话元数据 | 消息/元数据的**语义提取**因 SDK 而异 |
| `sdk-transport.ts`（ClineApi） | 内嵌 `fsSink` 开关、`messagesFromSink`、`getAuthoritativeResultText` 的读源降级 | 读源/归档逻辑与 Cline 结果模型耦合 |
| `ClineRunResult.archivedMessages` | 全量消息随终帧回传（本批次新增） | 回传字段名/形状是 Cline 语义 |

### 1.3 已暴露的架构断层（本批次修复，沉淀为通用原则）

1. **FsSink 拦截写盘（磁盘零残留）与 C# 归档读磁盘矛盾** → 修复：结果回传 `archivedMessages`，归档不再依赖磁盘。
2. **路由根与 SDK 实际写盘根不一致（miss → 落 `~/.cline`）** → 修复：同时拦截显式根与默认根。
3. **SDK 可能无视 `CLINE_HOME` 按自身 globalStoragePath 写盘** → 通用原则：路由规则必须覆盖"显式根 + 默认根"两个候选。


---

## 2. 通用框架目标（接入 Claude 等 SDK 的验收标准）

| 维度 | 目标 | 验收 |
|------|------|------|
| **低侵入** | 接入新 SDK 只写"适配器 + 路由声明"，不改 `FsRouter`/`MemoryVolume`/`FsSink` 核心 | 新增 SDK 零改动核心代码 |
| **规范** | 数据布局/消息形状/会话 ID/元数据语义统一走**契约化** | 无散落正则/硬编码路径 |
| **完善** | 磁盘零残留、SDK 读回闭环、结果落库、resume 回灌四件事在每 SDK 上**齐全** | 每 SDK 都过同一套验收用例 |
| **可观测** | 命中计数、路由 miss 告警、归档失败归因可见 | 有 `hits`/`miss`/`archive` 观测帧 |
| **可回退** | 每 SDK 的拦截可单独开关；未启用时行为与原 SDK 完全一致 | 开关关闭 → 走原 SDK 路径 |

---

## 3. 目标分层架构（通用版）

```
┌──────────────────────────────────────────────────────────────┐
│                    接入方（Agent SDK 子进程）                   │
│   Claude engine  │   Cline engine  │   Codex engine  │  ...    │
└──────┬────────────┴───────┬────────┴────────┬─────────────────┘
       │                    │                 │
       │   只调一个门面      │                 │
       ▼                    ▼                 ▼
┌──────────────────────────────────────────────────────────────┐
│                   AgentSinkHost（统一门面）                    │
│    patchFs(routes, {defaultRoot})  /  flushAll()  /           │
│    getMessages(sessionId)  /  getArtifacts(sessionId)  /      │
│    observe()  → 命中/放行/miss/归档 观测帧                     │
└───────┬─────────────────────┬────────────────────────────────┘
        │                     │
        ▼                     ▼
┌─────────────┐      ┌──────────────────────┐
│  PathRouter  │      │   MemoryVolume(s)    │   （均通用，现成）
│  (通用路由)   │─────▶│   (Sink 实现)        │
└──────┬──────┘      └──────────┬───────────┘
       │ 未命中→realFs 放行      │ 写事件
       ▼                        ▼
                        ┌──────────────────────┐
                        │   ArtifactExtractor  │  ← 新增：按 SDK 契约提取
                        │   (声明式解析器)       │    消息/元数据/会话ID
                        └──────────┬───────────┘
                                   ▼
                        ┌──────────────────────┐
                        │      DbBackend       │
                        │  InMemoryDb / Sqlite │
                        │  BridgeToCSharpDb    │  ← Phase 2
                        └──────────────────────┘
```

**关键新增抽象**：把当前 `DbMirror` 里"硬编码的 Cline 解析"外提为**按 SDK 契约驱动的 `ArtifactExtractor`**。

---

## 4. 核心抽象设计

### 4.1 `AgentRouteProvider`（声明式路由，替代写死 `clineRoutes`）

每个 SDK 提供一份**声明式路由契约**，`PathRouter` 消费，不感知 SDK：

```ts
/** 一条 SDK 会话产物的语义描述（替代 clineRoutes 里写死的 sessions/messages/manifest）。 */
export interface AgentArtifactSpec {
  /** 会话数据根（绝对路径，可多个候选根：显式隔离根 + SDK 默认根）。 */
  roots: string[];
  /** 会话产物文件 glob（相对任意根）。 */
  artifactGlobs: string[];          // 如 ["data/sessions/**/*.messages.json"]
  /** 会话 ID 提取（从命中路径 → sessionId）；由 SDK 适配器提供。 */
  extractSessionId: (path: string) => string | undefined;
  /** 该产物文件的语义类别。 */
  kind: "messages" | "session" | "ignored";
}

/** 路由工厂：声明式 → Route[]（sink 共享、mirror 按 kind 决定）。 */
export interface AgentRouteProvider {
  readonly name: string;            // "cline" | "claude" | "codex"
  buildRoutes(): Route[];
}
```

> 实现约定：`cline-route.ts` 重构为 `ClineRouteProvider implements AgentRouteProvider`；
> 新增 `ClaudeRouteProvider`/`CodexRouteProvider` 各自声明 roots/globs/提取器，**核心零改动**。

### 4.2 `ArtifactExtractor`（契约化解析，替代 DbMirror 硬编码）

```ts
/** 把内存卷快照条目解析为"可落库的数据单元"（消息数组 / 会话元数据）。 */
export interface ArtifactExtractor {
  readonly kind: "messages" | "session" | "ignored";
  /** 解析产物：返回可写入 DbBackend 的结构化数据；解析失败返回 undefined（待下次 flush）。 */
  parse(snapshot: SnapshotEntry): unknown | undefined;
}
```

- `ClineMessagesExtractor`：`{messages:[...]}` → 消息数组（替代现有 `mirror()` 的 if-else）。
- `ClineManifestExtractor`：manifest.json → 会话元数据。
- `ClaudeMessagesExtractor`：按 Claude SDK 的消息文件形状实现（仅新增一个类，不改 DbMirror）。
- `DbMirror` 简化为：订阅 Sink 写事件 → 查 `ArtifactExtractorRegistry`（按路径 kind 匹配）→ `parse` → `backend.write*`。

### 4.3 `DbBackend` 扩展（Phase 2：对接 C# 持久化）

现有 `DbBackend` SPI 已够用（`upsertSession`/`writeMessages`/`getMessages`）。通用化需补：
- **持久化后端**：`BridgeToCSharpDbBackend`（经 daemon IPC 桥接 C# agent.db / ur_nodes，替代 InMemoryDb）。
- **按 SDK 隔离命名空间**：不同 SDK 的 sessionId 可能冲突（Cline 的 id 与 Claude 的 id 都可能叫 `abc`）。建议 `writeMessages(sdk, sessionId, msgs)` 或后端层加 `sdk` 维度，避免跨 SDK 串数据。

### 4.4 `AgentSinkHost`（统一门面，替代裸 `FsSink`）

```ts
export class AgentSinkHost {
  constructor(providers: AgentRouteProvider[], db?: DbBackend) {}
  patchAll(): void;                       // 聚合所有 provider 路由 + 注册提取器
  unpatchAll(): void;
  getMessages(sdk: string, sessionId): Promise<unknown[] | undefined>;
  flushAll(): Promise<void>;
  observe(): SinkObserveFrame;            // { perRoute hits, perSdk misses, archives }
}
```

- **多 SDK 共存**：同进程可同时 patch Cline + Claude（不同路由前缀，互不干扰）。
- **观测帧**：`observe()` 返回每路由命中数、每 SDK miss 数（定位"又走了默认盘"问题）、归档失败数——对应 §1.3 断层 2/3 的可观测化。

### 4.5 结果回传的通用化（替代 `ClineRunResult.archivedMessages`）

Cline 的 `archivedMessages` 语义需要推广。建议引入统一结果基类字段（各 SDK 结果模型继承）：

```ts
/** 统一执行结果（SDK 无关），供宿主归档，不再读磁盘。 */
interface AgentRunArtifact {
  sessionId?: string;
  /** 本次运行的全部消息（若拦截/可提取则非空）。 */
  archivedMessages?: unknown[];
  /** 归档元信息（消息条数、来源、是否来自 FsSink）。 */
  archiveMeta?: { messageCount?: number; source: "sink" | "disk" | "aggregate" | "none" };
}
```

> Cline 侧：`ClineRunResult` 兼容保留 `archivedMessages`（已实现），同时对齐 `AgentRunArtifact` 约定；宿主统一消费 `archivedMessages`，弱化磁盘读取路径。

---

## 5. 接入新 SDK（以 Claude 为例）的规范步骤

> 目标：让"加一个 Agent"变成"加一个适配器目录 + 跑一套验收"，而非改核心。

### 5.1 六步接入法

| 步骤 | 动作 | 产出 | 是否改核心 |
|------|------|------|-----------|
| 1 | 新增 `claude/claude-route.ts`：声明 roots（显式隔离根 + SDK 默认根）、artifactGlobs、sessionId 提取、kind | `ClaudeRouteProvider` | 否 |
| 2 | 新增 `claude/claude-extractor.ts`：按 Claude 消息文件形状实现 `ArtifactExtractor` | 消息/会话提取器 | 否 |
| 3 | 新增 `ClaudeApi` 适配器：把 SDK 写盘根切到隔离根（等效 Cline 的 `CLINE_HOME` 注入） | SDK 隔离写盘 | 否 |
| 4 | 结果回传：Claude 结果模型对齐 `AgentRunArtifact`，`run()` 里 `fsSink.flushAll()` 后回带 `archivedMessages` | 落库数据源 | 否 |
| 5 | 宿主归档：统一消费 `archivedMessages`（C# 侧归档逻辑已通用，复用 §4.4 门面） | 落库闭环 | 否 |
| 6 | 跑通用验收用例 + 观测帧确认磁盘零残留 / miss=0 | 验收 | 否 |

### 5.2 通用验收用例（每 SDK 必过）

| # | 用例 | 断言 |
|---|------|------|
| 1 | 命中写 → 磁盘零残留 | 静态 `fs.existsSync` false，内存卷 true |
| 2 | 读回闭环 | 内存卷读到与写入一致内容 |
| 3 | 放行未命中 → 真实落盘 | 工具/源码路径照常写盘 |
| 4 | 流式写 close → 可见 | createWriteStream 后内存卷可见、磁盘不可见 |
| 5 | 目录递归 mkdir | 命中前缀内存成立、磁盘不落 |
| 6 | 提取 + 落库 | `flushAll` 后 `getMessages(sdk, sessionId)` 返回正确消息 |
| 7 | miss 告警 | `observe()` 显示 miss=0（写盘全被拦截） |
| 8 | 默认根覆盖 | SDK 回退写默认根（如 Claude 的 `~/.claude`）也被拦截 |

> 这 8 条直接复用现有 `fs-router.test.ts` 骨架，参数化 `provider` 即可复用。

---

## 6. 现状 → 通用框架的迁移映射（分阶段）

### Phase 2（近期，核心重构 + C# 桥接）

| 任务 | 改动 | 收益 |
|------|------|------|
| `clineRoutes` → `ClineRouteProvider` | 提取声明式接口 | 验证"声明式路由"形态 |
| `DbMirror` 解析 → `ArtifactExtractor` | 提取提取器 + 注册表 | 去除硬编码，Claude 只加类 |
| 新增 `AgentSinkHost` 门面 + `observe()` | 统一入口 + 观测 | 多 SDK 共存、miss 可观测 |
| `BridgeToCSharpDbBackend` | 经 daemon IPC 桥接 C# 持久化 | 磁盘零残留 + 真落库同时成立 |
| DbBackend 加 `sdk` 维度 | 命名空间隔离 | 跨 SDK sessionId 不冲突 |

### Phase 3（中期，第二 SDK 验证）

| 任务 | 改动 | 收益 |
|------|------|------|
| 新增 `ClaudeRouteProvider` + extractor | 按 §5 六步接入 | 验证通用性，反向回馈接口缺陷 |
| 复用 8 条通用验收用例 | 参数化 | 规范保障 |
| `AgentSinkHost` 支持多 provider 并行 patch | 聚合路由 | 同进程多 Agent |

### Phase 4（远期，独立发布 + 扩展）

- 把 `FsSink` 沉淀为**独立 npm 发布单元**（已满足 `package.json` 独立包条件）。
- 路由规则**可配置化**（配置文件而非代码声明），对齐 `project.md` 配置规范。
- 支持第三 SDK（Codex / Trae）验证通用性。
- 对接项目 `ur_nodes` 统一存储（NodeType 按 SDK 分类，如 `cline_messages`/`claude_messages`）。

---

## 7. 通用原则沉淀（每 SDK 接入必须遵守）

1. **磁盘零残留优先**：拦截路由必须覆盖"显式隔离根 + SDK 默认根"两候选，杜绝 miss 落默认盘（§1.3 断层 2/3）。
2. **落库不依赖磁盘**：结果经 `archivedMessages`（或统一 `AgentRunArtifact`）回传，宿主归档只消费回传数据，磁盘文件仅作兜底（§1.3 断层 1）。
3. **契约化而非写死**：路径布局、消息形状、会话 ID 提取、元数据语义全部走声明式契约，核心不做任何 SDK 假设。
4. **H1 preload**：patch 必须先于 SDK import（ESM 命名绑定快照）。
5. **H2 双套拦截**：`node:fs`（同步+回调）与 `fs/promises`（异步）都要代理。
6. **H3 子进程隔离**：patch 只允许在独立 engine 子进程内启用，不污染宿主。
7. **可回退**：每 SDK 拦截独立开关；未启用时行为与原 SDK 完全一致。
8. **可观测**：命中/miss/归档失败均有观测帧，支撑"又走了默认盘"类问题的快速定位。

---

## 8. 风险与权衡

| 风险 | 影响 | 缓解 |
|------|------|------|
| `@anthropic-ai/claude-agent-sdk` 可能不用 `node:fs`（用自封装 fs） | 拦截失效 | 先实测其 fs 使用方式（H2 前提）；必要时扩展拦截面 |
| Claude 数据布局未知 | 契约设计偏差 | Phase 2 先抽接口，Phase 3 用真实 SDK 回填契约，迭代修正 |
| 多 SDK sessionId 冲突 | 数据串库 | DbBackend 加 `sdk` 维度（§4.3） |
| 过度抽象（YAGNI） | 设计膨胀 | 坚持"只抽已被两次使用证实的概念"；Claude 接入前不预先抽象 |
| 拦截面过广（误伤源码/工具写盘） | 功能异常 | 路由规则最小化 + `observe()` miss/放行可观测 + 白名单（`addAllowAlways`） |

---

## 9. 结论

FsSink 当前的核心（`FsRouter`/`PathRouter`/`MemoryVolume`/`Sink`/`DbBackend`）已完全通用；真正 Cline 专用的是**路由声明、消息解析、结果回传语义**三处。通过引入 `AgentRouteProvider` + `ArtifactExtractor` + `AgentSinkHost` 三个抽象，即可把"加一个 Agent SDK"从**改核心**降级为**加一个适配器目录 + 跑一套通用验收**。Claude 接入既是目标也是通用性的试金石——先做接口抽离（Phase 2），再用真实 Claude SDK 回填契约并反向修正接口（Phase 3），保证不过度设计。
