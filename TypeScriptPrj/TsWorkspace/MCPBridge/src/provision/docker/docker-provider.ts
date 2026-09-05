/**
 * McpBridge — Docker 环境供给 Provider
 *
 * 实现核心抽象的 EnvironmentProvider，将 Docker 折叠为可插拔供给器。
 * 组合 DockerManager（含 Desktop 管理 + 事件监听），暴露就绪/生命周期/状态。
 * 编排层对 Docker 零感知，仅经由 EnvironmentProvider 接缝挂接。
 *
 * 就绪后：容器启动事件 → onReady 回调 → 触发重聚合。
 */

import { DockerConfig, RemoteMcpServerConfig } from "../../core/types.js";
import { EnvironmentProvider } from "../../core/interfaces/environment-provider.js";
import { DockerManager } from "./docker-manager.js";
import { logger } from "../../utils/logger.js";

/**
 * Docker 环境供给器
 *
 * 组合 DockerManager，向编排暴露统一的环境供给接缝。
 * 三个阶段回调可被注册，用于容器就绪后触发工具重新聚合。
 */
export class DockerProvider implements EnvironmentProvider {
  readonly kind = "docker";
  private dockerManager?: DockerManager;
  private onReadyCallback: () => void = () => {};
  private readonly config: DockerConfig;
  private readonly servers: RemoteMcpServerConfig[];

  /**
   * @param config Docker 配置
   * @param servers 远程 MCP 服务器配置（用于提取等待就绪的端口）
   */
  constructor(config: DockerConfig, servers: RemoteMcpServerConfig[]) {
    this.config = config;
    this.servers = servers;
  }

  /** 自报阶段名，编排按此动态插入流水线阶段 */
  stage(): string {
    return "prepare-docker";
  }

  /**
   * 确保 Docker 环境就绪（Docker Desktop → 守护进程 → 容器 → 端口）。
   */
  async ensureReady(): Promise<boolean> {
    this.ensureManager();
    const ready = await this.dockerManager!.ensureEnvironment();
    if (!ready) {
      logger.warn("DockerProvider", "Docker 环境就绪未完成，部分功能可能不可用");
    }
    return ready;
  }

  /** 当前状态（并入健康汇总，编排不解析其形状） */
  state(): Record<string, unknown> {
    const st = this.dockerManager?.getState();
    if (!st) return { kind: this.kind, ready: false };
    return {
      kind: this.kind,
      installed: st.installed,
      daemonReady: st.daemonReady,
      containerRunning: st.containerRunning,
      error: st.error,
    };
  }

  /** 容器就绪（start 事件）后触发的回调，用于触发重聚合 */
  onReady(cb: () => void): void {
    this.onReadyCallback = cb;
  }

  /** 释放资源 */
  stop(): void {
    this.dockerManager?.stop();
  }

  /**
   * 惰性创建 DockerManager（延迟到真正使用 Docker 时再实例化）。
   */
  private ensureManager(): void {
    if (this.dockerManager) return;
    this.dockerManager = new DockerManager(
      this.config,
      () => this.onReadyCallback()
    );
    this.dockerManager.setServerConfigs(this.servers);
  }
}