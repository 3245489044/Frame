/**
 * 自动生成 — dependencies
 *
 * 模板类型: query
 * 生成时间: 2026-06-23T06:14:11.138Z
 */

import { getDependencies } from "../core/parser.js";
import { formatDependenciesResult, extractSlnAndRoot } from "../core/utils.js";

export async function handle(args: Record<string, unknown>): Promise<string> {
  const path = args.path as string;
  if (path === undefined || path === null) {
    return `❌ 错误: 缺少必填参数 'path'`;
  }

  const projectRoot = extractSlnAndRoot(args);
  const depth = (args.depth as number) || 1;

  const results = getDependencies(path, projectRoot, depth, args.slnPath as string | undefined);

  return formatDependenciesResult(results);
}
