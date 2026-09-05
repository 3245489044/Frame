<#
.SYNOPSIS
    Docker 引擎修复脚本 — 修复 WSL2 docker-desktop 发行版丢失问题
.DESCRIPTION
    针对诊断脚本 Test-DockerDiagnostic.ps1 发现的根因（WSL2 docker-desktop 发行版丢失），
    按以下流程修复：
    1. 检查当前状态
    2. 完全退出 Docker Desktop
    3. 清理过期锁文件
    4. 清理损坏的 WSL 发行版
    5. 重启 Docker Desktop 并等待引擎就绪
    6. 验证修复结果

.PARAMETER Force
    跳过确认提示，直接执行修复
.PARAMETER WhatIf
    仅预览操作，不实际执行

.EXAMPLE
    .\Repair-DockerEngine.ps1
    交互式修复

.EXAMPLE
    .\Repair-DockerEngine.ps1 -Force
    自动修复（无交互）

.EXAMPLE
    .\Repair-DockerEngine.ps1 -WhatIf
    预览修复操作
#>

param(
    [switch]$Force,
    [switch]$WhatIf
)

# ============================================================
# 导入共享模块
# ============================================================
$ModulePath = Join-Path $PSScriptRoot "..\..\Modules\Docker\DockerCommon.psm1"
Import-Module $ModulePath -Force -DisableNameChecking

# ============================================================
# 常量
# ============================================================
$DockerDataDir = "$env:LOCALAPPDATA\Docker"
$DockerDesktopExe = "C:\Program Files\Docker\Docker\Docker Desktop.exe"
$DockerServiceName = "com.docker.service"
$MaxWaitSeconds = 120
$PollIntervalSeconds = 5

# ============================================================
# 修复步骤
# ============================================================


<#
.SYNOPSIS
    步骤 1: 检查当前状态
#>
function Step-CheckEnvironment {
    Write-Step "步骤 1/6: 环境检查"

    # 检查管理员权限
    if (-not (Test-AdminPrivilege)) {
        Write-Warning "建议以管理员身份运行此脚本（部分操作需要管理员权限）"
        if (-not $Force) {
            $continue = Read-Host "  是否继续？(y/N)"
            if ($continue -ne 'y' -and $continue -ne 'Y') { return $false }
        }
    } else {
        Write-Success "管理员权限"
    }

    # 检查 Docker 命令
    if (-not (Test-CommandExists "docker")) {
        Write-Error "Docker 命令不可用"
        return $false
    }
    Write-Success "Docker 命令可用"

    # 检查 Docker Desktop 可执行文件
    if (-not (Test-Path $DockerDesktopExe)) {
        Write-Error "Docker Desktop 未安装（找不到: $DockerDesktopExe）"
        return $false
    }
    Write-Success "Docker Desktop 已安装"

    # 检查 WSL
    if (-not (Test-CommandExists "wsl")) {
        Write-Error "WSL 命令不可用"
        return $false
    }
    Write-Success "WSL 命令可用"

    # 检查当前 WSL 状态
    $wslList = wsl --list --verbose 2>&1
    if ($wslList -match "docker-desktop") {
        Write-Info "当前 WSL docker-desktop 状态:"
        $wslList | Select-String "docker-desktop" | ForEach-Object {
            Write-Host "      $_" -ForegroundColor Gray
        }
    } else {
        Write-Warning "WSL docker-desktop 发行版不存在（这是需要修复的问题）"
    }

    # 检查 Docker 服务状态
    $svc = Get-Service -Name $DockerServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        Write-Info "Docker 服务状态: $($svc.Status)（启动类型: $($svc.StartType)）"
    }

    return $true
}

<#
.SYNOPSIS
    步骤 2: 完全退出 Docker Desktop
#>
function Step-StopDockerDesktop {
    Write-Step "步骤 2/6: 停止 Docker Desktop"

    if ($WhatIf) {
        Write-WhatIf "停止所有 Docker 相关进程"
        Write-WhatIf "停止 Windows 服务: $DockerServiceName"
        return $true
    }

    # 1. 停止 Windows 服务
    $svc = Get-Service -Name $DockerServiceName -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq "Running") {
        Write-Info "停止服务: $DockerServiceName..."
        Stop-Service -Name $DockerServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        $svc = Get-Service -Name $DockerServiceName
        if ($svc.Status -eq "Stopped") {
            Write-Success "服务已停止"
        } else {
            Write-Warning "服务状态: $($svc.Status)"
        }
    } else {
        Write-Info "服务已停止"
    }

    # 2. 停止 Docker Desktop 进程
    $processNames = @(
        "Docker Desktop",
        "com.docker.backend",
        "com.docker.service",
        "com.docker.proxy",
        "com.docker.slirp"
    )
    foreach ($name in $processNames) {
        $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
        if ($procs) {
            Write-Info "停止进程: $name..."
            $procs | Stop-Process -Force -ErrorAction SilentlyContinue
        }
    }
    Start-Sleep -Seconds 3

    # 验证
    $remaining = Get-Process -Name "Docker Desktop", "com.docker*" -ErrorAction SilentlyContinue
    if ($remaining) {
        Write-Warning "部分进程仍在运行: $($remaining.Count) 个"
        $remaining | ForEach-Object { Write-Info "  - $($_.ProcessName) (PID: $($_.Id))" }
    } else {
        Write-Success "所有 Docker 进程已停止"
    }

    return $true
}

<#
.SYNOPSIS
    步骤 3: 清理过期锁文件
#>
function Step-CleanLockFiles {
    Write-Step "步骤 3/6: 清理过期锁文件"

    if ($WhatIf) {
        Write-WhatIf "删除锁文件: $DockerDataDir\*.lock"
        return $true
    }

    $lockFiles = @(
        "$DockerDataDir\backend.lock",
        "$DockerDataDir\frontend.lock",
        "$DockerDataDir\launcher.lock"
    )

    $cleaned = 0
    foreach ($lock in $lockFiles) {
        if (Test-Path $lock) {
            try {
                Remove-Item -Path $lock -Force -ErrorAction Stop
                Write-Success "已删除: $lock"
                $cleaned++
            } catch {
                Write-Warning "删除失败: $lock ($_)"
            }
        }
    }

    if ($cleaned -eq 0) {
        Write-Info "没有需要清理的锁文件"
    } else {
        Write-Success "已清理 $cleaned 个锁文件"
    }

    return $true
}

<#
.SYNOPSIS
    步骤 4: 清理损坏的 WSL 发行版
#>
function Step-CleanWSL {
    Write-Step "步骤 4/6: 清理 WSL 发行版"

    if ($WhatIf) {
        Write-WhatIf "执行: wsl --shutdown"
        Write-WhatIf "执行: wsl --unregister docker-desktop（如果存在）"
        Write-WhatIf "执行: wsl --unregister docker-desktop-data（如果存在）"
        return $true
    }

    # 1. 关闭所有 WSL
    Write-Info "关闭所有 WSL 发行版..."
    $result = wsl --shutdown 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Success "WSL 已关闭"
    } else {
        Write-Warning "wsl --shutdown 结果: $result"
    }
    Start-Sleep -Seconds 2

    # 2. 注销 docker-desktop 发行版
    $wslList = wsl --list --verbose 2>&1
    $distrosToRemove = @("docker-desktop", "docker-desktop-data")

    foreach ($distro in $distrosToRemove) {
        if ($wslList -match $distro) {
            Write-Info "注销 WSL 发行版: $distro..."
            $result = wsl --unregister $distro 2>&1
            if ($LASTEXITCODE -eq 0) {
                Write-Success "已注销: $distro"
            } else {
                Write-Warning "注销失败: $distro ($result)"
            }
        } else {
            Write-Info "WSL 发行版不存在，跳过: $distro"
        }
    }

    # 3. 验证
    $remaining = wsl --list --verbose 2>&1
    if ($remaining -match "docker-desktop") {
        Write-Warning "docker-desktop 发行版仍存在"
    } else {
        Write-Success "WSL 清理完成"
    }

    return $true
}

<#
.SYNOPSIS
    步骤 5: 重启 Docker Desktop 并等待引擎就绪
#>
function Step-RestartDockerDesktop {
    Write-Step "步骤 5/6: 启动 Docker Desktop"

    if ($WhatIf) {
        Write-WhatIf "启动 Docker Desktop: $DockerDesktopExe"
        Write-WhatIf "等待引擎就绪（最长 ${MaxWaitSeconds}秒）"
        return $true
    }

    # 1. 启动 Docker Desktop
    Write-Info "启动 Docker Desktop..."
    try {
        Start-Process -FilePath $DockerDesktopExe -ErrorAction Stop
        Write-Success "Docker Desktop 已启动"
    } catch {
        Write-Error "启动失败: $_"
        return $false
    }

    # 2. 等待引擎就绪
    Write-Info "等待引擎就绪（最长 ${MaxWaitSeconds}秒）..."
    Write-Info "（可以观察系统托盘 Docker 鲸鱼图标是否变稳定）"

    $elapsed = 0
    $engineReady = $false
    $lastStatus = ""

    while ($elapsed -lt $MaxWaitSeconds) {
        Start-Sleep -Seconds $PollIntervalSeconds
        $elapsed += $PollIntervalSeconds

        # 检查命名管道（引擎就绪的标志）
        $pipeExists = [System.IO.Directory]::Exists("\\.\pipe\dockerDesktopLinuxEngine")
        $svcRunning = (Get-Service -Name $DockerServiceName -ErrorAction SilentlyContinue).Status -eq "Running"

        if ($pipeExists -and $svcRunning) {
            # 再试 docker info
            $infoResult = docker info --format "{{.ServerVersion}}" 2>&1
            if ($infoResult -match '\d+\.\d+\.\d+') {
                Write-Success "Docker 引擎就绪！版本: $($infoResult.Trim())"
                $engineReady = $true
                break
            }
        }

        $status = if ($svcRunning) { "服务运行中" } else { "服务未就绪" }
        if ($status -ne $lastStatus) {
            Write-Info "  [${elapsed}s] $status"
            $lastStatus = $status
        }
    }

    if (-not $engineReady) {
        Write-Error "等待超时（${MaxWaitSeconds}秒），引擎未就绪"
        Write-Info "请手动检查:"
        Write-Info "  1. 系统托盘 Docker 图标是否稳定"
        Write-Info "  2. 运行: docker info"
        Write-Info "  3. 查看日志: Get-Content '$DockerDataDir\log\host\com.docker.backend.exe.log' -Tail 20"
        return $false
    }

    return $true
}

<#
.SYNOPSIS
    步骤 6: 验证修复结果
#>
function Step-VerifyRepair {
    Write-Step "步骤 6/6: 验证修复结果"

    if ($WhatIf) {
        Write-WhatIf "运行诊断脚本验证修复结果"
        return $true
    }

    # 1. 检查 WSL 发行版
    Write-Info "检查 WSL 发行版..."
    $wslList = wsl --list --verbose 2>&1
    if ($wslList -match "docker-desktop\s+Running") {
        Write-Success "WSL docker-desktop 运行中"
    } elseif ($wslList -match "docker-desktop") {
        Write-Warning "WSL docker-desktop 存在但未运行"
    } else {
        Write-Error "WSL docker-desktop 仍未创建"
        return $false
    }

    # 2. 检查命名管道
    Write-Info "检查命名管道..."
    $pipeExists = [System.IO.Directory]::Exists("\\.\pipe\dockerDesktopLinuxEngine")
    if ($pipeExists) {
        Write-Success "命名管道已创建"
    } else {
        Write-Error "命名管道未创建"
        return $false
    }

    # 3. 检查 Docker 服务
    Write-Info "检查 Docker 服务..."
    $svc = Get-Service -Name $DockerServiceName -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq "Running") {
        Write-Success "Docker 服务运行中"
    } else {
        Write-Error "Docker 服务未运行"
        return $false
    }

    # 4. 检查 docker info
    Write-Info "检查 docker info..."
    $infoResult = docker info --format "{{.ServerVersion}}" 2>&1
    if ($infoResult -match '\d+\.\d+\.\d+') {
        Write-Success "Docker 引擎版本: $($infoResult.Trim())"
    } else {
        Write-Error "docker info 失败: $infoResult"
        return $false
    }

    # 5. 检查容器
    Write-Info "检查 MCP 容器..."
    $containerName = Get-ContainerName
    $containerResult = docker ps -a --filter "name=$containerName" --format "{{.Names}} {{.Status}}" 2>&1
    if ($containerResult -match [regex]::Escape($containerName)) {
        Write-Success "MCP 容器状态: $containerResult"
    } else {
        Write-Warning "MCP 容器不存在，需要重新构建"
        $composeFile = Get-ConfigValue "container" "composeFile" "DevEnvironment\Docker\docker-compose.yml"
        Write-Info "请运行: docker compose -f $composeFile up -d"
    }

    return $true
}

<#
.SYNOPSIS
    显示修复摘要
#>
function Show-Summary($Success) {
    Write-Step "修复完成" -Color "Green"

    if ($Success) {
        Write-Host "  🎉 Docker 引擎修复成功！" -ForegroundColor Green
        Write-Host ""
        $containerName = Get-ContainerName
        $composeFile = Get-ConfigValue "container" "composeFile" "DevEnvironment\Docker\docker-compose.yml"
        Write-Host "  后续步骤:" -ForegroundColor Cyan
        Write-Host "  1. 检查 MCP 容器状态: docker ps -a --filter name=$containerName" -ForegroundColor Gray
        Write-Host "  2. 如果容器不存在，重新构建: docker compose -f $composeFile up -d" -ForegroundColor Gray
        Write-Host "  3. 运行诊断验证: .\PowerShellScript\Tools\Test-DockerDiagnostic.ps1 -Quick" -ForegroundColor Gray
        Write-Host "  4. 重启 VS Code 让 Cline 重新加载 MCP 配置" -ForegroundColor Gray
    } else {
        Write-Host "  ❌ 修复未完成，请手动排查" -ForegroundColor Red
        Write-Host ""
        Write-Host "  手动排查步骤:" -ForegroundColor Yellow
        Write-Host "  1. 检查 Docker Desktop 系统托盘图标状态" -ForegroundColor Gray
        Write-Host "  2. 查看日志: Get-Content '$DockerDataDir\log\host\com.docker.backend.exe.log' -Tail 30" -ForegroundColor Gray
        Write-Host "  3. 尝试手动启动: net start com.docker.service" -ForegroundColor Gray
        Write-Host "  4. 如果持续失败，考虑重置 Docker Desktop: 设置 → Troubleshoot → Reset to factory defaults" -ForegroundColor Gray
    }
}

# ============================================================
# 主流程
# ============================================================

function Main {
    Write-Banner

    if ($WhatIf) {
        Write-Host "  🔮 WhatIf 模式 — 仅预览，不实际执行`n" -ForegroundColor Magenta
    }

    if (-not $Force -and -not $WhatIf) {
        Write-Warning "此操作将完全停止 Docker Desktop 并清理 WSL 发行版"
        Write-Warning "所有运行中的容器将被停止（但不会删除）"
        Write-Warning "修复完成后 Docker Desktop 会自动重建 WSL 环境"
        Write-Host ""
        $confirm = Read-Host "  确认执行修复？(y/N)"
        if ($confirm -ne 'y' -and $confirm -ne 'Y') {
            Write-Host "  已取消" -ForegroundColor Yellow
            return
        }
    }

    # ─── 步骤 1: 环境检查 ───
    $ok = Step-CheckEnvironment
    if (-not $ok) { Show-Summary $false; return }

    # ─── 步骤 2: 停止 Docker Desktop ───
    $ok = Step-StopDockerDesktop
    if (-not $ok) { Show-Summary $false; return }

    # ─── 步骤 3: 清理锁文件 ───
    $ok = Step-CleanLockFiles
    if (-not $ok) { Show-Summary $false; return }

    # ─── 步骤 4: 清理 WSL ───
    $ok = Step-CleanWSL
    if (-not $ok) { Show-Summary $false; return }

    # ─── 步骤 5: 重启 Docker Desktop ───
    $ok = Step-RestartDockerDesktop
    if (-not $ok) { Show-Summary $false; return }

    # ─── 步骤 6: 验证 ───
    $ok = Step-VerifyRepair

    # ─── 摘要 ───
    Show-Summary $ok
}

# ============================================================
# 启动
# ============================================================

Main
