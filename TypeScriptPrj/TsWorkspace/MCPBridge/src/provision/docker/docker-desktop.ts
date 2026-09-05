/**
 * McpBridge — Docker Desktop 进程管理器
 *
 * 核心能力：
 * 1. 检测 Docker Desktop 是否安装
 * 2. 自动启动 Docker Desktop 进程
 * 3. 等待 Docker 守护进程就绪
 * 4. 跨平台支持（Windows / macOS / Linux）
 *
 * 这是 MCPBridge 与 Docker 交互的关键优化：
 * 当 Docker 未运行时，不再只是报错，而是主动启动 Docker Desktop。
 *
 * 路径配置：
 * - Docker Desktop 安装路径从 mcp-bridge.config.json 的 docker.desktopPath 读取
 * - 超时等常量从 constants.ts 导入
 */

import { execSync, spawn, ChildProcess } from "child_process";
import * as fs from "fs";
import * as os from "os";
import { logger } from "../../utils/logger.js";
import {
  DOCKER_DAEMON_WAIT_TIMEOUT,
  DOCKER_DAEMON_POLL_INTERVAL,
} from "../../utils/constants.js";

// ─── 平台类型 ───────────────────────────────────────────────

type Platform = "windows" | "macos" | "linux";

function detectPlatform(): Platform {
  const p = os.platform();
  if (p === "win32") return "windows";
  if (p === "darwin") return "macos";
  return "linux";
}

// ─── 平台默认安装路径 ───────────────────────────────────────

/**
 * 获取当前平台的 Docker Desktop 默认安装路径
 *
 * 当配置文件中未指定 desktopPath 时使用此默认值。
 * 这些是各平台的标准安装路径，不属于项目路径硬编码。
 */
function getPlatformDefaultPath(): string {
  const platform = detectPlatform();
  switch (platform) {
    case "windows":
      return "C:\\Program Files\\Docker\\Docker\\Docker Desktop.exe";
    case "macos":
      return "/Applications/Docker.app";
    case "linux":
      return "docker"; // Linux 上直接使用 docker 命令
  }
}

// ─── Docker Desktop 管理器 ──────────────────────────────────

export class DockerDesktopManager {
  private process: ChildProcess | null = null;
  private daemonReady = false;
  private customPath: string;

  constructor(customPath?: string) {
    this.customPath = customPath ?? "";
  }

  // ─── 检测安装 ─────────────────────────────────────────────

  /**
   * 检测 Docker Desktop 是否已安装
   *
   * 检测顺序：
   * 1. 优先使用自定义路径（从配置文件传入）
   * 2. 检查平台默认安装路径
   * 3. 检查用户目录等替代路径（仅 Windows）
   *
   * @returns 安装路径，未安装返回 null
   */
  detectInstallation(): string | null {
    // 优先使用自定义路径
    if (this.customPath && fs.existsSync(this.customPath)) {
      logger.debug("DockerDesktop", `使用自定义路径: ${this.customPath}`);
      return this.customPath;
    }

    const platform = detectPlatform();
    const defaultPath = getPlatformDefaultPath();

    switch (platform) {
      case "windows": {
        // 检查默认路径
        if (fs.existsSync(defaultPath)) {
          return defaultPath;
        }
        // 检查用户目录（替代路径）
        const userPath = `${process.env.LOCALAPPDATA}\\Docker\\Docker Desktop.exe`;
        if (fs.existsSync(userPath)) return userPath;
        return null;
      }

      case "macos": {
        if (fs.existsSync(defaultPath)) {
          return defaultPath;
        }
        return null;
      }

      case "linux": {
        // Linux 上检查 docker 命令是否可用
        try {
          execSync("which docker", { stdio: "ignore" });
          return defaultPath; // 直接使用 docker 命令
        } catch {
          return null;
        }
      }
    }
  }

  // ─── 启动 Docker Desktop ──────────────────────────────────

  /**
   * 启动 Docker Desktop 并等待守护进程就绪
   *
   * @returns 是否成功启动
   */
  async launch(): Promise<boolean> {
    // 先检查是否已经运行
    if (await this.isDaemonReady()) {
      logger.info("DockerDesktop", "Docker 守护进程已在运行");
      this.daemonReady = true;
      return true;
    }

    const installPath = this.detectInstallation();
    if (!installPath) {
      logger.error("DockerDesktop", "Docker Desktop 未安装");
      return false;
    }

    logger.info("DockerDesktop", "正在启动 Docker Desktop...");

    try {
      await this.launchProcess(installPath);
      logger.info("DockerDesktop", "Docker Desktop 进程已启动，等待守护进程就绪...");

      // 等待守护进程就绪
      const ready = await this.waitForDaemon(DOCKER_DAEMON_WAIT_TIMEOUT);
      if (ready) {
        logger.info("DockerDesktop", "Docker 守护进程已就绪 ✅");
        this.daemonReady = true;
        return true;
      } else {
        logger.error("DockerDesktop", `Docker 守护进程未在 ${DOCKER_DAEMON_WAIT_TIMEOUT / 1000} 秒内就绪`);
        return false;
      }
    } catch (err) {
      logger.error("DockerDesktop", `启动 Docker Desktop 失败:`, err);
      return false;
    }
  }

  /**
   * 启动 Docker Desktop 进程
   */
  private launchProcess(installPath: string): Promise<void> {
    return new Promise((resolve, reject) => {
      const platform = detectPlatform();

      try {
        switch (platform) {
          case "windows": {
            // Windows: 使用 start 命令启动（不阻塞）
            this.process = spawn("cmd", ["/c", "start", "", installPath], {
              detached: true,
              stdio: "ignore",
            });
            this.process.unref();
            break;
          }

          case "macos": {
            // macOS: 使用 open 命令
            this.process = spawn("open", ["-a", installPath], {
              detached: true,
              stdio: "ignore",
            });
            this.process.unref();
            break;
          }

          case "linux": {
            // Linux: 尝试 systemctl 或 service
            try {
              execSync("systemctl --version", { stdio: "ignore" });
              this.process = spawn("systemctl", ["start", "docker"], {
                detached: true,
                stdio: "ignore",
              });
            } catch {
              this.process = spawn("service", ["docker", "start"], {
                detached: true,
                stdio: "ignore",
              });
            }
            this.process.unref();
            break;
          }
        }

        // 给进程一点时间启动
        setTimeout(() => resolve(), 2000);
      } catch (err) {
        reject(err);
      }
    });
  }

  // ─── 等待守护进程就绪 ─────────────────────────────────────

  /**
   * 等待 Docker 守护进程就绪
   *
   * 通过轮询 `docker info` 命令检测守护进程是否可用。
   *
   * @param timeout 超时时间（毫秒）
   */
  async waitForDaemon(timeout: number = DOCKER_DAEMON_WAIT_TIMEOUT): Promise<boolean> {
    const start = Date.now();

    while (Date.now() - start < timeout) {
      if (await this.isDaemonReady()) {
        return true;
      }
      await this.sleep(DOCKER_DAEMON_POLL_INTERVAL);
    }

    return false;
  }

  /**
   * 检查 Docker 守护进程是否就绪
   */
  async isDaemonReady(): Promise<boolean> {
    try {
      execSync("docker info --format '{{.ServerVersion}}'", {
        stdio: "pipe",
        timeout: 5000,
      });
      return true;
    } catch {
      return false;
    }
  }

  // ─── 工具方法 ─────────────────────────────────────────────

  private sleep(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }
}