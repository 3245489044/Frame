/**
 * MCPFramework — 通用类型定义
 *
 * 框架提供的基础类型，子项目可直接使用或扩展。
 * 包含 MCP 协议通用类型和框架内部类型。
 */

// ═══════════════════════════════════════════════════════════════
// 框架内部类型
// ═══════════════════════════════════════════════════════════════

/** 工具处理函数签名 */
export type ToolHandler = (args: Record<string, unknown>) => Promise<string>;

/** 工具定义（来自 tools.config.json） */
export interface ToolDefinition {
  name: string;
  description: string;
  handler: string;
  inputSchema: {
    type: "object";
    properties: Record<string, unknown>;
    required?: string[];
  };
}

/** 已注册工具条目（含处理函数） */
export interface RegisteredTool extends ToolDefinition {
  handlerFn: ToolHandler;
}

/** 业务服务接口 — 子项目实现此接口提供业务逻辑 */
export interface BusinessService {
  [key: string]: (...args: any[]) => any;
}

/** MCP 服务器配置 */
export interface McpServerConfig {
  name: string;
  version: string;
}

// ═══════════════════════════════════════════════════════════════
// MCP 协议通用类型
// ═══════════════════════════════════════════════════════════════

/** MCP 工具定义（来自 ListTools 响应） */
export interface McpToolDefinition {
  name: string;
  description?: string;
  inputSchema: {
    type: "object";
    properties: Record<string, unknown>;
    required?: string[];
  };
}

/** MCP JSON-RPC 请求 */
export interface JsonRpcRequest {
  jsonrpc: "2.0";
  id: string | number;
  method: string;
  params?: Record<string, unknown>;
}

/** MCP JSON-RPC 响应 */
export interface JsonRpcResponse {
  jsonrpc: "2.0";
  id: string | number;
  result?: unknown;
  error?: {
    code: number;
    message: string;
    data?: unknown;
  };
}

/** MCP ListTools 响应结果 */
export interface ListToolsResult {
  tools: McpToolDefinition[];
}

/** MCP CallTool 响应结果 */
export interface CallToolResult {
  content: Array<{
    type: string;
    text: string;
  }>;
  isError?: boolean;
}

/** 工具名 → 服务器名 映射条目 */
export interface ToolRouteEntry {
  toolName: string;
  serverName: string;
  toolDefinition: McpToolDefinition;
}
