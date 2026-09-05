/**
 * McpBridge — Docker 管理器
 *
 * 整合 Docker Desktop 自动启动、容器生命周期管理、事件监听。
 * 提供统一的 Docker 状态查询接口。
 *
 * 核心流程：
 * 1. 检测 Docker Desktop → 未安装则报错
 * 2. 检测 Docker 守护进程 → 未运行则自动启动 Docker Desktop
 * 3. 检测容器 → 未运行则 docker compose up -d
 * 4. 等待容器端口就绪
 * 5. 监听容器事件（事件驱动替代轮询）
 */

import { execSync, exec } from "child_process";
import * as fs from "fs";
import * as path from "path";
import { logger } from "../../utils/logger.js";
import { BridgeError, ErrorCode, formatError } from "../../utils/errors.js";
import { DockerConfig, DockerState, RemoteMcpServerConfig } from "../../core/types.js";
import { DockerDesktopManager } from "./docker-desktop.js";
import { DockerEventWatcher, DockerEvent } from "./docker-events.js";
import {
  DOCKER_CONTAINER_START_TIMEOUT,
  DOCKER_PORT_WAIT_TIMEOUT,
  DOCKER_PORT_POLL_INTERVAL,
} from "../../utils/constants.js";
import { getDockerComposePath, getDockerComposeDir } from "../../utils/paths.js";

// ─── Docker 管理器 ──────────────────────────────────────────

export class DockerManager {
  private config: DockerConfig;
  private desktopManager: DockerDesktopManager;
  private eventWatcher: DockerEventWatcher | null = null;
  private _containerRunning = false;
  private _daemonReady = false;
  private _error: string | null = null;
  private onContainerStarted?: () => void;
  /** 所有服务器端口列表（从配置中提取） */
  private serverPorts: number[] = [];

  /**
   * @param config Docker 配置
   * @param onContainerStarted 容器启动后的回调（用于触发重新聚合）
   */
  constructor(config: DockerConfig, onContainerStarted?: () => void) {
    this.config = config;
    this.desktopManager = new DockerDesktopManager(config.desktopPath);
    this.onContainerStarted = onContainerStarted;
  }

  /**
   * 设置服务器端口列表（从 MCP 服务器配置中提取）
   *
   * 在 Docker 容器启动后，需要等待所有服务器端口就绪。
   * 此方法由外部调用，传入所有已配置的服务器端口。
   *
   * @param serverConfigs 远程 MCP 服务器配置列表
   */
  setServerConfigs(serverConfigs: RemoteMcpServerConfig[]): void {
    this.serverPorts = serverConfigs
      .map(s => {
        const match = s.url.match(/:(\d+)\//);
        return match ? parseInt(match[1], 10) : null;
      })
      .filter((p): p is number => p !== null);
  }

  // ─── 核心流程 ─────────────────────────────────────────────

  /**
   * 确保 Docker 环境就绪
   *
   * 完整流程：
   * 1. 检测 Docker Desktop 安装
   * 2. 自动启动 Docker Desktop（如需）
   * 3. 等待 Docker 守护进程就绪
   * 4. 启动容器
   * 5. 等待端口就绪
   *
   * @returns 是否成功
   */
  async ensureEnvironment(): Promise<boolean> {
    // 1. 检测 Docker Desktop 安装
    const installPath = this.desktopManager.detectInstallation();
    if (!installPath) {
      this._error = "Docker Desktop 未安装";
      logger.error("DockerManager", this._error);
      return false;
    }
    logger.info("DockerManager", `Docker Desktop 已安装: ${installPath}`);

    // 2. 检测守护进程
    if (await this.desktopManager.isDaemonReady()) {
      logger.info("DockerManager", "Docker 守护进程已在运行");
      this._daemonReady = true;
    } else if (this.config.autoStartDesktop) {
      // 3. 自动启动 Docker Desktop
      logger.info("DockerManager", "Docker 守护进程未运行，正在启动 Docker Desktop...");
      const launched = await this.desktopManager.launch();
      if (!launched) {
        this._error = "Docker Desktop 启动失败";
        return false;
      }
      this._daemonReady = true;
    } else {
      this._error = "Docker 守护进程未运行（autoStartDesktop 已禁用）";
      logger.warn("DockerManager", this._error);
      return false;
    }

    // 4. 启动容器
    if (this.config.autoStart) {
      const containerStarted = await this.ensureContainerRunning();
      if (containerStarted) {
        this._containerRunning = true;
        // 启动事件监听
        this.startEventWatcher();
        return true;
      }
      return false;
    }

    return this._daemonReady;
  }

  /**
   * 确保 Docker 容器在运行
   *
   * 检测容器状态，如果未运行则执行 docker compose up -d。
   */
  async ensureContainerRunning(): Promise<boolean> {
    try {
      // 检查容器是否已在运行
      const running = await this.isContainerRunning();
      if (running) {
        logger.info("DockerManager", `容器 ${this.config.containerName || "unknown"} 已在运行`);
        this._containerRunning = true;
        return true;
      }

      logger.info("DockerManager", `容器 ${this.config.containerName || "unknown"} 未运行，正在启动...`);

      // 解析 docker-compose 路径
      const composePath = this.resolveComposePath();
      const composeDir = path.dirname(composePath);

      // 执行 docker compose up -d
      await this.runDockerComposeUp(composeDir);

      // 等待所有服务器端口就绪
      // 优先使用从配置中提取的服务器端口列表，回退到 healthCheckPort
      const portsToWait = this.serverPorts.length > 0
        ? this.serverPorts
        : [this.config.healthCheckPort || 3100];

      logger.info("DockerManager", `等待端口就绪: ${portsToWait.join(", ")}`);
      const portReady = await this.waitForPorts(
        portsToWait,
        DOCKER_PORT_WAIT_TIMEOUT
      );

      if (portReady) {
        logger.info("DockerManager", `容器已就绪，所有端口可访问 ✅`);
        this._containerRunning = true;
        return true;
      } else {
        this._error = `容器端口 ${portsToWait.join(", ")} 未在超时内就绪`;
        logger.error("DockerManager", this._error);
        return false;
      }
    } catch (err) {
      this._error = formatError(err, "容器启动失败");
      logger.error("DockerManager", this._error);
      return false;
    }
  }

  // ─── 事件监听 ─────────────────────────────────────────────

  /**
   * 启动 Docker 事件监听器
   *
   * 当容器启动/停止时，自动触发回调，替代轮询。
   */
  private startEventWatcher(): void {
    if (this.eventWatcher?.isRunning()) return;

    const containerName = this.config.containerName || "";
    this.eventWatcher = new DockerEventWatcher(
      containerName,
      (event: DockerEvent) => this.handleDockerEvent(event)
    );
    this.eventWatcher.start();
  }

  /**
   * 处理 Docker 事件
   */
  private handleDockerEvent(event: DockerEvent): void {
    switch (event.type) {
      case "start":
        logger.info("DockerManager", `容器已启动: ${event.containerName}`);
        this._containerRunning = true;
        this.onContainerStarted?.();
        break;

      case "die":
        logger.warn("DockerManager", `容器已停止: ${event.containerName}`);
        this._containerRunning = false;
        break;

      case "destroy":
        logger.warn("DockerManager", `容器已销毁: ${event.containerName}`);
        this._containerRunning = false;
        break;
    }
  }

  // ─── 容器操作 ─────────────────────────────────────────────

  /**
   * 检查容器是否在运行
   */
  async isContainerRunning(): Promise<boolean> {
    try {
      const containerName = this.config.containerName || "";
      const output = execSync(
        `docker ps --filter "name=${containerName}" --format "{{.Names}}"`,
        { encoding: "utf8", timeout: 5000 }
      ).trim();
      return output === containerName;
    } catch {
      return false;
    }
  }

  /**
   * 执行 docker compose up -d
   */
  private runDockerComposeUp(composeDir: string): Promise<void> {
    return new Promise((resolve, reject) => {
      exec(
        `docker compose up -d`,
        { cwd: composeDir, timeout: DOCKER_CONTAINER_START_TIMEOUT },
        (err, stdout, stderr) => {
          if (err) {
            reject(new BridgeError(ErrorCode.DOCKER_COMPOSE_FAILED, stderr || err.message));
            return;
          }
          logger.debug("DockerManager", `docker compose up 输出: ${stdout.trim()}`);
          resolve();
        }
      );
    });
  }

  /**
   * 等待端口就绪（单个端口，委托给 waitForPorts）
   *
   * ★ 修复 Bug 5：不仅检查 HTTP 状态码，还验证 JSON-RPC 响应有效性。
   * 同时支持检查多个端口（所有服务器端口都就绪才算就绪）。
   */
  private async waitForPort(port: number, timeout: number): Promise<boolean> {
    return this.waitForPorts([port], timeout);
  }

  /**
   * 等待多个端口就绪
   *
   * 检查所有指定端口是否都能返回有效的 JSON-RPC 响应。
   * 所有端口都就绪才返回 true。
   *
   * @param ports 端口列表
   * @param timeout 总超时时间（毫秒）
   */
  private async waitForPorts(ports: number[], timeout: number): Promise<boolean> {
    if (ports.length === 0) return true;

    const start = Date.now();
    const ready = new Set<number>();

    while (Date.now() - start < timeout) {
      for (const port of ports) {
        if (ready.has(port)) continue;

        try {
          const response = await fetch(`http://localhost:${port}/mcp`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              jsonrpc: "2.0",
              id: "ping",
              method: "tools/list",
            }),
            signal: AbortSignal.timeout(2000),
          });

          if (response.ok) {
            // ★ 修复 Bug 5：验证响应体是有效的 JSON-RPC
            try {
              const text = await response.text();
              if (text) {
                const parsed = JSON.parse(text);
                if (
                  parsed &&
                  typeof parsed === "object" &&
                  ("result" in parsed || "error" in parsed)
                ) {
                  ready.add(port);
                  continue;
                }
              }
            } catch {
              // JSON 解析失败，端口可能还未完全就绪
            }
          }
        } catch {
          // 端口未就绪，继续等待
        }
      }

      // 所有端口都已就绪
      if (ready.size >= ports.length) return true;

      await new Promise((r) => setTimeout(r, DOCKER_PORT_POLL_INTERVAL));
    }

    return false;
  }

  // ─── 路径解析 ─────────────────────────────────────────────

  /**
   * 解析 docker-compose.yml 的绝对路径
   *
   * 使用 paths.ts 集中管理的路径配置查找 docker-compose 文件。
   */
  private resolveComposePath(): string {
    const composePath = this.config.composePath || "";

    // 如果是绝对路径，直接使用
    if (path.isAbsolute(composePath)) {
      if (fs.existsSync(composePath)) return composePath;
      throw new BridgeError(
        ErrorCode.DOCKER_COMPOSE_FAILED,
        `docker-compose 文件未找到: ${composePath}`
      );
    }

    // 1. 优先使用 paths.ts 集中管理的路径
    const composeFromPaths = getDockerComposePath();
    if (fs.existsSync(composeFromPaths)) {
      logger.info("DockerManager", `docker-compose 文件已通过 paths 配置找到: ${composeFromPaths}`);
      return composeFromPaths;
    }

    // 2. 最后尝试从 Docker Compose 目录查找
    const composeDir = getDockerComposeDir();
    const composeFileInDir = path.resolve(composeDir, path.basename(composePath));
    if (fs.existsSync(composeFileInDir)) {
      logger.info("DockerManager", `docker-compose 文件已通过 Docker Compose 目录找到: ${composeFileInDir}`);
      return composeFileInDir;
    }

    throw new BridgeError(
      ErrorCode.DOCKER_COMPOSE_FAILED,
      `docker-compose 文件未找到: ${composePath}（已使用 paths 配置搜索）`
    );
  }

  // ─── 状态查询 ─────────────────────────────────────────────

  /**
   * 获取 Docker 整体状态
   */
  getState(): DockerState {
    return {
      installed: this.desktopManager.detectInstallation() !== null,
      daemonReady: this._daemonReady,
      containerRunning: this._containerRunning,
      error: this._error || undefined,
    };
  }

  // ─── 清理 ─────────────────────────────────────────────────

  /**
   * 停止事件监听器
   */
  stop(): void {
    this.eventWatcher?.stop();
    logger.info("DockerManager", "已停止");
  }
}