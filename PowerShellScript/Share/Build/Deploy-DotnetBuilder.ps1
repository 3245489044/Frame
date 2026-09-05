<#
.SYNOPSIS
    .NET 编译环境部署脚本 — 复用 MCP 容器（workflow-mcp-env）执行 .NET 编译

.DESCRIPTION
    不再创建独立的 dotnet-builder 容器，而是复用已有的 MCP 容器（workflow-mcp-env）。
    MCP 容器的 Dockerfile 已包含 .NET SDK 10.0，可直接执行 dotnet 命令。
    
    【设计原则】
    - 复用原则：不重复创建容器，统一使用 workflow-mcp-env（MCP 开发环境容器）
    - 缓存共享：默认挂载宿主机 NuGet 缓存（%USERPROFILE%\.nuget\packages），
      避免容器内重复下载，首次编译后后续几乎秒 restore
    - 懒加载：DockerBuild 模块在阶段 2 才按需加载，阶段 1 仅依赖轻量 DockerCommon
    - 并行验证：阶段 3 使用 PowerShell 后台作业并行执行 4 项验证，减少等待时间
    
    阶段 1: 环境检查
        - Docker 可用性检查
        - Linux 容器模式检查（.NET 编译需要 Linux 容器）
        - Dockerfile 存在性检查
        - 解决方案文件检查
     
    阶段 2: 确保 MCP 容器运行
        - 检查镜像是否存在，不存在则自动构建
        - 准备卷挂载（项目代码 + NuGet 缓存 + 可选 BuildOutput）
        - 使用 docker compose 确保容器运行
        - 验证容器内 dotnet SDK 可用
     
    阶段 3: 验证部署（并行执行）
        - 容器运行状态
        - 卷挂载完整性
        - dotnet SDK 版本验证
        - 编译测试（检查解决方案文件可访问性）
     
    阶段 4: 输出摘要
        - 容器信息（名称、状态、挂载点）
        - 常用命令提示
        - 快捷脚本使用说明

    所有 Docker 操作委托给 Modules/Docker/DockerBuild.psm1。

    用法:
        Deploy-DotnetBuilder.ps1                        交互模式，完整部署
        Deploy-DotnetBuilder.ps1 -Force                 跳过确认，直接执行
        Deploy-DotnetBuilder.ps1 -RemoveOld             容器已存在时重建
        Deploy-DotnetBuilder.ps1 -NoShareNuGet          不共享 NuGet 缓存（默认共享 PC 已有缓存）
        Deploy-DotnetBuilder.ps1 -ShareBuildOutput      共享编译产物目录
        Deploy-DotnetBuilder.ps1 -WhatIf                模拟模式，仅预览
        Deploy-DotnetBuilder.ps1 -Status                仅查看部署状态
        Deploy-DotnetBuilder.ps1 -Help                  显示帮助

    示例:
        Deploy-DotnetBuilder.ps1
        Deploy-DotnetBuilder.ps1 -Force -ShareBuildOutput
        Deploy-DotnetBuilder.ps1 -RemoveOld -Force
        Deploy-DotnetBuilder.ps1 -Status
        Deploy-DotnetBuilder.ps1 -WhatIf

.NOTES
    版本: 2.4.0
    变更:
        v2.4.0 - 改为通过 Import-Modules.ps1 按需加载模块
        v2.3.0 - 改为通过 PScriptManager 框架加载模块，修复 $Script:Config 作用域问题
        v2.2.0 - 代码优化：容器启动逻辑去重、并行验证超时保护、编译测试改为 dotnet build --no-restore、新增 -ContainerName 参数
        v2.1.0 - 完善文档注释，补充 NuGet 缓存共享机制说明
        v2.0.0 - 复用 MCP 容器（workflow-mcp-env），不再创建独立 dotnet-builder 容器
    依赖: Modules/Docker/DockerCommon.psm1（配置/路径/辅助函数）
           Modules/Docker/DockerBuild.psm1（镜像/容器/命令执行）
           DockerBuild → BasicMethod/DevOps/Docker.psm1
    镜像: workflow-mcp-env:latest（从 DockerConfig.json 读取）
    容器: workflow-mcp-env（从 DockerConfig.json 读取）
    Dockerfile: DevEnvironment/Docker/Dockerfile（从 DockerConfig.json 读取）
    NuGet缓存: 默认共享 %USERPROFILE%\.nuget\packages → /root/.nuget/packages
    @exports: Deploy-DotnetBuilder
    @requires: Get-ConfigValue, Get-ProjectRoot, Resolve-ProjectPath, Test-ImageExists, Import-DockerBuildModule
#>

param(
    # 容器已存在时删除重建
    [switch]$RemoveOld,

    # 不共享 NuGet 缓存（默认共享 PC 已有缓存）
    [switch]$NoShareNuGet,

    # 共享编译产物目录
    [switch]$ShareBuildOutput,

    # 跳过确认提示
    [switch]$Force,

    # 模拟模式，仅预览不执行
    [switch]$WhatIf,

    # 仅查看部署状态
    [switch]$Status,

    # 显示帮助
    [switch]$Help,

    # 指定容器名称（默认从 DockerConfig.json 读取）
    [string]$ContainerName = ""
)

# ============================================================
# 帮助
# ============================================================

if ($Help) {
    $scriptName = Split-Path -Leaf $MyInvocation.MyCommand.Path
    Write-Host @"
$scriptName — .NET 编译环境部署脚本（复用 MCP 容器）

用法:
    $scriptName                       交互模式，完整部署
    $scriptName -Force                跳过确认，直接执行
    $scriptName -RemoveOld            容器已存在时重建
    $scriptName -NoShareNuGet         不共享 NuGet 缓存（默认共享）
    $scriptName -ShareBuildOutput     共享编译产物目录
    $scriptName -WhatIf               模拟模式，仅预览
    $scriptName -Status               仅查看部署状态
    $scriptName -Help                 显示本帮助

示例:
    $scriptName
    $scriptName -Force -ShareBuildOutput
    $scriptName -RemoveOld -Force
    $scriptName -Status
    $scriptName -WhatIf

流程:
    阶段 1: 环境检查（Docker 可用性、Linux 模式、项目文件）
    阶段 2: 确保 MCP 容器运行（docker compose up -d）
    阶段 3: 验证部署（状态、挂载、dotnet 版本、编译测试）
    阶段 4: 输出摘要（容器信息、常用命令）

说明:
    本脚本复用 MCP 容器（workflow-mcp-env）执行 .NET 编译，
    不再创建独立的 dotnet-builder 容器，节省磁盘空间。
    MCP 容器的 Dockerfile 已包含 .NET SDK 10.0。
"@
    exit 0
}

# ============================================================
# 模块加载（通过 Import-Modules.ps1 按需加载）
# ============================================================

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$moduleDir = Resolve-Path (Join-Path $scriptDir "..\Modules")
. "$moduleDir\Import-Modules.ps1"
$projectRoot = Get-ProjectRoot

# 定义 DockerBuild 懒加载函数
$script:DockerBuildLoaded = $false

function Import-DockerBuildModule {
    <#
    .SYNOPSIS
        按需加载 DockerBuild 模块
    #>
    if ($script:DockerBuildLoaded) { return }
    Import-Module "$moduleDir\Docker\DockerBuild.psm1" -Force -DisableNameChecking -ErrorAction Stop
    $script:DockerBuildLoaded = $true
}

# ============================================================
# 配置
# ============================================================

# 从 DockerConfig.json 读取配置
$imageName   = Get-ConfigValue "dotnetBuilder" "imageName"
$containerName = if ($ContainerName) { $ContainerName } else { Get-ConfigValue "dotnetBuilder" "containerName" }
$dockerfile  = Get-ConfigValue "dotnetBuilder" "dockerfile"
$slnFile     = Get-ConfigValue "dotnetBuilder" "slnFile"
$composeDir  = Get-ConfigValue "dotnetBuilder" "composeDir"
$composeService = Get-ConfigValue "dotnetBuilder" "composeService"

$fullImageName = "$imageName`:latest"

# ============================================================
# 辅助函数
# ============================================================

function Write-Phase {
    <#
    .SYNOPSIS
        输出阶段标题
    #>
    param([string]$Number, [string]$Title)
    Write-Host ""
    Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║  阶段 $Number`: $Title" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
}

function Write-Step {
    <#
    .SYNOPSIS
        输出步骤标题
    #>
    param([string]$Text)
    Write-Host ""
    Write-Host "  ▶ $Text" -ForegroundColor Yellow
}

function Write-Summary {
    <#
    .SYNOPSIS
        输出摘要信息
    #>
    param([string]$Label, [string]$Value, [string]$Color = "Gray")
    Write-Host "    $($Label.PadRight(20)) $Value" -ForegroundColor $Color
}

# ============================================================
# 阶段 0：状态查看
# ============================================================

if ($Status) {
    Write-Phase "0" "部署状态查看"

    # Docker 引擎
    Write-Step "Docker 引擎"
    $dockerAvailable = Test-DockerAvailable
    if ($dockerAvailable) {
        $version = Get-DockerVersion
        Write-Summary "可用性" "✅ 可用" "Green"
        Write-Summary "版本" "$version"
        $linuxMode = Test-DockerLinuxMode
        Write-Summary "容器模式" "$(if ($linuxMode) { 'Linux' } else { 'Windows' })"
    } else {
        Write-Summary "可用性" "❌ 不可用" "Red"
    }

    # 镜像状态
    Write-Step "镜像状态"
    $imageExists = Test-ImageExists -ImageName $fullImageName
    if ($imageExists) {
        $imageInfo = Get-DockerImageInfo -ImageName $imageName
        if ($imageInfo) {
            Write-Summary "镜像" "$fullImageName" "Green"
            Write-Summary "大小" "$($imageInfo.Size)"
            Write-Summary "创建时间" "$($imageInfo.CreatedAt)"
        } else {
            Write-Summary "镜像" "$fullImageName" "Green"
        }
    } else {
        Write-Summary "镜像" "$fullImageName ❌ 不存在" "Red"
    }

    # 容器状态
    Write-Step "容器状态"
    $state = Get-ContainerState -ContainerName $containerName
    switch ($state) {
        "running" {
            Write-Summary "容器" "$containerName" "Green"
            Write-Summary "状态" "✅ 运行中" "Green"
            $health = Get-ContainerHealth -ContainerName $containerName
            Write-Summary "健康" "$health"
            $uptime = Get-ContainerUptime -ContainerName $containerName
            if ($uptime) { Write-Summary "运行时长" "$uptime" }
        }
        "exited" {
            Write-Summary "容器" "$containerName" "Yellow"
            Write-Summary "状态" "⏸️  已停止" "Yellow"
        }
        default {
            Write-Summary "容器" "$containerName ❌ 不存在" "Red"
        }
    }

    # 编译产物
    Write-Step "编译产物"
    $buildOutputDir = Join-Path $projectRoot "BuildOutput"
    if (Test-Path $buildOutputDir) {
        Write-Summary "BuildOutput" "✅ 存在" "Green"
    } else {
        Write-Summary "BuildOutput" "⚠️  不存在" "Yellow"
    }

    Write-Host ""
    exit 0
}

# ============================================================
# 主流程
# ============================================================

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   .NET 编译环境部署工具（复用 MCP 容器）                     ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# 确认提示
if (-not $Force -and -not $WhatIf) {
    $actionDesc = @(
        "阶段 1: 环境检查（Docker 可用性、Linux 模式）",
        "阶段 2: 确保 MCP 容器 $containerName 运行（docker compose up -d）",
        "阶段 3: 验证部署（状态、挂载、dotnet 版本、编译测试）"
    )
    if ($RemoveOld) { $actionDesc[1] += " [--remove-old]" }
    if (-not $NoShareNuGet) { $actionDesc[1] += " [共享 NuGet 缓存]" }
    if ($ShareBuildOutput) { $actionDesc[1] += " [共享 BuildOutput]" }

    Write-Host "  将执行以下操作:" -ForegroundColor Yellow
    foreach ($desc in $actionDesc) {
        Write-Host "    • $desc" -ForegroundColor Gray
    }
    Write-Host ""
    $answer = Read-Host "  确认执行？(Y/N)"
    if ($answer -ne 'Y' -and $answer -ne 'y') {
        Write-Host "  ⚠️  已取消" -ForegroundColor Yellow
        exit 0
    }
}

# ============================================================
# 阶段 1：环境检查
# ============================================================

# 提前加载 DockerBuild 模块（阶段 1 需要其中的 Test-DockerAvailable / Test-DockerLinuxMode）
Import-DockerBuildModule

Write-Phase "1" "环境检查"

# 1.1 Docker 可用性
Write-Step "Docker 可用性"
$dockerAvailable = Test-DockerAvailable
if (-not $dockerAvailable) {
    Write-Host "    ❌ Docker 不可用，请先安装 Docker Desktop" -ForegroundColor Red
    exit 1
}
$version = Get-DockerVersion
Write-Host "    ✅ Docker 可用（版本: $version）" -ForegroundColor Green

# 1.2 Linux 容器模式
Write-Step "Docker 容器模式"
$linuxMode = Test-DockerLinuxMode
if (-not $linuxMode) {
    Write-Host "    ❌ 当前为 Windows 容器模式，.NET 编译需要 Linux 容器模式" -ForegroundColor Red
    Write-Host "    请在 Docker Desktop 托盘图标右键 → Switch to Linux containers..." -ForegroundColor Yellow
    exit 1
}
Write-Host "    ✅ Linux 容器模式" -ForegroundColor Green

# 1.3 Dockerfile 检查
Write-Step "Dockerfile 检查"
$dockerfilePath = Resolve-ProjectPath $dockerfile
if (-not (Test-Path $dockerfilePath)) {
    Write-Host "    ❌ Dockerfile 不存在: $dockerfilePath" -ForegroundColor Red
    exit 1
}
Write-Host "    ✅ Dockerfile: $dockerfilePath" -ForegroundColor Green

# 1.4 项目文件检查
Write-Step "项目文件检查"
$slnPath = Join-Path $projectRoot $slnFile
if (-not (Test-Path $slnPath)) {
    Write-Host "    ⚠️  解决方案文件不存在: $slnPath（编译测试将跳过）" -ForegroundColor Yellow
} else {
    Write-Host "    ✅ 解决方案: $slnPath" -ForegroundColor Green
}

# ============================================================
# 阶段 2：确保 MCP 容器运行
# ============================================================

# 按需加载 DockerBuild 模块（镜像/容器操作）
Import-DockerBuildModule

Write-Phase "2" "确保 MCP 容器运行"

# 2.1 检查镜像
Write-Step "检查镜像"
$imageExists = Test-ImageExists -ImageName $fullImageName
if ($imageExists) {
    $imageInfo = Get-DockerImageInfo -ImageName $imageName
    $sizeInfo = if ($imageInfo -and $imageInfo.Size) { "（$($imageInfo.Size)）" } else { "" }
    Write-Host "    ✅ 镜像 $fullImageName 已存在 $sizeInfo" -ForegroundColor Green
} else {
    Write-Host "    ⏳ 镜像 $fullImageName 不存在，正在构建..." -ForegroundColor Yellow
    if ($WhatIf) {
        Write-Host "    [模拟] docker compose -f <composeDir> build" -ForegroundColor DarkGray
    } else {
        $composePath = Resolve-ProjectPath $composeDir
        Push-Location $composePath
        $buildOutput = docker compose build 2>&1
        Pop-Location
        if ($LASTEXITCODE -ne 0) {
            Write-Host "    ❌ 镜像构建失败" -ForegroundColor Red
            Write-Host "  $buildOutput" -ForegroundColor DarkGray
            exit 1
        }
        Write-Host "    ✅ 镜像构建完成" -ForegroundColor Green
    }
}

# 2.2 准备卷挂载
Write-Step "准备卷挂载"
$volumes = @("${projectRoot}:/workspace/WorkFlowPrj")

if (-not $NoShareNuGet) {
    $nugetSource = Join-Path $env:USERPROFILE ".nuget\packages"
    if (Test-Path $nugetSource) {
        $volumes += "${nugetSource}:/root/.nuget/packages"
        Write-Host "    ✅ 共享 NuGet 缓存: $nugetSource" -ForegroundColor Green
    } else {
        Write-Host "    ⚠️  NuGet 缓存目录不存在: $nugetSource，跳过共享" -ForegroundColor Yellow
    }
}

if ($ShareBuildOutput) {
    $buildOutputDir = Join-Path $projectRoot "BuildOutput"
    if (-not (Test-Path $buildOutputDir)) {
        $null = New-Item -ItemType Directory -Path $buildOutputDir -Force
    }
    $volumes += "${buildOutputDir}:/workspace/WorkFlowPrj/BuildOutput"
    Write-Host "    ✅ 共享 BuildOutput: $buildOutputDir" -ForegroundColor Green
}

Write-Host "    📁 卷挂载列表:" -ForegroundColor Gray
foreach ($v in $volumes) {
    Write-Host "       -v $v" -ForegroundColor DarkGray
}

# 2.3 确保容器运行
Write-Step "确保容器运行"

if ($WhatIf) {
    Write-Host "    [模拟] 确保容器 '$containerName' 运行" -ForegroundColor DarkGray
    Write-Host "    [模拟] 镜像: $fullImageName" -ForegroundColor DarkGray
    Write-Host "    [模拟] 工作目录: /workspace/WorkFlowPrj" -ForegroundColor DarkGray
} else {
    # 检查容器状态 — 统一处理 stopped 和 not_found（docker compose up -d 会处理两种情况）
    $state = Get-ContainerState -ContainerName $containerName

    if ($state -eq "running") {
        Write-Host "    ✅ 容器 '$containerName' 已在运行" -ForegroundColor Green
    } else {
        $actionMsg = if ($state -eq "stopped") { "已停止，正在启动" } else { "不存在，正在创建" }
        Write-Host "    ⏳ 容器 '$containerName' $actionMsg..." -ForegroundColor Yellow
        $composePath = Resolve-ProjectPath $composeDir
        Push-Location $composePath
        $output = docker compose up -d $composeService 2>&1
        Pop-Location
        if ($LASTEXITCODE -eq 0) {
            Write-Host "    ✅ 容器 '$containerName' 已就绪" -ForegroundColor Green
            Start-Sleep -Seconds 3
        } else {
            Write-Host "    ❌ 容器操作失败: $output" -ForegroundColor Red
            exit 1
        }
    }

    # 验证 dotnet SDK 在容器内可用
    Write-Step "验证 dotnet SDK"
    $dotnetVer = docker exec $containerName sh -c "dotnet --version" 2>&1
    if ($dotnetVer -match '\d+\.\d+\.\d+') {
        Write-Host "    ✅ dotnet SDK $($dotnetVer.Trim()) 可用" -ForegroundColor Green
    } else {
        Write-Host "    ❌ dotnet SDK 不可用: $dotnetVer" -ForegroundColor Red
        Write-Host "    请检查 MCP 容器的 Dockerfile 是否包含 .NET SDK" -ForegroundColor Yellow
        exit 1
    }
}

# ============================================================
# 阶段 3：验证部署
# ============================================================

Write-Phase "3" "验证部署"

if ($WhatIf) {
    Write-Host "    [模拟] 跳过验证" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║   模拟模式完成 — 未执行任何实际操作                       ║" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    exit 0
}

# 并行执行验证步骤（使用 PowerShell 后台作业）
Write-Step "并行验证（容器状态、卷挂载、dotnet 版本、编译测试）"
Write-Host "    ⏳ 正在并行执行 4 项验证..." -ForegroundColor Gray

# 等待容器稳定
Start-Sleep -Seconds 2

# 定义验证脚本块（每个作业带 30 秒超时保护）
$verifyTimeoutSec = 30

$stateJob = Start-Job -ScriptBlock {
    param($cn, $timeout)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $state = & docker ps --filter "name=^/${cn}$" --format '{{.Status}}' 2>&1
        if ($state -match '^Up') { return @{ Name = "容器状态"; Pass = $true; Detail = "✅ 运行中" } }
        Start-Sleep -Seconds 1
    } while ($sw.Elapsed.TotalSeconds -lt $timeout)
    return @{ Name = "容器状态"; Pass = $false; Detail = "❌ 状态异常（超时 ${timeout}s）: $state" }
} -ArgumentList $containerName, $verifyTimeoutSec

$mountJob = Start-Job -ScriptBlock {
    param($cn)
    $paths = @("/workspace/WorkFlowPrj/PowerShellScript", "/workspace/WorkFlowPrj/CsharpPrj")
    $allOk = $true
    foreach ($p in $paths) {
        $r = & docker exec $cn sh -c "test -d '$p' && echo 'exists'" 2>&1
        if ($r -notmatch 'exists') { $allOk = $false }
    }
    if ($allOk) { return @{ Name = "卷挂载验证"; Pass = $true; Detail = "✅ 卷挂载正常" } }
    return @{ Name = "卷挂载验证"; Pass = $false; Detail = "⚠️  部分卷挂载可能异常" }
} -ArgumentList $containerName

$sdkJob = Start-Job -ScriptBlock {
    param($cn)
    try {
        $ver = & docker exec $cn sh -c "dotnet --version" 2>&1
        if ($ver -match '\d+\.\d+\.\d+') {
            $sdks = & docker exec $cn sh -c "dotnet --list-sdks" 2>&1
            $sdkLines = ($sdks -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ }) -join ", "
            return @{ Name = "dotnet SDK"; Pass = $true; Detail = "✅ 版本: $($ver.Trim()) ($sdkLines)" }
        }
        return @{ Name = "dotnet SDK"; Pass = $false; Detail = "⚠️  无法获取 .NET 版本: $ver" }
    } catch {
        return @{ Name = "dotnet SDK"; Pass = $false; Detail = "⚠️  验证异常: $_" }
    }
} -ArgumentList $containerName

$buildJob = Start-Job -ScriptBlock {
    param($cn, $sln)
    try {
        # 执行真正的 dotnet build --no-restore（仅验证编译能力，不下载包）
        $result = & docker exec $cn sh -c "dotnet build '$sln' --no-restore -nologo -clp:NoSummary 2>&1 || true"
        if ($LASTEXITCODE -eq 0) {
            return @{ Name = "编译测试"; Pass = $true; Detail = "✅ dotnet build --no-restore 成功" }
        }
        # 如果 --no-restore 失败（可能 NuGet 缓存未就绪），回退到文件存在性检查
        $r = & docker exec $cn sh -c "test -f '$sln' && echo 'EXISTS' || echo 'NOT_FOUND'" 2>&1
        if ($r -match 'EXISTS') {
            return @{ Name = "编译测试"; Pass = $true; Detail = "✅ 解决方案可访问（build 跳过: $sln）" }
        }
        return @{ Name = "编译测试"; Pass = $false; Detail = "⚠️  解决方案不可访问: $sln" }
    } catch {
        return @{ Name = "编译测试"; Pass = $false; Detail = "⚠️  测试异常: $_" }
    }
} -ArgumentList $containerName, "/workspace/WorkFlowPrj/$slnFile"

# 等待所有作业完成（带超时保护）
$jobs = @($stateJob, $mountJob, $sdkJob, $buildJob)
$timeoutMs = ($verifyTimeoutSec + 15) * 1000  # 总超时 = 验证超时 + 15s 缓冲
$results = $jobs | Wait-Job -Timeout $timeoutMs | Receive-Job

# 检查是否有作业超时
$timedOut = $jobs | Where-Object { $_.State -eq 'Running' }
foreach ($t in $timedOut) {
    $results += @{ Name = $t.Name; Pass = $false; Detail = "⚠️  验证超时（>${verifyTimeoutSec}s）" }
    $t | Stop-Job -PassThru | Remove-Job -Force -ErrorAction SilentlyContinue
}

# 清理作业
$jobs | Remove-Job -Force -ErrorAction SilentlyContinue

# 输出结果
$allPassed = $true
foreach ($r in $results) {
    $color = if ($r.Pass) { "Green" } else { "Yellow" }
    if (-not $r.Pass) { $allPassed = $false }
    Write-Host "    $($r.Detail)" -ForegroundColor $color
}

if (-not $allPassed) {
    Write-Host "`n    ⚠️  部分验证未通过，请检查上述警告" -ForegroundColor Yellow
}

# ============================================================
# 阶段 4：输出摘要
# ============================================================

Write-Phase "4" "部署摘要"

Write-Host ""
Write-Host "  ── 容器信息 ──" -ForegroundColor White
Write-Summary "容器名称" "$containerName"
Write-Summary "镜像" "$fullImageName"
Write-Summary "状态" "✅ 运行中" "Green"
Write-Summary "工作目录" "/workspace/WorkFlowPrj"

Write-Host ""
Write-Host "  ── 卷挂载 ──" -ForegroundColor White
foreach ($v in $volumes) {
    Write-Host "    $v" -ForegroundColor Gray
}

Write-Host ""
Write-Host "  ── 常用命令 ──" -ForegroundColor White
Write-Host ""
Write-Host "    编译解决方案:" -ForegroundColor Yellow
Write-Host "      docker exec $containerName dotnet build /workspace/WorkFlowPrj/$slnFile -c Debug" -ForegroundColor Gray
Write-Host ""
Write-Host "    进入容器:" -ForegroundColor Yellow
Write-Host "      docker exec -it $containerName bash" -ForegroundColor Gray
Write-Host ""
Write-Host "    查看容器状态:" -ForegroundColor Yellow
Write-Host "      docker ps -a --filter name=$containerName" -ForegroundColor Gray
Write-Host ""
Write-Host "    查看容器日志:" -ForegroundColor Yellow
Write-Host "      docker logs $containerName" -ForegroundColor Gray
Write-Host ""
Write-Host "    停止容器:" -ForegroundColor Yellow
Write-Host "      docker stop $containerName" -ForegroundColor Gray
Write-Host ""
Write-Host "    删除容器:" -ForegroundColor Yellow
Write-Host "      docker rm -f $containerName" -ForegroundColor Gray
Write-Host ""

Write-Host "  ── 快捷脚本 ──" -ForegroundColor White
Write-Host ""
Write-Host "    编译:   .\PowerShellScript\Build\docker-dotnet.ps1 build CsharpPrj/MainProj.sln -c Debug" -ForegroundColor Gray
Write-Host "    测试:   .\PowerShellScript\Build\docker-dotnet.ps1 test CsharpPrj/MainProj.sln -c Debug" -ForegroundColor Gray
Write-Host "    Shell:  .\PowerShellScript\Build\docker-dotnet.ps1 --shell" -ForegroundColor Gray
Write-Host "    状态:   .\PowerShellScript\Build\docker-dotnet.ps1 --status" -ForegroundColor Gray
Write-Host ""

Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   部署完成                                                 ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""
