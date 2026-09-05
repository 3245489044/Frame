/**
 * MCPFramework — MCP Server 工厂
 *
 * 封装 MCP Server 的创建、ListTools 和 CallTool 请求处理。
 * 子项目只需调用 createMcpServer() 即可获得完整功能的 MCP 服务器。
 *
 * 核心流程：
 * - ListTools：从 tools.config.json 读取工具定义
 * - CallTool：通过 registry 动态查找并懒加载 handler
 */

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";
import { loadToolDefinitions, getHandler } from "./registry.js";
import { McpServerConfig } from "./types.js";
import { logger } from "../utils/logger.js";
import { formatError } from "../utils/errors.js";

/**
 * 创建 MCP 服务器
 *
 * @param config 服务器配置（名称、版本）
 * @returns Server 实例
 *
 * @example
 * ```typescript
 * import { createMcpServer } from "./core/server";
 *
 * const server = createMcpServer({
 *   name: "my-mcp-server",
 *   version: "1.0.0",
 * });
 * ```
 */
export function createMcpServer(config: McpServerConfig): Server {
  const server = new Server(
    {
      name: config.name,
      version: config.version,
    },
    {
      capabilities: {
        tools: {},
      },
    }
  );

  // 处理 ListTools 请求
  server.setRequestHandler(ListToolsRequestSchema, async () => {
    const defs = loadToolDefinitions();
    // 将 ToolDefinition[] 映射为 McpToolDefinition[]，确保类型匹配
    const tools = defs.map((def) => ({
      name: def.name,
      description: def.description,
      inputSchema: def.inputSchema,
    }));
    return { tools };
  });

  // 处理 CallTool 请求
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
      const errMsg = formatError(err);
      return {
        content: [{ type: "text", text: `❌ 执行错误: ${errMsg}` }],
        isError: true,
      };
    }
  });

  return server;
}

/**
 * 启动 MCP 服务器（连接 stdio 传输）
 *
 * @param server Server 实例
 * @param serverName 服务器显示名称（默认 "MCPFramework"）
 *
 * @example
 * ```typescript
 * const server = createMcpServer({ name: "my-server", version: "1.0.0" });
 * startServer(server, "my-server");
 * ```
 */
export async function startServer(server: Server, serverName?: string): Promise<void> {
  const transport = new StdioServerTransport();
  await server.connect(transport);
  const name = serverName || "MCPFramework";
  console.error(`${name} MCP server running on stdio`);
}
