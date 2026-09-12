/**
 * patch-singleton.ts — **测试专用**的进程内 patch 单例（原 `src/core/patch.ts`）。
 *
 * 生产入口唯一 = `ClineApi/src/preload/sink-preload.ts`（installSink：构造 AgentSinkHost → patchAll →
 * verifyBindings fail-fast）。本文件仅为 FsSink 自身的幂等/unpatch 单测提供便捷单例，
 * 不再作为任何生产路径的入口（消除「看似可用」的第三入口）。
 */

import { FsSink, type Route } from "../../src/index.js";

let singleton: FsSink | undefined;

/** 获取（惰性创建）全局单例 FsSink。 */
export function getFsSink(): FsSink {
  if (!singleton) singleton = new FsSink();
  return singleton;
}

/** patch 进程内 fs（须先于 SDK import，H1）。幂等。 */
export function patchFs(routes: Route[]): FsSink {
  const sink = getFsSink();
  sink.patchFs(routes);
  return sink;
}

/** 是否已 patch。 */
export function isPatched(): boolean {
  return singleton?.isPatched ?? false;
}

/** 恢复原 fs 行为。 */
export function unpatchFs(): void {
  singleton?.unpatchFs();
  singleton = undefined;
}
