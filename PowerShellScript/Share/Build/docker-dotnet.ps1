<#
.SYNOPSIS
    Docker 编译快捷脚本 — 在 MCP 容器中编译 C# / TypeScript

.DESCRIPTION
    统一编译入口，支持在 Docker 容器（workflow-mcp-env）中执行：
      - C# 编译（dotnet build/test/restore/clean）
      - TypeScript 编译（tsc --build）
      - 容器管理（状态查看、进入 Shell、构建镜像）

    所有 Docker 操作委托给 Modules/Docker/DockerBuild.psm1。

    用法:
        docker-dotnet.ps1 build CsharpPrj/MainProj.sln -c Debug
        docker-dotnet.ps1 test CsharpPrj/MainProj.sln -c Debug
        docker-dotnet.ps1 restore CsharpPrj/MainProj.sln
        docker-dotnet.ps1 typescript                   编译全部 TypeScript
        docker-dotnet.ps1 typescript -Project BasicMethod  编译指定 TS 子项目
        docker-dotnet.ps1 --build-image                构建镜像
        docker-dotnet.ps1 --shell                      进入容器
        docker-dotnet.ps1 --status                     查看状态
        docker-dotnet.ps1 --share-nuget build ...      共享 NuGet 缓存
        docker-dotnet.ps1 --share-output build ...     共享编译产物

.NOTES
    依赖: Modules/Docker/DockerBuild.psm1（通过 Import-Modules.ps1 加载）
    镜像: workflow-mcp-env:latest（复用 MCP 容器，含 .NET SDK + Node.js）
    容器: workflow-mcp-env
    Dockerfile: DevEnvironment/Docker/Dockerfile
    版本: 3.0.0
    变更:
        v3.0.0 - 统一模块加载方式（Import-Modules.ps1），新增 TypeScript 编译能力
        v2.0.0 - 复用 MCP 容器（workflow-mcp-env），不再创建独立 dotnet-builder 容器
# @requires Write-Header, Write-Success, Write-Info, Write-SubHeader, Write-Warning, Write-Error
# @requires Confirm-Action, Invoke-Step, Get-ProjectRoot
# @requires Test-ContainerRunning, Get-ContainerState, Invoke-DockerCommand
# @requires Test-CommandExists, Test-DockerEngineRunning
#>

param(
    # 特殊命令
    [switch]$BuildImage,
    [switch]$Shell,
    [switch]$Status,

    # 共享宿主机 NuGet 包缓存
    [switch]$ShareNuGet,

    # 共享编译产物目录
    [switch]$ShareOutput,

    # TypeScript 子项目名称（与 typescript 命令配合使用）
    [string]$Project = "",

    # 透传给 dotnet 的参数（剩余所有参数）
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Remaining
)

# ============================================================
# 加载模块（统一使用 Import-Modules.ps1）
# ============================================================

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$moduleDir = Resolve-Path (Join-Path $scriptDir "..\Modules")
. "$moduleDir\Import-Modules.ps1"

function Get-ContainerImageState {
    param([string]$ImageName)
    $result = docker images -q $ImageName 2>&1
    if ($result) { return "exists" }
    return "not_found"
}


# ============================================================
# 配置常量
# ============================================================

$script:ProjectRoot   = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$script:ContainerName = "workflow-mcp-env"
$script:ImageName     = "workflow-mcp-env:latest"
$script:CsharpSln     = "CsharpPrj/MainProj.sln"
$script:TsProjectRoot = "TypeScriptPrj"
$script:TsConfigFile  = "TypeScriptPrj/tsconfig.json"
$script:ComposeDir    = "DevEnvironment/Docker"

# ============================================================
# 容器管理函数
# ============================================================

function Ensure-ContainerRunning {
    <#
    .SYNOPSIS
        确保 MCP 容器正在运行，未运行则自动启动
    .OUTPUTS
        [bool] 容器就绪返回 $true
    #>
    $state = Get-ContainerState -ContainerName $script:ContainerName

    if ($state -eq "running") {
        Write-Success "容器 '$($script:ContainerName)' 已在运行"
        return $true
    }

    if ($state -eq "stopped") {
        Write-Info "容器 '$($script:ContainerName)' 已停止，正在启动..."
        $output = docker start $script:ContainerName 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Success "容器 '$($script:ContainerName)' 已启动"
            Start-Sleep -Seconds 2
            return $true
        } else {
            Write-Error "容器启动失败: $output"
            return $false
        }
    }

    # 容器不存在，需要创建
    Write-Info "容器 '$($script:ContainerName)' 不存在，正在创建..."

    # 检查镜像
    $imgState = Get-ContainerImageState -ImageName $script:ImageName
    if ($imgState -eq "not_found") {
        Write-Info "镜像 '$($script:ImageName)' 不存在，正在构建..."
        $composePath = Join-Path $script:ProjectRoot $script:ComposeDir
        Push-Location $composePath
        $buildOutput = docker compose build 2>&1
        Pop-Location
        if ($LASTEXITCODE -ne 0) {
            Write-Error "镜像构建失败"
            Write-Host "  $buildOutput" -ForegroundColor DarkGray
            return $false
        }
        Write-Success "镜像 '$($script:ImageName)' 构建完成"
    }

    # 使用 docker compose 启动
    $composePath = Join-Path $script:ProjectRoot $script:ComposeDir
    Push-Location $composePath
    $output = docker compose up -d 2>&1
    Pop-Location
    if ($LASTEXITCODE -eq 0) {
        Write-Success "容器 '$($script:ContainerName)' 已创建并启动"
        Start-Sleep -Seconds 3
        return $true
    } else {
        Write-Error "容器创建失败: $output"
        return $false
    }
}

function Invoke-ContainerCommand {
    <#
    .SYNOPSIS
        在容器内执行命令并输出结果
    .PARAMETER Command
        要执行的命令
    .PARAMETER Description
        命令描述
    .PARAMETER WorkingDir
        容器内工作目录
    .OUTPUTS
        [bool] 成功返回 $true
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,

        [string]$Description = "",
        [string]$WorkingDir = ""
    )

    if ($Description) {
        Write-Host ""
        Write-Host "  ⏳ $Description..." -ForegroundColor Yellow
    }
    Write-Info "命令: $Command"

    $execArgs = @("exec")
    if ($WorkingDir) {
        $execArgs += "-w"; $execArgs += $WorkingDir
    }
    $execArgs += $script:ContainerName
    $execArgs += @("sh", "-c", $Command)

    try {
        $output = & docker @execArgs 2>&1
        $exitCode = $LASTEXITCODE

        if ($output) {
            $output | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
        }

        if ($exitCode -eq 0) {
            if ($Description) { Write-Success "$Description 完成" }
            return $true
        } else {
            Write-Error "$Description 失败（退出码: $exitCode）"
            return $false
        }
    } catch {
        Write-Error "命令执行异常: $_"
        return $false
    }
}

# ============================================================
# 编译函数
# ============================================================

function Invoke-CSharpBuild {
    <#
    .SYNOPSIS
        在 Docker 容器内编译 C# 项目
    #>
    param([string]$Command)

    Write-SubHeader "C# 编译（容器: $($script:ContainerName)）"

    # 确保容器就绪
    $ok = Ensure-ContainerRunning
    if (-not $ok) { return $false }

    # 执行 dotnet 命令
    $slnPath = "/workspace/WorkFlowPrj/$($script:CsharpSln)"
    $result = Invoke-ContainerCommand -Command "dotnet $Command" -Description "dotnet $Command" -WorkingDir "/workspace/WorkFlowPrj"

    return $result
}

function Invoke-TypeScriptBuild {
    <#
    .SYNOPSIS
        在 Docker 容器内编译 TypeScript 项目
    .PARAMETER SubProject
        可选，指定子项目名称
    #>
    param([string]$SubProject = "")

    Write-SubHeader "TypeScript 编译（容器: $($script:ContainerName)）"

    # 确保容器就绪
    $ok = Ensure-ContainerRunning
    if (-not $ok) { return $false }

    $tsPrjRoot = "/workspace/WorkFlowPrj/$($script:TsProjectRoot)"

    # 检查 node_modules 是否已存在
    $checkResult = docker exec $script:ContainerName sh -c "test -d '$tsPrjRoot/node_modules' && echo 'EXISTS'" 2>&1
    $needInstall = ($checkResult -notmatch 'EXISTS')

    if ($needInstall) {
        Write-Info "依赖未就绪，正在安装 pnpm 依赖..."
        $installOk = Invoke-ContainerCommand -Command "cd '$tsPrjRoot' && pnpm install" -Description "安装项目依赖"
        if (-not $installOk) {
            Write-Error "依赖安装失败"
            return $false
        }
    } else {
        Write-Info "依赖已就绪，跳过安装"
    }

    # 执行 TypeScript 编译
    if ($SubProject) {
        $tscConfig = "$tsPrjRoot/TsWorkspace/$SubProject/tsconfig.json"
        Write-Info "编译子项目: $SubProject"
        $result = Invoke-ContainerCommand -Command "cd '$tsPrjRoot' && npx tsc --build '$tscConfig'" -Description "TypeScript 编译（$SubProject）"
    } else {
        $result = Invoke-ContainerCommand -Command "cd '$tsPrjRoot' && npx tsc --build" -Description "TypeScript 编译（全部）"
    }

    return $result
}

# ============================================================
# 状态查看
# ============================================================

function Show-DockerBuildStatus {
    Write-Header "Docker 编译环境状态"

    # Docker 引擎
    $dockerOk = Test-CommandExists "docker"
    if ($dockerOk) {
        $ver = docker --version 2>&1
        Write-Success "Docker 引擎: $ver"
    } else {
        Write-Error "Docker 不可用"
        return
    }

    # 容器状态
    $state = Get-ContainerState -ContainerName $script:ContainerName
    $imgState = Get-ContainerImageState -ImageName $script:ImageName

    $imgLabel = if ($imgState -eq "exists") { "✅" } else { "❌" }
    $stateLabel = switch ($state) {
        "running"   { "✅ 运行中" }
        "stopped"   { "⏸️  已停止" }
        "not_found" { "❌ 不存在" }
        default     { "❓ $state" }
    }

    Write-Host ""
    Write-Host "  ── MCP 容器 ──" -ForegroundColor White
    Write-Info "容器: $($script:ContainerName) ($stateLabel)"
    Write-Info "镜像: $($script:ImageName) $imgLabel"

    # 编译产物检查
    Write-Host ""
    Write-SubHeader "编译产物检查"
    $checkItems = @(
        @{ Path = "BuildOutput\Debug\net10.0\BasicMethod.dll"; Label = "C#: BasicMethod.dll" }
        @{ Path = "TypeScriptPrj\TsWorkspace\MCPBridge\build\index.js"; Label = "TS: MCPBridge" }
        @{ Path = "TypeScriptPrj\TsWorkspace\MCPFramework\build\index.js"; Label = "TS: MCPFramework" }
    )

    foreach ($item in $checkItems) {
        $fullPath = Join-Path $script:ProjectRoot $item.Path
        if (Test-Path $fullPath) {
            Write-Success "$($item.Label) ✅"
        } else {
            Write-Warning "$($item.Label) ❌ 未找到"
        }
    }
}

# ============================================================
# 操作分发
# ============================================================

# --status：查看状态
if ($Status) {
    Show-DockerBuildStatus
    exit 0
}

# --build-image：构建镜像
if ($BuildImage) {
    Write-Info "构建镜像 $($script:ImageName)..."
    $composePath = Join-Path $script:ProjectRoot $script:ComposeDir
    Push-Location $composePath
    $buildOutput = docker compose build 2>&1
    Pop-Location
    if ($LASTEXITCODE -eq 0) {
        Write-Success "镜像构建完成"
        exit 0
    } else {
        Write-Error "镜像构建失败"
        Write-Host "  $buildOutput" -ForegroundColor DarkGray
        exit 1
    }
}

# --shell：进入容器
if ($Shell) {
    $ok = Ensure-ContainerRunning
    if ($ok) {
        docker exec -it $script:ContainerName bash
    }
    exit 0
}

# typescript：编译 TypeScript
if ($Remaining.Count -gt 0 -and $Remaining[0] -eq 'typescript') {
    $ok = Invoke-TypeScriptBuild -SubProject $Project
    exit $(if ($ok) { 0 } else { 1 })
}

# 默认：透传 dotnet 命令
if ($Remaining.Count -eq 0) {
    Write-Host @"
用法:
  docker-dotnet.ps1 build CsharpPrj/MainProj.sln -c Debug
  docker-dotnet.ps1 test CsharpPrj/MainProj.sln -c Debug
  docker-dotnet.ps1 restore CsharpPrj/MainProj.sln
  docker-dotnet.ps1 typescript                   编译全部 TypeScript
  docker-dotnet.ps1 typescript -Project BasicMethod  编译指定 TS 子项目
  docker-dotnet.ps1 --build-image                构建镜像
  docker-dotnet.ps1 --shell                      进入容器
  docker-dotnet.ps1 --status                     查看状态
  docker-dotnet.ps1 --share-nuget build ...      共享 NuGet 缓存
  docker-dotnet.ps1 --share-output build ...     共享编译产物
"@
    exit 1
}

$dotnetArgs = $Remaining -join ' '
Write-Host "🚀 dotnet $dotnetArgs" -ForegroundColor Cyan

# 构建卷挂载参数
$volumes = @("${script:ProjectRoot}:/workspace/WorkFlowPrj")
if ($ShareNuGet) {
    $nugetSource = Join-Path $env:USERPROFILE ".nuget\packages"
    if (Test-Path $nugetSource) {
        $volumes += "${nugetSource}:/root/.nuget/packages"
        Write-Info "共享 NuGet 缓存: $nugetSource"
    }
}
if ($ShareOutput) {
    $buildOutputDir = Join-Path $script:ProjectRoot "BuildOutput"
    if (-not (Test-Path $buildOutputDir)) {
        $null = New-Item -ItemType Directory -Path $buildOutputDir -Force
    }
    $volumes += "${buildOutputDir}:/workspace/WorkFlowPrj/BuildOutput"
    Write-Info "共享编译产物: $buildOutputDir"
}

# 确保容器运行
$ok = Ensure-ContainerRunning
if (-not $ok) { exit 1 }

# 执行 dotnet 命令
$slnPath = "/workspace/WorkFlowPrj/$($script:CsharpSln)"
$result = Invoke-ContainerCommand -Command "dotnet $dotnetArgs" -Description "dotnet $dotnetArgs" -WorkingDir "/workspace/WorkFlowPrj"
exit $(if ($result) { 0 } else { 1 })
