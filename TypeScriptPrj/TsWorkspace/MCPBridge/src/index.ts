#!/usr/bin/env node

/**
 * McpBridge — MCP 桥接服务器入口
 *
 * 作为 Cline 与 Docker MCP 服务器之间的桥接层，
 * 自动管理 Docker Desktop 生命周期并聚合远程 MCP 工具。
 *
 * 核心流程：
 * 1. 创建 McpBridge 实例
 * 2. 初始化（加载配置、准备 Docker 环境）
 * 3. 启动（执行流水线：Docker → 聚合工具 → 健康检查）
 * 4. 通过 MCP SDK 暴露 ListTools / CallTool 接口
 */

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";
import { McpBridge } from "./core/bridge.js";
import { logger } from "./utils/logger.js";
import { formatError } from "./utils/errors.js";

// ─── 主入口 ─────────────────────────────────────────────────

async function main(): Promise<void> {
  // 1. 创建桥接器
  const bridge = new McpBridge();

  // 2. 初始化并启动
  await bridge.initialize();
  await bridge.start();

  // 3. 创建 MCP 服务器
  const server = new Server(
    {
      name: "mcp-bridge",
      version: "2.0.0",
    },
    {
      capabilities: {
        tools: {},
      },
    }
  );

  // 4. 处理 ListTools 请求
  server.setRequestHandler(ListToolsRequestSchema, async () => {
    const tools = bridge.getToolDefinitions();
    return { tools };
  });

  // 5. 处理 CallTool 请求
  server.setRequestHandler(CallToolRequestSchema, async (request) => {
    const { name, arguments: args } = request.params;

    try {
      const result = await bridge.callTool(name, (args || {}) as Record<string, unknown>);
      return {
        content: result.content,
        isError: result.isError,
      };
    } catch (err) {
      const errMsg = formatError(err);
      return {
        content: [{ type: "text", text: `❌ 执行错误: ${errMsg}` }],
        isError: true,
      };
    }
  });

  // 6. 连接 stdio 传输
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error("McpBridge MCP server running on stdio");
}

main().catch((err) => {
  logger.error("McpBridge", `启动失败: ${err instanceof Error ? err.message : String(err)}`);
  process.exit(1);
});
