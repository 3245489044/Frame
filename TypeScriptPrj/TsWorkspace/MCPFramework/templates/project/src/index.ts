#!/usr/bin/env node

/**
 * 子项目 MCP Server 入口
 *
 * 基于 MCPFramework 创建的 MCP 服务器。
 * 只需修改以下内容：
 * 1. 服务器名称和版本
 * 2. 注册自己的业务服务
 * 3. 注册自己的工具 handler
 *
 * 使用方式：
 * 1. 复制此模板到新目录
 * 2. 修改 package.json
 * 3. 实现 src/core/business.ts
 * 4. 实现 src/tools/*.ts
 * 5. 编写 tools.config.json
 * 6. npm install && npm run build
 */

import { registerAllHandlers, registerBusinessService } from "./core/registry";
import { createMcpServer, startServer } from "./core/server";
import { business } from "./core/business";

// 注册业务服务（handler 通过 getBusinessService() 访问）
registerBusinessService(business);

// 注册所有工具处理函数
registerAllHandlers();

// 创建并启动 MCP 服务器
const server = createMcpServer({
  name: "my-mcp-server",
  version: "1.0.0",
});

startServer(server).catch((err: Error) => {
  console.error("Fatal error:", err);
  process.exit(1);
});
