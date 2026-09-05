/**
 * MCPFramework — 工具实现模板
 *
 * 子项目参考此模板实现自己的工具处理函数。
 *
 * 每个工具文件必须：
 * 1. export async function handle(args: Record<string, unknown>): Promise<string>
 * 2. 通过 getBusinessService() 获取业务服务
 * 3. 返回 Markdown 格式的字符串
 *
 * tools.config.json 中的 handler 字段必须与文件名（不含扩展名）一致。
 */

import { getBusinessService } from "../core/registry.js";
import { formatError } from "basic-method";

/**
 * 工具处理函数
 *
 * @param args 工具参数（来自 tools.config.json 的 inputSchema 定义）
 * @returns Markdown 格式的结果字符串
 *
 * @example
 * ```typescript
 * // tools.config.json 定义：
 * {
 *   "name": "my_tool",
 *   "description": "我的工具",
 *   "handler": "my-tool",    // ← 对应文件名 my-tool.ts
 *   "inputSchema": {
 *     "type": "object",
 *     "properties": {
 *       "param1": { "type": "string", "description": "参数1" }
 *     },
 *     "required": ["param1"]
 *   }
 * }
 * ```
 */
export async function handle(args: Record<string, unknown>): Promise<string> {
  try {
    // 1. 参数校验
    const param1 = args.param1 as string;
    if (!param1) {
      return formatError("缺少必填参数 'param1'");
    }

    // 2. 获取业务服务
    const business = getBusinessService();

    // 3. 调用业务逻辑
    // const result = business.someMethod(param1);

    // 4. 格式化输出
    const lines: string[] = [];
    lines.push("# 工具执行结果\n");
    lines.push(`- **参数**: ${param1}`);
    lines.push(`- **结果**: 处理完成`);

    return lines.join("\n");
  } catch (err) {
    return formatError(`执行失败: ${err}`);
  }
}
