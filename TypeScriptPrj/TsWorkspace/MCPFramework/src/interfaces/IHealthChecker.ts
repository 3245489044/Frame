/**
 * MCPFramework — 健康检查器接口
 *
 * 定义健康检查的通用接口，用于定期检查远程 MCP 服务器的健康状态。
 * 子项目（如 MCPBridge）实现此接口以提供具体的健康检查逻辑。
 */

/**
 * 健康检查器接口
 *
 * 定期检查所有远程 MCP 服务器的健康状态，
 * 支持暂停/恢复、自动重连和状态变更通知。
 */
export interface IHealthChecker {
  /**
   * 启动定期健康检查
   */
  start(): void;

  /**
   * 停止定期健康检查
   */
  stop(): void;

  /**
   * 暂停健康检查
   */
  pause(): void;

  /**
   * 恢复健康检查
   */
  resume(): void;

  /**
   * 检查所有服务器的健康状态
   */
  checkAll(): Promise<void>;

  /**
   * 检查单个服务器的健康状态
   *
   * @param serverName 服务器名称
   */
  checkServer(serverName: string): Promise<void>;

  /**
   * 获取所有服务器的健康状态摘要
   */
  getStatusSummary(): string;
}
