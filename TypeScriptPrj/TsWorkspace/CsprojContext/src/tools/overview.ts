/**
 * 自动生成 — overview
 *
 * 模板类型: query-with-detail
 * 生成时间: 2026-06-23T06:14:11.140Z
 */

import { getAllModuleRules } from "../core/parser.js";
import { formatOverview, extractSlnAndRoot } from "../core/utils.js";

export async function handle(args: Record<string, unknown>): Promise<string> {

  const projectRoot = extractSlnAndRoot(args);

  const detail = args.detail === true || args.detail === "true";
  const results = getAllModuleRules(projectRoot, args.slnPath as string | undefined);
  if (results.length === 0) {
    return "⚠️ 未找到任何包含 ModuleRule 属性的 .csproj 文件。\n请先使用 set_module_rule 工具注入 ModuleRule 属性。";
  }

  return formatOverview(results, detail);
}
