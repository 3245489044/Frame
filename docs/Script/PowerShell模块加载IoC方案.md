# PowerShell 模块加载 IoC 方案

> **控制反转（IoC）**：脚本声明需求，统一加载器按需满足，而非脚本主动硬编码加载路径。

---

## 1. 背景与问题

### 当前现状

每个脚本自行决定加载哪些模块，硬编码了依赖路径：

```powershell
# Invoke-TypeScriptBuild.ps1 第 88-90 行
$pscriptManagerInit = Resolve-Path (Join-Path $PSScriptRoot "..\PScriptManager\_init.ps1")
. $pscriptManagerInit
```

### 存在的问题

| 问题 | 说明 |
|------|------|
| **硬编码路径** | 每个脚本都知道模块的具体位置，路径变更需修改所有脚本 |
| **全量加载** | 脚本只需要 `Write-Header` 等几个函数，却加载了整个 BasicMethod（15 个子模块） |
| **重复加载** | 多个脚本串联执行时，同一模块被反复加载 |
| **耦合度高** | 脚本与加载机制紧密耦合，无法独立替换模块实现 |
| **隐式依赖** | 脚本用了哪些函数需要阅读代码才能知道，没有显式声明 |

### 核心矛盾

```
脚本需要函数（What）  →  当前做法：脚本自己找模块（How）
脚本需要函数（What）  →  目标做法：加载器按需提供（How）
```

---

## 2. 设计目标

| 目标 | 优先级 | 说明 |
|------|--------|------|
| **声明式依赖** | P0 | 脚本头部声明需要的函数名，而非模块路径 |
| **按需加载** | P0 | 只加载脚本实际需要的模块，不加载无关模块 |
| **零重复加载** | P0 | 同一模块在整个会话中只加载一次 |
| **路径解耦** | P1 | 脚本不感知模块物理位置，路径变更不影响脚本 |
| **渐进迁移** | P1 | 现有脚本可逐步迁移，无需一次性改造 |
| **可替换性** | P2 | 同一函数名可映射到不同实现，支持 mock/测试 |

---

## 3. 架构概览

### 3.1 角色定义

```
┌─────────────────────────────────────────────────────────┐
│                    脚本（Consumer）                       │
│  # @requires Write-Header, Write-Success, Confirm-Action │
│  调用函数，不关心来源                                      │
└────────────────────────┬────────────────────────────────┘
                         │ dot-source
                         ▼
┌─────────────────────────────────────────────────────────┐
│              Import-Modules.ps1（加载器）                  │
│  1. 解析脚本头部的 @requires                              │
│  2. 查函数→模块映射表                                     │
│  3. 按需 Import-Module / dot-source                      │
│  4. 跳过已加载模块                                        │
└────────────────────────┬────────────────────────────────┘
                         │ 读取
                         ▼
┌─────────────────────────────────────────────────────────┐
│              函数→模块映射表（Registry）                    │
│  Write-Header    → BasicMethod/Core/Output.psm1          │
│  Confirm-Action  → Tools/modules/Confirm-Action.psm1     │
│  Invoke-Step     → Modules/Core/BuildHelpers.psm1        │
│  ...                                                     │
└─────────────────────────────────────────────────────────┘
```

### 3.2 两层加载体系

```
┌──────────────────────────────────────────────────────────┐
│                    全量加载（入口）                         │
│  PScriptManager/_init.ps1                                │
│  用途：交互式会话、需要全部模块的场景                        │
│  加载：PScriptManager 核心 + BasicMethod 全部 15 子模块    │
└────────────────────────┬─────────────────────────────────┘
                         │ 按需委托
                         ▼
┌──────────────────────────────────────────────────────────┐
│                    按需加载（脚本）                         │
│  Import-Modules.ps1                                      │
│  用途：自动化脚本、只需要部分函数的场景                      │
│  加载：仅加载 @requires 声明的函数所在模块                   │
└──────────────────────────────────────────────────────────┘
```

---

## 4. 核心机制：函数→模块映射表

### 4.1 映射表定义

映射表是 IoC 的核心，维护在 `Import-Modules.ps1` 中：

```powershell
# 函数→模块映射表
# Key   = 函数名（脚本 @requires 中声明的名称）
# Value = 模块路径（相对于 PowerShellScript/ 目录）
$functionModuleMap = @{
    # ===== BasicMethod/Core =====
    'Write-Header'       = 'BasicMethod/Core/Output.psm1'
    'Write-Success'      = 'BasicMethod/Core/Output.psm1'
    'Write-Info'         = 'BasicMethod/Core/Output.psm1'
    'Write-Error'        = 'BasicMethod/Core/Output.psm1'
    'Write-SubHeader'    = 'BasicMethod/Core/Output.psm1'
    'Read-File'          = 'BasicMethod/FileOps/FileAccess.psm1'
    'Search-Files'       = 'BasicMethod/FileOps/FileAccess.psm1'
    'BatchRead-File'     = 'BasicMethod/FileOps/FileBatch.psm1'
    'BatchWrite-File'    = 'BasicMethod/FileOps/FileBatch.psm1'
    'BatchReplace-InFile' = 'BasicMethod/FileOps/FileBatch.psm1'
    'Get-ProjectRoot'    = 'BasicMethod/Core/Path.psm1'
    'Get-ContainerState' = 'BasicMethod/DevOps/Docker.psm1'

    # ===== Modules/Core =====
    'Invoke-Step'        = 'Modules/Core/BuildHelpers.psm1'
    'Invoke-VerificationCheck' = 'Modules/Core/BuildHelpers.psm1'
    'Test-CommandExists' = 'Modules/Core/BuildHelpers.psm1'
    'Confirm-Action'     = 'Modules/Core/BuildHelpers.psm1'

    # ===== Tools/modules =====
    'Write-FormatTable'  = 'Tools/modules/Write-Format.psm1'
    'Write-Diagnostic'   = 'Tools/modules/Write-Diagnostic.psm1'
    'Test-Environment'   = 'Tools/modules/Test-Environment.psm1'
    'Convert-Format'     = 'Tools/modules/Convert-Format.psm1'
}
```

### 4.2 加载器核心逻辑

```powershell
function Invoke-RequirementBasedLoad {
    <#
    .SYNOPSIS
        基于 @requires 声明按需加载模块
    .DESCRIPTION
        解析调用脚本头部的 @requires 注释，查找函数→模块映射表，
        仅加载尚未加载的模块。
    #>
    param(
        # 调用脚本的完整路径（自动检测）
        [string]$CallerPath = $null
    )

    if (-not $CallerPath) {
        $CallerPath = Get-PSCallStack | Select-Object -Skip 1 -First 1 |
            ForEach-Object { $_.ScriptName }
        if (-not $CallerPath) { return }
    }

    # 读取脚本头部，提取 @requires 行
    $header = Get-Content $CallerPath -TotalCount 20 -Encoding UTF8
    $requiresLine = $header | Where-Object { $_ -match '#\s*@requires\s+(.+)$' }

    if (-not $requiresLine) { return }  # 没有声明需求，跳过

    # 解析函数名列表
    $requiredFunctions = @()
    foreach ($line in $requiresLine) {
        if ($line -match '#\s*@requires\s+(.+)$') {
            $requiredFunctions += $Matches[1] -split ',' | ForEach-Object { $_.Trim() }
        }
    }

    # 按需加载模块
    $loadedModules = @{}
    foreach ($funcName in $requiredFunctions) {
        if (-not $functionModuleMap.ContainsKey($funcName)) {
            Write-Warning "[Import-Modules] 未找到函数 '$funcName' 的模块映射"
            continue
        }

        $moduleRelPath = $functionModuleMap[$funcName]
        if ($loadedModules.ContainsKey($moduleRelPath)) { continue }

        $moduleFullPath = Join-Path $script:PowerShellScriptRoot $moduleRelPath
        if (-not (Test-Path $moduleFullPath)) {
            Write-Warning "[Import-Modules] 模块文件不存在: $moduleRelPath"
            continue
        }

        # 检查是否已加载
        $moduleName = [System.IO.Path]::GetFileNameWithoutExtension($moduleRelPath)
        $existing = Get-Module -Name $moduleName -ErrorAction SilentlyContinue
        if ($existing) {
            $loadedModules[$moduleRelPath] = $true
            continue
        }

        # 加载模块
        try {
            Import-Module $moduleFullPath -Force -DisableNameChecking -ErrorAction Stop
            $loadedModules[$moduleRelPath] = $true
            Write-Host "  [按需加载] $moduleRelPath" -ForegroundColor DarkGray
        } catch {
            Write-Warning "[Import-Modules] 模块加载失败: $moduleRelPath - $_"
        }
    }
}
```

### 4.3 惰性加载（可选增强）

对于无法提前声明需求的场景，支持在首次调用函数时自动加载：

```powershell
# 在 Import-Modules.ps1 末尾注册一个函数解析器
# 当脚本调用未定义的函数时，自动查找映射表并加载对应模块

$originalExecutionContext = $ExecutionContext
# 注：PowerShell 没有原生 __autoload，通过覆盖 Get-Command 实现
# 此功能为可选增强，基础方案不依赖此机制
```

---

## 5. 使用方式

### 5.1 脚本端（消费者）

在脚本头部用 `@requires` 声明需要的函数：

```powershell
<#
.SYNOPSIS
    编译 TypeScriptPrj 中所有 TypeScript 项目
# @requires Write-Header, Write-Success, Write-Info, Write-SubHeader
# @requires Confirm-Action, Invoke-Step, Invoke-VerificationCheck
# @requires Test-CommandExists, Get-ProjectRoot
#>

param(
    [switch]$Force
)

# ============================================================
# 加载模块（一行搞定，不关心模块位置）
# ============================================================
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptDir "..\Modules\Import-Modules.ps1")

# 从此处开始直接使用函数，无需关心它们来自哪个模块
Write-Header "开始编译"
# ...
```

### 5.2 加载器端（维护者）

当新增模块或函数时，只需更新映射表：

```powershell
# 在 Import-Modules.ps1 的 $functionModuleMap 中添加条目
'New-Function' = 'NewModule/NewSubModule.psm1'
```

### 5.3 全量加载（交互式会话）

对于需要全部模块的交互式会话，仍使用 `PScriptManager/_init.ps1`：

```powershell
. .\PScriptManager\_init.ps1
```

---

## 6. 迁移指南

### 6.1 迁移步骤

```
Step 1: 建立映射表
  └─ 在 Import-Modules.ps1 中创建 $functionModuleMap
  └─ 梳理所有模块的导出函数，填入映射表

Step 2: 添加按需加载函数
  └─ 在 Import-Modules.ps1 中添加 Invoke-RequirementBasedLoad
  └─ 保留原有的全量加载逻辑作为回退

Step 3: 逐个迁移脚本
  └─ 为每个脚本添加 @requires 注释
  └─ 将 dot-source PScriptManager/_init.ps1 替换为 Import-Modules.ps1
  └─ 测试脚本功能正常

Step 4: 清理
  └─ 确认所有脚本迁移完成后，可移除 Import-Modules.ps1 中的全量加载逻辑
```

### 6.2 迁移对照表

| 脚本 | 当前加载方式 | 迁移后 |
|------|-------------|--------|
| `Build/Invoke-TypeScriptBuild.ps1` | `. "$basicMethodDir\_init.ps1"` → `. $pscriptManagerInit` | `@requires` + `Import-Modules.ps1` |
| `Build/CleanupExtraBuildOutput.ps1` | `Import-Modules.ps1` | 添加 `@requires` |
| `Build/Invoke-CleanAll.ps1` | `Import-Modules.ps1` | 添加 `@requires` |
| `Tools/Docker/Manage-Docker.ps1` | `Import-Modules.ps1` | 添加 `@requires` |
| 其他脚本 | 检查加载方式 | 统一为 `@requires` + `Import-Modules.ps1` |

### 6.3 兼容性保障

迁移期间，`Import-Modules.ps1` 同时支持两种模式：

```powershell
# 模式 A：按需加载（新）
# @requires Write-Header, Confirm-Action
. .\Modules\Import-Modules.ps1

# 模式 B：全量加载（旧，兼容）
. .\Modules\Import-Modules.ps1
# 未声明 @requires 时，回退到原有的全量加载逻辑
```

---

## 7. 与现有系统的关系

### 7.1 模块层次结构

```
PowerShellScript/
├── PScriptManager/          ← 框架入口（全量加载）
│   └── _init.ps1            ← 交互式会话使用
├── Modules/                 ← 模块加载器
│   └── Import-Modules.ps1   ← IoC 加载器（按需加载）
├── BasicMethod/             ← 基础方法库（15 个子模块）
│   ├── Core/
│   ├── FileOps/
│   ├── DevOps/
│   └── Tooling/
├── Tools/                   ← 工具模块
│   └── modules/
├── Build/                   ← 构建脚本（消费者）
├── Solution/                ← 解决方案脚本（消费者）
└── ...
```

### 7.2 职责边界

| 组件 | 职责 | 不负责 |
|------|------|--------|
| `PScriptManager/_init.ps1` | 全量加载，交互式会话入口 | 按需加载，脚本依赖管理 |
| `Import-Modules.ps1` | 按需加载，依赖解析，映射表维护 | 框架初始化，注册表管理 |
| `$functionModuleMap` | 函数→模块的映射关系 | 加载逻辑，路径解析 |
| 各脚本的 `@requires` | 声明函数需求 | 模块路径，加载方式 |

### 7.3 与 ScriptLauncher 的关系

`ScriptLauncher.ps1` 作为统一启动器，在启动脚本前确保模块已加载：

```powershell
# ScriptLauncher.ps1 内部
function Invoke-Tool {
    param($ToolConfig, $ToolPath, $ToolArgs)

    # 步骤 1：确保 Import-Modules.ps1 已加载
    . (Join-Path $scriptPath "Modules\Import-Modules.ps1")

    # 步骤 2：执行目标脚本（脚本自身的 @requires 会按需补充加载）
    & $ToolPath @ToolArgs
}
```

---

## 8. 方案优势总结

| 维度 | 改造前 | 改造后 |
|------|--------|--------|
| **耦合度** | 脚本硬编码模块路径 | 脚本只声明函数名，零路径耦合 |
| **加载量** | 全量加载（15+ 模块） | 按需加载（仅加载需要的模块） |
| **依赖可见性** | 隐式（阅读代码才知道） | 显式（头部 @requires 一目了然） |
| **迁移成本** | — | 渐进式，每脚本 5 分钟 |
| **维护成本** | 路径变更需改所有脚本 | 只需更新映射表 |
| **可测试性** | 难以 mock | 映射表可替换为测试实现 |
| **启动速度** | 慢（全量加载） | 快（按需加载） |

---

## 9. 实施记录

### Phase 1：建立基础设施 ✅（2026-07-07）

1. ✅ 在 `Import-Modules.ps1` 中创建 `$functionModuleMap` 映射表
2. ✅ 实现 `Invoke-RequirementBasedLoad` 按需加载函数
3. ✅ 保留全量加载逻辑作为回退
4. ✅ 编写单元测试验证映射表完整性

### Phase 2：迁移核心脚本 ✅（2026-07-07）

| 脚本 | 迁移内容 | 状态 |
|------|---------|------|
| `Build/Invoke-TypeScriptBuild.ps1` | 添加 `@requires`，替换 `_init.ps1` → `Import-Modules.ps1` | ✅ |
| `Build/CleanupExtraBuildOutput.ps1` | 添加 `@requires`，替换 `_init.ps1` → `Import-Modules.ps1` | ✅ |
| `Build/Invoke-CleanAll.ps1` | 添加 `@requires`，替换 `_init.ps1` → `Import-Modules.ps1` | ✅ |
| `Build/Invoke-DockerReset.ps1` | 添加 `@requires`，替换 `_init.ps1` → `Import-Modules.ps1` | ✅ |
| `Build/Invoke-FullRebuild.ps1` | 添加 `@requires`，替换 `_init.ps1` → `Import-Modules.ps1` | ✅ |
| `Build/docker-dotnet.ps1` | 添加 `@requires`，替换 `_init.ps1` → `Import-Modules.ps1` | ✅ |
| `Build/Fix-MCPServices.ps1` | 添加 `@requires`，替换 `_init.ps1` → `Import-Modules.ps1` | ✅ |
| `Build/Repair-MCPServices.ps1` | 添加 `@requires`，替换 `_init.ps1` → `Import-Modules.ps1` | ✅ |
| `Build/Deploy-MCPBridge.ps1` | 添加 `@requires`，替换 `_init.ps1` → `Import-Modules.ps1` | ✅ |
| `Build/Deploy-DotnetBuilder.ps1` | 替换 PScriptManager 框架加载 → `Import-Modules.ps1` | ✅ |
| `Build/Initialize-DotnetSdk.ps1` | 添加 `@requires`，替换 `_init.ps1` → `Import-Modules.ps1` | ✅ |

### Phase 3：全面推广 ⏳（部分完成）

扫描结果：PowerShellScript 下共 100+ 脚本文件。

**已完成迁移的目录：**
- `Build/` — 全部 11 个脚本 ✅
- `Modules/` — 模块文件（.psm1）不需要 `@requires`，保持现状

**待迁移的脚本（需要添加 `@requires` 声明）：**
- `Tools/Docker/` — 7 个脚本（Manage-Docker, Install-McpServer, Repair-DockerEngine 等）
- `Tools/VSCode/` — 6 个脚本
- `Tools/NuGet/` — 3 个脚本
- `Tools/Testing/` — 2 个脚本
- `Tools/EnvMigration/` — 5 个脚本
- `Tools/Cleanup/` — 3 个脚本
- `Tools/Legacy/` — 3 个脚本
- `Solution/` — 3 个脚本
- `Git/` — 3 个脚本
- `Analysis/` — 1 个脚本
- `FileDriven/` — 6 个脚本
- `AIDecisionFramework/` — 6 个脚本

> **注意**：`@requires` 是最佳实践声明，不影响功能。未添加 `@requires` 的脚本仍可通过 `Import-Modules.ps1` 的全量加载回退逻辑正常工作。

### Phase 4：清理与优化 ⏳

1. ✅ 确认 Build 目录所有脚本已迁移
2. ⏳ 其他目录脚本的 `@requires` 声明 — 按需逐步添加
3. ⏳ 更新文档和知识图谱 — 当前文档已更新

---

## 10. 附录

### A. 映射表维护规范

- 每个函数只映射到一个模块（避免歧义）
- 模块路径使用正斜杠 `/`（跨平台兼容）
- 新增函数时同步更新映射表
- 删除函数时从映射表移除（避免死条目）

### B. @requires 语法规范

```
# @requires Func1, Func2, Func3
# @requires Func4
```

- 一行可声明多个函数，用逗号分隔
- 可写多行 `@requires`
- 函数名大小写敏感（与 PowerShell 函数定义一致）
- 注释必须位于脚本头部 20 行内

### C. 故障排查

| 症状 | 原因 | 解决 |
|------|------|------|
| `无法识别函数 xxx` | 函数未在映射表中注册 | 检查 `$functionModuleMap` 是否包含该函数 |
| `模块加载失败` | 模块文件路径错误 | 检查映射表中的路径是否正确 |
| `函数重复定义` | 同一函数被多个模块导出 | 使用 `-DisableNameChecking` 或移除重复 |
| `加载顺序错误` | 模块间有依赖关系 | 在映射表中按依赖拓扑排序 |
