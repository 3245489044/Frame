# Install Framework 代码审计报告

> **审计日期**: 2026-07-12  
> **审计范围**: `PowerShellScript\Install\` 目录  
> **审计目的**: 识别重复逻辑、冗余逻辑和错误逻辑，提供重构建议

---

## 执行摘要

本次审计共发现 **14 个问题**，其中：

| 严重程度 | 数量 | 问题类型 |
|---------|------|----------|
| **P0 - 致命** | 4 | 属性访问错误、函数调用错误、环境检测错误、路径处理错误 |
| **P1 - 高** | 3 | 代码注入安全风险、框架绕过、逻辑重复 |
| **P2 - 中** | 5 | 模块重复定义、路径/环境变量设置重复、加载策略不一致、闭包变量捕获 |
| **P3 - 低** | 2 | 路径解析冗余、状态管理冗余 |

**核心问题**: `Install-McpServer.ps1` 和 `Install-Rust.ps1` 完全绕过了 Install Framework，重新实现了并行安装逻辑，导致维护成本翻倍。

---

## 第一部分：P0 致命问题

### F-01: 属性访问错误 — $Server.package 不存在

**文件**: [Install-McpServer.ps1](Commands/Install-McpServer.ps1#L265)

**当前代码**:
```powershell
$package = $Server.package  # 第265行
```

**问题**: 清单配置使用 `source` 字段（见 [manifest-schema.json](manifest-schema.json#L46-L49)），但代码访问 `package` 属性，导致 `$package` 始终为 `$null`。

**修复方案**:
```powershell
$package = $Server.source  # 改为 source
```

**影响范围**: 第265、266、332、335、338、342、346、366、367、385、406、442、445、465、477、507、510、521、525、529、539、540、579、583行

---

### F-02: 函数调用错误 — Install-GitHubRelease 不存在

**文件**: [Install-McpServer.ps1](Commands/Install-McpServer.ps1#L466)

**当前代码**:
```powershell
return Install-GitHubRelease -Repo $package -Name $name -Executor $executor  # 第466行
```

**问题**: `Install-GitHubRelease` 函数不存在，应为 `Invoke-GitHubReleaseDownload`。

**修复方案**:
```powershell
return Invoke-GitHubReleaseDownload -Repo $package -Version $Version -InstallDir "/opt/mcp-servers/$name" -Executor $executor
```

**影响范围**: 第466、530行

---

### F-03: MSI Provider Docker 环境错误 — dpkg 无法安装 MSI

**文件**: [msi.ps1](Providers/msi.ps1#L54-L55)

**当前代码**:
```powershell
$result = & $Executor "dpkg -l | grep -i '$($Item.productCode)' 2>&1"  # 第54-55行
...
$result = & $Executor "dpkg -i '$tempPath' 2>&1"  # 第136行
```

**问题**: MSI 是 Windows 安装格式，不能在 Linux Docker 容器中使用 `dpkg` 安装。

**修复方案**:
```powershell
# 删除 Docker 环境下的 MSI 安装逻辑，MSI 仅支持本地环境
if ($Target -eq "docker") {
    Write-Error "MSI 安装包不支持 Docker 环境"
    return $false
}
```

**影响范围**: 第52-59行（检测逻辑）、第126-144行（安装逻辑）

---

### F-04: zip-extract.ps1 Docker 路径处理错误 — PowerShell 变量未展开

**文件**: [zip-extract.ps1](Providers/zip-extract.ps1#L158-L161)

**当前代码**:
```powershell
$tempZipPath = Join-Path $env:TEMP "$($Item.name).zip"  # 第154行
...
$result = & $Executor "curl -sSL -o '$tempZipPath' '$url'"  # 第158行
```

**问题**: `$tempZipPath` 使用 PowerShell 的 `$env:TEMP`，但在 Docker 环境中通过 `sh` 执行时，变量不会被正确展开。

**修复方案**:
```powershell
if ($Target -eq "docker") {
    $tempZipPath = "/tmp/$($Item.name).zip"  # 使用 Unix 路径
    $tempExtractDir = "/tmp/$($Item.name)-extract"
    $result = & $Executor "curl -sSL -o '$tempZipPath' '$url'"
    ...
}
```

**影响范围**: 第154-156行、第158-184行

---

## 第二部分：P1 高优先级问题

### F-05: 代码注入安全风险 — Invoke-Expression

**文件**: [Executor.ps1](Core/Executor.ps1#L56)

**当前代码**:
```powershell
return {
    param($cmd)
    Invoke-Expression $cmd 2>&1  # 第56行
}
```

**问题**: `Invoke-Expression` 执行来自 manifest JSON 的命令，存在代码注入风险。

**修复方案**:
```powershell
return {
    param($cmd)
    $process = Start-Process -FilePath "powershell" -ArgumentList "-Command", $cmd `
        -NoNewWindow -Wait -PassThru -RedirectStandardError ([ref]$null)
    $process.ExitCode
}
```

**影响范围**: 第54-58行

---

### F-06: 框架绕过 — Install-McpServer.ps1 重复实现安装逻辑

**文件**: [Install-McpServer.ps1](Commands/Install-McpServer.ps1)

**问题**: 该脚本实现了完整的安装逻辑（环境检查、状态检测、版本获取、安装执行），与 Install Framework 的 Core/Providers 功能完全重叠。

**重复功能对照表**:

| Framework 功能 | Install-McpServer.ps1 重复实现 |
|---------------|------------------------------|
| `Test-ExecutorAvailable` | `Test-DockerEnvironment` / `Test-LocalEnvironment` |
| Provider `Test-Installed` | `Test-McpServerInstalled` / `Test-DockerInstalled` / `Test-LocalInstalled` |
| Provider `Get-Version` | `Get-McpServerVersion` / `Get-DockerVersion` / `Get-LocalVersion` |
| Provider `Invoke-Install` | `Install-McpServerToDocker` / `Install-McpServerToLocal` |
| `Show-ManifestList` | `Show-McpServerList` |

**修复方案**: 重构为使用 `Install-Tool.ps1 -Manifest mcp-servers.json` 模式，删除所有重复逻辑。

---

### F-07: 逻辑重复 — Install-Rust.ps1 与 rustup.ps1 几乎完全相同

**文件**: [Install-Rust.ps1](Commands/Install-Rust.ps1) vs [rustup.ps1](Providers/rustup.ps1)

**重复代码对照表**:

| 功能 | Install-Rust.ps1 | rustup.ps1 |
|------|------------------|------------|
| 目录创建 | 第73-85行 | 第118-124行 |
| RUSTUP_HOME/CARGO_HOME 设置 | 第88-93行 | 第128-133行 |
| PATH 更新 | 第95-111行 | 第135-146行 |
| rustup-init 下载 | 第119-127行 | 第149-151行 |
| 安装器执行 | 第130-141行 | 第155-161行 |
| 验证安装 | 第153-169行 | 第168-173行 |

**修复方案**: 重构为使用 `Install-Tool.ps1 -Manifest rust.json` 模式。

---

## 第三部分：P2 中优先级问题

### F-08: 模块重复定义 — .ps1 与 .psm1 文件重复

**问题**: 每个 Core 模块和 Provider 都存在 `.ps1` 和 `.psm1` 两个文件，`.psm1` 仅通过 dot-source 调用 `.ps1`。

**涉及文件**:

| .ps1 文件 | .psm1 文件 |
|-----------|-----------|
| [Config.ps1](Core/Config.ps1) | [Config.psm1](Core/Config.psm1) |
| [Executor.ps1](Core/Executor.ps1) | [Executor.psm1](Core/Executor.psm1) |
| [Pipeline.ps1](Core/Pipeline.ps1) | [Pipeline.psm1](Core/Pipeline.psm1) |
| [npm.ps1](Providers/npm.ps1) | [npm.psm1](Providers/npm.psm1) |
| [pip.ps1](Providers/pip.ps1) | [pip.psm1](Providers/pip.psm1) |
| [github-release.ps1](Providers/github-release.ps1) | [github-release.psm1](Providers/github-release.psm1) |

**修复方案**: 删除所有 `.psm1` 文件，在 `.ps1` 文件末尾添加条件导出：

```powershell
if ($MyInvocation.MyCommand.Source -eq $null) {
    Export-ModuleMember -Function @('Func1', 'Func2')
}
```

---

### F-09: PATH/环境变量设置重复 — 4 个 Provider 包含相同代码

**问题**: `zip-extract.ps1`、`git-clone.ps1`、`local.ps1`、`msi.ps1` 的 `Invoke-*Install` 方法中包含完全相同的 PATH 和环境变量设置逻辑。

**重复代码模式**（约 30 行）:
```powershell
if ($Item.pathEntries -and $Item.pathEntries.Count -gt 0) {
    Write-Info "配置 PATH 环境变量..."
    foreach ($entry in $Item.pathEntries) {
        $fullPath = if ([System.IO.Path]::IsPathRooted($entry)) { $entry } else { Join-Path $installDir $entry }
        if ($Target -eq "docker") {
            & $Executor "echo 'export PATH=`$PATH:$fullPath' >> /root/.bashrc"
        } else {
            $currentPath = [Environment]::GetEnvironmentVariable("Path", "User")
            if ($currentPath -notlike "*$fullPath*") {
                [Environment]::SetEnvironmentVariable("Path", "$currentPath;$fullPath", "User")
                $env:Path = "$env:Path;$fullPath"
            }
        }
        Write-Hint "已添加: $fullPath"
    }
}
```

**修复方案**: 在 [ProviderBase.ps1](Core/ProviderBase.ps1) 中添加通用辅助函数（见附录 A）。

---

### F-10: Provider 加载策略不一致

**问题**: `_init.ps1` 和 `Install-Tool.ps1` 采用不同的 Provider 加载策略。

| 文件 | 加载策略 |
|------|----------|
| [_init.ps1](_init.ps1#L55-L77) | 预加载所有 8 个 Provider |
| [Install-Tool.ps1](Commands/Install-Tool.ps1#L59-L90) | 按需加载，仅加载当前类型所需 |

**修复方案**: 统一为按需加载策略，在 `_init.ps1` 中添加 `Load-Provider` 函数。

---

### F-11: Docker Executor 闭包变量捕获问题

**文件**: [Executor.ps1](Core/Executor.ps1#L47-L51)

**当前代码**:
```powershell
return {
    param($cmd)
    $escapedCmd = $cmd -replace "'", "'\''"
    docker exec $ContainerName sh -c "'$escapedCmd'" 2>&1  # $ContainerName 可能未正确捕获
}
```

**问题**: PowerShell 脚本块的变量作用域可能导致 `$ContainerName` 为空或使用错误的值。

**修复方案**: 使用 `$script:` 作用域或参数传递：

```powershell
$container = $ContainerName  # 创建局部副本
return {
    param($cmd)
    $escapedCmd = $cmd -replace "'", "'\''"
    docker exec $container sh -c "'$escapedCmd'" 2>&1
}.GetNewClosure()
```

---

### F-12: Install-DockerEngine.ps1 输出函数重复定义

**文件**: [Install-DockerEngine.ps1](Commands/Install-DockerEngine.ps1#L16-L19)

**当前代码**:
```powershell
function Write-Step($Title) { Write-Host "`n━━━ $Title ━━━" -ForegroundColor Cyan }
function Write-Info($Msg) { Write-Host "  ℹ️  $Msg" -ForegroundColor Gray }
function Write-Success($Msg) { Write-Host "  ✅ $Msg" -ForegroundColor Green }
function Write-Error($Msg) { Write-Host "  ❌ $Msg" -ForegroundColor Red }
```

**问题**: 与 `BasicMethod/Core/Output` 模块的函数重复。

**修复方案**: 删除这些重复定义，导入 `BasicMethod/Core/Output` 模块。

---

## 第四部分：P3 低优先级问题

### F-13: 路径解析冗余 — $script:installRoot 未定义

**文件**: [Config.ps1](Core/Config.ps1#L45)

**当前代码**:
```powershell
$ProjectRoot = Resolve-Path (Join-Path $script:installRoot "..")  # 第45行
```

**问题**: `$script:installRoot` 从未被赋值，此分支永远不会产生有效结果。

**修复方案**: 删除此无效分支。

---

### F-14: 状态管理冗余 — $script:InstallResult 双重管理

**文件**: [Pipeline.ps1](Core/Pipeline.ps1#L21-L27)

**问题**: `$script:InstallResult` 全局变量与 `Invoke-InstallPipeline` 返回值并存，调用者可通过两种方式获取结果，造成状态管理混乱。

**修复方案**: 移除全局变量，仅使用返回值传递结果。

---

## 第五部分：重构路线图

### Phase 1: 修复致命错误（P0）

| 步骤 | 任务 | 预计耗时 |
|------|------|----------|
| 1.1 | 修复 `Install-McpServer.ps1` 属性访问错误 | 30 分钟 |
| 1.2 | 修复 `Install-McpServer.ps1` 函数调用错误 | 30 分钟 |
| 1.3 | 修复 MSI Provider Docker 环境错误 | 30 分钟 |
| 1.4 | 修复 `zip-extract.ps1` Docker 路径错误 | 30 分钟 |

### Phase 2: 消除重复代码（P1-P2）

| 步骤 | 任务 | 预计耗时 |
|------|------|----------|
| 2.1 | 提取 PATH/环境变量设置到 `ProviderBase.ps1` | 1 小时 |
| 2.2 | 删除所有 `.psm1` 重复文件 | 30 分钟 |
| 2.3 | 统一 Provider 加载策略 | 30 分钟 |
| 2.4 | 重构 `Install-Rust.ps1` 使用框架 | 1 小时 |
| 2.5 | 重构 `Install-McpServer.ps1` 使用框架 | 2 小时 |

### Phase 3: 安全与架构优化（P1-P3）

| 步骤 | 任务 | 预计耗时 |
|------|------|----------|
| 3.1 | 替换 `Invoke-Expression` 为安全方式 | 30 分钟 |
| 3.2 | 修复 Docker Executor 闭包问题 | 30 分钟 |
| 3.3 | 清理冗余的路径解析逻辑 | 30 分钟 |
| 3.4 | 统一状态管理方式 | 30 分钟 |

---

## 附录 A：建议添加到 ProviderBase.ps1 的通用函数

```powershell
function Set-PathEntries {
    param(
        [array]$PathEntries,
        [string]$BaseDir,
        [string]$Target,
        [scriptblock]$Executor
    )
    if (-not $PathEntries -or $PathEntries.Count -eq 0) { return }
    Write-Info "配置 PATH 环境变量..."
    foreach ($entry in $PathEntries) {
        $fullPath = if ([System.IO.Path]::IsPathRooted($entry)) { $entry } else { Join-Path $BaseDir $entry }
        if ($Target -eq "docker") {
            & $Executor "echo 'export PATH=`$PATH:$fullPath' >> /root/.bashrc"
        } else {
            $currentPath = [Environment]::GetEnvironmentVariable("Path", "User")
            if ($currentPath -notlike "*$fullPath*") {
                [Environment]::SetEnvironmentVariable("Path", "$currentPath;$fullPath", "User")
                $env:Path = "$env:Path;$fullPath"
            }
        }
        Write-Hint "已添加: $fullPath"
    }
}

function Set-EnvironmentVariables {
    param(
        [object]$EnvVars,
        [string]$InstallDir,
        [string]$Target,
        [scriptblock]$Executor
    )
    if (-not $EnvVars) { return }
    Write-Info "配置环境变量..."
    foreach ($var in $EnvVars.PSObject.Properties) {
        $value = $var.Value
        if ($value -match '\$installDir|\$\{installDir\}') {
            $value = $value -replace '\$installDir|\$\{installDir\}', $InstallDir
        }
        if ($Target -eq "docker") {
            & $Executor "echo 'export $($var.Name)=$value' >> /root/.bashrc"
        } else {
            [Environment]::SetEnvironmentVariable($var.Name, $value, "User")
            Set-Item "Env:$($var.Name)" $value
        }
        Write-Hint "已设置: $($var.Name) = $value"
    }
}
```

---

## 附录 B：文件依赖关系图

```
Install/
├── _init.ps1                  # 入口，加载 Core + Providers
├── manifest-schema.json       # 清单 Schema
├── CODE-AUDIT.md              # 本审计报告
├── Core/
│   ├── Config.ps1             # 清单加载与验证
│   ├── Executor.ps1           # 执行器抽象
│   ├── Pipeline.ps1           # 安装管道编排
│   └── ProviderBase.ps1       # Provider 基类与辅助函数
├── Providers/
│   ├── npm.ps1                # npm Provider
│   ├── pip.ps1                # pip Provider
│   ├── github-release.ps1     # GitHub Release Provider
│   ├── zip-extract.ps1        # ZIP 解压 Provider
│   ├── rustup.ps1             # Rustup Provider
│   ├── git-clone.ps1          # Git Clone Provider
│   ├── local.ps1              # 本地验证 Provider
│   └── msi.ps1                # MSI Provider
├── Commands/
│   ├── Install-Tool.ps1       # 通用安装入口（正确使用框架）
│   ├── Install-McpServer.ps1  # ⚠️ 绕过框架，重复实现
│   ├── Install-Rust.ps1       # ⚠️ 绕过框架，重复实现
│   ├── Install-DockerEngine.ps1# 独立脚本（无框架依赖）
│   ├── Install-TypeScriptDeps.ps1# 独立脚本（无框架依赖）
│   └── install-deps.ps1       # 独立脚本（无框架依赖）
└── Manifests/
    ├── docker-engine.json     # Docker 引擎清单
    ├── mcp-servers.json       # MCP 服务器清单
    └── rust.json              # Rust 清单
```

---

## 附录 C：问题分布统计

```
问题数按文件分布:
├── Install-McpServer.ps1  ████████████ (4)
├── rustup.ps1             ████        (2)
├── msi.ps1                ████        (2)
├── zip-extract.ps1        ████        (2)
├── Executor.ps1           ████        (2)
├── Config.ps1             ██          (1)
├── Pipeline.ps1           ██          (1)
├── Install-Rust.ps1       ██          (1)
├── Install-DockerEngine.ps1 ██        (1)
└── _init.ps1              ██          (1)

问题数按类型分布:
├── 属性/函数调用错误       ████████    (4)
├── 逻辑重复               ██████████  (5)
├── 安全风险               ████        (1)
├── 环境检测错误           ████████    (2)
└── 冗余代码               ████████    (2)
```

---

**文档版本**: v1.0  
**最后更新**: 2026-07-12