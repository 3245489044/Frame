/**
 * 手写 — project-tree
 *
 * 模板类型: custom
 * 生成时间: 2026-06-23T06:14:11.138Z
 */

import { getAllModuleRules } from "../core/parser.js";
import { groupModulesByLayer, extractSlnAndRoot } from "../core/utils.js";

export async function handle(args: Record<string, unknown>): Promise<string> {

  const projectRoot = extractSlnAndRoot(args);

  const results = getAllModuleRules(projectRoot, args.slnPath as string | undefined);
  if (results.length === 0) {
    return "⚠️ 未找到任何包含 ModuleRule 属性的 .csproj 文件。\n请先使用 set_module_rule 工具注入 ModuleRule 属性。";
  }

  const grouped = groupModulesByLayer(results);
  let output = "📁 项目树形结构\n\n";
  for (const layer of grouped) {
    output += `[${layer.layerName}]\n`;
    for (const mod of layer.modules) {
      output += `  ├─ ${mod.title || mod.path}\n`;
    }
    output += "\n";
  }
  return output;
}
