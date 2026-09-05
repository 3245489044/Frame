/**
 * MCPFramework — 通用工具函数
 *
 * 提供框架级别的通用工具，子项目可复用。
 *
 * 字符串格式化函数已迁移至 BasicMethod（stringUtils），
 * 本文件保留框架特有的工具函数。
 */

import * as fs from "fs";
import * as path from "path";
import fg from "fast-glob";
import { resolveConfigPath } from "../utils/path-resolver.js";

// ─── 配置加载 ───────────────────────────────────────────────

/**
 * 查找第一个存在的文件路径
 *
 * 遍历候选路径列表，返回第一个存在的文件路径。
 * 如果都不存在，返回 null。
 *
 * @param candidates 候选文件路径列表
 * @returns 第一个存在的文件路径，或 null
 *
 * @deprecated 推荐使用 resolveConfigPath() 替代
 *
 * @example
 * ```typescript
 * const configPath = findFirstExisting([
 *   "tools.config.json",
 *   "config/tools.config.json",
 * ]);
 * ```
 */
export function findFirstExisting(candidates: string[]): string | null {
  for (const filePath of candidates) {
    if (fs.existsSync(filePath)) {
      return filePath;
    }
  }
  return null;
}

/**
 * 从 JSON 配置文件加载数据
 *
 * 使用统一路径解析器查找配置文件。
 *
 * @param relativePath 相对于项目根目录的路径
 * @returns 解析后的 JSON 对象
 *
 * @example
 * ```typescript
 * const layers = loadConfig<{ layers: LayerInfo[] }>("src/config/layers.json");
 * ```
 */
export function loadConfig<T = Record<string, unknown>>(relativePath: string): T {
  const configPath = resolveConfigPath({
    filename: path.basename(relativePath),
    strategy: "hybrid",
    maxDepth: 3,
    extraDirs: [path.dirname(relativePath)],
  });

  if (!configPath) {
    throw new Error(`配置文件未找到: ${relativePath}`);
  }

  const raw = fs.readFileSync(configPath, "utf-8");
  return JSON.parse(raw) as T;
}

// ─── 文件系统工具 ──────────────────────────────────────────

/**
 * 递归查找匹配模式的文件（使用 fast-glob）
 *
 * @param rootDir 根目录
 * @param pattern glob 匹配模式（如 "**／*.csproj"）
 * @param excludePatterns 要排除的目录名列表
 * @returns 匹配的文件路径列表
 *
 * @example
 * ```typescript
 * const files = findFiles("/project", "**／*.csproj");
 * const tsFiles = findFiles("/project", "**／*.ts", ["node_modules", "dist"]);
 * ```
 */
export function findFiles(
  rootDir: string,
  pattern: string = "**/*",
  excludePatterns: string[] = ["node_modules", ".git", "obj", "bin", "build"],
): string[] {
  const ignore = excludePatterns.map((p) => `**/${p}/**`);
  return fg.sync(pattern, {
    cwd: rootDir,
    ignore,
    absolute: true,
    dot: false,
    onlyFiles: true,
  });
}

/**
 * 递归查找匹配谓词的文件（兼容旧版 API，内部使用 fast-glob）
 *
 * @param rootDir 根目录
 * @param predicate 文件匹配函数
 * @param excludePatterns 要排除的目录名列表
 * @returns 匹配的文件路径列表
 *
 * @deprecated 推荐使用 findFiles() 配合 glob 模式
 */
export function findFilesByPredicate(
  rootDir: string,
  predicate: (filePath: string) => boolean,
  excludePatterns: string[] = ["node_modules", ".git", "obj", "bin", "build"],
): string[] {
  const allFiles = findFiles(rootDir, "**/*", excludePatterns);
  return allFiles.filter(predicate);
}
