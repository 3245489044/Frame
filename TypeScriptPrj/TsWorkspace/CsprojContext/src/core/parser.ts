import * as fs from "fs";
import * as path from "path";
import { ModuleRule } from "./types.js";
import { findCsprojFiles, resolveProjectRoot, resolveProjectRootWithSln } from "./utils.js";

/** 从 .csproj 文件内容中提取 ModuleRule 属性 */
export function parseModuleRule(filePath: string, projectRoot: string): ModuleRule | null {
  try {
    const content = fs.readFileSync(filePath, "utf-8");

    // 快速检查是否包含 ModuleRule
    if (!content.includes('Label="ModuleRule"')) return null;

    const relativePath = path.relative(projectRoot, filePath);
    const result: ModuleRule = {
      path: relativePath,
      title: "",
      layer: -1,
      description: "",
      keywords: "",
      rules: [],
      prompts: [],
      hasModuleRule: true,
    };

    // 使用正则提取（比 XML 解析更可靠，因为 .csproj 不是标准 XML）
    const titleMatch = content.match(/<ModuleRule_Title>(.*?)<\/ModuleRule_Title>/);
    if (titleMatch) result.title = titleMatch[1];

    const layerMatch = content.match(/<ModuleRule_Layer>(.*?)<\/ModuleRule_Layer>/);
    if (layerMatch) result.layer = parseInt(layerMatch[1], 10);

    const descMatch = content.match(/<ModuleRule_Description>(.*?)<\/ModuleRule_Description>/);
    if (descMatch) result.description = descMatch[1];

    const kwMatch = content.match(/<ModuleRule_Keywords>(.*?)<\/ModuleRule_Keywords>/);
    if (kwMatch) result.keywords = kwMatch[1];

    // 提取 Rules
    const rulesRegex = /<ModuleRule_Rule Include="(.*?)"/g;
    let ruleMatch;
    while ((ruleMatch = rulesRegex.exec(content)) !== null) {
      result.rules.push(ruleMatch[1]);
    }

    // 提取 Prompts
    const promptsRegex = /<ModuleRule_Prompt Include="(.*?)"/g;
    let promptMatch;
    while ((promptMatch = promptsRegex.exec(content)) !== null) {
      result.prompts.push(promptMatch[1]);
    }

    return result;
  } catch (err) {
    console.error(`[parser] 解析失败: ${filePath}`, err);
    return null;
  }
}

/**
 * 解析文件路径，支持大小写不敏感回退
 *
 * @param file 相对或绝对路径
 * @param root 项目根目录
 * @returns 解析后的绝对路径
 */
function resolveFilePath(file: string, root: string): string {
  const fullPath = path.isAbsolute(file) ? file : path.resolve(root, file);
  if (fs.existsSync(fullPath)) return fullPath;

  // 精确路径不存在时，尝试大小写不敏感查找
  const relativePath = path.isAbsolute(file) ? path.relative(root, file) : file;
  const caseInsensitivePath = findFileCaseInsensitive(root, relativePath);
  return caseInsensitivePath || fullPath;
}

/**
 * 获取所有包含 ModuleRule 的模块
 *
 * @param projectRoot 项目根目录
 * @param slnPath .sln 文件路径（可选，提供时只扫描 .sln 中注册的项目）
 */
export function getAllModuleRules(projectRoot?: string, slnPath?: string): ModuleRule[] {
  const root = resolveProjectRootWithSln(slnPath, projectRoot);
  const csprojFiles = findCsprojFiles(root, slnPath);
  const results: ModuleRule[] = [];

  for (const file of csprojFiles) {
    // file 可能是相对路径（来自 .sln）或绝对路径（来自递归扫描）
    const fullPath = resolveFilePath(file, root);
    const rule = parseModuleRule(fullPath, root);
    if (rule) {
      results.push(rule);
    }
  }

  return results;
}

/**
 * 大小写不敏感的文件查找
 *
 * 在 Docker Linux 容器中文件系统区分大小写，但 .sln 中的路径可能来自 Windows（大小写不敏感）。
 * 当精确路径不存在时，通过目录遍历进行大小写不敏感的匹配查找。
 *
 * @param basePath 基础目录
 * @param relativePath 相对路径（可能大小写不匹配）
 * @returns 找到的准确路径，或 null
 */
function findFileCaseInsensitive(basePath: string, relativePath: string): string | null {
  const parts = relativePath.split(/[\/\\]/).filter(Boolean);
  let current = basePath;

  for (const part of parts) {
    try {
      const entries = fs.readdirSync(current, { withFileTypes: true });
      const found = entries.find(
        (e) => e.name.toLowerCase() === part.toLowerCase()
      );
      if (!found) return null;
      current = path.join(current, found.name);
    } catch {
      return null;
    }
  }
  return current;
}

/**
 * 获取单个模块详情
 *
 * @param relativePath .csproj 文件路径（相对于项目根目录）
 * @param projectRoot 项目根目录
 * @param slnPath .sln 文件路径（可选）
 */
export function getModuleDetail(relativePath: string, projectRoot?: string, slnPath?: string): ModuleRule | null {
  const root = resolveProjectRootWithSln(slnPath, projectRoot);
  const fullPath = resolveFilePath(relativePath, root);

  if (!fs.existsSync(fullPath)) return null;
  return parseModuleRule(fullPath, root);
}

/**
 * 按关键词搜索模块
 *
 * @param keyword 搜索关键词
 * @param projectRoot 项目根目录
 * @param slnPath .sln 文件路径（可选）
 */
export function searchModules(keyword: string, projectRoot?: string, slnPath?: string): ModuleRule[] {
  const all = getAllModuleRules(projectRoot, slnPath);
  const kw = keyword.toLowerCase();

  return all.filter(
    (m) =>
      m.title.toLowerCase().includes(kw) ||
      m.description.toLowerCase().includes(kw) ||
      m.keywords.toLowerCase().includes(kw) ||
      m.path.toLowerCase().includes(kw)
  );
}

/**
 * 按层筛选模块
 *
 * @param layer 层编号
 * @param projectRoot 项目根目录
 * @param slnPath .sln 文件路径（可选）
 */
export function getModulesByLayer(layer: number, projectRoot?: string, slnPath?: string): ModuleRule[] {
  const all = getAllModuleRules(projectRoot, slnPath);
  return all.filter((m) => m.layer === layer);
}

/** 从 .csproj 内容中提取 ProjectReference 依赖 */
function extractProjectReferences(content: string): string[] {
  const refRegex = /<ProjectReference Include="([^"]+)"/g;
  const deps: string[] = [];
  let match;
  while ((match = refRegex.exec(content)) !== null) {
    deps.push(match[1]);
  }
  return deps;
}

/**
 * 递归获取模块的 ProjectReference 依赖
 *
 * @param relativePath .csproj 文件路径（相对于项目根目录）
 * @param projectRoot 项目根目录
 * @param depth 递归深度
 * @param slnPath .sln 文件路径（可选）
 */
export function getDependencies(
  relativePath: string,
  projectRoot?: string,
  depth: number = 1,
  slnPath?: string
): { path: string; title: string; dependencies: string[]; dependencyCount: number } | null {
  const root = resolveProjectRootWithSln(slnPath, projectRoot);
  const fullPath = resolveFilePath(relativePath, root);

  if (!fs.existsSync(fullPath)) return null;

  try {
    const content = fs.readFileSync(fullPath, "utf-8");
    const rawDeps = extractProjectReferences(content);

    // 直接依赖去重（防止 .csproj 中重复引用同一项目）
    const directDeps = [...new Set(rawDeps)];

    // 递归解析传递依赖（depth > 1 时）
    // 使用 Set 确保去重，且只收集传递依赖（不含直接依赖）
    const transitiveDeps = new Set<string>();
    if (depth > 1) {
      for (const dep of directDeps) {
        const depFullPath = path.resolve(path.dirname(fullPath), dep);
        if (fs.existsSync(depFullPath)) {
          const subResult = getDependencies(
            path.relative(root, depFullPath),
            root,
            depth - 1,
            undefined // 递归时 slnPath 不再需要，root 已确定
          );
          if (subResult) {
            for (const subDep of subResult.dependencies) {
              // 只添加不在直接依赖中的传递依赖
              if (!directDeps.includes(subDep)) {
                transitiveDeps.add(subDep);
              }
            }
          }
        }
      }
    }

    // 合并直接依赖 + 传递依赖（去重）
    const allDeps = [...directDeps, ...transitiveDeps];

    // 获取模块标题
    const moduleRule = parseModuleRule(fullPath, root);

    return {
      path: relativePath,
      title: moduleRule?.title || path.basename(relativePath, ".csproj"),
      dependencies: allDeps,
      dependencyCount: allDeps.length,
    };
  } catch (err) {
    console.error(`[parser] 获取依赖失败: ${relativePath}`, err);
    return null;
  }
}

/**
 * 注入/更新 ModuleRule 属性到 .csproj 文件
 *
 * @param relativePath .csproj 文件路径（相对于项目根目录）
 * @param options 模块属性
 * @param projectRoot 项目根目录
 * @param slnPath .sln 文件路径（可选）
 */
export function setModuleRule(
  relativePath: string,
  options: {
    title: string;
    layer: number;
    description: string;
    keywords?: string;
    rules?: string;
    prompts?: string;
  },
  projectRoot?: string,
  slnPath?: string
): { success: boolean; message: string } {
  const root = resolveProjectRootWithSln(slnPath, projectRoot);
  const fullPath = resolveFilePath(relativePath, root);

  if (!fs.existsSync(fullPath)) {
    return { success: false, message: `文件不存在: ${relativePath}` };
  }

  try {
    let content = fs.readFileSync(fullPath, "utf-8");

    // 移除旧的 ModuleRule 属性
    content = content.replace(/\s*<PropertyGroup Label="ModuleRule">[\s\S]*?<\/PropertyGroup>/, "");
    content = content.replace(/\s*<ItemGroup Label="ModuleRule_Rules">[\s\S]*?<\/ItemGroup>/, "");
    content = content.replace(/\s*<ItemGroup Label="ModuleRule_Prompts">[\s\S]*?<\/ItemGroup>/, "");

    // 构建新的 ModuleRule XML
    const sb: string[] = [];
    sb.push(`  <PropertyGroup Label="ModuleRule">`);
    sb.push(`    <ModuleRule_Title>${options.title}</ModuleRule_Title>`);
    sb.push(`    <ModuleRule_Layer>${options.layer}</ModuleRule_Layer>`);
    sb.push(`    <ModuleRule_Description>${options.description}</ModuleRule_Description>`);
    if (options.keywords) sb.push(`    <ModuleRule_Keywords>${options.keywords}</ModuleRule_Keywords>`);
    sb.push(`  </PropertyGroup>`);

    if (options.rules) {
      sb.push(``);
      sb.push(`  <ItemGroup Label="ModuleRule_Rules">`);
      for (const rule of options.rules.split(";").map((r) => r.trim()).filter(Boolean)) {
        sb.push(`    <ModuleRule_Rule Include="${rule}" />`);
      }
      sb.push(`  </ItemGroup>`);
    }

    if (options.prompts) {
      sb.push(``);
      sb.push(`  <ItemGroup Label="ModuleRule_Prompts">`);
      for (const prompt of options.prompts.split(";").map((p) => p.trim()).filter(Boolean)) {
        sb.push(`    <ModuleRule_Prompt Include="${prompt}" />`);
      }
      sb.push(`  </ItemGroup>`);
    }

    const moduleRuleXml = sb.join("\n");

    // 在 <Project> 标签之后、第一个子元素之前插入
    // 使用更健壮的方式：找到 <Project ...> 的结束位置
    // 使用行首锚定 + 非贪婪匹配，避免属性值中的 > 导致误匹配
    const projectTagMatch = content.match(/^[\s\S]*?<Project[\s\S]*?>/m);
    if (projectTagMatch) {
      const insertPos = projectTagMatch.index! + projectTagMatch[0].length;
      content = content.slice(0, insertPos) + "\n\n" + moduleRuleXml + content.slice(insertPos);
      fs.writeFileSync(fullPath, content, "utf-8");
      return { success: true, message: `✅ 已注入 ModuleRule 属性: ${relativePath}` };
    }

    return { success: false, message: `无法解析 Project 结构: ${relativePath}` };
  } catch (err) {
    return { success: false, message: `写入失败: ${err}` };
  }
}

/**
 * 验证所有 .csproj 的 ModuleRule 状态
 *
 * @param projectRoot 项目根目录
 * @param slnPath .sln 文件路径（可选）
 */
export function validateModuleRules(projectRoot?: string, slnPath?: string): {
  total: number;
  hasRule: number;
  noRule: number;
  noRuleList: string[];
} {
  const root = resolveProjectRootWithSln(slnPath, projectRoot);
  const csprojFiles = findCsprojFiles(root, slnPath);
  const noRuleList: string[] = [];
  let hasRule = 0;

  for (const file of csprojFiles) {
    // file 可能是相对路径（来自 .sln）或绝对路径（来自递归扫描）
    const fullPath = resolveFilePath(file, root);
    const relativePath = path.relative(root, fullPath);

    // 读取完整文件检查 ModuleRule 标记（避免因文件头部注释过长导致误判）
    const content = fs.readFileSync(fullPath, "utf-8");
    if (content.includes('Label="ModuleRule"')) {
      hasRule++;
    } else {
      noRuleList.push(relativePath);
    }
  }

  return {
    total: csprojFiles.length,
    hasRule,
    noRule: noRuleList.length,
    noRuleList,
  };
}
