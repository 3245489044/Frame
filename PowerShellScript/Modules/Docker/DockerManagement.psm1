<#
.SYNOPSIS
    Docker 容器生命周期管理模块
.DESCRIPTION
    提供 Docker 引擎和容器的统一管理功能，包括：
    - 状态查看（引擎、容器、资源占用）
    - 容器生命周期（构建、启动、停止、重启、进入）
    - 日志查看
    - 诊断和修复（委托给 Tools/Docker/ 下的脚本）
.NOTES
    模块名: DockerManagement
    依赖: DockerCommon（共享常量、辅助函数）
#>

# ============================================================
# 模块初始化
# ============================================================

# 依赖：DockerCommon 由 Docker.psd1 的 NestedModules 先加载

# ============================================================
# 核心函数
# ============================================================

<#
.SYNOPSIS
    获取 Docker 引擎 + 容器状态概览
.EXAMPLE
    Get-DockerStatus
#>
function Get-DockerStatus {
    [CmdletBinding()]
    param()

    Write-Header "Docker 状态概览"

    # 1. Docker 引擎
    Write-Host "  ── Docker 引擎 ──" -ForegroundColor White
    if (-not (Test-CommandExists "docker")) {
        Write-Error "Docker 命令不可用"
        return @{ Engine = "unavailable"; Container = "unknown" }
    }

    if (Test-DockerEngineRunning) {
        $version = Get-DockerVersion
        Write-Success "引擎版本: $version"
        $engineStatus = "running"
    } else {
        Write-Error "引擎不可用"
        $engineStatus = "stopped"
    }

    # 2. 容器状态
    Write-Host "`n  ── 容器状态 ──" -ForegroundColor White
    $state = Get-ContainerState
    if ($state -ne "unknown") {
        $health = Get-ContainerHealth
        $uptime = Get-ContainerUptime

        Write-Success "容器: $(Get-ContainerName)"
        Write-Info "状态: $state"
        Write-Info "健康: $health"
        if ($uptime) {
            Write-Info "运行时长: $uptime"
        }
        $containerStatus = $state
    } else {
        Write-Error "容器 $(Get-ContainerName) 不存在"
        $containerStatus = "not_found"
    }

    # 3. 资源占用
    if ($containerStatus -eq "running") {
        Write-Host "`n  ── 资源占用 ──" -ForegroundColor White
        try {
            $stats = docker stats $(Get-ContainerName) --no-stream --format "table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.MemPerc}}\t{{.NetIO}}\t{{.BlockIO}}" 2>&1
            $stats | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
        } catch {
            Write-Warning "无法获取资源占用: $_"
        }
    }

    # 4. 端口映射
    if ($containerStatus -eq "running") {
        Write-Host "`n  ── 端口映射 ──" -ForegroundColor White
        try {
            $portMapping = docker port $(Get-ContainerName) 2>&1
            if ($portMapping) {
                $portMapping | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
            } else {
                Write-Info "无端口映射"
            }
        } catch {
            Write-Warning "无法获取端口映射: $_"
        }
    }

    return @{
        Engine     = $engineStatus
        Container  = $containerStatus
    }
}

<#
.SYNOPSIS
    构建 Docker 镜像
.DESCRIPTION
    使用 docker compose build 构建镜像。
.PARAMETER NoCache
    不使用缓存，完全重新构建
.PARAMETER Service
    只构建指定服务（默认构建所有）
.EXAMPLE
    Build-DockerImage
.EXAMPLE
    Build-DockerImage -NoCache
#>
function Build-DockerImage {
    [CmdletBinding()]
    param(
        [switch]$NoCache,
        [string]$Service = ""
    )

    Write-Header "构建 Docker 镜像"

    $composePath = Resolve-ProjectPath (Get-ComposeFile)
    if (-not (Test-Path $composePath)) {
        Write-Error "docker-compose.yml 不存在: $composePath"
        return $false
    }

    Write-Info "配置文件: $composePath"
    if ($Service) {
        Write-Info "服务: $Service"
    }
    if ($NoCache) {
        Write-Info "模式: 无缓存重建"
    }

    $buildArgs = @("compose", "-f", $composePath, "build")
    if ($NoCache) {
        $buildArgs += "--no-cache"
    }
    if ($Service) {
        $buildArgs += $Service
    }

    Write-Host "`n  开始构建..." -ForegroundColor Yellow
    $result = docker @buildArgs 2>&1

    if ($LASTEXITCODE -eq 0) {
        Write-Success "镜像构建成功"
        return $true
    } else {
        Write-Error "镜像构建失败 (exit code: $LASTEXITCODE)"
        $result | Select-Object -Last 20 | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        return $false
    }
}

<#
.SYNOPSIS
    启动 Docker 容器
.DESCRIPTION
    使用 docker compose up -d 启动容器。
.PARAMETER ForceRebuild
    启动前强制重新构建
.EXAMPLE
    Start-DockerContainer
.EXAMPLE
    Start-DockerContainer -ForceRebuild
#>
function Start-DockerContainer {
    [CmdletBinding()]
    param(
        [switch]$ForceRebuild
    )

    Write-Header "启动 Docker 容器"

    if ($ForceRebuild) {
        $buildOk = Build-DockerImage
        if (-not $buildOk) {
            Write-Error "构建失败，取消启动"
            return $false
        }
    }

    $composePath = Resolve-ProjectPath (Get-ComposeFile)
    if (-not (Test-Path $composePath)) {
        Write-Error "docker-compose.yml 不存在: $composePath"
        return $false
    }

    Write-Info "启动容器: $(Get-ContainerName)"
    $result = docker compose -f $composePath up -d 2>&1

    if ($LASTEXITCODE -eq 0) {
        Write-Success "容器已启动"
        Write-Info "等待服务就绪（约 10 秒）..."
        Start-Sleep -Seconds 10
        Get-DockerStatus | Out-Null
        return $true
    } else {
        Write-Error "启动失败: $result"
        return $false
    }
}

<#
.SYNOPSIS
    停止 Docker 容器
.DESCRIPTION
    使用 docker compose down 停止容器。
.PARAMETER RemoveVolumes
    同时删除卷（会丢失数据）
.EXAMPLE
    Stop-DockerContainer
.EXAMPLE
    Stop-DockerContainer -RemoveVolumes
#>
function Stop-DockerContainer {
    [CmdletBinding()]
    param(
        [switch]$RemoveVolumes
    )

    Write-Header "停止 Docker 容器"

    $composePath = Resolve-ProjectPath (Get-ComposeFile)
    if (-not (Test-Path $composePath)) {
        Write-Error "docker-compose.yml 不存在: $composePath"
        return $false
    }

    $downArgs = @("compose", "-f", $composePath, "down")
    if ($RemoveVolumes) {
        $downArgs += "-v"
        Write-Warning "将删除所有卷（数据将丢失）"
    }

    Write-Info "停止容器: $(Get-ContainerName)"
    $result = docker @downArgs 2>&1

    if ($LASTEXITCODE -eq 0) {
        Write-Success "容器已停止"
        return $true
    } else {
        Write-Error "停止失败: $result"
        return $false
    }
}

<#
.SYNOPSIS
    重启 Docker 容器
.DESCRIPTION
    重启正在运行的容器。
.EXAMPLE
    Restart-DockerContainer
#>
function Restart-DockerContainer {
    [CmdletBinding()]
    param()

    Write-Header "重启 Docker 容器"

    $state = Get-ContainerState
    if ($state -ne "running") {
        Write-Warning "容器未在运行，尝试启动..."
        return Start-DockerContainer
    }

    Write-Info "重启容器: $(Get-ContainerName)"
    try {
        docker restart $(Get-ContainerName) 2>&1 | Out-Null

        if ($LASTEXITCODE -eq 0) {
            Write-Success "容器已重启"
            Write-Info "等待服务就绪（约 10 秒）..."
            Start-Sleep -Seconds 10
            Get-DockerStatus | Out-Null
            return $true
        } else {
            Write-Error "重启失败"
            return $false
        }
    } catch {
        Write-Error "重启失败: $_"
        return $false
    }
}

<#
.SYNOPSIS
    进入容器 Shell
.DESCRIPTION
    打开容器内的交互式 Bash Shell。
.EXAMPLE
    Enter-DockerContainer
#>
function Enter-DockerContainer {
    [CmdletBinding()]
    param()

    Write-Header "进入容器 Shell"

    $state = Get-ContainerState
    if ($state -ne "running") {
        Write-Error "容器未在运行"
        return $false
    }

    Write-Info "进入容器: $(Get-ContainerName)"
    Write-Info "输入 exit 退出容器`n"
    docker exec -it $(Get-ContainerName) bash
    return $true
}

<#
.SYNOPSIS
    查看容器日志
.DESCRIPTION
    查看容器的运行日志。
.PARAMETER Tail
    显示最后 N 行（默认 50）
.PARAMETER Follow
    持续跟踪日志输出
.PARAMETER Since
    显示指定时间以来的日志（如 "5m"、"1h"）
.EXAMPLE
    Get-DockerLogs
.EXAMPLE
    Get-DockerLogs -Tail 100 -Follow
#>
function Get-DockerLogs {
    [CmdletBinding()]
    param(
        [int]$Tail = 50,
        [switch]$Follow,
        [string]$Since = ""
    )

    Write-Header "容器日志"

    $logArgs = @("logs")
    if ($Tail -gt 0) {
        $logArgs += "--tail", $Tail
    }
    if ($Follow) {
        $logArgs += "-f"
    }
    if ($Since) {
        $logArgs += "--since", $Since
    }
    $logArgs += (Get-ContainerName)

    try {
        if ($Follow) {
            docker @logArgs
        } else {
            $logs = docker @logArgs 2>&1
            $logs | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
        }
        return $true
    } catch {
        Write-Error "获取日志失败: $_"
        return $false
    }
}

<#
.SYNOPSIS
    查看容器资源占用
.DESCRIPTION
    实时查看容器的 CPU、内存、网络等资源占用。
.EXAMPLE
    Get-DockerStats
#>
function Get-DockerStats {
    [CmdletBinding()]
    param()

    Write-Header "容器资源占用"

    try {
        docker stats $(Get-ContainerName) --no-stream --format "table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.MemPerc}}\t{{.NetIO}}\t{{.BlockIO}}" 2>&1 | ForEach-Object {
            Write-Host "  $_" -ForegroundColor Gray
        }
        return $true
    } catch {
        Write-Error "获取资源占用失败: $_"
        return $false
    }
}

<#
.SYNOPSIS
    运行 Docker 环境诊断
.DESCRIPTION
    委托给 Test-DockerDiagnostic.ps1 运行完整诊断。
.PARAMETER Quick
    快速诊断模式
.EXAMPLE
    Test-DockerEnvironment
.EXAMPLE
    Test-DockerEnvironment -Quick
#>
function Test-DockerEnvironment {
    [CmdletBinding()]
    param(
        [switch]$Quick
    )

    $diagScript = Resolve-ProjectPath "PowerShellScript\Tools\Docker\Test-DockerDiagnostic.ps1"
    if (-not (Test-Path $diagScript)) {
        Write-Error "诊断脚本不存在: $diagScript"
        return $false
    }

    if ($Quick) {
        & $diagScript -Quick
    } else {
        & $diagScript
    }
}

<#
.SYNOPSIS
    运行 MCP 服务验证
.DESCRIPTION
    委托给 Test-McpServices.ps1 验证所有 MCP 服务。
.PARAMETER Quick
    快速验证模式
.EXAMPLE
    Test-McpServices
.EXAMPLE
    Test-McpServices -Quick
#>
function Test-McpServices {
    [CmdletBinding()]
    param(
        [switch]$Quick
    )

    $testScript = Resolve-ProjectPath "PowerShellScript\Tools\Docker\Test-McpServices.ps1"
    if (-not (Test-Path $testScript)) {
        Write-Error "验证脚本不存在: $testScript"
        return $false
    }

    if ($Quick) {
        & $testScript -Quick
    } else {
        & $testScript
    }
}

<#
.SYNOPSIS
    修复 Docker 引擎
.DESCRIPTION
    委托给 Repair-DockerEngine.ps1 修复 WSL2 docker-desktop 问题。
.PARAMETER Force
    跳过确认提示
.EXAMPLE
    Repair-DockerEngine
.EXAMPLE
    Repair-DockerEngine -Force
#>
function Repair-DockerEngine {
    [CmdletBinding()]
    param(
        [switch]$Force
    )

    $repairScript = Resolve-ProjectPath "PowerShellScript\Tools\Docker\Repair-DockerEngine.ps1"
    if (-not (Test-Path $repairScript)) {
        Write-Error "修复脚本不存在: $repairScript"
        return $false
    }

    if ($Force) {
        & $repairScript -Force
    } else {
        & $repairScript
    }
}

<#
.SYNOPSIS
    快速修补 Docker 镜像
.DESCRIPTION
    委托给 Update-DockerImage.ps1 在旧镜像基础上安装包/复制文件。
.PARAMETER ExecCommands
    要在容器内执行的命令数组
.PARAMETER CopyFiles
    要复制到容器内的文件列表
.PARAMETER RestartContainer
    修补完成后重启容器
.EXAMPLE
    Update-DockerImage -ExecCommands @("apt-get update", "apt-get install -y procps")
#>
function Update-DockerImage {
    [CmdletBinding()]
    param(
        [string[]]$ExecCommands = @(),
        [hashtable[]]$CopyFiles = @(),
        [switch]$RestartContainer
    )

    $updateScript = Resolve-ProjectPath "PowerShellScript\Tools\Docker\Update-DockerImage.ps1"
    if (-not (Test-Path $updateScript)) {
        Write-Error "修补脚本不存在: $updateScript"
        return $false
    }

    $params = @{}
    if ($ExecCommands.Count -gt 0) { $params.ExecCommands = $ExecCommands }
    if ($CopyFiles.Count -gt 0) { $params.CopyFiles = $CopyFiles }
    if ($RestartContainer) { $params.RestartContainer = $true }

    & $updateScript @params
}

# ============================================================
# 模块导出
# ============================================================

Export-ModuleMember -Function @(
    "Get-DockerStatus"
    "Build-DockerImage"
    "Start-DockerContainer"
    "Stop-DockerContainer"
    "Restart-DockerContainer"
    "Enter-DockerContainer"
    "Get-DockerLogs"
    "Get-DockerStats"
    "Test-DockerEnvironment"
    "Test-McpServices"
    "Repair-DockerEngine"
    "Update-DockerImage"
)
