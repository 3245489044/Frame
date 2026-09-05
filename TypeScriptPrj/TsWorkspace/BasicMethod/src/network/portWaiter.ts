/**
 * BasicMethod — 端口等待工具
 *
 * 从 MCPBridge 的 DockerManager.waitForPorts() 中提取的通用端口等待逻辑。
 * 提供轮询等待 HTTP 端口就绪的能力，支持验证 JSON-RPC 响应有效性。
 *
 * 设计原则：
 * - 零外部依赖（仅依赖 basic-method 自身的 constants）
 * - 不绑定 Docker 或任何特定业务场景
 * - 支持单端口和多端口等待
 * - 支持自定义就绪验证函数
 *
 * 使用方式：
 * ```typescript
 * import { waitForPort, waitForPorts } from "basic-method/network/portWaiter";
 *
 * // 等待单个端口就绪
 * const ready = await waitForPort(3100, 30000);
 *
 * // 等待多个端口就绪
 * const allReady = await waitForPorts([3100, 3101], 30000);
 *
 * // 自定义验证函数
 * const customReady = await waitForPort(8080, 10000, async (port) => {
 *   const res = await fetch(`http://localhost:${port}/health`);
 *   return res.ok;
 * });
 * ```
 */

import { DEFAULT_PORT_POLL_INTERVAL, HTTP_PORT_CHECK_TIMEOUT } from "../constants.js";

// ═══════════════════════════════════════════════════════════════
// 类型定义
// ═══════════════════════════════════════════════════════════════

/**
 * 端口就绪验证函数
 *
 * 自定义验证逻辑，返回 true 表示端口已就绪。
 *
 * @param port 端口号
 * @returns 端口是否就绪
 */
export type PortReadyCheck = (port: number) => Promise<boolean>;

// ═══════════════════════════════════════════════════════════════
// 默认验证函数
// ═══════════════════════════════════════════════════════════════

/**
 * 默认端口就绪检查：验证 HTTP 端点返回有效 JSON-RPC 响应
 *
 * 发送 JSON-RPC 请求到 `http://localhost:{port}/mcp`，
 * 验证响应体包含 result 或 error 字段。
 *
 * @param port 端口号
 * @returns 端口是否就绪
 */
async function defaultPortCheck(port: number): Promise<boolean> {
  try {
    const response = await fetch(`http://localhost:${port}/mcp`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        jsonrpc: "2.0",
        id: "ping",
        method: "health/ping",
      }),
      signal: AbortSignal.timeout(HTTP_PORT_CHECK_TIMEOUT),
    });

    if (response.ok) {
      // 验证响应体是有效的 JSON-RPC
      try {
        const text = await response.text();
        if (text) {
          const parsed = JSON.parse(text);
          if (
            parsed &&
            typeof parsed === "object" &&
            ("result" in parsed || "error" in parsed)
          ) {
            return true;
          }
        }
      } catch {
        // JSON 解析失败，端口可能还未完全就绪
      }
    }
  } catch {
    // 端口未就绪，继续等待
  }

  return false;
}

// ═══════════════════════════════════════════════════════════════
// 核心函数
// ═══════════════════════════════════════════════════════════════

/**
 * 等待单个 HTTP 端口就绪
 *
 * 通过轮询检查端口是否可访问，支持自定义就绪验证函数。
 *
 * @param port 要检查的端口号
 * @param timeout 超时时间（毫秒，默认 30 秒）
 * @param checkFn 自定义就绪验证函数（可选，默认验证 JSON-RPC 响应）
 * @param pollInterval 轮询间隔（毫秒，默认 1 秒）
 * @returns 端口是否在超时内就绪
 *
 * @example
 * ```typescript
 * // 默认验证（JSON-RPC）
 * const ready = await waitForPort(3100);
 *
 * // 自定义验证
 * const ready = await waitForPort(8080, 10000, async (port) => {
 *   const res = await fetch(`http://localhost:${port}/health`);
 *   return res.ok;
 * });
 * ```
 */
export async function waitForPort(
  port: number,
  timeout: number = 30_000,
  checkFn: PortReadyCheck = defaultPortCheck,
  pollInterval: number = DEFAULT_PORT_POLL_INTERVAL
): Promise<boolean> {
  return waitForPorts([port], timeout, checkFn, pollInterval);
}

/**
 * 等待多个 HTTP 端口就绪
 *
 * 检查所有指定端口是否都能通过就绪验证。
 * 所有端口都就绪才返回 true。
 *
 * @param ports 要检查的端口列表
 * @param timeout 总超时时间（毫秒，默认 30 秒）
 * @param checkFn 自定义就绪验证函数（可选，默认验证 JSON-RPC 响应）
 * @param pollInterval 轮询间隔（毫秒，默认 1 秒）
 * @returns 所有端口是否都在超时内就绪
 *
 * @example
 * ```typescript
 * // 等待多个端口
 * const allReady = await waitForPorts([3100, 3101, 3102], 60000);
 *
 * // 自定义验证
 * const allReady = await waitForPorts(
 *   [8080, 8081],
 *   30000,
 *   async (port) => {
 *     const res = await fetch(`http://localhost:${port}/health`);
 *     return res.ok;
 *   }
 * );
 * ```
 */
export async function waitForPorts(
  ports: number[],
  timeout: number = 30_000,
  checkFn: PortReadyCheck = defaultPortCheck,
  pollInterval: number = DEFAULT_PORT_POLL_INTERVAL
): Promise<boolean> {
  if (ports.length === 0) return true;

  const start = Date.now();
  const ready = new Set<number>();

  while (Date.now() - start < timeout) {
    for (const port of ports) {
      if (ready.has(port)) continue;

      if (await checkFn(port)) {
        ready.add(port);
      }
    }

    // 所有端口都已就绪
    if (ready.size >= ports.length) return true;

    await sleep(pollInterval);
  }

  return false;
}

// ═══════════════════════════════════════════════════════════════
// 工具函数
// ═══════════════════════════════════════════════════════════════

/**
 * 延迟等待
 *
 * @param ms 毫秒数
 */
function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
