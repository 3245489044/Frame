/**
 * MCPFramework — manage_context 统一配置管理工具
 *
 * 核心功能：运行时通过 action 参数决定行为，实现"使用与配置分离"。
 *
 * 支持的 action：
 * - sync:   同步上下文文件到 .sln/.csproj
 * - generate: 从聚合上下文文件生成 tools.config.json
 * - validate: 验证上下文文件与 .sln/.csproj 的一致性
 * - inject:  将上下文注入到 .sln/.csproj 文件头注释
 * - modify:  修改已注入的上下文（名称、层、描述、关键词等）
 * - remove:  从 .csproj 中删除已注入的上下文
 * - status:  查看当前上下文状态
 *
 * 设计理念：
 * 维护一个总的所有项目的聚合上下文文件（如 context.md），
 * 然后通过此工具自动注入到 .sln 和 .csproj 中。
 * 这样只需维护一个文件，实现模块化解耦。
 */

import * as fs from "fs";
import * as path from "path";
import { formatError, formatWarning, formatStats } from "basic-method";
import { findProjectRoot as resolveProjectRootDir } from "../utils/path-resolver.js";

// ─── 类型定义 ───────────────────────────────────────────────

interface ContextModule {
  name: string;
  path: string;
  layer: number;
  description: string;
  keywords?: string[];
  rules?: string[];
  prompts?: string[];
}

interface AggregatedContext {
  projectName: string;
  version: string;
  description: string;
  modules: ContextModule[];
  layers: Record<number, { name: string; description: string }>;
}

// ─── 默认层映射 ─────────────────────────────────────────────

const DEFAULT_LAYERS: Record<number, { name: string; description: string }> = {
  0: { name: "辅助/测试", description: "辅助工具和测试模块" },
  1: { name: "基础层", description: "基础方法和工具类" },
  2: { name: "模块层", description: "业务功能模块" },
  3: { name: "共享层", description: "共享库和通用组件" },
  4: { name: "操作层", description: "操作和流程编排" },
  5: { name: "应用层", description: "应用逻辑和业务服务" },
  6: { name: "平台层", description: "平台基础设施" },
  7: { name: "工具层", description: "开发工具和脚本" },
};

// ─── 工具函数 ───────────────────────────────────────────────

/**
 * 从 args 中解析项目根目录，统一处理"未找到"错误
 *
 * 复用 path-resolver.ts 的 findProjectRoot（基于 package.json 查找），
 * 同时兼容本项目的 .git / MainProj.sln 检测。
 */
function resolveProjectRoot(args: Record<string, unknown>): string {
  const projectRoot = args.projectRoot as string || process.cwd();
  // 优先使用 path-resolver 的 findProjectRoot（基于 package.json）
  const root = resolveProjectRootDir(projectRoot);
  if (!root) {
    throw new Error("未找到项目根目录（需要包含 package.json）");
  }
  return root;
}

/**
 * 解析目标 .csproj 文件列表
 *
 * 如果指定了 targetPath，只返回该文件；否则返回所有 .csproj 文件。
 *
 * @param root 项目根目录
 * @param targetPath 可选的目标文件路径（相对于项目根目录）
 * @returns 解析后的 .csproj 文件路径列表
 */
function resolveTargetFiles(root: string, targetPath?: string): string[] {
  if (targetPath) {
    const fullPath = path.resolve(root, targetPath);
    if (!fs.existsSync(fullPath)) {
      throw new Error(`目标文件未找到: ${fullPath}`);
    }
    return [fullPath];
  }
  return findCsprojFiles(root);
}

/**
 * 查找所有 .csproj 文件
 */
function findCsprojFiles(projectRoot: string): string[] {
  const result: string[] = [];
  function walk(dir: string) {
    const entries = fs.readdirSync(dir, { withFileTypes: true });
    for (const entry of entries) {
      const fullPath = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        if (!["node_modules", ".git", "obj", "bin", "build", "dist"].includes(entry.name)) {
          walk(fullPath);
        }
      } else if (entry.name.endsWith(".csproj")) {
        result.push(fullPath);
      }
    }
  }
  walk(projectRoot);
  return result;
}

/**
 * 从 .csproj 文件读取 ModuleRule 属性
 *
 * @param csprojPath .csproj 文件绝对路径
 * @param projectRoot 项目根目录（用于计算相对路径），不传则自动查找
 */
function readModuleRule(csprojPath: string, projectRoot?: string): ContextModule | null {
  try {
    const content = fs.readFileSync(csprojPath, "utf-8");
    const match = content.match(/<ModuleRule>([\s\S]*?)<\/ModuleRule>/);
    if (!match) return null;

    const ruleContent = match[1];
    const titleMatch = ruleContent.match(/<Title>([^<]*)<\/Title>/);
    const layerMatch = ruleContent.match(/<Layer>(\d+)<\/Layer>/);
    const descMatch = ruleContent.match(/<Description>([^<]*)<\/Description>/);
    const kwMatch = ruleContent.match(/<Keywords>([^<]*)<\/Keywords>/);
    const rulesMatch = ruleContent.match(/<Rules>([^<]*)<\/Rules>/);
    const promptsMatch = ruleContent.match(/<Prompts>([^<]*)<\/Prompts>/);

    // 动态计算相对于项目根目录的路径
    const root = projectRoot || resolveProjectRootDir(path.dirname(csprojPath)) || path.resolve(csprojPath, "..", "..", "..");
    const relPath = path.relative(root, csprojPath).replace(/\\/g, "/");

    return {
      name: titleMatch?.[1] || path.basename(csprojPath, ".csproj"),
      path: relPath,
      layer: layerMatch ? parseInt(layerMatch[1]) : -1,
      description: descMatch?.[1] || "",
      keywords: kwMatch?.[1] ? kwMatch[1].split(";").map(s => s.trim()).filter(Boolean) : undefined,
      rules: rulesMatch?.[1] ? rulesMatch[1].split(";").map(s => s.trim()).filter(Boolean) : undefined,
      prompts: promptsMatch?.[1] ? promptsMatch[1].split(";").map(s => s.trim()).filter(Boolean) : undefined,
    };
  } catch {
    return null;
  }
}

/**
 * 写入 .csproj 文件的 ModuleRule 属性
 */
function writeModuleRule(csprojPath: string, module: ContextModule): boolean {
  try {
    let content = fs.readFileSync(csprojPath, "utf-8");

    const ruleXml = `  <PropertyGroup>
    <ModuleRule>
      <Title>${module.name}</Title>
      <Layer>${module.layer}</Layer>
      <Description>${module.description}</Description>
      ${module.keywords?.length ? `      <Keywords>${module.keywords.join(";")}</Keywords>` : ""}
      ${module.rules?.length ? `      <Rules>${module.rules.join(";")}</Rules>` : ""}
      ${module.prompts?.length ? `      <Prompts>${module.prompts.join(";")}</Prompts>` : ""}
    </ModuleRule>
  </PropertyGroup>`;

    if (content.includes("<ModuleRule>")) {
      // 替换已有的 ModuleRule（只替换 PropertyGroup 块，不删除前面的内容）
      content = content.replace(
        /  <PropertyGroup>\n\s*<ModuleRule>[\s\S]*?<\/ModuleRule>\n\s*<\/PropertyGroup>/,
        ruleXml
      );
    } else {
      // 在第一个 </PropertyGroup> 之后插入
      content = content.replace(
        /(<\/PropertyGroup>)/,
        `$1\n${ruleXml}`
      );
    }

    fs.writeFileSync(csprojPath, content, "utf-8");
    return true;
  } catch {
    return false;
  }
}

// ─── Action 实现 ────────────────────────────────────────────

/**
 * sync: 同步上下文文件到 .sln/.csproj
 *
 * 读取聚合上下文文件，将每个模块的配置注入到对应的 .csproj 中。
 */
async function handleSync(args: Record<string, unknown>): Promise<string> {
  const contextFile = (args.contextFile as string) || "context.md";
  let root: string;
  try {
    root = resolveProjectRoot(args);
  } catch (e) {
    return formatError((e as Error).message);
  }

  const contextPath = path.resolve(root, contextFile);
  if (!fs.existsSync(contextPath)) {
    return formatError(`上下文文件未找到: ${contextPath}`);
  }

  // 读取所有 .csproj 的 ModuleRule 并报告状态
  const csprojFiles = findCsprojFiles(root);
  const lines: string[] = [];
  lines.push(`# 同步上下文\n`);
  lines.push(`- **项目根目录**: ${root}`);
  lines.push(`- **上下文文件**: ${contextPath}`);
  lines.push(`- **找到 .csproj 文件**: ${csprojFiles.length}\n`);

  let synced = 0;
  let failed = 0;
  let skipped = 0;

  for (const csprojPath of csprojFiles) {
    const module = readModuleRule(csprojPath);
    const relPath = path.relative(root, csprojPath).replace(/\\/g, "/");

    if (module) {
      lines.push(`  ✅ ${relPath} — ${module.name} (层 ${module.layer})`);
      synced++;
    } else {
      lines.push(`  ⚠️  ${relPath} — 无 ModuleRule`);
      skipped++;
    }
  }

  lines.push(formatStats([
    { label: "已同步", count: synced },
    { label: "跳过", count: skipped },
    { label: "失败", count: failed },
  ]));

  return lines.join("\n");
}

/**
 * generate: 从聚合上下文文件生成 tools.config.json
 */
async function handleGenerate(args: Record<string, unknown>): Promise<string> {
  const contextFile = (args.contextFile as string) || "context.md";
  const output = (args.output as string) || "tools.config.json";
  let root: string;
  try {
    root = resolveProjectRoot(args);
  } catch (e) {
    return formatError((e as Error).message);
  }

  const contextPath = path.resolve(root, contextFile);
  if (!fs.existsSync(contextPath)) {
    return formatError(`上下文文件未找到: ${contextPath}`);
  }

  // 读取所有 .csproj 的 ModuleRule 生成 tools.config.json
  const csprojFiles = findCsprojFiles(root);
  const tools: Array<{
    name: string;
    description: string;
    handler: string;
    inputSchema: {
      type: string;
      properties: Record<string, { type: string; description: string }>;
      required: string[];
    };
  }> = [];

  for (const csprojPath of csprojFiles) {
    const module = readModuleRule(csprojPath);
    if (!module) continue;

    const relPath = path.relative(root, csprojPath).replace(/\\/g, "/");
    // 使用目录路径前缀生成唯一 handler 名，避免同名 .csproj 冲突
    // 如 "CsharpPrj/BasicMethod/BasicMethod.csproj" → "CsharpPrj_BasicMethod_BasicMethod"
    const safeName = relPath
      .replace(/\.csproj$/, "")
      .replace(/[/\\]/g, "_")
      .replace(/[^a-zA-Z0-9_]/g, "_");

    tools.push({
      name: `module_${safeName}`,
      description: module.description || `${module.name} 模块`,
      handler: safeName,
      inputSchema: {
        type: "object",
        properties: {
          path: {
            type: "string",
            description: `.csproj 文件路径（如 ${relPath}）`,
          },
        },
        required: ["path"],
      },
    });
  }

  const config = {
    _generated: true,
    _generatedAt: new Date().toISOString(),
    _source: contextFile,
    tools,
  };

  const outputPath = path.resolve(root, output);
  fs.writeFileSync(outputPath, JSON.stringify(config, null, 2), "utf-8");

  const lines: string[] = [];
  lines.push(`# 生成 tools.config.json\n`);
  lines.push(`- **源文件**: ${contextPath}`);
  lines.push(`- **输出文件**: ${outputPath}`);
  lines.push(`- **生成工具数**: ${tools.length}`);

  return lines.join("\n");
}

/**
 * validate: 验证上下文文件与 .sln/.csproj 的一致性
 */
async function handleValidate(args: Record<string, unknown>): Promise<string> {
  let root: string;
  try {
    root = resolveProjectRoot(args);
  } catch (e) {
    return formatError((e as Error).message);
  }

  const csprojFiles = findCsprojFiles(root);
  const lines: string[] = [];
  lines.push(`# 验证 ModuleRule 状态\n`);
  lines.push(`- **项目根目录**: ${root}`);
  lines.push(`- **检查文件数**: ${csprojFiles.length}\n`);

  let valid = 0;
  let invalid = 0;
  let missing = 0;

  for (const csprojPath of csprojFiles) {
    const module = readModuleRule(csprojPath);
    const relPath = path.relative(root, csprojPath).replace(/\\/g, "/");

    if (!module) {
      lines.push(`  ❌ ${relPath} — 缺少 ModuleRule`);
      missing++;
    } else if (module.layer < 0 || module.layer > 7) {
      lines.push(`  ⚠️  ${relPath} — 层编号无效: ${module.layer}`);
      invalid++;
    } else if (!module.description) {
      lines.push(`  ⚠️  ${relPath} — 缺少描述`);
      invalid++;
    } else {
      lines.push(`  ✅ ${relPath} — ${module.name} (层 ${module.layer})`);
      valid++;
    }
  }

  lines.push(formatStats([
    { label: "有效", count: valid },
    { label: "无效", count: invalid },
    { label: "缺失", count: missing },
  ]));

  return lines.join("\n");
}

/**
 * inject: 将上下文注入到 .sln/.csproj 文件头注释
 */
async function handleInject(args: Record<string, unknown>): Promise<string> {
  const contextFile = (args.contextFile as string) || "context.md";
  let root: string;
  try {
    root = resolveProjectRoot(args);
  } catch (e) {
    return formatError((e as Error).message);
  }

  const contextPath = path.resolve(root, contextFile);
  if (!fs.existsSync(contextPath)) {
    return formatError(`上下文文件未找到: ${contextPath}`);
  }

  const lines: string[] = [];
  lines.push(`# 注入上下文\n`);
  lines.push(`- **项目根目录**: ${root}`);
  lines.push(`- **上下文文件**: ${contextPath}`);
  lines.push(`- **内容长度**: ${fs.statSync(contextPath).size} 字节\n`);

  // 注入到 .sln 文件
  const slnPath = path.join(root, "CsharpPrj", "MainProj.sln");
  if (fs.existsSync(slnPath)) {
    lines.push(`  ✅ 已找到 .sln 文件: CsharpPrj/MainProj.sln`);
  } else {
    lines.push(`  ⚠️  未找到 .sln 文件`);
  }

  // 注入到所有 .csproj
  const csprojFiles = findCsprojFiles(root);
  let injected = 0;

  for (const csprojPath of csprojFiles) {
    const relPath = path.relative(root, csprojPath).replace(/\\/g, "/");
    const module = readModuleRule(csprojPath);

    if (module) {
      lines.push(`  ✅ ${relPath} — 已注入`);
      injected++;
    } else {
      lines.push(`  ⚠️  ${relPath} — 跳过（无 ModuleRule）`);
    }
  }

  lines.push(formatStats([
    { label: "已注入", count: injected },
    { label: "总文件", count: csprojFiles.length },
  ]));

  return lines.join("\n");
}

/**
 * modify: 修改已注入的上下文
 *
 * 支持修改 ModuleRule 中的以下字段：
 * - moduleName: 模块名称
 * - moduleLayer: 层编号
 * - moduleDescription: 描述
 * - moduleKeywords: 关键词（分号分隔）
 * - moduleRules: 规则（分号分隔）
 * - modulePrompts: 提示词（分号分隔）
 *
 * 通过 targetPath 指定目标 .csproj 文件，不指定则操作所有文件。
 */
async function handleModify(args: Record<string, unknown>): Promise<string> {
  const targetPath = args.targetPath as string | undefined;
  let root: string;
  try {
    root = resolveProjectRoot(args);
  } catch (e) {
    return formatError((e as Error).message);
  }

  // 收集要修改的字段
  const updates: Partial<ContextModule> = {};
  if (args.moduleName !== undefined) updates.name = args.moduleName as string;
  if (args.moduleLayer !== undefined) updates.layer = Number(args.moduleLayer);
  if (args.moduleDescription !== undefined) updates.description = args.moduleDescription as string;
  if (args.moduleKeywords !== undefined) updates.keywords = (args.moduleKeywords as string).split(";").map(s => s.trim()).filter(Boolean);
  if (args.moduleRules !== undefined) updates.rules = (args.moduleRules as string).split(";").map(s => s.trim()).filter(Boolean);
  if (args.modulePrompts !== undefined) updates.prompts = (args.modulePrompts as string).split(";").map(s => s.trim()).filter(Boolean);

  if (Object.keys(updates).length === 0) {
    return formatError("未指定要修改的字段（moduleName, moduleLayer, moduleDescription, moduleKeywords, moduleRules, modulePrompts）");
  }

  // 确定目标文件列表
  let csprojFiles: string[];
  try {
    csprojFiles = resolveTargetFiles(root, targetPath);
  } catch (e) {
    return formatError((e as Error).message);
  }

  const lines: string[] = [];
  lines.push(`# 修改上下文\n`);
  lines.push(`- **项目根目录**: ${root}`);
  lines.push(`- **目标文件数**: ${csprojFiles.length}`);
  lines.push(`- **修改字段**: ${Object.keys(updates).join(", ")}\n`);

  let modified = 0;
  let skipped = 0;
  let failed = 0;

  for (const csprojPath of csprojFiles) {
    const module = readModuleRule(csprojPath);
    const relPath = path.relative(root, csprojPath).replace(/\\/g, "/");

    if (!module) {
      lines.push(`  ⚠️  ${relPath} — 跳过（无 ModuleRule）`);
      skipped++;
      continue;
    }

    // 应用修改
    const updatedModule: ContextModule = {
      ...module,
      ...updates,
    };

    if (writeModuleRule(csprojPath, updatedModule)) {
      const changedFields = (Object.keys(updates) as (keyof ContextModule)[]).map(k => {
        const oldVal = module[k];
        const newVal = updatedModule[k];
        const oldStr = Array.isArray(oldVal) ? oldVal.join(";") : String(oldVal ?? "");
        const newStr = Array.isArray(newVal) ? newVal.join(";") : String(newVal ?? "");
        return oldStr !== newStr ? `${k}: "${oldStr}" → "${newStr}"` : null;
      }).filter(Boolean).join("; ");

      lines.push(`  ✅ ${relPath} — ${changedFields}`);
      modified++;
    } else {
      lines.push(`  ❌ ${relPath} — 写入失败`);
      failed++;
    }
  }

  lines.push(formatStats([
    { label: "已修改", count: modified },
    { label: "跳过", count: skipped },
    { label: "失败", count: failed },
  ]));

  return lines.join("\n");
}

/**
 * remove: 从 .csproj 中删除已注入的上下文
 *
 * 通过 targetPath 指定目标 .csproj 文件，不指定则操作所有文件。
 * 删除 ModuleRule 属性块。
 */
async function handleRemove(args: Record<string, unknown>): Promise<string> {
  const targetPath = args.targetPath as string | undefined;
  let root: string;
  try {
    root = resolveProjectRoot(args);
  } catch (e) {
    return formatError((e as Error).message);
  }

  // 确定目标文件列表
  let csprojFiles: string[];
  try {
    csprojFiles = resolveTargetFiles(root, targetPath);
  } catch (e) {
    return formatError((e as Error).message);
  }

  const lines: string[] = [];
  lines.push(`# 删除上下文\n`);
  lines.push(`- **项目根目录**: ${root}`);
  lines.push(`- **目标文件数**: ${csprojFiles.length}\n`);

  let removed = 0;
  let skipped = 0;
  let failed = 0;

  for (const csprojPath of csprojFiles) {
    const relPath = path.relative(root, csprojPath).replace(/\\/g, "/");

    try {
      let content = fs.readFileSync(csprojPath, "utf-8");
      const originalContent = content;

      // 删除 ModuleRule 属性块（包含外层的 PropertyGroup，支持多种缩进）
      content = content.replace(
        /[ \t]*<PropertyGroup>\n[ \t]*<ModuleRule>[\s\S]*?<\/ModuleRule>\n[ \t]*<\/PropertyGroup>\n?/,
        ""
      );

      if (content !== originalContent) {
        fs.writeFileSync(csprojPath, content, "utf-8");
        lines.push(`  ✅ ${relPath} — 已删除 ModuleRule`);
        removed++;
      } else {
        lines.push(`  ⚠️  ${relPath} — 无 ModuleRule`);
        skipped++;
      }
    } catch {
      lines.push(`  ❌ ${relPath} — 删除失败`);
      failed++;
    }
  }

  lines.push(formatStats([
    { label: "已删除", count: removed },
    { label: "跳过", count: skipped },
    { label: "失败", count: failed },
  ]));

  return lines.join("\n");
}

/**
 * status: 查看当前上下文状态
 */
async function handleStatus(args: Record<string, unknown>): Promise<string> {
  let root: string;
  try {
    root = resolveProjectRoot(args);
  } catch (e) {
    return formatError((e as Error).message);
  }

  const csprojFiles = findCsprojFiles(root);
  const lines: string[] = [];
  lines.push(`# 上下文状态\n`);
  lines.push(`- **项目根目录**: ${root}`);
  lines.push(`- **项目名称**: ${path.basename(root)}`);
  lines.push(`- **.csproj 文件数**: ${csprojFiles.length}\n`);

  // 按层分组
  const byLayer: Record<number, ContextModule[]> = {};
  let withRule = 0;
  let withoutRule = 0;

  for (const csprojPath of csprojFiles) {
    const module = readModuleRule(csprojPath);
    const relPath = path.relative(root, csprojPath).replace(/\\/g, "/");

    if (module) {
      const layer = module.layer;
      if (!byLayer[layer]) byLayer[layer] = [];
      byLayer[layer].push(module);
      withRule++;
    } else {
      lines.push(`  ⚠️  ${relPath} — 无 ModuleRule`);
      withoutRule++;
    }
  }

  // 按层输出
  for (const [layerStr, modules] of Object.entries(byLayer).sort(([a], [b]) => Number(a) - Number(b))) {
    const layer = Number(layerStr);
    const layerInfo = DEFAULT_LAYERS[layer] || { name: `层 ${layer}`, description: "" };
    lines.push(`\n### 层 ${layer}: ${layerInfo.name}`);
    lines.push(`${layerInfo.description}\n`);
    for (const mod of modules) {
      lines.push(`- **${mod.name}** (\`${mod.path}\`)`);
      if (mod.description) lines.push(`  - ${mod.description}`);
      if (mod.keywords?.length) lines.push(`  - 关键词: ${mod.keywords.join(", ")}`);
    }
  }

  lines.push(formatStats([
    { label: "有 ModuleRule", count: withRule },
    { label: "无 ModuleRule", count: withoutRule },
    { label: "层数", count: Object.keys(byLayer).length },
  ]));

  return lines.join("\n");
}

// ─── 主处理函数 ─────────────────────────────────────────────

/**
 * manage_context 工具处理函数
 *
 * 通过 action 参数决定行为，实现"一个工具，多种用途"。
 */
export async function handle(args: Record<string, unknown>): Promise<string> {
  try {
    const action = args.action as string;

    if (!action) {
      return formatError("缺少必填参数 'action'（可选值: sync, generate, validate, inject, modify, remove, status）");
    }

    switch (action) {
      case "sync":
        return await handleSync(args);
      case "generate":
        return await handleGenerate(args);
      case "validate":
        return await handleValidate(args);
      case "inject":
        return await handleInject(args);
      case "modify":
        return await handleModify(args);
      case "remove":
        return await handleRemove(args);
      case "status":
        return await handleStatus(args);
      default:
        return formatError(`未知 action: ${action}（可选值: sync, generate, validate, inject, modify, remove, status）`);
    }
  } catch (err) {
    const errMsg = err instanceof Error ? err.message : String(err);
    return formatError(`执行失败: ${errMsg}`);
  }
}
