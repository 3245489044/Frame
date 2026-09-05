<#
.SYNOPSIS
    Docker 容器重启重置脚本 — 将 workflow-mcp-env 容器恢复到初始状态

.DESCRIPTION
    三级重置方案，仅操作 Docker 容器，不涉及宿主机文件：

    L1 - 软重启（最快）
        docker restart 容器，重启所有 MCP 服务进程，释放内存。
        保留所有数据（mcp-data 命名卷、npm-cache 命名卷、容器文件系统）。
        耗时约 30 秒。

    L2 - 标准重置
        docker compose down + up，重建容器，清除容器内临时文件。
        保留命名卷（mcp-data、npm-cache），知识图谱和 npm 缓存不丢失。
        耗时约 2 分钟。

    L3 - 深度重置
        docker compose down -v + up，删除所有命名卷，完全恢复到镜像初始状态。
        清除 memory.json 等知识图谱数据、npm 缓存。
        可选 --RebuildImage 使用 --no-cache 重建镜像。
        耗时约 3 分钟（含镜像重建约 15 分钟）。

    用法:
        Invoke-DockerReset.ps1                   交互模式，默认 L1 软重启
        Invoke-DockerReset.ps1 -Level L2         标准重置
        Invoke-DockerReset.ps1 -Level L3         深度重置
        Invoke-DockerReset.ps1 -Force            跳过确认
        Invoke-DockerReset.ps1 -Status           仅查看容器状态
        Invoke-DockerReset.ps1 -Level L3 -RebuildImage  深度重置 + 重建镜像
        Invoke-DockerReset.ps1 -Level L3 -BackupPath "D:\Project\Program\WorkFlowPrj\Storage\DockerBackup"  深度重置前备份命名卷

    示例:
        Invoke-DockerReset.ps1
        Invoke-DockerReset.ps1 -Level L2 -Force
        Invoke-DockerReset.ps1 -Level L3 -RebuildImage -Force
        Invoke-DockerReset.ps1 -Level L3 -BackupPath "D:\Project\Program\WorkFlowPrj\Storage\DockerBackup"
        Invoke-DockerReset.ps1 -Level L3 -BackupPath "D:\Project\Program\WorkFlowPrj\Storage\DockerBackup" -RebuildImage


.NOTES
    依赖: BasicMethod/DevOps/Docker.psm1（Get-ContainerState, Invoke-DockerCommand 等）
          BasicMethod/_init.ps1（Write-Header, Write-Success 等输出函数）
    容器: workflow-mcp-env
    端口: 3100-3107（不含 3104）
# @requires Write-Header, Write-Success, Write-Info, Write-SubHeader, Write-Warning, Write-Error
# @requires Confirm-Action, Invoke-Step, Get-ProjectRoot
# @requires Test-ContainerRunning, Get-ContainerState, Get-ContainerUptime
# @requires Invoke-DockerCommand, Test-DockerEngineRunning, Get-DockerVersion
# @requires Test-PortHttpReachable, Wait-ContainerReady
#>

param(
    # 重置级别：L1（软重启）/ L2（标准重置）/ L3（深度重置）
    [ValidateSet("L1", "L2", "L3")]
    [string]$Level = "L1",

    # 跳过确认提示
    [switch]$Force,

    # L3 时使用 --no-cache 重建镜像
    [switch]$RebuildImage,

    # 仅查看容器状态，不执行重置
    [switch]$Status,

    # L3 深度重置前备份命名卷到指定目录（可选，不传则不备份）
    # 备份文件以 {卷名}_YYYYMMDD_HHmmss/ 格式存储
    [string]$BackupPath = ""
)

# ============================================================
# 加载模块
# ============================================================

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$moduleDir = Resolve-Path (Join-Path $scriptDir "..\Modules")
. "$moduleDir\Import-Modules.ps1"

# ============================================================
# 配置常量
# ============================================================

$script:ProjectRoot   = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$script:ContainerName = "workflow-mcp-env"
$script:ImageName     = "workflow-mcp-env:latest"
$script:ComposeDir    = Join-Path $script:ProjectRoot "DevEnvironment\Docker"
$script:ComposeFile   = Join-Path $script:ComposeDir "docker-compose.yml"

# 端口列表（不含已删除的 filesystem 3104）
$script:Ports = @(3100, 3101, 3102, 3103, 3105, 3106, 3107)

# 命名卷列表
$script:NamedVolumes = @("mcp-data", "npm-cache")

# ============================================================
# 辅助函数
# ============================================================

function Get-ContainerImageState {
    <#
    .SYNOPSIS
        检查镜像是否存在
    #>
    param([string]$ImageName)
    $result = docker images -q $ImageName 2>&1
    if ($result) { return "exists" }
    return "not_found"
}

function Get-ContainerUptimeFormatted {
    <#
    .SYNOPSIS
        获取容器运行时间（人类可读格式）
    #>
    param([string]$ContainerName)
    try {
        $startedAt = docker inspect --format "{{.State.StartedAt}}" $ContainerName 2>&1
        if ($LASTEXITCODE -eq 0 -and $startedAt) {
            $startTime = [DateTime]::Parse($startedAt)
            $duration = (Get-Date) - $startTime
            if ($duration.TotalDays -ge 1) {
                return "$([math]::Floor($duration.TotalDays))天 $($duration.Hours)小时 $($duration.Minutes)分钟"
            } elseif ($duration.TotalHours -ge 1) {
                return "$([math]::Floor($duration.TotalHours))小时 $($duration.Minutes)分钟"
            } else {
                return "$([math]::Floor($duration.TotalMinutes))分钟"
            }
        }
    } catch {}
    return "未知"
}

function Get-ContainerMemoryUsage {
    <#
    .SYNOPSIS
        获取容器内存使用情况
    #>
    param([string]$ContainerName)
    try {
        $stats = docker stats $ContainerName --no-stream --format "{{.MemUsage}}" 2>&1
        if ($LASTEXITCODE -eq 0 -and $stats) {
            return $stats.Trim()
        }
    } catch {}
    return "未知"
}

function Wait-ForContainerHealth {
    <#
    .SYNOPSIS
        等待容器健康检查通过，最多等待指定时间
    #>
    param(
        [string]$ContainerName,
        [int]$TimeoutSeconds = 60,
        [string]$Label = "容器就绪"
    )

    Write-Info "等待 $Label（最长 ${TimeoutSeconds}s）..."
    $startTime = Get-Date

    while ($true) {
        $elapsed = [math]::Floor(((Get-Date) - $startTime).TotalSeconds)
        if ($elapsed -ge $TimeoutSeconds) {
            Write-Warning "等待超时（${TimeoutSeconds}s）"
            return $false
        }

        # 检查容器是否运行
        $state = Get-ContainerState -ContainerName $ContainerName
        if ($state -ne "running") {
            Start-Sleep -Seconds 2
            continue
        }

        # 检查所有端口是否就绪
        $allReady = $true
        foreach ($port in $script:Ports) {
            $reachable = Test-PortHttpReachable -Port $port -TimeoutSeconds 2
            if (-not $reachable) {
                $allReady = $false
                break
            }
        }

        if ($allReady) {
            Write-Success "$Label 已就绪（${elapsed}s）"
            return $true
        }

        # 每 3 秒显示一次进度
        if ($elapsed % 6 -eq 0 -and $elapsed -gt 0) {
            Write-Info "  等待中...（${elapsed}s）"
        }
        Start-Sleep -Seconds 3
    }
}

function Show-ContainerStatus {
    <#
    .SYNOPSIS
        显示容器当前状态
    #>
    Write-Header "Docker 容器状态 — workflow-mcp-env"

    # Docker 引擎检查
    $dockerOk = Test-CommandExists "docker"
    if (-not $dockerOk) {
        Write-Error "Docker 不可用"
        return
    }
    $ver = docker --version 2>&1
    Write-Success "Docker 引擎: $ver"

    # 容器状态
    $state = Get-ContainerState -ContainerName $script:ContainerName
    $imgState = Get-ContainerImageState -ImageName $script:ImageName

    $stateLabel = switch ($state) {
        "running"   { "✅ 运行中" }
        "exited"    { "⏹️  已停止" }
        "paused"    { "⏸️  已暂停" }
        "not_found" { "❌ 不存在" }
        default     { "❓ $state" }
    }
    $imgLabel = if ($imgState -eq "exists") { "✅ 存在" } else { "❌ 不存在" }

    Write-Host ""
    Write-Host "  ── 容器信息 ──" -ForegroundColor White
    Write-Info "容器: $($script:ContainerName) ($stateLabel)"
    Write-Info "镜像: $($script:ImageName) ($imgLabel)"

    if ($state -eq "running") {
        $uptime = Get-ContainerUptimeFormatted -ContainerName $script:ContainerName
        $memory = Get-ContainerMemoryUsage -ContainerName $script:ContainerName
        Write-Info "运行时间: $uptime"
        Write-Info "内存使用: $memory"

        # 端口状态
        Write-Host ""
        Write-SubHeader "端口状态"
        foreach ($port in $script:Ports) {
            $reachable = Test-PortHttpReachable -Port $port -TimeoutSeconds 2
            if ($reachable) {
                Write-Success "端口 $port ✅"
            } else {
                Write-Warning "端口 $port ❌"
            }
        }
    }

    # 命名卷状态
    Write-Host ""
    Write-SubHeader "命名卷"
    foreach ($vol in $script:NamedVolumes) {
        $volExists = docker volume ls -q --filter "name=^${vol}$" 2>&1
        if ($volExists) {
            $volInfo = docker volume inspect $vol --format "{{.Driver}} | {{.Mountpoint}}" 2>&1
            Write-Success "$vol ($volInfo)"
        } else {
            Write-Warning "$vol (不存在)"
        }
    }

    # docker-compose 配置
    Write-Host ""
    Write-SubHeader "Compose 配置"
    if (Test-Path $script:ComposeFile) {
        Write-Info "Compose 文件: $($script:ComposeFile)"
    } else {
        Write-Warning "Compose 文件不存在: $($script:ComposeFile)"
    }
}

# ============================================================
# 重置函数
# ============================================================

function Invoke-ResetL1 {
    <#
    .SYNOPSIS
        L1 软重启 — docker restart 容器
    #>
    Write-Step "L1 软重启: docker restart $($script:ContainerName)"

    # 检查容器是否存在
    $state = Get-ContainerState -ContainerName $script:ContainerName
    if ($state -eq "not_found") {
        Write-Error "容器 '$($script:ContainerName)' 不存在，请使用 L2 或 L3 重置"
        return $false
    }

    # 记录重启前的内存使用
    $beforeMem = Get-ContainerMemoryUsage -ContainerName $script:ContainerName
    Write-Info "重启前内存: $beforeMem"

    # 执行重启
    Write-Info "正在重启容器..."
    $output = docker restart $script:ContainerName 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "容器重启失败: $output"
        return $false
    }
    Write-Success "容器已重启"

    # 等待就绪
    $ready = Wait-ForContainerHealth -ContainerName $script:ContainerName -TimeoutSeconds 60 -Label "容器重启"
    if (-not $ready) {
        Write-Error "容器重启后未就绪"
        return $false
    }

    # 记录重启后的内存使用
    $afterMem = Get-ContainerMemoryUsage -ContainerName $script:ContainerName
    Write-Info "重启后内存: $afterMem"

    return $true
}

function Invoke-ResetL2 {
    <#
    .SYNOPSIS
        L2 标准重置 — docker compose down + up（保留命名卷）
    #>
    Write-Step "L2 标准重置: docker compose down + up（保留命名卷）"

    if (-not (Test-Path $script:ComposeFile)) {
        Write-Error "Compose 文件不存在: $script:ComposeFile"
        return $false
    }

    # 停止并删除容器（保留命名卷）
    Push-Location $script:ComposeDir
    try {
        Write-Info "停止并删除容器（保留命名卷）..."
        $downOutput = docker compose down 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "docker compose down 输出: $downOutput"
        }
        Write-Success "容器已停止并删除"

        # 重新创建并启动
        Write-Info "重新创建并启动容器..."
        $upOutput = docker compose up -d 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "容器启动失败: $upOutput"
            return $false
        }
        Write-Success "容器已创建并启动"
    } finally {
        Pop-Location
    }

    # 等待就绪
    $ready = Wait-ForContainerHealth -ContainerName $script:ContainerName -TimeoutSeconds 90 -Label "容器重建"
    if (-not $ready) {
        Write-Error "容器重建后未就绪"
        return $false
    }

    return $true
}

function Backup-NamedVolumes {
    <#
    .SYNOPSIS
        备份容器命名卷到宿主机指定目录
    .DESCRIPTION
        在 L3 深度重置前，通过 docker cp 将命名卷内容备份到宿主机。
        备份目录格式: {BackupPath}/{卷名}_YYYYMMDD_HHmmss/
    #>
    param(
        [string]$BackupPath,
        [string]$ContainerName
    )

    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $backupRoot = Resolve-Path $BackupPath -ErrorAction SilentlyContinue
    if (-not $backupRoot) {
        $backupRoot = $BackupPath
        $null = New-Item -ItemType Directory -Path $backupRoot -Force
    }

    $backupDirs = @()
    $allSuccess = $true

    # 备份 mcp-data（memory.json 知识图谱）
    $volMappings = @(
        @{ Volume = "mcp-data"; ContainerPath = "/opt/mcp-data"; Description = "MCP 知识图谱数据" }
        @{ Volume = "npm-cache"; ContainerPath = "/root/.npm"; Description = "npm 包缓存" }
    )

    foreach ($mapping in $volMappings) {
        $volName = $mapping.Volume
        $containerPath = $mapping.ContainerPath
        $description = $mapping.Description

        # 检查容器内路径是否存在
        $checkResult = docker exec $ContainerName sh -c "test -d '$containerPath' && echo 'EXISTS'" 2>&1
        if ($checkResult -notmatch 'EXISTS') {
            Write-Info "  跳过 $volName（容器内路径 $containerPath 不存在）"
            continue
        }

        $backupDir = Join-Path $backupRoot "${volName}_${timestamp}"
        $null = New-Item -ItemType Directory -Path $backupDir -Force

        Write-Info "  备份 $volName → $backupDir（$description）"

        # 使用 docker cp 复制
        $cpOutput = docker cp "${ContainerName}:${containerPath}/." "$backupDir" 2>&1
        if ($LASTEXITCODE -eq 0) {
            # 统计文件数
            $fileCount = (Get-ChildItem -Path $backupDir -Recurse -File -ErrorAction SilentlyContinue).Count
            Write-Success "    $volName 备份完成（$fileCount 个文件）"
            $backupDirs += @{ Volume = $volName; Path = $backupDir; Files = $fileCount }
        } else {
            Write-Warning "    $volName 备份失败: $cpOutput"
            $allSuccess = $false
        }
    }

    return @{ Success = $allSuccess; BackupDirs = $backupDirs; Root = $backupRoot }
}

function Invoke-ResetL3 {
    <#
    .SYNOPSIS
        L3 深度重置 — docker compose down -v + up（清除所有命名卷）
    #>
    Write-Step "L3 深度重置: docker compose down -v + up（清除所有命名卷）"

    if (-not (Test-Path $script:ComposeFile)) {
        Write-Error "Compose 文件不存在: $script:ComposeFile"
        return $false
    }

    # 备份命名卷（如果指定了 BackupPath）
    $backupResult = $null
    if ($BackupPath) {
        Write-Info "备份命名卷到: $BackupPath"
        $backupResult = Backup-NamedVolumes -BackupPath $BackupPath -ContainerName $script:ContainerName
        if (-not $backupResult.Success) {
            Write-Warning "部分备份失败，继续执行重置..."
        }
    }

    # 可选：重建镜像
    if ($RebuildImage) {
        Write-Info "使用 --no-cache 重建镜像..."
        Push-Location $script:ComposeDir
        try {
            $buildOutput = docker compose build --no-cache 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Error "镜像重建失败"
                Write-Host "  $buildOutput" -ForegroundColor DarkGray
                return $false
            }
            Write-Success "镜像重建完成"
        } finally {
            Pop-Location
        }
    }

    # 停止并删除容器 + 命名卷
    Push-Location $script:ComposeDir
    try {
        Write-Info "停止并删除容器及所有命名卷..."
        $downOutput = docker compose down -v 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "docker compose down -v 输出: $downOutput"
        }
        Write-Success "容器及命名卷已删除"

        # 重新创建并启动
        Write-Info "重新创建并启动容器..."
        $upOutput = docker compose up -d 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "容器启动失败: $upOutput"
            return $false
        }
        Write-Success "容器已创建并启动"
    } finally {
        Pop-Location
    }

    # 等待就绪
    $ready = Wait-ForContainerHealth -ContainerName $script:ContainerName -TimeoutSeconds 120 -Label "容器重建"
    if (-not $ready) {
        Write-Error "容器重建后未就绪"
        return $false
    }

    # 输出备份恢复提示
    if ($backupResult -and $backupResult.BackupDirs.Count -gt 0) {
        Write-Host ""
        Write-SubHeader "备份恢复提示"
        Write-Info "命名卷已备份到: $($backupResult.Root)"
        foreach ($bd in $backupResult.BackupDirs) {
            Write-Info "  $($bd.Volume): $($bd.Path)（$($bd.Files) 个文件）"
        }
        Write-Host ""
        Write-Info "如需恢复数据，请在容器就绪后执行:"
        foreach ($bd in $backupResult.BackupDirs) {
            # 根据卷名确定容器内恢复路径
            $restoreContainerPath = switch ($bd.Volume) {
                "mcp-data"  { "/opt/mcp-data/" }
                "npm-cache" { "/root/.npm/" }
                default     { "/opt/mcp-data/" }
            }
            $restoreCmd = "docker cp `"$($bd.Path)/.`" ${script:ContainerName}:$restoreContainerPath"
            Write-Info "  $restoreCmd"
        }
    }


    return $true
}


# ============================================================
# 主流程
# ============================================================

# --Status：仅查看状态
if ($Status) {
    Show-ContainerStatus
    exit 0
}

# 前置检查
if (-not (Test-CommandExists "docker")) {
    Write-Error "Docker 不可用，请先安装 Docker Desktop"
    exit 1
}

# 显示重置信息
$levelNames = @{
    "L1" = "L1 - 软重启（docker restart，释放内存）"
    "L2" = "L2 - 标准重置（docker compose down + up，保留命名卷）"
    "L3" = "L3 - 深度重置（docker compose down -v + up，清除所有数据）"
}

Write-Header "Invoke-DockerReset — 容器重启重置"
Write-Host "  容器: $($script:ContainerName)" -ForegroundColor Gray
Write-Host "  级别: $($levelNames[$Level])" -ForegroundColor Cyan
if ($RebuildImage) {
    Write-Host "  模式: 重建镜像（--no-cache）" -ForegroundColor Yellow
}
Write-Host ""

# 显示当前状态
$currentState = Get-ContainerState -ContainerName $script:ContainerName
$currentImgState = Get-ContainerImageState -ImageName $script:ImageName
Write-Host "  当前容器状态: $(switch ($currentState) { 'running' { '✅ 运行中' } 'exited' { '⏹️  已停止' } 'not_found' { '❌ 不存在' } default { "❓ $_" } })" -ForegroundColor Gray
Write-Host "  当前镜像状态: $(if ($currentImgState -eq 'exists') { '✅ 存在' } else { '❌ 不存在' })" -ForegroundColor Gray
Write-Host ""

# 确认提示
if (-not $Force) {
    $actionDesc = @()
    $actionDesc += "$($levelNames[$Level])"
    if ($RebuildImage) { $actionDesc += "重建镜像（--no-cache）" }

    $confirmed = Confirm-Action -ActionDescription $actionDesc
    if (-not $confirmed) {
        Write-Info "已取消"
        exit 0
    }
}

# ============================================================
# 执行重置
# ============================================================

$success = $false
switch ($Level) {
    "L1" { $success = Invoke-ResetL1 }
    "L2" { $success = Invoke-ResetL2 }
    "L3" { $success = Invoke-ResetL3 }
}

# ============================================================
# 结果汇总
# ============================================================

Write-Host ""
Write-Header "重置结果汇总"

if ($success) {
    Write-Success "✅ 容器重置完成"
    Write-Info "容器: $($script:ContainerName)"
    Write-Info "级别: $($levelNames[$Level])"

    # 显示最终状态
    $finalMem = Get-ContainerMemoryUsage -ContainerName $script:ContainerName
    Write-Info "当前内存: $finalMem"

    Write-Host ""
    Write-Info "常用命令:"
    Write-Info "  查看日志: docker logs $($script:ContainerName) -f"
    Write-Info "  进入容器: docker exec -it $($script:ContainerName) bash"
    Write-Info "  查看状态: Invoke-DockerReset.ps1 -Status"
} else {
    Write-Error "❌ 容器重置失败，请检查上方错误信息"
    Write-Info "排查步骤:"
    Write-Info "  1. 检查 Docker 引擎是否运行"
    Write-Info "  2. 检查 Compose 文件: $($script:ComposeFile)"
    Write-Info "  3. 查看容器日志: docker logs $($script:ContainerName)"
    exit 1
}
