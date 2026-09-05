/**
 * McpBridge — 环境供给 Provider 装配
 *
 * 配置 → Provider 装配的唯一入口，也是全仓唯一引用 `docker.enabled` 的点。
 * 编排层只依赖返回的 ProviderSet，不感知 Docker 装配细节。
 *
 * - docker.enabled=false（默认）：空 provider 列表 + Identity 路径重写（宿主直连）
 * - docker.enabled=true：装配 DockerProvider + DockerPathRewriter
 */

import { BridgeConfig } from "../core/types.js";
import {
  EnvironmentProvider,
} from "../core/interfaces/environment-provider.js";
import {
  PathRewriter,
  IdentityPathRewriter,
} from "../core/interfaces/path-rewriter.js";
import { DockerProvider } from "./docker/docker-provider.js";
import { DockerPathRewriter } from "./docker/docker-path-rewriter.js";

/** Provider 装配结果 */
export interface ProviderSet {
  /** 环境供给 Provider 列表 */
  providers: EnvironmentProvider[];
  /** 路径重写器（默认 Identity = 宿主直通） */
  pathRewriter: PathRewriter;
}

/**
 * 根据配置装配环境供给 Provider。
 *
 * @param config 桥接配置
 * @returns Provider 集合
 */
export function buildProviders(config: BridgeConfig): ProviderSet {
  if (config.docker?.enabled === true) {
    return {
      providers: [new DockerProvider(config.docker, config.servers || [])],
      pathRewriter: new DockerPathRewriter(),
    };
  }

  // 默认宿主直连：无环境供给 Provider，路径原样透传
  return {
    providers: [],
    pathRewriter: new IdentityPathRewriter(),
  };
}