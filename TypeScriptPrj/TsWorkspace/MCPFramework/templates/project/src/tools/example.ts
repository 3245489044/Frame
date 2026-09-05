/**
 * 示例工具处理函数
 *
 * 子项目参考此文件实现自己的工具。
 * tools.config.json 中的 handler 字段必须与文件名（不含扩展名）一致。
 *
 * @example
 * ```json
 * // tools.config.json
 * {
 *   "name": "example_tool",
 *   "description": "示例工具",
 *   "handler": "example",
 *   "inputSchema": { ... }
 * }
 * ```
 */

import { getBusinessService } from "../core/registry";
import { formatError } from "../core/utils";

export async function handle(args: Record<string, unknown>): Promise<string> {
  try {
    const param1 = args.param1 as string;
    if (!param1) {
      return formatError("缺少必填参数 'param1'");
    }

    const business = getBusinessService<typeof import("../core/business").business>();
    const result = business.exampleMethod(param1);

    const lines: string[] = [];
    lines.push("# 示例工具执行结果\n");
    lines.push(`- **参数**: ${param1}`);
    lines.push(`- **结果**: ${result}`);

    return lines.join("\n");
  } catch (err) {
    return formatError(`执行失败: ${err}`);
  }
}
