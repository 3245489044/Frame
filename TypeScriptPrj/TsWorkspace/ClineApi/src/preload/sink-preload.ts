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
  ClineRouteProvider,
  ClineDbProvider,
  DbSink,
  type DbSinkObserveFrame,
  type SinkObserveFrame,
} from "fs-sink";
import { resolveClineDataRoot, resolveDefaultClineDataRoot } from "../runtime/cline-data-root.js";

export interface SinkInstallOptions {
  /** 写侧实时推 persist 帧到 C#（engine 路径由 CLINE_FS_SINK_PERSIST_BRIDGE 开关决定；cli 路径恒 false）。 */
  persistBridge?: boolean;
  /** 数据根覆盖（缺省 = `resolveClineDataRoot().dataDir`，即 SDK 实际写盘根）。 */
  dataDir?: string;
}

export interface InstalledSink {
  /** 已 patch 的统一门面（注入 SdkTransport 用）。 */
  sink: AgentSinkHost;
  /** 模块层 sqlite shim（缺省 off；`CLINE_DB_SINK=observe|relocate|memory` 时生效）。 */
  dbSink: DbSink;
  /** 生效的数据根（供诊断回带）。 */
  dataDir: string;
  /** 绑定自检结果（空 = 通过；非空已 fail-fast 抛错，不会返回）。 */
  bindings: string[];
}

let installed: InstalledSink | undefined;

/**
 * 安装 FsSink（幂等）：解析数据根 → 构造宿主 → patch → 绑定自检。
 * 绑定自检非空即抛错终止（把「facade 早于 patch 建立」的静默失效升级为启动期显性失败）。
 */
export async function installSink(opts: SinkInstallOptions = {}): Promise<InstalledSink> {
  if (installed) return installed;

  const root = resolveClineDataRoot();
  const dataDir = opts.dataDir ?? root.dataDir;
  // 泄漏观测面（仅观测，不路由）：忽略 CLINE_* 后的同算法 sessions/db 目录 = SDK 默认根；
  // 若 SDK 无视注入变量写到那里，路径命中该前缀却未命中路由 → 计 miss，定位「又走了默认盘」。
  const defaults = resolveDefaultClineDataRoot();
  const defaultSessionDir = defaults.sessionsDir;
  const defaultDbDir = defaults.dbDir;

  // persistBridge：把 DbBackend 换成桥接 C# 持久化的实现（写侧单向推送，失败不阻断内存闭环）。
  const db = opts.persistBridge
    ? new BridgeToCSharpDbBackend(new DaemonIpcPersistenceTransport())
    : undefined;

  const sink = new AgentSinkHost(
    [
      new ClineRouteProvider({
        dataDir,
        missWatchRoots: [`${dataDir}/sessions`, defaultSessionDir],
        // db 根观测面（仅观测）：索引库原生 SQLite 不经 fs，拦截不可达 → 单列 dbMisses 计数。
        dbWatchRoots: [`${dataDir}/db`, defaultDbDir],
      }),
    ],
    db
  );

  // DbSink（模块层 sqlite 拦截）：`CLINE_DB_SINK` 缺省 off ⇒ 不 patch（零行为变更）。
  // 构造须早于 fs patch（捕获原始 fs 引用用于 relocate 建目录）；安装与 fs patch 同一窗口。
  const dbSink = new DbSink([
    new ClineDbProvider({ dataDir, defaultDbDir, dbNames: ["sessions.db"] }),
  ]);

  sink.patchAll();
  dbSink.patchAll();

  const bindings = await sink.verifyBindings();
  if (bindings.length > 0) {
    throw new Error(
      `FsSink 绑定自检失败：patch 对 ESM 命名导入不可见（未对齐方法: ${bindings.join(", ")}）。` +
        `原因：本进程在 patch 之前已有模块静态 import 了 node:fs，导致 facade 快照冻结为原函数。` +
        `修复：入口必须 bootstrap（先 installSink 再动态 import 主体），且 preload 静态闭包不得含 node:fs 消费者。`
    );
  }

  installed = { sink, dbSink, dataDir, bindings };
  return installed;
}

/** 已安装的门面（未安装返回 undefined；供 SdkTransport 注入）。 */
export function getInstalledSink(): AgentSinkHost | undefined {
  return installed?.sink;
}

/** 观测帧（perRoute / perSdk hits·misses / archives / bindings）；未安装返回 undefined。 */
export function observeSink(): SinkObserveFrame | undefined {
  return installed?.sink.observe();
}

/** DbSink 观测帧（mode/opened/relocated/memoryHits/denied/fallbacks）；未安装返回 undefined。 */
export function dbObserve(): DbSinkObserveFrame | undefined {
  return installed?.dbSink.observe();
}

/** 终态落库 + 卸载 patch（进程退出/dispose 时调用；可重复调用）。 */
export async function disposeSink(): Promise<void> {
  const current = installed;
  if (!current) return;
  installed = undefined;
  try {
    await current.sink.flushAll();
  } catch {
    /* 终态落库失败不阻断卸载 */
  }
  current.dbSink.unpatchAll();
  current.sink.unpatchAll();
}
