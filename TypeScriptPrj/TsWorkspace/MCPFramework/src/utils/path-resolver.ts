/**
 * MCPFramework — 统一路径解析器
 *
 * 集中管理所有路径查找逻辑，消除各模块中散落的硬编码候选路径。
 * 提供统一的查找策略，支持跨项目复用。
 *
 * 核心能力：
 * 1. findProjectRoot() — 从任意位置向上查找项目根目录
 * 2. resolveConfigPath() — 按策略查找配置文件
 * 3. resolveToolsDir() — 解析 tools 目录路径
 * 4. resolveRelative() — 安全拼接路径
 *
 * 使用方式：
 * ```typescript
 * import { resolveConfigPath, findProjectRoot } from "../utils/path-resolver.js";
 *
 * // 查找配置文件
 * const configPath = resolveConfigPath({
 *   filename: "mcp-bridge.config.json",
 *   strategy: "hybrid",
 * });
 * ```
 */

import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";

// ═══════════════════════════════════════════════════════════════
// 类型定义
// ═══════════════════════════════════════════════════════════════

/** 路径查找策略 */
export type PathResolveStrategy =
  /** 从项目根目录查找（最可靠，适合生产部署） */
  | "project-root"
  /** 从脚本所在目录查找（适合 CLI 工具） */
  | "script-dir"
  /** 从当前工作目录查找（适合开发模式） */
  | "cwd"
  /** 从调用方模块所在目录向上查找（适合模块化项目） */
  | "caller-dir"
  /** 组合策略：project-root → script-dir → cwd → caller-dir */
  | "hybrid";

/** 路径解析选项 */
export interface PathResolveOptions {
  /** 要查找的文件名（如 "mcp-bridge.config.json"） */
  filename: string;
  /** 查找策略（默认 "hybrid"） */
  strategy?: PathResolveStrategy;
  /** 向上搜索的最大层级（默认 5） */
  maxDepth?: number;
  /** 额外候选子目录（相对于基准目录） */
  extraDirs?: string[];
  /** 是否缓存结果（默认 true） */
  cache?: boolean;
  /**
   * 基准目录（可选）
   *
   * 指定后，project-root 策略将从该目录向上查找 package.json；
   * cwd 策略将使用该目录替代 process.cwd()。
   * 不指定时使用默认行为（path-resolver 自身位置 / process.cwd()）。
   */
  baseDir?: string;
}

// ═══════════════════════════════════════════════════════════════
// 缓存
// ═══════════════════════════════════════════════════════════════

/** 路径解析结果缓存 */
const resolveCache = new Map<string, string | null>();

/** 项目根目录缓存 */
let _projectRoot: string | null = null;

// ═══════════════════════════════════════════════════════════════
// 核心函数
// ═══════════════════════════════════════════════════════════════

/**
 * 查找项目根目录
 *
 * 从当前模块位置（或指定起始目录）向上查找，找到包含 package.json 的目录。
 * 如果找不到 package.json，回退到 process.cwd()。
 *
 * @param startDir 起始目录（默认从本文件位置向上查找）
 * @returns 项目根目录的绝对路径
 *
 * @example
 * ```typescript
 * const root = findProjectRoot();
 * // => "d:/Project/Program/WorkFlowPrj/TypeScriptPrj/TsWorkspace/MCPFramework"
 * ```
 */
export function findProjectRoot(startDir?: string): string {
  if (_projectRoot) return _projectRoot;

  // 确定起始目录
  let dir = startDir;
  if (!dir) {
    // 从本文件位置开始
    const currentFile = fileURLToPath(import.meta.url);
    dir = path.dirname(currentFile);
  }

  // 向上查找 package.json
  let current = path.resolve(dir);
  const root = path.parse(current).root; // 盘符根目录（如 "d:\\"）

  while (current !== root) {
    if (fs.existsSync(path.join(current, "package.json"))) {
      _projectRoot = current;
      return current;
    }
    current = path.dirname(current);
  }

  // 回退到 cwd
  _projectRoot = process.cwd();
  return _projectRoot;
}

/**
 * 清除项目根目录缓存（用于测试或热重载）
 */
export function clearProjectRootCache(): void {
  _projectRoot = null;
}

/**
 * 按策略查找配置文件
 *
 * 支持五种查找策略：
 * - project-root: 从项目根目录开始，在根目录和 extraDirs 中查找
 * - script-dir: 从脚本所在目录开始，向上搜索 maxDepth 层
 * - cwd: 从当前工作目录开始，向上搜索 maxDepth 层
 * - caller-dir: 从调用方模块所在目录开始，向上搜索 maxDepth 层
 * - hybrid: 按 project-root → script-dir → cwd → caller-dir 顺序尝试，返回第一个找到的
 *
 * @param options 查找选项
 * @returns 配置文件的绝对路径，未找到时返回 null
 *
 * @example
 * ```typescript
 * // 混合策略查找
 * const path = resolveConfigPath({
 *   filename: "mcp-bridge.config.json",
 *   strategy: "hybrid",
 * });
 *
 * // 从项目根目录查找
 * const path = resolveConfigPath({
 *   filename: "tools.config.json",
 *   strategy: "project-root",
 *   extraDirs: ["config"],
 * });
 * ```
 */
export function resolveConfigPath(options: PathResolveOptions): string | null {
  const {
    filename,
    strategy = "hybrid",
    maxDepth = 5,
    extraDirs = [],
    cache = true,
    baseDir,
  } = options;

  // 缓存键
  const cacheKey = `${strategy}:${filename}:${maxDepth}:${extraDirs.join(",")}:${baseDir || ""}`;
  if (cache && resolveCache.has(cacheKey)) {
    return resolveCache.get(cacheKey) ?? null;
  }

  let result: string | null = null;

  switch (strategy) {
    case "project-root":
      result = resolveFromProjectRoot(filename, extraDirs, baseDir);
      break;
    case "script-dir":
      result = resolveFromScriptDir(filename, maxDepth, extraDirs);
      break;
    case "cwd":
      result = resolveFromCwd(filename, maxDepth, extraDirs, baseDir);
      break;
    case "caller-dir":
      result = resolveFromCallerDir(filename, maxDepth, extraDirs);
      break;
    case "hybrid":
      result = resolveHybrid(filename, maxDepth, extraDirs, baseDir);
      break;
  }

  if (cache) {
    resolveCache.set(cacheKey, result);
  }
  return result;
}

/**
 * 清除路径解析缓存（用于测试或热重载）
 */
export function clearPathCache(): void {
  resolveCache.clear();
}

/**
 * 解析 tools 目录路径
 *
 * 处理两种场景：
 * 1. 开发模式（tsx 运行）：src/tools/
 * 2. 构建后（node 运行）：build/tools/
 *
 * @param baseDir 基准目录（默认从项目根目录查找）
 * @returns tools 目录的绝对路径
 */
export function resolveToolsDir(baseDir?: string): string {
  const root = baseDir ? path.resolve(baseDir) : findProjectRoot();

  const candidates = [
    // 构建后：build/tools/
    path.join(root, "build", "tools"),
    // 开发模式：src/tools/
    path.join(root, "src", "tools"),
    // 当前工作目录下的 tools/
    path.join(process.cwd(), "src", "tools"),
  ];

  for (const dir of candidates) {
    if (fs.existsSync(dir)) {
      return dir;
    }
  }

  // 默认回退
  return candidates[0];
}

/**
 * 安全拼接路径
 *
 * 确保返回绝对路径，处理 Windows 和 POSIX 路径分隔符差异。
 *
 * @param base 基准路径
 * @param segments 路径片段
 * @returns 拼接后的绝对路径
 *
 * @example
 * ```typescript
 * const fullPath = resolveRelative("/project", "config", "app.json");
 * // => "/project/config/app.json"
 * ```
 */
export function resolveRelative(base: string, ...segments: string[]): string {
  return path.resolve(base, ...segments);
}

// ═══════════════════════════════════════════════════════════════
// 内部实现
// ═══════════════════════════════════════════════════════════════

/**
 * 从项目根目录查找
 */
function resolveFromProjectRoot(
  filename: string,
  extraDirs: string[],
  baseDir?: string
): string | null {
  const root = baseDir ? findProjectRoot(baseDir) : findProjectRoot();

  // 1. 直接在根目录查找
  const rootPath = path.join(root, filename);
  if (fs.existsSync(rootPath)) return rootPath;

  // 2. 在 extraDirs 中查找
  for (const dir of extraDirs) {
    const candidate = path.join(root, dir, filename);
    if (fs.existsSync(candidate)) return candidate;
  }

  return null;
}

/**
 * 从脚本所在目录向上查找
 */
function resolveFromScriptDir(
  filename: string,
  maxDepth: number,
  extraDirs: string[]
): string | null {
  const currentFile = fileURLToPath(import.meta.url);
  const scriptDir = path.dirname(currentFile);

  // 从脚本目录向上搜索
  let current = scriptDir;
  for (let i = 0; i <= maxDepth; i++) {
    // 检查当前目录
    const candidate = path.join(current, filename);
    if (fs.existsSync(candidate)) return candidate;

    // 检查 extraDirs
    for (const dir of extraDirs) {
      const extraCandidate = path.join(current, dir, filename);
      if (fs.existsSync(extraCandidate)) return extraCandidate;
    }

    // 向上
    const parent = path.dirname(current);
    if (parent === current) break; // 已到根目录
    current = parent;
  }

  return null;
}

/**
 * 从当前工作目录向上查找
 */
function resolveFromCwd(
  filename: string,
  maxDepth: number,
  extraDirs: string[],
  baseDir?: string
): string | null {
  let current = baseDir ? path.resolve(baseDir) : process.cwd();

  for (let i = 0; i <= maxDepth; i++) {
    // 检查当前目录
    const candidate = path.join(current, filename);
    if (fs.existsSync(candidate)) return candidate;

    // 检查 extraDirs
    for (const dir of extraDirs) {
      const extraCandidate = path.join(current, dir, filename);
      if (fs.existsSync(extraCandidate)) return extraCandidate;
    }

    // 向上
    const parent = path.dirname(current);
    if (parent === current) break;
    current = parent;
  }

  return null;
}

/**
 * 从调用方模块所在目录向上查找
 *
 * 通过解析 Error.stack 获取调用方文件路径，
 * 然后从调用方所在目录向上搜索 maxDepth 层。
 * 适用于模块化项目中，调用方与配置文件在同一项目树下的场景。
 */
function resolveFromCallerDir(
  filename: string,
  maxDepth: number,
  extraDirs: string[]
): string | null {
  const callerDir = getCallerDirectory();
  if (!callerDir) return null;

  let current = callerDir;
  for (let i = 0; i <= maxDepth; i++) {
    // 检查当前目录
    const candidate = path.join(current, filename);
    if (fs.existsSync(candidate)) return candidate;

    // 检查 extraDirs
    for (const dir of extraDirs) {
      const extraCandidate = path.join(current, dir, filename);
      if (fs.existsSync(extraCandidate)) return extraCandidate;
    }

    // 向上
    const parent = path.dirname(current);
    if (parent === current) break;
    current = parent;
  }

  return null;
}

/**
 * 获取调用方模块的目录路径
 *
 * 通过解析 Error.stack 获取调用栈信息，找到调用 resolveConfigPath 的模块路径。
 * 跳过 path-resolver.ts 自身和内部辅助函数，定位到实际业务模块。
 *
 * 缓存结果避免重复解析。
 */
const callerDirCache = new Map<string, string | null>();

function getCallerDirectory(): string | null {
  // 生成一个唯一键用于缓存（基于当前调用栈）
  // 注意：这里不能缓存太死，因为不同调用方需要不同的结果
  // 我们只缓存到 resolveConfigPath 的调用方

  const stack = new Error().stack;
  if (!stack) return null;

  const lines = stack.split("\n");

  // 解析调用栈，找到第一个不是 path-resolver.ts 的调用方
  // 格式示例：
  //   at resolveConfigPath (file:///.../path-resolver.ts:158:20)
  //   at McpBridge.loadConfig (file:///.../bridge.ts:324:24)
  //   at McpBridge.initialize (file:///.../bridge.ts:81:24)
  let callerFile: string | null = null;

  for (const line of lines) {
    // 跳过当前文件自身
    if (line.includes("path-resolver.ts")) continue;
    if (line.includes("resolveHybrid") || line.includes("resolveFrom")) continue;

    // 提取文件路径
    const match = line.match(/\(?(file:\/\/\/[^:)]+)/);
    if (match) {
      callerFile = match[1];
      break;
    }

    // 兼容 Windows 路径格式
    const winMatch = line.match(/\(?([A-Za-z]:\\[^:)]+)/);
    if (winMatch) {
      callerFile = winMatch[1];
      break;
    }
  }

  if (!callerFile) return null;

  // 转换为文件系统路径
  let filePath: string;
  if (callerFile.startsWith("file://")) {
    filePath = fileURLToPath(callerFile);
  } else {
    filePath = callerFile;
  }

  return path.dirname(filePath);
}

/**
 * 混合策略：按 project-root → script-dir → cwd → caller-dir 顺序尝试
 */
function resolveHybrid(
  filename: string,
  maxDepth: number,
  extraDirs: string[],
  baseDir?: string
): string | null {
  // 1. 先尝试 project-root
  const fromRoot = resolveFromProjectRoot(filename, extraDirs, baseDir);
  if (fromRoot) return fromRoot;

  // 2. 再尝试 script-dir
  const fromScript = resolveFromScriptDir(filename, maxDepth, extraDirs);
  if (fromScript) return fromScript;

  // 3. 然后尝试 cwd
  const fromCwd = resolveFromCwd(filename, maxDepth, extraDirs, baseDir);
  if (fromCwd) return fromCwd;

  // 4. 最后尝试 caller-dir（从调用方模块所在目录查找）
  const fromCaller = resolveFromCallerDir(filename, maxDepth, extraDirs);
  if (fromCaller) return fromCaller;

  return null;
}
