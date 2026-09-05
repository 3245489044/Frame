<#
.SYNOPSIS
    PScriptManager 框架一键初始化
.DESCRIPTION
    加载 PScriptManager 框架及其所有子模块。
    使用方式：
        . .\PScriptManager\_init.ps1
    或指定项目根目录：
        . .\PScriptManager\_init.ps1 -ProjectRoot "D:\Project\Program\WorkFlowPrj"
.NOTES
    版本: 1.0.0
#>

param(
    [string]$ProjectRoot = ""
)

# 解析路径
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ProjectRoot) {
    # 默认项目根目录：.git 哨兵自锚定（路径无关），不硬编码深度
    $__cand = $scriptDir
    while ($__cand -and -not (Test-Path -LiteralPath (Join-Path $__cand '.git'))) { $__cand = Split-Path -Parent $__cand }
    if (-not $__cand) { Write-Warning '[PScriptManager] 无法定位仓库根（.git）'; return }
    $ProjectRoot = $__cand
}

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║     PScriptManager v1.0.0 — 脚本管理框架        ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# 直接 dot-source 所有核心模块（确保函数在当前作用域可用）
$coreModules = @(
    'Core\ScriptScanner.ps1'
    'Core\ConfigGenerator.ps1'
    'Core\ManifestManager.ps1'
    'Core\ScriptRegistry.ps1'
)


$loadedCount = 0
foreach ($module in $coreModules) {
    $modulePath = Join-Path $scriptDir $module
    if (Test-Path $modulePath) {
        . $modulePath
        $loadedCount++
    }
    else {
        Write-Warning "[PScriptManager] 模块不存在: $modulePath"
    }
}

if ($loadedCount -gt 0) {
    Write-Host "  ✅ PScriptManager 框架已加载 ($loadedCount 个核心模块)" -ForegroundColor Green
}
else {
    Write-Warning "[PScriptManager] 未加载任何模块"
    return
}


# 设置全局项目根目录
$global:PScriptManagerProjectRoot = $ProjectRoot
Write-Host "  📂 项目根目录: $ProjectRoot" -ForegroundColor Gray

# ============================================================
# 自动加载注册表
# ============================================================

$manifestPath = Join-Path $scriptDir "manifest.json"
if (Test-Path $manifestPath) {
    $manifest = Get-Content $manifestPath -Encoding UTF8 -Raw | ConvertFrom-Json
    if ($manifest.registry.enabled -and $manifest.registry.autoLoad) {
        $registryFile = Join-Path $scriptDir $manifest.registry.file
        Set-RegistryFilePath -Path $registryFile
        if (Test-Path $registryFile) {
            Load-Registry
        }
        else {
            Write-Host "  ⚠️  注册表文件不存在，请运行 Build-Registry.ps1 构建" -ForegroundColor Yellow
        }
    }
}

# ============================================================
# 自动加载 BasicMethod 模块（按依赖拓扑）
# ============================================================
# 注意：BasicMethod 的加载逻辑已内联至此，不再依赖 BasicMethod/_init.ps1
$basicMethodRoot = Join-Path $ProjectRoot "PowerShellScript\BasicMethod"
if (Test-Path $basicMethodRoot) {
    Write-Host "  🔗 加载 BasicMethod 模块..." -ForegroundColor Cyan

    # 加载顺序（按依赖拓扑排序）
    $bmLoadOrder = @(
        # Layer 0: 零依赖模块（Core 基础）
        @{ Path = "Core\FileUtils.psm1";       Name = "FileUtils" }
        @{ Path = "Core\Output.psm1";          Name = "Output" }
        @{ Path = "Core\Json.psm1";            Name = "Json" }
        @{ Path = "Core\Path.psm1";            Name = "Path" }
        @{ Path = "Core\Utils.psm1";           Name = "Utils" }

        # Layer 1: 零依赖模块（DevOps）
        @{ Path = "DevOps\Docker.psm1";        Name = "Docker" }
        @{ Path = "DevOps\PackageManager.psm1"; Name = "PackageManager" }
        @{ Path = "DevOps\GitClone.psm1";      Name = "GitClone" }
        @{ Path = "DevOps\GitHubRelease.psm1"; Name = "GitHubRelease" }

        # Layer 1: 零依赖模块（Tooling）
        @{ Path = "Tooling\Diagnostic.psm1";   Name = "Diagnostic" }
        @{ Path = "Tooling\ToolBridge.psm1";   Name = "ToolBridge" }

        # Layer 2: 依赖 FileUtils
        @{ Path = "FileOps\FileDiff.psm1";     Name = "FileDiff" }

        # Layer 3: 依赖 FileUtils + FileDiff
        @{ Path = "FileOps\FileAccess.psm1";   Name = "FileAccess" }

        # Layer 4: 依赖 FileUtils + FileDiff + FileAccess
        @{ Path = "FileOps\FileBatch.psm1";    Name = "FileBatch" }

        # 额外：Download 依赖 Output
        @{ Path = "FileOps\Download.psm1";     Name = "Download" }
    )

    $bmLoaded = @()
    $bmFailed = @()

    foreach ($bmModule in $bmLoadOrder) {
        $bmFullPath = Join-Path $basicMethodRoot $bmModule.Path
        if (-not (Test-Path $bmFullPath)) {
            Write-Warning "[PScriptManager] BasicMethod 模块文件不存在，跳过: $($bmModule.Path)"
            $bmFailed += $bmModule.Name
            continue
        }

        try {
            # 使用 Import-Module 确保函数正确注册到模块系统
            Import-Module $bmFullPath -Force -ErrorAction Stop
            $bmLoaded += $bmModule.Name
            Write-Host "  [OK] BasicMethod\$($bmModule.Path)" -ForegroundColor Green
        } catch {
            Write-Warning "[PScriptManager] BasicMethod 模块加载失败: $($bmModule.Path) - $_"
            $bmFailed += $bmModule.Name
        }
    }

    Write-Host "[PScriptManager] BasicMethod 加载完成: $($bmLoaded.Count) 成功, $($bmFailed.Count) 失败" -ForegroundColor Cyan
    if ($bmFailed.Count -gt 0) {
        Write-Warning "[PScriptManager] BasicMethod 失败的模块: $($bmFailed -join ', ')"
    }
    Write-Host "  ✅ BasicMethod 模块已加载" -ForegroundColor Green
}
else {
    Write-Warning "[PScriptManager] BasicMethod 目录未找到: $basicMethodRoot"
}

# ============================================================
# 自动加载 Path Framework（Install Framework 的依赖）
# ============================================================
$pathFrameworkLoaded = (Get-Command 'Register-FrameworkPath' -ErrorAction SilentlyContinue) -ne $null
if (-not $pathFrameworkLoaded) {
    $pathInitPath = Join-Path $ProjectRoot "PowerShellScript\Path\_init.ps1"
    if (Test-Path $pathInitPath) {
        Write-Host "  🔗 加载 Path Framework..." -ForegroundColor Cyan
        . $pathInitPath -ProjectRoot $ProjectRoot
        Write-Host "  ✅ Path Framework 已加载" -ForegroundColor Green
    }
}

# ============================================================
# 自动加载 Install Framework
# ============================================================
$installInitPath = Join-Path $ProjectRoot "PowerShellScript\Install\_init.ps1"
if (Test-Path $installInitPath) {
    Write-Host "  🔗 加载 Install Framework..." -ForegroundColor Cyan
    . $installInitPath -ProjectRoot $ProjectRoot
    Write-Host "  ✅ Install Framework 已加载" -ForegroundColor Green
}

# 显示可用命令
Write-Host ""
Write-Host "  可用命令:" -ForegroundColor Yellow
Write-Host "    Scan-Scripts.ps1       扫描并显示所有脚本" -ForegroundColor White
Write-Host "    Register-Script.ps1    注册脚本到 launcher.config.json" -ForegroundColor White
Write-Host "    Build-Registry.ps1     构建函数注册表（解耦脚本路径）" -ForegroundColor White
Write-Host "    Show-Registry          显示注册表内容" -ForegroundColor White
Write-Host "    Invoke-Function        通过函数名调用脚本（核心功能）" -ForegroundColor White
Write-Host ""
Write-Host "  Install Framework 命令:" -ForegroundColor Yellow
Write-Host "    Install-Tool           根据清单安装工具" -ForegroundColor White
Write-Host "    Install-McpServer      安装 MCP 服务器" -ForegroundColor White
Write-Host "    Install-Rust           安装 Rust 工具链" -ForegroundColor White
Write-Host "    Show-ManifestList      显示可用安装清单" -ForegroundColor White
Write-Host ""
Write-Host "  示例:" -ForegroundColor Gray
Write-Host "    # 扫描 PowerShellScript 目录" -ForegroundColor Gray
Write-Host "    .\PScriptManager\Commands\Scan-Scripts.ps1" -ForegroundColor DarkGray
Write-Host ""
Write-Host "    # 构建函数注册表" -ForegroundColor Gray
Write-Host "    .\PScriptManager\Commands\Build-Registry.ps1" -ForegroundColor DarkGray
Write-Host ""
Write-Host "    # 通过函数名调用（无需知道脚本路径）" -ForegroundColor Gray
Write-Host "    Invoke-Function -Name 'Build-Project' -Arguments @{Configuration='Release'}" -ForegroundColor DarkGray
Write-Host ""
Write-Host "    # 通过 PScriptManager 调用 Install 命令" -ForegroundColor Gray
Write-Host "    Invoke-Function -Name 'Install-McpServer' -Arguments @{Name='context7-mcp'}" -ForegroundColor DarkGray
Write-Host "    Invoke-Function -Name 'Install-Tool' -Arguments @{Manifest='mcp-servers.json'}" -ForegroundColor DarkGray
Write-Host ""
Write-Host "    # 直接调用 BasicMethod 函数（已自动加载）" -ForegroundColor Gray
Write-Host "    Read-File -Path 'README.md'" -ForegroundColor DarkGray
Write-Host "    Search-Files -Path 'CsharpPrj' -Pattern 'class' -FilePattern '*.cs'" -ForegroundColor DarkGray
Write-Host ""
