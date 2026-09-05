<#
.SYNOPSIS
    PowerShell 测试框架 - 一键运行所有测试

.DESCRIPTION
    运行 PowerShellTests 目录下的所有 Pester 测试，
    并生成测试报告。

.PARAMETER OutputDir
    测试报告输出目录（可选，默认为 PowerShellTests\TestResults）

.PARAMETER NoReport
    不生成 XML 报告文件

.PARAMETER PassThru
    将测试结果对象输出到管道

.EXAMPLE
    .\PowerShellTests\Run-AllTests.ps1

.EXAMPLE
    .\PowerShellTests\Run-AllTests.ps1 -OutputDir "C:\Reports" -PassThru
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$OutputDir = "",

    [Parameter(Mandatory=$false)]
    [switch]$NoReport,

    [Parameter(Mandatory=$false)]
    [switch]$PassThru
)

# ============================================
#  初始化
# ============================================

$scriptName = Split-Path -Leaf $MyInvocation.MyCommand.Path
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-Path "$scriptDir\.."

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  PowerShell 测试框架 - 测试运行器" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 检查 Pester 版本
$pesterModule = Get-Module -ListAvailable Pester | Sort-Object Version -Descending | Select-Object -First 1
if (-not $pesterModule) {
    Write-Host "[ERROR] 未安装 Pester！请先运行 Tools\Setup-PesterTestEnv.ps1" -ForegroundColor Red
    exit 1
}

if ($pesterModule.Version -lt "5.0.0") {
    Write-Host "[ERROR] 需要 Pester 5.x，当前版本: $($pesterModule.Version)" -ForegroundColor Red
    Write-Host "[INFO] 请运行 Tools\Setup-PesterTestEnv.ps1 升级 Pester" -ForegroundColor Yellow
    exit 1
}

Write-Host "[INFO] Pester 版本: $($pesterModule.Version)" -ForegroundColor Green
Write-Host "[INFO] 项目根目录: $projectRoot" -ForegroundColor Gray
Write-Host "[INFO] 测试目录: $scriptDir" -ForegroundColor Gray
Write-Host ""

# ============================================
#  查找测试文件
# ============================================

$testFiles = Get-ChildItem -Path $scriptDir -Filter "*.Tests.ps1" -Recurse
$testCount = @($testFiles).Count

if ($testCount -eq 0) {
    Write-Host "[WARN] 未找到任何测试文件" -ForegroundColor Yellow
    exit 0
}

Write-Host "[INFO] 找到 $testCount 个测试文件:" -ForegroundColor Yellow
foreach ($file in $testFiles) {
    $relativePath = $file.FullName.Substring($scriptDir.Length + 1)
    Write-Host "       - $relativePath" -ForegroundColor Gray
}
Write-Host ""

# ============================================
#  运行测试
# ============================================

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  开始运行测试..." -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 配置 Pester 运行参数
$pesterConfig = New-PesterConfiguration
$pesterConfig.Run.Path = $testFiles.FullName
$pesterConfig.Run.PassThru = $true
$pesterConfig.TestResult.Enabled = $true

# 设置输出目录
if (-not $OutputDir) {
    $OutputDir = Join-Path $scriptDir "TestResults"
}
if (-not (Test-Path $OutputDir)) {
    New-Item -Path $OutputDir -ItemType Directory -Force | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$outputFile = Join-Path $OutputDir "TestResults_$timestamp.xml"
$pesterConfig.TestResult.OutputPath = $outputFile
$pesterConfig.TestResult.OutputFormat = "NUnitXml"

# 运行测试
$results = Invoke-Pester -Configuration $pesterConfig

# ============================================
#  显示结果摘要
# ============================================

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  测试结果摘要" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$totalCount = $results.TotalCount
$passedCount = $results.PassedCount
$failedCount = $results.FailedCount
$skippedCount = $results.SkippedCount
$duration = $results.Duration

Write-Host "  总计: $totalCount" -ForegroundColor White
Write-Host "  通过: $passedCount" -ForegroundColor Green
if ($failedCount -gt 0) {
    Write-Host "  失败: $failedCount" -ForegroundColor Red
} else {
    Write-Host "  失败: $failedCount" -ForegroundColor Green
}
if ($skippedCount -gt 0) {
    Write-Host "  跳过: $skippedCount" -ForegroundColor Yellow
}
Write-Host ("  耗时: {0:N2} 秒" -f $duration.TotalSeconds) -ForegroundColor Gray
Write-Host ""

# 显示失败的测试详情
if ($failedCount -gt 0) {
    Write-Host "  --------------------------------" -ForegroundColor Red
    Write-Host "  失败的测试:" -ForegroundColor Red
    Write-Host "  --------------------------------" -ForegroundColor Red
    Write-Host ""

    foreach ($file in $results.Files) {
        foreach ($block in $file.Blocks) {
            foreach ($test in $block.Tests) {
                if ($test.Result -eq "Failed") {
                    Write-Host "  [失败] $($test.ExpandedPath)" -ForegroundColor Red
                    Write-Host "         错误: $($test.ErrorRecord.Message)" -ForegroundColor Gray
                    Write-Host ""
                }
            }
        }
    }
}

# 显示报告路径
if (-not $NoReport) {
    Write-Host "[INFO] 测试报告已保存: $outputFile" -ForegroundColor Gray
}
Write-Host ""

# 输出最终状态
if ($failedCount -eq 0) {
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  所有测试通过！" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
} else {
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  存在 $failedCount 个失败的测试" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
}

Write-Host ""

# 返回测试结果
if ($PassThru) {
    return $results
}

# 以退出码返回结果
if ($failedCount -gt 0) {
    exit 1
}
exit 0
