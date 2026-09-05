<#
.SYNOPSIS
    Docker 数据目录迁移工具 — 将 Docker 数据从 C 盘迁移到 D:\DevEnv
.DESCRIPTION
    使用 Docker Desktop 4.32+ 原生支持的 dataFolder 配置，
    通过修改 %APPDATA%\Docker\settings.json 来指定数据目录位置。
    
    原理：Docker Desktop 4.32+ 支持在 Settings > Resources > Advanced >
    Disk image location 中设置数据目录，实际就是修改 settings.json 的 dataFolder 字段。
    
    本脚本自动完成：
    1. 停止 Docker Desktop
    2. 写入 dataFolder 配置到 settings.json
    3. 启动 Docker Desktop（它会自动将数据移动到新位置）
    
.PARAMETER Force
    跳过所有确认提示
.PARAMETER WhatIf
    仅预览操作，不实际执行
.PARAMETER TargetRoot
    目标根目录，默认 D:\DevEnv

.EXAMPLE
    .\Migrate-Docker.ps1
    交互式执行

.EXAMPLE
    .\Migrate-Docker.ps1 -Force
    自动执行，跳过所有确认

.EXAMPLE
    .\Migrate-Docker.ps1 -WhatIf
    预览操作，不实际执行
#>

param(
    [switch]$Force,
    [switch]$WhatIf,
    [string]$TargetRoot = "D:\DevEnv"
)

# ============================================================
# 常量
# ============================================================
$DockerSettingsPath = Join-Path $env:APPDATA "Docker\settings.json"
$DockerWslLocal = Join-Path $env:LOCALAPPDATA "Docker\wsl"
$DockerWslTarget = Join-Path $TargetRoot "Docker\wsl"

# ============================================================
# 辅助函数
# ============================================================

function Write-Banner {
    Write-Host "`n╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║     Docker 数据目录迁移工具                              ║" -ForegroundColor Cyan
    Write-Host "║     使用 Docker Desktop 原生 dataFolder 配置              ║" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""
}

function Write-Step($Title, $Color = "Cyan") {
    Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor $Color
    Write-Host "  $Title" -ForegroundColor $Color
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor $Color
}

function Write-Info($Msg) { Write-Host "  ℹ️  $Msg" -ForegroundColor Gray }
function Write-Success($Msg) { Write-Host "  ✅ $Msg" -ForegroundColor Green }
function Write-Warning($Msg) { Write-Host "  ⚠️  $Msg" -ForegroundColor Yellow }
function Write-Error($Msg) { Write-Host "  ❌ $Msg" -ForegroundColor Red }
function Write-WhatIf($Msg) { Write-Host "  [WhatIf] $Msg" -ForegroundColor Magenta }

function Confirm-Prompt($Prompt, $Default = "n") {
    if ($Force) { return $true }
    $optStr = if ($Default -eq "y") { "Y/n" } else { "y/N" }
    $r = Read-Host "  $Prompt [$optStr]"
    if ([string]::IsNullOrWhiteSpace($r)) { $r = $Default }
    return $r.ToLower() -eq "y"
}

function Get-HumanSize($Bytes) {
    if (-not $Bytes) { return "0 B" }
    if ($Bytes -gt 1GB) { return "{0:N2} GB" -f ($Bytes / 1GB) }
    if ($Bytes -gt 1MB) { return "{0:N2} MB" -f ($Bytes / 1MB) }
    if ($Bytes -gt 1KB) { return "{0:N2} KB" -f ($Bytes / 1KB) }
    return "$Bytes B"
}

function Test-CommandExists($Command) {
    return $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
}

# ============================================================
# 阶段 1: 环境检查
# ============================================================

function Test-Prerequisites {
    Write-Step "阶段 1/5: 环境检查"

    # 检查 Docker Desktop 是否安装
    $dockerPath = Get-Command "docker" -ErrorAction SilentlyContinue
    if (-not $dockerPath) {
        Write-Error "Docker Desktop 未安装"
        exit 1
    }
    Write-Success "Docker Desktop 已安装: $($dockerPath.Source)"

    # 检查 Docker Desktop 版本 (需要 4.32+)
    $ddPath = "C:\Program Files\Docker\Docker\Docker Desktop.exe"
    if (Test-Path $ddPath) {
        $ver = (Get-Item $ddPath).VersionInfo
        $major = $ver.ProductMajorPart
        $minor = $ver.ProductMinorPart
        Write-Success "Docker Desktop 版本: $major.$minor.$($ver.ProductBuildPart)"
        if ($major -lt 4 -or ($major -eq 4 -and $minor -lt 32)) {
            Write-Warning "Docker Desktop 4.32+ 才支持 dataFolder 配置，当前版本可能不支持"
            if (!(Confirm-Prompt "是否继续？" "n")) { exit 0 }
        }
    }

    # 检查目标磁盘空间
    try {
        $drive = [System.IO.Path]::GetPathRoot($TargetRoot)
        $driveLetter = $drive.TrimEnd(':\')
        $driveInfo = Get-PSDrive -Name $driveLetter -ErrorAction Stop
        $freeGB = [math]::Round($driveInfo.Free / 1GB, 1)
        Write-Success "目标磁盘 $drive 剩余空间: ${freeGB}GB"
        if ($freeGB -lt 10) {
            Write-Warning "剩余空间不足 10GB"
            if (!(Confirm-Prompt "是否继续？" "n")) { exit 0 }
        }
    } catch {
        Write-Warning "无法检查磁盘空间"
    }

    # 显示当前数据大小
    Write-Host ""
    Write-Info "当前 Docker 数据（C 盘）:"
    $vhdxFiles = Get-ChildItem $DockerWslLocal -Recurse -Force -Filter "*.vhdx" -ErrorAction SilentlyContinue
    $totalSize = 0
    foreach ($f in $vhdxFiles) {
        $size = $f.Length
        $totalSize += $size
        Write-Host "  📁 $($f.Name): $(Get-HumanSize $size)" -ForegroundColor Gray
    }
    Write-Host "  ─────────────────────" -ForegroundColor Gray
    Write-Host "  总计: $(Get-HumanSize $totalSize)" -ForegroundColor Yellow
}

# ============================================================
# 阶段 2: 停止 Docker Desktop
# ============================================================

function Stop-DockerDesktop {
    Write-Step "阶段 2/5: 停止 Docker Desktop"

    if ($WhatIf) {
        Write-WhatIf "停止 Docker Desktop"
        return
    }

    # 检查是否在运行
    $dockerRunning = $false
    try {
        $info = docker info 2>&1
        if ($LASTEXITCODE -eq 0) { $dockerRunning = $true }
    } catch {}

    if (-not $dockerRunning) {
        Write-Success "Docker Desktop 未运行"
        return
    }

    Write-Info "正在停止 Docker Desktop..."
    try {
        Stop-Process -Name "Docker Desktop" -Force -ErrorAction SilentlyContinue
    } catch {}
    try {
        wsl --terminate docker-desktop 2>&1 | Out-Null
    } catch {}

    Start-Sleep -Seconds 3
    Write-Success "Docker Desktop 已停止"
}

# ============================================================
# 阶段 3: 配置 dataFolder
# ============================================================

function Set-DataFolderConfig {
    Write-Step "阶段 3/5: 配置 dataFolder"

    $targetDataDir = Join-Path $TargetRoot "Docker"

    if ($WhatIf) {
        Write-WhatIf "设置 dataFolder = $targetDataDir"
        Write-WhatIf "配置文件: $DockerSettingsPath"
        return
    }

    # 确保目标目录存在
    if (-not (Test-Path $targetDataDir)) {
        New-Item $targetDataDir -ItemType Directory -Force | Out-Null
        Write-Info "创建目录: $targetDataDir"
    }

    # 读取或创建 settings.json
    $settings = @{}
    if (Test-Path $DockerSettingsPath) {
        try {
            $settings = Get-Content $DockerSettingsPath -Raw | ConvertFrom-Json -ErrorAction Stop
            Write-Info "读取现有配置: $DockerSettingsPath"
        } catch {
            Write-Warning "无法解析现有 settings.json，将创建新配置"
            $settings = @{}
        }
    } else {
        Write-Info "settings.json 不存在，将创建新配置"
    }

    # 设置 dataFolder
    $settings.dataFolder = $targetDataDir

    # 保存
    $settings | ConvertTo-Json -Depth 10 | Set-Content $DockerSettingsPath -Force
    Write-Success "已设置 dataFolder = $targetDataDir"
    Write-Info "配置文件: $DockerSettingsPath"
}

# ============================================================
# 阶段 4: 启动 Docker Desktop
# ============================================================

function Start-DockerDesktop {
    Write-Step "阶段 4/5: 启动 Docker Desktop"

    if ($WhatIf) {
        Write-WhatIf "启动 Docker Desktop"
        return $true
    }

    # 检查是否已在运行
    try {
        $info = docker info 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Docker Desktop 已在运行"
            return $true
        }
    } catch {}

    Write-Info "正在启动 Docker Desktop..."
    Write-Info "Docker Desktop 会自动将数据从 C 盘移动到: $(Join-Path $TargetRoot 'Docker')"

    $dockerExe = "C:\Program Files\Docker\Docker\Docker Desktop.exe"
    if (Test-Path $dockerExe) {
        Start-Process -FilePath $dockerExe
    } else {
        Start-Process "docker://"
    }

    # 等待就绪
    Write-Info "等待 Docker 引擎就绪（首次迁移数据可能需要几分钟）..."
    $maxWait = 300  # 5分钟，因为要移动 8GB 数据
    $waited = 0

    while ($waited -lt $maxWait) {
        Start-Sleep -Seconds 5
        $waited += 5
        try {
            $info = docker info 2>&1
            if ($LASTEXITCODE -eq 0) {
                Write-Success "Docker Desktop 已就绪（${waited}s）"
                return $true
            }
        } catch {}
        if ($waited % 15 -eq 0) {
            Write-Host "  ⏳ 等待中... (${waited}s)" -ForegroundColor Yellow
        }
    }

    Write-Error "Docker Desktop 启动超时"
    Write-Info "请手动启动 Docker Desktop 并检查状态"
    return $false
}

# ============================================================
# 阶段 5: 验证
# ============================================================

function Invoke-Verification {
    Write-Step "阶段 5/5: 验证迁移结果"

    $allOk = $true
    $targetDataDir = Join-Path $TargetRoot "Docker"

    # 检查 dataFolder 配置
    if (Test-Path $DockerSettingsPath) {
        $settings = Get-Content $DockerSettingsPath -Raw | ConvertFrom-Json
        if ($settings.dataFolder -eq $targetDataDir) {
            Write-Success "dataFolder 配置正确: $targetDataDir"
        } else {
            Write-Warning "dataFolder 配置异常: $($settings.dataFolder)"
            $allOk = $false
        }
    }

    # 检查 Docker 引擎
    try {
        $info = docker info 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Docker 引擎运行正常"
            
            # 检查镜像是否还在
            $images = docker images 2>&1
            if ($images -match "workflow-mcp-env") {
                Write-Success "镜像 workflow-mcp-env 存在"
            }
        } else {
            Write-Warning "Docker 引擎未就绪"
            $allOk = $false
        }
    } catch {
        Write-Warning "无法连接 Docker 引擎"
        $allOk = $false
    }

    # 检查 C 盘数据是否已迁移
    $cDriveVhdx = Get-ChildItem $DockerWslLocal -Recurse -Force -Filter "*.vhdx" -ErrorAction SilentlyContinue
    if ($cDriveVhdx) {
        Write-Warning "C 盘仍有 VHDX 文件，可能未完全迁移"
        foreach ($f in $cDriveVhdx) {
            Write-Host "  📁 $($f.Name): $(Get-HumanSize $f.Length)" -ForegroundColor Gray
        }
        $allOk = $false
    } else {
        Write-Success "C 盘 VHDX 文件已迁移"
    }

    # 检查 D 盘数据
    $dVhdx = Get-ChildItem $targetDataDir -Recurse -Force -Filter "*.vhdx" -ErrorAction SilentlyContinue
    if ($dVhdx) {
        Write-Success "D 盘数据文件:"
        foreach ($f in $dVhdx) {
            Write-Host "  📁 $($f.FullName): $(Get-HumanSize $f.Length)" -ForegroundColor Gray
        }
    }

    return $allOk
}

# ============================================================
# 摘要
# ============================================================

function Write-Summary {
    param([bool]$Success)

    Write-Step "迁移完成" -Color "Green"

    if ($Success) {
        Write-Host "  ✅ Docker 数据目录已迁移到: $(Join-Path $TargetRoot 'Docker')" -ForegroundColor Green
        Write-Host ""
        Write-Host "  📂 迁移后目录结构:" -ForegroundColor Cyan
        Write-Host "  $TargetRoot\" -ForegroundColor Gray
        Write-Host "  └── Docker\" -ForegroundColor Gray
        Write-Host "      └── wsl\" -ForegroundColor Gray
        Write-Host "          ├── disk\docker_data.vhdx   ← Docker 镜像/容器/卷" -ForegroundColor Gray
        Write-Host "          └── main\ext4.vhdx          ← Docker 系统数据" -ForegroundColor Gray
        Write-Host ""
        Write-Host "  💾 预计释放 C 盘空间: ~8GB" -ForegroundColor Green
    } else {
        Write-Host "  ❌ 迁移可能未完全成功，请检查上述错误信息" -ForegroundColor Red
        Write-Host "  可尝试手动启动 Docker Desktop 并检查 Settings > Resources > Advanced > Disk image location" -ForegroundColor Yellow
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

    if (-not $WhatIf -and -not $Force) {
        Write-Host "  本工具将自动完成以下操作:" -ForegroundColor White
        Write-Host "  1. 检查环境" -ForegroundColor White
        Write-Host "  2. 停止 Docker Desktop（如运行中）" -ForegroundColor White
        Write-Host "  3. 配置 dataFolder = $(Join-Path $TargetRoot 'Docker')" -ForegroundColor White
        Write-Host "  4. 启动 Docker Desktop（自动迁移数据）" -ForegroundColor White
        Write-Host "  5. 验证迁移结果" -ForegroundColor White
        Write-Host ""
        Write-Host "  ⚠️  注意：Docker Desktop 4.32+ 启动时会自动将数据从" -ForegroundColor Yellow
        Write-Host "      %LOCALAPPDATA%\Docker\wsl 移动到 dataFolder 指定位置" -ForegroundColor Yellow
        Write-Host ""
        if (!(Confirm-Prompt "确认开始？" "n")) { Write-Info "已取消。"; exit 0 }
    }

    # 阶段 1: 环境检查
    Test-Prerequisites

    # 阶段 2: 停止 Docker Desktop
    Stop-DockerDesktop

    # 阶段 3: 配置 dataFolder
    Set-DataFolderConfig

    # 阶段 4: 启动 Docker Desktop
    if (-not $WhatIf) {
        $startOk = Start-DockerDesktop
    } else {
        $startOk = $true
    }

    # 阶段 5: 验证
    if (-not $WhatIf) {
        $verifyResult = Invoke-Verification
    } else {
        $verifyResult = $true
    }

    # 摘要
    Write-Summary -Success (($startOk -and $verifyResult) -or $WhatIf)

    if ($WhatIf) {
        Write-Host "`n  🔮 WhatIf 模式结束 — 未执行任何实际操作" -ForegroundColor Magenta
    }
}

# ============================================================
# 启动
# ============================================================

Main
