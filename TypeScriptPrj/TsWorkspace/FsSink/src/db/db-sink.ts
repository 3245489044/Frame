/**
 * db-sink — 模块层 sqlite 拦截（`node:sqlite` 的 `DatabaseSync` 导出替换）。
 *
 * 职责（对齐 DbSink 文档 §5.2/§5.3 与《零落盘：后端替换与会话库 shim 方案对比》§3）：
 *  ① 形态：`off`（缺省，不 patch）/ `observe`（只观测）/ `relocate`（改写落点，保留 WAL）/ `memory`（`:memory:`）；
 *  ② 双轨拦截：A 直接替换 builtin 导出（实测 `writable/configurable=true`）+ B `Module._load` 钩子
 *     （覆盖 `createRequire` 路径；探针实测命中）；
 *  ③ fail-open：拿不到 `node:sqlite` / 导出形态不符 / 任意异常 → 不 patch（等价 off，绝不阻断 engine 启动）；
 *     单次构造改写失败 → 该次直通真实库并记 `fallbacks`（异常级回退）；
 *  ④ 观测：`observe()` 帧（opened/relocated/memoryHits/denied/fallbacks），与 fs 侧 `dbMisses` 互补。
 *
 * 洁净面（I1）：本模块及静态闭包禁止静态 `import ... from "node:fs"`（只用 `createRequire`）；
 * 守卫脚本：`Frame/PowerShellScript/Operation/DSL/Check-FsSinkEntry.ps1`。
 * 时序：在 `installSink()` 内、`import("./engine-main.js")` 之前安装（与 fs patch 同一窗口）。
 */

import { createRequire } from "node:module";
import Module from "node:module";
import * as path from "node:path";
import { normalizeFsPath } from "../core/route.js";
import { routeDbPath, type DbRouteDecision, type DbRouteProvider, type DbSinkMode } from "./db-route-provider.js";

const require = createRequire(import.meta.url);

/** `node:sqlite` 的 DatabaseSync 构造签名（结构最小面；其余经 Proxy 透传）。 */
type DatabaseSyncCtor = new (path: string, options?: unknown) => object;

/** 模块加载器（`Module._load` 未在 @types/node 声明，按结构断言）。 */
const moduleLoader = Module as unknown as {
  _load: (request: string, parent: unknown, isMain?: boolean) => unknown;
};

/** 单次构造的观测记录。 */
export interface DbOpenedRecord {
  /** SDK 请求的库路径。 */
  path: string;
  /** 实际落点（`:memory:` = 已改写为内存库）。 */
  db: string;
  provider?: string;
  /** 判定依据（未命中 = not-claimed）。 */
  reason: string;
}

/** DbSink 观测帧（与 `SinkObserveFrame.perSdk[].dbMisses` 互补：后者说明 db 根被触碰，本帧说明已被改写）。 */
export interface DbSinkObserveFrame {
  /** 生效形态（多 provider 时取首个非 off）。 */
  mode: DbSinkMode;
  /** 每 provider 形态（诊断用）。 */
  providers: Record<string, DbSinkMode>;
  /** 是否已安装 shim。 */
  installed: boolean;
  /** 被构造的库序列（观测面，非真值判据）。 */
  opened: DbOpenedRecord[];
  /** relocate 改写次数（落点被重定向）。 */
  relocated: number;
  /** memory 改写次数（`:memory:`）。 */
  memoryHits: number;
  /** 根外/非白名单构造次数（放行，不接管）。 */
  denied: number;
  /** 改写失败而直通真实库的次数（异常级 fail-open）。 */
  fallbacks: number;
  /** 安装失败原因（空 = 安装正常或未启用）。 */
  installError?: string;
}

/** 测试/宿主 seam（缺省走真实实现）。 */
export interface DbSinkDeps {
  /** 加载 sqlite 模块（缺省 `createRequire("node:sqlite")`）。 */
  loadSqlite?: () => Record<string, unknown>;
  /** 是否安装 `Module._load` 兜底钩子（缺省 true）。 */
  installLoadHook?: boolean;
}

function defaultLoadSqlite(): Record<string, unknown> {
  return require("node:sqlite") as Record<string, unknown>;
}


/**
 * DbSink — 进程级模块 shim（fail-open）。
 * 幂等：`patchAll()` 重复调用只装一次；`unpatchAll()` 与安装严格对称（含 `Module._load` 还原）。
 */
export class DbSink {
  private readonly providers: readonly DbRouteProvider[];
  private readonly deps: DbSinkDeps;
  /** 原始 fs（构造时捕获；仅用于 relocate 建目录，避免静态 import node:fs）。 */
  private readonly rawFs: { mkdirSync?: (p: string, o?: unknown) => unknown };
  private readonly opened: DbOpenedRecord[] = [];
  private patched = false;
  private realDatabaseSync?: DatabaseSyncCtor;
  private shim?: unknown;
  private sqliteModule?: Record<string, unknown>;
  private originalLoad?: (request: string, parent: unknown, isMain?: boolean) => unknown;
  private relocated = 0;
  private memoryHits = 0;
  private denied = 0;
  private fallbacks = 0;
  private installError?: string;

  constructor(providers: readonly DbRouteProvider[], deps: DbSinkDeps = {}) {
    this.providers = providers;
    this.deps = deps;
    // createRequire 取 CJS 引用（非静态 import，不建 ESM facade）；构造早于 fs patch 时即原始函数。
    this.rawFs = require("node:fs") as { mkdirSync?: (p: string, o?: unknown) => unknown };
  }

  get isPatched(): boolean {
    return this.patched;
  }

  /** 当前生效形态（未启用 = off）。 */
  get mode(): DbSinkMode {
    return this.providers.map((p) => p.mode()).find((m) => m !== "off") ?? "off";
  }

  /**
   * 安装模块层 shim（幂等，fail-open）。
   * 返回是否已安装；任何异常（含拿不到 `node:sqlite`、导出不可替换且钩子不可用）→ 不 patch，不抛。
   */
  patchAll(): boolean {
    if (this.patched) return true;
    this.installError = undefined;
    if (this.mode === "off") return false; // 缺省路径：零行为变更

    try {
      const sqlite = (this.deps.loadSqlite ?? defaultLoadSqlite)();
      const real = sqlite?.["DatabaseSync"];
      if (typeof real !== "function") {
        throw new Error("node:sqlite 无 DatabaseSync 导出");
      }
      this.sqliteModule = sqlite;
      this.realDatabaseSync = real as DatabaseSyncCtor;
      this.shim = this.buildShim(real as DatabaseSyncCtor);

      // A：直接替换 builtin 导出（探针实测 writable/configurable=true）。
      let trackA = false;
      try {
        sqlite["DatabaseSync"] = this.shim;
        trackA = sqlite["DatabaseSync"] === this.shim;
      } catch {
        trackA = false;
      }
      // B：Module._load 钩子（覆盖钩子之后 createRequire/require 的调用点）。
      const trackB = this.deps.installLoadHook === false ? false : this.installLoadHook();
      if (!trackA && !trackB) {
        throw new Error("DatabaseSync 导出不可替换且 Module._load 钩子不可用");
      }
      this.patched = true;
      return true;
    } catch (err) {
      this.installError = err instanceof Error ? err.message : String(err);
      this.rollback();
      return false;
    }
  }

  /** 卸载 shim（与 patchAll 对称；可重复调用）。 */
  unpatchAll(): void {
    this.rollback();
  }

  /** 观测帧（opened/relocated/memoryHits/denied/fallbacks + 安装诊断）。 */
  observe(): DbSinkObserveFrame {
    const providers: Record<string, DbSinkMode> = {};
    for (const p of this.providers) providers[p.name] = p.mode();
    return {
      mode: this.mode,
      providers,
      installed: this.patched,
      opened: [...this.opened],
      relocated: this.relocated,
      memoryHits: this.memoryHits,
      denied: this.denied,
      fallbacks: this.fallbacks,
      installError: this.installError,
    };
  }

  /** 还原导出与加载钩子（幂等；仅当导出仍为本 shim 时才还原，避免覆盖他人后装值）。 */
  private rollback(): void {
    if (this.sqliteModule && this.shim !== undefined && this.sqliteModule["DatabaseSync"] === this.shim) {
      try {
        this.sqliteModule["DatabaseSync"] = this.realDatabaseSync;
      } catch {
        /* 导出已不可写：保持现状（不阻断卸载） */
      }
    }
    if (this.originalLoad) {
      moduleLoader._load = this.originalLoad;
      this.originalLoad = undefined;
    }
    this.sqliteModule = undefined;
    this.shim = undefined;
    this.realDatabaseSync = undefined;
    this.patched = false;
  }

  /** 构造 shim：`construct` 先判定路由 → 真实构造器落点改写 → 实例经 Proxy 透传（D5）。 */
  private buildShim(real: DatabaseSyncCtor): unknown {
    const sink = this;
    return new Proxy(real, {
      construct(target, args) {
        const requested = String(args[0]);
        const options = args[1];
        const decision = sink.decide(requested);
        sink.recordDecision(requested, decision);
        const inner =
          options === undefined
            ? Reflect.construct(target, [decision.db])
            : Reflect.construct(target, [decision.db, options]);
        return sink.wrapInstance(inner);
      },
    });
  }

  /** 实例 Proxy：未列名方法亦透传（`prepare/exec/close/function/aggregate/...`），方法绑定到真实实例。 */
  private wrapInstance(inner: object): object {
    return new Proxy(inner, {
      get(target, prop) {
        const value = Reflect.get(target, prop, target) as unknown;
        return typeof value === "function" ? (value as (...a: unknown[]) => unknown).bind(target) : value;
      },
    });
  }

  /** 判定 + 单次调用级 fail-open（relocate 建目录失败 → 直通真实库并记 fallback）。 */
  private decide(requested: string): DbRouteDecision {
    let decision: DbRouteDecision;
    try {
      decision = routeDbPath(requested, this.providers);
    } catch {
      return { kind: "passthrough", matched: false, db: requested, mode: this.mode, reason: "route-error" };
    }
    if (decision.kind === "relocate") {
      try {
        this.ensureDir(path.dirname(decision.db));
      } catch {
        this.fallbacks += 1;
        return { ...decision, kind: "passthrough", db: requested, reason: "relocate-mkdir-failed" };
      }
    }
    return decision;
  }

  /** 建目录（relocate 目标根）；用构造期捕获的 fs 引用，不引入静态 import。 */
  private ensureDir(dir: string): void {
    const mkdirSync = this.rawFs?.mkdirSync;
    if (typeof mkdirSync !== "function") throw new Error("relocate 需要 mkdirSync");
    mkdirSync(dir, { recursive: true });
  }

  /** 记账 + 观测（opened 序列 / relocated / memoryHits / denied）。 */
  private recordDecision(requested: string, decision: DbRouteDecision): void {
    this.opened.push({
      path: requested,
      db: decision.db,
      provider: decision.provider,
      reason: decision.reason,
    });
    if (!decision.matched) {
      this.denied += 1;
      return;
    }
    if (decision.kind === "memory") this.memoryHits += 1;
    if (decision.kind === "relocate" && normalizeFsPath(decision.db) !== normalizeFsPath(requested)) {
      this.relocated += 1;
    }
  }

  /** B 轨：`Module._load` 钩子返回浅拷贝（保留其余导出），使钩子之后的 require 拿到 shim。 */
  private installLoadHook(): boolean {
    const original = moduleLoader._load;
    if (typeof original !== "function") return false;
    const shim = this.shim;
    try {
      moduleLoader._load = function patchedLoad(
        this: unknown,
        request: string,
        parent: unknown,
        isMain?: boolean
      ): unknown {
        if (request === "node:sqlite" || request === "sqlite") {
          const exports = original.call(this, request, parent, isMain) as Record<string, unknown>;
          if (exports && typeof exports === "object") {
            return { ...exports, DatabaseSync: shim };
          }
          return exports;
        }
        return original.call(this, request, parent, isMain);
      };
      this.originalLoad = original;
      return true;
    } catch {
      return false;
    }
  }
}
