<#
.SYNOPSIS
    查找 FileDriven.Launcher.exe 的输出目录，未找到时自动构建

.DESCRIPTION
    ★ 重构：使用 ProjectPaths 模块的 Get-LauncherOutputDir 统一处理。
    优先查找 Debug 版本，其次 Release 版本。
    如果都未找到，自动执行 dotnet build 构建 Debug 版本。
    输出找到的目录路径到 stdout，未找到时退出码为 1。

    注意：所有日志信息使用 Write-Host 输出到控制台（stderr 流），
    只有最终的目录路径使用 Write-Output 输出到 stdout。
    调用方通过 2>&1 合并流后，使用 Where-Object { $_ -is [string] } 过滤，
    可以正确区分路径输出和日志输出。

.PARAMETER FileDrivenDir
    FileDriven 项目根目录（★ 不再需要，保留仅为兼容旧调用方）

.EXAMPLE
    powershell -File FindLauncherDir.ps1 -FileDrivenDir "D:\Project\Program\WorkFlowPrj\FileDriven"
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$FileDrivenDir = ""
)

# ★ 加载 ProjectPaths 模块（此脚本在独立 PowerShell 进程中运行，需要先加载模块）
$modulePath = Join-Path $PSScriptRoot "..\Core\ProjectPaths.psm1"
if (-not (Get-Module -Name ProjectPaths -ErrorAction SilentlyContinue)) {
    Import-Module $modulePath -Force -DisableNameChecking -ErrorAction Stop
}

# ★ 使用 ProjectPaths 模块查找启动器目录
$launcherDir = Get-LauncherOutputDir

if ($launcherDir) {
    Write-Output $launcherDir
    exit 0
} else {
    Write-Host "[ERROR] 无法获取启动器目录" -ForegroundColor Red
    exit 1
}
