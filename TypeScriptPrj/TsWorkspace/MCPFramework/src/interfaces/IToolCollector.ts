/**
 * MCPFramework — 工具收集器接口
 *
 * 定义工具收集的通用接口，用于从远程 MCP 服务器收集工具定义。
 * 子项目（如 MCPBridge）实现此接口以提供具体的工具收集逻辑。
 */

import { McpToolDefinition, ToolRouteEntry } from "../core/types.js";

/**
 * 工具收集器接口
 *
 * 负责从多个 MCP 服务器收集工具定义，建立工具名 → 服务器的映射表。
 */
export interface IToolCollector {
  /**
   * 初始化收集器
   *
   * @param configs 远程 MCP 服务器配置列表
   */
  initialize(configs: unknown[]): Promise<void>;

  /**
   * 执行全量工具聚合
   *
   * 向所有已配置的远程 MCP 服务器发送 ListTools 请求，
   * 收集工具定义并建立路由映射。
   *
   * @returns 聚合结果摘要
   */
  aggregateAll(): Promise<{
    total: number;
    succeeded: number;
    failed: number;
    toolsCount: number;
  }>;

  /**
   * 聚合单个服务器的工具
   *
   * @param serverName 服务器名称
   */
  aggregateServer(serverName: string): Promise<void>;

  /**
   * 获取所有已聚合的工具定义列表
   */
  getAllToolDefinitions(): McpToolDefinition[];

  /**
   * 根据工具名查找对应的服务器和工具定义
   *
   * @param toolName 工具名称
   * @returns 路由条目，未找到时返回 undefined
   */
  findRoute(toolName: string): ToolRouteEntry | undefined;

  /**
   * 获取所有服务器的运行时信息
   */
  getAllServers(): unknown[];

  /**
   * 获取指定服务器的运行时信息
   *
   * @param serverName 服务器名称
   */
  getServer(serverName: string): unknown | undefined;

  /**
   * 获取已连接服务器数量
   */
  getConnectedCount(): number;

  /**
   * 获取工具总数
   */
  getToolCount(): number;

  /**
   * 获取服务器总数
   */
  getServerCount(): number;
}
