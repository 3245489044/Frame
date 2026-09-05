/**
 * cline-route-provider — Cline 专属声明式路由 Provider（替代写死的 clineRoutes）。
 *
 * 拦截根取「显式 CLINE_HOME」与「默认 ~/.cline（VSCode globalStorage）」**两者**：
 *  - `home` 为显式隔离根（CLINE_HOME，engine 子进程由 C# 注入，如 `cline_runtime/worker-1`）；
 *  - `defaultHome` 为 SDK 可能回退的 `os.homedir()/.cline`（VSCode 插件 globalStorage）。
 * 只要 SDK 把会话写到任一根下的 `data/sessions/`，都会被拦进内存卷（磁盘零残留），
 * 杜绝「SDK 无视 CLINE_HOME、按自身 globalStoragePath 写 ~/.cline → 路由 miss → 落真实磁盘」的泄漏。
 * 一个内存卷承载所有会话产物（sessions 前缀 + messages/manifest/compaction glob）。
 * 未命中的一切路径（工具读写源码、node_modules、系统目录）默认放行 realFs（H3 路径白名单天然生效）。
 */

import * as os from "os";
import * as path from "path";
import { MemoryVolume } from "../core/memory-volume.js";
import { normalizeFsPath, type Route } from "../core/route.js";
import type { AgentRouteProvider } from "../provider/agent-route-provider.js";
import { clineExtractors } from "./cline-extractor.js";

export interface ClineRouteProviderOptions {
  /** 显式 Cline 数据根目录（CLINE_HOME）。 */
  home: string;
  /** 兜底默认根（缺省 os.homedir()/.cline）；与 home 不同时一并拦截，覆盖 SDK 无视 CLINE_HOME 的回退写盘。 */
  defaultHome?: string;
}

/** 从归一化路径解析 sessionId：`<...>/sessions/<id>/<file>.messages.json`。 */
export function extractClineSessionId(pathName: string): string | undefined {
  const m = /\/sessions\/([^/]+)\/[^/]+\.(messages|manifest)\.json$/i.exec(pathName);
  return m?.[1];
}

/** Cline 声明式路由 Provider：name="cline"，供 AgentSinkHost 接入。 */
export class ClineRouteProvider implements AgentRouteProvider {
  readonly name = "cline";
  readonly roots: string[];
  readonly extractSessionId = extractClineSessionId;
  readonly extractors = clineExtractors;

  private readonly home: string;
  private readonly defaultHome: string;

  constructor(opts: ClineRouteProviderOptions) {
    this.home = normalizeFsPath(opts.home);
    const fallback = opts.defaultHome ?? path.join(os.homedir(), ".cline");
    this.defaultHome = normalizeFsPath(fallback);
    this.roots = Array.from(new Set([this.home, this.defaultHome]));
  }

  buildRoutes(): Route[] {
    const sink = new MemoryVolume();
    const routes: Route[] = [];
    for (const root of this.roots) {
      routes.push({
        spec: { name: "cline-sessions", prefix: `${root}/data/sessions/` },
        sink,
        mirror: true,
      });
      // glob 按根作用域（而非全局 `**`）：避免跨 SDK 拦截到其他 provider 根下的文件，保证 sdk 隔离。
      routes.push(
        { spec: { name: "cline-messages-glob", glob: `${root}/**/*.messages.json` }, sink, mirror: true },
        { spec: { name: "cline-manifest", glob: `${root}/**/*.manifest.json` }, sink, mirror: false },
        { spec: { name: "cline-compaction", glob: `${root}/**/*.compaction.json` }, sink, mirror: false }
      );
    }
    return routes;
  }
}
