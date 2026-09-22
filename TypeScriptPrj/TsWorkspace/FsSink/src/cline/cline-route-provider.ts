/**
 * cline-route-provider — Cline 专属声明式路由 Provider（替代写死的 clineRoutes）。
 *
 * 拦截根 = 权威数据根 `dataDir`（= `<clineDir>/data`，与 SDK `@cline/shared/storage` 的
 * `resolveClineDataDir()` 同算法；TS 侧权威解析器 = `ClineApi/src/runtime/cline-data-root.ts`，
 * C# 侧 = `Frame/CallCenter/CallCenter.Core/Cline/ClineDataRoot.cs`）。
 * 只声明单根：根对上了不需要「猜两边」；根对不上由入口的绑定自检/观测 fail-fast 暴露，不再静默放行。
 * 一个内存卷承载所有会话产物（sessions 前缀 + messages/manifest/compaction glob）。
 * 未命中的一切路径（工具读写源码、node_modules、系统目录、data 下的 settings/cache/auth）默认放行 realFs
 * （H3 路径白名单天然生效；整根内存化会让 SDK 读不到自己的设置，故有意不纳入）。
 */

import { MemoryVolume } from "../core/memory-volume.js";
import { normalizeFsPath, type Route } from "../core/route.js";
import type { AgentRouteProvider } from "../provider/agent-route-provider.js";
import { clineExtractors } from "./cline-extractor.js";

export interface ClineRouteProviderOptions {
  /** 权威 Cline 数据根目录（= `resolveClineDataDir()`，即 `<clineDir>/data`）。 */
  dataDir: string;
  /**
   * miss 观测根（命中该前缀但未命中路由 → 计该 sdk miss）。缺省 = `<dataDir>/sessions`（被拦截面）。
   * 装配方可另传「SDK 默认根 sessions」（`resolveDefaultClineDataRoot().sessionsDir`）
   * 以检出「又走了默认盘」的泄漏——路由仍只有单根，绝不据此放行/兜底拦截。
   */
  missWatchRoots?: string[];
  /**
   * db 根观测面（仅观测，不路由）：命中该前缀但未命中路由 → 计该 sdk 的 `dbMisses`。
   * 缺省 = `<dataDir>/db`（索引库被隔离后的落点）；装配方可另传「SDK 默认根 db」
   * （`resolveDefaultClineDataRoot().dbDir`）以检出「又走了默认盘」的索引库泄漏。
   */
  dbWatchRoots?: string[];
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
  readonly dbRoots: string[];
  readonly extractSessionId = extractClineSessionId;
  readonly extractors = clineExtractors;

  private readonly dataDir: string;

  constructor(opts: ClineRouteProviderOptions) {
    this.dataDir = normalizeFsPath(opts.dataDir);
    // miss 观测面（仅观测，不参与路由）：缺省 = 被拦截面 <dataDir>/sessions。
    this.roots = (opts.missWatchRoots ?? [`${this.dataDir}/sessions`]).map(normalizeFsPath);
    // db 观测面（同上，仅观测）：索引库为原生 SQLite，不经 fs → 单列计数（dbMisses）。
    this.dbRoots = (opts.dbWatchRoots ?? [`${this.dataDir}/db`]).map(normalizeFsPath);
  }

  buildRoutes(): Route[] {
    const sink = new MemoryVolume();
    const root = this.dataDir;
    return [
      { spec: { name: "cline-sessions", prefix: `${root}/sessions/` }, sink, mirror: true },
      { spec: { name: "cline-messages-glob", glob: `${root}/**/*.messages.json` }, sink, mirror: true },
      { spec: { name: "cline-manifest", glob: `${root}/**/*.manifest.json` }, sink, mirror: false },
      { spec: { name: "cline-compaction", glob: `${root}/**/*.compaction.json` }, sink, mirror: false },
    ];
  }
}
