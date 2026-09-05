/**
 * MCPFramework — 结构化日志工具
 *
 * 提供带级别的统一日志输出，支持环境变量控制日志级别。
 *
 * 日志输出目标：
 * - ERROR → console.error (stderr)
 * - WARN  → console.warn  (stderr)
 * - INFO  → console.log   (stdout)
 * - DEBUG → console.log   (stdout)
 *
 * 注意：对于 MCP 服务器，stdout 是协议通道，INFO/DEBUG 输出到 stdout
 * 可能会干扰 MCP 通信。如果遇到问题，可通过环境变量 MCP_FRAMEWORK_LOG_LEVEL=ERROR
 * 将日志级别设为仅输出错误。
 */

// ─── 日志级别 ───────────────────────────────────────────────

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

// ─── 当前日志级别（从环境变量读取） ─────────────────────────

function resolveLogLevel(): LogLevel {
  const env = process.env.MCP_FRAMEWORK_LOG_LEVEL?.toUpperCase();
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
      // 默认日志级别为 WARN，避免 INFO/DEBUG 日志通过 console.log 污染 stdout
      // MCP 协议使用 stdout 通信，任何非协议输出都会干扰通信
      return LogLevel.WARN;
  }
}

let currentLevel = resolveLogLevel();

// ─── 日志函数 ───────────────────────────────────────────────

function formatTime(): string {
  return new Date().toISOString().replace("T", " ").slice(0, 23);
}

function log(level: LogLevel, module: string, message: string, ...args: unknown[]): void {
  if (level < currentLevel) return;

  const levelName = LEVEL_NAMES[level] ?? "UNKNOWN";
  const prefix = `[${formatTime()}] [${levelName}] [${module}]`;
  const extra = args.length > 0 ? " " + args.map((a) => (typeof a === "object" ? JSON.stringify(a) : String(a))).join(" ") : "";

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

// ─── 导出接口 ───────────────────────────────────────────────

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
};
