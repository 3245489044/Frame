# WorkFlowPrj（工作流框架）

简体中文 | [English](README.en.md)

## 项目简介

**WorkFlowPrj** 是一款基于 .NET 10.0 平台构建的企业级模块化软件开发框架，采用 C# 12 语言特性，融合 PowerShell 脚本自动化、TypeScript 前端能力与 Python AI 集成。框架以「万物皆函数，一切皆配置」为设计哲学，通过分层架构将复杂业务逻辑分解为独立可组合的模块，支持依赖注入、事件驱动、响应式编程等现代软件工程实践。

框架深度集成 AI 能力（Microsoft.SemanticKernel 1.74 + DeepSeek）、动态代码编译（Roslyn）、Docker 容器化部署与 MCP（Model Context Protocol）服务生态，为开发者提供从底层基础设施到上层业务逻辑的完整工具链。

## 技术栈

| 领域 | 技术 |
|------|------|
| **后端** | .NET 10.0 + C# 12（可空引用类型、隐式 using） |
| **脚本** | PowerShell 5.1+（模块化脚本框架） |
| **前端** | TypeScript（pnpm + Turborepo + Project References 统一构建） |
| **AI** | Python 3.10+（DeepSeek 模型集成）+ SemanticKernel |
| **容器** | Docker + Docker Compose（MCP 服务集群） |
| **协议** | MCP（Model Context Protocol）服务网格 |
| **测试** | xUnit 2.6 + MSTest 4.2 + FluentAssertions + Moq |

## 解决方案总览

本项目包含 7 个独立解决方案，覆盖不同技术域：

| 解决方案 | 路径 | 说明 |
|----------|------|------|
| **MainProj.sln** | `CsharpPrj/MainProj.sln` | C# 主解决方案（基础层 → 模块层 → 共享层 → 操作层 → 平台层） |
| **FileDriven.sln** | `FileDriven/FileDriven.sln` | 文件驱动框架（配置驱动 UI 自动化 + CLI 双模式） |
| **Agent.sln** | `Agent/Agent.sln` | 声明式智能体系统（ReAct 循环 + MCP 工具调用） |
| **GameEditor.sln** | `Engine/GameEditor.sln` | 游戏编辑器引擎（ECS / Graphics / Scene / Windowing） |
| **TypeScript.sln** | `TypeScriptPrj/TypeScript.sln` | TypeScript 工作空间（MCP 框架 + 桥接 + 工具服务） |
| **Protocol.sln** | `CsharpPrj/Protocol/Protocol.sln` | 协议层（CSG 统一框架 + RGE） |
| **CodeAnalyzer.sln** | `Storage/Utilitys/CodeAnalyzer/CodeAnalyzer.sln` | 代码分析工具（分析 / 优化 / 提示词生成） |

## 项目结构

```
WorkFlowPrj/
├── CsharpPrj/                      # C# 主解决方案（MainProj.sln）
│   ├── BasicMethod/               # 基础方法库
│   │   ├── Map/                   #   函数式数据结构（Block/Calculus/Container/DecisionEngine/
│   │   │                          #   Declarative/Functor/GraphFoundation/Queue/SortMent/
│   │   │                          #   StateMachine/Transformers）
│   │   ├── RefectionMethod/       #   反射方法
│   │   ├── JsonProcess/           #   JSON 处理（JsonUIBeautifier / ProcessJsonConfig）
│   │   ├── PathManager/           #   路径管理
│   │   ├── ReactiveData/          #   响应式数据
│   │   ├── Logging/               #   日志
│   │   ├── ContextFrame/          #   上下文框架
│   │   ├── ECS/                   #   实体组件系统
│   │   ├── FrameMethod/           #   框架方法
│   │   ├── UniversalLayout/       #   通用布局
│   │   ├── Framework/             #   框架核心 + 抽象
│   │   └── Common/                #   通用功能
│   ├── Modules/                   # 模块层
│   │   ├── Pipeline/              #   数据处理流水线
│   │   ├── Registry/              #   函数注册表
│   │   ├── ParameterResolver/     #   参数解析器（KeyMapping 映射）
│   │   ├── EventFramework/        #   事件框架
│   │   ├── InternalStorage/       #   内部存储
│   │   ├── DiagnosticContext/     #   诊断上下文
│   │   ├── GraphProcessing/       #   图处理
│   │   ├── LogicComposition/      #   逻辑组合
│   │   ├── ComponentData/         #   组件数据
│   │   ├── Parsing/               #   解析器
│   │   ├── Threading/             #   线程管理
│   │   ├── Timing/                #   定时器
│   │   ├── MetaCommand/           #   元命令
│   │   ├── ValidationFramework/   #   验证框架
│   │   └── ExecutionFramework/    #   执行框架
│   ├── Share/                     # 共享层
│   │   ├── UniversalReference/    #   统一引用（UniversalWorkflowFramework）
│   │   ├── GraphFramework/        #   图框架
│   │   ├── CommandFramework/      #   命令框架
│   │   ├── PlaceholderFramework/  #   占位符框架
│   │   ├── TreeTraceFramework/    #   树追踪框架
│   │   ├── Diagnostics/           #   诊断
│   │   ├── StateManagement/       #   状态管理
│   │   ├── Adapters/              #   适配器
│   │   └── ...                    #   其他共享组件
│   ├── Operation/                 # 操作层
│   │   ├── WorkFlowMethod/        #   工作流方法 + 执行引擎
│   │   ├── BackEndDriven/         #   后端驱动
│   │   ├── FrontEndDriven/        #   前端驱动
│   │   └── StateHoldDriven/       #   状态保持驱动
│   ├── Plate/                     # 平台层
│   │   ├── RoslynMethod/          #   Roslyn 动态编译
│   │   ├── DataManage/            #   数据管理
│   │   ├── Mathematics/           #   数学库
│   │   ├── JsonMethod/            #   JSON 方法
│   │   └── TConfig/               #   配置工具
│   └── Protocol/                  # 协议层
│       ├── CSG/                   #   CSG 统一框架（Configuration-StateMachine-Graph）
│       └── RGE/                   #   RGE 协议
├── FileDriven/                    # 文件驱动框架（FileDriven.sln）
│   ├── FileDriven.Launcher/       #   启动入口
│   ├── FileDriven.Business/       #   业务逻辑
│   ├── FileDriven.Execution/      #   执行引擎
│   ├── FileDriven.Pipeline/       #   管道
│   ├── FileDriven.JsonRefer/      #   JSON 引用
│   ├── FileDriven.Abstractions/   #   抽象
│   ├── FileDriven.Utilities/      #   工具
│   ├── DrivenConfig/              #   配置目录（JsonConfig UI 配置）
│   └── framework.config.json      #   框架配置
├── Agent/                         # 智能体系统（Agent.sln）
│   └── src/
│       ├── Agent.Core/            #   核心（AgentOrchestrator / AgentLoopFunction）
│       ├── Agent.Planner/         #   LLM 规划器（SemanticKernel + DeepSeek）
│       ├── Agent.McpTools/        #   MCP 工具桥接
│       └── Agent.Guardrails/      #   安全护栏
├── Engine/                        # 游戏编辑器引擎（GameEditor.sln）
│   ├── GameEditor.Configuration/  #   配置
│   ├── GameEditor.ECS/            #   实体组件系统
│   ├── GameEditor.Graphics/       #   图形（+ D3D11 后端）
│   ├── GameEditor.Scene/          #   场景
│   ├── GameEditor.Windowing/      #   窗口
│   ├── GameEditor.Standalone/     #   独立运行
│   └── GameEditor.VirtualMatrix/  #   虚拟矩阵
├── TypeScriptPrj/                 # TypeScript 工作空间（TypeScript.sln）
│   ├── TsWorkspace/
│   │   ├── BasicMethod/           #   基础方法（Pipeline / HealthChecker / RequestRouter）
│   │   ├── MCPFramework/          #   MCP 框架（服务器/客户端/工具注册/SSE）
│   │   ├── MCPBridge/             #   MCP 桥接（Docker 容器代理 / 健康检查 / 路由）
│   │   ├── CsprojContext/         #   C# 项目上下文 MCP 服务
│   │   └── ClineApi/              #   Cline API 集成
│   ├── TsSln/                     #   构建工具
│   └── build/                     #   构建脚本
├── PowerShellScript/              # PowerShell 脚本体系
│   ├── BasicMethod/               #   通用模块（Core / FileOps / DevOps / Tooling）
│   ├── Modules/                   #   扩展模块（Docker / Csproj / FileDriven / StringTools）
│   ├── Build/                     #   构建脚本（全量编译 / 清理 / MCP 部署）
│   ├── Tools/                     #   工具脚本（Docker 管理）
│   ├── AIDecisionFramework/       #   AI 决策框架
│   ├── PScriptManager/            #   脚本管理框架
│   ├── DSL/                       #   领域特定语言
│   ├── Git/                       #   Git 操作脚本
│   └── ScriptLauncher.ps1         #   脚本启动器
├── PythonModel/                   # Python AI 模型集成
│   ├── clients/                   #   AI 客户端（base / deepseek）
│   ├── DeepSeekInvoke.py          #   DeepSeek 调用
│   ├── PythonModel.Bridge/        #   C# <-> Python 桥接
│   └── requirements.txt           #   Python 依赖
├── Boundary/                      # 边界测试框架（LLM 测试设计）
├── Storage/                       # 存储与工具集
│   └── Utilitys/
│       ├── CodeAnalyzer/          #   代码分析器（Analysis / Optimization / Prompt）
│       ├── CodePathExplorer/      #   代码路径探索器
│       ├── CodeRLFramework/       #   代码强化学习框架
│       ├── Agent/                 #   Agent 工具
│       └── Tools/                 #   工具集（CodeConverter 等）
├── CentralTest/                   # 集中测试
│   ├── CSharpTest/                #   C# 测试（BasicMethod / Modules / Operation / Share / Protocol）
│   ├── AgentTest/                 #   Agent 测试
│   ├── PowerShellTests/           #   PowerShell 测试
│   └── TypeScriptTest/            #   TypeScript 测试
├── DevEnvironment/                # 开发环境配置
│   └── Docker/                    #   Docker 配置（Dockerfile / docker-compose / Makefile）
├── EnableMent/                    # 启动脚本
│   ├── UI启动.bat                  #   GUI 模式启动
│   ├── CLI启动.bat                 #   CLI 模式启动
│   ├── 交互启动.bat                #   交互式脚本启动
│   └── 编译C#项目.bat              #   C# 项目编译
├── DataStorage/                   # 数据存储（SQLite / LiteDB 数据库文件）
├── docs/                          # 文档目录
├── Instruction/                   # 工程规范（spec.md / mcp.md）
├── credentials/                   # 凭据配置
└── vscode-plugin-framework/       # VS Code 插件框架
```

## 架构分层

框架采用严格的分层架构，下层不依赖上层，上层可引用下层：

```
协议层 (0)  ── CSG 统一框架（Configuration → StateMachine → Graph 三投影变换）
    │
基础层 (1)  ── BasicMethod（反射 / 算法 / 函数式数据结构 / 通用功能）
    │
模块层 (2)  ── Modules（Pipeline / Registry / ParameterResolver / EventFramework / ...）
    │
共享层 (3)  ── Share（UniversalWorkflowFramework / GraphFramework / CommandFramework / ...）
    │
操作层 (4)  ── Operation（WorkFlowMethod 工作流引擎 / 函数执行引擎 / 驱动模式）
    │
应用层 (5)  ── FileDriven（配置驱动 UI 自动化 / CLI 双模式）
    │
平台层 (6)  ── Plate（RoslynMethod 动态编译 / DataManage / Mathematics / ...）
```

## 核心模块详解

### C# 后端

| 模块 | 所属层 | 说明 |
|------|--------|------|
| **CSG.Unified** | 协议层 | CSG 统一框架：C（配置）→ S（状态机）→ G（图）三投影变换引擎 |
| **BasicMethod** | 基础层 | 基础方法库：反射提取、数据处理流水线、图算法、函数注册与动态调用、函数式数据结构（Map 下的 11 个子模块） |
| **Pipeline** | 模块层 | 数据处理流水线：节点编排、数据流转 |
| **Registry** | 模块层 | 函数注册表：自动扫描注册、函数名调用 |
| **ParameterResolver** | 模块层 | 参数解析器：KeyMapping 映射框架、类型转换、多策略匹配 |
| **EventFramework** | 模块层 | 事件框架：事件发布/订阅、触发器 |
| **UniversalWorkflowFramework** | 共享层 | 统一工作流框架：WorkflowExecution 抽象 |
| **GraphFramework** | 共享层 | 图框架：图结构、遍历算法 |
| **CommandFramework** | 共享层 | 命令框架：命令模式封装 |
| **WorkFlowMethod** | 操作层 | 工作流引擎：Pipeline 流水线、节点执行器、会话管理、状态机驱动 |
| **FunctionExecutionFramework** | 操作层 | 函数执行引擎：表达式树编译、依赖注入、动态函数生成 |
| **FileDriven** | 应用层 | 文件驱动框架：配置驱动 UI 自动化、CLI 双模式运行 |
| **RoslynMethod** | 平台层 | Roslyn 动态代码编译：C# 脚本执行、动态程序集生成 |

### 智能体系统（Agent）

基于 WorkFlowMethod 框架构建的声明式 Agent，采用 ReAct（Reasoning + Acting）循环：

```
Agent (C#) ──stdio JSON-RPC──→ MCPBridge (Node.js) ──HTTP/SSE──→ Docker 容器内 MCP 服务器
```

| 模块 | 说明 |
|------|------|
| **Agent.Core** | 核心：AgentOrchestrator、AgentLoopFunction、DynamicWorkflowMerger |
| **Agent.Planner** | LLM 规划器：基于 SemanticKernel + DeepSeek API，Auto Function Calling |
| **Agent.McpTools** | MCP 工具桥接：通过 MCPBridge 调用 Docker 容器内 MCP 服务器 |
| **Agent.Guardrails** | 安全护栏：输入/输出安全过滤 |

工作流定义在 `Agent/Config/workflows/agent-loop.json`（v3），声明式编排 ReAct 循环：

```
start → guard_input → sk_plan → check_complete
                                    ├── complete → guard_output → end
                                    └── continue → execute_dynamic → guard_input（循环）
```

### PowerShell 脚本体系

| 模块 | 说明 |
|------|------|
| **BasicMethod** | 通用模块库：Core / FileOps（FileUtils / FileDiff / FileBatch）/ DevOps / Tooling |
| **Modules\Docker** | Docker 容器生命周期管理、MCP 服务安装、CLine 配置管理 |
| **PScriptManager** | 脚本管理框架：自动扫描注册、函数名调用、模块清单生成 |
| **AIDecisionFramework** | AI 决策框架：决策引擎、Token 核算、文件访问、工具桥接 |
| **Build** | 全量编译脚本：`Invoke-FullRebuild.ps1`（C# + TypeScript 联合编译）、清理、MCP 部署 |
| **ScriptLauncher** | 脚本启动器：交互式菜单 + 命令行模式 |

### TypeScript 前端

| 模块 | 说明 |
|------|------|
| **BasicMethod** | 基础方法（TS 版）：Pipeline、HealthChecker、RequestRouter |
| **MCPFramework** | MCP 框架：服务器/客户端抽象、工具注册、SSE 传输 |
| **MCPBridge** | MCP 桥接：Docker 容器内 MCP 服务代理、健康检查、请求路由 |
| **CsprojContext** | C# 项目上下文 MCP 服务：查询 .csproj 模块信息 |
| **ClineApi** | Cline API 集成 |

### AI 集成

| 模块 | 说明 |
|------|------|
| **PythonModel** | DeepSeek 模型调用、AI 客户端封装（base + deepseek） |
| **PythonModel.Bridge** | C# ↔ Python 桥接（PythonNet 3.1） |
| **SemanticKernel** | 工作流引擎中的 AI 节点：提示词工程、函数调用、记忆管理 |

### 工具集（Storage/Utilitys）

| 工具 | 说明 |
|------|------|
| **CodeAnalyzer** | 代码分析器：代码结构分析、优化建议、AI 提示词生成 |
| **CodePathExplorer** | 代码路径探索器 |
| **CodeRLFramework** | 代码强化学习框架 |
| **CodeConverter** | 代码转换工具 |

## Docker & MCP 生态

项目通过 Docker 容器化部署 MCP 服务集群，提供统一的开发环境：

- **容器**：`workflow-mcp-env`（含 .NET SDK 10.0、Node.js、Python 3.10）
- **端口映射**：3100-3107（8 个 MCP 服务）
- **资源限制**：CPU 4 核 / 内存 8G（预留 2 核 / 4G）
- **健康检查**：30s 间隔，3 次重试
- **数据卷**：项目目录挂载 + NuGet 缓存 + MCP 数据 + npm 缓存
- **管理脚本**：`PowerShellScript/Tools/Docker/Manage-Docker.ps1`（交互式菜单 + 命令行模式）
- **配置文件**：`DevEnvironment/Docker/docker-compose.yml`、`Dockerfile`、`Makefile`

```bash
# 启动 MCP 环境
cd DevEnvironment/Docker
docker compose up -d

# 或使用管理脚本
pwsh PowerShellScript/Tools/Docker/Manage-Docker.ps1
```

## 快速开始

### 环境准备

1. **.NET 10.0 SDK** — [下载](https://dotnet.microsoft.com/download)
2. **PowerShell 5.1+** — Windows 自带；Linux/macOS 安装 PowerShell 7
3. **Node.js 18+ 和 pnpm** — TypeScript 项目构建
4. **Docker Desktop**（可选）— MCP 服务集群
5. **Python 3.10+**（可选）— AI 集成

### 编译项目

```bash
# 克隆项目
git clone https://github.com/3245489044/ZhiLianStorage.git
cd ZhiLianStorage

# ── C# 主解决方案 ──
dotnet restore CsharpPrj/MainProj.sln
dotnet build CsharpPrj/MainProj.sln

# ── 文件驱动框架 ──
dotnet build FileDriven/FileDriven.sln

# ── Agent 智能体 ──
dotnet build Agent/Agent.sln

# ── 游戏编辑器引擎 ──
dotnet build Engine/GameEditor.sln

# ── TypeScript 工作空间 ──
cd TypeScriptPrj
pnpm install
node build/TsSln/cli.js build

# ── 一键全量编译（C# + TypeScript 联合）──
pwsh PowerShellScript/Build/Invoke-FullRebuild.ps1
```

### 启动方式

```bash
# GUI 模式（文件驱动框架配置选择器）
EnableMent\UI启动.bat

# 交互式脚本启动（PowerShell 脚本启动器）
EnableMent\交互启动.bat

# CLI 模式
EnableMent\CLI启动.bat

# 编译 C# 项目
EnableMent\编译C#项目.bat
```

### 运行测试

```bash
# C# 测试（集中测试入口）
dotnet test CentralTest/CSharpTest/CentralConsole.csproj

# Agent 测试
dotnet test CentralTest/AgentTest/AgentTest.csproj

# 或使用测试脚本
pwsh CentralTest/RunWdUITests.ps1
```

## 开发规范

### 代码风格

- **命名**：类型/方法 PascalCase，私有字段 `_camelCase`，接口 `I` 前缀
- **格式**：Allman 大括号，4 空格缩进，120 字符行限制
- **可空性**：全局启用 `Nullable` 和 `ImplicitUsings`
- **注释**：公共 API 必须包含 XML 文档注释（中文）
- **版本管理**：NuGet 包版本统一在 `CsharpPrj/versions.json` 中管理，由 `Generate-BuildProps.ps1` 生成构建属性

### Git 工作流

- **主分支**：`Main`
- **远程仓库**：GitHub + Gitee 双平台同步
  - GitHub：`https://github.com/3245489044/ZhiLianStorage.git`
  - Gitee：`https://gitee.com/Ljy_111/ZhiLianStorage.git`
- **同步脚本**：`sync-repos.sh "提交信息"`

### 构建配置

- **统一输出目录**：所有 C# 项目输出到 `CsharpPrj/BuildOutput/`，消除 DLL 重复复制
- **Linux 容器编译**：启用 `EnableWindowsTargeting`，支持在 Linux 上编译 Windows 目标
- **NuGet 安全锁定**：全局锁定 `System.Drawing.Common`、`System.Data.SqlClient` 等传递依赖的安全版本

### 测试要求

- 核心模块单元测试覆盖率 80%+
- 核心业务逻辑覆盖率 90%+
- 测试遵循 Arrange-Act-Assert 模式
- 测试框架：xUnit + MSTest + FluentAssertions + Moq

## 关键依赖

| 依赖 | 版本 | 用途 |
|------|------|------|
| Microsoft.SemanticKernel | 1.74.0 | AI 编排框架 |
| Microsoft.CodeAnalysis.CSharp | 5.3.0 | Roslyn 动态编译 |
| Newtonsoft.Json | 13.0.4 | JSON 序列化 |
| System.Text.Json | 10.0.9 | JSON 序列化（内置） |
| MediatR | 14.1.0 | 中介者模式 |
| Polly | 8.7.0 | 弹性策略（重试/熔断） |
| LiteDB | 5.0.21 | 嵌入式 NoSQL 数据库 |
| System.Data.SQLite | 1.0.119 | SQLite 数据访问 |
| PythonNet | 3.1.0 | C# ↔ Python 互操作 |
| Selenium WebDriver | 4.43.0 | UI 自动化 |
| LibGit2Sharp | 0.31.0 | Git 操作 |
| AutoMapper | 16.1.1 | 对象映射 |
| FluentValidation | 12.1.1 | 验证框架 |
| NLog | 6.1.2 | 日志框架 |
| MathNet.Numerics | 6.0.0-beta2 | 数学计算 |
| System.Reactive | 6.1.0 | 响应式编程 |

## 许可证

本项目闭源。

## 联系方式

- **GitHub**：[3245489044/ZhiLianStorage](https://github.com/3245489044/ZhiLianStorage)
- **Gitee**：[Ljy_111/ZhiLianStorage](https://gitee.com/Ljy_111/ZhiLianStorage)

## 更新日志

### v1.0.0 (2025-05-11)

- 初始版本发布，建立完整模块化项目结构
- 集成 Microsoft.SemanticKernel AI 能力
- 实现 Roslyn 动态代码编译引擎
- 开发工作流引擎与命令框架
- 整合 LiteDB、SQLite 等多种数据存储
- 提供 Windows 桌面 UI 组件库

---

**WorkFlowPrj** —— 让企业级应用开发更简单、更高效、更智能！
