/**
 * ClineDbProvider — Cline 专属声明式库路由（受控根 / 白名单 / 形态）。
 *
 * 白名单 = Cline 已知四类库（sessions/tasks/cron/connectors）；未列出的库仍 fail-open 放行。
 * 缺省形态由 AGENT_STORAGE_PROFILE / CLINE_DB_SINK 共同决定（见 agent-storage 策略）。
 */


import * as path from "node:path";
import type { DbRouteProvider, DbSinkMode } from "../db/db-route-provider.js";
import { DB_SINK_ENV, parseDbSinkMode } from "../db/db-route-provider.js";

/** 新档位开关（与 agent-storage 同名单源；此处仅读取，不重复定义策略）。 */
const STORAGE_PROFILE_ENV = "AGENT_STORAGE_PROFILE";


export interface ClineDbProviderOptions {
  /** 权威 Cline 数据根（= `resolveClineDataRoot().dataDir`）。 */
  dataDir: string;
  /** SDK 默认 db 根（= `resolveDefaultClineDataRoot().dbDir`）；仅作为额外接管根，不据此放行路由。 */
  defaultDbDir?: string;
  /** 库名白名单（缺省 `["sessions.db"]`）。 */
  dbNames?: string[];
  /** 形态来源 env（缺省 `process.env`，便于测试注入）。 */
  env?: NodeJS.ProcessEnv;
}

/** Cline 声明式库路由 Provider：name="cline"，供 DbSink 接入。 */
export class ClineDbProvider implements DbRouteProvider {
  readonly name = "cline";
  /** 受控根：权威数据根下的 db 目录（唯一）。 */
  readonly roots: string[];
  /** 额外接管根：SDK 默认 db 根（可为空）。 */
  readonly claimRoots: string[];
  readonly dbNames: string[];
  /** relocate 目标根 = 受控根（单点控制，不依赖宿主 env 注入）。 */
  readonly relocateRoot: string;

  private readonly env: NodeJS.ProcessEnv;

  constructor(opts: ClineDbProviderOptions) {
    this.roots = [path.join(opts.dataDir, "db")];
    this.claimRoots = opts.defaultDbDir ? [opts.defaultDbDir] : [];
    this.dbNames = opts.dbNames ?? ["sessions.db", "tasks.db", "cron.db", "connectors.db"];
    this.relocateRoot = this.roots[0];
    this.env = opts.env ?? process.env;
  }

  /** 形态来源：AGENT_STORAGE_PROFILE 优先；否则旧 CLINE_DB_SINK；缺省 off（单元/旧路径零行为变更）。 */
  mode(): DbSinkMode {
    const profile = this.env[STORAGE_PROFILE_ENV]?.trim().toLowerCase();
    if (profile === "zerodisk") return "memory";
    if (profile === "strictnosqlite") return "memory";
    if (profile === "relocated") return "relocate";
    if (profile === "off") return "off";
    return parseDbSinkMode(this.env[DB_SINK_ENV]);
  }
}
