#!/usr/bin/env node

/**
 * MCPFramework — CLI 命令行接口
 *
 * 通用 CLI 入口，基于 tools.config.json 自动生成命令行工具。
 * 使用 commander 进行参数解析，支持子命令和 --help。
 *
 * 核心流程：
 * 1. 从 tools.config.json 加载工具定义
 * 2. 动态生成 CLI 子命令
 * 3. 运行时通过 registry 查找并执行 handler
 *
 * 使用方式：
 *   mcp-framework-cli <tool> [参数...]
 *
 * 示例：
 *   mcp-framework-cli get_overview
 *   mcp-framework-cli get_detail --path some/file.csproj
 */

import { Command } from "commander";
import { loadToolDefinitions, getHandler } from "./core/registry.js";
import { formatError } from "./utils/errors.js";

async function main() {
  const program = new Command();

  program
    .name("mcp-framework-cli")
    .description("MCPFramework — 通用 MCP 服务器命令行工具")
    .version("1.0.0");

  const tools = loadToolDefinitions();

  if (tools.length === 0) {
    program
      .command("help")
      .description("显示帮助信息")
      .action(() => {
        console.log("MCPFramework CLI — 通用 MCP 服务器命令行工具\n");
        console.log("用法:");
        console.log("  mcp-framework-cli <tool> [参数...]\n");
        console.log("可用工具:");
        console.log("  (未找到 tools.config.json，请确保配置文件存在)");
      });
  } else {
    for (const tool of tools) {
      const cmd = program
        .command(tool.name)
        .description(tool.description);

      // 从 inputSchema 动态生成选项
      if (tool.inputSchema?.properties) {
        for (const [key, prop] of Object.entries(tool.inputSchema.properties)) {
          const propObj = prop as { type?: string; description?: string };
          const flag = `--${key}`;
          const required = tool.inputSchema.required?.includes(key);
          const desc = `${propObj.description || ""}${required ? " (必填)" : ""}`;

          if (propObj.type === "boolean") {
            cmd.option(flag, desc);
          } else {
            cmd.option(`${flag} <value>`, desc);
          }
        }
      }

      cmd.action(async (args: Record<string, unknown>) => {
        const handler = await getHandler(tool.name);
        if (!handler) {
          console.error(`❌ 未知工具: ${tool.name}`);
          process.exit(1);
        }

        try {
          const result = await handler(args || {});
          console.log(result);
        } catch (err) {
          console.error(`❌ 执行错误: ${formatError(err)}`);
          process.exit(1);
        }
      });
    }
  }

  // 解析命令行参数
  await program.parseAsync(process.argv);
}

main().catch((err) => {
  console.error("Fatal error:", err);
  process.exit(1);
});
