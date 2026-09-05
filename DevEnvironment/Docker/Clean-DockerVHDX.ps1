<#
.SYNOPSIS
    Docker WSL2 VHDX 虚拟磁盘安全清理与压缩脚本
.DESCRIPTION
    安全清理 Docker 构建缓存、未使用的镜像/容器，并压缩 WSL2 虚拟磁盘文件（VHDX）。
    适用于 Docker Desktop on Windows (WSL2 backend)。

    执行流程:
    1. 检查运行环境（管理员权限、Docker 引擎）
    2. 清理 Docker 内部空间（构建缓存、未使用的镜像/容器）
    3. 停止 Docker Desktop
    4. 等待 WSL2 实例关闭
    5. 压缩 VHDX 虚拟磁盘文件
    6. 重启 Docker Desktop
    7. 显示清理前后大小对比报告

.NOTES
    作者: Auto Generated
    版本: 1.0
    需要: 管理员权限（用于 diskpart 压缩 VHDX）
    路径: D:\DevEnv\Docker\wsl\disk\docker_data.vhdx
#>

# ============================================
# 配置
# ============================================
$VhdxPath = "D:\DevEnv\Docker\wsl\disk\docker_data.vhdx"
$LogFile = Join-Path $PSScriptRoot "Clean-DockerVHDX.log"

# Docker Desktop 可能安装路径列表
$DockerDesktopCandidates = @(
    "$env:ProgramFiles\Docker\Docker\Docker Desktop.exe",
    "${env:ProgramFiles(x86)}\Docker\Docker\Docker Desktop.exe",
    "$env:LOCALAPPDATA\Docker\Docker Desktop.exe"
)

# ============================================
# 日志函数
# ============================================
function Write-Log {
    param([string]$Message, [string]$ForegroundColor = "White")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMsg = "[$timestamp] $Message"
    Write-Host $logMsg -ForegroundColor $ForegroundColor
    Add-Content -Path $LogFile -Value $logMsg
}

function Write-Step {
    param([string]$Message)
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "  $Message" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Log "[STEP] $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Log "[OK] $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Log "[WARN] $Message" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Log "[ERROR] $Message" -ForegroundColor Red
}

# ============================================
# 辅助函数
# ============================================
function Get-FileSizeHuman {
    param([long]$Bytes)
    if ($Bytes -ge 1TB) { return "{0:N2} TB" -f ($Bytes / 1TB) }
    if ($Bytes -ge 1GB) { return "{0:N2} GB" -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return "{0:N2} MB" -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return "{0:N2} KB" -f ($Bytes / 1KB) }
    return "$Bytes B"
}

function Test-AdminPrivilege {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-DockerEngineRunning {
    try {
        $null = docker info --format "{{.ServerVersion}}" 2>&1
        return $LASTEXITCODE -eq 0
    } catch {
        return $false
    }
}

function Get-DockerDiskUsage {
    try {
        $result = docker system df 2>&1
        return $result -join "`n"
    } catch {
        return "无法获取 Docker 磁盘使用信息"
    }
}

function Find-DockerDesktopExe {
    foreach ($path in $DockerDesktopCandidates) {
        if (Test-Path $path) {
            return $path
        }
    }
    return $null
}

function Stop-DockerDesktop {
    Write-Log "正在停止 Docker Desktop..." -ForegroundColor Yellow

    # 1. 先尝试优雅关闭 Docker Desktop
    $ddExe = Find-DockerDesktopExe
    if ($ddExe) {
        Write-Log "找到 Docker Desktop: $ddExe" -ForegroundColor Gray
        try {
            & $ddExe --shutdown 2>&1 | Out-Null
            Start-Sleep -Seconds 5
        } catch {
            Write-Warning "Docker Desktop --shutdown 失败: $_"
        }
    } else {
        Write-Warning "未找到 Docker Desktop 可执行文件，尝试通过 wsl 关闭"
    }

    # 2. 停止 dockerd 进程（仅限 Docker 引擎本身）
    try {
        $dockerPids = @()
        $proc = Get-Process -Name "dockerd" -ErrorAction SilentlyContinue
        if ($proc) { $dockerPids += $proc.Id }
        $proc = Get-Process -Name "com.docker.backend" -ErrorAction SilentlyContinue
        if ($proc) { $dockerPids += $proc.Id }

        if ($dockerPids.Count -gt 0) {
            Write-Log "正在停止 Docker 引擎进程 (PID: $($dockerPids -join ', '))..." -ForegroundColor Yellow
            Stop-Process -Id $dockerPids -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 3
        }
    } catch {
        Write-Warning "停止 Docker 引擎进程时出现异常: $_"
    }

    # 3. 关闭 WSL2 实例以释放 VHDX 文件锁
    Write-Log "正在关闭 WSL2 实例 (wsl --shutdown)..." -ForegroundColor Yellow
    try {
        $wslOutput = wsl --shutdown 2>&1
        Start-Sleep -Seconds 5
        Write-Success "WSL2 实例已关闭"
    } catch {
        Write-Warning "wsl --shutdown 失败: $_"
    }

    # 4. 验证 VHDX 文件是否已释放
    try {
        $fileStream = [System.IO.File]::Open($VhdxPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $fileStream.Close()
        Write-Success "VHDX 文件锁已释放"
    } catch {
        Write-Warning "VHDX 文件仍被占用，尝试强制关闭 WSL..."
        try {
            wsl --shutdown 2>&1 | Out-Null
            Start-Sleep -Seconds 5
            # 再试一次
            $fileStream = [System.IO.File]::Open($VhdxPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
            $fileStream.Close()
            Write-Success "VHDX 文件锁已释放（重试后）"
        } catch {
            Write-Error "VHDX 文件仍被占用，无法安全压缩。请手动关闭 Docker Desktop 后重试。"
            return $false
        }
    }

    Write-Success "Docker Desktop 已停止"
    return $true
}

function Start-DockerDesktop {
    Write-Log "正在启动 Docker Desktop..." -ForegroundColor Yellow
    $ddExe = Find-DockerDesktopExe
    if (-not $ddExe) {
        Write-Warning "未找到 Docker Desktop 可执行文件（已搜索: $($DockerDesktopCandidates -join ', ')）"
        Write-Warning "请手动启动 Docker Desktop"
        return $false
    }
    try {
        Start-Process -FilePath $ddExe -WindowStyle Hidden
        Write-Success "Docker Desktop 启动命令已发送: $ddExe"
        return $true
    } catch {
        Write-Error "启动 Docker Desktop 失败: $_"
        return $false
    }
}

function Wait-DockerEngineReady {
    param([int]$TimeoutSeconds = 120)
    Write-Log "等待 Docker 引擎就绪（最长 ${TimeoutSeconds}秒）..." -ForegroundColor Yellow
    $elapsed = 0
    $interval = 5
    while ($elapsed -lt $TimeoutSeconds) {
        if (Test-DockerEngineRunning) {
            Write-Success "Docker 引擎已就绪"
            return $true
        }
        Start-Sleep -Seconds $interval
        $elapsed += $interval
        Write-Host "  ...等待中 ($elapsed/$TimeoutSeconds 秒)" -ForegroundColor DarkGray
    }
    Write-Error "Docker 引擎未能在 $TimeoutSeconds 秒内就绪"
    return $false
}

# ============================================
# 主流程
# ============================================
function Invoke-CleanDockerVHDX {
    Write-Host @"

╔══════════════════════════════════════════════════╗
║     Docker WSL2 VHDX 安全清理与压缩工具          ║
║     版本 1.0 | 需要管理员权限                     ║
╚══════════════════════════════════════════════════╝

"@ -ForegroundColor Cyan

    # ============================================
    # 阶段 0: 环境检查
    # ============================================
    Write-Step "阶段 0/6: 环境检查"

    # 检查管理员权限
    if (-not (Test-AdminPrivilege)) {
        Write-Error "此脚本需要管理员权限才能压缩 VHDX 文件。"
        Write-Host "请以管理员身份重新运行:右键点击 PowerShell → 以管理员身份运行" -ForegroundColor Yellow
        return $false
    }
    Write-Success "管理员权限检查通过"

    # 检查 VHDX 文件是否存在
    if (-not (Test-Path $VhdxPath)) {
        Write-Error "VHDX 文件不存在: $VhdxPath"
        Write-Host "请确认 Docker WSL2 数据目录位置是否正确。" -ForegroundColor Yellow
        return $false
    }
    $vhdxBefore = (Get-Item $VhdxPath).Length
    Write-Success "VHDX 文件存在: $VhdxPath ($(Get-FileSizeHuman $vhdxBefore))"

    # 检查 Docker 是否可用
    $dockerAvailable = Test-DockerEngineRunning
    if (-not $dockerAvailable) {
        Write-Warning "Docker 引擎当前未运行，将跳过 Docker 内部清理步骤"
    } else {
        Write-Success "Docker 引擎运行中"
    }

    # ============================================
    # 阶段 1: Docker 内部空间清理
    # ============================================
    Write-Step "阶段 1/6: Docker 内部空间清理"

    if ($dockerAvailable) {
        # 显示清理前磁盘使用情况
        Write-Host "`n清理前 Docker 磁盘使用情况:" -ForegroundColor Yellow
        $beforeUsage = Get-DockerDiskUsage
        Write-Host $beforeUsage -ForegroundColor Gray

        # 1a. 清理构建缓存
        Write-Log "清理构建缓存 (docker builder prune)..." -ForegroundColor Yellow
        $builderOutput = docker builder prune -a -f 2>&1
        Write-Host $builderOutput -ForegroundColor Gray
        Write-Success "构建缓存清理完成"

        # 1b. 清理未使用的镜像
        Write-Log "清理未使用的镜像 (docker image prune)..." -ForegroundColor Yellow
        $imageOutput = docker image prune -a -f 2>&1
        Write-Host $imageOutput -ForegroundColor Gray
        Write-Success "未使用镜像清理完成"

        # 1c. 清理未使用的容器、网络
        Write-Log "清理未使用的容器和网络 (docker container prune + docker network prune)..." -ForegroundColor Yellow
        $containerOutput = docker container prune -f 2>&1
        Write-Host $containerOutput -ForegroundColor Gray
        $networkOutput = docker network prune -f 2>&1
        Write-Host $networkOutput -ForegroundColor Gray
        Write-Success "未使用容器和网络清理完成"

        # 1d. 清理未使用的卷（仅清理匿名卷，保留命名卷）
        Write-Log "清理未使用的匿名卷 (docker volume prune)..." -ForegroundColor Yellow
        $volumeOutput = docker volume prune -f 2>&1
        Write-Host $volumeOutput -ForegroundColor Gray
        Write-Success "未使用卷清理完成"
        Write-Host "  （注意: 仅清理未被任何容器引用的匿名卷，命名卷不受影响）" -ForegroundColor DarkGray

        # 显示清理后磁盘使用情况
        Write-Host "`n清理后 Docker 磁盘使用情况:" -ForegroundColor Yellow
        $afterUsage = Get-DockerDiskUsage
        Write-Host $afterUsage -ForegroundColor Gray
    } else {
        Write-Warning "Docker 引擎未运行，跳过 Docker 内部清理"
    }

    # ============================================
    # 阶段 2: 停止 Docker Desktop
    # ============================================
    Write-Step "阶段 2/6: 停止 Docker Desktop"

    if ($dockerAvailable) {
        $stopResult = Stop-DockerDesktop
        if (-not $stopResult) {
            Write-Warning "Docker Desktop 停止可能不完整，继续执行..."
        }
    } else {
        Write-Warning "Docker 引擎未运行，跳过停止步骤"
    }

    # 额外等待确保 WSL 实例释放
    Write-Log "等待 10 秒确保 WSL 实例释放文件锁..." -ForegroundColor Yellow
    Start-Sleep -Seconds 10

    # ============================================
    # 阶段 3: 压缩 VHDX
    # ============================================
    Write-Step "阶段 3/6: 压缩 VHDX 虚拟磁盘"

    Write-Log "正在压缩 VHDX 文件: $VhdxPath" -ForegroundColor Yellow
    Write-Host "  VHDX 当前大小: $(Get-FileSizeHuman $vhdxBefore)" -ForegroundColor Gray
    Write-Host "  这可能需要几分钟时间，请耐心等待..." -ForegroundColor Yellow

    # 生成 diskpart 脚本（使用随机文件名避免冲突）
    $diskpartScript = @"
select vdisk file="$VhdxPath"
attach vdisk readonly
compact vdisk
detach vdisk
exit
"@
    $randomName = [System.IO.Path]::GetRandomFileName() + ".txt"
    $diskpartScriptPath = Join-Path $env:TEMP $randomName
    try {
        $diskpartScript | Set-Content -Path $diskpartScriptPath -Encoding ASCII -Force
        $diskpartOutput = & diskpart /s $diskpartScriptPath 2>&1
        Write-Host $diskpartOutput -ForegroundColor Gray

        # 检查 diskpart 是否成功
        # 注意: diskpart 即使成功也可能返回非零退出码，所以不依赖 $LASTEXITCODE
        # 改为检查输出中是否包含成功关键词（中英文均可）
        $compactSuccess = $diskpartOutput -match "成功" -or $diskpartOutput -match "successfully" -or $diskpartOutput -match "success"
        if ($compactSuccess) {
            Write-Success "VHDX 压缩完成"
        } else {
            Write-Warning "diskpart 返回了非预期结果，请检查上面的输出"
        }
    } catch {
        Write-Error "VHDX 压缩失败: $_"
        Write-Warning "请手动压缩 VHDX: 以管理员身份运行 diskpart，然后执行 compact vdisk 命令"
    } finally {
        # 清理临时文件，不吞掉错误
        if (Test-Path $diskpartScriptPath) {
            Remove-Item -Path $diskpartScriptPath -Force -ErrorAction Stop
        }
    }

    # ============================================
    # 阶段 4: 重启 Docker Desktop
    # ============================================
    Write-Step "阶段 4/6: 重启 Docker Desktop"

    $startResult = Start-DockerDesktop
    if ($startResult) {
        $ready = Wait-DockerEngineReady -TimeoutSeconds 120
        if (-not $ready) {
            Write-Warning "Docker 引擎未能在预期时间内就绪，请稍后手动检查"
        }
    }

    # ============================================
    # 阶段 5: 生成报告
    # ============================================
    Write-Step "阶段 5/6: 清理报告"

    $vhdxAfter = (Get-Item $VhdxPath).Length
    $savedBytes = $vhdxBefore - $vhdxAfter
    $savedPercent = if ($vhdxBefore -gt 0) { [math]::Round(($savedBytes / $vhdxBefore) * 100, 1) } else { 0 }

    Write-Host @"

╔══════════════════════════════════════════════════╗
║                 清理报告                          ║
╠══════════════════════════════════════════════════╣
"@ -ForegroundColor Green

    Write-Host "  VHDX 文件: $VhdxPath" -ForegroundColor White
    Write-Host "  清理前大小: $(Get-FileSizeHuman $vhdxBefore)" -ForegroundColor Yellow
    Write-Host "  清理后大小: $(Get-FileSizeHuman $vhdxAfter)" -ForegroundColor Green
    if ($savedBytes -gt 0) {
        Write-Host "  释放空间:   $(Get-FileSizeHuman $savedBytes) ($savedPercent%)" -ForegroundColor Green
    } else {
        Write-Host "  释放空间:   无变化（VHDX 内部可能已有空闲空间）" -ForegroundColor Yellow
    }

    # 重新检测 Docker 引擎状态（阶段 0 的 $dockerAvailable 可能已过期）
    $dockerRunningNow = Test-DockerEngineRunning
    if ($dockerRunningNow) {
        Write-Host "`n  Docker 当前磁盘使用:" -ForegroundColor White
        $finalUsage = Get-DockerDiskUsage
        Write-Host $finalUsage -ForegroundColor Gray
    } else {
        Write-Host "`n  Docker 引擎未运行，跳过磁盘使用查询" -ForegroundColor Yellow
    }

    Write-Host @"
╚══════════════════════════════════════════════════╝
"@ -ForegroundColor Green

    # ============================================
    # 阶段 6: 后续建议
    # ============================================
    Write-Step "阶段 6/6: 后续建议"

    Write-Host @"
📌 防止 VHDX 再次膨胀的建议:

  1. 定期运行此脚本清理（建议每月一次）
  2. 在 %USERPROFILE%\.wslconfig 中限制 WSL2 磁盘大小:
     [wsl2]
     memory=8GB
     localhostForwarding=true

  3. 构建镜像时使用 --no-cache 避免累积缓存层
  4. 不使用的镜像及时删除: docker image rm <image-name>

📝 日志已保存至: $LogFile
"@ -ForegroundColor Cyan

    return $true
}

# ============================================
# 执行入口
# ============================================
$startTime = Get-Date
Write-Log "========== 脚本开始 ==========" -ForegroundColor Cyan

# 确认提示（支持 -AutoConfirm 参数跳过交互）
$autoConfirm = $args -contains "-AutoConfirm" -or $args -contains "-y"
if (-not $autoConfirm) {
    Write-Host "`n⚠️  此脚本将执行以下操作:" -ForegroundColor Yellow
    Write-Host "  1. 清理 Docker 构建缓存和未使用的镜像/容器" -ForegroundColor White
    Write-Host "  2. 停止 Docker Desktop" -ForegroundColor White
    Write-Host "  3. 压缩 WSL2 VHDX 虚拟磁盘文件" -ForegroundColor White
    Write-Host "  4. 重启 Docker Desktop" -ForegroundColor White
    Write-Host "`n  注意: 压缩 VHDX 期间 Docker 将不可用" -ForegroundColor Red
    Write-Host "  预计耗时: 3-10 分钟（取决于 VHDX 大小）`n" -ForegroundColor Yellow

    $confirm = Read-Host "是否继续? (Y/N)"
    if ($confirm -ne "Y" -and $confirm -ne "y") {
        Write-Host "已取消操作。" -ForegroundColor Red
        exit 0
    }
} else {
    Write-Host "`n⚠️  自动确认模式，跳过交互提示" -ForegroundColor Yellow
}

# 执行主流程
$result = Invoke-CleanDockerVHDX

$endTime = Get-Date
$duration = $endTime - $startTime
Write-Log "========== 脚本结束（耗时: $($duration.Minutes)分 $($duration.Seconds)秒）==========" -ForegroundColor Cyan

if ($result) {
    Write-Host "`n✅ 清理完成！" -ForegroundColor Green
} else {
    Write-Host "`n⚠️  清理过程中出现了一些问题，请检查上面的日志。" -ForegroundColor Yellow
}
