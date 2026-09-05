/**
 * AgentSinkHost — 统一门面（替代裸 FsSink）。
 *
 * 聚合多个 `AgentRouteProvider` 的路由并 patch 进程内 node:fs / fs/promises，使同进程可
 * 同时拦截 Cline + Claude 等 SDK（不同路由前缀互不干扰，DbBackend 按 sdk 命名空间隔离）。
 * 提供 getMessages(sdk, sessionId) / flushAll() / observe() 观测帧。
 */

import type { DbBackend } from "../backend/db-backend.js";
import { InMemoryDbBackend } from "../backend/in-memory-db.js";
import { FsRouter } from "../core/fs-router.js";
import { PathRouter, type Route } from "../core/route.js";
import type { Sink } from "../core/sink.js";
import type { ArtifactExtractor } from "../extract/artifact-extractor.js";
import { ArtifactExtractorRegistry } from "../extract/artifact-extractor.js";
import { DbMirror } from "../mirror/db-mirror.js";
import type { AgentRouteProvider } from "../provider/agent-route-provider.js";

/** 观测帧：每路由命中数、每 SDK 命中/miss 数、归档尝试/失败数。 */
export interface SinkObserveFrame {
  perRoute: Record<string, number>;
  perSdk: Record<string, { hits: number; misses: number }>;
  archives: { attempted: number; failed: number };
  totalHits: number;
}

export class AgentSinkHost {
  readonly router = new PathRouter();
  readonly db: DbBackend;

  private readonly fsRouter: FsRouter;
  private readonly providers: AgentRouteProvider[];
  private readonly mirrors: DbMirror[] = [];
  private readonly mirroredSinks = new Set<Sink>();
  private readonly routeNameToSdk = new Map<string, string>();
  private patched = false;

  constructor(providers: AgentRouteProvider[], db?: DbBackend) {
    this.providers = providers;
    this.db = db ?? new InMemoryDbBackend();
    this.fsRouter = new FsRouter({ router: this.router });
  }

  get isPatched(): boolean {
    return this.fsRouter.isPatched;
  }

  /** 命中内存卷的调用计数。 */
  get hits(): number {
    return this.fsRouter.hits;
  }

  /** 聚合所有 provider 路由 + 注册提取器 + patch node:fs / fs/promises（H1 preload 时序）。幂等。 */
  patchAll(): void {
    if (this.patched) return;
    for (const provider of this.providers) {
      // miss 观测：SDK 根候选（显式隔离根 + 默认根）落在路由外的写 → 计该 sdk miss。
      for (const root of provider.roots) {
        this.fsRouter.registerMissWatch(root, provider.name);
      }
      const registry = new ArtifactExtractorRegistry();
      for (const ex of provider.extractors) registry.register(ex);
      for (const route of provider.buildRoutes()) {
        this.router.add(route);
        this.routeNameToSdk.set(route.spec.name, provider.name);
        if (route.mirror && !this.mirroredSinks.has(route.sink)) {
          this.mirroredSinks.add(route.sink);
          this.mirrors.push(
            new DbMirror(route.sink, this.db, {
              sdk: provider.name,
              registry,
              extractSessionId: provider.extractSessionId,
            })
          );
        }
      }
    }
    this.fsRouter.patch();
    this.patched = true;
  }

  /**
   * 低层注册（FsSink 兼容路径）：以裸 Route[] 注册到 sdk 命名空间。
   * 不立即 patch；需配合 ensurePatched()。供 FsSink 兼容薄封装使用。
   */
  addRoutes(
    routes: Route[],
    opts?: {
      sdk?: string;
      extractSessionId?: (p: string) => string | undefined;
      extractors?: ArtifactExtractor[];
      missWatchPrefixes?: string[];
    }
  ): void {
    const sdk = opts?.sdk ?? "cline";
    const registry = new ArtifactExtractorRegistry();
    for (const ex of opts?.extractors ?? []) registry.register(ex);
    for (const prefix of opts?.missWatchPrefixes ?? []) {
      this.fsRouter.registerMissWatch(prefix, sdk);
    }
    for (const route of routes) {
      this.router.add(route);
      this.routeNameToSdk.set(route.spec.name, sdk);
      if (route.mirror && !this.mirroredSinks.has(route.sink)) {
        this.mirroredSinks.add(route.sink);
        this.mirrors.push(
          new DbMirror(route.sink, this.db, {
            sdk,
            registry,
            extractSessionId: opts?.extractSessionId ?? (() => undefined),
          })
        );
      }
    }
  }

  /** 幂等触发 patch（供 addRoutes 后使用）。 */
  ensurePatched(): void {
    if (!this.patched) {
      this.fsRouter.patch();
      this.patched = true;
    }
  }

  /** 恢复原 fs 行为（engine 退出 / dispose 时调用）。 */
  unpatchAll(): void {
    this.fsRouter.unpatch();
    this.patched = false;
  }

  /** 读取某 SDK 会话消息数组（来自内存卷→DB）。未写入返回 undefined。 */
  async getMessages(sdk: string, sessionId: string): Promise<unknown[] | undefined> {
    return this.db.getMessages(sdk, sessionId);
  }

  /** 同步读取某 SDK 会话消息数组。后端不支持同步读时返回 undefined（调用方回退磁盘）。 */
  getMessagesSync(sdk: string, sessionId: string): unknown[] | undefined {
    return this.db.getMessagesSync?.(sdk, sessionId);
  }

  /** 终态落库：取消 debounce，直接遍历内存卷快照镜像到 DB。 */
  async flushAll(): Promise<void> {
    for (const m of this.mirrors) await m.flushAll();
  }

  /** 观测帧：每路由命中数、每 SDK 命中/miss 数、归档尝试/失败数。 */
  observe(): SinkObserveFrame {
    const perRoute: Record<string, number> = {};
    for (const [name, n] of this.fsRouter.routeHits()) perRoute[name] = n;

    const perSdk: Record<string, { hits: number; misses: number }> = {};
    for (const provider of this.providers) {
      let hits = 0;
      for (const [name, n] of this.fsRouter.routeHits()) {
        if (this.routeNameToSdk.get(name) === provider.name) hits += n;
      }
      perSdk[provider.name] = { hits, misses: this.fsRouter.misses(provider.name) };
    }

    let attempted = 0;
    let failed = 0;
    for (const m of this.mirrors) {
      attempted += m.stats.attempted;
      failed += m.stats.failed;
    }
    return { perRoute, perSdk, archives: { attempted, failed }, totalHits: this.fsRouter.hits };
  }
}
