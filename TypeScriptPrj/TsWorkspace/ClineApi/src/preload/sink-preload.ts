/**
 * sink-preload — FsSink 唯一安装入口（进程级 preload）。
 *
 * 职责收拢（消除旧的「3 类入口 × N 份根解析」）：
 *   ① 数据根唯一权威：`resolveClineDataRoot()`（与 SDK `@cline/shared/storage` 同算法）；
 *   ② patch 时机唯一：本模块 `patchAll()` 后才允许 `import("@cline/sdk")`（H1 时序）；
 *   ③ 观测唯一：`observeSink()`（perRoute / perSdk hits·misses）+ 绑定自检 fail-fast（I2）。
 *
 * **不变式 I1**：本模块的静态依赖闭包禁止 `import ... from "node:fs"` / `"node:fs/promises"`
 * （只允许 `createRequire` 或动态 import）。任何静态 fs 消费者都会在 patch 之前建立 ESM facade，
 * 使 patch 对 SDK 的命名导入不可见（静默落盘的根因）。守卫脚本：Check-FsSinkEntry.ps1。
 *
 * 使用（入口 bootstrap，动态 import 本模块 → 再动态 import 主体）：
 *   const { installSink } = await import("./preload/sink-preload.js");
 *   const sink = await installSink({ persistBridge: true });
 */

import {
  AgentSinkHost,
  BridgeToCSharpDbBackend,
  DaemonIpcPersistenceTransport,
  DbSink,
  type DbSinkObserveFrame,
  type SinkObserveFrame,
} from "fs-sink";
import {
  AgentStorageHost,
  STORAGE_PROFILE_ENV,
  type AgentStorageObserveFrame,
  type StorageProfile,
} from "agent-storage";
import { ClineStorageProvider } from "../storage/cline-storage-provider.js";

export interface SinkInstallOptions {
  /** 写侧实时推 persist 帧到 C#（engine 路径由 CLINE_FS_SINK_PERSIST_BRIDGE 开关决定；cli 路径恒 false）。 */
  persistBridge?: boolean;
  /** 数据根覆盖（缺省 = `resolveClineDataRoot().dataDir`，即 SDK 实际写盘根）。 */
  dataDir?: string;
  /** 显式档位（缺省 = AGENT_STORAGE_PROFILE / CLINE_DB_SINK / zeroDisk）。 */
  profile?: StorageProfile;
}

export interface InstalledSink {
  /** 通用存储宿主（策略/观测/清理面权威）。 */
  host: AgentStorageHost;
  /** 已 patch 的统一门面（注入 SdkTransport 用）。 */
  sink: AgentSinkHost;
  /** 模块层 DB 拦截器（兼容旧导出；等价 host.dbSink）。 */
  dbSink: DbSink;
  /** 生效的数据根（供诊断回带）。 */
  dataDir: string;
  /** 绑定自检结果（空 = 通过；非空已 fail-fast 抛错，不会返回）。 */
  bindings: string[];
  /** 生效档位。 */
  profile: StorageProfile;
  /** 严格零 SQLite 后端（仅 strictNoSqlite 时有值）。 */
  sessionBackend?: unknown;
}

let installed: InstalledSink | undefined;

/**
 * 安装 AgentStorage（幂等）：解析上下文 → 构造宿主 → patch fs/db → 绑定自检。
 * 绑定自检非空即抛错终止（把「facade 早于 patch 建立」的静默失效升级为启动期显性失败）。
 */
export async function installSink(opts: SinkInstallOptions = {}): Promise<InstalledSink> {
  if (installed) return installed;

  // persistBridge：把 DbBackend 换成桥接 C# 持久化的实现（写侧单向推送，失败不阻断内存闭环）。
  const db = opts.persistBridge
    ? new BridgeToCSharpDbBackend(new DaemonIpcPersistenceTransport())
    : undefined;

  const provider = new ClineStorageProvider({ dataDir: opts.dataDir });
  const host = await AgentStorageHost.install({
    provider,
    profile: opts.profile,
    persistBridge: opts.persistBridge,
    dbBackend: db,
  });

  installed = {
    host,
    sink: host.sink,
    dbSink: host.dbSink,
    dataDir: host.context.dataDir,
    bindings: host.bindings,
    profile: host.policy.profile,
    sessionBackend: host.sessionBackend,
  };
  return installed;
}

/** 已安装的门面（未安装返回 undefined；供 SdkTransport 注入）。 */
export function getInstalledSink(): AgentSinkHost | undefined {
  return installed?.sink;
}

/** 已安装的通用宿主（供 SdkTransport 注入 sessionBackend/观测）。 */
export function getInstalledHost(): AgentStorageHost | undefined {
  return installed?.host;
}

/** 观测帧（perRoute / perSdk hits·misses / archives / bindings）；未安装返回 undefined。 */
export function observeSink(): SinkObserveFrame | undefined {
  return installed?.sink.observe();
}

/** DbSink 观测帧（mode/opened/relocated/memoryHits/denied/fallbacks）；未安装返回 undefined。 */
export function dbObserve(): DbSinkObserveFrame | undefined {
  return installed?.dbSink.observe();
}

/** 通用存储观测帧（policy/context/fs/db/sessionBackend）；未安装返回 undefined。 */
export function observeStorage(): AgentStorageObserveFrame | undefined {
  return installed?.host.observe();
}

/** 当前档位（诊断/阶段帧回带）。 */
export function currentStorageProfile(): StorageProfile | undefined {
  return installed?.profile;
}

/** 环境变量名（供 C# / 文档引用，避免各处手写）。 */
export const AGENT_STORAGE_PROFILE_ENV = STORAGE_PROFILE_ENV;

/** 终态落库 + 卸载 patch（进程退出/dispose 时调用；可重复调用）。 */
export async function disposeSink(): Promise<void> {
  const current = installed;
  if (!current) return;
  installed = undefined;
  try {
    await current.host.flushAll();
  } catch {
    /* 终态落库失败不阻断卸载 */
  }
  current.host.dispose();
}

