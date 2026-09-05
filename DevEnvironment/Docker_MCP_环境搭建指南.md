# WorkFlowPrj Docker MCP 环境搭建指南

> **版本**：v2.0  
> **最后更新**：2026-06-23  
> **目标**：将 CLine MCP 服务器 + .NET SDK + Python 环境统一容器化，实现环境隔离与高性能开发
> **基础镜像**：Debian 12 Slim（轻量、稳定、高性能）

---

## 目录

1. [概述](#1-概述)
2. [架构设计](#2-架构设计)
3. [环境要求](#3-环境要求)
4. [目录结构](#4-目录结构)
5. [Dockerfile 详解](#5-dockerfile-详解)
6. [MCP HTTP/SSE 网关](#6-mcp-httpsse-网关)
7. [docker-compose 配置](#7-docker-compose-配置)
8. [CLine 配置](#8-cline-配置)
9. [安装步骤](#9-安装步骤)
10. [日常使用](#10-日常使用)
11. [更新指南](#11-更新指南)
12. [故障排除](#12-故障排除)

---

## 1. 概述

### 1.1 动机

| 优势 | 说明 |
|------|------|
| **环境隔离** | 所有依赖（Node.js、.NET SDK、Python）封装在容器中，主机只需 VS Code |
| **一致性** | 团队共享同一镜像，消除"在我机器上能跑"的问题 |
| **易于更新** | 分层 Dockerfile，各层职责清晰，更新某层不影响其他层 |
| **性能** | Linux 容器内运行 .NET/Python，文件 I/O 通过卷挂载零拷贝 |

### 1.2 架构总览

```
Windows 11 (宿主机)
│
├── VS Code + CLine 扩展
│     └── cline_mcp_settings.json → type: "http"
│           └── 通过 HTTP/SSE 连接 Docker 内的 MCP 服务
│
└── Docker Desktop (WSL2 后端)
      └── 容器: workflow-mcp-env (Debian 12 Slim)
            ├── MCP 服务器群 (Node.js/Python)
            │     ├── mcp-sequentialthinking-tools  (端口 3100)
            │     ├── context7-mcp                  (端口 3101)
            │     ├── context-mode                  (端口 3102)
            │     ├── memory                        (端口 3103)
            │     ├── filesystem                    (端口 3104)
            │     ├── lexis-mcp                     (端口 3105)
            │     ├── token-savior-recall (Python)  (端口 3106)
            │     └── csproj-context                (端口 3107)
            ├── .NET SDK 10.0 (编译运行 C#)
            └── Python 3.13 (运行 Python 模型)
```

---

## 2. 架构设计

### 2.1 设计原则

1. **分层构建** — Dockerfile 多阶段构建，每层职责单一，便于缓存和更新
2. **代码在外** — 项目代码通过卷挂载从主机映射到容器，修改即时生效
3. **HTTP/SSE 通信** — MCP 服务器通过 HTTP/SSE 协议与主机 CLine 通信，替代原有的 stdio 方式
4. **统一网关** — MCP 网关容器内管理所有 MCP 子进程，对外暴露统一端口范围

### 2.2 分层架构

```
┌─────────────────────────────────────────────────────────────┐
│  Layer 5: MCP 服务层                                         │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  MCP Gateway (Node.js)                                │  │
│  │  管理所有 MCP 子进程，暴露 HTTP/SSE 端点               │  │
│  │  端口: 3100-3107                                      │  │
│  └───────────────────────────────────────────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│  Layer 4: Python 环境                                        │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Python 3.13 + pip 依赖                                │  │
│  │  token-savior-recall, PythonModel 等                   │  │
│  └───────────────────────────────────────────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│  Layer 3: .NET SDK                                          │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  .NET SDK 10.0                                        │  │
│  │  dotnet build / dotnet run / dotnet test              │  │
│  └───────────────────────────────────────────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│  Layer 2: Node.js 环境                                       │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Node.js 22 LTS + npm                                 │  │
│  │  全局包: context-mode, lexis-mcp                      │  │
│  └───────────────────────────────────────────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│  Layer 1: 基础系统 (Debian 12 Slim)                         │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  系统工具: git, curl, unzip, ca-certificates          │  │
│  │  进程管理: tini (init 进程)                           │  │
│  │  时区: Asia/Shanghai                                  │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

### 2.3 数据流

```
CLine (VS Code)
  │
  │ HTTP POST /mcp (JSON-RPC over SSE)
  ▼
MCP Gateway (容器内, localhost:3100-3107)
  │
  ├──→ 子进程: sequential-thinking (stdio)
  ├──→ 子进程: context7-mcp (stdio)
  ├──→ 子进程: context-mode (stdio)
  ├──→ 子进程: memory (stdio)
  ├──→ 子进程: filesystem (stdio) ──→ 卷挂载: /workspace/WorkFlowPrj
  ├──→ 子进程: lexis-mcp (stdio) ──→ 卷挂载: /workspace/WorkFlowPrj
  ├──→ 子进程: token-savior (stdio) ──→ 卷挂载: /workspace/WorkFlowPrj
  └──→ 子进程: csproj-context (stdio) ──→ 卷挂载: /workspace/WorkFlowPrj
```

### 2.4 基础镜像选择分析

| 对比项 | Debian 12 Slim ✅ | Ubuntu 24.04 | Alpine Linux |
|--------|-------------------|--------------|--------------|
| **镜像大小** | **~80MB** | ~200MB | **~5MB** |
| **最终镜像预估** | **~1.2GB** | ~1.5GB | ~1.0GB（但兼容性差） |
| **稳定性** | ⭐⭐⭐ 极稳定 | ⭐⭐⭐ 稳定 | ⭐⭐ 包版本更新快 |
| **.NET 兼容性** | ✅ 官方支持 | ✅ 官方支持 | ⚠️ 需要 musl 兼容层 |
| **Python 兼容性** | ✅ 源码编译完美 | ✅ 原生支持 | ⚠️ musl 兼容问题 |
| **Node.js 兼容性** | ✅ 官方支持 | ✅ 官方支持 | ✅ 官方支持 |
| **社区支持** | ⭐⭐⭐ 文档丰富 | ⭐⭐⭐ 文档丰富 | ⭐⭐ 小众问题难查 |
| **长期支持** | 5年（2028+） | 5年（LTS） | 约2年 |

**结论**：Debian 12 Slim 在体积、稳定性、兼容性之间取得了最佳平衡。Python 需源码编译（增加首次构建时间），但产物会被 Docker 层缓存。

---

## 3. 环境要求

### 3.1 宿主机 (Windows)

| 组件 | 要求 | 说明 |
|------|------|------|
| 操作系统 | Windows 10/11 Pro/Enterprise | 需要 WSL2 支持 |
| Docker Desktop | 4.x+ | [下载地址](https://www.docker.com/products/docker-desktop/) |
| WSL2 | 已安装并设为默认 | Docker Desktop 安装向导会自动配置 |
| VS Code | 最新版 | [下载地址](https://code.visualstudio.com/) |
| CLine 扩展 | 最新版 | VS Code 扩展市场安装 |

### 3.2 容器内环境

| 组件 | 版本 | 说明 |
|------|------|------|
| 基础系统 | Debian 12 Slim (Bookworm) | 镜像仅 ~80MB |
| Node.js | 22.x LTS | 当前最新 LTS |
| .NET SDK | 10.0 | 与项目一致 |
| Python | 3.13 | 源码编译 |
| Tini | 最新版 | 容器 init 进程 |

---

## 4. 目录结构

```
Docker/
├── Dockerfile                    # 多阶段构建 (8 个阶段)
├── docker-compose.yml            # 服务编排
├── .dockerignore                 # 构建忽略文件
├── Makefile                      # 常用命令快捷方式
│
├── scripts/
│   ├── entrypoint.sh             # 容器入口脚本
│   ├── healthcheck.sh            # 健康检查脚本
│   └── build-mcp-servers.sh      # 安装本地 MCP 服务器
│
├── mcp-gateway/                  # MCP HTTP/SSE 网关
│   ├── package.json
│   ├── tsconfig.json
│   └── src/
│       ├── index.ts              # 入口
│       ├── gateway.ts            # 网关核心
│       ├── mcp-manager.ts        # MCP 子进程管理器
│       ├── sse-transport.ts      # SSE 传输适配
│       └── config.ts             # MCP 服务器注册配置
│
├── config/
│   ├── mcp-servers.json          # MCP 服务器清单
│   └── cline_mcp_settings.json   # 生成的 CLine 配置模板
│
└── docs/
      ├── README.md               # 快速开始
      ├── BUILD.md                # 构建指南
      └── UPDATE.md               # 更新指南
```

---

## 5. Dockerfile 详解

### 5.1 完整 Dockerfile

```dockerfile
# ============================================================
# WorkFlowPrj MCP 开发环境 Dockerfile
# 多阶段构建 | 高性能 | 分层缓存
# ============================================================

# ===== Stage 1: 基础系统 (Debian 12 Slim) =====
FROM debian:12-slim AS base

ENV DEBIAN_FRONTEND=noninteractive \
    TZ=Asia/Shanghai \
    LANG=en_US.UTF-8

RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates curl wget git unzip xz-utils \
    tzdata locales gnupg tini \
    && ln -fs /usr/share/zoneinfo/Asia/Shanghai /etc/localtime \
    && dpkg-reconfigure --frontend noninteractive tzdata \
    && locale-gen en_US.UTF-8 \
    && rm -rf /var/lib/apt/lists/*

# ===== Stage 2: Node.js 22 LTS =====
FROM base AS nodejs

RUN curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/* \
    && npm install -g npm@latest

RUN node --version && npm --version

# ===== Stage 3: .NET SDK 10.0 =====
FROM nodejs AS dotnet

RUN wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
    && ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
    && rm /tmp/dotnet-install.sh

RUN dotnet --version && dotnet --list-sdks

# ===== Stage 4: Python 3.13 (源码编译) =====
FROM dotnet AS python

RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential zlib1g-dev libncurses5-dev libgdbm-dev libnss3-dev \
    libssl-dev libreadline-dev libffi-dev libsqlite3-dev libbz2-dev \
    && rm -rf /var/lib/apt/lists/*

RUN wget https://www.python.org/ftp/python/3.13.3/Python-3.13.3.tgz -O /tmp/python.tgz \
    && tar -xzf /tmp/python.tgz -C /tmp/ \
    && cd /tmp/Python-3.13.3 \
    && ./configure --enable-optimizations --with-ensurepip=install \
    && make -j$(nproc) && make install \
    && rm -rf /tmp/python.tgz /tmp/Python-3.13.3

RUN python3 -m pip install --upgrade pip setuptools wheel
RUN python3 --version && pip3 --version

# ===== Stage 5: 全局 npm 包 =====
FROM python AS npm-global

RUN npm install -g context-mode lexis-mcp

# ===== Stage 6: 本地 MCP 服务器 =====
FROM npm-global AS mcp-local

WORKDIR /opt/mcp-servers
COPY Docker/scripts/build-mcp-servers.sh /tmp/
RUN bash /tmp/build-mcp-servers.sh

# ===== Stage 7: 构建 MCP 网关 (Alpine 构建阶段) =====
FROM node:22-alpine AS gateway-builder

WORKDIR /build
COPY Docker/mcp-gateway/ .
RUN npm ci && npm run build

# ===== Stage 8: 最终镜像 =====
FROM mcp-local AS final

COPY --from=gateway-builder /build/dist /opt/mcp-gateway/dist
COPY --from=gateway-builder /build/node_modules /opt/mcp-gateway/node_modules
COPY --from=gateway-builder /build/package.json /opt/mcp-gateway/
COPY Docker/scripts/entrypoint.sh /entrypoint.sh
COPY Docker/scripts/healthcheck.sh /healthcheck.sh
COPY Docker/config/mcp-servers.json /opt/mcp-gateway/config.json

RUN chmod +x /entrypoint.sh /healthcheck.sh

EXPOSE 3100-3107
WORKDIR /workspace/WorkFlowPrj
ENTRYPOINT ["/usr/bin/tini", "--", "/entrypoint.sh"]
```

### 5.2 构建优化说明

| 优化策略 | 实现方式 | 效果 |
|----------|----------|------|
| **多阶段构建** | 8 个阶段，最终镜像只保留运行所需 | 减小最终镜像体积 |
| **层缓存** | 不常变的基础层放前面，频繁变的 MCP 配置放最后 | 加速重复构建 |
| **独立构建阶段** | MCP 网关在 Alpine 中构建，产物复制到最终镜像 | 构建工具不进入运行时 |
| **Tini init 进程** | 作为 PID 1，正确处理信号转发 | 避免僵尸进程 |
| **--no-install-recommends** | 只安装必要包 | 减小镜像体积 |

---

## 6. MCP HTTP/SSE 网关

### 6.1 设计思路

MCP 协议原生支持 stdio 通信。为了让容器内的 MCP 服务器与主机上的 CLine 通信，需要将 stdio 包装为 HTTP/SSE 协议。

**MCP 网关** 是一个 Node.js 服务，负责：
1. 启动每个 MCP 服务器作为子进程（stdio 模式）
2. 将子进程的 stdin/stdout 桥接到 HTTP/SSE 端点
3. 每个 MCP 服务器监听独立端口（3100-3107）

### 6.2 核心代码

#### config.ts — MCP 服务器注册配置

```typescript
export interface McpServerConfig {
  name: string;
  port: number;
  command: string;
  args: string[];
  env?: Record<string, string>;
}

export const mcpServers: McpServerConfig[] = [
  { name: 'mcp-sequentialthinking-tools', port: 3100,
    command: 'npx', args: ['-y', 'mcp-sequentialthinking-tools'],
    env: { MAX_HISTORY_SIZE: '1000' } },
  { name: 'context7-mcp', port: 3101,
    command: 'node', args: ['/opt/mcp-servers/context7-mcp/index.js'] },
  { name: 'context-mode', port: 3102,
    command: 'node', args: ['/usr/local/lib/node_modules/context-mode/start.mjs'] },
  { name: 'memory', port: 3103,
    command: 'node', args: ['/opt/mcp-servers/memory/index.js'],
    env: { MEMORY_FILE_PATH: '/opt/mcp-data/memory.json' } },
  { name: 'filesystem', port: 3104,
    command: 'node', args: ['/opt/mcp-servers/filesystem/index.js', '/workspace/WorkFlowPrj'] },
  { name: 'lexis-mcp', port: 3105,
    command: 'node', args: ['/usr/local/lib/node_modules/lexis-mcp/dist/cli/main.js', 'mcp', '--path', '/workspace/WorkFlowPrj'] },
  { name: 'token-savior-recall', port: 3106,
    command: 'python3', args: ['-m', 'token_savior', 'mcp'],
    env: { WORKSPACE_ROOTS: '/workspace/WorkFlowPrj', TOKEN_SAVIOR_CLIENT: 'claude-code', TOKEN_SAVIOR_PROFILE: 'optimized' } },
  { name: 'csproj-context', port: 3107,
    command: 'node', args: ['/workspace/WorkFlowPrj/CsprojContext/build/index.js'] },
];
```

#### mcp-manager.ts — 子进程管理器

```typescript
import { spawn, ChildProcess } from 'child_process';
import { McpServerConfig } from './config';

export class McpManager {
  private processes = new Map<string, { config: McpServerConfig; process: ChildProcess }>();

  async startAll(configs: McpServerConfig[]): Promise<void> {
    for (const config of configs) await this.startOne(config);
  }

  async startOne(config: McpServerConfig): Promise<void> {
    const proc = spawn(config.command, config.args, {
      env: { ...process.env, ...config.env },
      stdio: ['pipe', 'pipe', 'pipe'],
    });
    proc.stdout?.on('data', (data) => this.broadcast(config.name, data.toString()));
    proc.stderr?.on('data', (data) => console.error(`[${config.name}] ${data}`));
    proc.on('exit', (code) => {
      console.log(`[${config.name}] exited with code ${code}`);
      setTimeout(() => this.startOne(config), 1000); // 自动重启
    });
    this.processes.set(config.name, { config, process: proc });
    console.log(`[${config.name}] started on port ${config.port}`);
  }

  private broadcast(name: string, data: string): void { /* SSE 广播 */ }

  async stopAll(): Promise<void> {
    for (const [, managed] of this.processes) managed.process.kill();
    this.processes.clear();
  }
}
```

#### gateway.ts — HTTP/SSE 网关核心

```typescript
import http from 'http';
import { McpManager } from './mcp-manager';
import { mcpServers } from './config';

export class McpGateway {
  private manager = new McpManager();

  async start(): Promise<void> {
    await this.manager.startAll(mcpServers);
    for (const config of mcpServers) this.createServer(config);
    console.log(`MCP Gateway started: ${mcpServers.map(s => `${s.name}:${s.port}`).join(', ')}`);
  }

  private createServer(config: typeof mcpServers[0]): void {
    const server = http.createServer((req, res) => {
      if (req.method === 'GET' && req.url === '/mcp') {
        res.writeHead(200, {
          'Content-Type': 'text/event-stream',
          'Cache-Control': 'no-cache',
          'Connection': 'keep-alive',
          'Access-Control-Allow-Origin': '*',
        });
        const keepAlive = setInterval(() => res.write(':keepalive\n\n'), 15000);
        req.on('close', () => clearInterval(keepAlive));
      } else if (req.method === 'POST' && req.url === '/mcp') {
        let body = '';
        req.on('data', chunk => body += chunk);
        req.on('end', () => this.forwardRequest(config.name, body, res));
      } else { res.writeHead(404); res.end(); }
    });
    server.listen(config.port, '0.0.0.0');
  }

  private forwardRequest(name: string, body: string, res: http.ServerResponse): void {
    // 将 HTTP 请求转发到 MCP 子进程的 stdin，将 stdout 写回 HTTP 响应
  }
}
```

---

## 7. docker-compose 配置

```yaml
version: "3.9"

services:
  mcp-env:
    build:
      context: ..
      dockerfile: Docker/Dockerfile
    image: workflow-mcp-env:latest
    container_name: workflow-mcp-env
    restart: unless-stopped

    ports:
      - "3100-3107:3100-3107"

    volumes:
      - D:\Project\Program\WorkFlowPrj:/workspace/WorkFlowPrj
      - mcp-data:/opt/mcp-data
      - npm-cache:/root/.npm

    environment:
      - TZ=Asia/Shanghai
      - DOTNET_CLI_TELEMETRY_OPTOUT=1
      - PYTHONUNBUFFERED=1

    deploy:
      resources:
        limits:   { cpus: "4", memory: "8G" }
        reservations: { cpus: "2", memory: "4G" }

    healthcheck:
      test: ["CMD", "/healthcheck.sh"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 30s

volumes:
  mcp-data:
  npm-cache:
```

> 端口映射与 §6.2 config.ts 中的定义一致：3100-3107 分别对应 8 个 MCP 服务。

---

## 8. CLine 配置

将以下配置添加到 `%APPDATA%\Code\User\globalStorage\saoudrizwan.claude-dev\settings\cline_mcp_settings.json`：

```json
{
  "mcpServers": {
    "mcp-sequentialthinking-tools": {
      "type": "http", "url": "http://localhost:3100/mcp",
      "autoApprove": ["sequentialthinking_tools"], "disabled": false
    },
    "context7-mcp": {
      "type": "http", "url": "http://localhost:3101/mcp",
      "autoApprove": ["resolve-library-id", "query-docs"], "disabled": false
    },
    "context-mode": {
      "type": "http", "url": "http://localhost:3102/mcp",
      "autoApprove": ["ctx_batch_execute","ctx_execute","ctx_execute_file","ctx_index","ctx_search","ctx_fetch_and_index","ctx_stats","ctx_doctor","ctx_upgrade","ctx_purge","ctx_insight"],
      "disabled": false
    },
    "memory": {
      "type": "http", "url": "http://localhost:3103/mcp",
      "autoApprove": ["create_entities","create_relations","add_observations","read_graph","search_nodes","open_nodes"],
      "disabled": false
    },
    "filesystem": {
      "type": "http", "url": "http://localhost:3104/mcp",
      "autoApprove": ["read_file","read_media_file","read_multiple_files","write_file","create_directory","move_file","list_directory","list_directory_with_sizes","directory_tree","get_file_info","search_files"],
      "disabled": false
    },
    "lexis-mcp": {
      "type": "http", "url": "http://localhost:3105/mcp",
      "autoApprove": ["search_code","get_symbol","find_references","find_file","list_symbols","pattern_search","call_chain","list_entrypoints","impact_analysis","dead_code","find_writes","interface_implementations","event_handlers"],
      "disabled": false
    },
    "token-savior-recall": {
      "type": "http", "url": "http://localhost:3106/mcp",
      "autoApprove": [], "disabled": false
    },
    "csproj-context": {
      "type": "http", "url": "http://localhost:3107/mcp",
      "autoApprove": ["get_overview","get_detail","search_modules","get_by_layer","get_dependencies","get_layer_map","get_project_tree","validate_rules","set_module_rule"],
      "disabled": false
    }
  }
}
```

> **配置说明**：`type: "http"` 使用 HTTP/SSE 传输协议；`url` 指向 Docker 映射到主机的端口；`autoApprove` 列出自动批准的工具以减少交互确认。

---

## 9. 安装步骤

### 步骤 1：安装 Docker Desktop

```powershell
# 下载安装 https://www.docker.com/products/docker-desktop/
# 安装时选择 WSL2 后端，完成后重启电脑
docker --version
docker info
```

### 步骤 2：创建目录结构

```powershell
mkdir Docker\scripts, Docker\mcp-gateway\src, Docker\config, Docker\docs -Force
```

### 步骤 3：创建所有文件

| 文件 | 用途 |
|------|------|
| `Docker/Dockerfile` | 多阶段构建 |
| `Docker/docker-compose.yml` | 服务编排 |
| `Docker/.dockerignore` | 构建忽略 |
| `Docker/Makefile` | 快捷命令 |
| `Docker/scripts/entrypoint.sh` | 容器入口 |
| `Docker/scripts/healthcheck.sh` | 健康检查 |
| `Docker/scripts/build-mcp-servers.sh` | 安装 MCP 服务器 |
| `Docker/mcp-gateway/package.json` | 网关依赖 |
| `Docker/mcp-gateway/tsconfig.json` | TypeScript 配置 |
| `Docker/mcp-gateway/src/index.ts` | 网关入口 |
| `Docker/mcp-gateway/src/config.ts` | MCP 配置 |
| `Docker/mcp-gateway/src/gateway.ts` | 网关核心 |
| `Docker/mcp-gateway/src/mcp-manager.ts` | 进程管理 |
| `Docker/config/mcp-servers.json` | 服务器清单 |
| `Docker/config/cline_mcp_settings.json` | CLine 配置模板 |

### 步骤 4：构建并启动

```powershell
# 构建镜像
docker compose -f Docker/docker-compose.yml build

# 启动容器
docker compose -f Docker/docker-compose.yml up -d

# 验证所有 MCP 服务
docker exec workflow-mcp-env curl http://localhost:3100/mcp
docker exec workflow-mcp-env curl http://localhost:3101/mcp
# ... 逐一验证 3100-3107
```

### 步骤 5：配置 CLine

将 `Docker/config/cline_mcp_settings.json` 合并到 `%APPDATA%\Code\User\globalStorage\saoudrizwan.claude-dev\settings\cline_mcp_settings.json`，然后重启 VS Code。

### 步骤 6：验证

在 CLine 中检查 MCP 服务器状态，确认所有 8 个服务器显示为 ✅ 正常。

---

## 10. 日常使用

### 10.1 Makefile 快捷命令

```powershell
make build       # 构建镜像
make up          # 启动容器
make down        # 停止容器
make restart     # 重启容器
make logs        # 查看日志
make shell       # 进入容器 Shell
make dotnet-build  # 编译 C#
make python-run cmd="PythonModel/DeepSeekInvoke.py"  # 运行 Python
make clean       # 清理
```

### 10.2 手动 Docker 命令

```powershell
# 进入容器
docker exec -it workflow-mcp-env bash

# 在容器内执行命令（编译 C# / 运行 Python / 测试）
docker exec workflow-mcp-env dotnet build CsharpPrj/MainProj.sln
docker exec workflow-mcp-env python PythonModel/DeepSeekInvoke.py
docker exec workflow-mcp-env npm test

# 容器管理
docker ps -a --filter name=workflow-mcp-env
docker stats workflow-mcp-env
docker logs -f workflow-mcp-env
```

> 进入容器后，可直接在 `/workspace/WorkFlowPrj` 下执行 `dotnet build`、`python` 等命令。

---

## 11. 更新指南

### 11.1 更新 MCP 服务器版本

```bash
# 修改 Docker/scripts/build-mcp-servers.sh 中的版本号
# 然后重建
make build && make up
```

### 11.2 更新 .NET SDK 版本

```dockerfile
# 修改 Dockerfile Stage 3 的 --channel 参数
RUN /tmp/dotnet-install.sh --channel 9.0 --install-dir /usr/share/dotnet
```

### 11.3 更新 Python 版本

```dockerfile
# 修改 Dockerfile Stage 4 的下载 URL 和版本号
RUN wget https://www.python.org/ftp/python/3.14.0/Python-3.14.0.tgz -O /tmp/python.tgz \
    && tar -xzf /tmp/python.tgz -C /tmp/ \
    && cd /tmp/Python-3.14.0 \
    && ./configure --enable-optimizations --with-ensurepip=install \
    && make -j$(nproc) && make install
```

### 11.4 更新 Node.js 版本

```dockerfile
# 修改 Dockerfile Stage 2 的 setup_xx.x
RUN curl -fsSL https://deb.nodesource.com/setup_23.x | bash -
```

### 11.5 添加新的 MCP 服务器

1. 在 `Docker/mcp-gateway/src/config.ts` 中添加新配置
2. 在 `Docker/scripts/build-mcp-servers.sh` 中添加安装命令
3. 在 `Docker/config/cline_mcp_settings.json` 中添加 CLine 配置
4. 在 `Docker/docker-compose.yml` 中添加端口映射
5. 重建并重启：`make build && make up`

---

## 12. 故障排除

### 12.1 容器无法启动

```powershell
docker logs workflow-mcp-env
netstat -ano | findstr "3100"   # 检查端口冲突
docker info                     # 检查 Docker 服务状态
```

### 12.2 MCP 服务连接失败

```powershell
docker ps --filter name=workflow-mcp-env
curl http://localhost:3100/mcp
docker exec workflow-mcp-env curl http://localhost:3100/mcp
```

### 12.3 文件权限问题

```bash
docker exec workflow-mcp-env ls -la /workspace/WorkFlowPrj
```

### 12.4 性能问题

```powershell
docker stats workflow-mcp-env
# 修改 docker-compose.yml 中的资源限制后重启
make down && make up
```

### 12.5 构建失败

```powershell
docker compose -f Docker/docker-compose.yml build --no-cache 2>&1
docker build -f Docker/Dockerfile --check .
docker builder prune -f
docker compose -f Docker/docker-compose.yml build --no-cache
```

### 12.6 WSL2 相关问题

```powershell
wsl --status
wsl --list --verbose
wsl --shutdown   # 重启 WSL2
# 如果文件 I/O 慢，将项目移到 WSL2 文件系统中
```

---

## 附录 A：参考资源

| 资源 | 链接 |
|------|------|
| Docker Desktop for Windows | https://www.docker.com/products/docker-desktop/ |
| MCP 协议规范 | https://modelcontextprotocol.io/ |
| .NET SDK 下载 | https://dotnet.microsoft.com/download |
| Node.js 下载 | https://nodejs.org/ |
| Debian 12 Slim | https://www.debian.org/distrib/ |
| CLine 扩展 | VS Code 扩展市场搜索 "Cline" |

---

## 附录 B：版本历史

| 版本 | 日期 | 变更说明 |
|------|------|----------|
| v1.0 | 2026-06-23 | 初始版本（Ubuntu 24.04 基础） |
| v1.1 | 2026-06-23 | 基础镜像切换为 Debian 12 Slim，添加基础镜像选择分析 |
| v2.0 | 2026-06-23 | 修复 Dockerfile Python 阶段重复逻辑错误；修复 §11.3 Python 更新方式（源码编译）；修复 §2.4 标题级别；合并 §10.3/10.4 到 §10.2；精简 §6.2/§8.1/§9 冗余内容；移除 §8.2 配置说明（合并到注释） |

---

*文档生成时间：2026-06-23*
*生成环境：WorkFlowPrj 项目*
