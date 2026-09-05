/**
 * McpBridge — 路径重写抽象
 *
 * 将「入参路径是否需翻译」从路由核心剥离为可插拔策略。
 * 路由核心持有此接口，默认 IdentityPathRewriter（本地直通）。
 * Docker 模式由 DockerPathRewriter 提供列名化的重写实现。
 */

/**
 * 路径重写器
 *
 * @param args 工具调用入参
 * @returns 路径重写后的入参
 */
export interface PathRewriter {
  rewrite(args: Record<string, unknown>): Record<string, unknown>;
}

/**
 * 恒等路径重写器（默认）
 *
 * 不修改任何入参，用于本地直连模式（宿主机路径原样透传）。
 */
export class IdentityPathRewriter implements PathRewriter {
  rewrite(args: Record<string, unknown>): Record<string, unknown> {
    return args;
  }
}