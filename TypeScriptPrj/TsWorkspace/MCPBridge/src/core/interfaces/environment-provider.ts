/**
 * McpBridge — 环境供给 Provider 抽象
 *
 * 将「远程服务器环境供给策略」抽象为可插拔组件。
 * 编排层只依赖此接口，不感知任何具体实现（如 Docker）。
 *
 * 供编排关注的三个抽象概念：
 * - 端点是否就绪（ensureReady）
 * - 就绪后是否触发重聚合（onReady）
 * - 状态并入健康汇总，但编排不解析其形状（state）
 */

/**
 * 环境供给 Provider
 *
 * 具体实现（如 DockerProvider）只负责向编排暴露就绪/生命周期/状态，
 * 编排对具体实现零感知。
 */
export interface EnvironmentProvider {
  /** 供给类型标识（如 "docker"），用于观测归因 */
  readonly kind: string;
  /** 自报阶段名（如 "prepare-docker"），编排不预注册、按列表动态插入 */
  stage(): string;
  /** 确保环境就绪。编排仅在服务器尚未可达时作为前置条件调用 */
  ensureReady(): Promise<boolean>;
  /** 当前状态。并入健康汇总，编排不解析其形状 */
  state(): Record<string, unknown>;
  /** 环境就绪后的回调（如容器启动 → 触发重聚合） */
  onReady(cb: () => void): void;
  /** 释放资源 */
  stop(): void;
}