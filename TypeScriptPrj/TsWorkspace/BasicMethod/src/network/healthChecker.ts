/**
 * BasicMethod — 健康检查器
 *
 * 从 MCPBridge 的 HealthChecker 中提取的通用健康检查逻辑。
 * 定期检查多个远程端点的健康状态，支持状态变更通知和自动重连。
 *
 * 设计原则：
 * - 零外部依赖（仅依赖 basic-method 自身的 error/constants/logger）
 * - 不绑定任何特定业务场景
 * - 与 MCPBridge 的 HealthChecker 接口兼容
 * - 支持暂停/恢复、状态变更回调、自动重连
 *
 * 使用方式：
 * ```typescript
 * import { HealthChecker } from "basic-method/network/healthChecker";
 *
 * const checker = new HealthChecker([
 *   {
 *     name: "my-server",
 *     check: async () => {
 *       const res = await fetch("http://localhost:3100/health");
 *       return res.ok;
 *     },
 *     onReconnect: async (name) => {
 *       console.log(`重连 ${name}`);
 *     },
 *   },
 * ]);
 *
 * checker.start();
 * // ... 一段时间后
 * checker.stop();
 * ```
 */

import { logger } from "../logger.js";
import { formatError } from "../error.js";
import { DEFAULT_HEALTH_CHECK_INTERVAL, DEFAULT_RECONNECT_DELAY, DEFAULT_PROCESS_START_DELAY } from "../constants.js";

// ═══════════════════════════════════════════════════════════════
// 类型定义
// ═══════════════════════════════════════════════════════════════

/**
 * 健康检查目标
 *
 * 定义需要定期检查的远程端点。
 */
export interface HealthCheckTarget {
  /** 目标名称（用于日志和错误信息） */
  name: string;
  /**
   * 健康检查函数
   *
   * 返回 true 表示健康，false 表示不健康。
   * 抛出异常视为不健康。
   */
  check: () => Promise<boolean>;
  /**
   * 状态变更回调（可选）
   *
   * @param name 目标名称
   * @param oldStatus 旧状态
   * @param newStatus 新状态
   */
  onStatusChange?: (name: string, oldStatus: string, newStatus: string) => void;
  /**
   * 重连回调（可选）
   *
   * 当目标不健康时，自动调用此函数尝试重连。
   *
   * @param name 目标名称
   */
  onReconnect?: (name: string) => Promise<void>;
}

// ═══════════════════════════════════════════════════════════════
// 健康检查器运行模式
// ═══════════════════════════════════════════════════════════════

/**
 * 健康检查器运行模式
 */
export enum HealthCheckMode {
  /** 正常运行 */
  NORMAL = "normal",
  /** 已暂停 */
  PAUSED = "paused",
}

// ═══════════════════════════════════════════════════════════════
// 健康检查器类
// ═══════════════════════════════════════════════════════════════

/**
 * 健康检查器
 *
 * 定期检查所有注册的远程端点，更新其状态。
 * 支持自动重连、暂停/恢复、状态变更通知。
 *
 * @example
 * ```typescript
 * // 基本使用
 * const checker = new HealthChecker([
 *   {
 *     name: "server-1",
 *     check: async () => {
 *       const res = await fetch("http://localhost:3100/mcp");
 *       return res.ok;
 *     },
 *   },
 * ]);
 * checker.start();
 *
 * // 带状态变更回调
 * const checker = new HealthChecker(targets, 30000, (name, old, new) => {
 *   console.log(`${name}: ${old} → ${new}`);
 * });
 * ```
 */
export class HealthChecker {
  private targets: HealthCheckTarget[];
  private intervalId: ReturnType<typeof setInterval> | null = null;
  private checkInterval: number;
  private onStatusChange?: (
    name: string,
    oldStatus: string,
    newStatus: string
  ) => void;
  private mode: HealthCheckMode = HealthCheckMode.NORMAL;
  /** ★ 修复：优雅启动期（毫秒），在此期间不执行健康检查 */
  private gracePeriod: number;
  /** ★ 修复：启动时间戳，用于计算优雅启动期 */
  private startTime: number = 0;

  /**
   * @param targets 健康检查目标列表
   * @param checkInterval 检查间隔（毫秒，默认 30 秒）
   * @param onStatusChange 状态变更回调（可选）
   * @param gracePeriod 优雅启动期（毫秒，默认 5 秒），在此期间不执行健康检查
   */
  constructor(
    targets: HealthCheckTarget[],
    checkInterval: number = DEFAULT_HEALTH_CHECK_INTERVAL,
    onStatusChange?: (
      name: string,
      oldStatus: string,
      newStatus: string
    ) => void,
    gracePeriod: number = DEFAULT_PROCESS_START_DELAY + 3000  // 默认 5 秒
  ) {
    this.targets = targets;
    this.checkInterval = checkInterval;
    this.onStatusChange = onStatusChange;
    this.gracePeriod = gracePeriod;
  }

  // ─── 运行模式管理 ─────────────────────────────────────────

  /**
   * 暂停健康检查
   *
   * 暂停后，定期检查停止，但已启动的检查不受影响。
   */
  pause(): void {
    this.mode = HealthCheckMode.PAUSED;
    logger.info("HealthChecker", "健康检查已暂停");
  }

  /**
   * 恢复健康检查
   */
  resume(): void {
    this.mode = HealthCheckMode.NORMAL;
    logger.info("HealthChecker", "健康检查已恢复");
  }

  /**
   * 获取当前运行模式
   */
  getMode(): HealthCheckMode {
    return this.mode;
  }

  // ─── 启动/停止 ────────────────────────────────────────────

  /**
   * 启动定期健康检查
   *
   * ★ 修复：不再立即执行检查，而是等待优雅启动期（gracePeriod）后执行首次检查。
   * 这给服务器留出足够的启动时间，避免在服务器未就绪时误判为不健康。
   *
   * 重复调用不会创建多个定时器。
   */
  start(): void {
    if (this.intervalId) return;

    this.startTime = Date.now();

    // 定期执行
    this.intervalId = setInterval(() => {
      this.checkAll();
    }, this.checkInterval);

    // ★ 修复：在优雅启动期后执行首次检查
    // 让服务器有足够时间完成启动，避免瞬态失败
    setTimeout(() => {
      if (this.mode === HealthCheckMode.NORMAL) {
        logger.info("HealthChecker", `优雅启动期结束，执行首次健康检查`);
        this.checkAll();
      }
    }, this.gracePeriod);

    logger.info(
      "HealthChecker",
      `健康检查已启动 (间隔 ${this.checkInterval / 1000} 秒，优雅启动期 ${this.gracePeriod / 1000} 秒)`
    );
  }

  /**
   * 停止定期健康检查
   */
  stop(): void {
    if (this.intervalId) {
      clearInterval(this.intervalId);
      this.intervalId = null;
    }
    logger.info("HealthChecker", "健康检查已停止");
  }

  // ─── 健康检查 ─────────────────────────────────────────────

  /**
   * 检查所有目标的健康状态
   */
  async checkAll(): Promise<void> {
    // 如果已暂停，跳过检查
    if (this.mode !== HealthCheckMode.NORMAL) {
      logger.debug("HealthChecker", "跳过健康检查（已暂停）");
      return;
    }

    const promises = this.targets.map((target) => this.checkTarget(target));
    await Promise.allSettled(promises);
  }

  /**
   * 检查单个目标的健康状态
   *
   * @param target 健康检查目标
   */
  private async checkTarget(target: HealthCheckTarget): Promise<void> {
    const oldStatus = this.getTargetStatus(target.name);

    try {
      const alive = await target.check();

      if (alive) {
        this.setTargetStatus(target.name, "connected");
      } else {
        this.setTargetStatus(target.name, "disconnected");
        this.attemptReconnect(target);
      }
    } catch (err) {
      this.setTargetStatus(target.name, "error");
      logger.error("HealthChecker", `检查失败: ${target.name} - ${formatError(err)}`);
      this.attemptReconnect(target);
    }

    // 状态变更通知
    const newStatus = this.getTargetStatus(target.name);
    if (oldStatus !== newStatus && this.onStatusChange) {
      this.onStatusChange(target.name, oldStatus, newStatus);
    }
    if (oldStatus !== newStatus && target.onStatusChange) {
      target.onStatusChange(target.name, oldStatus, newStatus);
    }
  }

  // ─── 自动重连 ─────────────────────────────────────────────

  /**
   * 尝试重连目标
   *
   * @param target 健康检查目标
   */
  private async attemptReconnect(target: HealthCheckTarget): Promise<void> {
    if (!target.onReconnect) return;

    this.setTargetStatus(target.name, "connecting");

    // 等待重连延迟
    await new Promise((resolve) =>
      setTimeout(resolve, DEFAULT_RECONNECT_DELAY)
    );

    try {
      await target.onReconnect(target.name);
      logger.info("HealthChecker", `✅ 重连成功: ${target.name}`);
    } catch (err) {
      logger.error("HealthChecker", `❌ 重连失败: ${target.name} - ${formatError(err)}`);
    }
  }

  // ─── 状态管理 ─────────────────────────────────────────────

  /** 目标状态映射：目标名 → 状态 */
  private targetStatuses = new Map<string, string>();

  /**
   * 获取目标状态
   *
   * @param name 目标名称
   * @returns 状态字符串（默认 "unknown"）
   */
  private getTargetStatus(name: string): string {
    return this.targetStatuses.get(name) ?? "unknown";
  }

  /**
   * 设置目标状态
   *
   * @param name 目标名称
   * @param status 状态字符串
   */
  private setTargetStatus(name: string, status: string): void {
    this.targetStatuses.set(name, status);
  }

  // ─── 状态查询 ─────────────────────────────────────────────

  /**
   * 获取所有目标的健康状态摘要
   *
   * @returns Markdown 格式的状态摘要
   */
  getStatusSummary(): string {
    const lines: string[] = [];

    const statusEmoji: Record<string, string> = {
      connected: "✅",
      connecting: "🔄",
      disconnected: "❌",
      error: "⚠️",
    };

    lines.push("# 健康状态\n");

    if (this.mode === HealthCheckMode.PAUSED) {
      lines.push("> ⏸️ 健康检查已暂停\n");
    }

    lines.push(`| 目标 | 状态 |`);
    lines.push(`|------|------|`);

    for (const target of this.targets) {
      const status = this.getTargetStatus(target.name);
      const emoji = statusEmoji[status] || "❓";
      lines.push(`| ${target.name} | ${emoji} ${status} |`);
    }

    lines.push("");
    lines.push(
      `> 共 ${this.targets.length} 个目标`
    );

    return lines.join("\n");
  }
}
