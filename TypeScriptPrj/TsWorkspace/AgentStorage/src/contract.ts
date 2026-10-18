/**
 * AgentStorage 契约 — 通用 Agent 存储声明（核心不感知任何 Agent 专名）。
 *
 * 一个 Provider 同时声明三件事：
 *  ① fs 路由（复用 fs-sink 的 AgentRouteProvider：sessions/messages/manifest/...）；
 *  ② DB 接管声明（DbClaim：受控根 + 库名白名单 + 形态）；
 *  ③ 可选能力（env 缺省、scratch 根、严格零 SQLite 的 sessionBackend）。
 *
 * 接入新 Agent = 新增一个 Provider；核心 Host / 策略 / 门禁零改动。
 */

import type {
  AgentRouteProvider,
  DbSinkMode,
  Route,
} from "fs-sink";

/** 存储档位（对使用者暴露的单一入口）。 */
export type StorageProfile = "off" | "relocated" | "zeroDisk" | "strictNoSqlite";

/** 解析后的策略（Host/DbSink 消费）。 */
export interface StoragePolicy {
  profile: StorageProfile;
  /** 会话产物面：off=不拦截；memory=内存卷（磁盘零残留）。 */
  fs: "off" | "memory";
  /** 索引库面：off/observe/relocate/memory。 */
  db: DbSinkMode;
  /** 严格目标：不加载 node:sqlite 模块（依赖 Provider.sessionBackend）。 */
  strictNoSqlite: boolean;
}

/** 一次安装的上下文（由 Provider 解析，核心只读）。 */
export interface AgentStorageContext {
  /** 隔离 worker 根（可选；Shared 模式为空）。 */
  workerRoot?: string;
  /** 权威数据根（= SDK 实际写盘根）。 */
  dataDir: string;
  /** 会话产物根。 */
  sessionsDir: string;
  /** 索引库根。 */
  dbDir: string;
  /** SDK 默认数据根（泄漏观测用）。 */
  defaultDataDir?: string;
  /** SDK 默认索引库根（泄漏观测用）。 */
  defaultDbDir?: string;
  env: NodeJS.ProcessEnv;
}

/** 单条 DB 接管声明（结构事实：根 + 库名 + 形态）。 */
export interface DbClaim {
  name: string;
  roots: string[];
  claimRoots?: string[];
  /** basename 白名单；缺省 = 根内全部 *.db。 */
  dbNames?: string[];
  relocateRoot?: string;
  /** 单条覆盖形态；缺省用 policy.db。 */
  mode?: DbSinkMode;
}

/** 通用 Provider 契约。 */
export interface AgentStorageProvider extends AgentRouteProvider {
  /** 解析本次安装的权威上下文（env 已应用 Provider 缺省）。 */
  context(env: NodeJS.ProcessEnv): AgentStorageContext;
  /** 缺省环境变量（只填空，不覆盖宿主显式值）。 */
  env?(env: NodeJS.ProcessEnv): Record<string, string | undefined>;
  /** DB 接管声明（按策略求值）。 */
  dbClaims(context: AgentStorageContext, policy: StoragePolicy): DbClaim[];
  /** worker 生命周期清理面（可选；仅隔离根下使用）。 */
  scratchRoots?(context: AgentStorageContext): string[];
  /** 严格零 SQLite 后端（可选；仅 strictNoSqlite 时注入 SDK）。 */
  sessionBackend?(context: AgentStorageContext): unknown;
  /**
   * 显式允许落盘的残留（相对/绝对路径前缀）。zeroDisk 下仅这些残留可接受；
   * 门禁据此把「已声明残留」与「未声明泄漏」区分开（禁静默丢事实）。
   */
  residualPaths?(context: AgentStorageContext): string[];
}

/** Provider 路由工厂辅助（新 Agent 直接复用 MemoryVolume + Route）。 */
export type AgentStorageRoute = Route;
