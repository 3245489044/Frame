<#
.SYNOPSIS
    Install Framework 一键初始化
.DESCRIPTION
    加载 Install Framework 及其所有子模块。
    使用方式：
        . .\Install\_init.ps1
    或指定项目根目录：
        . .\Install\_init.ps1 -ProjectRoot "D:\Project\Program\WorkFlowPrj"

    注意：建议先加载 PScriptManager（. .\PScriptManager\_init.ps1），
    它会自动加载 Path Framework 和 BasicMethod。
    如果单独加载 Install Framework，也会自动加载 Path Framework。
.NOTES
    版本: 1.2.0
    依赖: PScriptManager（推荐）, Path Framework, BasicMethod
#>

param(
    [string]$ProjectRoot = ""
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# ============================================================
# 加载 Path Framework（如果尚未加载）
# ============================================================

$pathFrameworkLoaded = (Get-Command 'Get-ProjectRoot' -ErrorAction SilentlyContinue) -ne $null
if (-not $pathFrameworkLoaded) {
    $pathInitPath = Join-Path $scriptDir "..\Path\_init.ps1"
    if (Test-Path $pathInitPath) {
        . $pathInitPath
    } else {
        Write-Warning "[Install] Path Framework 未找到，使用回退路径检测"
    }
}

if (-not $ProjectRoot) {
    $ProjectRoot = Get-ProjectRoot
}

# ============================================================
# 加载 Install Core 模块
# ============================================================

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║     Install Framework v1.2.0 — 通用安装框架      ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$coreModules = @(
    'Core\Config.ps1',
    'Core\Executor.ps1',
    'Core\Pipeline.ps1'
)

$loadedCount = 0
foreach ($module in $coreModules) {
    $modulePath = Join-Path $scriptDir $module
    if (Test-Path $modulePath) {
        . $modulePath
        $loadedCount++
        Write-Host "  [OK] $module" -ForegroundColor Green
    } else {
        Write-Warning "[Install] 模块不存在: $modulePath"
    }
}

if ($loadedCount -gt 0) {
    Write-Host "  ✅ Install Framework 核心模块已加载" -ForegroundColor Green
} else {
    Write-Warning "[Install] 未加载任何核心模块"
    return
}

Write-Host "  ✅ Provider 按需加载模式 (通过 Load-Provider)" -ForegroundColor Green

# ============================================================
# 注册框架路径
# ============================================================

Register-FrameworkPath -Name "Install" -Directories @{
    Manifests = "PowerShellScript\Install\Manifests"
    Providers = "PowerShellScript\Install\Providers"
    Core      = "PowerShellScript\Install\Core"
    Commands  = "PowerShellScript\Install\Commands"
} -ProjectRoot $ProjectRoot

$global:InstallFrameworkProjectRoot = $ProjectRoot
Write-Host "  📂 项目根目录: $ProjectRoot" -ForegroundColor Gray

# ============================================================
# 显示可用命令
# ============================================================

Write-Host ""
Write-Host "  可用命令:" -ForegroundColor Yellow
Write-Host "    Install-Tool.ps1         根据清单安装工具" -ForegroundColor White
Write-Host "    Load-InstallManifest     加载安装清单" -ForegroundColor White
Write-Host "    Show-ManifestList        显示可用清单列表" -ForegroundColor White
Write-Host "    Invoke-InstallPipeline   执行安装管道" -ForegroundColor White
Write-Host "    Load-Provider            按需加载 Provider (-Type npm/pip/github-release等)" -ForegroundColor White
Write-Host ""
Write-Host "  通过 PScriptManager 调用:" -ForegroundColor Yellow
Write-Host "    Invoke-Function -Name 'Install-Tool' -Arguments @{Manifest='mcp-servers.json'}" -ForegroundColor DarkGray
Write-Host "    Invoke-Function -Name 'Install-McpServer' -Arguments @{Name='context7-mcp'}" -ForegroundColor DarkGray
Write-Host "    Invoke-Function -Name 'Install-Rust' -Arguments @{DevEnvRoot='D:\DevEnv'}" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  示例:" -ForegroundColor Gray
Write-Host "    # 列出可用清单" -ForegroundColor Gray
Write-Host "    Show-ManifestList" -ForegroundColor DarkGray
Write-Host ""
Write-Host "    # 安装 MCP 服务器（通过 PScriptManager）" -ForegroundColor Gray
Write-Host "    Invoke-Function -Name 'Install-McpServer' -Arguments @{Name='context7-mcp'}" -ForegroundColor DarkGray
Write-Host ""
Write-Host "    # 直接执行脚本（兼容旧方式）" -ForegroundColor Gray
Write-Host "    .\Install\Commands\Install-Tool.ps1 -Manifest mcp-servers.json" -ForegroundColor DarkGray
Write-Host ""
Write-Host "    # 通过 Provider 直接安装" -ForegroundColor Gray
Write-Host "    Invoke-InstallPipeline -Items @(@{name='test';type='npm';source='lodash';global=$true}) -Provider (Get-NpmProvider) -Target local" -ForegroundColor DarkGray
Write-Host ""
