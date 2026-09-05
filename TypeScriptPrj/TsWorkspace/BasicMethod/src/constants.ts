/**
 * BasicMethod — 通用常量集中管理
 *
 * 从 MCPFramework 和 MCPBridge 中提取的通用常量。
 * 所有魔术数字集中在此，避免散落在各模块中。
 *
 * 设计原则：
 * - 零依赖，纯 TypeScript 实现
 * - 所有常量使用 const 声明（非枚举），便于 tree-shaking
 * - 按类别分组，预留扩展空间
 */

// ═══════════════════════════════════════════════════════════════
// 超时时间（毫秒）
// ═══════════════════════════════════════════════════════════════

/** HTTP 请求默认超时（30 秒） */
export const HTTP_DEFAULT_TIMEOUT = 30_000;

/** HTTP ping 健康检查超时（5 秒） */
export const HTTP_PING_TIMEOUT = 5_000;

/** HTTP 请求单个端口超时（2 秒） */
export const HTTP_PORT_CHECK_TIMEOUT = 2_000;

/** 重连默认延迟（2 秒） */
export const DEFAULT_RECONNECT_DELAY = 2_000;

/** 流水线阶段默认超时（30 秒） */
export const PIPELINE_DEFAULT_TIMEOUT = 30_000;

/** 健康检查默认间隔（30 秒） */
export const DEFAULT_HEALTH_CHECK_INTERVAL = 30_000;

/** 端口轮询默认间隔（1 秒） */
export const DEFAULT_PORT_POLL_INTERVAL = 1_000;

/** 进程启动等待时间（2 秒） */
export const DEFAULT_PROCESS_START_DELAY = 2_000;

// ═══════════════════════════════════════════════════════════════
// 文件系统
// ═══════════════════════════════════════════════════════════════

/** 向上查找的最大目录层级 */
export const DEFAULT_MAX_DEPTH = 5;

/** 默认排除的目录名 */
export const DEFAULT_EXCLUDE_DIRS = [
  "node_modules",
  ".git",
  "obj",
  "bin",
  "build",
  "dist",
  ".next",
  "coverage",
  ".venv",
  "__pycache__",
  ".DS_Store",
];

// ═══════════════════════════════════════════════════════════════
// 环境变量前缀
// ═══════════════════════════════════════════════════════════════

/** BasicMethod 环境变量前缀 */
export const ENV_PREFIX = "BASIC_METHOD_";

// ═══════════════════════════════════════════════════════════════
// 网络
// ═══════════════════════════════════════════════════════════════

/** HTTP 请求默认 Content-Type */
export const DEFAULT_CONTENT_TYPE = "application/json";

/** SSE 响应接受的 Content-Type */
export const SSE_ACCEPT_HEADER = "application/json, text/event-stream";

/** SSE data: 前缀 */
export const SSE_DATA_PREFIX = "data: ";

/** JSON-RPC 版本 */
export const JSONRPC_VERSION = "2.0";
