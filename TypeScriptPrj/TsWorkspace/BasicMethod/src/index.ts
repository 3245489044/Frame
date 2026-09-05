/**
 * BasicMethod — 基础方法库
 *
 * 提供通用的字符串处理、格式化、错误处理、日志、网络通信、文件路径解析等工具函数。
 * 从 CsprojContext、MCPFramework、MCPBridge 中提取的通用逻辑。
 *
 * 模块结构：
 * - stringUtils     — 字符串处理工具（Markdown 格式化、XML 提取、字符串匹配）
 * - error           — 错误处理（ErrorCode、BasicError、formatError）
 * - logger          — 结构化日志（LogLevel、logger）
 * - constants       — 通用常量集中管理
 * - network/        — 网络通信工具（HttpTransport、portWaiter、pipeline、healthChecker、requestRouter）
 * - file/           — 文件系统工具（pathResolver）
 */

// ─── 核心工具 ────────────────────────────────────────────────

export * from "./stringUtils.js";
export * from "./error.js";
export * from "./logger.js";
export * from "./constants.js";

// ─── 网络工具 ────────────────────────────────────────────────

export { HttpTransport } from "./network/httpTransport.js";
export type { HttpTransportConfig } from "./network/httpTransport.js";
export { waitForPort, waitForPorts } from "./network/portWaiter.js";
export type { PortReadyCheck } from "./network/portWaiter.js";
export { Pipeline } from "./network/pipeline.js";
export type { StageExecutor, PipelineProgress } from "./network/pipeline.js";
export { HealthChecker, HealthCheckMode } from "./network/healthChecker.js";
export type { HealthCheckTarget } from "./network/healthChecker.js";
export { RequestRouter } from "./network/requestRouter.js";
export type { RouteEntry, RouteHandler, RouterStats } from "./network/requestRouter.js";

// ─── 文件工具 ────────────────────────────────────────────────

export {
  findProjectRoot,
  clearProjectRootCache,
  resolveConfigPath,
  clearPathCache,
  loadPathsConfig,
  resolveRelative,
  findFilesByPattern,
} from "./file/pathResolver.js";
export type {
  PathResolveStrategy,
  PathResolveOptions,
  PathsConfig,
} from "./file/pathResolver.js";
