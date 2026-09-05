/**
 * 自动生成 — detail
 *
 * 模板类型: detail
 * 生成时间: 2026-06-23T06:14:11.139Z
 */

import { getModuleDetail } from "../core/parser.js";
import { getLayerName, formatDetailResult, extractSlnAndRoot } from "../core/utils.js";

export async function handle(args: Record<string, unknown>): Promise<string> {
  const path = args.path as string;
  if (path === undefined || path === null) {
    return `❌ 错误: 缺少必填参数 'path'`;
  }

  const projectRoot = extractSlnAndRoot(args);

  const result = getModuleDetail(path, projectRoot, args.slnPath as string | undefined);
  if (!result) {
    return `⚠️ 未找到 ModuleRule 属性: ${path}\n可能原因：文件不存在或未配置 ModuleRule 属性。`;
  }

  return formatDetailResult(result, getLayerName);
}
