# WdUI 测试运行脚本
# 用法: .\RunWdUITests.ps1 [选项]
# 选项:
#   -Report       生成 HTML 测试报告
#   -Category     按类别运行 (Unit, Integration, WdUI, etc.)
#   -TestName     按名称运行
#   -NoBuild      跳过构建

param(
    [switch]$Report,
    [string]$Category = "",
    [string]$TestName = "",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$TestProject = Join-Path $ProjectDir "Share.Tsets\WdUI.Tests\WdUI.Tests.csproj"
$ResultsDir = Join-Path $ProjectDir "TestResults"
$ReportFile = Join-Path $ResultsDir "WdUI_Test_Report.html"
$JUnitFile = Join-Path $ResultsDir "WdUI_Test_Report.xml"

# 确保结果目录存在
if (-not (Test-Path $ResultsDir)) {
    New-Item -ItemType Directory -Path $ResultsDir -Force | Out-Null
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  WdUI 测试运行器" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 构建
if (-not $NoBuild) {
    Write-Host "[1/3] 构建测试项目..." -ForegroundColor Yellow
    dotnet build $TestProject -c Debug --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Host "构建失败!" -ForegroundColor Red
        exit 1
    }
    Write-Host "构建完成" -ForegroundColor Green
    Write-Host ""
}

# 构建测试参数
$dotnetArgs = @("test", $TestProject, "--no-build", "-c", "Debug", "--nologo")
$dotnetArgs += "--settings", (Join-Path (Split-Path $TestProject -Parent) "test.runsettings")
$dotnetArgs += "--results-directory", $ResultsDir

# 日志输出
$dotnetArgs += "-v", "normal"

# 按类别过滤
if ($Category) {
    $dotnetArgs += "--filter", "TestCategory=$Category"
    Write-Host "过滤类别: $Category" -ForegroundColor Yellow
}

# 按名称过滤
if ($TestName) {
    $dotnetArgs += "--filter", "FullyQualifiedName~$TestName"
    Write-Host "过滤名称: $TestName" -ForegroundColor Yellow
}

# 输出格式
if ($Report) {
    $dotnetArgs += "--logger", "trx;LogFileName=WdUI_Test_Results.trx"
}

Write-Host "[2/3] 运行测试..." -ForegroundColor Yellow
Write-Host "命令: dotnet $($dotnetArgs -join ' ')" -ForegroundColor Gray
Write-Host ""

# 计时
$sw = [System.Diagnostics.Stopwatch]::StartNew()

# 运行测试
dotnet $dotnetArgs 2>&1 | ForEach-Object {
    $line = $_.ToString()
    if ($line -match "失败|Failed|Error|ERROR") {
        Write-Host $line -ForegroundColor Red
    } elseif ($line -match "通过|Passed|PASS") {
        Write-Host $line -ForegroundColor Green
    } elseif ($line -match "跳过|Skipped") {
        Write-Host $line -ForegroundColor Yellow
    } else {
        Write-Host $line
    }
}

$sw.Stop()
$exitCode = $LASTEXITCODE

Write-Host ""
Write-Host "[3/3] 完成" -ForegroundColor Yellow
Write-Host "总耗时: $($sw.Elapsed.TotalSeconds.ToString('F1'))s" -ForegroundColor Cyan

if ($exitCode -eq 0) {
    Write-Host "所有测试通过!" -ForegroundColor Green
} else {
    Write-Host "存在失败的测试 (退出码: $exitCode)" -ForegroundColor Red
}

# 查找并显示 TRX 报告路径
$trxFiles = Get-ChildItem -Path $ResultsDir -Filter "*.trx" -Recurse -ErrorAction SilentlyContinue
if ($trxFiles) {
    Write-Host "TRX 报告: $($trxFiles[-1].FullName)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan

exit $exitCode
