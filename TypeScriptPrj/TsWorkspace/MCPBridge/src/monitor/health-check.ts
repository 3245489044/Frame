/**
 * McpBridge — 健康检查模块
 *
 * 基于 BasicMethod 的通用 HealthChecker，添加 MCP 特定的健康检查逻辑。
 * 使用 ToolAggregator 获取服务器列表和传输层，执行 MCP 协议级别的健康检查。
 *
 * 优化：感知 Docker 状态，Docker 启动期间进入等待模式。
 * ★ P2 修复：连续失败熔断保护，避免频繁重试已宕机的服务器。
 */

import { HealthChecker as BaseHealthChecker, HealthCheckMode } from "basic-method/network/healthChecker";
import type { HealthCheckTarget } from "basic-method/network/healthChecker";
import { ToolAggregator } from "../core/aggregator.js";
import { ServerStatus } from "../core/types.js";
import { logger } from "../utils/logger.js";
import { formatError } from "../utils/errors.js";

// 重新导出 HealthCheckMode 保持接口兼容
export { HealthCheckMode };

// ─── 熔断常量 ───────────────────────────────────────────────

/** 连续失败阈值：超过此值进入熔断状态 */
const FAILURE_THRESHOLD = 3;

/** 熔断持续时间（毫秒）：熔断期间跳过健康检查 */
const BREAKER_TIMEOUT = 60_000;

// ─── 熔断状态接口 ───────────────────────────────────────────

/**
 * 熔断状态
 */
interface BreakerState {
  /** 连续失败次数 */
  failCount: number;
  /** 熔断到期时间戳（0 表示未熔断） */
  breakerExpiresAt: number;
}

// ─── 健康检查器类 ───────────────────────────────────────────

/**
 * McpBridge 健康检查器
 *
 * 继承 BasicMethod 的通用 HealthChecker，添加 MCP 特定的健康检查逻辑。
 * 定期 ping 所有远程 MCP 服务器，更新其状态，支持自动重连。
 * 感知 Docker 状态，Docker 启动期间进入等待模式。
 *
 * ★ P2 修复：连续失败熔断保护
 * - 连续失败超过 FAILURE_THRESHOLD 次后进入熔断状态
 * - 熔断期间跳过健康检查，减少无效请求
 * - 熔断到期后自动半开检测，尝试恢复
 */
export class HealthChecker extends BaseHealthChecker {
  private aggregator: ToolAggregator;

  /** 状态变更回调（用于子类自建 checkAll 的过渡通知） */
  private statusChangeCb: ((
    serverName: string,
    oldStatus: string,
    newStatus: string
  ) => void) | null = null;

  /** 熔断状态映射：服务器名 → { 连续失败次数, 熔断到期时间 } */
  private breakerStates = new Map<string, BreakerState>();

  /**
   * @param aggregator 工具聚合器实例
   * @param checkInterval 检查间隔（毫秒，默认 30 秒）
   * @param onStatusChange 状态变更回调
   */
  constructor(
    aggregator: ToolAggregator,
    checkInterval?: number,
    onStatusChange?: (
      serverName: string,
      oldStatus: string,
      newStatus: string
    ) => void
  ) {
    // 创建健康检查目标列表
    const targets = HealthChecker.buildTargets(aggregator, onStatusChange);
    super(targets, checkInterval, onStatusChange);
    this.aggregator = aggregator;
    this.statusChangeCb = onStatusChange ?? null;
  }

  /**
   * 从 ToolAggregator 构建健康检查目标列表
   */
  private static buildTargets(
    aggregator: ToolAggregator,
    onStatusChange?: (
      serverName: string,
      oldStatus: string,
      newStatus: string
    ) => void
  ): HealthCheckTarget[] {
    const servers = aggregator.getAllServers();
    return servers.map((info) => ({
      name: info.name,
      check: async () => {
        const client = aggregator.getClient(info.name);
        if (!client) return false;
        return client.ping();
      },
      onStatusChange: (name, oldStatus, newStatus) => {
        // 状态恢复时重新聚合工具
        if (newStatus === "connected" && oldStatus !== "connected") {
          aggregator.aggregateServer(name).catch((err) => {
            logger.warn("HealthChecker", `聚合失败但 ping 成功: ${name} - ${formatError(err)}`);
          });
        }
        // 通知外部回调
        if (onStatusChange) {
          onStatusChange(name, oldStatus, newStatus);
        }
      },
      onReconnect: async (name) => {
        // 指数退避重连，避免瞬态失败导致永久 ERROR
        const maxRetries = 3;
        for (let i = 0; i < maxRetries; i++) {
          try {
            await aggregator.aggregateServer(name);
            logger.info("HealthChecker", `重连成功: ${name}`);
            return;
          } catch (err) {
            if (i < maxRetries - 1) {
              const delay = Math.pow(2, i) * 1000; // 1s, 2s, 4s
              logger.warn(
                "HealthChecker",
                `重连 ${name} 失败，${delay}ms 后重试 (${i + 1}/${maxRetries})`
              );
              await new Promise((r) => setTimeout(r, delay));
            }
          }
        }
        throw new Error(`重连 ${name} 失败（已重试 ${maxRetries} 次）`);
      },
    }));
  }

  // ─── 健康检查 ─────────────────────────────────────────────

  /**
   * 检查所有服务器的健康状态
   *
   * ★ #10 修复：循环内真正跳过熔断服务器（不再只是空循环后调用 super.checkAll）。
   * 基类 checkTarget 为 private 无法复用，此处自建熔断感知的逐服务器检查，
   * 并自行驱动状态过渡 / 重连 / 工具重聚合。
   */
  async checkAll(): Promise<void> {
    const servers = this.aggregator.getAllServers();

    for (const server of servers) {
      // 跳过 enabled=false 的服务器（统一网关安全登记，不参与健康检查/重连）
      if (server.config?.enabled === false) {
        continue;
      }
      // ★ 熔断检查：跳过处于熔断状态的服务器（真正不发起 ping）
      if (this.isBreakerOpen(server.name)) {
        logger.debug("HealthChecker", `熔断保护: 跳过 ${server.name} 的健康检查`);
        continue;
      }
      await this.checkServer(server.name);
    }
  }

  /**
   * 检查单个服务器（熔断感知，替代基类 checkTarget）
   */
  private async checkServer(serverName: string): Promise<void> {
    const info = this.aggregator.getServer(serverName);
    if (!info) return;

    const oldStatus = info.status || "";

    const client = this.aggregator.getClient(serverName);
    let alive = false;
    try {
      alive = client ? await client.ping() : false;
    } catch {
      alive = false;
    }

    if (alive) {
      this.onCheckSucceeded(serverName);
      this.aggregator.updateServerStatus(serverName, ServerStatus.CONNECTED);
    } else {
      this.onCheckFailed(serverName);
      this.aggregator.updateServerStatus(serverName, ServerStatus.ERROR, "ping 失败");
      this.reconnectServer(serverName);
    }

    const newStatus = info.status || "";
    if (oldStatus !== newStatus) {
      // 状态恢复时重新聚合工具
      if (newStatus === ServerStatus.CONNECTED && oldStatus !== ServerStatus.CONNECTED) {
        this.aggregator.aggregateServer(serverName).catch((err) => {
          logger.warn(
            "HealthChecker",
            `聚合失败但 ping 成功: ${serverName} - ${formatError(err)}`
          );
        });
      }
      if (this.statusChangeCb) {
        this.statusChangeCb(serverName, oldStatus, newStatus);
      }
    }
  }

  /**
   * 指数退避重连
   */
  private async reconnectServer(serverName: string): Promise<void> {
    const maxRetries = 3;
    for (let i = 0; i < maxRetries; i++) {
      try {
        await this.aggregator.aggregateServer(serverName);
        logger.info("HealthChecker", `重连成功: ${serverName}`);
        return;
      } catch (err) {
        logger.warn(
          "HealthChecker",
          `重连失败(${i + 1}/${maxRetries}): ${serverName} - ${formatError(err)}`
        );
        if (i < maxRetries - 1) {
          const delay = Math.pow(2, i) * 1000;
          await new Promise((resolve) => setTimeout(resolve, delay));
        }
      }
    }
    logger.warn("HealthChecker", `重连 ${serverName} 失败，等待下一轮健康检查`);
  }

  /**
   * 检查指定服务器是否处于熔断状态
   *
   * ★ P2 修复：熔断保护逻辑
   *
   * @param serverName 服务器名称
   * @returns true 表示熔断已打开，应跳过健康检查
   */
  private isBreakerOpen(serverName: string): boolean {
    const state = this.breakerStates.get(serverName);
    if (!state) return false;

    const now = Date.now();

    // 熔断到期 → 半开状态，允许一次检测
    if (state.breakerExpiresAt > 0 && now >= state.breakerExpiresAt) {
      logger.info("HealthChecker", `熔断到期: ${serverName}，允许半开检测`);
      // 重置熔断状态，允许本次检查
      state.breakerExpiresAt = 0;
      return false;
    }

    // 熔断未到期 → 跳过检查
    return state.breakerExpiresAt > 0;
  }

  /**
   * 记录健康检查失败（由基类回调触发）
   *
   * ★ P2 修复：失败计数达到阈值时打开熔断
   *
   * @param serverName 服务器名称
   */
  onCheckFailed(serverName: string): void {
    let state = this.breakerStates.get(serverName);
    if (!state) {
      state = { failCount: 0, breakerExpiresAt: 0 };
      this.breakerStates.set(serverName, state);
    }

    state.failCount++;

    if (state.failCount >= FAILURE_THRESHOLD) {
      // 打开熔断
      state.breakerExpiresAt = Date.now() + BREAKER_TIMEOUT;
      logger.warn(
        "HealthChecker",
        `熔断已打开: ${serverName}（连续失败 ${state.failCount} 次，熔断 ${BREAKER_TIMEOUT}ms）`
      );
    }
  }

  /**
   * 记录健康检查成功（由基类回调触发）
   *
   * ★ P2 修复：成功后重置熔断状态
   *
   * @param serverName 服务器名称
   */
  onCheckSucceeded(serverName: string): void {
    const state = this.breakerStates.get(serverName);
    if (state) {
      state.failCount = 0;
      state.breakerExpiresAt = 0;
    }
  }

  // ─── 状态查询 ─────────────────────────────────────────────

  /**
   * 获取所有服务器的健康状态摘要
   *
   * 覆盖基类方法，添加 MCP 特定的信息（工具数、错误信息、熔断状态）。
   */
  getStatusSummary(): string {
    const servers = this.aggregator.getAllServers();
    const lines: string[] = [];

    const statusEmoji: Record<string, string> = {
      [ServerStatus.CONNECTED]: "✅",
      [ServerStatus.CONNECTING]: "🔄",
      [ServerStatus.DISCONNECTED]: "❌",
      [ServerStatus.ERROR]: "⚠️",
    };

    lines.push("# McpBridge 健康状态\n");

    if (this.getMode() === HealthCheckMode.PAUSED) {
      lines.push("> ⏸️ 健康检查已暂停\n");
    }

    lines.push(`| 服务器 | 状态 | 工具数 | 熔断 | 错误信息 |`);
    lines.push(`|--------|------|--------|------|----------|`);

    for (const server of servers) {
      const statusStr = server.status || "";
      const emoji = statusEmoji[statusStr] || "❓";
      const error = server.lastError ? `\`${server.lastError}\`` : "-";

      // 熔断状态
      const breakerState = this.breakerStates.get(server.name);
      let breakerStr = "-";
      if (breakerState && breakerState.breakerExpiresAt > 0) {
        const remaining = Math.ceil((breakerState.breakerExpiresAt - Date.now()) / 1000);
        breakerStr = `🔴 ${remaining}s`;
      } else if (breakerState && breakerState.failCount > 0) {
        breakerStr = `⚠️ ${breakerState.failCount}/${FAILURE_THRESHOLD}`;
      }

      lines.push(
        `| ${server.name} | ${emoji} ${statusStr} | ${server.tools?.length || 0} | ${breakerStr} | ${error} |`
      );
    }

    lines.push("");
    lines.push(
      `> 共 ${servers.length} 个服务器，` +
        `${this.aggregator.getConnectedCount()} 个已连接，` +
        `${this.aggregator.getToolCount()} 个工具`
    );

    return lines.join("\n");
  }
}
