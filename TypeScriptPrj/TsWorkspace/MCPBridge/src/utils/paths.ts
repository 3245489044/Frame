/**
 * McpBridge — 路径配置加载器
 *
 * 集中管理所有项目路径配置，从 mcp-bridge.config.json 的 paths 节读取。
 * 各模块通过此模块获取路径，消除散落的 fileURLToPath 和 ".." 硬编码。
 *
 * 核心原则：
 * - 所有项目路径在 mcp-bridge.config.json 的 paths 节中定义
 * - 此模块负责加载和解析，其他模块只调用 API
 * - 支持绝对路径和相对路径（相对于项目根目录）
 * - 无硬编码默认值：从 process.cwd() 向上查找配置文件
 */

import * as fs from "fs";
import * as path from "path";
import { logger } from "./logger.js";

// ═══════════════════════════════════════════════════════════════
// 类型定义
// ═══════════════════════════════════════════════════════════════

/** 路径配置 */
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
  /** 容器内路径根（可选，默认 "/workspace/WorkFlowPrj"） */
  containerRoot?: string;
  /** 需要做路径重写的参数键名白名单（可选，默认 5 个内置键） */
  pathFields?: string[];
}

// ═══════════════════════════════════════════════════════════════
// 常量
// ═══════════════════════════════════════════════════════════════

/** 配置文件名称 */
const CONFIG_FILENAME = "mcp-bridge.config.json";

/** 向上查找的最大深度 */
const MAX_SEARCH_DEPTH = 5;

// ═══════════════════════════════════════════════════════════════
// 缓存
// ═══════════════════════════════════════════════════════════════

let _paths: PathsConfig | null = null;
let _configDir: string | null = null;

// ═══════════════════════════════════════════════════════════════
// 内部工具函数
// ═══════════════════════════════════════════════════════════════

/**
 * 从指定目录开始向上查找配置文件
 *
 * @param startDir 搜索起点
 * @param filename 要查找的文件名
 * @param maxDepth 最大向上查找深度
 * @returns 配置文件的绝对路径，未找到返回 null
 */
function findConfigUpwards(
  startDir: string,
  filename: string,
  maxDepth: number
): string | null {
  let current = path.resolve(startDir);
  for (let i = 0; i < maxDepth; i++) {
    const candidate = path.join(current, filename);
    if (fs.existsSync(candidate)) return candidate;
    const parent = path.dirname(current);
    if (parent === current) break; // 已到根目录
    current = parent;
  }
  return null;
}

// ═══════════════════════════════════════════════════════════════
// 核心函数
// ═══════════════════════════════════════════════════════════════

/**
 * 加载路径配置
 *
 * 从 mcp-bridge.config.json 的 paths 节读取配置。
 * 从 process.cwd() 开始向上查找配置文件，最多查找 5 级。
 * 结果缓存，避免重复读取。
 *
 * @param configDir 配置文件所在目录（可选，默认从 process.cwd() 开始向上查找）
 * @returns 路径配置
 * @throws 未找到配置文件时抛出错误
 */
export function loadPathsConfig(configDir?: string): PathsConfig {
  if (_paths) return _paths as PathsConfig;

  // 确定搜索起点
  const startDir = configDir || process.cwd();

  // 向上查找配置文件
  const configPath = findConfigUpwards(startDir, CONFIG_FILENAME, MAX_SEARCH_DEPTH);

  if (!configPath) {
    throw new Error(
      `未找到 ${CONFIG_FILENAME}。请确保在项目目录中运行。\n` +
      `搜索起点: ${startDir}\n` +
      `最大查找深度: ${MAX_SEARCH_DEPTH} 级`
    );
  }

  try {
    const raw = JSON.parse(fs.readFileSync(configPath, "utf-8"));
    if (!raw.paths) {
      throw new Error(
        `配置文件 ${configPath} 中缺少 "paths" 节。\n` +
        `请在配置文件中添加 paths 配置。`
      );
    }
    _configDir = path.dirname(configPath);
    _paths = raw.paths as PathsConfig;
    logger.debug("PathsConfig", `已从 ${configPath} 加载路径配置`);
    return _paths as PathsConfig;
  } catch (err) {
    if (err instanceof SyntaxError) {
      throw new Error(`配置文件 ${configPath} 格式错误: ${err.message}`);
    }
    throw err;
  }
}

/**
 * 清除路径配置缓存（用于测试或热重载）
 */
export function clearPathCache(): void {
  _paths = null;
  _configDir = null;
}

/**
 * 获取桥接器根目录的绝对路径
 */
export function getBridgeRoot(configDir?: string): string {
  const cfg = loadPathsConfig(configDir);
  return path.resolve(cfg.projectRoot, cfg.bridgeRoot);
}

/**
 * 获取 Docker Compose 文件的绝对路径
 */
export function getDockerComposePath(configDir?: string): string {
  const cfg = loadPathsConfig(configDir);
  return path.resolve(cfg.projectRoot, cfg.dockerComposeFile);
}

/**
 * 获取 Docker Compose 目录的绝对路径
 */
export function getDockerComposeDir(configDir?: string): string {
  const cfg = loadPathsConfig(configDir);
  return path.resolve(cfg.projectRoot, cfg.dockerComposeDir);
}

/**
 * 获取配置文件的绝对路径
 */
export function getConfigFilePath(configDir?: string): string {
  const cfg = loadPathsConfig(configDir);
  return path.resolve(cfg.projectRoot, cfg.bridgeRoot, cfg.configFile);
}
