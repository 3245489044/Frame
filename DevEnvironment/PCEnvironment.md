# PC 本地开发环境部署指南

> 本文档介绍在全新 Windows PC 上从零部署 WorkFlowPrj 项目所需的编程语言开发环境，
> 涵盖 C#（.NET）、TypeScript（Node.js）和 Python 三大技术栈。

---

## 1. 系统要求

| 项目 | 最低要求 | 推荐 |
|------|----------|------|
| 操作系统 | Windows 10 22H2 (19045+) | Windows 11 |
| 架构 | x64 | x64 |
| 内存 | 8 GB | 16 GB+ |
| 磁盘 | 20 GB 可用空间 | SSD 50 GB+ |
| PowerShell | 5.1（Windows 内置） | PowerShell 7+ |
| Git | 2.39+ | 最新版 |

> **注意**：项目 C# 目标框架为 `net10.0-windows`，使用了 WindowsForms 等 Windows 专有 API，
> 因此 **必须在 Windows 上编译运行**。Linux/macOS 仅可通过 Docker 容器编译（已配置 `EnableWindowsTargeting`）。

---

## 2. C# / .NET 环境

### 2.1 安装 .NET SDK

| 组件 | 版本 | 说明 |
|------|------|------|
| .NET SDK | **10.0.x** | 必须安装 SDK（非仅 Runtime），自带 C# 12 编译器 |

**下载地址**：<https://dotnet.microsoft.com/download/dotnet/10.0>

**安装后验证**：

```bash
dotnet --version          # 应输出 10.0.xxx
dotnet --list-sdks        # 确认 SDK 已安装
```

### 2.2 项目构建配置

项目通过 `CsharpPrj/Directory.Build.props` 全局定义编译行为，无需额外配置：

| 配置项 | 值 | 说明 |
|--------|-----|------|
| `TargetFramework` | net10.0-windows | 目标框架（各项目自行指定） |
| `Nullable` / `ImplicitUsings` | enable | 现代 C# 特性 |
| `EnableWindowsTargeting` | true | 支持在 Linux 上编译 Windows 目标 |
| `OutputPath` | `CsharpPrj/BuildOutput/` | 统一输出目录 |

### 2.3 编译命令

```bash
# 还原 NuGet 依赖包（自动从 versions.json 读取版本）
dotnet restore CsharpPrj/MainProj.sln

# 编译主解决方案
dotnet build CsharpPrj/MainProj.sln

# 编译其他解决方案
dotnet build FileDriven/FileDriven.sln
dotnet build Agent/Agent.sln
dotnet build Engine/GameEditor.sln
dotnet build TypeScriptPrj/TypeScript.sln

# 运行测试
dotnet test CentralTest/CSharpTest/CentralConsole.csproj
```

### 2.4 解决方案清单

| 解决方案 | 路径 | 说明 |
|----------|------|------|
| MainProj.sln | `CsharpPrj/MainProj.sln` | C# 主解决方案 |
| FileDriven.sln | `FileDriven/FileDriven.sln` | 文件驱动框架 |
| Agent.sln | `Agent/Agent.sln` | 智能体系统 |
| GameEditor.sln | `Engine/GameEditor.sln` | 游戏编辑器引擎 |
| TypeScript.sln | `TypeScriptPrj/TypeScript.sln` | TypeScript 工作空间（含 C# 桥接项目） |
| Protocol.sln | `CsharpPrj/Protocol/Protocol.sln` | 协议层 |
| CodeAnalyzer.sln | `Storage/Utilitys/CodeAnalyzer/CodeAnalyzer.sln` | 代码分析工具 |

> 如需调整 NuGet 包版本，编辑 `CsharpPrj/versions.json` 后运行
> `PowerShellScript/Modules/Csproj/Generate-BuildProps.ps1` 重新生成版本定义文件。

---

## 3. TypeScript / Node.js 环境

### 3.1 安装 Node.js

| 组件 | 版本 | 说明 |
|------|------|------|
| Node.js | **22 LTS**（v22.14.0+） | 项目统一使用 22.x 版本 |
| npm | 11.x+ | 随 Node.js 安装 |

**下载地址**：<https://nodejs.org/en/download>

**安装后验证**：

```bash
node --version    # 应输出 v22.x.x
npm --version     # 应输出 11.x+
```

### 3.2 安装 pnpm

项目使用 pnpm workspace 管理多包，版本锁定在 `package.json`：

```json
"packageManager": "pnpm@10.8.1"
```

**安装方式**（任选其一）：

```bash
# 方式一：通过 Corepack 启用（推荐，Node.js 16+ 内置）
corepack enable
corepack prepare pnpm@10.8.1 --activate

# 方式二：全局安装
npm install -g pnpm@10.8.1
```

**验证**：

```bash
pnpm --version    # 应输出 10.8.1
```

### 3.3 安装与编译

```bash
cd TypeScriptPrj

# 安装依赖（必须使用 pnpm，workspace 结构依赖它）
pnpm install

# 编译全部 workspace 包
node build/TsSln/cli.js build

# 其他常用命令
node build/TsSln/cli.js clean       # 清理构建产物
node build/TsSln/cli.js rebuild     # 清理 + 重新编译
node build/TsSln/cli.js list        # 列出所有 workspace
```

### 3.4 工作空间结构

```
TypeScriptPrj/
├── TsWorkspace/
│   ├── BasicMethod/          # 基础方法（Pipeline / HealthChecker / RequestRouter）
│   ├── MCPFramework/         # MCP 框架（服务器/客户端/工具注册/SSE）
│   ├── MCPBridge/            # MCP 桥接（Docker 容器代理）
│   ├── CsprojContext/        # C# 项目上下文 MCP 服务
│   ├── ClineApi/             # Cline API 集成
│   └── Protocol/CSG/         # CSG 协议 TypeScript 实现
├── TsSln/                    # 构建工具（自定义 CLI）
└── build/                    # 构建脚本
```

### 3.5 编译器配置（项目内已定义）

`tsconfig.json` 统一配置，无需改动：

| 配置项 | 值 |
|--------|-----|
| TypeScript | 5.9.3+ |
| target / module | ES2022 / NodeNext |
| strict | true |
| declaration / sourceMap | true |

---

## 4. Python 环境

### 4.1 安装 Python

| 组件 | 版本 | 说明 |
|------|------|------|
| Python | **3.13.x**（最低 3.10+） | 用于 DeepSeek AI 模型集成 |

**下载地址**：<https://www.python.org/downloads/>

**安装时注意事项**：
- 勾选 **"Add Python to PATH"**（PythonNet 桥接必需）
- 勾选 "Install pip"

**安装后验证**：

```bash
python --version     # 应输出 Python 3.13.x
pip --version        # 确认 pip 可用
```

### 4.2 安装依赖

项目 Python 依赖极简（仅 `openai` SDK），依赖文件为 `PythonPrj/PythonModel/requirements.txt`：

```bash
cd PythonPrj/PythonModel
pip install -r requirements.txt
```

### 4.3 环境变量配置

Python 模块通过环境变量读取 API Key：

| 环境变量 | 说明 |
|----------|------|
| `DeepSeekAPI` | DeepSeek API 密钥（调用 AI 功能时必须配置） |

**设置方式**：

```powershell
# PowerShell：永久设置（用户级）
[System.Environment]::SetEnvironmentVariable("DeepSeekAPI", "sk-xxxxxxxx", "User")
```

### 4.4 C# ↔ Python 互操作

C# 通过 PythonNet 3.1.0 桥接调用 Python 代码：

- C# 桥接项目：`PythonPrj/PythonModel.Bridge/`
- Python 模块：`PythonPrj/PythonModel/`

> **注意**：PythonNet 依赖系统 PATH 找到 Python 运行时，请勿在安装时取消 "Add Python to PATH"。

### 4.5 模块结构

```
PythonPrj/PythonModel/
├── DeepSeekInvoke.py          # 兼容层（委托给 clients.deepseek）
├── requirements.txt
└── clients/
    ├── base.py                # BaseModelClient 抽象基类
    └── deepseek.py            # DeepSeekClient — 封装 OpenAI SDK
```

---

## 5. PowerShell 环境

| 组件 | 版本 | 说明 |
|------|------|------|
| PowerShell | 5.1+（Windows 内置） | 最低要求 |
| PowerShell 7 | 推荐 | 跨平台，性能更好 |

**PowerShell 7 下载**：<https://github.com/PowerShell/PowerShell/releases>

项目脚本位于 `PowerShellScript/` 目录（构建、Docker 管理、模块化脚本框架）。

---

## 6. Docker 环境（可选）

用于运行 MCP 服务集群，非项目编译必需。

| 组件 | 版本 |
|------|------|
| Docker Desktop | 最新版（基于 WSL2） |
| Docker Compose | v2+（随 Desktop 安装） |

**启动 MCP 环境**：

```bash
cd DevEnvironment
docker compose -f Docker/docker-compose.yml up -d
```

> 容器内已内置 .NET SDK 10.0、Node.js 22 LTS、Python 3.13。
> 详见 [Docker_MCP_环境搭建指南.md](./Docker_MCP_环境搭建指南.md)。

---

## 7. IDE 推荐

### VS Code（推荐）

| 扩展 | 用途 |
|------|------|
| C# Dev Kit | C# 智能提示、调试、测试 |
| Python | Python 开发支持 |
| PowerShell | PowerShell 脚本开发 |
| Docker | Docker 容器管理 |
| Cline | AI 辅助编码（MCP 客户端） |

### Visual Studio 2022

- 需安装 **.NET desktop development** 工作负载 + **.NET 10 SDK** 组件
- 适用于 WindowsForms UI 开发和调试

---

## 8. 一键部署清单

```
步骤 1  → 安装 Git                    https://git-scm.com/download/win
步骤 2  → 安装 .NET 10 SDK            https://dotnet.microsoft.com/download/dotnet/10.0
步骤 3  → 安装 Node.js 22 LTS         https://nodejs.org/en/download
步骤 4  → 启用 pnpm                   corepack enable && corepack prepare pnpm@10.8.1 --activate
步骤 5  → 安装 Python 3.13            https://www.python.org/downloads/
步骤 6  → 安装 PowerShell 7（可选）    https://github.com/PowerShell/PowerShell/releases
步骤 7  → 安装 Docker Desktop（可选）  https://www.docker.com/products/docker-desktop/
步骤 8  → 克隆项目仓库                git clone https://github.com/3245489044/ZhiLianStorage.git
步骤 9  → 编译 C# 项目                dotnet restore CsharpPrj/MainProj.sln && dotnet build CsharpPrj/MainProj.sln
步骤 10 → 编译 TypeScript 项目        cd TypeScriptPrj && pnpm install && node build/TsSln/cli.js build
步骤 11 → 安装 Python 依赖            cd PythonPrj/PythonModel && pip install -r requirements.txt
步骤 12 → 配置 DeepSeek API Key       设置环境变量 DeepSeekAPI
```

---

## 9. 一键全量编译

```powershell
# 交互模式（确认后执行）
pwsh PowerShellScript/Build/Invoke-FullRebuild.ps1

# 跳过确认，直接编译
pwsh PowerShellScript/Build/Invoke-FullRebuild.ps1 -Force

# 编译 Release 配置
pwsh PowerShellScript/Build/Invoke-FullRebuild.ps1 -Configuration Release
```

流程：集中清理 → 全量编译 C#（MainProj.sln）→ 全量编译 TypeScript → 验证输出完整性。

---

## 10. 环境验证检查清单

```bash
# ── C# ──
dotnet --version                    # ✅ 10.0.xxx
dotnet build CsharpPrj/MainProj.sln # ✅ 编译成功，无错误

# ── TypeScript ──
node --version                      # ✅ v22.x.x
pnpm --version                      # ✅ 10.8.1
cd TypeScriptPrj
pnpm install                        # ✅ 依赖安装成功
node build/TsSln/cli.js build       # ✅ 编译成功

# ── Python ──
python --version                    # ✅ Python 3.13.x
cd PythonPrj/PythonModel
pip install -r requirements.txt     # ✅ openai 安装成功
python -c "from openai import OpenAI; print('OK')"  # ✅ 模块导入成功

# ── 一键全量编译 ──
pwsh PowerShellScript/Build/Invoke-FullRebuild.ps1 -Force  # ✅ 全量编译通过
```

---

## 11. 常见问题

### Q1: TypeScript 编译报错 "Cannot find module"

**原因**：未安装依赖，或使用了 `npm install` 而非 `pnpm install`。

**解决**：必须使用 `pnpm install`（项目为 pnpm workspace 结构）。

### Q2: PythonNet 找不到 Python 运行时

**原因**：Python 未加入系统 PATH。

**解决**：确认 `python --version` 在命令行可正常执行；或安装时勾选 "Add Python to PATH"。

### Q3: pnpm 版本不匹配

**原因**：`package.json` 锁定 `pnpm@10.8.1`，系统安装的是其他版本。

**解决**：

```bash
corepack prepare pnpm@10.8.1 --activate
```

### Q4: Docker 容器编译 C# 失败

**解决**：项目已配置 `EnableWindowsTargeting=true`。确保 Docker 镜像为最新构建
（`docker compose build`），且包含 .NET 10 SDK。

### Q5: 修改 NuGet 包版本后编译报错

**解决**：编辑 `CsharpPrj/versions.json` 后，运行
`PowerShellScript/Modules/Csproj/Generate-BuildProps.ps1` 重新生成版本定义，再重新编译。

---

## 12. 版本汇总速查表

| 技术栈 | 组件 | 版本 |
|--------|------|------|
| **C#** | .NET SDK | 10.0.x |
| | 目标框架 | net10.0-windows |
| **TypeScript** | Node.js | 22 LTS (v22.14.0+) |
| | pnpm | 10.8.1 |
| | TypeScript | 5.9.3+ |
| **Python** | Python | 3.13.x (最低 3.10+) |
| | openai SDK | >= 1.0.0 |
| **其他** | PowerShell | 5.1+ (推荐 7) |
| | Git | 2.39+ |
| | Docker | 最新版 (可选) |
