/**
 * McpBridge — 类型定义
 *
 * 继承 MCPFramework 的 MCP 协议通用类型，添加 MCPBridge 特定类型。
 */

// ═══════════════════════════════════════════════════════════════
// MCPBridge 特定类型
// ═══════════════════════════════════════════════════════════════

// ─── 导入类型 ──────────────────────────────────────────────

import type { PathsConfig } from "../utils/paths.js";
import type { McpToolDefinition } from "mcp-framework/core/types";

// ─── 桥暴露工具定义（origin 标注，C2）─────────────────────────

/**
 * 桥对外暴露的工具定义：在标准 MCP 工具定义上补充来源归属，使消费方（如 CallCenter classifier）
 * 不必靠名字猜测本地 TS/PS vs 聚合远程（防误判）。
 * - origin = "local"：桥自带本地工具（TS 模块 / invoke-ps-script，工具名见 tools-definitions/*.json）
 * - origin = "remote"：经 ToolAggregator 聚合的远程 MCP 服务器工具（serverName 为归属服务器）
 */
export interface BridgeToolDefinition extends McpToolDefinition {
  origin: "local" | "remote";
  /** remote 时的归属服务器名；local 时缺省。 */
  serverName?: string;
}


// ─── Docker 配置 ────────────────────────────────────────────

/** Docker 配置 */
export interface DockerConfig {
  /** 是否启用 Docker 管理（默认 false，即默认不调用 Docker；置 true 恢复自动管理） */
  enabled?: boolean;
  /** Docker Desktop 路径 */
  desktopPath?: string;
  /** 是否自动启动 Docker Desktop */
  autoStartDesktop?: boolean;
  /** 是否自动启动 */
  autoStart?: boolean;
  /** 容器名称 */
  containerName?: string;
  /** 健康检查端口 */
  healthCheckPort?: number;
  /** Docker Compose 文件路径 */
  composePath?: string;
}

/** Docker 状态 */
export interface DockerState {
  /** 是否已安装 Docker */
  installed: boolean;
  /** 守护进程是否就绪 */
  daemonReady: boolean;
  /** 容器是否在运行 */
  containerRunning: boolean;
  /** 错误信息 */
  error?: string;
}

// ─── 桥接器配置 ────────────────────────────────────────────

/** 桥接器配置 */
export interface BridgeConfig {
  /** 路径配置（可选，不配置时使用默认值） */
  paths?: PathsConfig;
  /** Docker 配置 */
  docker: DockerConfig;
  /** 远程 MCP 服务器配置列表 */
  servers?: RemoteMcpServerConfig[];
  /** 桥接器信息 */
  bridge?: {
    version: string;
  };
}

/** 桥接器统计 */
export interface BridgeStats {
  /** 服务器总数 */
  totalServers: number;
  /** 已连接服务器数量 */
  connectedServers: number;
  /** 工具总数 */
  totalTools: number;
  /** 启动时间 */
  startTime: number;
  /** 运行时长（毫秒） */
  uptime: number;
  /** 请求总数 */
  totalRequests: number;
  /** 成功请求数 */
  successfulRequests: number;
  /** 失败请求数 */
  failedRequests: number;
  /** 服务器详情列表 */
  serverDetails?: RemoteMcpServerInfo[];
  /** 工具名冲突数（同名工具被多服务器暴露） */
  conflictCount?: number;
}

// ─── 远程 MCP 服务器 ───────────────────────────────────────

/** 服务器来源（网关双向登记用）：csharp = C# 对等端点，ts = TS/本地端点 */
export type ServerOrigin = "csharp" | "ts";

/** 远程 MCP 服务器配置 */
export interface RemoteMcpServerConfig {
  /** 服务器名称 */
  name: string;
  /** 服务器 URL */
  url: string;
  /** 超时时间（毫秒） */
  timeout?: number;
  /** 是否启用（默认 true） */
  enabled?: boolean;
  /** 来源归属（统一网关用，默认 "ts"） */
  origin?: ServerOrigin;
}

/** 远程 MCP 服务器运行时信息 */
export interface RemoteMcpServerInfo {
  /** 服务器名称 */
  name: string;
  /** 服务器 URL */
  url: string;
  /** 服务器配置 */
  config?: RemoteMcpServerConfig;
  /** 来源归属（统一网关用，默认 "ts"） */
  origin?: ServerOrigin;
  /** 是否已连接 */
  connected: boolean;
  /** 连接状态 */
  status?: string;
  /** 工具列表 */
  tools?: import("mcp-framework/core/types").McpToolDefinition[];
  /** 工具数量 */
  toolCount: number;
  /** 失败次数 */
  failCount?: number;
  /** 最后检查时间 */
  lastCheck: number;
  /** 最后连接时间 */
  lastConnected?: number;
  /** 最后错误信息 */
  lastError?: string;
}

// ─── 服务器状态 ────────────────────────────────────────────

/** 服务器状态 */
export enum ServerStatus {
  CONNECTED = "connected",
  CONNECTING = "connecting",
  DISCONNECTED = "disconnected",
  ERROR = "error",
}
