/**
 * McpBridge — 本地工具注册表
 *
 * 从 tools-definitions/*.json 加载工具定义，自动注册 handler 到实现的映射。
 * 消除 bridge.ts 中的硬编码 switch 语句，实现纯配置驱动。
 *
 * 核心流程：
 * 1. 扫描 tools-definitions/*.json 文件
 * 2. 解析每个工具定义的 handler 字段
 * 3. 动态 import 对应的实现模块
 * 4. 提供统一的 execute 接口
 *
 * 工具定义 JSON 格式：
 * ```json
 * {
 *   "name": "invoke_ps_script",
 *   "handler": "invoke-ps-script",        // 对应 src/tools/invoke-ps-script.ts
 *   "template": "with-args",              // 参数模板类型
 *   "inputSchema": { ... },
 *   "implementation": {                   // 可选：声明式调用描述
 *     "imports": [...],
 *     "mainCall": "...",
 *     "outputCall": "..."
 *   }
 * }
 * ```
 */

import * as fs from "fs";
import * as path from "path";
import { logger } from "../utils/logger.js";
import { loadPathsConfig } from "../utils/paths.js";
import { McpToolDefinition, CallToolResult } from "mcp-framework/core/types";

// ═══════════════════════════════════════════════════════════════
// 类型定义
// ═══════════════════════════════════════════════════════════════

/** 工具定义 JSON 的完整结构 */
export interface ToolDefinitionJson extends McpToolDefinition {
  /** handler 名称，对应 src/tools/ 下的模块文件名（不含 .ts） */
  handler: string;
  /** 参数模板类型 */
  template?: "no-args" | "with-args";
  /** 声明式调用描述（可选） */
  implementation?: {
    imports?: Array<{ name: string; from: string }>;
    mainCall: string;
    outputCall: string;
  };
}

/** 工具执行函数签名 */
type ToolHandler = (args: Record<string, unknown>) => Promise<string>;

/** 已注册的工具条目 */
interface RegisteredTool {
  /** 工具定义 */
  definition: ToolDefinitionJson;
  /** 执行函数 */
  handler: ToolHandler;
}

// ═══════════════════════════════════════════════════════════════
// 注册表
// ═══════════════════════════════════════════════════════════════

class LocalToolRegistry {
  /** 工具名称 → 注册条目 */
  private tools = new Map<string, RegisteredTool>();

  /** 是否已初始化 */
  private initialized = false;

  // ─── 初始化 ───────────────────────────────────────────────

  /**
   * 初始化注册表
   *
   * 扫描 tools-definitions/*.json，加载所有工具定义并注册 handler。
   */
  async initialize(): Promise<void> {
    if (this.initialized) return;

    const defsDir = this.resolveDefinitionsDir();
    if (!defsDir) {
      logger.warn("LocalToolRegistry", "工具定义目录未找到，跳过本地工具注册");
      this.initialized = true;
      return;
    }

    const files = fs.readdirSync(defsDir)
      .filter(f => f.endsWith(".json"))
      .sort();

    let loaded = 0;
    for (const file of files) {
      try {
        const filePath = path.join(defsDir, file);
        const def = JSON.parse(fs.readFileSync(filePath, "utf-8")) as ToolDefinitionJson;

        if (!def.name || !def.handler) {
          logger.warn("LocalToolRegistry", `跳过无效工具定义: ${file}（缺少 name 或 handler）`);
          continue;
        }

        const handler = await this.resolveHandler(def);
        this.tools.set(def.name, { definition: def, handler });
        logger.debug("LocalToolRegistry", `已注册本地工具: ${def.name} (handler: ${def.handler})`);
        loaded++;
      } catch (err) {
        logger.warn("LocalToolRegistry", `加载工具定义失败: ${file} - ${err instanceof Error ? err.message : String(err)}`);
      }
    }

    logger.info("LocalToolRegistry", `已注册 ${loaded} 个本地工具`);
    this.initialized = true;
  }

  /**
   * 解析工具定义目录路径
   *
   * 从 mcp-bridge.config.json 的 localTools.definitionsDir 读取，
   * 默认为 "tools-definitions"（相对于桥接器根目录）。
   */
  private resolveDefinitionsDir(): string | null {
    try {
      const cfg = loadPathsConfig();
      const bridgeRoot = path.resolve(cfg.projectRoot, cfg.bridgeRoot);

      // 尝试从配置文件读取 definitionsDir
      const configPath = path.resolve(bridgeRoot, cfg.configFile);
      let defsDirName = "tools-definitions";

      if (fs.existsSync(configPath)) {
        try {
          const config = JSON.parse(fs.readFileSync(configPath, "utf-8"));
          if (config.localTools?.definitionsDir) {
            defsDirName = config.localTools.definitionsDir;
          }
        } catch {
          // 忽略配置解析错误，使用默认值
        }
      }

      const defsDir = path.resolve(bridgeRoot, defsDirName);
      return fs.existsSync(defsDir) ? defsDir : null;
    } catch {
      return null;
    }
  }

  /**
   * 解析 handler 名称到实际的执行函数
   *
   * 查找顺序：
   * 1. 尝试动态 import src/tools/{handler}.ts
   * 2. 如果模块有 default export，使用它
   * 3. 如果模块有同名 export，使用它
   *
   * @param def 工具定义
   * @returns 执行函数
   */
  private async resolveHandler(def: ToolDefinitionJson): Promise<ToolHandler> {
    const handlerName = def.handler;

    try {
      // 动态 import handler 模块
      // handler 名称使用 kebab-case，对应 src/tools/{handler}.ts
      const module = await import(`./${handlerName}.js`);

      // 优先使用 default export
      if (typeof module.default === "function") {
        return module.default as ToolHandler;
      }

      // 其次使用同名 export
      const exportName = this.kebabToCamel(handlerName);
      if (typeof module[exportName] === "function") {
        return module[exportName] as ToolHandler;
      }

      // 最后使用 named export
      if (typeof module[handlerName] === "function") {
        return module[handlerName] as ToolHandler;
      }

      throw new Error(
        `handler 模块 ${handlerName}.js 中未找到可用的导出函数。` +
        `请确保模块 export default 一个函数，或 export 名为 ${exportName} 的函数。`
      );
    } catch (err) {
      if (err instanceof Error && err.message.includes("Cannot find module")) {
        throw new Error(
          `handler 模块未找到: ${handlerName}.js。` +
          `请确保 src/tools/${handlerName}.ts 存在且已编译。`
        );
      }
      throw err;
    }
  }

  /**
   * kebab-case → camelCase 转换
   *
   * 例如: "invoke-ps-script" → "invokePsScript"
   */
  private kebabToCamel(str: string): string {
    return str.replace(/-([a-z])/g, (_, c) => c.toUpperCase());
  }

  // ─── 公共接口 ─────────────────────────────────────────────

  /**
   * 获取所有已注册的工具定义
   */
  getAllDefinitions(): McpToolDefinition[] {
    return Array.from(this.tools.values()).map(t => t.definition);
  }

  /**
   * 检查工具是否已注册
   *
   * @param toolName 工具名称
   */
  hasTool(toolName: string): boolean {
    return this.tools.has(toolName);
  }

  /**
   * 获取已注册的工具名称集合
   */
  getToolNames(): Set<string> {
    return new Set(this.tools.keys());
  }

  /**
   * 执行本地工具
   *
   * @param toolName 工具名称
   * @param args 工具参数
   * @returns 执行结果
   */
  async callTool(toolName: string, args: Record<string, unknown>): Promise<CallToolResult> {
    const registered = this.tools.get(toolName);
    if (!registered) {
      return {
        content: [{ type: "text", text: `❌ 未知本地工具: ${toolName}` }],
        isError: true,
      };
    }

    try {
      const result = await registered.handler(args);
      return {
        content: [{ type: "text", text: result }],
      };
    } catch (err) {
      const errMsg = err instanceof Error ? err.message : String(err);
      return {
        content: [{ type: "text", text: `❌ 本地工具执行错误: ${errMsg}` }],
        isError: true,
      };
    }
  }

  /**
   * 获取注册的工具数量
   */
  get size(): number {
    return this.tools.size;
  }
}

// ═══════════════════════════════════════════════════════════════
// 单例导出
// ═══════════════════════════════════════════════════════════════

/** 全局唯一的本地工具注册表实例 */
export const localToolRegistry = new LocalToolRegistry();
