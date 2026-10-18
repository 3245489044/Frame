/**
 * ClineStorageProvider — Cline 方言的通用存储声明（唯一 Agent 专名所在处）。
 *
 * 组合既有 ClineRouteProvider / ClineDbProvider；核心 AgentStorage 不感知 Cline。
 */

import * as path from "node:path";
import {
  ClineRouteProvider,
  clineExtractors,
  extractClineSessionId,
  type ArtifactExtractor,
  type DbSinkMode,
  type Route,
} from "fs-sink";
import {
  STORAGE_WORKER_ROOT_ENV,
  createInMemorySessionService,
  type AgentStorageContext,
  type AgentStorageProvider,
  type DbClaim,
  type StoragePolicy,
} from "agent-storage";
import { resolveClineDataRoot, resolveDefaultClineDataRoot } from "../runtime/cline-data-root.js";

export interface ClineStorageProviderOptions {
  /** 数据根覆盖（缺省 = resolveClineDataRoot(env)）。 */
  dataDir?: string;
  /** 额外 miss 观测根。 */
  missWatchRoots?: string[];
  /** 额外 db 观测根。 */
  dbWatchRoots?: string[];
}

/** Cline 通用 Provider。 */
export class ClineStorageProvider implements AgentStorageProvider {
  readonly name = "cline";
  readonly extractSessionId = extractClineSessionId;
  readonly extractors: ArtifactExtractor[] = clineExtractors;

  private readonly opts: ClineStorageProviderOptions;
  private current?: AgentStorageContext;
  private routeProvider: ClineRouteProvider;

  constructor(opts: ClineStorageProviderOptions = {}) {
    this.opts = opts;
    this.routeProvider = new ClineRouteProvider({
      dataDir: opts.dataDir ?? resolveClineDataRoot().dataDir,
      missWatchRoots: opts.missWatchRoots,
      dbWatchRoots: opts.dbWatchRoots,
    });
  }

  /** 缺省环境：worker 根存在但 CLINE_* 未显式注入时派生（宿主显式值优先）。 */
  env(env: NodeJS.ProcessEnv): Record<string, string | undefined> {
    const workerRoot = env[STORAGE_WORKER_ROOT_ENV]?.trim();
    if (!workerRoot) return {};
    return {
      CLINE_DIR: env.CLINE_DIR ?? workerRoot,
      CLINE_DATA_DIR: env.CLINE_DATA_DIR ?? path.join(workerRoot, "data"),
      CLINE_DB_DATA_DIR: env.CLINE_DB_DATA_DIR ?? path.join(workerRoot, "data", "db"),
    };
  }

  /** 解析上下文并重建路由（Host 在 patch 之前调用）。 */
  context(env: NodeJS.ProcessEnv): AgentStorageContext {
    const root = resolveClineDataRoot(env);
    const defaults = resolveDefaultClineDataRoot(env);
    const dataDir = this.opts.dataDir ?? root.dataDir;
    this.current = {
      workerRoot: env[STORAGE_WORKER_ROOT_ENV]?.trim() || undefined,
      dataDir,
      sessionsDir: root.sessionsDir,
      dbDir: root.dbDir,
      defaultDataDir: defaults.dataDir,
      defaultDbDir: defaults.dbDir,
      env,
    };
    this.routeProvider = new ClineRouteProvider({
      dataDir,
      missWatchRoots: this.opts.missWatchRoots ?? [`${dataDir}/sessions`, defaults.sessionsDir],
      dbWatchRoots: this.opts.dbWatchRoots ?? [`${dataDir}/db`, defaults.dbDir],
    });
    return this.current;
  }

  get roots(): string[] {
    return this.routeProvider.roots;
  }

  get dbRoots(): string[] {
    return this.routeProvider.dbRoots ?? [];
  }

  buildRoutes(): Route[] {
    return this.routeProvider.buildRoutes();
  }

  /** Cline 的 DB 接管面：sessions/tasks/cron/connectors 全部按策略接管。 */
  dbClaims(context: AgentStorageContext, policy: StoragePolicy): DbClaim[] {
    const mode: DbSinkMode = policy.db;
    return [
      {
        name: this.name,
        roots: [path.join(context.dataDir, "db")],
        claimRoots: context.defaultDbDir ? [context.defaultDbDir] : [],
        dbNames: ["sessions.db", "tasks.db", "cron.db", "connectors.db"],
        relocateRoot: path.join(context.dataDir, "db"),
        mode,
      },
    ];
  }

  /** worker 生命周期清理面（与 ClineWorkerScratch 的清理面一致）。 */
  scratchRoots(context: AgentStorageContext): string[] {
    return [context.dbDir, context.sessionsDir];
  }

  /** 严格零 SQLite：注入自建内存会话后端（灰度；默认策略不启用）。 */
  sessionBackend(): unknown {
    return createInMemorySessionService();
  }

  /**
   * 显式可接受残留：hooks.jsonl 是 C# 事实摄取源（ClineHooksFactIngestor），
   * 不能在 zeroDisk 下内存化（否则 LlmIteration/ToolCall/FinalAnswer 断链）。
   * 仅日志，不含会话正文；门禁据此只告警不判红。
   */
  residualPaths(context: AgentStorageContext): string[] {
    return [path.join(context.dataDir, "logs", "hooks.jsonl")];
  }
}
