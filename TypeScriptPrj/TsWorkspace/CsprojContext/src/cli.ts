#!/usr/bin/env node

/**
 * CsprojContext CLI - 命令行接口
 * 用于在终端中直接查询 .csproj ModuleRule 信息
 */

import { registerAllHandlers, loadToolDefinitions, getHandler } from "./tools/registry.js";

// 批量注册所有工具处理函数
await registerAllHandlers();

async function main() {
  const args = process.argv.slice(2);

  if (args.length === 0) {
    console.log("CsprojContext CLI - .csproj ModuleRule 查询工具\n");
    console.log("用法:");
    console.log("  csprojcontext-cli <tool> [参数...]\n");
    console.log("可用工具:");
    const tools = loadToolDefinitions();
    for (const tool of tools) {
      console.log(`  ${tool.name} - ${tool.description}`);
    }
    console.log("\n示例:");
    console.log('  csprojcontext-cli get_overview');
    console.log('  csprojcontext-cli get_detail --path BasicMethod/BasicMethod.csproj');
    console.log('  csprojcontext-cli search_modules --keyword 数据');
    console.log('  csprojcontext-cli get_by_layer --layer 1');
    console.log('  csprojcontext-cli get_layer_map');
    console.log('  csprojcontext-cli validate_rules');
    return;
  }

  const toolName = args[0];
  const handler = await getHandler(toolName);

  if (!handler) {
    console.error(`❌ 未知工具: ${toolName}`);
    console.error(`使用 csprojcontext-cli 查看可用工具列表`);
    process.exit(1);
  }

  // 解析参数
  const parsedArgs: Record<string, unknown> = {};
  for (let i = 1; i < args.length; i++) {
    if (args[i].startsWith("--")) {
      const key = args[i].slice(2);
      const value = args[i + 1];
      if (value && !value.startsWith("--")) {
        // 尝试解析数字
        const num = Number(value);
        parsedArgs[key] = isNaN(num) ? value : num;
        i++;
      } else {
        parsedArgs[key] = true;
      }
    }
  }

  try {
    const result = await handler(parsedArgs);
    console.log(result);
  } catch (err) {
    console.error(`❌ 执行错误:`, err);
    process.exit(1);
  }
}

main();
