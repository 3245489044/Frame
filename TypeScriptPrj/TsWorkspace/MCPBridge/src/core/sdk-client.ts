/**
 * McpBridge — SDK Client 包装器
 *
 * 基于官方 @modelcontextprotocol/sdk 的 Client 传输层封装，替换自写的
 * BasicMethod / MCPFramework HttpTransport（原始 fetch + SSE 解析 + 私有 ping）。
 *
 * 职责边界：
 * - 纵轴（传输/协议/握手/会话）：由官方 SDK 承担
 *   - StreamableHTTPClientTransport 负责 initialize 握手、Mcp-Session-Id 维护、增量 SSE
 *   - Client.listTools / callTool / ping 负责标准 JSON-RPC 语义
 * - 横轴（编排/超时/熔断）：仍由上层 ToolAggregator / HealthChecker 承担
 *
 * 超时策略：本包装器不向 requestInit 注入 AbortSignal（该 signal 会同时作用于
 * 长存活的 SSE 接收流，误伤长连接）。连接级超时交由聚合器 Promise.race + close() 处理。
 *
 * 重连：SDK Client 关闭后不可复用，本包装器在 close() 后重置状态，
 * 再次 connect() 时重建 Client + Transport。
 */

import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StreamableHTTPClientTransport } from "@modelcontextprotocol/sdk/client/streamableHttp.js";
import type { CallToolResult, McpToolDefinition } from "mcp-framework/core/types";
import { logger } from "../utils/logger.js";
import { formatError } from "../utils/errors.js";

// ─── 配置 ───────────────────────────────────────────────────

/** SDK Client 包装器配置 */
export interface SdkClientConfig {
  /** 服务器名称（用于日志） */
  name: string;
  /** MCP 端点 URL */
  url: string;
  /** 连接超时（毫秒） */
  timeout?: number;
}

// ─── SDK Client 包装器类 ───────────────────────────────────

/**
 * 包一层官方 SDK Client，为上层提供稳定的最小接口。
 */
export class SdkClient {
  private config: SdkClientConfig;
  private client: Client | null = null;
  private transport: StreamableHTTPClientTransport | null = null;
  private _connected = false;
  /** 进行中的连接 Promise（去重并发 connect 调用） */
  private pendingConnect: Promise<boolean> | null = null;

  constructor(config: SdkClientConfig) {
    this.config = config;
  }

  /** 当前是否已连接 */
  get connected(): boolean {
    return this._connected;
  }

  /**
   * 建立连接（内含 initialize 握手）。
   *
   * 幂等：已连接直接返回 true；并发调用共享同一连接过程。
   * 关闭后调用会重建 Client + Transport。
   */
  async connect(): Promise<boolean> {
    if (this._connected) return true;
    if (this.pendingConnect) return this.pendingConnect;

    this.pendingConnect = this.doConnect().finally(() => {
      this.pendingConnect = null;
    });
    return this.pendingConnect;
  }

  private async doConnect(): Promise<boolean> {
    // 关闭后重建底层对象（SDK Client 关闭后不可复用）
    if (!this.client || !this.transport) {
      this.build();
    }

    try {
      await this.client!!.connect(this.transport!!);
      this._connected = true;
      return true;
    } catch (err) {
      // 连接失败时清理，便于下次重建
      this.reset();
      logger.warn("SdkClient", `连接失败 ${this.config.name}: ${formatError(err)}`);
      return false;
    }
  }

  /** 创建 Client + Transport（内部） */
  private build(): void {
    this.transport = new StreamableHTTPClientTransport(new URL(this.config.url));
    this.client = new Client(
      { name: "mcp-bridge", version: "2.0.0" },
      { capabilities: {} }
    );
  }

  /** 清理底层对象（内部） */
  private reset(): void {
    this._connected = false;
    this.client = null;
    this.transport = null;
  }

  /**
   * 列出服务器全部工具
   */
  async listTools(): Promise<McpToolDefinition[]> {
    if (!this._connected) {
      const ok = await this.connect();
      if (!ok) throw new Error(`服务器未连接: ${this.config.name}`);
    }
    const result = await this.client!!.listTools();
    return result.tools as unknown as McpToolDefinition[];
  }

  /**
   * 调用工具
   *
   * @param toolName 工具名称
   * @param args 工具参数
   */
  async callTool(
    toolName: string,
    args: Record<string, unknown>
  ): Promise<CallToolResult> {
    if (!this._connected) {
      const ok = await this.connect();
      if (!ok) {
        return {
          content: [{ type: "text", text: `❌ 服务器连接失败: ${this.config.name}` }],
          isError: true,
        };
      }
    }
    const result = await this.client!!.callTool({ name: toolName, arguments: args });
    // SDK 返回类型为宽松的 { content: unknown; isError?: unknown }，显式收敛到项目类型
    return {
      content: (result.content ?? []) as CallToolResult["content"],
      isError: Boolean(result.isError),
    };
  }

  /**
   * 标准 MCP ping 健康检查
   */
  async ping(): Promise<boolean> {
    if (!this._connected) {
      const ok = await this.connect();
      if (!ok) return false;
    }
    try {
      await this.client!!.ping();
      return true;
    } catch {
      return false;
    }
  }

  /**
   * 关闭连接（SDK 释放底层资源，杜绝迟到写入）
   */
  async close(): Promise<void> {
    try {
      await this.client!!.close();
    } catch (err) {
      logger.debug("SdkClient", `关闭连接失败 ${this.config.name}: ${formatError(err)}`);
    }
    this.reset();
  }
}