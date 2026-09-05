/**
 * BasicMethod — 统一路径解析器
 *
 * 从 MCPFramework 的 path-resolver 和 MCPBridge 的 paths.ts 中提取的通用路径查找逻辑。
 * 合并了两者的能力：MCPFramework 的"查找策略" + MCPBridge 的"配置加载"。
 *
 * 设计原则：
 * - 零外部依赖（仅依赖 basic-method 自身的 constants）
 * - 五种查找策略，覆盖所有使用场景
 * - 支持缓存，避免重复文件系统操作
 * - 与 MCPFramework 的 path-resolver 接口兼容
 *
 * 使用方式：
 * ```typescript
 * import { findProjectRoot, resolveConfigPath } from "basic-method/file/pathResolver";
 *
 * // 查找项目根目录
 * const root = findProjectRoot();
 *
 * // 混合策略查找配置文件
 * const configPath = resolveConfigPath({
 *   filename: "app.config.json",
 *   strategy: "hybrid",
 * });
 * ```
 */

import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";
import { DEFAULT_MAX_DEPTH, DEFAULT_EXCLUDE_DIRS } from "../constants.js";

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
  /** 要查找的文件名（如 "app.config.json"） */
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
   * 不指定时使用默认行为。
   */
  baseDir?: string;
}

/** 路径配置（从 JSON 配置文件加载） */
export interface PathsConfig {
  /** 项目根目录（绝对路径） */
  projectRoot: string;
  /** 工作区根目录（相对于 projectRoot） */
  workspaceRoot: string;
  /** 桥接器根目录（相对于 projectRoot） */
  bridgeRoot: string;
  /** Docker Compose 目录（相对于 projectRoot） */
  dockerComposeDir: string;
  /** Docker Compose 文件（相对于 projectRoot） */
  dockerComposeFile: string;
  /** 配置文件名称 */
  configFile: string;
}

// ═══════════════════════════════════════════════════════════════
// 缓存
// ═══════════════════════════════════════════════════════════════

/** 路径解析结果缓存 */
const resolveCache = new Map<string, string | null>();

/** 项目根目录缓存 */
let _projectRoot: string | null = null;

// ═══════════════════════════════════════════════════════════════
// 项目根目录查找
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
 * // => "d:/Project/Program/WorkFlowPrj/TypeScriptPrj/TsWorkspace/BasicMethod"
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

// ═══════════════════════════════════════════════════════════════
// 配置文件查找
// ═══════════════════════════════════════════════════════════════

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
 *   filename: "app.config.json",
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
    maxDepth = DEFAULT_MAX_DEPTH,
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

// ═══════════════════════════════════════════════════════════════
// 路径配置加载（从 JSON 文件）
// ═══════════════════════════════════════════════════════════════

/**
 * 默认路径配置
 */
const DEFAULT_PATHS: PathsConfig = {
  projectRoot: "",
  workspaceRoot: "",
  bridgeRoot: "",
  dockerComposeDir: "",
  dockerComposeFile: "",
  configFile: "app.config.json",
};

/**
 * 从 JSON 配置文件加载路径配置
 *
 * 从指定目录（或从当前工作目录开始查找）的 JSON 配置文件中读取 paths 节。
 * 如果配置文件中未定义 paths 节，返回默认值。
 * 结果缓存，避免重复读取。
 *
 * @param configDir 配置文件所在目录（可选，默认从 process.cwd() 开始查找）
 * @param configFileName 配置文件名（默认 "app.config.json"）
 * @returns 路径配置
 *
 * @example
 * ```typescript
 * const paths = loadPathsConfig();
 * console.log(paths.projectRoot);
 * ```
 */
export function loadPathsConfig(configDir?: string, configFileName?: string): PathsConfig {
  const fileName = configFileName || DEFAULT_PATHS.configFile;

  // 确定配置文件所在目录
  const searchDirs = configDir
    ? [configDir]
    : [
        process.cwd(),
      ];

  for (const dir of searchDirs) {
    const configPath = path.resolve(dir, fileName);
    if (fs.existsSync(configPath)) {
      try {
        const raw = JSON.parse(fs.readFileSync(configPath, "utf-8"));
        if (raw.paths) {
          return { ...DEFAULT_PATHS, ...raw.paths };
        }
      } catch {
        // 解析失败，使用默认值
      }
    }
  }

  return { ...DEFAULT_PATHS };
}

// ═══════════════════════════════════════════════════════════════
// 安全路径拼接
// ═══════════════════════════════════════════════════════════════

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
// 目录遍历
// ═══════════════════════════════════════════════════════════════

/**
 * 递归查找匹配模式的文件
 *
 * @param rootDir 根目录
 * @param pattern 文件名匹配模式（如 ".csproj"）
 * @param excludeDirs 排除的目录名列表
 * @returns 匹配文件的绝对路径列表
 *
 * @example
 * ```typescript
 * const csprojFiles = findFilesByPattern(projectRoot, ".csproj");
 * const jsonFiles = findFilesByPattern(projectRoot, ".json", ["node_modules", ".git"]);
 * ```
 */
export function findFilesByPattern(
  rootDir: string,
  pattern: string,
  excludeDirs: string[] = DEFAULT_EXCLUDE_DIRS
): string[] {
  const result: string[] = [];

  function walk(dir: string) {
    let entries: fs.Dirent[];
    try {
      entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch {
      return;
    }

    for (const entry of entries) {
      const fullPath = path.join(dir, entry.name);

      if (entry.isDirectory()) {
        if (!excludeDirs.includes(entry.name)) {
          walk(fullPath);
        }
      } else if (entry.name.endsWith(pattern)) {
        result.push(fullPath);
      }
    }
  }

  walk(rootDir);
  return result;
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
 */
function getCallerDirectory(): string | null {
  const stack = new Error().stack;
  if (!stack) return null;

  const lines = stack.split("\n");

  // 解析调用栈，找到第一个不是 pathResolver.ts 的调用方
  let callerFile: string | null = null;

  for (const line of lines) {
    // 跳过当前文件自身
    if (line.includes("pathResolver.ts")) continue;
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
