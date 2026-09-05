/**
 * McpBridge — 工具聚合器
 *
 * 负责从所有远程 MCP 服务器收集工具定义，建立工具名 → 服务器的映射表。
 * 支持：
 * - 启动时全量聚合
 * - 按需刷新单个服务器
 * - 增量更新
 * - ★ 聚合超时熔断：每个服务器独立超时控制，超时不阻塞其他服务器
 */

import { SdkClient } from "./sdk-client.js";
import { ToolRouteEntry } from "mcp-framework/core/types";
import {
  BridgeToolDefinition,
  RemoteMcpServerConfig,
  RemoteMcpServerInfo,
  ServerStatus,
} from "./types.js";
import { HTTP_DEFAULT_TIMEOUT } from "mcp-framework/utils/constants";
import { logger } from "../utils/logger.js";
import { BridgeError, ErrorCode, formatError } from "../utils/errors.js";

// ─── 工具聚合器类 ───────────────────────────────────────────

/**
 * 工具聚合器
 *
 * 管理所有远程 MCP 服务器的工具定义收集和路由映射。
 */
export class ToolAggregator {
  /** 服务器运行时信息映射：服务器名 → 信息 */
  private servers = new Map<string, RemoteMcpServerInfo>();

  /** 工具路由映射：工具名 → 路由条目 */
  private routeMap = new Map<string, ToolRouteEntry>();

  /** 工具名冲突映射：裸工具名 → 共享该名的服务器列表（首个占有者可裸名直达） */
  private conflicts = new Map<string, string[]>();

  /** SDK Client 实例缓存：服务器名 → 客户端 */
  private clients = new Map<string, SdkClient>();

  /** 聚合完成后的回调 */
  private onAggregateComplete?: (routeMap: Map<string, ToolRouteEntry>) => void;

  /**
   * @param onAggregateComplete 聚合完成后的回调
   */
  constructor(onAggregateComplete?: (routeMap: Map<string, ToolRouteEntry>) => void) {
    this.onAggregateComplete = onAggregateComplete;
  }

  // ─── 初始化 ───────────────────────────────────────────────

  /**
   * 初始化聚合器
   *
   * @param configs 远程 MCP 服务器配置列表
   */
  async initialize(configs: RemoteMcpServerConfig[]): Promise<void> {
    for (const config of configs) {
      this.servers.set(config.name, {
        name: config.name,
        url: config.url,
        config,
        origin: config.origin ?? "ts",
        status: ServerStatus.DISCONNECTED,
        connected: false,
        lastConnected: undefined,
        lastError: undefined,
        tools: [],
        toolCount: 0,
        failCount: 0,
        lastCheck: Date.now(),
      });
      this.clients.set(config.name, new SdkClient({
        name: config.name,
        url: config.url,
        timeout: config.timeout,
      }));
    }
  }

  // ─── 工具聚合 ─────────────────────────────────────────────

  /**
   * 执行全量工具聚合
   *
   * 向所有已配置的远程 MCP 服务器发送 ListTools 请求，
   * 收集工具定义并建立路由映射。
   * ★ 每个服务器有独立的超时控制，超时不阻塞其他服务器。
   *
   * @returns 聚合结果摘要
   */
  async aggregateAll(): Promise<{
    total: number;
    succeeded: number;
    failed: number;
    toolsCount: number;
  }> {
    const total = this.servers.size;
    let succeeded = 0;
    let failed = 0;

    const promises: Promise<void>[] = [];

    for (const [name] of this.servers) {
      // 跳过 enabled=false 的服务器（统一网关安全登记：只登记不连接）
      const info = this.servers.get(name);
      if (info?.config?.enabled === false) {
        succeeded++; // 禁用视为跳过成功，不参与连接
        continue;
      }
      promises.push(
        this.aggregateServer(name)
          .then(() => {
            succeeded++;
          })
          .catch(() => {
            failed++;
          })
      );
    }

    await Promise.allSettled(promises);
    this.rebuildRouteMap();

    // #8：全部服务器聚合失败时上抛，避免上层误以为成功
    if (total > 0 && succeeded === 0) {
      throw new BridgeError(
        ErrorCode.BRIDGE_AGGREGATE_ALL_FAILED,
        `全部 ${total} 个服务器聚合失败`
      );
    }

    const toolsCount = this.routeMap.size;

    if (this.onAggregateComplete) {
      this.onAggregateComplete(this.routeMap);
    }

    return { total, succeeded, failed, toolsCount };
  }

  /**
   * 聚合单个服务器的工具
   *
   * ★ P1 修复：使用 Promise.race 实现独立超时控制，
   *   超时的服务器标记为 ERROR 状态，不阻塞其他服务器。
   *
   * @param serverName 服务器名称
   */
  async aggregateServer(serverName: string): Promise<void> {
    const info = this.servers.get(serverName);
    if (!info) {
      throw new Error(`未知服务器: ${serverName}`);
    }

    const client = this.clients.get(serverName);
    if (!client) {
      throw new Error(`未找到客户端: ${serverName}`);
    }

    info.status = ServerStatus.CONNECTING;

    // ★ 获取该服务器的超时配置，默认 30 秒
    const timeout = info.config?.timeout ?? HTTP_DEFAULT_TIMEOUT;

    try {
      // ★ 用 Promise.race 实现独立超时熔断
      const response = await Promise.race([
        this.doAggregate(client, info),
        this.createTimeout(timeout, serverName),
      ]);

      info.status = ServerStatus.CONNECTED;
      info.connected = true;
      info.lastConnected = Date.now();
      info.lastError = undefined;
      info.failCount = 0;
      info.toolCount = info.tools?.length || 0;
      info.lastCheck = Date.now();

      // 聚合成功后重建路由映射
      this.rebuildRouteMap();
    } catch (err) {
      // #7：关闭客户端以终止在途请求，防止超时后迟到响应继续写入；
      // SdkClient 关闭后重置，下次 aggregateServer 会自动重建。
      await client.close();

      info.status = ServerStatus.ERROR;
      info.connected = false;
      info.lastError = err instanceof Error ? err.message : String(err);
      info.failCount = (info.failCount || 0) + 1;
      info.tools = [];
      info.toolCount = 0;
      info.lastCheck = Date.now();
      throw err;
    }
  }

  /**
   * 执行实际的聚合请求（提取为独立方法，供 Promise.race 使用）
   *
   * @param client SDK Client
   * @param info 服务器信息（用于写入 tools 列表）
   */
  private async doAggregate(
    client: SdkClient,
    info: RemoteMcpServerInfo
  ): Promise<void> {
    // SDK Client 内部完成握手与协议，直接返回标准 tool 列表
    const tools = await client.listTools();
    // #7 防迟到写：若超时熔断已把 status 置为非 CONNECTING，则丢弃迟到结果
    if (info.status === ServerStatus.CONNECTING) {
      info.tools = tools || [];
    }
  }

  /**
   * 创建超时 Promise（用于 Promise.race 熔断）
   *
   * @param timeout 超时时间（毫秒）
   * @param serverName 服务器名称（用于错误信息）
   */
  private createTimeout(timeout: number, serverName: string): Promise<never> {
    return new Promise((_, reject) => {
      setTimeout(() => {
        reject(new Error(`聚合超时 (${timeout}ms): ${serverName}`));
      }, timeout);
    });
  }

  // ─── 状态更新（供 HealthChecker 使用） ────────────────────

  /**
   * 更新指定服务器的状态
   *
   * @param serverName 服务器名称
   * @param status 新状态
   * @param lastError 错误信息（可选）
   */
  updateServerStatus(serverName: string, status: ServerStatus, lastError?: string | null): void {
    const info = this.servers.get(serverName);
    if (!info) return;
    info.status = status;
    if (status === ServerStatus.CONNECTED) {
      info.connected = true;
    } else if (status === ServerStatus.DISCONNECTED || status === ServerStatus.ERROR) {
      info.connected = false;
    }
    if (lastError !== undefined) {
      info.lastError = lastError ?? undefined;
    }
    info.lastCheck = Date.now();
  }

  // ─── 路由映射 ─────────────────────────────────────────────

  /**
   * 重建工具路由映射表
   */
  private rebuildRouteMap(): void {
    this.routeMap.clear();
    this.conflicts.clear();

    // 记录每个裸工具名首次出现的服务器（首个占有者可裸名直达）
    const firstOwner = new Map<string, string>();

    for (const [serverName, info] of this.servers) {
      if (info.status !== ServerStatus.CONNECTED) continue;

      for (const tool of (info.tools || [])) {
        // 首次出现：占用裸工具名
        if (!firstOwner.has(tool.name)) {
          firstOwner.set(tool.name, serverName);
          this.routeMap.set(tool.name, {
            toolName: tool.name,
            serverName,
            toolDefinition: tool,
          });
          continue;
        }

        // 冲突：裸名已被占用，加命名空间前缀 `${serverName}:${tool.name}`
        const nsKey = `${serverName}:${tool.name}`;
        this.routeMap.set(nsKey, {
          toolName: nsKey,
          serverName,
          toolDefinition: tool,
        });

        // 记录冲突关系（含首个占有者）
        const baseServer = firstOwner.get(tool.name)!;
        const owners = this.conflicts.get(tool.name) ?? [baseServer];
        if (!owners.includes(serverName)) owners.push(serverName);
        this.conflicts.set(tool.name, owners);
      }
    }
  }

  /**
   * 根据工具名查找对应的服务器和工具定义
   *
   * @param toolName 工具名称
   * @returns 路由条目，未找到时返回 undefined
   */
  findRoute(toolName: string): ToolRouteEntry | undefined {
    return this.routeMap.get(toolName);
  }

  /**
   * 获取所有已聚合的工具定义列表（标注 origin=remote + 归属 serverName，C2，供消费方区分本地/远程防误判）
   */
  getAllToolDefinitions(): BridgeToolDefinition[] {
    const tools: BridgeToolDefinition[] = [];
    for (const entry of this.routeMap.values()) {
      tools.push({
        ...entry.toolDefinition,
        origin: "remote",
        serverName: entry.serverName,
      });
    }
    return tools;
  }

  /**
   * 获取已聚合远程工具的「名称 → 归属服务器」清单（C2，供 bridge_origins 返回权威 origin 映射）。
   * 名称即 ListTools 暴露名（含冲突时的命名空间前缀）。
   */
  getRemoteToolOrigins(): Array<{ name: string; serverName: string }> {
    return Array.from(this.routeMap.values()).map((e) => ({
      name: e.toolName,
      serverName: e.serverName,
    }));
  }

  // ─── 服务器信息 ───────────────────────────────────────────

  /**
   * 获取所有服务器的运行时信息
   */
  getAllServers(): RemoteMcpServerInfo[] {
    return Array.from(this.servers.values());
  }

  /**
   * 获取指定服务器的运行时信息
   *
   * @param serverName 服务器名称
   */
  getServer(serverName: string): RemoteMcpServerInfo | undefined {
    return this.servers.get(serverName);
  }

  /**
   * 获取指定服务器的 SDK Client
   *
   * @param serverName 服务器名称
   */
  getClient(serverName: string): SdkClient | undefined {
    return this.clients.get(serverName);
  }

  /**
   * 获取工具名冲突映射（裸工具名 → 共享该名的服务器列表）
   */
  getConflicts(): Map<string, string[]> {
    return new Map(this.conflicts);
  }

  /**
   * 获取已连接服务器数量
   */
  getConnectedCount(): number {
    return Array.from(this.servers.values()).filter(
      (s) => s.status === ServerStatus.CONNECTED
    ).length;
  }

  /**
   * 获取工具总数
   */
  getToolCount(): number {
    return this.routeMap.size;
  }

  /**
   * 获取服务器总数
   */
  getServerCount(): number {
    return this.servers.size;
  }
}
