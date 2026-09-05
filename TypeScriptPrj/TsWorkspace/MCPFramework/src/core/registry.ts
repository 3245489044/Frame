/**
 * MCPFramework — 工具注册表
 *
 * 核心功能：
 * 1. 运行时动态扫描 tools 目录，懒加载 handler
 * 2. 从 tools.config.json 加载工具定义
 * 3. 业务服务注入（子项目通过此机制提供业务逻辑）
 * 4. 使用与配置分离：工具定义在 JSON，实现在 TS，运行时动态关联
 */

import * as fs from "fs";
import * as path from "path";
import { ToolDefinition, ToolHandler, BusinessService } from "./types.js";
import { resolveConfigPath, resolveToolsDir, clearPathCache } from "../utils/path-resolver.js";

// ─── 内部状态 ───────────────────────────────────────────────

/** handler 缓存：handler 名 → 处理函数（懒加载后缓存） */
const handlerCache = new Map<string, ToolHandler>();

/** 业务服务实例（由子项目注入） */
let businessService: BusinessService | null = null;

/** tools.config.json 缓存 */
let _cachedDefs: ToolDefinition[] | null = null;

/** tools 目录路径缓存 */
let _toolsDir: string | null = null;

// ─── 路径解析 ──────────────────────────────────────────────

/**
 * 解析 tools 目录路径
 *
 * 使用统一路径解析器，处理开发模式和构建后两种场景。
 *
 * @returns tools 目录的绝对路径
 */
function resolveToolsDirCached(): string {
  if (_toolsDir) return _toolsDir;
  _toolsDir = resolveToolsDir();
  return _toolsDir;
}

// ─── 工具定义加载 ──────────────────────────────────────────

/**
 * 从 tools.config.json 加载工具定义
 *
 * 使用统一路径解析器查找配置文件。
 *
 * @returns 工具定义列表
 */
export function loadToolDefinitions(): ToolDefinition[] {
  if (_cachedDefs !== null) return _cachedDefs;

  const configPath = resolveConfigPath({
    filename: "tools.config.json",
    strategy: "hybrid",
    maxDepth: 3,
  });

  if (!configPath) {
    _cachedDefs = [];
    return _cachedDefs;
  }

  try {
    const raw = fs.readFileSync(configPath, "utf-8");
    const config = JSON.parse(raw);
    _cachedDefs = (config.tools as ToolDefinition[]) || [];
    return _cachedDefs;
  } catch {
    _cachedDefs = [];
    return _cachedDefs;
  }
}

/**
 * 清除工具定义缓存（用于测试或热重载）
 */
export function clearToolDefinitionsCache(): void {
  _cachedDefs = null;
  _toolsDir = null;
}

// ─── Handler 懒加载 ────────────────────────────────────────

/**
 * 懒加载一个 handler 模块
 *
 * 按需加载 tools 目录下的 handler 文件，加载后缓存。
 * 支持 .ts（开发模式 tsx）和 .js（构建后 node）两种扩展名。
 *
 * @param handlerName handler 名称（对应文件名，不含扩展名）
 * @returns 是否加载成功
 */
async function lazyLoadHandler(handlerName: string): Promise<boolean> {
  if (!handlerName || handlerCache.has(handlerName)) return false;

  const toolsDir = resolveToolsDirCached();
  const extensions = [".js", ".ts"];

  for (const ext of extensions) {
    const filePath = path.join(toolsDir, `${handlerName}${ext}`);
    if (!fs.existsSync(filePath)) continue;

    try {
      const mod = await import(filePath);
      const handle = mod.handle;
      if (typeof handle === "function") {
        handlerCache.set(handlerName, handle);
        return true;
      }
    } catch {
      // 尝试下一个扩展名
      continue;
    }
  }

  return false;
}

// ─── Handler 注册（手动注册备选）───────────────────────────

/**
 * 手动注册一个工具处理函数
 *
 * 当自动懒加载无法满足需求时（如动态生成 handler），
 * 可以使用此方法手动注册。
 *
 * @param name handler 名称
 * @param handler 处理函数
 */
export function registerHandler(name: string, handler: ToolHandler): void {
  handlerCache.set(name, handler);
}

// ─── 业务服务注入 ──────────────────────────────────────────

/**
 * 注入业务服务实例
 *
 * 子项目在入口文件中调用此方法，将业务逻辑注入框架。
 * handler 通过 getBusinessService() 获取业务服务。
 *
 * @param service 业务服务对象
 */
export function registerBusinessService(service: BusinessService): void {
  businessService = service;
}

/**
 * 获取业务服务实例
 * @returns 业务服务对象
 * @throws 如果业务服务未注入则抛出错误
 */
export function getBusinessService<T extends BusinessService = BusinessService>(): T {
  if (!businessService) {
    throw new Error(
      "业务服务未注入。请在入口文件中调用 registerBusinessService()。"
    );
  }
  return businessService as T;
}

// ─── Handler 查找（核心 API）───────────────────────────────

/**
 * 获取工具处理函数
 *
 * 核心查找流程：
 * 1. 从 tools.config.json 查找工具定义，获取 handler 名
 * 2. 尝试懒加载 handler 模块
 * 3. 返回处理函数
 *
 * 支持两种输入：
 * - tool name（如 "get_overview"）→ 通过 tools.config.json 映射到 handler
 * - handler name（如 "overview"）→ 直接查找
 *
 * @param name tool name 或 handler name
 * @returns 处理函数，未找到时返回 undefined
 */
export async function getHandler(name: string): Promise<ToolHandler | undefined> {
  // 1. 先检查缓存
  if (handlerCache.has(name)) {
    return handlerCache.get(name);
  }

  // 2. 通过 tools.config.json 查找 handler 名
  const defs = loadToolDefinitions();
  const def = defs.find((d) => d.name === name);
  const handlerName = def?.handler || name;

  // 3. 检查缓存（可能 handler 名与 tool name 不同）
  if (handlerCache.has(handlerName)) {
    return handlerCache.get(handlerName);
  }

  // 4. 懒加载 handler
  if (await lazyLoadHandler(handlerName)) {
    return handlerCache.get(handlerName);
  }

  return undefined;
}

/**
 * 获取所有已注册的工具定义（含处理函数）
 * @returns 工具定义列表（仅包含有对应 handler 的）
 */
export async function getRegisteredTools(): Promise<ToolDefinition[]> {
  const defs = loadToolDefinitions();
  const results: ToolDefinition[] = [];
  for (const d of defs) {
    if (!d.handler) continue;
    if (handlerCache.has(d.handler)) {
      results.push(d);
    } else if (await lazyLoadHandler(d.handler)) {
      results.push(d);
    }
  }
  return results;
}

/**
 * 检查指定工具是否有对应的处理函数
 * @param name tool name 或 handler name
 */
export async function hasHandler(name: string): Promise<boolean> {
  return (await getHandler(name)) !== undefined;
}

/**
 * 获取所有已加载的 handler 名称列表
 */
export function getLoadedHandlerNames(): string[] {
  return [...handlerCache.keys()];
}
