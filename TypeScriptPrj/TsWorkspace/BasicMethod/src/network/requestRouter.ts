/**
 * BasicMethod — 请求路由器
 *
 * 从 MCPBridge 的 RequestRouter 中提取的通用请求路由逻辑。
 * 根据路由映射将请求转发到对应的处理函数，支持统计和错误处理。
 *
 * 设计原则：
 * - 零外部依赖（仅依赖 basic-method 自身的 error）
 * - 不绑定任何特定协议或业务类型
 * - 与 MCPBridge 的 RequestRouter 接口兼容
 * - 支持动态注册/注销路由
 *
 * 使用方式：
 * ```typescript
 * import { RequestRouter } from "basic-method/network/requestRouter";
 *
 * const router = new RequestRouter();
 *
 * // 注册路由
 * router.registerRoute("greet", async (params) => {
 *   const name = params.name as string;
 *   return `Hello, ${name}!`;
 * });
 *
 * // 路由请求
 * const result = await router.route("greet", { name: "World" });
 * // → "Hello, World!"
 *
 * // 获取统计
 * const stats = router.getStats();
 * // → { totalRequests: 1, failedRequests: 0 }
 * ```
 */

import { BasicError, ErrorCode, formatError } from "../error.js";

// ═══════════════════════════════════════════════════════════════
// 类型定义
// ═══════════════════════════════════════════════════════════════

/**
 * 路由条目
 *
 * 定义名称到处理函数的映射。
 */
export interface RouteEntry {
  /** 路由名称 */
  name: string;
  /** 处理函数 */
  handler: (params: Record<string, unknown>) => Promise<unknown>;
}

/**
 * 路由处理函数
 *
 * @param params 请求参数
 * @returns 处理结果
 */
export type RouteHandler = (params: Record<string, unknown>) => Promise<unknown>;

/**
 * 路由统计信息
 */
export interface RouterStats {
  /** 请求总数 */
  totalRequests: number;
  /** 失败请求数 */
  failedRequests: number;
}

// ═══════════════════════════════════════════════════════════════
// 请求路由器类
// ═══════════════════════════════════════════════════════════════

/**
 * 请求路由器
 *
 * 将请求按名称路由到对应的处理函数。
 * 支持动态注册/注销、统计、错误处理。
 *
 * @example
 * ```typescript
 * // 基本使用
 * const router = new RequestRouter();
 * router.registerRoute("echo", async (params) => params);
 * const result = await router.route("echo", { message: "hello" });
 *
 * // 批量注册
 * router.registerRoutes([
 *   { name: "fn1", handler: async (p) => p.a },
 *   { name: "fn2", handler: async (p) => p.b },
 * ]);
 *
 * // 检查路由是否存在
 * if (router.hasRoute("echo")) {
 *   const result = await router.route("echo", {});
 * }
 * ```
 */
export class RequestRouter {
  /** 路由表：名称 → 处理函数 */
  private routes = new Map<string, RouteHandler>();

  /** 请求计数 */
  private requestCount = 0;

  /** 失败计数 */
  private failCount = 0;

  // ─── 路由注册 ─────────────────────────────────────────────

  /**
   * 注册单个路由
   *
   * @param name 路由名称
   * @param handler 处理函数
   *
   * @example
   * ```typescript
   * router.registerRoute("greet", async (params) => {
   *   return `Hello, ${params.name}!`;
   * });
   * ```
   */
  registerRoute(name: string, handler: RouteHandler): void {
    this.routes.set(name, handler);
  }

  /**
   * 批量注册路由
   *
   * @param entries 路由条目列表
   *
   * @example
   * ```typescript
   * router.registerRoutes([
   *   { name: "fn1", handler: async (p) => p.a },
   *   { name: "fn2", handler: async (p) => p.b },
   * ]);
   * ```
   */
  registerRoutes(entries: RouteEntry[]): void {
    for (const entry of entries) {
      this.routes.set(entry.name, entry.handler);
    }
  }

  /**
   * 注销路由
   *
   * @param name 路由名称
   * @returns 是否成功注销
   */
  unregisterRoute(name: string): boolean {
    return this.routes.delete(name);
  }

  /**
   * 检查路由是否存在
   *
   * @param name 路由名称
   * @returns 是否存在
   */
  hasRoute(name: string): boolean {
    return this.routes.has(name);
  }

  /**
   * 获取所有已注册的路由名称
   *
   * @returns 路由名称列表
   */
  getRouteNames(): string[] {
    return Array.from(this.routes.keys());
  }

  /**
   * 获取已注册的路由数量
   */
  getRouteCount(): number {
    return this.routes.size;
  }

  // ─── 路由执行 ─────────────────────────────────────────────

  /**
   * 路由并执行请求
   *
   * 根据名称查找对应的处理函数并执行。
   * 如果路由不存在，抛出 BasicError。
   *
   * @param name 路由名称
   * @param params 请求参数
   * @returns 处理结果
   *
   * @throws {BasicError} 路由不存在时抛出 ErrorCode.NOT_FOUND
   *
   * @example
   * ```typescript
   * try {
   *   const result = await router.route("greet", { name: "World" });
   *   console.log(result);
   * } catch (err) {
   *   console.error("路由失败:", err);
   * }
   * ```
   */
  async route(name: string, params: Record<string, unknown>): Promise<unknown> {
    this.requestCount++;

    const handler = this.routes.get(name);
    if (!handler) {
      this.failCount++;
      throw new BasicError(
        ErrorCode.NOT_FOUND,
        `路由未找到: ${name}`
      );
    }

    try {
      return await handler(params);
    } catch (err) {
      this.failCount++;
      throw new BasicError(
        ErrorCode.UNKNOWN,
        `路由执行失败: ${name} - ${formatError(err)}`,
        err
      );
    }
  }

  // ─── 统计 ─────────────────────────────────────────────────

  /**
   * 获取路由统计信息
   *
   * @returns 统计信息（请求总数、失败数）
   */
  getStats(): RouterStats {
    return {
      totalRequests: this.requestCount,
      failedRequests: this.failCount,
    };
  }

  /**
   * 重置统计信息
   */
  resetStats(): void {
    this.requestCount = 0;
    this.failCount = 0;
  }
}
