/**
 * patch.ts — 全局单例 preload 入口。
 *
 * 为接入方（ClineApi engine 子进程）提供进程级 patch/unpatch/isPatched 便利函数，
 * 内部持有唯一 FsSink 实例，避免同一进程重复 patch。
 */

import { FsSink, type Route } from "../index.js";

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
