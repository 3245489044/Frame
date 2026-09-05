/**
 * 手写 — validate
 *
 * 模板类型: custom
 * 生成时间: 2026-06-23T06:14:11.138Z
 */

import { validateModuleRules } from "../core/parser.js";
import { extractSlnAndRoot } from "../core/utils.js";

export async function handle(args: Record<string, unknown>): Promise<string> {

  const projectRoot = extractSlnAndRoot(args);

  const results = validateModuleRules(projectRoot, args.slnPath as string | undefined);
  let output = `📊 ModuleRule 验证报告\n\n`;
  output += `  总 .csproj 文件数: ${results.total}\n`;
  output += `  已配置 ModuleRule: ${results.hasRule}\n`;
  output += `  未配置 ModuleRule: ${results.noRule}\n`;
  if (results.noRuleList.length > 0) {
    output += `\n  未配置列表:\n`;
    for (const file of results.noRuleList) {
      output += `    ❌ ${file}\n`;
    }
  }
  return output;
}
