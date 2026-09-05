# FsSink — 截获硬盘写入 API 的通用内存落盘框架（实施文档）

> **版本**：v0.1（Phase 1 实施稿）　**日期**：2026-08-23
> **状态**：方案已评审（基于 `@cline/core@0.0.70` 源码实测），待实施
> **落位**：实现于 `TypeScriptPrj/TsWorkspace/FsSink/`；本文档放 `docs/analysis/2026/8/8.23/`

---

## 1. 背景与问题

一类编码代理 SDK（Cline、Claude Code SDK、Codex SDK 等）**强制把会话/消息全文落盘为文件**（Cline 即
`<sessionsDir>/<sessionId>/<sessionId>.messages.json`），且**没有官方"不落盘"开关**。经对 `@cline/core@0.0.70`
类型声明与 dist 源码实测，其持久化分两层：

| 层 | 封装点 | 能否关掉 |
|---|---|---|
| 会话元数据行 | `SessionPersistenceAdapter` → `SqliteSessionStore` | 已进 SQLite |
| 消息全文 | `SessionManifestStore.persistSessionMessages` → 写 messages.json | **无官方开关** |
| 落盘后镜像 | `ClineCoreOptions.messagesArtifactUploader` | 只补刀，拦不住先落盘 |
| 持久化层注入 | `ClineCoreOptions.sessionService`（@internal） | 只接管会话行，不接管消息 |

因此"不落盘"只能靠**在 SDK 外部截获其写入动作**，而非依赖某个开关。本框架（**FsSink**）据此设计。

---

## 2. 目标与范围

### 2.1 目标
- **通用**：适配所有"用 Node 标准 `fs`/`fs/promises` 落盘会话数据"的 SDK，不依赖任何 SDK 注入点。
- **不落盘**：会话/消息数据物理不写磁盘；SDK 的写→读→校验闭环在**内存卷**成立。
- **可持久化**：被拦截的数据异步镜像到可插拔数据库后端（对齐项目现有 `PersistSessionAsync`/`ISnapshotStore`）。
- **低侵入**：接入新 SDK 只需加一条路径规则，不改 SDK 内部。

### 2.2 范围
- **Phase 1（本文档主诉）**：FsSink 核心（`FsRouter` + `MemoryVolume` + `InMemoryDb`）+ 在 ClineApi engine 子进程接入，验证磁盘零残留、SDK 读回闭环、结果从 DbBackend 取。
- **Phase 2**：`DbMirror` 对接 C# 现有持久化；事务/flush 一致性。
- **Phase 3**：路径规则可配置化 + 接入第二 SDK（Claude/Codex）验证通用性，沉淀独立发布单元。

### 2.3 非目标
- 不改动/不 `pnpm patch` 任何 `@cline/*` 源码（避免升级脆弱）。
- 不拦截 SDK 正常读写项目源码/工具的路径（默认放行 realFs）。
- 不追求绝对一致性的分布式事务（首版以 flush 语义保证落库时序）。

---

## 3. 方案原理

在进程内代理 `node:fs` 与 `node:fs/promises` 的写入 API，按**路径规则**把写入路由到三处：

| 目标 | 行为 | 作用 |
|---|---|---|
| `realFs`（真实磁盘） | 默认放行 | 工具读写源码、SDK 日志等，零开销透传 |
| `MemoryVolume`（内存文件） | 命中"会话数据规则"→ 拦截 | SDK 的 `mkdir→write→read/exists→stat` 闭环在内存成立，**物理不落盘** |
| `DbMirror`（数据库） | 被拦截写入异步镜像 | 真正的持久化 |

对 SDK 而言它"以为"自己在正常写文件（读回也成功）；对宿主而言磁盘零临时数据，数据只落在内存 + 数据库。

---

## 4. 已验证技术事实与硬约束

### 4.1 `@cline/core` 的 fs 使用实测（dist/index.js）
```js
import { appendFileSync, existsSync, readdirSync, readFileSync } from "node:fs";  // 命名绑定
import * as fsPromises from "node:fs/promises";                                  // 命名空间绑定
```
另含 `writeFileSync`、`writeFile`、`mkdir`、`unlink`、`createWriteStream`、`rename` 等标准 API。

### 4.2 三条硬约束
- **H1（preload 时序）**：ESM **命名绑定**（`import {appendFileSync}`）经 CJS interop 是**模块加载时快照**；
  patch 后不自动更新。⇒ `patchFs()` 必须发生在 `import("@cline/core")` **之前**（ClineApi 的 `loadSdk()` 是
  动态 import，时序可控）。
- **H2（双套拦截）**：`node:fs`（同步+回调）与 `node:fs/promises`（异步）是两个模块对象，写类 + 读类都要代理。
- **H3（进程级副作用 → 子进程隔离）**：patch 全局 fs 影响同进程所有代码。框架要求在**独立子进程**内启用。
  ClineApi 的 **engine 已是独立子进程**（daemon.ts `spawn engine.js`），在此 patch 天然与 C# 宿主、daemon 隔离。

---

## 5. 总体架构

```
调用方：engine 子进程（ClineApi）在 import SDK 前调用
      │  patchFs(routes)                 ← preload，必须先于 SDK 加载
      ▼
┌──────────────────────────────────────────────┐
│ FsRouter（核心：Proxy node:fs + fs/promises） │
│  每个写/读方法：resolve(path) → 分发           │
└───────┬──────────────┬──────────────┬────────┘
        │ 命中规则       │              │
   ┌────▼────┐    ┌──────▼───────┐   ┌──▼─────────┐
   │ realFs  │    │ MemoryVolume │   │ DbMirror   │
   │ (放行)   │    │ (memfs)      │   │(异步镜像)    │
   └─────────┘    │ mkdir/write  │   └─────┬───────┘
                  │ read/exists  │         ▼
                  │ stat/readdir │   ┌─────────────┐
                  └──────────────┘   │ DbBackend   │
                                     │ (存储 SPI)   │
                                     └─────────────┘
```

依赖：`memfs`（内存卷）为唯一运行时依赖；`FsRouter` 保存 `realFs` 原引用做透传；`DbBackend` 为可插拔 SPI。

---

## 6. 目录与文件清单（Phase 1）

目标：新建独立 TS workspace `TypeScriptPrj/TsWorkspace/FsSink/`。

```
FsSink/
├─ package.json                  # name: fs-sink；type: module；仅依赖 memfs
├─ tsconfig.json                 # 对齐 ClineApi（NodeNext / ESM）
├─ vitest.config.ts              # 测试（对齐 CentralTest/TypeScriptTest/ClineSdk.Tests）
└─ src/
   ├─ index.ts                   # 公共导出
   ├─ core/
   │  ├─ route.ts                # Route/RouteSpec/PathRouter：路径规则与匹配（含 Win 归一）
   │  ├─ sink.ts                 # Sink 接口：拦截目标（内存卷 + 可选镜像）
   │  ├─ memory-volume.ts        # MemoryVolume：基于 memfs Volume 封装
   │  ├─ fs-router.ts            # FsRouter：Proxy node:fs + fs/promises，写/读方法映射
   │  └─ patch.ts                # patchFs(routes)/unpatchFs()/isPatched()：preload 入口
   ├─ backend/
   │  ├─ db-backend.ts           # DbBackend SPI
   │  └─ in-memory-db.ts         # InMemoryDbBackend（Phase 1 默认实现）
   ├─ mirror/
   │  └─ db-mirror.ts            # DbMirror：把内存卷中的会话数据异步镜像到 DbBackend
   └─ cline/
      └─ cline-route.ts          # Cline 的路径规则工厂（CLINE_HOME/data/sessions/** 等）
└─ test/
   ├─ fs-router.test.ts          # 拦截/放行/读回闭环
   └─ patch.test.ts              # preload 时序、unpatch 恢复
```

> 接入 ClineApi 的改造点位于 `ClineApi/src/transport/sdk-transport.ts`（loadSdk 前 patch）与
> `ClineApi/src/types.ts` 结果提取路径（见 §11）。

---

## 7. 核心接口定义（TS 源码级）

### 7.1 路径路由 `core/route.ts`
```ts
/** 命中规则：prefix 前缀 / glob glob 匹配 / re 正则；三者取其一即可命中。 */
export interface RouteSpec {
  name: string;
  prefix?: string;            // 如 "<dataDir>/data/sessions/"
  glob?: string;              // 如 "**/*.messages.json"（内部转正则）
  re?: RegExp;
}

export interface Route {
  spec: RouteSpec;
  /** 命中的路径进入哪个 Sink（内存卷）。 */
  sink: Sink;
  /** 命中的写入是否异步镜像到 DbBackend。 */
  mirror?: boolean;
}

/** 归一化 Windows 路径：大小写不敏感、`\`→`/`、去掉尾分隔符。 */
export function normalizeFsPath(p: string): string;

/** 按路径路由；未命中任何 Route 返回 null（→ 放行 realFs）。 */
export class PathRouter {
  add(r: Route): void;
  resolve(normalized: string): Route | null;
  /** 在进程启动时注册默认放行域（如 <cwd>/node_modules、系统临时目录），保证工具读写不受影响。 */
  addAllowAlways(prefix: string): void;
}
```

### 7.2 拦截目标 `core/sink.ts`
```ts
/** 一个拦截目标 = 内存卷 + 可选 DB 镜像。Sink 是"SDK 以为它在写真实文件"的承载体。 */
export interface Sink {
  /** 写类 */
  writeFileSync(p: string, data: NodeJS.ArrayBufferView | string, opts?: unknown): void;
  writeFile(p: string, data: NodeJS.ArrayBufferView | string, opts?: unknown): Promise<void>;
  appendFileSync(p: string, data: string | Uint8Array, opts?: unknown): void;
  appendFile(p: string, data: string | Uint8Array, opts?: unknown): Promise<void>;
  createWriteStream(p: string, opts?: unknown): NodeJS.WritableStream;
  mkdir(p: string, opts?: unknown): void;
  mkdirp(p: string): string;
  rm(p: string, opts?: unknown): void;
  unlink(p: string): void;
  rename(oldP: string, newP: string): void;
  /** 读类（让 SDK 读回闭环） */
  readFileSync(p: string, opts?: unknown): NodeJS.ArrayBufferView | string;
  readFile(p: string, opts?: unknown): Promise<NodeJS.ArrayBufferView | string>;
  existsSync(p: string): boolean;
  stat(p: string): unknown;      // Stats 近似；realpath 等视接入需要再补
  readdir(p: string): string[];
  createReadStream(p: string, opts?: unknown): NodeJS.ReadableStream;
  /** 把该路径在内存中的最新内容异步落 DB（DbMirror 调用）。 */
  flush(p: string): Promise<void>;
}
```

### 7.3 存储 SPI `backend/db-backend.ts`
```ts
/** 对齐项目现有 PersistSessionAsync/ISnapshotStore 语义，便于 Phase 2 桥接 C#。 */
export interface DbBackend {
  upsertSession(row: unknown): Promise<void>;
  writeMessages(sessionId: string, messages: unknown[]): Promise<void>; // 幂等
  getMessages(sessionId: string): Promise<unknown[] | undefined>;
  listSessions(limit?: number): Promise<unknown[]>;
  updateStatus(sessionId: string, status: string, exitCode?: number | null): Promise<void>;
}
```

### 7.4 preload 入口 `core/patch.ts`
```ts
export interface PatchOptions {
  /** 命中规则进入内存卷的路径；未命中默认放行 realFs。 */
  routes: Route[];
  /** 内存卷数据目录根（用于 memfs Volume 初始化），默认空。 */
  dataDir?: string;
  /** 是否在每条写入后同步 flush 到 DbBackend（默认 false，Phase 1 用 flush 事件驱动）。 */
  autoMirror?: boolean;
}
/** 必须先于 import SDK 调用；返回原 fs，供后续 unpatch 精确还原。 */
export function patchFs(opts: PatchOptions): { realFs: typeof fs };
export function unpatchFs(): void;
export function isPatched(): boolean;
```

---

## 8. FsRouter 代理设计（写/读方法映射）

`FsRouter` 用 `Proxy` 包装，对每个方法按"首个参数是否路径"判断并路由；保存 `realFs` 原引用透传未命中路径。
实现要点：**同一套路由逻辑同时应用到 `node:fs` 与 `node:fs/promises` 两个模块对象**（H2）。

| 组 | 方法（node:fs） | 方法（fs/promises） | 分发逻辑 |
|---|---|---|---|
| 写 | `writeFileSync/appendFileSync/mkdirSync/rmSync/unlinkSync/renameSync` | `writeFile/appendFile/mkdir/rm/unlink/rename` | 命中→Sink，未命中→realFs |
| 流写 | `createWriteStream` | — | 命中→Sink.createWriteStream，未命中→realFs |
| 读 | `readFileSync/existsSync/statSync/lstatSync/readdirSync/realpathSync/accessSync` | `readFile/exists/stat/lstat/readdir/realpath/access` | 命中→Sink（读回闭环），未命中→realFs |
| 流读 | `createReadStream` | — | 命中→Sink.createReadStream，未命中→realFs |
| 目录 | `mkdirSync`（含 recursive） | `mkdir` | 命中→Sink.mkdirp，未命中→realFs |
| 其它 | 全部保持 realFs | 全部保持 realFs | 不拦截（如 watch/rename 域外） |

> **实现细节（防死循环）**：Sink 内部对内存卷的操作必须调用**已保存的 `realFs`** 或 memfs 自身 API，
> **绝不能**再经过已被代理的 `fs`，否则递归路由（patching）。`MemoryVolume` 与 `FsRouter` 通过构造注入 `realFs` 引用。

### 8.1 代理骨架（示意）
```ts
function createFsProxy(target: typeof fs, router: PathRouter, sink: Sink): typeof fs {
  const real = target; // 保存原引用
  const handler: ProxyHandler<typeof fs> = {
    get(t, prop, recv) {
      if (WRITE_METHODS.has(prop)) return makeWriteProxy(real[prop], router, sink, false);
      if (READ_METHODS.has(prop)) return makeReadProxy(real[prop], router, sink, false);
      return Reflect.get(t, prop, recv);
    },
  };
  return new Proxy(target, handler);
}
// fs/promises 复用同一 handler，只是目标不同、方法返回 Promise。
```
> 命名绑定（H1）在此意味着：`patchFs` 返回的代理必须通过"替换 `module.exports` 属性"生效，
> 且**必须在 SDK `import` 前执行**。对命名空间绑定（`import * as`）则 patch 后即时生效。

---

## 9. 路径路由规则（Cline 实例）

`cline/cline-route.ts` 提供 Cline 专属路由工厂：
```ts
export function clineRoutes(opts: { home: string }): Route[] {
  const sessionsPrefix = `${normalizeFsPath(opts.home)}/data/sessions/`;
  const sink = new MemoryVolume(); // 一个内存卷承载所有会话产物
  return [
    { spec: { name: "cline-sessions", prefix: sessionsPrefix }, sink, mirror: true },
    { spec: { name: "cline-messages-glob", glob: "**/*.messages.json" }, sink, mirror: true },
    // manifest / compaction / 子 agent 产物一并进内存卷
    { spec: { name: "cline-manifest", glob: "**/*.manifest.json" }, sink, mirror: false },
    { spec: { name: "cline-compaction", glob: "**/*.compaction.json" }, sink, mirror: false },
  ];
}
```
- `home` 取自 `CLINE_HOME`（engine 子进程由 C# 注入，见 `ClineDaemonHost` 的 `CLINE_HOME` 环境变量）或 `os.homedir()/.cline`。
- 未命中的一切路径（工具读写源码、node_modules、系统目录）默认放行 realFs（H3 的路径白名单天然生效）。

---

## 10. MemoryVolume 与 DbMirror

### 10.1 MemoryVolume（`core/memory-volume.ts`）
- 基于 `memfs` 的 `Volume` 封装，实现 `Sink` 接口；`mkdirp` 用 `vol.mkdirp`，写/读/stat/readdir 全走内存。
- 维护一个 `Map<path, {data, mtime}>` 快照，供 `flush(path)` 读取最新内容。
- 暴露 `snapshotAll(): Array<{path, data}>`，供 DbMirror 批处理。

### 10.2 DbMirror（`mirror/db-mirror.ts`）
- 订阅 `Sink` 的写事件（MemoryVolume 在写后回调）。
- 针对 `*.messages.json`：`JSON.parse` → 提取 `sessionId`（由路径 `<...>/sessions/<id>/<id>.messages.json` 解析）→ `backend.writeMessages(sessionId, messages)`。
- 针对会话元数据：`upsertSession`。
- **flush 时机**：内存卷写后 debounce（如 200ms）+ 显式 `flushAll()`（在 SDK 的 `session_snapshot` 事件触发时调用，保证终态落库）。Phase 1 用 `InMemoryDbBackend`，即 `Map` 承载，纯验证闭环。

### 10.3 InMemoryDbBackend（`backend/in-memory-db.ts`）
- 实现 `DbBackend`：`Map<sessionId, {row?, messages?}>`。为 Phase 1 验收与测试提供零依赖后端；Phase 2 替换为桥接 C# 的实现。

---

## 11. ClineApi 接入改造点

### 11.1 `transport/sdk-transport.ts` — 在 loadSdk 前 patch
- `loadSdk()` 改为先 `patchFs`（H1：必须先于 `import("@cline/core")`），后动态 import。
- `ensureCore()` 里 `ClineCore.create` 传参不变（`backendMode:"local"`），无需 `sessionService`/`uploader`——消息落盘已被 fs 层截获。
- patch 时机若已 `isPatched()` 则幂等跳过（单进程单 patch）。
- engine 退出（`dispose()`）时 `unpatchFs()`。

```ts
// 伪代码（sdk-transport.ts 内部）
const fsSink = new FsSink();                       // 内部 = PathRouter + MemoryVolume + DbMirror(InMemory)
fsSink.patchFs(clineRoutes({ home: resolveHome() }));  // preload
const sdk = await import("@cline/sdk");            // 命名绑定在此快照到已 patch 的 fs
```

### 11.2 结果提取 — 去掉对磁盘 messages.json 的依赖
`EventCollector.getAuthoritativeResultText` 现有三级：snapshot → **persisted-file（读磁盘 messages.json）** → 事件流。
接入后：
- 保留 snapshot 级（`session_snapshot.messages` 仍在，SDK 内存里有）。
- `persisted-file` 级改读 **`FsSink.db.getMessages(sessionId)`**（内存卷 → DB），不再 `fs.readFileSync(messagesPath)`。
- 事件流级兜底保留。
- `resolveMessagesPath` 仍返回路径（供 `artifacts.messagesPath` 语义一致），但读取走 DB。

> **兼容**：patch 未启用时（非 engine 路径，如单测），`getMessages` 回退磁盘读取，保持旧行为。

### 11.3 `ClineDaemonHost`（C# 侧）— 无改动
`CLINE_HOME` 已按 worker 注入（Isolated 策略）。FsSink 拦截的是 **engine 子进程内**对该目录的写入，
C# 侧无需感知；若需要，可通过新增 daemon 帧（如 `type:"fs-sink"` 上报命中统计/落库数量）做可观测，属 Phase 2。

---

## 12. 测试方案（Phase 1）

| 用例 | 断言 |
|---|---|
| 写命中规则 | `writeFileSync("<home>/data/sessions/x/x.messages.json", ...)` 后，`existsSync` 于真实磁盘为 false，内存卷存在 |
| 读回闭环 | 写入后 `readFileSync` 同路径能读回，内容一致（模拟 SDK `persistSessionMessages`→读 manifest/messages） |
| 放行未命中 | `writeFileSync("<cwd>/src/a.cs", ...)` 正常落盘真实磁盘 |
| 流式写 | `createWriteStream` 到命中路径，close 后内存卷可见、磁盘不可见 |
| 目录递归 | `mkdir`（recursive）到命中前缀，内存卷成立 |
| 镜像落库 | 写 `messages.json` → `DbBackend.getMessages` 返回解析后的消息数组 |
| preload 时序 | `import("@cline/core")` 前的 `patchFs` 生效；`unpatchFs` 后恢复原 fs 行为 |
| 防递归 | Sink 内操作不触发路由（用计数器断言无递归调用） |

测试框架对齐 `CentralTest/TypeScriptTest/ClineSdk.Tests`（vitest + 子进程跑 `@cline/core`）。

---

## 13. 副作用与取舍

| 项 | 说明 | 对策 |
|---|---|---|
| 全局 patch 副作用 | 影响同进程所有 fs | 仅 engine 子进程启用（H3）+ 路径默认放行 + `unpatchFs` |
| 命名绑定 preload 约束 | 必须在 import SDK 前 patch | API 强制：`patchFs` 后才能 load；engine 单点入口 |
| 性能 | 命中路径一次内存写 + 异步 DB 写 | 未命中零开销透传 realFs；镜像 debounce |
| Windows 路径 | 大小写/`\`/`/` 差异 | `normalizeFsPath` 统一后再路由 |
| 崩溃一致性 | DbMirror 异步镜像 | `flushAll` 于 `session_snapshot`/`ended` 事件触发，保证终态落库 |
| 大文件/二进制 | 仅命中会话数据规则才拦 | 规则精确（`sessions/**` 等），不全局 `**` |

---

## 14. 验收标准（Phase 1 完成判据）

1. **磁盘零残留**：一次完整 Cline 会话后，`CLINE_HOME` 指向目录内无 `messages.json/manifest.json` 落盘（可在测试中断言目录为空或仅含预期文件）。
2. **SDK 读回闭环**：`core.readMessages` / `session_snapshot.artifacts.messagesPath` 语义正常，`getAuthoritativeResultText` 能取到结果（来自 DB，非磁盘）。
3. **结果提取切换**：`persisted-file` 级改读 `DbBackend.getMessages` 后，既有 ClineApi 用例（`CentralTest/AgentTest/Agent.ClineUsage.Tests`）通过。
4. **放行不受影响**：工具读写项目源码的路径照常落盘（回归）。
5. **隔离**：daemon 控制面 / C# 宿主进程不受 patch 影响。
6. **测试绿**：§12 全部用例通过。

---

## 15. 演进路径

- **Phase 1（本文档主诉）**：FsSink 核心 + ClineApi engine 接入 + 验收。
- **Phase 2**：`BridgeToCSharpDbBackend` 经 `ClineDaemonIpc` 通道对接 C# `PersistSessionAsync`/`ISnapshotStore`，DB 归属统一到 C#；补 `fs-sink` 可观测帧；处理子 agent/team 嵌套 session。
- **Phase 3**：路径规则配置化（`routes.json`）+ 抽象 `resolveStrategy` 接入第二 SDK（Claude/Codex）验证通用性；沉淀独立发布单元（`npm` 包）。

---

## 附录 A：`@cline/core@0.0.70` fs 使用证据（dist/index.js 实测）

```js
import { appendFileSync, existsSync, readdirSync, readFileSync } from "node:fs";  // 命名绑定（H1）
import * as fsPromises from "node:fs/promises";                                  // 命名空间绑定
```
含 `writeFileSync` / `writeFile` / `mkdir` / `unlink` / `createWriteStream` / `rename` 等标准 API；
持久化关键路径：`SessionManifestStore.persistSessionMessages` → `SessionArtifacts.sessionMessagesPath` 写
`<sessionsDir>/<sessionId>/<sessionId>.messages.json`。

## 附录 B：术语
| 术语 | 含义 |
|---|---|
| realFs | patch 前保存的原 `node:fs` / `node:fs/promises` 引用 |
| MemoryVolume | memfs `Volume` 封装，承载被拦截文件的内存镜像 |
| DbMirror | 把内存卷中的会话数据异步写入 DbBackend 的镜像器 |
| Route/RouteSpec | 路径拦截规则（prefix/glob/re） |



