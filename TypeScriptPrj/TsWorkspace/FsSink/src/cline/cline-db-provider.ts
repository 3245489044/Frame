/**
 * cline-db-provider — Cline 专属声明式库路由（受控根 / 白名单 / 形态）。
 *
 * 受控根 = `<dataDir>/db`（权威数据根派生，与 SDK `resolveDbDataDir()` 同算法；权威解析器 =
 * `ClineApi/src/runtime/cline-data-root.ts`），额外接管根 = SDK 默认 db 根（`~/.cline/data/db`），
 * 使宿主忘记注入 `CLINE_DIR`/`CLINE_DB_DATA_DIR`（兜底落仓库树的场景）在 relocate/memory 下也不落仓库树。
 * 只声明单根，不猜两边；白名单限域到 `sessions.db`（`cron.db`/`connectors.db` 等原样放行）。
 */

import * as path from "node:path";
import type { DbRouteProvider, DbSinkMode } from "../db/db-route-provider.js";
import { DB_SINK_ENV, parseDbSinkMode } from "../db/db-route-provider.js";

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
    this.dbNames = opts.dbNames ?? ["sessions.db"];
    this.relocateRoot = this.roots[0];
    this.env = opts.env ?? process.env;
  }

  /** 形态来自 env（缺省 off；非法值亦退 off，一键回退 = 删除变量）。 */
  mode(): DbSinkMode {
    return parseDbSinkMode(this.env[DB_SINK_ENV]);
  }
}
