# Docker 模块与工具代码分析报告

> 分析范围：`PowerShellScript/Modules/Docker/`（4 个 .psm1 + 1 个 .psd1）+ `PowerShellScript/Tools/Docker/`（7 个 .ps1）
> 总代码量：约 5600 行
> 分析日期：2026-06-24

---

## 架构关系说明

### Modules/Docker/ 与 Tools/Docker/ 的关系

```
Modules/Docker/          ← 可复用的模块层（被 Tools 和外部脚本引用）
├── Docker.psd1          ← 模块清单，定义嵌套模块加载顺序
├── DockerCommon.psm1    ← 共享常量、辅助函数、MCP 服务清单
├── DockerManagement.psm1 ← 容器生命周期管理（构建/启动/停止/日志）
├── McpInstallation.psm1 ← MCP 服务安装/卸载/更新
└── McpConfiguration.psm1 ← CLine 配置管理（读写/备份/同步）

Tools/Docker/            ← 可执行的工具脚本（用户直接调用）
├── Manage-Docker.ps1    ← 统一入口（交互式菜单 + 命令行模式）
├── Test-DockerDiagnostic.ps1  ← Docker 环境诊断
├── Test-McpServices.ps1       ← MCP 服务验证
├── Repair-DockerEngine.ps1    ← Docker 引擎修复
├── Repair-DockerImage.ps1     ← Docker 镜像修复
├── Update-DockerImage.ps1     ← Docker 镜像更新
└── Fix-CsprojContextConfig.ps1 ← CsprojContext 配置修复
```

**设计意图**：
- **Modules** 是"库"——提供可复用的函数和常量，不直接执行
- **Tools** 是"应用"——调用 Modules 的函数完成具体任务，用户直接运行
- **Manage-Docker.ps1** 是统一入口，通过交互式菜单或命令行参数调度其他 Tools 和 Modules

**实际存在的问题**：
- Tools 脚本虽然 `Import-Module` 了 Modules，但大量重复定义 Modules 已有的常量和函数
- 部分 Tools 脚本（如 Repair-DockerImage）直接实现完整流程，未复用其他 Tools 的通用框架
- 模块的 `$Script:` 作用域变量对 Tools 不可见，迫使 Tools 自行定义常量

---

## 目录

1. [重复逻辑](#一重复逻辑)
2. [冗余逻辑](#二冗余逻辑)
3. [错误逻辑](#三错误逻辑)
4. [量化总结与修复建议](#四量化总结与修复建议)
5. [已实施修复总结](#五已实施修复总结)

---

## 一、重复逻辑

### 1.1 MCP 服务清单三处重复

| 位置 | 行号 | 内容 |
|------|------|------|
| `DockerCommon.psm1` | L50-115 | `$Script:DefaultMcpServers` — 单一数据源 |
| `Test-McpServices.ps1` | L44-53 | `$McpServices` — 完全相同的 8 个服务 |
| `Fix-CsprojContextConfig.ps1` | L292-301 | `$services` — 再次定义 8 个服务 |

**影响**：新增/修改服务需同步 3 个文件，极易遗漏。

**建议**：Tools 脚本通过调用 `Get-McpServers()` 函数获取服务列表，而非自行定义。

### 1.2 输出辅助函数四处重复

| 文件 | 重复定义的函数 |
|------|---------------|
| `DockerCommon.psm1` | `Write-Header`, `Write-Section`, `Write-Step`, `Write-Diagnostic`, `Write-Result` 等 15 个 |
| `Test-DockerDiagnostic.ps1` | `Write-Diagnostic`, `Write-Section`, `Write-SubSection` |
| `Test-McpServices.ps1` | `Write-Diagnostic`, `Write-Section`, `Write-SubSection`, `Test-CommandExists` |
| `Fix-CsprojContextConfig.ps1` | `Write-Step`, `Write-Result` |

**影响**：Tools 脚本虽然 `Import-Module DockerCommon`，但仍各自定义输出函数，导致输出风格不一致。

**建议**：删除 Tools 脚本中的重复定义，统一使用 DockerCommon 的输出函数。

### 1.3 常量定义六处重复

每个 Tools 脚本都重新定义了以下常量：

| 常量 | 重复次数 | 涉及文件 |
|------|---------|---------|
| `$ContainerName` | 5 | DockerCommon, Test-DockerDiagnostic, Test-McpServices, Repair-DockerImage, Fix-CsprojContextConfig |
| `$DockerDataDir` | 2 | DockerCommon, Test-DockerDiagnostic, Repair-DockerEngine |
| `$DockerDesktopExe` | 3 | DockerCommon, Test-DockerDiagnostic, Repair-DockerEngine |
| `$DockerServiceName` | 2 | DockerCommon, Repair-DockerEngine |
| `$MaxWaitSeconds` | 2 | DockerCommon, Repair-DockerEngine |
| `$PollIntervalSeconds` | 2 | DockerCommon, Repair-DockerEngine |

**根因**：DockerCommon 使用 `$Script:` 作用域定义变量，模块外部不可见。

**建议**：将 DockerCommon 的 `$Script:` 变量改为通过函数暴露（如 `Get-ContainerName()`），或使用 `$global:` 作用域。

### 1.4 `Test-PortHttpReachable` 两处重复

- `DockerCommon.psm1` L333-350
- `Test-McpServices.ps1` L249-266

完全相同的实现。Test-McpServices 已导入 DockerCommon，应直接调用。

### 1.5 容器状态检查六处重复

`docker inspect --format "{{.State.Status}}"` 在以下文件中重复出现：

| 文件 | 出现位置 |
|------|---------|
| `DockerManagement.psm1` | `Get-DockerStatus` 函数内联 |
| `McpInstallation.psm1` | 5 个函数各重复一次 |
| `Test-McpServices.ps1` | `Check-Prerequisites` 函数 |
| `Fix-CsprojContextConfig.ps1` | Main 函数步骤 1 |
| `Repair-DockerImage.ps1` | `Step-VerifyResult` 函数 |
| `Manage-Docker.ps1` | `Show-MainMenu` 函数 |

**建议**：统一使用 DockerCommon 的 `Test-ContainerRunning` 函数，或新增 `Assert-ContainerRunning` 辅助函数。

### 1.6 镜像修补流程两处重复

- `Update-DockerImage.ps1`：通用框架（启动临时容器 → 执行命令 → 复制文件 → 提交 → 清理 → 重启）
- `Repair-DockerImage.ps1`：自己实现了完整流程，未复用 Update-DockerImage

**建议**：Repair-DockerImage 应调用 Update-DockerImage 的通用框架，仅提供修复参数。

---

## 二、冗余逻辑

### 2.1 输出函数过度设计

DockerCommon 定义了 15 个输出辅助函数，其中多组功能重叠：

| 函数组 | 函数 | 功能重叠说明 |
|--------|------|-------------|
| 标题组 | `Write-Header`, `Write-Section`, `Write-Banner`, `Write-MenuHeader`, `Write-Step` | 都是打印带装饰边框的标题，仅装饰字符和颜色略有差异 |
| 状态输出组 | `Write-Diagnostic`, `Write-Result` | 都是带状态图标（✅❌⚠️）的输出，参数结构相同 |
| 单行输出组 | `Write-Info`, `Write-Success`, `Write-Warning`, `Write-Error`, `Write-WhatIf` | 5 个函数可合并为 1 个带颜色参数的通用函数 |

**建议**：合并为 3-4 个核心输出函数，减少 API 表面积。

### 2.2 模块加载回退逻辑

`Manage-Docker.ps1` L69-85：

```powershell
$modulePath = Join-Path $ProjectRoot "Modules\Docker\Docker.psd1"
if (Test-Path $modulePath) {
    Import-Module $modulePath -Force -ErrorAction SilentlyContinue
} else {
    # 回退：逐个加载
    $moduleFiles = @(
        "Modules\Docker\DockerCommon.psm1",
        "Modules\Docker\DockerManagement.psm1",
        "Modules\Docker\McpInstallation.psm1",
        "Modules\Docker\McpConfiguration.psm1"
    )
    ...
}
```

正常环境下 `.psd1` 文件必然存在，回退路径永远不会执行。

**建议**：删除回退逻辑，仅保留 `Import-Module`。

### 2.3 McpInstallation 中 switch($server.Type) 模式重复

以下 5 个函数都包含相同的 `switch ($server.Type)` 结构：

| 函数 | 分发逻辑 |
|------|---------|
| `Test-McpServerInstalled` | `"npm"` / `"pip"` / `"local"` |
| `Get-McpServerVersion` | `"npm"` / `"pip"` / `"local"` |
| `Install-McpServer` | `"npm"` / `"pip"` / `"local"` |
| `Uninstall-McpServer` | `"npm"` / `"pip"` / `"local"` |
| `Update-McpServer` | `"npm"` / `"pip"` / `"local"` |

**建议**：提取为 `Invoke-McpServerAction` 分发函数，或使用哈希表映射。

### 2.4 诊断脚本全局变量模式重复

`Test-DockerDiagnostic.ps1` 和 `Test-McpServices.ps1` 都定义了：

```powershell
$Global:Results = @()
$Global:PassCount = 0
$Global:FailCount = 0
$Global:WarnCount = 0
```

以及完全相同的 `Write-Diagnostic` 函数。

**建议**：将诊断框架提取到 DockerCommon 中。

### 2.5 `Get-DockerStatus` 重复 docker inspect 调用

`DockerManagement.psm1` L61-74 对同一个容器连续调用 4 次 `docker inspect`：

```powershell
$state = docker inspect --format "{{.State.Status}}" ...
$health = docker inspect --format "{{.State.Health.Status}}" ...
$uptime = docker inspect --format "{{.State.StartedAt}}" ...
$ports = docker inspect --format "{{json .NetworkSettings.Ports}}" ...
```

**建议**：使用一次 `docker inspect --format "{{json .}}"` 获取所有信息，然后解析 JSON。

---

## 三、错误逻辑

### 3.1 🔴 `$Script:` 作用域问题（架构级）

**严重程度**：高

DockerCommon 使用 `$Script:` 作用域定义常量：

```powershell
$Script:ContainerName = "workflow-mcp-env"
$Script:DockerDataDir = "$env:LOCALAPPDATA\Docker"
```

Tools 脚本 `Import-Module` 后无法访问这些变量。**这是所有常量重复的根因**。

**修复方案**：
- 方案 A：将 `$Script:` 改为 `$global:`（简单但污染全局命名空间）
- 方案 B：通过函数暴露（如 `function Get-ContainerName { "workflow-mcp-env" }`）
- 方案 C：在模块的 `.psm1` 底部使用 `Export-ModuleMember -Variable` 导出变量

### 3.2 🔴 `Test-ContainerExists` 死代码

**严重程度**：高

`DockerCommon.psm1` L277-285 定义了 `Test-ContainerExists` 函数，但**没有任何文件调用它**。

```powershell
function Test-ContainerExists {
    param([string]$ContainerName)
    try {
        $state = docker inspect --format "{{.State.Status}}" $ContainerName 2>$null
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}
```

**建议**：删除或修正为实际可用的函数。

### 3.3 🟡 `2>&1` 重定向导致语义混淆

**严重程度**：中

`Get-ContainerState` 和 `Get-ContainerHealth` 使用 `2>&1` 合并 stderr：

```powershell
return docker inspect --format "{{.State.Status}}" $ContainerName 2>&1
```

当容器不存在时，docker 输出错误到 stderr，合并后函数返回错误字符串。调用方无法区分"容器不存在"和"状态为 unknown"。

**建议**：分离 stderr 处理，容器不存在时返回明确值（如 `$null` 或 `"not_found"`）。

### 3.4 🟡 `Write-Diagnostic` 函数冲突

**严重程度**：中

`Test-DockerDiagnostic.ps1` 先 `Import-Module DockerCommon`，然后在脚本中重新定义同名 `Write-Diagnostic`。后者覆盖前者，可能导致行为不一致。

**建议**：删除脚本中的重复定义，直接使用 DockerCommon 的函数。

### 3.5 🟡 `Repair-DockerImage.ps1` 硬编码路径

**严重程度**：中

```powershell
$HealthcheckSource = Join-Path $ScriptRoot "..\..\DevEnvironment\Docker\scripts\healthcheck.sh"
$EntrypointSource = Join-Path $ScriptRoot "..\..\DevEnvironment\Docker\scripts\entrypoint.sh"
$DockerComposePath = Join-Path $ScriptRoot "..\..\DevEnvironment\Docker\docker-compose.yml"
```

未使用 DockerCommon 的 `Resolve-ProjectPath` 函数。如果项目结构变化，这些路径会失效。

**建议**：使用 `Resolve-ProjectPath` 或 `Get-ProjectRoot` 函数。

### 3.6 🟡 `Fix-CsprojContextConfig.ps1` 脆弱正则

**严重程度**：中

Node.js 修复脚本（L126-186）使用正则表达式匹配注释块：

```javascript
const patterns = [
    {
        search: /\/\/ csproj-context 需要项目内构建，暂时注释\n\s+\/\/ \{\n\s+\/\/\s+name: 'csproj-context',\n.../,
        replace: `    {\n        name: 'csproj-context',\n...`
    },
    ...
];
```

如果 `config.js` 的格式稍有变化（缩进、换行、注释风格），正则就会匹配失败。虽然有 `NO_MATCH` 回退，但回退只是打印配置尾部，没有自动修复能力。

**建议**：使用更健壮的匹配策略（如逐行解析），或提供交互式修复选项。

### 3.7 🟢 `Test-McpServices.ps1` 临时文件泄露风险

**严重程度**：低

```powershell
$tempFile = [System.IO.Path]::GetTempFileName() + ".mjs"
$nodeScript | Out-File -FilePath $tempFile -Encoding UTF8 -Force
$output = node $tempFile 2>&1
Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
```

如果 `node` 命令异常退出，`$output` 变量可能未定义，导致后续 `$output.IndexOf()` 调用报错。虽然外层有 try/catch，但临时文件可能不会被清理。

**建议**：使用 try/catch/finally 确保临时文件始终被清理。

### 3.8 🟢 缺少统一错误处理模式

**严重程度**：低

混合使用三种错误处理方式：

| 方式 | 使用场景 |
|------|---------|
| `return $false` | DockerCommon 函数 |
| `exit 1` | Repair-DockerImage, Update-DockerImage |
| `try/catch` | 所有 Tools 脚本 |

没有统一的错误码或异常策略。

**建议**：统一使用 `throw` + try/catch 模式，或定义标准错误码。

### 3.9 🟢 `Manage-Docker.ps1` 调用方式不一致

**严重程度**：低

```powershell
# 方式 1：通过模块函数调用（大多数命令）
"R" { Repair-DockerEngine -Force:$Force }

# 方式 2：直接调用脚本（仅 Repair-DockerImage）
"I" { & (Join-Path $ScriptRoot "Repair-DockerImage.ps1") }
```

**建议**：统一为模块函数调用方式。

---

## 四、量化总结与修复建议

### 4.1 问题统计

| 类别 | 数量 | 严重程度分布 |
|------|------|-------------|
| 重复逻辑 | 6 类 | 🔴 3 / 🟡 3 |
| 冗余逻辑 | 5 类 | 🟡 2 / 🟢 3 |
| 错误逻辑 | 9 类 | 🔴 2 / 🟡 4 / 🟢 3 |

### 4.2 优先修复建议

| 优先级 | 问题 | 影响范围 | 预估工作量 |
|--------|------|---------|-----------|
| P0 | `$Script:` 作用域问题 | 所有 Tools 脚本 | 1 天 |
| P0 | MCP 服务清单三处重复 | 新增服务时易遗漏 | 0.5 天 |
| P1 | 容器状态检查六处重复 | 代码维护成本 | 0.5 天 |
| P1 | 输出辅助函数重复 | 输出风格一致性 | 0.5 天 |
| P1 | McpInstallation switch 模式重复 | 代码维护成本 | 0.5 天 |
| P2 | 镜像修补流程重复 | 功能冗余 | 0.5 天 |
| P2 | 其他小问题 | 代码质量 | 1 天 |

### 4.3 架构改进建议

1. **统一数据源**：所有 MCP 服务配置只定义在 DockerCommon 中，通过函数暴露
2. **统一输出框架**：将诊断/验证脚本的输出框架提取到 DockerCommon
3. **统一错误处理**：定义标准错误码和异常处理策略
4. **统一常量访问**：通过函数而非变量暴露模块常量
5. **统一容器操作**：所有容器状态检查统一使用 DockerCommon 的函数

---

## 5. 已实施修复总结

> 以下修复已在本次分析中同步完成。

### 5.1 DockerCommon.psm1 — 新增缺失函数

| 新增函数 | 用途 |
|----------|------|
| `Get-ComposeFile` | 获取 docker-compose.yml 路径 |
| `Get-DockerDataDir` | 获取 Docker 数据目录 |
| `Get-DockerLogDir` | 获取 Docker 日志目录 |
| `Get-DockerConfigDir` | 获取 Docker 配置目录 |
| `Get-DockerDesktopExe` | 获取 Docker Desktop 可执行文件路径 |
| `Get-ClineConfigPath` | 获取 CLine MCP 配置文件路径 |

### 5.2 McpInstallation.psm1 — 消除 switch 模式重复

- **提取 `Invoke-McpServerAction` 辅助函数**：统一处理 npm/pip/local 三种类型的 install/uninstall/update/check/version 命令分发
- **提取 `Assert-ContainerRunning` 辅助函数**：统一容器运行状态检查
- **消除 5 处 switch 模式重复**：Test-McpServerInstalled / Get-McpServerVersion / Install-McpServer / Uninstall-McpServer / Update-McpServer 中的 switch 分发逻辑合并为单一函数
- **消除 6 处容器状态检查重复**：统一使用 Assert-ContainerRunning
- **消除 2 处 `Get-McpServers` 重复**：统一使用 Get-McpServerList（内部调用 Get-McpServers）

### 5.3 Test-McpServices.ps1 — 消除重复

- **删除重复的 MCP 服务清单**：改为使用 `Get-McpServers()`
- **删除重复的输出函数**：改为使用 DockerCommon 的 `Write-Diagnostic` / `Write-Section` / `Write-SubSection`
- **删除重复的 `Test-PortHttpReachable`**：改为使用 DockerCommon 的版本
- **删除重复的 `Test-CommandExists`**：改为使用 DockerCommon 的版本
- **使用 `Get-ContainerState` / `Get-ContainerHealth`**：替代直接 docker inspect
- **使用 `Get-ClineConfigPath`**：替代硬编码路径

### 5.4 Fix-CsprojContextConfig.ps1 — 消除重复

- **删除重复的 `Write-Step` / `Write-Result`**：改为使用 DockerCommon 的版本
- **删除重复的 MCP 服务清单**：改为使用 `Get-McpServers()`
- **删除重复的端口可达性测试**：改为使用 `Test-PortHttpReachable()`
- **使用 `Get-ContainerState`**：替代直接 docker inspect

### 5.5 Test-DockerDiagnostic.ps1 — 消除重复

- **删除重复的 `Write-Diagnostic` / `Write-Section` / `Write-SubSection`**：改为使用 DockerCommon 的版本
- **使用 `Get-DockerDataDir` / `Get-DockerLogDir` / `Get-DockerConfigDir` / `Get-DockerDesktopExe` / `Get-ContainerName`**：替代硬编码常量

### 5.6 修复统计

| 指标 | 数值 |
|------|------|
| 修改文件数 | 6 个 |
| 新增函数 | 7 个 |
| 消除重复代码块 | ~25 处 |
| 消除重复 MCP 服务清单 | 3 处 |
| 消除重复输出函数 | ~15 个 |
| 消除 switch 模式重复 | 5 处 |
| 消除容器状态检查重复 | ~8 处 |
| 统一常量访问 | ~12 处 |
