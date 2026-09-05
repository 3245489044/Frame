/**
 * Route / PathRouter — 路径拦截规则与匹配（含 Windows 路径归一）。
 *
 * 规则语义：RouteSpec 三种匹配器（prefix 前缀 / glob 通配 / re 正则）三者取其一即可命中。
 * 未命中任何 Route 的路径放行 realFs（工具读写源码、node_modules、系统目录等零开销透传）。
 */

import type { Sink } from "./sink.js";

/** 路径拦截规则声明：prefix / glob / re 三者取其一即可命中。 */
export interface RouteSpec {
  name: string;
  /** 前缀匹配，如 "<dataDir>/data/sessions/"（内部做 Windows 归一比较）。 */
  prefix?: string;
  /** glob 通配，如 "**\/*.messages.json"（内部转正则）。 */
  glob?: string;
  /** 正则匹配。 */
  re?: RegExp;
}

/** 一条路由：命中 spec 的路径进入哪个 Sink，以及是否异步镜像。 */
export interface Route {
  spec: RouteSpec;
  /** 命中的路径进入哪个 Sink（内存卷）。 */
  sink: Sink;
  /** 命中的写入是否异步镜像到 DbBackend。 */
  mirror?: boolean;
}

/**
 * 归一化 Windows 路径：大小写不敏感（toLowerCase）、`\`→`/`、去掉尾部分隔符。
 * 供路由匹配统一比较；也可用于构造规则前缀。
 */
export function normalizeFsPath(p: string): string {
  return p.replace(/\\/g, "/").replace(/\/+$/, "").toLowerCase();
}

/** 把 glob 表达式转成锚定正则（双星号跨任意目录、双星号加斜杠为零或多级目录、单星号单段内任意、其余字符转义）。 */
export function globToRegExp(glob: string): RegExp {
  // 路径已归一为小写，glob 一并小写，保证大小写一致匹配。
  const g = glob.toLowerCase();
  let out = "";
  let i = 0;
  const n = g.length;
  while (i < n) {
    const c = g[i];
    if (c === "*") {
      if (g[i + 1] === "*") {
        if (g[i + 2] === "/") {
          // "**/": 零或多级目录（含根级），使 "**/*.messages.json" 也能命中根目录文件。
          out += "(?:[^/]+/)*";
          i += 3;
        } else {
          // "**": 跨任意（含 "/"）
          out += ".*";
          i += 2;
        }
      } else {
        // 单 "*": 单段内任意（不含 "/"）
        out += "[^/]*";
        i += 1;
      }
      continue;
    }
    // 其余字符转义（"/" 保留）
    out += c === "/" ? "/" : c.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    i += 1;
  }
  return new RegExp(`^${out}$`);
}

/** 内部匹配器：注册时预归一化前缀 / 预编译 glob，避免每次 resolve 重复计算。 */
interface Matcher {
  route: Route;
  prefix?: string;
  globRe?: RegExp;
  re?: RegExp;
}

/**
 * 路径路由器：按规则分发。未命中任何 Route / 命中 AllowAlways → null（放行 realFs）。
 */
export class PathRouter {
  private matchers: Matcher[] = [];
  private allowAlways: string[] = [];

  add(r: Route): void {
    this.matchers.push({
      route: r,
      prefix: r.spec.prefix !== undefined ? normalizeFsPath(r.spec.prefix) : undefined,
      globRe: r.spec.glob !== undefined ? globToRegExp(r.spec.glob) : undefined,
      re: r.spec.re,
    });
  }

  /** 注册默认放行域（如 <cwd>/node_modules、系统临时目录）：命中则永不进入内存卷。 */
  addAllowAlways(prefix: string): void {
    this.allowAlways.push(normalizeFsPath(prefix));
  }

  /** 解析一条已归一化的路径 → 命中的 Route；未命中返回 null。 */
  resolve(normalized: string): Route | null {
    for (const a of this.allowAlways) {
      if (normalized.startsWith(a)) return null;
    }
    for (const m of this.matchers) {
      if (m.prefix !== undefined && normalized.startsWith(m.prefix)) return m.route;
      if (m.globRe !== undefined && m.globRe.test(normalized)) return m.route;
      if (m.re !== undefined && m.re.test(normalized)) return m.route;
    }
    return null;
  }
}
