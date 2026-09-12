/**
 * FsRouter — 在进程内代理 node:fs（同步/回调）与 node:fs/promises（异步）的写/读 API。
 *
 * 关键实现约束（经 Spike 验证）：
 *  - ESM namespace 只读，patch 必须作用于 CJS `require('node:fs')` 返回的 module.exports 对象
 *    及其 `promises` 子对象（替换属性）；
 *  - 必须 preload（在 SDK `import("@cline/...")` 之前 patch），使 SDK 首次 ESM 加载时
 *    从 CJS 侧快照到 patch 后的函数。
 *
 * 分发：每个被代理方法先归一化路径 → PathRouter.resolve；命中 Route → 走 Sink（内存卷），
 * 未命中 → 透传 realFs 原函数。`mirror` 路由由 FsSink 组合时把同一 Sink 交给 DbMirror 订阅。
 */

import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import type { PathRouter } from "./route.js";
import { normalizeFsPath } from "./route.js";
import type { Sink } from "./sink.js";

const require = createRequire(import.meta.url);

/** 命中内存卷的写类方法。 */
const WRITE = new Set<string>([
  "writeFileSync",
  "writeFile",
  "appendFileSync",
  "appendFile",
  "createWriteStream",
  "mkdirSync",
  "mkdir",
  "rmSync",
  "rm",
  "unlinkSync",
  "unlink",
  "renameSync",
  "rename",
]);

/** 命中内存卷的读类方法（让 SDK 读回闭环）。 */
const READ = new Set<string>([
  "readFileSync",
  "readFile",
  "existsSync",
  "statSync",
  "stat",
  "readdirSync",
  "readdir",
  "createReadStream",
]);

/** 全部被代理方法（patch 循环复用，避免两次展开同一集合）。 */
const METHODS = [...WRITE, ...READ];

/** 同步形态方法名（含 Sync 后缀）。createWriteStream/createReadStream 虽无 Sync 后缀，
 *  但 routeToSink 对其直接返回、callReal 仍走 realFs，adapter 不影响其行为。 */
function isSyncName(name: string): boolean {
  return name.endsWith("Sync");
}

type Adapter = "sync" | "promise" | "callback";

export interface FsRouterDeps {
  router: PathRouter;
}

/**
 * FsRouter — 持有 realFs 原引用，执行 patch/unpatch 与路径分发。
 */
export class FsRouter {
  private readonly realFs: Record<string, unknown>;
  private readonly realPromises: Record<string, unknown>;
  private readonly originalsFs = new Map<string, unknown>();
  private readonly originalsPromises = new Map<string, unknown>();
  private patched = false;
  /** 命中内存卷的调用计数（供观测测试断言）。 */
  private _hits = 0;
  /** 每路由命中计数（按 route.spec.name，供 observe() 观测帧）。 */
  private readonly perRouteHits = new Map<string, number>();
  /** 路由 miss 观测：SDK 根候选前缀 → sdk（定位「又走了默认盘」）。 */
  private readonly missWatch: Array<{ prefix: string; sdk: string }> = [];
  private readonly missesBySdk = new Map<string, number>();


  constructor(
    private readonly deps: FsRouterDeps,
    realFs?: Record<string, unknown>
  ) {
    // 默认取 CJS module.exports（可 patch；ESM namespace 只读）。
    this.realFs = realFs ?? (require("node:fs") as unknown as Record<string, unknown>);
    const promises = this.realFs["promises"];
    if (!promises || typeof promises !== "object") {
      throw new Error("FsRouter: node:fs 的 promises 子对象不可用");
    }
    this.realPromises = promises as Record<string, unknown>;
  }

  get isPatched(): boolean {
    return this.patched;
  }

  /** 命中内存卷的调用计数。 */
  get hits(): number {
    return this._hits;
  }
  /** 注册路由 miss 观测前缀（SDK 根候选）。命中该前缀但未命中路由的路径计为该 sdk 的 miss。 */
  registerMissWatch(prefix: string, sdk: string): void {
    this.missWatch.push({ prefix: normalizeFsPath(prefix), sdk });
  }

  /** 某 sdk 的 miss 计数（「落根内却未拦截」的路径数，定位又走了默认盘）。 */
  misses(sdk: string): number {
    return this.missesBySdk.get(sdk) ?? 0;
  }

  /** 每路由命中计数（route.spec.name → 次数）。 */
  routeHits(): ReadonlyMap<string, number> {
    return this.perRouteHits;
  }

  /** 观测快照：记录一次命中（按路由名）。 */
  private recordHit(routeName: string): void {
    this.perRouteHits.set(routeName, (this.perRouteHits.get(routeName) ?? 0) + 1);
  }

  /** 观测快照：命中某 sdk 根候选但未命中路由 → 计 miss。 */
  private recordMiss(p: string): void {
    for (const w of this.missWatch) {
      if (p.startsWith(w.prefix)) {
        this.missesBySdk.set(w.sdk, (this.missesBySdk.get(w.sdk) ?? 0) + 1);
        break;
      }
    }
  }


  /**
   * 代理 node:fs 与 node:fs/promises 的写/读方法。
   * 调用方必须保证在 `import("@cline/...")` 之前调用（H1 preload 时序）。
   */
  patch(): void {
    if (this.patched) return;
    // node:fs：同步 + 回调形态
    for (const name of METHODS) {
      const fn = this.realFs[name];
      if (typeof fn !== "function") continue;
      this.originalsFs.set(name, fn);
      const adapter: Adapter = isSyncName(name) ? "sync" : "callback";
      this.realFs[name] = this.makeWrapper(name, adapter);
    }
    // node:fs/promises：Promise 形态
    for (const name of METHODS) {
      const fn = this.realPromises[name];
      if (typeof fn !== "function") continue;
      this.originalsPromises.set(name, fn);
      this.realPromises[name] = this.makeWrapper(name, "promise");
    }
    this.patched = true;
  }

  /**
   * 绑定自检（fail-fast 的判据来源）：patch 后动态 import 三个内建 specifier，
   * 比对命名导出是否等于 patch 值（即 CJS 属性）。
   *
   * ESM 命名绑定是「facade 首次求值时的快照」——若有模块在本 patch 之前 import 过 node:fs，
   * 则该 facade 已冻结为原函数，patch 对 ESM 消费者不可见（静默落盘的 A 类失效）。
   * 返回未对齐的方法名清单（空 = 通过）；调用方（preload 入口）据此抛错终止。
   * `fs` 与 `node:fs` 两个 specifier 都验：两者共享同一 facade，任一被提前建立都应被抓住。
   */
  async verifyEsmBindings(): Promise<string[]> {
    if (!this.patched) return [];
    const mismatched = new Set<string>();
    const namespaces = [
      (await import("fs")) as unknown as Record<string, unknown>,
      (await import("node:fs")) as unknown as Record<string, unknown>,
    ];
    for (const name of METHODS) {
      if (!this.originalsFs.has(name)) continue;
      if (namespaces.some((ns) => ns[name] !== this.realFs[name])) mismatched.add(name);
    }
    const promises = (await import("node:fs/promises")) as unknown as Record<string, unknown>;
    for (const name of METHODS) {
      if (!this.originalsPromises.has(name)) continue;
      if (promises[name] !== this.realPromises[name]) mismatched.add(`promises.${name}`);
    }
    return [...mismatched];
  }

  /** 恢复原 fs 行为（engine 退出 / dispose 时调用）。 */
  unpatch(): void {
    if (!this.patched) return;
    for (const [name, fn] of this.originalsFs) {
      this.realFs[name] = fn;
    }
    for (const [name, fn] of this.originalsPromises) {
      this.realPromises[name] = fn;
    }
    this.originalsFs.clear();
    this.originalsPromises.clear();
    this.patched = false;
  }

  private makeWrapper(name: string, adapter: Adapter): (...args: unknown[]) => unknown {
    return (...args: unknown[]) => this.handle(name, adapter, args);
  }

  /** 统一分发入口。 */
  private handle(name: string, adapter: Adapter, args: unknown[]): unknown {
    const raw = this.findPathArg(args);
    if (raw === undefined) {
      return this.callReal(name, adapter, args);
    }
    // 一次归一化：分发判定与 sink 内部键都用同一字符串，避免重复计算 / URL 对象损坏。
    const p = normalizeFsPath(raw);
    const route = this.deps.router.resolve(p);
    if (!route) {
      this.recordMiss(p); // 观测：命中 SDK 根候选却未拦截 -> 计该 sdk miss
      return this.callReal(name, adapter, args);
    }
    this._hits++;
    this.recordHit(route.spec.name);
    return this.routeToSink(name, adapter, args, route.sink, p);
  }

  /**
   * 提取路径参数：首参为 string / URL / Buffer 时归一为字符串；否则 undefined（透传 realFs）。
   * 注意：node:fs 允许 URL / fd 作路径，URL 必须转成字符串参与路由，否则会绕过拦截写真实磁盘。
   */
  private findPathArg(args: unknown[]): string | undefined {
    if (args.length === 0) return undefined;
    const first = args[0];
    if (typeof first === "string") return first;
    if (first instanceof URL) return fileURLToPath(first);
    if (Buffer.isBuffer(first)) return first.toString();
    return undefined;
  }

  /** 透传 realFs / realPromises 原函数。 */
  private callReal(name: string, adapter: Adapter, args: unknown[]): unknown {
    const target = adapter === "promise" ? this.realPromises : this.realFs;
    // 优先用 patch 前保存的原函数（realFs[name] 现已被替换为 wrapper，直接读会递归）。
    const originals = adapter === "promise" ? this.originalsPromises : this.originalsFs;
    const saved = originals.get(name);
    const fn = typeof saved === "function" ? saved : target[name];
    if (typeof fn !== "function") {
      throw new TypeError(`FsRouter: ${name} 不是函数`);
    }
    return (fn as (...a: unknown[]) => unknown).apply(target, args);
  }

  /** 命中 → 路由到 Sink。按方法名映射到 sink 对应方法并适配返回形态。 */
  private routeToSink(
    name: string,
    adapter: Adapter,
    args: unknown[],
    sink: Sink,
    p: string
  ): unknown {
    const data = args[1];
    const isDataWrite = name.startsWith("writeFile") || name.startsWith("appendFile");
    const callOpts = isDataWrite ? args[2] : args[1];

    switch (name) {
      case "writeFileSync":
        sink.writeFileSync(p, data as never, callOpts);
        return undefined;
      case "appendFileSync":
        sink.appendFileSync(p, data as never, callOpts);
        return undefined;
      case "writeFile":
      case "appendFile": {
        const promise =
          name === "writeFile"
            ? sink.writeFile(p, data as never, callOpts)
            : sink.appendFile(p, data as never, callOpts);
        return this.settle(promise, adapter, args);
      }
      case "createWriteStream":
        return sink.createWriteStream(p, callOpts);
      case "mkdirSync":
      case "mkdir": {
        const recursive = Boolean(
          callOpts && typeof callOpts === "object" && (callOpts as { recursive?: boolean }).recursive
        );
        return this.adapt(adapter, args, () => {
          if (recursive) sink.mkdirp(p);
          else sink.mkdir(p, callOpts);
        });
      }
      case "rmSync":
      case "rm":
        return this.adapt(adapter, args, () => sink.rm(p, callOpts));
      case "unlinkSync":
      case "unlink":
        return this.adapt(adapter, args, () => sink.unlink(p));
      case "renameSync":
      case "rename":
        // 目标路径同域（内存卷内），一并归一化。
        return this.adapt(adapter, args, () => sink.rename(p, normalizeFsPath(args[1] as string)));
      case "readFileSync":
        return sink.readFileSync(p, callOpts);
      case "readFile":
        return this.settle(sink.readFile(p, callOpts), adapter, args);
      case "existsSync":
        return sink.existsSync(p);
      case "statSync":
      case "stat":
        return this.adapt(adapter, args, () => sink.stat(p));
      case "readdirSync":
      case "readdir":
        return this.adapt(adapter, args, () => sink.readdir(p));
      case "createReadStream":
        return sink.createReadStream(p, callOpts);
      default:
        // 未知方法：放行 realFs（默认安全）
        return this.callReal(name, adapter, args);
    }
  }

  /** 取 args 末尾回调（若有）。 */
  private lastCallback(
    args: unknown[]
  ): ((err?: Error | null, value?: unknown) => void) | undefined {
    const last = args[args.length - 1];
    return typeof last === "function"
      ? (last as (err?: Error | null, value?: unknown) => void)
      : undefined;
  }

  /**
   * 适配「同步执行、同步产出结果」的调用（mkdir/rm/unlink/rename/stat/readdir）：
   *  - promise 适配器：成功返回 resolved promise，失败返回 rejected promise；
   *  - sync 适配器：直接返回值 / 同步抛错；
   *  - callback 适配器：成功/失败都经末尾回调交付（不同步抛出），无回调时容错返回。
   */
  private adapt(adapter: Adapter, args: unknown[], run: () => unknown): unknown {
    let value: unknown;
    try {
      value = run();
    } catch (err) {
      const e = err instanceof Error ? err : new Error(String(err));
      if (adapter === "callback") {
        const cb = this.lastCallback(args);
        if (cb) {
          cb(e);
          return undefined;
        }
      }
      if (adapter === "promise") return Promise.reject(e);
      throw e;
    }
    if (adapter === "promise") return Promise.resolve(value);
    if (adapter === "sync") return value;
    const cb = this.lastCallback(args);
    if (cb) {
      cb(null, value);
      return undefined;
    }
    return value; // 无回调（容错）：返回同步值保持行为
  }

  /**
   * 把 Promise 结果适配到调用方期望形态：
   *  - promise 适配器：直接返回 promise；
   *  - callback 适配器（node:fs 回调式）：若 args 末尾是回调，则以 promise 结果驱动回调。
   */
  private settle(promise: Promise<unknown>, adapter: Adapter, args: unknown[]): unknown {
    if (adapter === "promise") return promise;
    if (adapter === "sync") return undefined;
    const cb = this.lastCallback(args);
    if (cb) {
      promise.then(
        (value) => cb(null, value),
        (err) => cb(err instanceof Error ? err : new Error(String(err)))
      );
      return undefined;
    }
    // 无回调（容错）：返回 promise 保持行为
    return promise;
  }
}

