/**
 * McpBridge — Docker 路径重写器
 *
 * 实现 PathRewriter，将宿主机路径转换为容器内路径。
 * 从 loadPathsConfig() 读取 containerRoot 与路径键名白名单，
 * 取代原 router.ts 内置的硬编码翻译（第七章 #12 的落点）。
 */

import * as path from "path";
import { PathRewriter } from "../../core/interfaces/path-rewriter.js";
import { loadPathsConfig } from "../../utils/paths.js";

/**
 * Docker 路径重写器
 *
 * MCPBridge 运行在宿主机（Windows），但远程 MCP 服务器运行在 Docker 容器内。
 * CLine 传入的路径参数是宿主机相对/绝对路径，需转换为容器内绝对路径。
 *
 * 路径键名白名单默认值：
 * - path / file_path / script_path / rootDir / projectRoot
 */
export class DockerPathRewriter implements PathRewriter {
  /** 容器内路径根 */
  private readonly containerRoot: string;
  /** 需要重写的路径参数键名白名单 */
  private readonly pathKeys: string[];

  constructor() {
    const cfg = loadPathsConfig();
    this.containerRoot = cfg.containerRoot ?? "/workspace/WorkFlowPrj";
    this.pathKeys = cfg.pathFields ?? ["path", "file_path", "script_path", "rootDir", "projectRoot"];
  }

  /**
   * 重写路径参数：将宿主机路径转换为容器内路径。
   *
   * - 已是容器内路径（以 / 开头）：原样透传
   * - 宿主机绝对路径：替换 projectRoot 根为 containerRoot
   * - 相对路径：拼接 containerRoot 前缀
   */
  rewrite(args: Record<string, unknown>): Record<string, unknown> {
    const cfg = loadPathsConfig();
    const projectRoot = cfg.projectRoot.replace(/\\/g, "/"); // 标准化为 /

    const rewritten: Record<string, unknown> = {};
    for (const [key, value] of Object.entries(args)) {
      if (typeof value === "string" && this.pathKeys.includes(key)) {
        let val = value as string;

        // 跳过已经是容器内路径的（以 / 开头）
        if (val.startsWith("/")) {
          rewritten[key] = val;
          continue;
        }

        // 标准化反斜杠为正斜杠
        val = val.replace(/\\/g, "/");

        // 如果是宿主机绝对路径，替换根部分
        if (path.win32.isAbsolute(value as string)) {
          val = val.replace(projectRoot, this.containerRoot);
        } else {
          // 如果是相对路径，拼接容器根目录
          val = `${this.containerRoot}/${val}`;
        }

        rewritten[key] = val;
      } else {
        rewritten[key] = value;
      }
    }

    return rewritten;
  }
}