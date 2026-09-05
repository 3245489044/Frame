/**
 * MCPFramework — 错误处理工具
 *
 * 统一错误格式化、错误码定义。
 * 提供框架级别的错误处理基础设施。
 */

// ─── 错误码 ─────────────────────────────────────────────────

export enum ErrorCode {
  // 通用
  UNKNOWN = "UNKNOWN",
  TIMEOUT = "TIMEOUT",
  NOT_FOUND = "NOT_FOUND",
  INVALID_CONFIG = "INVALID_CONFIG",

  // 网络
  NETWORK_FETCH_FAILED = "NETWORK_FETCH_FAILED",
  NETWORK_TIMEOUT = "NETWORK_TIMEOUT",
  NETWORK_SSE_PARSE_ERROR = "NETWORK_SSE_PARSE_ERROR",

  // MCP
  MCP_SERVER_UNREACHABLE = "MCP_SERVER_UNREACHABLE",
  MCP_TOOL_NOT_FOUND = "MCP_TOOL_NOT_FOUND",
  MCP_AGGREGATION_FAILED = "MCP_AGGREGATION_FAILED",
}

// ─── 带错误码的异常类 ──────────────────────────────────────

export class FrameworkError extends Error {
  public readonly code: ErrorCode;
  public readonly cause?: unknown;

  constructor(code: ErrorCode, message: string, cause?: unknown) {
    super(message);
    this.name = "FrameworkError";
    this.code = code;
    this.cause = cause;
  }
}

// ─── 工具函数 ───────────────────────────────────────────────

/**
 * 统一格式化错误信息
 *
 * 替代代码中重复出现的 `err instanceof Error ? err.message : String(err)` 模式。
 *
 * @param err 任意错误值
 * @param fallback 兜底信息（可选）
 */
export function formatError(err: unknown, fallback = "未知错误"): string {
  if (err instanceof FrameworkError) {
    return `[${err.code}] ${err.message}`;
  }
  if (err instanceof Error) {
    return err.message;
  }
  if (typeof err === "string") {
    return err;
  }
  try {
    return JSON.stringify(err);
  } catch {
    return fallback;
  }
}

/**
 * 从错误中提取 ErrorCode
 *
 * @param err 任意错误值
 */
export function getErrorCode(err: unknown): ErrorCode {
  if (err instanceof FrameworkError) {
    return err.code;
  }
  if (err instanceof Error) {
    if (err.name === "AbortError") return ErrorCode.TIMEOUT;
    if (err.message.includes("fetch")) return ErrorCode.NETWORK_FETCH_FAILED;
    if (err.message.includes("timeout") || err.message.includes("超时"))
      return ErrorCode.TIMEOUT;
  }
  return ErrorCode.UNKNOWN;
}
