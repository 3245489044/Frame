import { ToolDefinition, ToolHandler } from "../core/types.js";
import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";

// ═══════════════════════════════════════════════════════════════
// 自动生成文件 — 请勿手动修改
// 生成时间: 2026-06-24T11:32:54.704Z
// 数据源: tools-definitions/*.json
// 生成命令: npm run generate
// ═══════════════════════════════════════════════════════════════

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const handlerMap = new Map<string, ToolHandler>();
const nameMap = new Map<string, string>();

export function registerHandler(name: string, handler: ToolHandler): void {
  handlerMap.set(name, handler);
}

async function tryRegister(
  handlerName: string,
  toolName: string,
  importPath: string
): Promise<void> {
  try {
    const mod = await import(importPath);
    if (mod.handle) {
      registerHandler(handlerName, mod.handle);
      nameMap.set(toolName, handlerName);
    }
  } catch {
    /* 忽略 */
  }
}

export async function registerAllHandlers(): Promise<void> {
  await Promise.all([
    tryRegister("dependencies", "get_dependencies", "./dependencies.js"),
    tryRegister("detail", "get_detail", "./detail.js"),
    tryRegister("layer-map", "get_layer_map", "./layer-map.js"),
    tryRegister("layer", "get_by_layer", "./layer.js"),
    tryRegister("overview", "get_overview", "./overview.js"),
    tryRegister("project-tree", "get_project_tree", "./project-tree.js"),
    tryRegister("search", "search_modules", "./search.js"),
    tryRegister("set-rule", "set_module_rule", "./set-rule.js"),
    tryRegister("validate", "validate_rules", "./validate.js"),
  ]);
}

export function getHandler(name: string): ToolHandler | undefined {
  // 先按 handler name 查找
  const direct = handlerMap.get(name);
  if (direct) return direct;

  // 再按 tool name 查找（通过 nameMap 映射）
  const handlerName = nameMap.get(name);
  if (handlerName) {
    return handlerMap.get(handlerName);
  }

  // 回退到读取 tools.config.json
  try {
    const defs = loadToolDefinitions();
    const found = defs.find((d) => d.name === name);
    if (found && handlerMap.has(found.handler)) {
      nameMap.set(name, found.handler);
      return handlerMap.get(found.handler);
    }
  } catch { /* 忽略 */ }

  return undefined;
}

export function loadToolDefinitions(): ToolDefinition[] {
  const configPath = path.resolve(__dirname, "..", "..", "tools.config.json");
  const raw = fs.readFileSync(configPath, "utf-8");
  const config = JSON.parse(raw);
  return config.tools;
}
