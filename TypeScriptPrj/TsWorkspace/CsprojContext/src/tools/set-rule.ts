/**
 * 自动生成 — set-rule
 *
 * 模板类型: write
 * 生成时间: 2026-06-23T06:14:11.141Z
 */

import { setModuleRule } from "../core/parser.js";
import { extractSlnAndRoot } from "../core/utils.js";

export async function handle(args: Record<string, unknown>): Promise<string> {
  const path = args.path as string;
  if (path === undefined || path === null) {
    return `❌ 错误: 缺少必填参数 'path'`;
  }
  const title = args.title as string;
  if (title === undefined || title === null) {
    return `❌ 错误: 缺少必填参数 'title'`;
  }
  const layer = args.layer as number;
  if (layer === undefined || layer === null) {
    return `❌ 错误: 缺少必填参数 'layer'`;
  }
  const description = args.description as string;
  if (description === undefined || description === null) {
    return `❌ 错误: 缺少必填参数 'description'`;
  }

  const projectRoot = extractSlnAndRoot(args);
  const keywords = args.keywords as string | undefined;
  const rules = args.rules as string | undefined;
  const prompts = args.prompts as string | undefined;

  const result = setModuleRule(path, { title, layer, description, keywords, rules, prompts }, projectRoot, args.slnPath as string | undefined);

  return result.success
    ? `✅ ${result.message}`
    : `❌ ${result.message}`;
}
