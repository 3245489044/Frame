<#
.SYNOPSIS
    BasicMethod 测试公共辅助模块
.DESCRIPTION
    提供统一的模块加载、测试辅助函数、测试环境管理，消除各测试文件间的重复代码。
    被 Test-BatchRead.ps1, Test-BatchWrite.ps1, Test-BatchReplace.ps1 共用。
#>

# ============================================
# 模块顶层路径缓存（函数内 $PSScriptRoot 为空）
# ============================================

# 计算项目根目录：TestHelper.psm1 位于 CentralTest/PowerShellTests/BasicMethod.Tests/
$_moduleDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$_testDir = Split-Path -Parent $_moduleDir
$_projectRoot = Resolve-Path (Join-Path $_testDir "..\..")
$_basicMethodModulePath = Join-Path $_projectRoot "PowerShellScript\BasicMethod"

# ============================================
# 模块加载
# ============================================

function Import-BasicMethodModules {
    <#
    .SYNOPSIS
        统一加载 BasicMethod 的三个核心模块
    .PARAMETER Force
        强制重新加载（默认 $true）
    #>
    param(
        [switch]$Force = $true
    )

    $modules = @(
        "BasicMethod-FileUtils.psm1",
        "BasicMethod-FileDiff.psm1",
        "BasicMethod-FileBatch.psm1"
    )

    if ($Force) {
        $modules | ForEach-Object {
            $name = $_ -replace '\.psm1$'
            Remove-Module $name -Force -ErrorAction SilentlyContinue
        }
    }

    $modules | ForEach-Object {
        $fullPath = Join-Path $_basicMethodModulePath $_
        Import-Module $fullPath -Force -Global -ErrorAction Stop
    }
}

# ============================================
# 测试辅助函数
# ============================================

function Write-TestHeader {
    <#
    .SYNOPSIS
        输出测试节标题
    #>
    param([string]$Title)
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
}

function Write-TestResult {
    <#
    .SYNOPSIS
        输出测试结果（PASS/FAIL）
    #>
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Detail = ""
    )
    $icon = if ($Passed) { "[PASS]" } else { "[FAIL]" }
    $color = if ($Passed) { "Green" } else { "Red" }
    Write-Host "$icon $TestName" -ForegroundColor $color
    if ($Detail) {
        Write-Host "       $Detail" -ForegroundColor DarkGray
    }
}

function Test-Case {
    <#
    .SYNOPSIS
        统一测试用例包装器
    .DESCRIPTION
        执行测试脚本块，自动捕获异常并输出 PASS/FAIL
    #>
    param(
        [string]$Name,
        [scriptblock]$ScriptBlock
    )
    Write-Host "=== $Name ===" -ForegroundColor Cyan
    try {
        & $ScriptBlock
        Write-Host "[PASS] $Name" -ForegroundColor Green
        return $true
    } catch {
        Write-Host "[FAIL] $Name : $_" -ForegroundColor Red
        return $false
    }
    Write-Host ""
}

# ============================================
# 测试环境管理
# ============================================

function Get-TestTempDir {
    <#
    .SYNOPSIS
        获取测试临时目录路径
    #>
    return Join-Path $_projectRoot "CentralTest\PowerShellTests\TestTemp"
}

function Initialize-TestEnvironment {
    <#
    .SYNOPSIS
        创建测试临时目录和初始文件
    .PARAMETER FileNames
        要创建的初始测试文件名列表（默认 file1.txt, file2.txt, file3.txt）
    .PARAMETER MergeFileNames
        用于 Merge 测试的文件名列表（默认 merge1.txt, merge2.txt）
    #>
    param(
        [string[]]$FileNames = @("file1.txt", "file2.txt", "file3.txt"),
        [string[]]$MergeFileNames = @("merge1.txt", "merge2.txt")
    )

    $testTempDir = Get-TestTempDir

    if (Test-Path $testTempDir) {
        Remove-Item -Path $testTempDir -Recurse -Force
    }
    New-Item -Path $testTempDir -ItemType Directory -Force | Out-Null

    # 创建基本测试文件
    $FileNames | ForEach-Object {
        $path = Join-Path $testTempDir $_
        Set-Content -Path $path -Value "原始内容 - $_" -Encoding UTF8
    }

    # 创建 Merge 测试文件
    $mergeContent = @"
# 配置文件
SettingName = "default"
Timeout = 30
DebugMode = false
"@
    $MergeFileNames | ForEach-Object {
        $path = Join-Path $testTempDir $_
        Set-Content -Path $path -Value $mergeContent -Encoding UTF8
    }

    Write-Host "[初始化] 测试环境已准备: $testTempDir" -ForegroundColor DarkGray
    return $testTempDir
}

function Cleanup-TestEnvironment {
    <#
    .SYNOPSIS
        清理测试临时目录
    #>
    $testTempDir = Get-TestTempDir
    if (Test-Path $testTempDir) {
        Remove-Item -Path $testTempDir -Recurse -Force
        Write-Host "[清理] 测试临时目录已删除" -ForegroundColor DarkGray
    }
}

function New-TestFilePath {
    <#
    .SYNOPSIS
        获取测试临时目录中指定文件的完整路径
    #>
    param([string]$FileName)
    return Join-Path (Get-TestTempDir) $FileName
}

# ============================================
# 测试汇总输出
# ============================================

function Write-TestSummary {
    <#
    .SYNOPSIS
        输出测试汇总信息
    #>
    param(
        [int]$PassedCount,
        [int]$FailedCount,
        [int]$TotalCount
    )

    Write-Host "`n========================================" -ForegroundColor Magenta
    Write-Host "  测试完成" -ForegroundColor Magenta
    Write-Host "  通过: $PassedCount / $TotalCount" -ForegroundColor Green
    if ($FailedCount -gt 0) {
        Write-Host "  失败: $FailedCount" -ForegroundColor Red
    }
    Write-Host "========================================" -ForegroundColor Magenta
}

Export-ModuleMember -Function Import-BasicMethodModules, Write-TestHeader, Write-TestResult, Test-Case, Get-TestTempDir, Initialize-TestEnvironment, Cleanup-TestEnvironment, New-TestFilePath, Write-TestSummary
