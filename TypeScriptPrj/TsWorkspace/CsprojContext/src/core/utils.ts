import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";
import { LayerInfo, ModuleRule } from "./types.js";
import { getCsprojPathsFromSln, resolveProjectRootFromSln } from "./sln-parser.js";

// ─── ESM 兼容的 __dirname ───────────────────────────────

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// ─── 缓存 ───────────────────────────────────────────────

let _layersCache: LayerInfo[] | null = null;
let _defaultsCache: { defaultProjectRoot: string; excludePatterns: string[] } | null = null;
let _csprojCache: { root: string; excludeKey: string; files: string[]; timestamp: number } | null = null;
const CACHE_TTL_MS = 30000; // 缓存有效期 30 秒

// ─── 项目根目录 ─────────────────────────────────────────

/** 获取项目根目录（CsprojContext 目录） */
function getProjectRoot(): string {
  // __dirname 在 build 中为 build/core/，向上两级到项目根
  const candidate = path.resolve(__dirname, "..", "..");
  // 检查 src/config 是否存在
  if (fs.existsSync(path.join(candidate, "src", "config"))) {
    return candidate;
  }
  // 回退：可能是从 src 直接运行
  const candidate2 = path.resolve(__dirname, "..");
  if (fs.existsSync(path.join(candidate2, "src", "config"))) {
    return candidate2;
  }
  // 最终回退到当前目录
  return process.cwd();
}

// ─── 层定义（带缓存）────────────────────────────────────

/** 获取层定义 */
export function getLayers(): LayerInfo[] {
  if (_layersCache) return _layersCache;
  const layersPath = path.join(getProjectRoot(), "src", "config", "layers.json");
  const raw = fs.readFileSync(layersPath, "utf-8");
  _layersCache = JSON.parse(raw).layers;
  return _layersCache!;
}

/** 获取层名称 */
export function getLayerName(layerId: number): string {
  const layers = getLayers();
  const found = layers.find((l) => l.id === layerId);
  return found ? found.name : `未知层 (${layerId})`;
}

// ─── 默认配置（带缓存）──────────────────────────────────

function loadDefaults(): { defaultProjectRoot: string; excludePatterns: string[] } {
  if (_defaultsCache) return _defaultsCache;
  const defaultsPath = path.join(getProjectRoot(), "src", "config", "defaults.json");
  const raw = fs.readFileSync(defaultsPath, "utf-8");
  _defaultsCache = JSON.parse(raw);
  return _defaultsCache!;
}

/** 获取默认项目根目录 */
export function getDefaultProjectRoot(): string {
  return loadDefaults().defaultProjectRoot;
}

/** 获取排除模式 */
export function getExcludePatterns(): string[] {
  return loadDefaults().excludePatterns;
}

/** 解析项目根目录（使用传入值或默认值） */
export function resolveProjectRoot(projectRoot?: string): string {
  return projectRoot || getDefaultProjectRoot();
}

/**
 * 解析 slnPath 参数，返回项目根目录
 *
 * 当提供 slnPath 时，从 .sln 文件推导 projectRoot；
 * 否则使用传入的 projectRoot 或默认值。
 *
 * @param slnPath .sln 文件路径（可选）
 * @param projectRoot 项目根目录（可选，slnPath 优先）
 * @returns 项目根目录绝对路径
 */
export function resolveProjectRootWithSln(
  slnPath?: string,
  projectRoot?: string
): string {
  if (slnPath) {
    return resolveProjectRootFromSln(slnPath);
  }
  return resolveProjectRoot(projectRoot);
}

/**
 * 从工具参数中提取 slnPath 和 projectRoot
 *
 * 消除 9 个 handler 中重复的解析代码：
 *   const slnPath = args.slnPath as string | undefined;
 *   const projectRoot = resolveProjectRootWithSln(slnPath, args.projectRoot as string | undefined);
 *
 * @param args 工具调用参数
 * @returns 解析后的项目根目录绝对路径
 */
export function extractSlnAndRoot(args: Record<string, unknown>): string {
  const slnPath = args.slnPath as string | undefined;
  return resolveProjectRootWithSln(slnPath, args.projectRoot as string | undefined);
}

/**
 * 获取 .csproj 文件列表
 *
 * 当提供 slnPath 时，只返回 .sln 中注册的 .csproj 文件；
 * 否则递归扫描 projectRoot 下的所有 .csproj 文件。
 *
 * @param rootDir 项目根目录
 * @param slnPath .sln 文件路径（可选）
 * @returns .csproj 文件路径列表（相对于 rootDir）
 */
export function findCsprojFiles(rootDir: string, slnPath?: string): string[] {
  if (slnPath) {
    // 从 .sln 中获取项目列表
    return getCsprojPathsFromSln(slnPath, rootDir);
  }
  // 原有的递归扫描逻辑
  return findCsprojFilesRecursive(rootDir);
}

/**
 * 递归查找所有 .csproj 文件（带缓存）
 */
function findCsprojFilesRecursive(rootDir: string): string[] {
  const now = Date.now();
  const excludePatterns = getExcludePatterns();
  const excludeKey = excludePatterns.join(",");

  if (
    _csprojCache &&
    _csprojCache.root === rootDir &&
    _csprojCache.excludeKey === excludeKey &&
    (now - _csprojCache.timestamp) < CACHE_TTL_MS
  ) {
    return _csprojCache.files;
  }

  const results: string[] = [];

  function walk(dir: string) {
    try {
      const entries = fs.readdirSync(dir, { withFileTypes: true });
      for (const entry of entries) {
        if (shouldExclude(entry.name, excludePatterns)) continue;
        const fullPath = path.join(dir, entry.name);
        if (entry.isDirectory()) {
          walk(fullPath);
        } else if (entry.isFile() && entry.name.endsWith(".csproj")) {
          results.push(fullPath);
        }
      }
    } catch {
      // 跳过无权限目录
    }
  }

  walk(rootDir);
  _csprojCache = { root: rootDir, excludeKey, files: results, timestamp: now };
  return results;
}

// ─── 文件系统工具 ───────────────────────────────────────

/** 检查路径是否应被排除（精确匹配目录名） */
export function shouldExclude(dirName: string, excludePatterns: string[]): boolean {
  return excludePatterns.some((p) => dirName === p);
}

/** 清除文件系统缓存（用于测试或强制刷新） */
export function clearCache(): void {
  _layersCache = null;
  _defaultsCache = null;
  _csprojCache = null;
}

// ─── 分组工具 ───────────────────────────────────────────

/** 按层分组模块 */
export function groupModulesByLayer(
  modules: ModuleRule[]
): { layerId: number; layerName: string; modules: ModuleRule[] }[] {
  const grouped = new Map<number, ModuleRule[]>();
  for (const mod of modules) {
    const existing = grouped.get(mod.layer) || [];
    existing.push(mod);
    grouped.set(mod.layer, existing);
  }
  return [...grouped.entries()]
    .sort(([a], [b]) => a - b)
    .map(([layerId, mods]) => ({
      layerId,
      layerName: getLayerName(layerId),
      modules: mods,
    }));
}

// ─── 格式化输出 ─────────────────────────────────────────

/** 格式化输出（Markdown 风格） */
export function formatOverview(
  modules: ModuleRule[],
  detail: boolean
): string {
  const grouped = groupModulesByLayer(modules);
  const lines: string[] = [];

  lines.push("# 项目架构概览\n");

  for (const { layerId, layerName, modules: mods } of grouped) {
    lines.push(`## [层 ${layerId}] ${layerName}（${mods.length} 个项目）\n`);

    for (const mod of mods) {
      lines.push(`- **${mod.title}** — \`${mod.path}\``);
      if (detail) {
        lines.push(`  - 描述: ${mod.description}`);
        if (mod.keywords) lines.push(`  - 关键词: ${mod.keywords}`);
        if (mod.rules.length > 0) lines.push(`  - 规则: ${mod.rules.join("; ")}`);
        if (mod.prompts.length > 0) lines.push(`  - 提示: ${mod.prompts.join("; ")}`);
      } else {
        lines.push(`  - ${mod.description}`);
      }
      lines.push("");
    }
  }

  lines.push(`---\n总计: **${modules.length}** 个项目已配置 ModuleRule 属性`);
  return lines.join("\n");
}

/** 格式化模块详情（Markdown） */
export function formatDetailResult(
  module: ModuleRule,
  getLayerNameFn: (id: number) => string
): string {
  const layerName = getLayerNameFn(module.layer);
  const lines: string[] = [];
  lines.push(`# 模块详情: ${module.title}\n`);
  lines.push(`- **文件路径**: \`${module.path}\``);
  lines.push(`- **层**: [${module.layer}] ${layerName}`);
  lines.push(`- **描述**: ${module.description}`);
  if (module.keywords) {
    lines.push(`- **关键词**: ${module.keywords}`);
  }
  if (module.rules.length > 0) {
    lines.push(`\n### 规则`);
    module.rules.forEach((r) => lines.push(`- ${r}`));
  }
  if (module.prompts.length > 0) {
    lines.push(`\n### 提示词`);
    module.prompts.forEach((p) => lines.push(`- ${p}`));
  }
  return lines.join("\n");
}

/** 格式化层内模块列表（Markdown） */
export function formatLayerResults(
  modules: ModuleRule[],
  layer: number,
  getLayerNameFn: (id: number) => string
): string {
  const layerName = getLayerNameFn(layer);
  const lines: string[] = [];
  lines.push(`# 📂 [层 ${layer}] ${layerName}（${modules.length} 个项目）\n`);
  for (const mod of modules) {
    lines.push(`- **${mod.title}** — \`${mod.path}\``);
    lines.push(`  - ${mod.description}`);
    if (mod.keywords) lines.push(`  - 关键词: ${mod.keywords}`);
    lines.push("");
  }
  return lines.join("\n");
}

/** 格式化搜索结果（Markdown） */
export function formatSearchResults(
  results: ModuleRule[],
  keyword: string,
  getLayerNameFn: (id: number) => string
): string {
  const lines: string[] = [];
  lines.push(`# 🔍 搜索 "${keyword}" 找到 ${results.length} 个匹配项目\n`);
  for (const mod of results) {
    const layerName = getLayerNameFn(mod.layer);
    lines.push(`- **[${mod.layer}]** \`${mod.path}\` → **${mod.title}**`);
    lines.push(`  - ${mod.description}`);
    lines.push(`  - 层: ${layerName}`);
    if (mod.keywords) lines.push(`  - 关键词: ${mod.keywords}`);
    lines.push("");
  }
  return lines.join("\n");
}

/** 格式化依赖关系结果（Markdown） */
export function formatDependenciesResult(
  result: { path: string; title: string; dependencies: string[]; dependencyCount: number } | null
): string {
  if (!result) {
    return "⚠️ 文件不存在或无法读取";
  }
  const lines: string[] = [];
  lines.push(`# 依赖关系: ${result.title}\n`);
  lines.push(`- **文件路径**: \`${result.path}\``);
  lines.push(`- **直接依赖数**: ${result.dependencyCount}`);
  if (result.dependencies.length > 0) {
    lines.push(`\n### 依赖列表`);
    result.dependencies.forEach((dep) => lines.push(`- \`${dep}\``));
  } else {
    lines.push(`\n_无 ProjectReference 依赖_`);
  }
  return lines.join("\n");
}

/** 格式化层映射表（Markdown） */
export function formatLayerMap(layers: { id: number; name: string; description: string }[]): string {
  const lines: string[] = [];
  lines.push(`# 层编号映射表\n`);
  lines.push(`| 编号 | 层名称 | 说明 |`);
  lines.push(`|------|--------|------|`);
  for (const layer of layers) {
    lines.push(`| ${layer.id} | ${layer.name} | ${layer.description} |`);
  }
  lines.push(``);
  lines.push(`> 层编号约定：编号越小越基础，上层依赖下层。`);
  return lines.join("\n");
}
