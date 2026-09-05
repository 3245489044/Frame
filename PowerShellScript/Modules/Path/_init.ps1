<#
.SYNOPSIS
    路径处理框架一键初始化
.DESCRIPTION
    加载 Path 路径处理框架及其所有子模块。
    使用方式：
        . .\Path\_init.ps1
    或指定项目根目录：
        . .\Path\_init.ps1 -ProjectRoot "D:\Project\Program\WorkFlowPrj"
.NOTES
    版本: 1.1.0
    架构:
        Layer 0: RootDetection.ps1       - 零依赖项目根目录检测
        Layer 1: PathConversion.psm1     - 路径转换与归一化
        Layer 1: ScriptContext.psm1      - 脚本目录获取与同级路径解析
        Layer 1: PathEnvManager.psm1     - PATH 环境变量管理
        Layer 2: BuildConfig.ps1         - 构建配置解析
        Layer 2: ProjectPaths.psm1       - 项目子目录映射与别名
        Layer 2: FrameworkPaths.psm1     - 框架目录注册机制
#>

param(
    [string]$ProjectRoot = ""
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║     Path Framework v1.1.0 — 通用路径处理框架     ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$coreModules = @(
    'Core\RootDetection.ps1',
    'Core\PathConversion.psm1',
    'Core\ScriptContext.psm1',
    'Core\PathEnvManager.psm1',
    'Project\BuildConfig.ps1',
    'Project\ProjectPaths.psm1',
    'Project\FrameworkPaths.psm1'
)

$loadedCount = 0
foreach ($module in $coreModules) {
    $modulePath = Join-Path $scriptDir $module
    if (Test-Path $modulePath) {
        $extension = [System.IO.Path]::GetExtension($module)
        if ($extension -eq '.psm1') {
            Import-Module $modulePath -Force -DisableNameChecking -ErrorAction SilentlyContinue
        } else {
            . $modulePath
        }
        $loadedCount++
        Write-Host "  [OK] $module" -ForegroundColor Green
    } else {
        Write-Warning "[Path] 模块不存在: $modulePath"
    }
}

if ($loadedCount -gt 0) {
    Write-Host "  ✅ Path Framework 核心模块已加载" -ForegroundColor Green
} else {
    Write-Warning "[Path] 未加载任何核心模块"
    return
}

if (-not $ProjectRoot) {
    try {
        $ProjectRoot = Get-ProjectRoot
    } catch {
        Write-Warning "[Path] 自动检测项目根目录失败"
    }
}

$global:PathFrameworkProjectRoot = $ProjectRoot
Write-Host "  📂 项目根目录: $ProjectRoot" -ForegroundColor Gray

Write-Host ""
Write-Host "  可用函数:" -ForegroundColor Yellow
Write-Host "    Layer 0 (根目录检测):" -ForegroundColor White
Write-Host "      Get-ProjectRoot           智能检测项目根目录" -ForegroundColor DarkGray
Write-Host "      Reset-ProjectRootCache    清除项目根目录缓存" -ForegroundColor DarkGray
Write-Host ""
Write-Host "    Layer 1 (路径转换与管理):" -ForegroundColor White
Write-Host "      Convert-ContainerPathToHost   容器路径→宿主机路径" -ForegroundColor DarkGray
Write-Host "      Convert-HostPathToContainer   宿主机路径→容器路径" -ForegroundColor DarkGray
Write-Host "      Normalize-Path                智能路径归一化" -ForegroundColor DarkGray
Write-Host "      Test-IsContainerPath          检测容器路径格式" -ForegroundColor DarkGray
Write-Host "      Resolve-ProjectPath           解析项目路径" -ForegroundColor DarkGray
Write-Host "      Get-ScriptDirectory           可靠获取脚本目录" -ForegroundColor DarkGray
Write-Host "      Resolve-SiblingPath           解析同级/上级路径" -ForegroundColor DarkGray
Write-Host "      Set-PathEntries               添加 PATH 条目" -ForegroundColor DarkGray
Write-Host "      Set-EnvironmentVariables      设置环境变量" -ForegroundColor DarkGray
Write-Host ""
Write-Host "    Layer 2 (项目与框架配置):" -ForegroundColor White
Write-Host "      Get-ProjectDirectories        获取项目子目录映射" -ForegroundColor DarkGray
Write-Host "      Get-PathAliases               获取路径别名" -ForegroundColor DarkGray
Write-Host "      Set-AliasEnvironmentFromPaths 注入路径别名到环境变量" -ForegroundColor DarkGray
Write-Host "      Get-BuildOutputPath           获取构建输出目录" -ForegroundColor DarkGray
Write-Host "      Get-TargetFramework           获取目标框架" -ForegroundColor DarkGray
Write-Host "      Get-LauncherOutputDir         获取启动器输出目录" -ForegroundColor DarkGray
Write-Host "      Get-CsharpProjectRoot         获取 C# 项目根目录" -ForegroundColor DarkGray
Write-Host "      Get-FileDrivenDir             获取 FileDriven 目录" -ForegroundColor DarkGray
Write-Host "      Find-LauncherExe              查找启动器 exe" -ForegroundColor DarkGray
Write-Host "      Register-FrameworkPath        注册框架目录映射" -ForegroundColor DarkGray
Write-Host "      Get-FrameworkPath             获取框架目录路径" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  示例:" -ForegroundColor Gray
Write-Host "    # 获取项目根目录" -ForegroundColor Gray
Write-Host "    $root = Get-ProjectRoot" -ForegroundColor DarkGray
Write-Host ""
Write-Host "    # 转换容器路径" -ForegroundColor Gray
Write-Host "    $hostPath = Convert-ContainerPathToHost -Path '/workspace/WorkFlowPrj/test.txt'" -ForegroundColor DarkGray
Write-Host ""
Write-Host "    # 获取子目录映射" -ForegroundColor Gray
Write-Host "    $dirs = Get-ProjectDirectories" -ForegroundColor DarkGray
Write-Host "    Write-Host $($dirs.FileDriven)" -ForegroundColor DarkGray
Write-Host ""