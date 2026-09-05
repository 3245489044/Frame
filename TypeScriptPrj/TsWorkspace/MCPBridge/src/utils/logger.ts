/**
 * McpBridge — 结构化日志工具
 *
 * 继承 MCPFramework 的 Logger，绑定 MCPBridge 的环境变量前缀。
 *
 * 日志级别（从低到高）：
 *   DEBUG < INFO < WARN < ERROR < SILENT
 *
 * 环境变量控制：
 *   MCP_BRIDGE_LOG_LEVEL — 设置日志级别（默认 WARN）
 *   MCP_BRIDGE_LOG_PREFIX — 设置日志前缀（默认 "[McpBridge]"）
 */

import { logger as frameworkLogger, LogLevel } from "mcp-framework/utils/logger";

// ─── 重新导出框架类型 ─────────────────────────────────────

export { LogLevel };

// ─── 环境变量常量 ──────────────────────────────────────────

/** 日志级别环境变量名 */
const ENV_LOG_LEVEL = "MCP_BRIDGE_LOG_LEVEL";

/** 日志前缀环境变量名 */
const ENV_LOG_PREFIX = "MCP_BRIDGE_LOG_PREFIX";

/** 默认日志前缀 */
const DEFAULT_PREFIX = "[McpBridge]";

// ─── 获取环境变量配置 ─────────────────────────────────────

function getPrefix(): string {
  return process.env[ENV_LOG_PREFIX] || DEFAULT_PREFIX;
}

// ─── 创建 McpBridge 日志实例 ──────────────────────────────

/**
 * 基于 MCPFramework 的 logger 创建 McpBridge 专用日志实例
 *
 * 使用方式：
 * ```typescript
 * import { logger } from "../utils/logger";
 * logger.info("Bridge", "服务启动");
 * logger.error("Bridge", "连接失败", err);
 * ```
 */
export const logger = {
  debug: (module: string, message: string, ...args: unknown[]) =>
    frameworkLogger.debug(`[${getPrefix()}]`, `[${module}] ${message}`, ...args),
  info: (module: string, message: string, ...args: unknown[]) =>
    frameworkLogger.info(`[${getPrefix()}]`, `[${module}] ${message}`, ...args),
  warn: (module: string, message: string, ...args: unknown[]) =>
    frameworkLogger.warn(`[${getPrefix()}]`, `[${module}] ${message}`, ...args),
  error: (module: string, message: string, ...args: unknown[]) =>
    frameworkLogger.error(`[${getPrefix()}]`, `[${module}] ${message}`, ...args),
};
