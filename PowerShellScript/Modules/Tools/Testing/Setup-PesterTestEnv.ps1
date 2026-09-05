<#
.SYNOPSIS
    一键搭建 PowerShell Pester 测试环境
.DESCRIPTION
    安装 NuGet 提供程序、升级 Pester 到 5.x、创建测试目录结构
.NOTES
    需要以管理员身份运行
#>

# 检查管理员权限
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)

if (-not $isAdmin) {
    Write-Host "[ERROR] 请以管理员身份运行此脚本" -ForegroundColor Red
    Write-Host ""
    Write-Host "请右键点击此脚本，选择"以管理员身份运行"" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Pester 测试环境搭建工具" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 步骤 1：安装 NuGet 提供程序
Write-Host "[1/3] 安装 NuGet 提供程序..." -ForegroundColor Yellow
try {
    $nugetProvider = Get-PackageProvider -Name NuGet -ListAvailable -ErrorAction SilentlyContinue
    if (-not $nugetProvider) {
        Install-PackageProvider -Name NuGet -Force -ErrorAction Stop
        Write-Host "[OK] NuGet 提供程序安装成功" -ForegroundColor Green
    } else {
        Write-Host "[OK] NuGet 提供程序已安装（版本: $($nugetProvider.Version)）" -ForegroundColor Green
    }
} catch {
    Write-Host "[ERROR] NuGet 提供程序安装失败: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "请手动安装 NuGet 提供程序：" -ForegroundColor Yellow
    Write-Host "  1. 以管理员身份运行 PowerShell" -ForegroundColor Gray
    Write-Host "  2. 执行: Install-PackageProvider -Name NuGet -Force" -ForegroundColor Gray
    exit 1
}

# 步骤 2：安装/升级 Pester
Write-Host "[2/3] 安装 Pester 5.x..." -ForegroundColor Yellow
try {
    $currentPester = Get-Module -ListAvailable Pester | Sort-Object Version -Descending | Select-Object -First 1
    if ($currentPester -and $currentPester.Version -ge "5.0.0") {
        Write-Host "[OK] Pester $($currentPester.Version) 已安装" -ForegroundColor Green
    } else {
        Write-Host "[INFO] 当前 Pester 版本: $($currentPester.Version)，正在升级到 5.x..." -ForegroundColor Yellow
        Install-Module -Name Pester -Force -SkipPublisherCheck -ErrorAction Stop
        Write-Host "[OK] Pester 升级成功" -ForegroundColor Green
    }
} catch {
    Write-Host "[ERROR] Pester 安装失败: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "可能的原因：" -ForegroundColor Yellow
    Write-Host "  1. 网络问题：无法连接到 PSGallery" -ForegroundColor Gray
    Write-Host "  2. 执行策略限制：请先执行 Set-ExecutionPolicy RemoteSigned -Scope CurrentUser" -ForegroundColor Gray
    Write-Host "  3. PSGallery 未注册：请先执行 Register-PSRepository -Default" -ForegroundColor Gray
    exit 1
}

# 步骤 3：验证安装
Write-Host "[3/3] 验证安装..." -ForegroundColor Yellow
$pesterVersion = (Get-Module -ListAvailable Pester | Sort-Object Version -Descending | Select-Object -First 1).Version
$pesterPath = (Get-Module -ListAvailable Pester | Sort-Object Version -Descending | Select-Object -First 1).Path

Write-Host "[OK] Pester 版本: $pesterVersion" -ForegroundColor Green
Write-Host "[OK] Pester 路径: $pesterPath" -ForegroundColor Green

# 验证关键命令
$requiredCommands = @("Describe", "It", "Should", "Mock", "BeforeAll", "AfterAll")
$missingCommands = @()
foreach ($cmd in $requiredCommands) {
    $found = Get-Command -Name $cmd -Module Pester -ErrorAction SilentlyContinue
    if (-not $found) {
        $missingCommands += $cmd
    }
}

if ($missingCommands.Count -gt 0) {
    Write-Host "[WARN] 以下命令未找到（可能需要导入模块）: $($missingCommands -join ', ')" -ForegroundColor Yellow
    Write-Host "[INFO] 请执行: Import-Module Pester -Force" -ForegroundColor Yellow
} else {
    Write-Host "[OK] 所有关键命令可用" -ForegroundColor Green
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Pester 测试环境搭建完成！" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "运行测试命令:" -ForegroundColor White
Write-Host "  Invoke-Pester -Path 'CentralConsole\FileDriven.Tests\FileDriven\*.Tests.ps1'" -ForegroundColor Gray
Write-Host ""
Write-Host "详细文档:" -ForegroundColor White
Write-Host "  docs\Project\PowerShell测试环境搭建指南.md" -ForegroundColor Gray
Write-Host ""

# 暂停（非交互模式时跳过）
if ($host.Name -eq "ConsoleHost") {
    Read-Host "按回车键退出"
}
