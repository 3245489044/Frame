/**
 * cline-route — Cline 路由的兼容薄封装。
 *
 * 路由逻辑已迁至 `ClineRouteProvider`（声明式 Provider）。本文件保留 `clineRoutes()` 函数形态，
 * 内部走 Provider，使既有调用方（FsSink.patchFs / 测试）零破坏（可回退原则）。
 */

import type { Route } from "../core/route.js";
import {
  ClineRouteProvider,
  type ClineRouteProviderOptions,
} from "./cline-route-provider.js";

export interface ClineRoutesOptions extends ClineRouteProviderOptions {}

/** 为 CLINE_HOME（+ 默认 ~/.cline 兜底）生成 Cline 会话数据拦截规则（兼容薄封装）。 */
export function clineRoutes(opts: ClineRoutesOptions): Route[] {
  return new ClineRouteProvider(opts).buildRoutes();
}

