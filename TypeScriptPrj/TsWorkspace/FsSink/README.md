# FsSink — 进程内文件/数据库拦截框架

## 职责

FsSink 在进程内代理 `node:fs` / `node:fs/promises`，按声明式路由把 Agent 会话产物写入内存卷，
并在模块层接管 `node:sqlite`（`DbSink`），使原生 SQLite 的索引库落点变为 `:memory:`。

```
AgentStorageHost (agent-storage)
  ├─ AgentSinkHost (fs-sink)   ← fs 路由（sessions/messages/manifest/compaction）
  └─ DbSink       (fs-sink)   ← node:sqlite 接管（sessions/tasks/cron/connectors）
```

## 入口与顺序契约

- 唯一安装入口：`ClineApi/src/preload/sink-preload.ts` → `AgentStorageHost.install()`；
- **必须先 install，再动态 `import("@cline/sdk")`**（H1）；preload 静态闭包禁止静态 `node:fs`；
- 守卫：`Frame/PowerShellScript/Operation/DSL/Usharp/Check-FsSinkEntry.ps1`。

## DbSink 驱动注册表

`DbSink` 不感知具体 Agent，由 Provider 的 `DbClaim` 声明：

```ts
{
  name: "cline",
  roots: [path.join(context.dataDir, "db")],
  dbNames: ["sessions.db", "tasks.db", "cron.db", "connectors.db"],
  mode: "memory" // off | observe | relocate | memory
}
```

驱动侧单源 = `NODE_SQLITE_DRIVER`（`agent-storage` 再导出）；新增 Agent 只新增 Provider，
不改 `host.ts` / `contract.ts`。

## 档位（AGENT_STORAGE_PROFILE）

| 档位 | fs | db | 说明 |
|---|---|---|---|
| `off` | off | off | 完全回退真实落盘（兼容旧 `CLINE_DB_SINK=off`） |
| `relocated` | memory | relocate | 产物内存化，索引库重定向到隔离根 |
| `zeroDisk`（默认） | memory | memory | 产物内存化 + 索引库 `:memory:`；磁盘仅剩显式残留 |
| `strictNoSqlite` | memory | memory | 额外注入 `sessionService`；**实验性**，见下 |

## 已声明的可接受残留

- `<dataDir>/logs/hooks.jsonl`：C# `ClineHooksFactIngestor` 的事实摄取源。
  该文件是**日志**（不含会话正文），zeroDisk 下保留；由 Provider 的 `residualPaths()` 显式声明，
  `Check-AgentStorageIsolation` 据此只记录、不判红。

## 观测

`AgentStorageHost.observe()` 返回：

- `fs.perSdk[].misses`：命中权威根却未拦截的路径数（判据 `== 0`）；
- `fs.perSdk[].dbMisses`：索引库根被触碰的 fs 路径数（**仅观测**，不参与 misses 判据）；
- `db.opened[].db`：实际落点（`:memory:` = 已改写）；
- `db.memoryHits` / `db.fallbacks` / `db.denied`；
- `sessionBackendCalls`：注入后端被 SDK 调用的方法总数（strict 实证）；
- `residuals`：Provider 声明的残留清单。

## strictNoSqlite 现状（2026-10-17 实测）

探针报告表明：

- `sessionBackendInjected=true`；
- sentinel 注入后被 SDK 调用并抛错（`sentinelThrown=true`），说明 SDK **确实消费** `sessionService`；
- `sessionServiceCalls=1`（`listSessions`）；
- `DatabaseSync` 构造 = 0；
- 但 `nodeSqliteLoaded=1`（SDK 仍 `require("node:sqlite")` 取类，未构造数据库）。

因此 `strictNoSqlite` 仍是**实验性**：可保证「不构造 DB + 零文件」，尚不能保证「不加载模块」。
正式零落盘目标为 `zeroDisk`。
