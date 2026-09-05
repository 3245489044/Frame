# McpBridge — Cline ↔ Docker MCP 桥接服务器

## 概述

McpBridge 是一个专门的 MCP 服务器，运行在 **宿主机（Windows）** 上，作为 Cline 与多个 MCP 服务器的**统一代理/桥接**。

> **默认无 Docker 调用**：默认配置下 McpBridge 不会启动 Docker Desktop、不会执行 `docker` 命令、也不会运行 `docker compose`。它只负责把配置中 `servers` 里已运行（宿主机本地进程或已手动启动的 Docker 容器）的 MCP 端点聚合为一个 stdio MCP 服务器。需要自动管理 Docker 时，在 `mcp-bridge.config.json` 的 `docker` 节将 `"enabled": true` 即可（详见下文「Docker 模式」）。

### 解决的问题

| 问题 | 说明 |
|------|------|
| **配置复杂** | 原来 Cline 需要配置 8 个独立的 HTTP MCP 服务器 |
| **端口管理** | 每个 MCP 服务器占用独立端口（3100-3107） |
| **无统一监控** | 无法集中查看所有 MCP 服务器的健康状态 |
| **无自动重连** | MCP 进程崩溃后需要手动重启 |

### 架构

```
Cline (VS Code)
  │
  │ stdio (单一连接)
  ▼
McpBridge (宿主机 Node.js 进程)
  │
  │ HTTP/SSE (localhost:3100-3107)
  ▼
Docker 容器 (workflow-mcp-env)
  ├── mcp-sequentialthinking-tools (3100)
  ├── context7-mcp (3101)
  ├── context-mode (3102)
  ├── memory (3103)
  ├── filesystem (3104)
  ├── lexis-mcp (3105)
  ├── token-savior-recall (3106)
  └── csproj-context (3107)
```

## 快速开始

### 1. 安装依赖

```bash
cd TypeScriptPrj/McpBridge
npm install
```

### 2. 构建

```bash
npm run build
```

### 3. 配置 Cline

在 `cline_mcp_settings.json` 中添加：

```json
{
  "mcpServers": {
    "docker-mcp-bridge": {
      "command": "node",
      "args": ["D:/Project/Program/WorkFlowPrj/TypeScriptPrj/McpBridge/build/index.js"],
      "autoApprove": ["*"],
      "disabled": false
    }
  }
}
```

### 4. 启动

默认（无 Docker）模式下，只需确保 `servers` 中配置的 MCP 端点已在宿主机可访问（例如本地进程已启动，或容器已手动运行），然后启动 McpBridge：

```bash
node TypeScriptPrj/McpBridge/build/index.js
```

或者通过 CLine 自动启动（配置后重启 VS Code 即可）。

> 需要自动管理 Docker（自动启动 Desktop / `docker compose up`）时，见下文「Docker 模式」。

## 配置

编辑 `mcp-bridge.config.json`：

```json
{
  "version": "1.0",
  "bridge": {
    "name": "docker-mcp-bridge",
    "description": "Cline ↔ Docker MCP 桥接服务器"
  },
  "servers": [
    {
      "name": "mcp-sequentialthinking-tools",
      "url": "http://localhost:3100/mcp",
      "timeout": 30000,
      "autoReconnect": true,
      "reconnectDelay": 2000
    }
    // ... 更多服务器
  ]
}
```

### 配置字段说明

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `name` | string | - | 服务器唯一标识 |
| `url` | string | - | MCP HTTP/SSE 端点 URL |
| `timeout` | number | 30000 | 请求超时（毫秒） |
| `autoReconnect` | boolean | true | 是否自动重连 |
| `reconnectDelay` | number | 2000 | 重连延迟（毫秒） |

### Docker 模式

Docker 管理**默认关闭**（不调用 Docker）。如需自动管理 Docker 环境（自动启动 Docker Desktop、检测守护进程、执行 `docker compose up`、等待端口就绪），将 `mcp-bridge.config.json` 的 `docker.enabled` 置为 `true`：

```json
{
  "docker": {
    "enabled": true,
    "autoStart": true,
    "autoStartDesktop": true,
    "desktopPath": "C:\\Program Files\\Docker\\Docker\\Docker Desktop.exe",
    "composePath": "DevEnvironment/Docker/docker-compose.yml",
    "containerName": "workflow-mcp-env",
    "healthCheckPort": 3100
  }
}
```

`docker` 节字段说明：

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `enabled` | boolean | `false` | 是否启用 Docker 管理。默认 `false`，即默认不调用 Docker；置 `true` 恢复自动管理 |
| `autoStart` | boolean | `true` | 容器未运行（`docker compose up`）时是否自动启动容器 |
| `autoStartDesktop` | boolean | `true` | 守护进程未运行时是否自动启动 Docker Desktop |
| `desktopPath` | string | - | Docker Desktop 可执行文件路径 |
| `composePath` | string | - | docker-compose.yml 路径（可用 paths 节集中配置） |
| `containerName` | string | - | 目标容器名称 |
| `healthCheckPort` | number | `3100` | 端口就绪健康检查端口 |

> 启用 Docker 模式后，路由会把宿主机路径重写为容器内路径（如 `/workspace/WorkFlowPrj/...`）；关闭时保持宿主机路径原样转发。

## CLI 管理命令

```bash
# 查看桥接状态
node build/cli.js status

# 重新加载配置
node build/cli.js reload

# 刷新指定服务器的工具列表
node build/cli.js refresh mcp-sequentialthinking-tools

# 列出所有已注册的工具
node build/cli.js list

# 按服务器筛选工具
node build/cli.js list --server context-mode
```

## 内置管理工具

McpBridge 自身提供 3 个管理工具：

| 工具名 | 说明 |
|--------|------|
| `bridge_list` | 列出所有远程 MCP 服务器及其健康状态 |
| `bridge_status` | 查看桥接器详细统计信息 |
| `bridge_reload` | 重新加载配置并重新聚合工具 |

## 项目结构

```
TypeScriptPrj/McpBridge/
├── package.json
├── tsconfig.json
├── tsup.config.ts
├── mcp-bridge.config.json        # 桥接配置
├── README.md
│
├── src/
│   ├── index.ts                  # MCP 服务器入口
│   ├── cli.ts                    # CLI 管理入口
│   │
│   ├── core/
│   │   ├── types.ts              # 类型定义
│   │   ├── bridge.ts             # 桥接核心（整合所有模块）
│   │   ├── aggregator.ts         # 工具聚合器
│   │   └── router.ts             # 请求路由器
│   │
│   ├── transport/
│   │   └── http-transport.ts     # HTTP/SSE 传输层
│   │
│   ├── monitor/
│   │   └── health-check.ts       # 健康检查 + 自动重连
│   │
│   └── tools/                    # 管理工具（自动生成）
│
└── tools-definitions/            # 工具定义（JSON）
    ├── bridge-list.json
    ├── bridge-status.json
    └── bridge-reload.json
```

## 核心模块说明

### 工具聚合器 (ToolAggregator)
- 启动时向所有 Docker MCP 发送 `ListTools` 请求
- 建立 `tool name → MCP server` 映射表
- 支持按需刷新单个服务器

### 请求路由器 (RequestRouter)
- 根据工具名查找目标 MCP 服务器
- 通过 HTTP/SSE 转发请求
- 统一错误处理和响应格式化

### 健康检查器 (HealthChecker)
- 每 30 秒 ping 所有 MCP 服务器
- 自动重连失败的服务器
- 状态变更通知

### HTTP 传输层 (HttpTransport)
- 基于 Node.js fetch API
- 支持超时控制
- 兼容 SSE 和 JSON 响应格式

## 与现有架构的关系

McpBridge **不改变** Docker 容器内部的任何配置。它作为宿主机上的一个额外层，将多个 HTTP MCP 端点聚合为一个 stdio MCP 服务器。

- Docker 容器继续运行所有 MCP 服务
- 端口映射（3100-3107）保持不变
- Cline 配置从 8 个条目简化为 1 个
