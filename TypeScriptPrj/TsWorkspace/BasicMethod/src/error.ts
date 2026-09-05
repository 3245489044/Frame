/**
 * BasicMethod — 错误处理工具
 *
 * 从 MCPFramework 和 MCPBridge 中提取的通用错误处理逻辑。
 * 提供统一的错误码枚举、带错误码的异常类、错误格式化函数。
 *
 * 设计原则：
 * - 零依赖，纯 TypeScript 实现
 * - 通用错误码（不绑定任何特定业务领域）
 * - 与 MCPFramework 的 FrameworkError 接口兼容
 * - 与 MCPBridge 的 BridgeError 模式一致
 */

// ═══════════════════════════════════════════════════════════════
// 错误码
// ═══════════════════════════════════════════════════════════════

/**
 * 通用错误码枚举
 *
 * 按类别分组，预留扩展空间：
 * - 通用 (0-99)
 * - 网络 (100-199)
 * - 文件 (200-299)
 * - 配置 (300-399)
 */
export enum ErrorCode {
  // ── 通用 (0-99) ──────────────────────────────────────────
  /** 未知错误 */
  UNKNOWN = "UNKNOWN",
  /** 操作超时 */
  TIMEOUT = "TIMEOUT",
  /** 资源未找到 */
  NOT_FOUND = "NOT_FOUND",
  /** 参数无效 */
  INVALID_ARGUMENT = "INVALID_ARGUMENT",
  /** 操作不被支持 */
  NOT_SUPPORTED = "NOT_SUPPORTED",

  // ── 网络 (100-199) ───────────────────────────────────────
  /** HTTP 请求失败 */
  NETWORK_FETCH_FAILED = "NETWORK_FETCH_FAILED",
  /** 网络超时 */
  NETWORK_TIMEOUT = "NETWORK_TIMEOUT",
  /** SSE 响应解析失败 */
  NETWORK_SSE_PARSE_ERROR = "NETWORK_SSE_PARSE_ERROR",
  /** 服务器不可达 */
  NETWORK_UNREACHABLE = "NETWORK_UNREACHABLE",
  /** 连接丢失 */
  NETWORK_CONNECTION_LOST = "NETWORK_CONNECTION_LOST",

  // ── 文件 (200-299) ───────────────────────────────────────
  /** 文件未找到 */
  FILE_NOT_FOUND = "FILE_NOT_FOUND",
  /** 文件读取失败 */
  FILE_READ_ERROR = "FILE_READ_ERROR",
  /** 文件写入失败 */
  FILE_WRITE_ERROR = "FILE_WRITE_ERROR",
  /** 目录未找到 */
  DIRECTORY_NOT_FOUND = "DIRECTORY_NOT_FOUND",

  // ── 配置 (300-399) ───────────────────────────────────────
  /** 配置无效 */
  INVALID_CONFIG = "INVALID_CONFIG",
  /** 配置缺失 */
  MISSING_CONFIG = "MISSING_CONFIG",
  /** 配置解析失败 */
  CONFIG_PARSE_ERROR = "CONFIG_PARSE_ERROR",
}

// ═══════════════════════════════════════════════════════════════
// 错误码到默认消息的映射
// ═══════════════════════════════════════════════════════════════

const ERROR_MESSAGES: Record<string, string> = {
  [ErrorCode.UNKNOWN]: "未知错误",
  [ErrorCode.TIMEOUT]: "操作超时",
  [ErrorCode.NOT_FOUND]: "资源未找到",
  [ErrorCode.INVALID_ARGUMENT]: "参数无效",
  [ErrorCode.NOT_SUPPORTED]: "操作不被支持",
  [ErrorCode.NETWORK_FETCH_FAILED]: "HTTP 请求失败",
  [ErrorCode.NETWORK_TIMEOUT]: "网络超时",
  [ErrorCode.NETWORK_SSE_PARSE_ERROR]: "SSE 响应解析失败",
  [ErrorCode.NETWORK_UNREACHABLE]: "服务器不可达",
  [ErrorCode.NETWORK_CONNECTION_LOST]: "连接丢失",
  [ErrorCode.FILE_NOT_FOUND]: "文件未找到",
  [ErrorCode.FILE_READ_ERROR]: "文件读取失败",
  [ErrorCode.FILE_WRITE_ERROR]: "文件写入失败",
  [ErrorCode.DIRECTORY_NOT_FOUND]: "目录未找到",
  [ErrorCode.INVALID_CONFIG]: "配置无效",
  [ErrorCode.MISSING_CONFIG]: "配置缺失",
  [ErrorCode.CONFIG_PARSE_ERROR]: "配置解析失败",
};

// ═══════════════════════════════════════════════════════════════
// 带错误码的异常类
// ═══════════════════════════════════════════════════════════════

/**
 * 通用框架错误类
 *
 * 携带错误码，便于上层统一处理。
 * 与 MCPFramework 的 FrameworkError 接口兼容。
 *
 * @example
 * ```typescript
 * throw new BasicError(ErrorCode.NETWORK_TIMEOUT, "请求超时");
 * throw new BasicError(ErrorCode.FILE_NOT_FOUND, "配置文件未找到", originalError);
 * ```
 */
export class BasicError extends Error {
  /** 错误码 */
  public readonly code: ErrorCode;
  /** 原始错误（可选） */
  public readonly cause?: unknown;

  constructor(code: ErrorCode, message?: string, cause?: unknown) {
    const msg = message || ERROR_MESSAGES[code] || `未知错误 (${code})`;
    super(msg);
    this.name = "BasicError";
    this.code = code;
    this.cause = cause;
  }
}

// ═══════════════════════════════════════════════════════════════
// 工具函数
// ═══════════════════════════════════════════════════════════════

/**
 * 统一格式化错误信息
 *
 * 替代代码中重复出现的 `err instanceof Error ? err.message : String(err)` 模式。
 * 支持 BasicError、标准 Error、字符串、对象等多种错误类型。
 *
 * @param err 任意错误值
 * @param fallback 兜底信息（可选，默认 "未知错误"）
 * @returns 格式化后的错误字符串
 *
 * @example
 * ```typescript
 * formatError(new BasicError(ErrorCode.TIMEOUT, "超时"))  // → "[TIMEOUT] 超时"
 * formatError(new Error("出错了"))                          // → "出错了"
 * formatError("出错了")                                     // → "出错了"
 * formatError(null)                                         // → "未知错误"
 * ```
 */
export function formatError(err: unknown, fallback = "未知错误"): string {
  if (err instanceof BasicError) {
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
 * @returns 提取到的 ErrorCode，无法识别时返回 ErrorCode.UNKNOWN
 *
 * @example
 * ```typescript
 * getErrorCode(new BasicError(ErrorCode.TIMEOUT))  // → ErrorCode.TIMEOUT
 * getErrorCode(new Error("fetch failed"))           // → ErrorCode.NETWORK_FETCH_FAILED
 * getErrorCode("unknown")                           // → ErrorCode.UNKNOWN
 * ```
 */
export function getErrorCode(err: unknown): ErrorCode {
  if (err instanceof BasicError) {
    return err.code;
  }
  if (err instanceof Error) {
    if (err.name === "AbortError") return ErrorCode.TIMEOUT;
    if (err.message.includes("fetch") || err.message.includes("network"))
      return ErrorCode.NETWORK_FETCH_FAILED;
    if (err.message.includes("timeout") || err.message.includes("超时"))
      return ErrorCode.TIMEOUT;
    if (err.message.includes("ENOENT") || err.message.includes("not found"))
      return ErrorCode.FILE_NOT_FOUND;
  }
  return ErrorCode.UNKNOWN;
}

/**
 * 获取错误码对应的默认消息
 *
 * @param code 错误码
 * @returns 默认错误消息
 */
export function getErrorMessage(code: ErrorCode): string {
  return ERROR_MESSAGES[code] || `未知错误 (${code})`;
}
