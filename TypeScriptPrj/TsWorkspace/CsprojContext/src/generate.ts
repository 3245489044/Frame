#!/usr/bin/env node

/**
 * CsprojContext — 代码生成器
 *
 * 从 tools-definitions/*.json 生成：
 * 1. tools.config.json — 工具定义聚合文件
 * 2. 更新 registry.ts 中的 handler 注册代码
 *
 * 使用方式：npm run generate
 */

import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const TOOLS_DEF_DIR = path.resolve(__dirname, "..", "tools-definitions");
const TOOLS_CONFIG_PATH = path.resolve(__dirname, "..", "tools.config.json");
const REGISTRY_PATH = path.resolve(__dirname, "tools", "registry.ts");

interface ToolDefinition {
  name: string;
  description: string;
  handler: string;
  template?: string;
  inputSchema: {
    type: string;
    properties: Record<string, unknown>;
    required?: string[];
  };
  implementation?: {
    imports?: Array<{ name: string; from: string }>;
    mainCall?: string;
    emptyCheck?: string;
    outputCall?: string;
  };
}

function generateToolsConfig(): void {
  const files = fs.readdirSync(TOOLS_DEF_DIR).filter((f) => f.endsWith(".json"));
  const tools: ToolDefinition[] = [];

  for (const file of files) {
    const content = fs.readFileSync(path.join(TOOLS_DEF_DIR, file), "utf-8");
    const def = JSON.parse(content) as ToolDefinition;
    tools.push(def);
  }

  const config = {
    _generated: true,
    _generatedAt: new Date().toISOString(),
    _source: "tools-definitions/*.json",
    _command: "npm run generate",
    tools,
  };

  fs.writeFileSync(TOOLS_CONFIG_PATH, JSON.stringify(config, null, 2), "utf-8");
  console.log(`✅ 已生成 tools.config.json (${tools.length} 个工具定义)`);
}

function generateRegistry(): void {
  const files = fs.readdirSync(TOOLS_DEF_DIR).filter((f) => f.endsWith(".json"));
  const handlers: Array<{ handler: string; toolName: string; importPath: string }> = [];

  for (const file of files) {
    const content = fs.readFileSync(path.join(TOOLS_DEF_DIR, file), "utf-8");
    const def = JSON.parse(content) as ToolDefinition;
    handlers.push({
      handler: def.handler,
      toolName: def.name,
      importPath: `./${def.handler}.js`,
    });
  }

  // 生成 registry.ts 内容
  const registryContent = `import { ToolDefinition, ToolHandler } from "../core/types.js";
import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";

// ═══════════════════════════════════════════════════════════════
// 自动生成文件 — 请勿手动修改
// 生成时间: ${new Date().toISOString()}
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
${handlers.map((h) => `    tryRegister("${h.handler}", "${h.toolName}", "${h.importPath}"),`).join("\n")}
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
`;


  fs.writeFileSync(REGISTRY_PATH, registryContent, "utf-8");
  console.log(`✅ 已更新 registry.ts (${handlers.length} 个 handler)`);
}

// ─── 主入口 ─────────────────────────────────────────────────

function main(): void {
  console.log("🚀 CsprojContext 代码生成器\n");

  if (!fs.existsSync(TOOLS_DEF_DIR)) {
    console.error(`❌ tools-definitions 目录不存在: ${TOOLS_DEF_DIR}`);
    process.exit(1);
  }

  generateToolsConfig();
  generateRegistry();

  console.log("\n✨ 生成完成！");
}

main();
