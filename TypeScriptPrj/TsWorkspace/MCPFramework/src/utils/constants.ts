/**
 * MCPFramework — 常量集中管理
 *
 * 所有魔术数字集中在此，避免散落在各模块中。
 * 提供框架级别的通用常量。
 */

// ─── 超时时间（毫秒） ───────────────────────────────────────

/** HTTP 请求默认超时 */
export const HTTP_DEFAULT_TIMEOUT = 30_000;

/** HTTP ping 超时 */
export const HTTP_PING_TIMEOUT = 5_000;

/** 重连默认延迟 */
export const DEFAULT_RECONNECT_DELAY = 2_000;

/** 流水线阶段默认超时 */
export const PIPELINE_DEFAULT_TIMEOUT = 30_000;

/** 健康检查默认间隔 */
export const DEFAULT_HEALTH_CHECK_INTERVAL = 30_000;

// ─── 环境变量前缀 ──────────────────────────────────────────

/** 环境变量前缀 */
export const ENV_PREFIX = "MCP_FRAMEWORK_";
