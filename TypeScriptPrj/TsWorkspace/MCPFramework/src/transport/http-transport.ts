/**
 * MCPFramework — HTTP/SSE 传输层
 *
 * 负责与远程 MCP 服务器通过 HTTP 协议通信。
 * 支持 JSON-RPC 请求封装、SSE 响应解析、健康检查 ping。
 *
 * 适用于连接任何支持 Streamable HTTP 传输的 MCP 服务器。
 *
 * 参考: https://github.com/punkpeye/mcp-proxy
 */

import { FrameworkError, ErrorCode } from "../utils/errors.js";
import { HTTP_DEFAULT_TIMEOUT, HTTP_PING_TIMEOUT } from "../utils/constants.js";

// ─── 通用传输配置 ───────────────────────────────────────────

/**
 * HTTP 传输层配置
 *
 * 通用配置接口，不绑定任何特定业务类型。
 */
export interface HttpTransportConfig {
  /** 服务器名称（用于日志和错误信息） */
  name?: string;
  /** MCP HTTP/SSE 端点 URL */
  url: string;
  /** 请求超时时间（毫秒） */
  timeout?: number;
}

// ─── HTTP 传输类 ───────────────────────────────────────────

/**
 * HTTP 传输层
 *
 * 通过 HTTP POST 发送 JSON-RPC 请求到 MCP 端点。
 * 支持 Streamable HTTP 传输，响应为 SSE 格式或纯 JSON。
 */
export class HttpTransport {
  protected config: HttpTransportConfig;
  protected timeout: number;

  constructor(config: HttpTransportConfig, timeout?: number) {
    this.config = config;
    this.timeout = timeout ?? config.timeout ?? HTTP_DEFAULT_TIMEOUT;
  }

  /**
   * 发送 HTTP POST 请求到 MCP 端点
   *
   * 提取公共的 fetch 逻辑，供 request() 和 ping() 复用。
   *
   * @param body 请求体
   * @param timeout 超时时间（毫秒）
   */
  protected async post(body: Record<string, unknown>, timeout: number): Promise<Response> {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), timeout);

    try {
      const response = await fetch(this.config.url, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          // 支持 Streamable HTTP：同时接受 application/json 和 text/event-stream
          Accept: "application/json, text/event-stream",
        },
        body: JSON.stringify(body),
        signal: controller.signal,
      });
      return response;
    } catch (err) {
      if (err instanceof Error && err.name === "AbortError") {
        const serverName = this.config.name || "unknown";
        throw new FrameworkError(
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
   * 发送 JSON-RPC 请求到 MCP 服务器
   *
   * @param method JSON-RPC 方法名
   * @param params 请求参数
   * @returns JSON-RPC 响应
   */
  async request(method: string, params?: Record<string, unknown>): Promise<unknown> {
    const body: Record<string, unknown> = {
      jsonrpc: "2.0",
      id: Date.now().toString(36) + Math.random().toString(36).slice(2, 8),
      method,
    };
    if (params) {
      body.params = params;
    }

    const response = await this.post(body, this.timeout);

    if (!response.ok) {
      throw new FrameworkError(
        ErrorCode.NETWORK_FETCH_FAILED,
        `HTTP ${response.status}: ${response.statusText}`
      );
    }

    // 读取响应文本
    const text = await response.text();
    return this.parseResponse(text);
  }

  /**
   * 发送 ListTools 请求
   */
  async listTools(): Promise<unknown> {
    return this.request("tools/list");
  }

  /**
   * 发送 CallTool 请求
   *
   * @param toolName 工具名称
   * @param args 工具参数
   */
  async callTool(toolName: string, args: Record<string, unknown>): Promise<unknown> {
    return this.request("tools/call", {
      name: toolName,
      arguments: args,
    });
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
   */
  private parseResponse(text: string): unknown {
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
      if (line.startsWith("data: ")) {
        dataLines.push(line.slice(6));
      }
    }

    if (dataLines.length === 0) {
      throw new FrameworkError(
        ErrorCode.NETWORK_SSE_PARSE_ERROR,
        "无法解析 SSE 响应: 未找到 data 字段"
      );
    }

    // 多个 data: 行用 \n 连接（符合 SSE 规范）
    const jsonStr = dataLines.join("\n").trim();

    try {
      return JSON.parse(jsonStr);
    } catch {
      throw new FrameworkError(
        ErrorCode.NETWORK_SSE_PARSE_ERROR,
        `无法解析 SSE data 为 JSON: ${jsonStr.slice(0, 100)}`
      );
    }
  }

  /**
   * 健康检查：发送 POST 请求到 MCP 端点
   *
   * 复用 post() 方法，避免与 request() 重复的 HTTP 请求代码。
   */
  async ping(): Promise<boolean> {
    try {
      const response = await this.post(
        {
          jsonrpc: "2.0",
          id: "ping",
          method: "tools/list",
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
        // 有 error 但结构完整也算有效响应（服务器活着但返回了错误）
        return (
          parsed !== null &&
          typeof parsed === "object" &&
          ("result" in parsed || "error" in parsed)
        );
      } catch {
        // JSON 解析失败，响应体不是有效 JSON
        return false;
      }
    } catch {
      return false;
    }
  }
}
