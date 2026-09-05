/**
 * McpBridge — 请求路由器
 *
 * 基于 BasicMethod 的通用 RequestRouter，添加 MCP 特定的路由逻辑。
 * 负责将 Cline 发来的 CallTool 请求路由到正确的远程 MCP 服务器。
 *
 * 核心逻辑：
 * 1. 根据工具名查找路由映射（通过 ToolAggregator）
 * 2. 通过 HTTP/SSE 转发请求到目标 MCP 服务器
 * 3. 返回响应给 Cline
 */

import { RequestRouter as BaseRequestRouter } from "basic-method/network/requestRouter";
import { ToolAggregator } from "./aggregator.js";
import { ServerStatus } from "./types.js";
import { CallToolResult, ToolRouteEntry } from "mcp-framework/core/types";
import { PathRewriter, IdentityPathRewriter } from "./interfaces/path-rewriter.js";
import { logger } from "../utils/logger.js";
import { formatError } from "../utils/errors.js";

// ─── 请求路由器类 ───────────────────────────────────────────

/**
 * McpBridge 请求路由器
 *
 * 继承 BasicMethod 的通用 RequestRouter，添加 MCP 特定的路由逻辑。
 * 将工具调用请求路由到对应的远程 MCP 服务器。
 */
export class RequestRouter extends BaseRequestRouter {
  private aggregator: ToolAggregator;
  /** 路径重写器（默认 Identity，即宿主直通；Docker 模式由外部注入 DockerPathRewriter） */
  private pathRewriter: PathRewriter;

  /**
   * @param aggregator 工具聚合器实例
   * @param pathRewriter 路径重写器（可选，默认 IdentityPathRewriter = 本地直通）
   */
  constructor(
    aggregator: ToolAggregator,
    pathRewriter?: PathRewriter
  ) {
    super();
    this.aggregator = aggregator;
    this.pathRewriter = pathRewriter ?? new IdentityPathRewriter();
  }

  /**
   * 执行已路由的工具调用（内部方法）
   *
   * 在找到路由后执行实际的工具调用，提取公共逻辑供 routeCall 和重试逻辑复用。
   *
   * @param route 路由条目
   * @param toolName 工具名称
   * @param args 工具参数（已重写路径）
   * @returns 工具执行结果
   */
  private async executeCall(
    route: ToolRouteEntry,
    toolName: string,
    args: Record<string, unknown>
  ): Promise<CallToolResult> {
    // 1. 获取 SDK Client
    const client = this.aggregator.getClient(route.serverName);
    if (!client) {
      return {
        content: [
          {
            type: "text",
            text: `❌ 服务器连接失败: ${route.serverName}（未找到客户端）`,
          },
        ],
        isError: true,
      };
    }

    // 2. 转发请求（SDK Client 内部完成握手/协议，返回标准 CallToolResult）
    try {
      return await client.callTool(toolName, args);
    } catch (err) {
      const errMsg = err instanceof Error ? err.message : String(err);
      return {
        content: [
          {
            type: "text",
            text: `❌ 工具执行错误 [${route.serverName}]: ${errMsg}`,
          },
        ],
        isError: true,
      };
    }
  }

  /**
   * 路由并执行工具调用
   *
   * 支持自动重试：当工具未注册时，先尝试重新聚合所有服务器，
   * 如果聚合后找到工具则重试调用，避免因启动时聚合超时导致的工具不可用。
   *
   * @param toolName 工具名称
   * @param args 工具参数
   * @returns 工具执行结果
   */
  async routeCall(
    toolName: string,
    args: Record<string, unknown>
  ): Promise<CallToolResult> {
    // 0. 路径重写：由注入的 PathRewriter 决定是否翻译（Docker 重写、本地直通）
    const rewrittenArgs = this.pathRewriter.rewrite(args);

    // 1. 查找路由
    let route = this.aggregator.findRoute(toolName);

    // ★ #9 修复：工具未注册时，仅刷新"未连接"的服务器，
    // 不再对全部服务器做 aggregateAll 全量风暴（健康服务器无需重拉）。
    if (!route) {
      logger.info("RequestRouter", `工具 "${toolName}" 未注册，尝试刷新未连接服务器...`);
      await this.refreshDisconnectedServers(toolName);
      // 重试查找（refreshDisconnectedServers 内部成功后已更新路由）
      route = this.aggregator.findRoute(toolName);

      if (!route) {
        return {
          content: [
            {
              type: "text",
              text: `❌ 未知工具: ${toolName}\n\n可用工具请使用 list_tools 查看。`,
            },
          ],
          isError: true,
        };
      }
    }

    // 2. 执行调用（提取为独立方法，支持重试路径复用）
    return this.executeCall(route, toolName, rewrittenArgs);
  }

  /**
   * 只刷新未连接的服务器（跳过已连接的健康服务器，避免全量风暴）。
   * 工具命中后立即返回，不再继续刷新其余服务器。
   */
  private async refreshDisconnectedServers(toolName: string): Promise<void> {
    const servers = this.aggregator.getAllServers();
    const targets = servers.filter(
      (s) => s.status !== ServerStatus.CONNECTED && s.status !== ServerStatus.CONNECTING
    );

    for (const server of targets) {
      try {
        await this.aggregator.aggregateServer(server.name);
        // 一旦该服务器聚合出目标工具即停止刷新
        if (this.aggregator.findRoute(toolName)) {
          logger.info("RequestRouter", `工具 "${toolName}" 已在服务器 ${server.name} 上找到`);
          return;
        }
      } catch (err) {
        logger.warn("RequestRouter", `刷新服务器 ${server.name} 失败: ${formatError(err)}`);
      }
    }
  }
}
