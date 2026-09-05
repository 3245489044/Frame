/**
 * 自动生成 — search
 *
 * 模板类型: query
 * 生成时间: 2026-06-23T06:14:11.140Z
 */

import { searchModules } from "../core/parser.js";
import { getLayerName, formatSearchResults, extractSlnAndRoot } from "../core/utils.js";

export async function handle(args: Record<string, unknown>): Promise<string> {
  const keyword = args.keyword as string;
  if (keyword === undefined || keyword === null) {
    return `❌ 错误: 缺少必填参数 'keyword'`;
  }

  const projectRoot = extractSlnAndRoot(args);

  const results = searchModules(keyword, projectRoot, args.slnPath as string | undefined);
  if (!results || (Array.isArray(results) && results.length === 0)) {
    return `🔍 未找到匹配 "${keyword}" 的项目`;
  }

  return formatSearchResults(results, keyword, getLayerName);
}
