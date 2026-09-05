#!/usr/bin/env node

/**
 * CsprojContext — MCP Server 入口
 *
 * 提供 .csproj ModuleRule 上下文查询的 MCP 服务器。
 * 通过 MCP SDK 暴露工具接口，运行时动态加载 handler。
 *
 * 核心流程：
 * 1. 注册所有工具 handler
 * 2. 创建 MCP Server（从 tools.config.json 加载工具定义）
 * 3. 启动服务器（连接 stdio 传输）
 */

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";
import { registerAllHandlers, loadToolDefinitions, getHandler } from "./tools/registry.js";

async function main(): Promise<void> {
  // 1. 注册所有工具处理函数
  await registerAllHandlers();

  // 2. 创建 MCP 服务器
  const server = new Server(
    {
      name: "csproj-context",
      version: "1.0.0",
    },
    {
      capabilities: {
        tools: {},
      },
    }
  );

  // 3. 处理 ListTools 请求
  server.setRequestHandler(ListToolsRequestSchema, async () => {
    const tools = loadToolDefinitions();
    return { tools };
  });

  // 4. 处理 CallTool 请求
  server.setRequestHandler(CallToolRequestSchema, async (request) => {
    const { name, arguments: args } = request.params;

    const handler = await getHandler(name);
    if (!handler) {
      return {
        content: [{ type: "text", text: `❌ 未知工具: ${name}` }],
        isError: true,
      };
    }

    try {
      const result = await handler(args || {});
      return {
        content: [{ type: "text", text: result }],
      };
    } catch (err) {
      const errMsg = err instanceof Error ? err.message : String(err);
      return {
        content: [{ type: "text", text: `❌ 执行错误: ${errMsg}` }],
        isError: true,
      };
    }
  });

  // 5. 启动服务器
  const transport = new StdioServerTransport();
  console.error("CsprojContext MCP server starting...");
  await server.connect(transport);
}

main().catch((err) => {
  console.error("CsprojContext 启动失败:", err instanceof Error ? err.message : String(err));
  process.exit(1);
});
