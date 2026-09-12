/**
 * FsSink — 通用内存落盘框架门面（兼容薄封装，内部委托 AgentSinkHost）。
 *
 * @deprecated 历史/测试兼容路径。生产入口唯一 = 入口 preload（`ClineApi/src/preload/sink-preload.ts`
 * 的 `installSink`：构造 AgentSinkHost → patchAll → 绑定自检 fail-fast）；新接入请直接用
 * `AgentSinkHost` + `AgentRouteProvider`，不要经本类自建 patch 时机（时序错误会静默失效）。
 *
 * 组合 PathRouter + FsRouter + MemoryVolume(s) + DbMirror + DbBackend，向接入方提供：
 *  - `patchFs(routes)`：注册规则并 patch 进程内 node:fs / node:fs/promises（须先于 SDK import，H1）；
 *  - `unpatchFs()`：恢复原 fs 行为；
 *  - `getMessages(sessionId)`：结果提取改从 DB 取（不再读磁盘 messages.json）；
 *  - `flushAll()`：终态落库（session_snapshot/ended 事件触发）。
 */

import type { DbBackend } from "./backend/db-backend.js";
import { InMemoryDbBackend } from "./backend/in-memory-db.js";
import { PathRouter, type Route } from "./core/route.js";
import { AgentSinkHost, type SinkObserveFrame } from "./host/agent-sink-host.js";
import { clineExtractors } from "./cline/cline-extractor.js";
import { extractClineSessionId } from "./cline/cline-route-provider.js";

export class FsSink {
  readonly router: PathRouter;
  readonly db: DbBackend;

  private readonly host: AgentSinkHost;

  constructor(db?: DbBackend) {
    this.host = new AgentSinkHost([], db ?? new InMemoryDbBackend());
    this.router = this.host.router;
    this.db = this.host.db;
  }

  get isPatched(): boolean {
    return this.host.isPatched;
  }

  /** 命中内存卷的调用计数（防递归/观测）。 */
  get hits(): number {
    return this.host.hits;
  }

  /**
   * 注册路由并 patch node:fs / node:fs/promises。
   * 必须保证在 `import("@cline/...")` 之前调用（H1 preload 时序）。
   * 幂等：已 patch 则仅追加规则，不重复 patch。
   */
  patchFs(routes: Route[]): void {
    this.host.addRoutes(routes, {
      sdk: "cline",
      extractSessionId: extractClineSessionId,
      extractors: clineExtractors,
      missWatchPrefixes: routes
        .map((r) => r.spec.prefix)
        .filter((p): p is string => p !== undefined),
    });
    this.host.ensurePatched();
  }

  /** 恢复原 fs 行为（engine 退出 / dispose 时调用）。 */
  unpatchFs(): void {
    this.host.unpatchAll();
  }

  /** 读取某会话消息数组（来自内存卷→DB）。未写入返回 undefined。 */
  async getMessages(sessionId: string): Promise<unknown[] | undefined> {
    return this.host.getMessages("cline", sessionId);
  }

  /**
   * 同步读取某会话消息数组。仅当后端实现可选的 getMessagesSync（Phase 1 的 InMemoryDbBackend）时有效；
   * 后端不支持时返回 undefined（调用方回退磁盘读取，保持兼容）。
   */
  getMessagesSync(sessionId: string): unknown[] | undefined {
    return this.host.getMessagesSync("cline", sessionId);
  }

  /** 终态落库：取消 debounce，直接遍历内存卷快照镜像到 DB。 */
  async flushAll(): Promise<void> {
    await this.host.flushAll();
  }

  /** 观测帧（委托 AgentSinkHost；sdk 恒为 cline）。 */
  observe(): SinkObserveFrame {
    return this.host.observe();
  }
}

export { FsRouter } from "./core/fs-router.js";
export { PathRouter, normalizeFsPath, globToRegExp } from "./core/route.js";
export type { Route, RouteSpec } from "./core/route.js";
export { MemoryVolume } from "./core/memory-volume.js";
export type { Sink, SnapshotEntry, SinkData, WriteListener } from "./core/sink.js";
export { DbMirror } from "./mirror/db-mirror.js";
export type { DbMirrorOptions } from "./mirror/db-mirror.js";
export type { DbBackend, ParsedMessage } from "./backend/db-backend.js";
export { InMemoryDbBackend } from "./backend/in-memory-db.js";
export { BridgeToCSharpDbBackend } from "./backend/bridge-to-csharp.js";
export type { CSharpPersistenceTransport } from "./backend/bridge-to-csharp.js";
export { DaemonIpcPersistenceTransport } from "./backend/daemon-ipc-transport.js";
export type { PersistFrameEmitter } from "./backend/daemon-ipc-transport.js";
export { AgentSinkHost } from "./host/agent-sink-host.js";
export type { SinkObserveFrame } from "./host/agent-sink-host.js";
export type { AgentRouteProvider } from "./provider/agent-route-provider.js";
export { ArtifactExtractorRegistry } from "./extract/artifact-extractor.js";
export type {
  ArtifactExtractor,
  ArtifactKind,
} from "./extract/artifact-extractor.js";
export { ClineRouteProvider, extractClineSessionId } from "./cline/cline-route-provider.js";
export type { ClineRouteProviderOptions } from "./cline/cline-route-provider.js";
export { clineRoutes } from "./cline/cline-route.js";
export type { ClineRoutesOptions } from "./cline/cline-route.js";

