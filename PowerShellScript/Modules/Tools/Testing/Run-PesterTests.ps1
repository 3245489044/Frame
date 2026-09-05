<#
.SYNOPSIS
    Pester 测试运行器（从 BatTool 迁移）

.DESCRIPTION
    运行 PowerShellTests 目录下的所有 Pester 测试。
    替代 BatToolConfig.bat --run-tests 功能。

    用法:
        .\Run-PesterTests.ps1              运行所有测试
        .\Run-PesterTests.ps1 -Category BatTool  仅运行 BatTool 测试
        .\Run-PesterTests.ps1 -List        列出可用的测试分类
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Category = "",

    [switch]$List
)

# ============================================
#  初始化
# ============================================

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = (Get-Item (Join-Path $scriptPath "..\..")).FullName
$testDir = Join-Path $projectRoot "PowerShellTests"

# ============================================
#  核心功能
# ============================================

function Get-TestCategories {
    <#
    .SYNOPSIS
        获取 PowerShellTests 下的所有测试分类
    #>

    if (-not (Test-Path $testDir)) {
        return @()
    }

    $categories = Get-ChildItem -Path $testDir -Directory | ForEach-Object { $_.Name }
    return $categories
}

function Show-TestCategories {
    <#
    .SYNOPSIS
        列出所有可用的测试分类
    #>

    $categories = Get-TestCategories

    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  可用测试分类" -ForegroundColor White
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""

    if ($categories.Count -eq 0) {
        Write-Host "  （未找到测试分类）" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  测试目录: $testDir" -ForegroundColor Gray
        return
    }

    foreach ($cat in $categories) {
        $testFiles = Get-ChildItem -Path (Join-Path $testDir $cat) -Filter "*.Tests.ps1" -ErrorAction SilentlyContinue
        $count = ($testFiles | Measure-Object).Count
        Write-Host "  • $cat ($count 个测试文件)" -ForegroundColor White
    }

    Write-Host ""
    Write-Host "  用法:" -ForegroundColor Gray
    Write-Host "    .\Run-PesterTests.ps1                    运行所有测试" -ForegroundColor Gray
    Write-Host "    .\Run-PesterTests.ps1 -Category BatTool  运行指定分类" -ForegroundColor Gray
}

function Invoke-AllTests {
    <#
    .SYNOPSIS
        运行所有 Pester 测试
    #>

    if (-not (Test-Path $testDir)) {
        Write-Host "[错误] 未找到测试目录: $testDir" -ForegroundColor Red
        return
    }

    Write-Host "[测试] 正在运行所有 Pester 测试..." -ForegroundColor Cyan
    Write-Host ""

    try {
        & pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $testDir "Run-AllTests.ps1")
    }
    catch {
        Write-Host "[错误] 测试执行失败: $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Invoke-CategoryTests {
    <#
    .SYNOPSIS
        运行指定分类的 Pester 测试

    .PARAMETER Category
        测试分类名称
    #>

    param([string]$Category)

    $catPath = Join-Path $testDir $Category

    if (-not (Test-Path $catPath)) {
        Write-Host "[错误] 未找到测试分类: $Category" -ForegroundColor Red
        Write-Host "  预期路径: $catPath" -ForegroundColor Gray
        Write-Host ""
        Write-Host "  使用 -List 参数查看可用分类" -ForegroundColor Yellow
        return
    }

    Write-Host "[测试] 正在运行 $Category 测试..." -ForegroundColor Cyan
    Write-Host ""

    try {
        & pwsh -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester -Path '$catPath\*.Tests.ps1' -Show All"
    }
    catch {
        Write-Host "[错误] 测试执行失败: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# ============================================
#  主入口
# ============================================

if ($List) {
    Show-TestCategories
}
elseif ($Category) {
    Invoke-CategoryTests -Category $Category
}
else {
    Invoke-AllTests
}

Write-Host ""
Write-Host "[测试] 测试完成" -ForegroundColor Cyan
Write-Host ""

if ($host.Name -eq "ConsoleHost") {
    Write-Host "按回车键继续..." -ForegroundColor Gray
    $null = Read-Host
}
