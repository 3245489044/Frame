<#
.SYNOPSIS
    Docker 镜像快速修补工具 — 在旧镜像基础上安装包/复制文件，避免重新构建
.DESCRIPTION
    利用 docker commit 机制，在旧镜像启动的临时容器中执行修补操作，
    然后提交为新镜像。适用于以下场景：
    - 安装额外的系统包（如 apt-get install）
    - 更新配置文件/脚本
    - 安装 npm/pip 包
    - 任何不需要改变基础运行时（Node.js/.NET/Python）的修改

    相比 docker compose build 的优势：
    - 不需要重新下载 Node.js/.NET/Python 等重型依赖
    - 秒级完成（实际只增加一层）
    - 不依赖 Dockerfile 缓存

.PARAMETER ImageName
    要修补的旧镜像名称（默认 workflow-mcp-env:latest）
.PARAMETER TempContainerName
    临时容器名称（默认 temp-mcp-patch）
.PARAMETER ExecCommands
    要在容器内执行的命令数组（按顺序执行）
    示例: @("apt-get update", "apt-get install -y procps")
.PARAMETER CopyFiles
    要复制到容器内的文件列表，格式为 Hashtable 数组：
    @{ Source = "本地路径"; Dest = "容器内路径" }
.PARAMETER NewImageTag
    新镜像标签（默认覆盖旧镜像标签）
.PARAMETER RestartContainer
    修补完成后重启正在运行的容器（开关）
.PARAMETER RunningContainerName
    要重启的运行中容器名称（默认 workflow-mcp-env）
.PARAMETER WhatIf
    仅预览操作，不实际执行

.EXAMPLE
    # 安装 procps 包
    .\Update-DockerImage.ps1 -ExecCommands @("apt-get update", "apt-get install -y procps")

.EXAMPLE
    # 复制脚本文件到镜像
    .\Update-DockerImage.ps1 -CopyFiles @(
        @{ Source = "..\..\DevEnvironment\Docker\scripts\healthcheck.sh"; Dest = "/healthcheck.sh" },
        @{ Source = "..\..\DevEnvironment\Docker\scripts\entrypoint.sh"; Dest = "/entrypoint.sh" }
    )

.EXAMPLE
    # 组合操作：安装包 + 复制文件 + 重启容器
    .\Update-DockerImage.ps1 `
        -ExecCommands @("apt-get update", "apt-get install -y procps") `
        -CopyFiles @{Source="..\..\DevEnvironment\Docker\scripts\healthcheck.sh"; Dest="/healthcheck.sh"} `
        -RestartContainer

.EXAMPLE
    # 预览模式
    .\Update-DockerImage.ps1 -ExecCommands "apt-get install -y procps" -WhatIf
#>

param(
    [string]$ImageName = "",

    [string]$TempContainerName = "temp-mcp-patch",

    [string[]]$ExecCommands = @(),

    [hashtable[]]$CopyFiles = @(),

    [string]$NewImageTag = "",

    [switch]$RestartContainer,

    [string]$RunningContainerName = "",

    [switch]$WhatIf
)

# ============================================================
# 导入共享模块
# ============================================================
$ModulePath = Join-Path $PSScriptRoot "..\..\Modules\Docker\DockerCommon.psm1"
Import-Module $ModulePath -Force -DisableNameChecking

# ============================================================
# 核心函数
# ============================================================

<#
.SYNOPSIS
    启动临时容器

.DESCRIPTION
    用旧镜像启动一个临时容器，保持运行状态以便执行修补操作。
#>
function Start-TempContainer {
    param(
        [string]$ImageName,
        [string]$ContainerName
    )

    Write-Step "启动临时容器"

    # 清理同名的旧容器
    $existing = docker ps -a --filter "name=$ContainerName" --format "{{.ID}}" 2>$null
    if ($existing) {
        Write-Info "清理旧临时容器: $ContainerName"
        docker rm -f $ContainerName 2>$null
    }

    Write-Info "用镜像 $ImageName 启动临时容器..."
    $result = docker run -d --name $ContainerName --entrypoint sleep $ImageName infinity 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "启动临时容器失败: $result"
        return $false
    }

    Write-Success "临时容器已启动: $ContainerName"
    return $true
}

<#
.SYNOPSIS
    在容器内执行命令
.DESCRIPTION
    按顺序在运行中的容器内执行指定的命令。
#>
function Invoke-ContainerCommands {
    param(
        [string]$ContainerName,
        [string[]]$Commands
    )

    if ($Commands.Count -eq 0) {
        return $true
    }

    Write-Step "在容器内执行命令"

    foreach ($cmd in $Commands) {
        Write-Info "执行: $cmd"
        $result = docker exec $ContainerName sh -c $cmd 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "命令执行失败 (exit code: $LASTEXITCODE): $cmd"
            Write-Error "输出: $result"
            return $false
        }
        Write-Success "完成: $cmd"
    }

    return $true
}

<#
.SYNOPSIS
    复制文件到容器
.DESCRIPTION
    将本地文件复制到容器内的指定路径。
#>
function Copy-FilesToContainer {
    param(
        [string]$ContainerName,
        [hashtable[]]$Files
    )

    if ($Files.Count -eq 0) {
        return $true
    }

    Write-Step "复制文件到容器"

    foreach ($file in $Files) {
        $source = $file.Source
        $dest = $file.Dest

        # 解析相对路径（相对于脚本所在目录）
        if (-not [System.IO.Path]::IsPathRooted($source)) {
            $source = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $source))
        }

        if (-not (Test-Path $source)) {
            Write-Error "源文件不存在: $source"
            return $false
        }

        Write-Info "复制: $source → $ContainerName`:$dest"
        $result = docker cp $source "${ContainerName}:${dest}" 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "复制失败: $result"
            return $false
        }

        # 设置可执行权限（如果是脚本文件）
        if ($dest -match '\.(sh|ps1)$') {
            docker exec $ContainerName chmod +x $dest 2>$null
        }

        Write-Success "已复制: $dest"
    }

    return $true
}

<#
.SYNOPSIS
    提交临时容器为新镜像
.DESCRIPTION
    将修补后的临时容器提交为新镜像。
#>
function Commit-NewImage {
    param(
        [string]$ContainerName,
        [string]$NewTag
    )

    Write-Step "提交新镜像"

    $tag = if ($NewTag) { $NewTag } else { $ImageName }
    Write-Info "提交容器 $ContainerName → $tag"

    $result = docker commit $ContainerName $tag 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "提交失败: $result"
        return $false
    }

    Write-Success "新镜像已创建: $tag"
    return $true
}

<#
.SYNOPSIS
    清理临时容器
#>
function Cleanup-TempContainer {
    param([string]$ContainerName)

    Write-Step "清理临时容器"

    $existing = docker ps -a --filter "name=$ContainerName" --format "{{.ID}}" 2>$null
    if ($existing) {
        docker rm -f $ContainerName 2>$null
        Write-Success "临时容器已清理: $ContainerName"
    } else {
        Write-Info "无临时容器需要清理"
    }
}

<#
.SYNOPSIS
    重启运行中的容器
.DESCRIPTION
    如果指定了 -RestartContainer，则重启正在运行的正式容器。
#>
function Restart-RunningContainer {
    param([string]$ContainerName)

    if (-not $RestartContainer) {
        return
    }

    Write-Step "重启容器"

    if (Test-ContainerRunning -ContainerName $ContainerName) {
        Write-Info "重启容器: $ContainerName"
        docker restart $ContainerName 2>$null
        Write-Success "容器已重启: $ContainerName"
    } else {
        Write-Warning "容器未在运行: $ContainerName"
        Write-Info "请手动启动: docker start $ContainerName"
    }
}

<#
.SYNOPSIS
    显示修补摘要
#>
function Show-Summary {
    Write-Step "修补完成" -Color "Green"

    $tag = if ($NewImageTag) { $NewImageTag } else { $ImageName }
    Write-Host "  ✅ 新镜像: $tag" -ForegroundColor Green

    if ($ExecCommands.Count -gt 0) {
        Write-Host "  📦 执行命令:" -ForegroundColor Cyan
        foreach ($cmd in $ExecCommands) {
            Write-Host "     - $cmd" -ForegroundColor Gray
        }
    }

    if ($CopyFiles.Count -gt 0) {
        Write-Host "  📄 复制文件:" -ForegroundColor Cyan
        foreach ($file in $CopyFiles) {
            Write-Host "     - $($file.Source) → $($file.Dest)" -ForegroundColor Gray
        }
    }

    if ($RestartContainer) {
        Write-Host "  🔄 已重启容器: $RunningContainerName" -ForegroundColor Cyan
    }

    Write-Host ""
    Write-Host "  💡 提示: 运行 docker images 查看新镜像" -ForegroundColor Yellow
}

# ============================================================
# 主流程
# ============================================================

function Main {
    Write-Banner

    if ($WhatIf) {
        Write-Host "  🔮 WhatIf 模式 — 仅预览，不实际执行`n" -ForegroundColor Magenta
    }

    # ─── 参数默认值（从配置读取） ───
    if (-not $ImageName) { $ImageName = Get-ImageName }
    if (-not $RunningContainerName) { $RunningContainerName = Get-ContainerName }

    # ─── 检查参数 ───
    if ($ExecCommands.Count -eq 0 -and $CopyFiles.Count -eq 0) {
        Write-Error "请指定至少一个操作：-ExecCommands 或 -CopyFiles"
        Write-Info "示例: .\Update-DockerImage.ps1 -ExecCommands @('apt-get update', 'apt-get install -y procps')"
        exit 1
    }

    # ─── 检查 Docker ───
    if (-not (Test-CommandExists "docker")) {
        Write-Error "Docker 命令不可用，请先安装 Docker Desktop"
        exit 1
    }
    Write-Success "Docker 命令可用"

    # ─── 检查旧镜像 ───
    if (-not (Test-ImageExists -ImageName $ImageName)) {
        Write-Error "旧镜像不存在: $ImageName"
        Write-Info "请先构建基础镜像: docker compose build"
        exit 1
    }
    Write-Success "旧镜像: $ImageName"

    # ─── 预览模式 ───
    if ($WhatIf) {
        Write-Step "预览操作"

        Write-Host "  镜像: $ImageName" -ForegroundColor White
        Write-Host "  临时容器: $TempContainerName" -ForegroundColor White
        $tag = if ($NewImageTag) { $NewImageTag } else { "$ImageName (覆盖)" }
        Write-Host "  新标签: $tag" -ForegroundColor White

        if ($ExecCommands.Count -gt 0) {
            Write-Host "`n  将执行命令:" -ForegroundColor Cyan
            foreach ($cmd in $ExecCommands) {
                Write-Host "    docker exec $TempContainerName sh -c `"$cmd`"" -ForegroundColor Gray
            }
        }

        if ($CopyFiles.Count -gt 0) {
            Write-Host "`n  将复制文件:" -ForegroundColor Cyan
            foreach ($file in $CopyFiles) {
                $source = $file.Source
                if (-not [System.IO.Path]::IsPathRooted($source)) {
                    $source = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $source))
                }
                Write-Host "    docker cp $source ${TempContainerName}:$($file.Dest)" -ForegroundColor Gray
            }
        }

        if ($RestartContainer) {
            Write-Host "`n  将重启容器: $RunningContainerName" -ForegroundColor Cyan
        }

        Write-Host "`n  🔮 WhatIf 模式结束 — 未执行任何实际操作" -ForegroundColor Magenta
        return
    }

    # ─── 阶段 1: 启动临时容器 ───
    $tempOk = Start-TempContainer -ImageName $ImageName -ContainerName $TempContainerName
    if (-not $tempOk) { exit 1 }

    try {
        # ─── 阶段 2: 执行命令 ───
        $cmdOk = Invoke-ContainerCommands -ContainerName $TempContainerName -Commands $ExecCommands
        if (-not $cmdOk) { exit 1 }

        # ─── 阶段 3: 复制文件 ───
        $copyOk = Copy-FilesToContainer -ContainerName $TempContainerName -Files $CopyFiles
        if (-not $copyOk) { exit 1 }

        # ─── 阶段 4: 提交新镜像 ───
        $commitOk = Commit-NewImage -ContainerName $TempContainerName -NewTag $NewImageTag
        if (-not $commitOk) { exit 1 }

    } finally {
        # ─── 阶段 5: 清理临时容器 ───
        Cleanup-TempContainer -ContainerName $TempContainerName
    }

    # ─── 阶段 6: 重启正式容器 ───
    Restart-RunningContainer -ContainerName $RunningContainerName

    # ─── 摘要 ───
    Show-Summary
}

# ============================================================
# 启动
# ============================================================

Main
