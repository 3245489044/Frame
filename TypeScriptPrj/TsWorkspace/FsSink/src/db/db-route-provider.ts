/**
 * db-route-provider — DbSink 声明式契约 + 纯路由判定（模块层 sqlite 拦截）。
 *
 * 背景：`sessions.db` 由 `node:sqlite` 的 `DatabaseSync` 原生直写，**不经 JS fs**（实测 fs 计数 0），
 * 故 fs 层路由原理不可达（补 `data/db/` 路由 = 假拦截）。可行拦截面 = 模块层：替换 builtin 的
 * `DatabaseSync` 导出（探针实测 `writable/configurable=true`）。
 *
 * 限域（R1 缓解）：仅「库路径位于受控根 ∧ basename ∈ 白名单」→ 改写；其余（`cron.db`/`connectors.db`/
 * team store/工具自建库）原样放行（fail-open），不猜根、不整根内存化。
 *
 * 消费方：`DbSink`（db-sink.ts）。文献：`Docs/analysis/2026/9/9.13/2026-09-13-FsSink数据库截获-DbSink-分析与实施方案.md`（§5.2/§5.3）。
 */

import { normalizeFsPath } from "../core/route.js";

/** DbSink 形态（能力递增）：off=不 patch；observe=只观测；relocate=改写落点；memory=`/`+行镜像前的真不落盘`。 */
export type DbSinkMode = "off" | "observe" | "relocate" | "memory";

/** 全部合法形态（供 env 解析与守卫断言）。 */
export const DB_SINK_MODES: readonly DbSinkMode[] = ["off", "observe", "relocate", "memory"];

/** 开关环境变量名（缺省 off = 今日行为；一键回退 = 删除/置 off）。 */
export const DB_SINK_ENV = "CLINE_DB_SINK";

/** 解析形态串：未设置/非法 → `off`（fail-open，绝不因拼写错误改变行为）。 */
export function parseDbSinkMode(raw: string | undefined | null): DbSinkMode {
  const v = raw?.trim().toLowerCase();
  return DB_SINK_MODES.find((m) => m === v) ?? "off";
}

/**
 * SDK 专属的库路由声明（`DbSink` 消费，核心不感知 SDK）。
 * 字段均为**原始**路径串（归一化在判定时进行，便于用声明值拼装 relocate 目标）。
 */
export interface DbRouteProvider {
  readonly name: string; // "cline"
  /** 受控根（如 `<dataDir>/db`）：命中之库按白名单接管。 */
  readonly roots: string[];
  /**
   * 额外接管根（可选；如 SDK 默认 db 根）：库名白名单命中但落在这些根时，`relocate`/`memory` 亦接管
   * （`observe` 下与 `roots` 同权，仅观测）。
   */
  readonly claimRoots?: string[];
  /** 库名白名单（basename，大小写不敏感）；缺省 = 根内全部 `*.db`。 */
  readonly dbNames?: string[];
  /** relocate 目标根（缺省 = `roots[0]`）。 */
  readonly relocateRoot?: string;
  /** 安装期求值的形态（供 env 一键回退；非 off 才参与判定）。 */
  mode(): DbSinkMode;
}

/** 单次构造的路由判定结果（observable，供观测帧与契约测试断言）。 */
export interface DbRouteDecision {
  kind: "passthrough" | "relocate" | "memory";
  /** 是否命中某 provider 的「受控根/接管根 ∧ 白名单」。false = 根外或非白名单（放行）。 */
  matched: boolean;
  /** 命中的 provider 名（未命中为空）。 */
  provider?: string;
  /** 实际传给 `DatabaseSync` 的落点（passthrough = 原请求路径；memory = `:memory:`）。 */
  db: string;
  /** 生效形态。 */
  mode: DbSinkMode;
  /** 判定依据（诊断/观测用）。 */
  reason: string;
}

/** 取归一化路径的 basename。 */
function baseName(normalized: string): string {
  const i = normalized.lastIndexOf("/");
  return i < 0 ? normalized : normalized.slice(i + 1);
}

/** 是否位于某个根之内（前缀 + 分隔边界，避免 `/db2` 误命中 `/db`）。 */
function withinRoot(normalized: string, root: string): boolean {
  const r = normalizeFsPath(root);
  return normalized === r || normalized.startsWith(`${r}/`);
}

/** 库名是否在白名单内（缺省 = 全部 `*.db`）。 */
function nameAllowed(base: string, dbNames?: string[]): boolean {
  if (!dbNames || dbNames.length === 0) return base.endsWith(".db");
  return dbNames.some((n) => n.trim().toLowerCase() === base);
}

/** 以 dir 的分隔符风格拼装路径（保留声明值原貌，不引入平台相关大小写改写）。 */
function joinPath(dir: string, base: string): string {
  const sep = dir.includes("\\") ? "\\" : "/";
  return `${dir.replace(/[\\/]+$/, "")}${sep}${base}`;
}

/**
 * 纯判定：请求路径 → 路由决策（不产生副作用，契约测试直接断言）。
 * 未命中任何 provider/白名单，或 provider 形态为 `off`/`observe` → `passthrough`（真实落盘，fail-open）。
 */
export function routeDbPath(
  path: string,
  providers: readonly DbRouteProvider[]
): DbRouteDecision {
  const normalized = normalizeFsPath(path);
  const base = baseName(normalized);
  for (const provider of providers) {
    const mode = provider.mode();
    if (mode === "off") continue;
    if (!nameAllowed(base, provider.dbNames)) continue;
    const inRoots = provider.roots.some((r) => withinRoot(normalized, r));
    const inClaims = (provider.claimRoots ?? []).some((r) => withinRoot(normalized, r));
    if (!inRoots && !inClaims) continue;

    if (mode === "observe") {
      return { kind: "passthrough", matched: true, provider: provider.name, db: path, mode, reason: "observe-only" };
    }
    if (mode === "memory") {
      return { kind: "memory", matched: true, provider: provider.name, db: ":memory:", mode, reason: "whitelisted" };
    }
    const relocateRoot = provider.relocateRoot ?? provider.roots[0] ?? provider.claimRoots?.[0];
    if (!relocateRoot) {
      return { kind: "passthrough", matched: true, provider: provider.name, db: path, mode, reason: "no-relocate-root" };
    }
    return {
      kind: "relocate",
      matched: true,
      provider: provider.name,
      db: joinPath(relocateRoot, base),
      mode,
      reason: inRoots ? "relocate-in-root" : "relocate-from-claim-root",
    };
  }
  return { kind: "passthrough", matched: false, db: path, mode: "off", reason: "not-claimed" };
}
