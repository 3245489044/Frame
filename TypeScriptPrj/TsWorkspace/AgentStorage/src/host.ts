/**
 * AgentStorageHost — 通用存储宿主（唯一安装/观测/卸载入口）。
 *
 * 组合：
 *   - fs-sink.AgentSinkHost（进程内 node:fs 路由到内存卷）；
 *   - fs-sink.DbSink（模块层 DB 接管，Provider 的 DbClaim 转声明式 DbRouteProvider）。
 *
 * 时序契约（与既有 FsSink 一致）：Host.install() 必须早于 import("@cline/sdk")。
 * 洁净面（I1）：本文件及其静态闭包禁止静态 import node:fs。
 */

import {
  AgentSinkHost,
  DbSink,
  type DbBackend,
  type DbRouteProvider,
  type DbSinkDeps,
  type DbSinkObserveFrame,
  type DbSinkMode,
  type SinkObserveFrame,
} from "fs-sink";
import type {
  AgentStorageContext,
  AgentStorageProvider,
  DbClaim,
  StoragePolicy,
} from "./contract.js";
import { resolveStoragePolicy } from "./policy.js";

/** 安装选项。 */
export interface AgentStorageInstallOptions {
  provider: AgentStorageProvider;
  /** 显式档位（优先于 env）。 */
  profile?: StoragePolicy["profile"];
  /** 持久化桥开关（写侧推 C#；失败不阻断内存闭环）。 */
  persistBridge?: boolean;
  /** 数据后端（缺省 InMemoryDbBackend）。 */
  dbBackend?: DbBackend;
  /** 环境注入 seam（测试用；缺省 process.env）。 */
  env?: NodeJS.ProcessEnv;
  /** DbSink 测试 seam。 */
  dbDeps?: DbSinkDeps;
  /** 是否做 ESM 绑定自检（测试可关；生产必须为 true）。 */
  verifyBindings?: boolean;
}

/** 统一观测帧。 */
export interface AgentStorageObserveFrame {
  provider: string;
  policy: StoragePolicy;
  context: Pick<AgentStorageContext, "workerRoot" | "dataDir" | "sessionsDir" | "dbDir">;
  fs: SinkObserveFrame;
  db: DbSinkObserveFrame;
  sessionBackend: boolean;
  scratchRoots: string[];
}

/** DbClaim → fs-sink 声明式 DbRouteProvider（核心只做结构转换）。 */
function toRouteProvider(claim: DbClaim, policy: StoragePolicy): DbRouteProvider {
  const mode: DbSinkMode = claim.mode ?? policy.db;
  return {
    name: claim.name,
    roots: claim.roots,
    claimRoots: claim.claimRoots,
    dbNames: claim.dbNames,
    relocateRoot: claim.relocateRoot ?? claim.roots[0],
    mode: () => mode,
  };
}

/** 通用存储宿主。 */
export class AgentStorageHost {
  readonly provider: AgentStorageProvider;
  readonly policy: StoragePolicy;
  readonly context: AgentStorageContext;
  readonly sink: AgentSinkHost;
  readonly dbSink: DbSink;
  readonly scratchRoots: string[];
  readonly sessionBackend?: unknown;
  private lastBindings: string[] = [];

  private constructor(opts: {
    provider: AgentStorageProvider;
    policy: StoragePolicy;
    context: AgentStorageContext;
    sink: AgentSinkHost;
    dbSink: DbSink;
    scratchRoots: string[];
    sessionBackend?: unknown;
  }) {
    this.provider = opts.provider;
    this.policy = opts.policy;
    this.context = opts.context;
    this.sink = opts.sink;
    this.dbSink = opts.dbSink;
    this.scratchRoots = opts.scratchRoots;
    this.sessionBackend = opts.sessionBackend;
  }

  /**
   * 安装（幂等由调用方保证；本方法每次构造新宿主）。
   * 绑定自检失败 → 抛错终止（把静默失效升级为启动期显性失败）。
   */
  static async install(opts: AgentStorageInstallOptions): Promise<AgentStorageHost> {
    const env = opts.env ?? process.env;
    // Provider 缺省 env 只填空：宿主显式注入（C#）优先。
    const defaults = opts.provider.env?.(env) ?? {};
    for (const [key, value] of Object.entries(defaults)) {
      if (value !== undefined && env[key] === undefined) env[key] = value;
    }

    const policy = resolveStoragePolicy(env, opts.profile);
    const context = opts.provider.context(env);

    const sink = new AgentSinkHost(
      policy.fs === "off" ? [] : [opts.provider],
      opts.dbBackend
    );
    const dbProviders = opts.provider
      .dbClaims(context, policy)
      .map((claim) => toRouteProvider(claim, policy));
    const dbSink = new DbSink(dbProviders, opts.dbDeps);

    if (policy.fs !== "off") sink.patchAll();
    dbSink.patchAll();

    const bindings =
      policy.fs === "off" || opts.verifyBindings === false ? [] : await sink.verifyBindings();
    if (bindings.length > 0) {
      dbSink.unpatchAll();
      sink.unpatchAll();
      throw new Error(
        `AgentStorage 绑定自检失败：patch 对 ESM 命名导入不可见（未对齐: ${bindings.join(", ")}）。` +
          `修复：入口必须 bootstrap（先 install 再动态 import SDK），且 preload 静态闭包不得含 node:fs 消费者。`
      );
    }

    const scratchRoots = opts.provider.scratchRoots?.(context) ?? [];
    const sessionBackend = policy.strictNoSqlite
      ? opts.provider.sessionBackend?.(context)
      : undefined;

    const host = new AgentStorageHost({
      provider: opts.provider,
      policy,
      context,
      sink,
      dbSink,
      scratchRoots,
      sessionBackend,
    });
    host.lastBindings = bindings;
    return host;
  }

  /** ESM 绑定自检结果（空 = 通过）。 */
  get bindings(): string[] {
    return [...this.lastBindings];
  }

  /** 统一观测帧（fs + db + policy/context）。 */
  observe(): AgentStorageObserveFrame {
    return {
      provider: this.provider.name,
      policy: this.policy,
      context: {
        workerRoot: this.context.workerRoot,
        dataDir: this.context.dataDir,
        sessionsDir: this.context.sessionsDir,
        dbDir: this.context.dbDir,
      },
      fs: this.sink.observe(),
      db: this.dbSink.observe(),
      sessionBackend: this.sessionBackend !== undefined,
      scratchRoots: [...this.scratchRoots],
    };
  }

  /** 终态落库（镜像提交）。 */
  async flushAll(): Promise<void> {
    await this.sink.flushAll();
  }

  /** 卸载补丁（与 install 对称）。 */
  dispose(): void {
    this.dbSink.unpatchAll();
    this.sink.unpatchAll();
  }
}
