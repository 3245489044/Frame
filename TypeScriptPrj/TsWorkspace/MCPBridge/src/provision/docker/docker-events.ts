/**
 * McpBridge — Docker 事件监听器
 *
 * 通过 `docker events` 监听容器生命周期事件，
 * 替代轮询方式检测容器状态变化。
 *
 * 核心能力：
 * 1. 监听容器 start/die 事件
 * 2. 事件驱动触发回调（替代轮询）
 * 3. 自动重连事件流
 */

import { spawn, ChildProcess } from "child_process";
import { logger } from "../../utils/logger.js";

// ─── 事件类型 ───────────────────────────────────────────────

export interface DockerEvent {
  /** 事件类型（start, die, destroy 等） */
  type: string;
  /** 容器名称 */
  containerName: string;
  /** 原始事件行 */
  raw: string;
}

export type DockerEventHandler = (event: DockerEvent) => void;

// ─── Docker 事件监听器 ──────────────────────────────────────

export class DockerEventWatcher {
  private process: ChildProcess | null = null;
  private containerName: string;
  private onEvent: DockerEventHandler;
  private running = false;
  private retryDelay: number;
  private maxRetries: number;
  private retryCount = 0;

  /**
   * @param containerName 要监听的容器名称
   * @param onEvent 事件回调
   * @param retryDelay 重连延迟（毫秒，默认 5 秒）
   * @param maxRetries 最大重试次数（默认 5）
   */
  constructor(
    containerName: string,
    onEvent: DockerEventHandler,
    retryDelay: number = 5000,
    maxRetries: number = 5
  ) {
    this.containerName = containerName;
    this.onEvent = onEvent;
    this.retryDelay = retryDelay;
    this.maxRetries = maxRetries;
  }

  // ─── 启动/停止 ────────────────────────────────────────────

  /**
   * 开始监听 Docker 事件
   */
  start(): void {
    if (this.running) return;
    this.running = true;
    this.retryCount = 0;
    this.spawnWatcher();
    logger.info("DockerEvents", `开始监听容器事件: ${this.containerName}`);
  }

  /**
   * 停止监听
   */
  stop(): void {
    this.running = false;
    if (this.process) {
      this.process.kill();
      this.process = null;
    }
    logger.info("DockerEvents", "停止监听容器事件");
  }

  /**
   * 是否正在运行
   */
  isRunning(): boolean {
    return this.running;
  }

  // ─── 内部实现 ─────────────────────────────────────────────

  /**
   * 启动 docker events 子进程
   */
  private spawnWatcher(): void {
    if (!this.running) return;

    try {
      // docker events --filter 'container=xxx' --format '{{json .}}'
      this.process = spawn("docker", [
        "events",
        "--filter",
        `container=${this.containerName}`,
        "--format",
        "{{json .}}",
      ], {
        stdio: ["ignore", "pipe", "pipe"],
      });

      this.process.stdout?.on("data", (data: Buffer) => {
        this.handleEventData(data.toString());
      });

      this.process.stderr?.on("data", (data: Buffer) => {
        logger.debug("DockerEvents", `stderr: ${data.toString().trim()}`);
      });

      this.process.on("exit", (code, signal) => {
        logger.debug("DockerEvents", `进程退出 code=${code} signal=${signal}`);
        this.process = null;
        this.scheduleRetry();
      });

      this.process.on("error", (err) => {
        logger.error("DockerEvents", `进程错误: ${err.message}`);
        this.process = null;
        this.scheduleRetry();
      });
    } catch (err) {
      logger.error("DockerEvents", `启动 docker events 失败:`, err);
      this.scheduleRetry();
    }
  }

  /**
   * 处理事件数据
   */
  private handleEventData(data: string): void {
    const lines = data.split("\n").filter((l) => l.trim());

    for (const line of lines) {
      try {
        const parsed = JSON.parse(line);
        // ★ 修复：docker events --format '{{json .}}' 输出的 JSON 中，
        //   事件类型字段是 status（如 "start"、"die"），而不是 Type
        //   Type 字段的值是 "container"（表示事件来源类型）
        const event: DockerEvent = {
          type: parsed.status ?? parsed.Action ?? "unknown",
          containerName: parsed.Actor?.Attributes?.name ?? this.containerName,
          raw: line,
        };

        logger.debug("DockerEvents", `事件: ${event.type} - ${event.containerName}`);
        this.onEvent(event);
      } catch {
        // 非 JSON 行，忽略
        logger.debug("DockerEvents", `非 JSON 事件行: ${line}`);
      }
    }
  }

  /**
   * 调度重连
   */
  private scheduleRetry(): void {
    if (!this.running) return;
    if (this.retryCount >= this.maxRetries) {
      logger.warn("DockerEvents", `已达最大重试次数 (${this.maxRetries})，停止监听`);
      this.running = false;
      return;
    }

    this.retryCount++;
    logger.info(
      "DockerEvents",
      `将在 ${this.retryDelay / 1000} 秒后重连 (${this.retryCount}/${this.maxRetries})...`
    );

    setTimeout(() => {
      this.spawnWatcher();
    }, this.retryDelay);
  }
}