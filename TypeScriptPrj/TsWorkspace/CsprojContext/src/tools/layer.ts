/**
 * 自动生成 — layer
 *
 * 模板类型: query
 * 生成时间: 2026-06-23T06:14:11.138Z
 */

import { getModulesByLayer } from "../core/parser.js";
import { getLayerName, formatLayerResults, extractSlnAndRoot } from "../core/utils.js";

export async function handle(args: Record<string, unknown>): Promise<string> {
  const layer = args.layer as number;
  if (layer === undefined || layer === null) {
    return `❌ 错误: 缺少必填参数 'layer'`;
  }

  const projectRoot = extractSlnAndRoot(args);

  const results = getModulesByLayer(layer, projectRoot, args.slnPath as string | undefined);
  if (!results || (Array.isArray(results) && results.length === 0)) {
    return `⚠️ 层 [${layer}] ${getLayerName(layer)} 下没有已配置 ModuleRule 属性的项目`;
  }

  return formatLayerResults(results, layer, getLayerName);
}
