/**
 * BasicMethod — 结构化日志工具
 *
 * 从 MCPFramework 和 MCPBridge 中提取的通用日志逻辑。
 * 提供带级别的统一日志输出，支持环境变量控制日志级别和前缀。
 *
 * 设计原则：
 * - 零依赖，纯 TypeScript 实现
 * - 环境变量驱动配置（无需代码初始化）
 * - 支持运行时动态调整日志级别
 * - 与 MCPFramework 的 logger 接口兼容
 *
 * 日志输出目标：
 * - ERROR → console.error (stderr)
 * - WARN  → console.warn  (stderr)
 * - INFO  → console.log   (stdout)
 * - DEBUG → console.log   (stdout)
 *
 * 环境变量控制：
 * - BASIC_METHOD_LOG_LEVEL — 设置日志级别（DEBUG / INFO / WARN / ERROR / SILENT，默认 WARN）
 * - BASIC_METHOD_LOG_PREFIX — 设置日志前缀（默认 "[BasicMethod]"）
 */

// ═══════════════════════════════════════════════════════════════
// 日志级别
// ═══════════════════════════════════════════════════════════════

export enum LogLevel {
  DEBUG = 0,
  INFO = 1,
  WARN = 2,
  ERROR = 3,
  SILENT = 4,
}

const LEVEL_NAMES: Record<LogLevel, string> = {
  [LogLevel.DEBUG]: "DEBUG",
  [LogLevel.INFO]: "INFO",
  [LogLevel.WARN]: "WARN",
  [LogLevel.ERROR]: "ERROR",
  [LogLevel.SILENT]: "SILENT",
};

// ═══════════════════════════════════════════════════════════════
// 环境变量配置
// ═══════════════════════════════════════════════════════════════

/** 日志级别环境变量名 */
const ENV_LOG_LEVEL = "BASIC_METHOD_LOG_LEVEL";

/** 日志前缀环境变量名 */
const ENV_LOG_PREFIX = "BASIC_METHOD_LOG_PREFIX";

/** 默认日志前缀 */
const DEFAULT_PREFIX = "[BasicMethod]";

/**
 * 从环境变量解析日志级别
 *
 * 默认日志级别为 WARN，避免 INFO/DEBUG 日志通过 console.log 污染 stdout。
 * 对于使用 stdout 作为协议通道的场景（如 MCP 服务器），此默认值可防止干扰。
 */
function resolveLogLevel(): LogLevel {
  const env = process.env[ENV_LOG_LEVEL]?.toUpperCase();
  switch (env) {
    case "DEBUG":
      return LogLevel.DEBUG;
    case "INFO":
      return LogLevel.INFO;
    case "WARN":
      return LogLevel.WARN;
    case "ERROR":
      return LogLevel.ERROR;
    case "SILENT":
      return LogLevel.SILENT;
    default:
      return LogLevel.WARN;
  }
}

/**
 * 获取日志前缀
 */
function resolvePrefix(): string {
  return process.env[ENV_LOG_PREFIX] || DEFAULT_PREFIX;
}

// ═══════════════════════════════════════════════════════════════
// 内部状态
// ═══════════════════════════════════════════════════════════════

let currentLevel = resolveLogLevel();
let currentPrefix = resolvePrefix();

// ═══════════════════════════════════════════════════════════════
// 日志函数
// ═══════════════════════════════════════════════════════════════

function formatTime(): string {
  return new Date().toISOString().replace("T", " ").slice(0, 23);
}

function log(level: LogLevel, module: string, message: string, ...args: unknown[]): void {
  if (level < currentLevel) return;

  const levelName = LEVEL_NAMES[level] ?? "UNKNOWN";
  const prefix = `[${formatTime()}] [${levelName}] ${currentPrefix}[${module}]`;
  const extra =
    args.length > 0
      ? " " +
        args
          .map((a) => (typeof a === "object" ? JSON.stringify(a) : String(a)))
          .join(" ")
      : "";

  const output = `${prefix} ${message}${extra}`;

  switch (level) {
    case LogLevel.ERROR:
      console.error(output);
      break;
    case LogLevel.WARN:
      console.warn(output);
      break;
    case LogLevel.INFO:
      console.log(output);
      break;
    case LogLevel.DEBUG:
      console.log(output);
      break;
    default:
      console.log(output);
      break;
  }
}

// ═══════════════════════════════════════════════════════════════
// 导出接口
// ═══════════════════════════════════════════════════════════════

/**
 * 结构化日志实例
 *
 * 使用方式：
 * ```typescript
 * import { logger } from "basic-method/logger";
 *
 * logger.info("Module", "服务启动");
 * logger.error("Module", "连接失败", err);
 * logger.debug("Module", "详细调试信息");
 * ```
 */
export const logger = {
  debug: (module: string, message: string, ...args: unknown[]) =>
    log(LogLevel.DEBUG, module, message, ...args),

  info: (module: string, message: string, ...args: unknown[]) =>
    log(LogLevel.INFO, module, message, ...args),

  warn: (module: string, message: string, ...args: unknown[]) =>
    log(LogLevel.WARN, module, message, ...args),

  error: (module: string, message: string, ...args: unknown[]) =>
    log(LogLevel.ERROR, module, message, ...args),

  /** 动态设置日志级别（运行时） */
  setLevel: (level: LogLevel) => {
    currentLevel = level;
  },

  /** 获取当前日志级别 */
  getLevel: (): LogLevel => currentLevel,

  /** 动态设置日志前缀（运行时） */
  setPrefix: (prefix: string) => {
    currentPrefix = prefix;
  },

  /** 获取当前日志前缀 */
  getPrefix: (): string => currentPrefix,
};
