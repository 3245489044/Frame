/**
 * McpBridge — 错误处理工具
 *
 * 提供 McpBridge 特定的错误码和错误处理函数。
 * 与 MCPFramework 的 errors 保持接口一致，但使用数字枚举（兼容 JSON 序列化）。
 */

// ─── McpBridge 错误码 ─────────────────────────────────────

/**
 * McpBridge 错误码枚举（数字枚举，兼容 JSON 序列化）
 */
export enum ErrorCode {
  // Docker 相关错误 (1000-1099)
  DOCKER_NOT_FOUND = 1000,
  DOCKER_DESKTOP_NOT_FOUND = 1001,
  DOCKER_CONTAINER_FAILED = 1002,
  DOCKER_COMPOSE_FAILED = 1003,
  DOCKER_HEALTH_CHECK_FAILED = 1004,

  // 桥接相关错误 (1100-1199)
  BRIDGE_INIT_FAILED = 1100,
  BRIDGE_ALREADY_RUNNING = 1101,
  BRIDGE_NOT_RUNNING = 1102,
  BRIDGE_AGGREGATE_ALL_FAILED = 1103,

  // 远程服务器相关错误 (1200-1299)
  REMOTE_SERVER_UNREACHABLE = 1200,
  REMOTE_SERVER_TIMEOUT = 1201,
  REMOTE_SERVER_INVALID_RESPONSE = 1202,
  REMOTE_SERVER_CONNECTION_LOST = 1203,

  // 工具相关错误 (1300-1399)
  TOOL_NOT_FOUND = 1300,
  TOOL_EXECUTION_FAILED = 1301,
  TOOL_ROUTING_FAILED = 1302,
}

// ─── 错误码映射 ───────────────────────────────────────────

/** McpBridge 错误码到可读消息的映射 */
const ERROR_MESSAGES: Record<number, string> = {
  [ErrorCode.DOCKER_NOT_FOUND]: "Docker 未安装或未运行",
  [ErrorCode.DOCKER_DESKTOP_NOT_FOUND]: "Docker Desktop 未找到",
  [ErrorCode.DOCKER_CONTAINER_FAILED]: "Docker 容器启动失败",
  [ErrorCode.DOCKER_COMPOSE_FAILED]: "Docker Compose 执行失败",
  [ErrorCode.DOCKER_HEALTH_CHECK_FAILED]: "Docker 健康检查失败",
  [ErrorCode.BRIDGE_INIT_FAILED]: "桥接器初始化失败",
  [ErrorCode.BRIDGE_ALREADY_RUNNING]: "桥接器已在运行",
  [ErrorCode.BRIDGE_NOT_RUNNING]: "桥接器未运行",
  [ErrorCode.BRIDGE_AGGREGATE_ALL_FAILED]: "全部 MCP 服务器聚合失败",
  [ErrorCode.REMOTE_SERVER_UNREACHABLE]: "远程 MCP 服务器不可达",
  [ErrorCode.REMOTE_SERVER_TIMEOUT]: "远程 MCP 服务器超时",
  [ErrorCode.REMOTE_SERVER_INVALID_RESPONSE]: "远程 MCP 服务器返回无效响应",
  [ErrorCode.REMOTE_SERVER_CONNECTION_LOST]: "远程 MCP 服务器连接丢失",
  [ErrorCode.TOOL_NOT_FOUND]: "工具未找到",
  [ErrorCode.TOOL_EXECUTION_FAILED]: "工具执行失败",
  [ErrorCode.TOOL_ROUTING_FAILED]: "工具路由失败",
};

// ─── BridgeError 类 ───────────────────────────────────────

/**
 * McpBridge 错误类
 *
 * 携带错误码，便于上层统一处理。
 */
export class BridgeError extends Error {
  readonly code: ErrorCode;
  readonly cause?: unknown;

  constructor(code: ErrorCode, message?: string, cause?: unknown) {
    const msg = message || ERROR_MESSAGES[code] || `未知错误 (${code})`;
    super(msg);
    this.name = "BridgeError";
    this.code = code;
    this.cause = cause;
  }
}

// ─── 辅助函数 ─────────────────────────────────────────────

/**
 * 统一格式化错误信息
 *
 * @param err 任意错误值
 * @param fallback 兜底信息（可选）
 */
export function formatError(err: unknown, fallback?: string): string {
  if (err instanceof BridgeError) {
    return `[${err.code}] ${err.message}`;
  }
  if (err instanceof Error) {
    return err.message;
  }
  return fallback || String(err);
}

