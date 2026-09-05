#!/usr/bin/env node

/**
 * MCPFramework — MCP Server 入口
 *
 * 这是框架的通用 MCP Server 入口。
 * 子项目可以直接使用此入口，或在入口文件中调用框架 API。
 *
 * 核心流程：
 * 1. 创建 MCP Server（自动从 tools.config.json 加载工具定义）
 * 2. 启动服务器（连接 stdio 传输）
 * 3. 运行时动态查找并懒加载 handler
 *
 * 使用方式：
 * 1. 直接运行：node build/index.js
 * 2. 作为子项目入口：在子项目的 index.ts 中 import 框架组件
 */

import { createMcpServer, startServer } from "./core/server.js";
import { logger } from "./utils/logger.js";

// 创建并启动 MCP 服务器
const server = createMcpServer({
  name: "mcp-framework",
  version: "1.0.0",
});

startServer(server, "mcp-framework").catch((err) => {
  logger.error("MCPFramework", `启动失败: ${err instanceof Error ? err.message : String(err)}`);
  process.exit(1);
});
