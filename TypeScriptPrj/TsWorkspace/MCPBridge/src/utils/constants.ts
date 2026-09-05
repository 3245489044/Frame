/**
 * McpBridge — 常量集中管理
 *
 * 所有魔术数字集中在此，避免散落在各模块中。
 * 通用常量从 MCPFramework 导入，Docker 特定常量保留在此。
 *
 * 路径相关常量已迁移到 mcp-bridge.config.json 的 paths/docker 节，
 * 通过 loadPathsConfig() 动态读取。
 */

// ─── Docker 特定常量（保留在 MCPBridge） ────────────────────

/** Docker 守护进程就绪等待超时 */
export const DOCKER_DAEMON_WAIT_TIMEOUT = 60_000;

/** Docker 守护进程轮询间隔 */
export const DOCKER_DAEMON_POLL_INTERVAL = 2_000;

/** 容器启动等待超时 */
export const DOCKER_CONTAINER_START_TIMEOUT = 120_000;

/** 容器端口就绪等待超时 */
export const DOCKER_PORT_WAIT_TIMEOUT = 30_000;

/** 容器端口轮询间隔 */
export const DOCKER_PORT_POLL_INTERVAL = 1_000;

// ─── 配置文件名 ─────────────────────────────────────────────

/** 默认配置文件名称 */
export const DEFAULT_CONFIG_FILE = "mcp-bridge.config.json";
