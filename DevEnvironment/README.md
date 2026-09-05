# DevEnvironment — 开发环境概览

> **WorkFlowPrj 开发环境**  
> 统一容器化开发环境，集成 MCP 服务集群 + .NET SDK + Python 运行时

---

## 📋 目录结构

```
DevEnvironment/
├── README.md                          # 本文件 — 环境概览
├── Docker_MCP_环境搭建指南.md          # 详细搭建指南（767 行）
│
└── Docker/                            # Docker 环境核心目录
    ├── Dockerfile                     # 多阶段构建（8 阶段）
    ├── docker-compose.yml             # 服务编排
    ├── .dockerignore                  # 构建忽略
    ├── Makefile                       # 快捷命令
    │
    ├── scripts/
    │   ├── entrypoint.sh              # 容器入口
    │   ├── healthcheck.sh             # 健康检查
    │   └── build-mcp-servers.sh       # MCP 服务器安装脚本
    │
    ├── mcp-gateway/                   # MCP HTTP/SSE 网关（TypeScript）
    │   ├── package.json
    │   ├── tsconfig.json
    │   └── src/
    │       ├── index.ts               # 入口
    │       ├── gateway.ts             # 网关核心
    │       ├── mcp-manager.ts         # 子进程管理器
    │       └── config.ts              # MCP 服务器注册配置
    │
    ├── config/
    │   ├── mcp-servers.json           # MCP 服务器清单
    │   └── cline_mcp_settings.json    # CLine 配置模板
    │
    └── docs/
        └── INSTALL_STEPS.md           # 安装步骤速查
```

---

## 🏗️ 架构总览

```
Windows 11 (宿主机)
│
├── VS Code + CLine 扩展
│     └── cline_mcp_settings.json → type: "http"
│           └── HTTP/SSE → Docker 容器
│
└── Docker Desktop (WSL2)
      └── 容器: workflow-mcp-env (Debian 12 Slim)
            ├── MCP 服务集群 (端口 3100-3106)
            ├── .NET SDK 10.0
            ├── Python 3.13
            └── Node.js 22 LTS
```

---

## 🚀 快速开始

### 启动环境

```bash
# 从 DevEnvironment 目录执行
cd D:\Project\Program\WorkFlowPrj\DevEnvironment

# 启动容器（首次自动构建）
docker compose -f Docker/docker-compose.yml up -d

# 查看启动日志
docker logs workflow-mcp-env
```

### 停止环境

```bash
docker compose -f Docker/docker-compose.yml down
```

### 重新构建

```bash
docker compose -f Docker/docker-compose.yml build
docker compose -f Docker/docker-compose.yml up -d
```

### 进入容器

```bash
docker exec -it workflow-mcp-env bash
```

---

## 🧩 MCP 服务清单

| 端口 | 服务名称 | 类型 | 说明 |
|------|----------|------|------|
| 3100 | `mcp-sequentialthinking-tools` | Node.js | 分步推理工具 |
| 3101 | `context7-mcp` | Node.js | 第三方库文档查询 |
| 3102 | `context-mode` | Node.js | 上下文管理（沙箱执行/索引/搜索） |
| 3103 | `memory` | Node.js | 知识图谱持久化 |
| 3104 | `filesystem` | Node.js | 文件系统安全访问 |
| 3105 | `lexis-mcp` | Node.js | 代码搜索/分析（28+ 工具） |
| 3106 | `token-savior-recall` | Python | 符号索引/代码分析 |

> **注意**：`csproj-context`（端口 3107）需要在项目内构建后启用，当前已注释。

---

## 🛠️ 容器内环境

| 组件 | 版本 | 用途 |
|------|------|------|
| 基础系统 | Debian 12 Slim | 轻量稳定 |
| Node.js | v22.14.0 | MCP 网关 + JS/TS 运行 |
| npm | 11.17.0 | 包管理 |
| .NET SDK | 10.0.301 | C# 项目编译运行 |
| Python | 3.13.3 | Python 模型运行 |
| Git | 2.39.5 | 版本控制 |

---

## 🔄 数据流

```
CLine (VS Code)
  │
  │ HTTP POST /mcp (JSON-RPC over SSE)
  ▼
MCP Gateway (容器内, localhost:3100-3106)
  │
  ├──→ 子进程: sequential-thinking (stdio)
  ├──→ 子进程: context7-mcp (stdio)
  ├──→ 子进程: context-mode (stdio)
  ├──→ 子进程: memory (stdio)
  ├──→ 子进程: filesystem (stdio) ──→ /workspace/WorkFlowPrj
  ├──→ 子进程: lexis-mcp (stdio) ──→ /workspace/WorkFlowPrj
  └──→ 子进程: token-savior (stdio) ──→ /workspace/WorkFlowPrj
```

项目代码通过卷挂载从主机映射到容器 `/workspace/WorkFlowPrj`，修改即时生效。

---

## 📚 参考文档

| 文档 | 说明 |
|------|------|
| [Docker_MCP_环境搭建指南.md](./Docker_MCP_环境搭建指南.md) | 完整搭建指南（767 行） |
| [Docker/docs/INSTALL_STEPS.md](./Docker/docs/INSTALL_STEPS.md) | 安装步骤速查 |
| [Docker/Makefile](./Docker/Makefile) | 快捷命令 |

---

## ⚠️ 已知问题

1. **`csproj-context` 暂不可用** — 需要在项目内构建 `CsprojContext` 后，取消 `config.ts` 中的注释并重建镜像
2. **`token-savior-recall` 向量搜索未启用** — 需要安装 `token-savior-recall[memory-vector]` 扩展包
3. **首次构建较慢** — Python 源码编译约 10-15 分钟，后续构建使用缓存
