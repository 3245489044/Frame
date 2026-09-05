/**
 * BasicMethod — HTTP/SSE 传输层
 *
 * 从 MCPFramework 的 HttpTransport 中提取的通用网络通信逻辑。
 * 提供 HTTP POST 请求封装、JSON-RPC 请求/响应、SSE 响应解析、健康检查 ping。
 *
 * 设计原则：
 * - 零外部依赖（仅依赖 basic-method 自身的 error/constants）
 * - 不绑定任何特定协议或业务类型
 * - 与 MCPFramework 的 HttpTransport 接口兼容
 * - 支持 Streamable HTTP 传输（SSE 格式响应）
 *
 * 使用方式：
 * ```typescript
 * import { HttpTransport } from "basic-method/network/httpTransport";
 *
 * const transport = new HttpTransport({ name: "my-server", url: "http://localhost:3100/mcp" });
 * const result = await transport.request("tools/list");
 * const isAlive = await transport.ping();
 * ```
 */

import { BasicError, ErrorCode } from "../error.js";
import {
  HTTP_DEFAULT_TIMEOUT,
  HTTP_PING_TIMEOUT,
  DEFAULT_CONTENT_TYPE,
  SSE_ACCEPT_HEADER,
  SSE_DATA_PREFIX,
  JSONRPC_VERSION,
} from "../constants.js";

// ═══════════════════════════════════════════════════════════════
// 类型定义
// ═══════════════════════════════════════════════════════════════

/**
 * HTTP 传输层配置
 *
 * 通用配置接口，不绑定任何特定业务类型。
 */
export interface HttpTransportConfig {
  /** 服务器名称（用于日志和错误信息） */
  name?: string;
  /** HTTP/SSE 端点 URL */
  url: string;
  /** 请求超时时间（毫秒，默认 30 秒） */
  timeout?: number;
}

// ═══════════════════════════════════════════════════════════════
// HTTP 传输类
// ═══════════════════════════════════════════════════════════════

/**
 * HTTP 传输层
 *
 * 通过 HTTP POST 发送 JSON-RPC 请求到远程端点。
 * 支持 Streamable HTTP 传输，响应为 SSE 格式或纯 JSON。
 *
 * @example
 * ```typescript
 * // 基本使用
 * const transport = new HttpTransport({ url: "http://localhost:3100/mcp" });
 * const response = await transport.request("tools/list");
 *
 * // 健康检查
 * const isAlive = await transport.ping();
 *
 * // 自定义超时
 * const transport2 = new HttpTransport({ url: "...", timeout: 10000 });
 * ```
 */
export class HttpTransport {
  protected config: HttpTransportConfig;
  protected timeout: number;

  constructor(config: HttpTransportConfig, timeout?: number) {
    this.config = config;
    this.timeout = timeout ?? config.timeout ?? HTTP_DEFAULT_TIMEOUT;
  }

  /**
   * 发送 HTTP POST 请求到端点
   *
   * 提取公共的 fetch 逻辑，供 request() 和 ping() 复用。
   * 使用 AbortController 实现超时控制。
   *
   * @param body 请求体
   * @param timeout 超时时间（毫秒）
   * @returns Fetch Response 对象
   *
   * @throws {BasicError} 超时时抛出 ErrorCode.NETWORK_TIMEOUT
   */
  protected async post(body: Record<string, unknown>, timeout: number): Promise<Response> {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), timeout);

    try {
      const response = await fetch(this.config.url, {
        method: "POST",
        headers: {
          "Content-Type": DEFAULT_CONTENT_TYPE,
          // 支持 Streamable HTTP：同时接受 application/json 和 text/event-stream
          Accept: SSE_ACCEPT_HEADER,
        },
        body: JSON.stringify(body),
        signal: controller.signal,
      });
      return response;
    } catch (err) {
      if (err instanceof Error && err.name === "AbortError") {
        const serverName = this.config.name || "unknown";
        throw new BasicError(
          ErrorCode.NETWORK_TIMEOUT,
          `请求超时 (${timeout}ms): ${serverName}`
        );
      }
      throw err;
    } finally {
      clearTimeout(timeoutId);
    }
  }

  /**
   * 发送 JSON-RPC 请求
   *
   * 构造 JSON-RPC 2.0 请求体，发送 POST 请求，解析响应。
   *
   * @param method JSON-RPC 方法名
   * @param params 请求参数（可选）
   * @returns JSON-RPC 响应（result 或 error 字段的内容）
   *
   * @throws {BasicError} HTTP 状态码非 2xx 时抛出 ErrorCode.NETWORK_FETCH_FAILED
   * @throws {BasicError} SSE 解析失败时抛出 ErrorCode.NETWORK_SSE_PARSE_ERROR
   */
  async request(method: string, params?: Record<string, unknown>): Promise<unknown> {
    const body: Record<string, unknown> = {
      jsonrpc: JSONRPC_VERSION,
      id: Date.now().toString(36) + Math.random().toString(36).slice(2, 8),
      method,
    };
    if (params) {
      body.params = params;
    }

    const response = await this.post(body, this.timeout);

    if (!response.ok) {
      throw new BasicError(
        ErrorCode.NETWORK_FETCH_FAILED,
        `HTTP ${response.status}: ${response.statusText}`
      );
    }

    // 读取响应文本
    const text = await response.text();
    return this.parseResponse(text);
  }

  /**
   * 解析响应文本
   *
   * Streamable HTTP 响应格式：
   * ```
   * event: message
   * id: <uuid>
   * data: {"jsonrpc":"2.0","id":"...","result":{...}}
   *
   * ```
   *
   * 也可能是纯 JSON 格式（某些实现）。
   * 也可能是多事件 SSE（多个 data: 行，需用 \n 连接）。
   *
   * @param text 响应文本
   * @returns 解析后的 JSON 对象
   *
   * @throws {BasicError} 无法解析时抛出 ErrorCode.NETWORK_SSE_PARSE_ERROR
   */
  parseResponse(text: string): unknown {
    // 尝试直接解析为 JSON
    try {
      return JSON.parse(text);
    } catch {
      // 不是纯 JSON，解析 SSE 格式
    }

    // 解析 SSE 格式：收集所有 data: 行，用 \n 连接
    const lines = text.split("\n");
    const dataLines: string[] = [];

    for (const line of lines) {
      if (line.startsWith(SSE_DATA_PREFIX)) {
        dataLines.push(line.slice(SSE_DATA_PREFIX.length));
      }
    }

    if (dataLines.length === 0) {
      throw new BasicError(
        ErrorCode.NETWORK_SSE_PARSE_ERROR,
        "无法解析 SSE 响应: 未找到 data 字段"
      );
    }

    // 多个 data: 行用 \n 连接（符合 SSE 规范）
    const jsonStr = dataLines.join("\n").trim();

    try {
      return JSON.parse(jsonStr);
    } catch {
      throw new BasicError(
        ErrorCode.NETWORK_SSE_PARSE_ERROR,
        `无法解析 SSE data 为 JSON: ${jsonStr.slice(0, 100)}`
      );
    }
  }

  /**
   * 健康检查：发送 POST 请求到端点
   *
   * 复用 post() 方法，避免与 request() 重复的 HTTP 请求代码。
   * 不仅检查 HTTP 状态码，还验证响应体是有效的 JSON-RPC 响应。
   *
   * ★ 修复 Bug 3：error 响应不再视为健康，只有 result 响应才算健康。
   * 如果服务器返回了 JSON-RPC error，说明服务器在运行但功能不正常。
   *
   * @returns 服务器是否健康
   */
  async ping(): Promise<boolean> {
    try {
      const response = await this.post(
        {
          jsonrpc: JSONRPC_VERSION,
          id: "ping",
          method: "health/ping",
        },
        HTTP_PING_TIMEOUT
      );

      // 不仅检查 HTTP 状态码，还验证响应体是有效的 JSON-RPC 响应
      if (!response.ok) return false;

      // 尝试解析响应体，验证 JSON-RPC 有效性
      try {
        const text = await response.text();
        if (!text) return false;

        const parsed = JSON.parse(text);
        // 有效的 JSON-RPC 响应必须有 result 或 error 字段
        if (
          parsed &&
          typeof parsed === "object" &&
          ("result" in parsed || "error" in parsed)
        ) {
          // ★ 修复 Bug 3：只有 result 响应才算健康
          // error 响应说明服务器在运行但功能不正常（如方法不存在）
          if ("result" in parsed) {
            return true;
          }
          // 有 error 字段：服务器活着但返回了错误，不算健康
          return false;
        }
        return false;
      } catch {
        // JSON 解析失败，响应体不是有效 JSON
        return false;
      }
    } catch {
      return false;
    }
  }

  /**
   * ★ 修复：带重试的健康检查
   *
   * 对于启动中的服务器，第一次 ping 很可能失败。
   * 使用指数退避重试，提高瞬态失败场景的容错性。
   *
   * @param retries 重试次数（默认 3）
   * @param baseDelay 基础延迟（毫秒，默认 1000）
   * @returns 服务器是否健康
   */
  async pingWithRetry(retries: number = 3, baseDelay: number = 1000): Promise<boolean> {
    for (let i = 0; i < retries; i++) {
      if (await this.ping()) return true;
      if (i < retries - 1) {
        const delay = baseDelay * Math.pow(2, i); // 1s, 2s, 4s
        await new Promise((r) => setTimeout(r, delay));
      }
    }
    return false;
  }
}
