<#
.SYNOPSIS
    C# 测试运行器 —— 一键运行 CentralTest 下所有 MSTest 测试工程，结果直接输出到终端（不写任何日志文件）。

.DESCRIPTION
    遍历 CentralTest 下所有 *.Tests.csproj / *.Test.csproj 测试工程，逐个运行 `dotnet test`
    （quiet 构建 + console minimal logger），在【内存】中捕获子进程输出并解析通过/失败/跳过/总计，
    最后在终端打印紧凑的逐工程结果 + 汇总表。结果不经磁盘日志，直接落到终端。
    退出码：全部通过返回 0；存在失败工程返回 1。

.PARAMETER Project
    按工程名过滤（正则，作用于相对 CentralTest 的工程路径）。默认 ".*" 匹配全部测试工程。

.PARAMETER Configuration
    构建/测试配置，默认 Debug。

.PARAMETER NoBuild
    跳过构建（使用既有二进制；需已构建过，否则测试失败）。

.PARAMETER IncludeConsoleChecks
    额外运行 HybridSchedulerCheck 控制台自检（非 MSTest，输出 ALL PASS 判定）。

.EXAMPLE
    .\Run-CSharpTests.ps1
    .\Run-CSharpTests.ps1 -Project 'Agent\.|StateManagement'
    .\Run-CSharpTests.ps1 -NoBuild -Project 'Agent.Core|Boundary'
    .\Run-CSharpTests.ps1 -IncludeConsoleChecks
#>
[CmdletBinding()]
param(
    [string]$Project = ".*",
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$IncludeConsoleChecks
)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path   # CentralTest
if (-not $root) { $root = Get-Location }

# 收集 C# 测试工程（排除输出/obj/node_modules 与已知非 MSTest 控制台工程）
$projects = Get-ChildItem -Path $root -Recurse -Filter *.csproj -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\BuildOutput\\|\\obj\\|node_modules' } |
    Where-Object { $_.Name -match '\.Tests\.csproj$|\.Test\.csproj$' } |
    Where-Object { $_.Name -notmatch 'HybridSchedulerCheck|LoggingValidator|WdUI' } |
    Where-Object { ($_.FullName.Substring($root.Length + 1)) -match $Project } |
    Sort-Object Name

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  CentralTest C# 测试运行器" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ("  配置: {0}   过滤: {1}   构建: {2}" -f $Configuration, $Project, $(if ($NoBuild) { 'no-build' } else { 'build' })) -ForegroundColor Gray
Write-Host ("  发现测试工程: {0} 个" -f $projects.Count) -ForegroundColor Gray
Write-Host ""

if ($projects.Count -eq 0) {
    Write-Host "  [WARN] 未匹配到测试工程" -ForegroundColor Yellow
    exit 0
}

$totPass = 0; $totFail = 0; $totSkip = 0; $totTotal = 0
$badProjects = @()

foreach ($p in $projects) {
    Write-Host ("  > 运行 {0} ..." -f $p.Name) -ForegroundColor White

    $runArgs = @('test', $p.FullName, '--configuration', $Configuration, '--nologo', '-v', 'q', '--logger', 'console;verbosity=minimal')
    if ($NoBuild) { $runArgs += '--no-build' }

    # 内存捕获（不写日志文件）：合并 stdout/stderr，统一解析
    $out = (& dotnet @runArgs 2>&1 | Out-String)
    $code = $LASTEXITCODE

    # 解析汇总（兼容中文/英文 locale）
    $m = [regex]::Match($out, '失败:\s*(\d+)\s*，通过:\s*(\d+)\s*，已跳过:\s*(\d+)\s*，总计:\s*(\d+)')
    if (-not $m.Success) {
        $m = [regex]::Match($out, 'Failed:\s*(\d+)\s*,\s*Passed:\s*(\d+)\s*,\s*Skipped:\s*(\d+)\s*,\s*Total:\s*(\d+)')
    }

    $f = 0; $pa = 0; $sk = 0; $tt = 0
    if ($m.Success) {
        $f = [int]$m.Groups[1].Value; $pa = [int]$m.Groups[2].Value
        $sk = [int]$m.Groups[3].Value; $tt = [int]$m.Groups[4].Value
        $totFail += $f; $totPass += $pa; $totSkip += $sk; $totTotal += $tt
    }

    if ($code -eq 0) {
        if ($m.Success) {
            Write-Host ("  [PASS] {0,-32} 通过 {1,-5} 失败 {2,-4} 跳过 {3,-4} 总计 {4}" -f $p.Name, $pa, $f, $sk, $tt) -ForegroundColor Green
        } else {
            Write-Host ("  [PASS] {0,-32} (退出码 0，无法解析计数)" -f $p.Name) -ForegroundColor Green
        }
    } else {
        $badProjects += $p.Name
        if ($m.Success) {
            Write-Host ("  [FAIL] {0,-32} 通过 {1,-5} 失败 {2,-4} 跳过 {3,-4} 总计 {4}" -f $p.Name, $pa, $f, $sk, $tt) -ForegroundColor Red
        } else {
            Write-Host ("  [FAIL] {0,-32} (退出码 {1})" -f $p.Name, $code) -ForegroundColor Red
        }
        # 失败时打印输出尾部，便于直接定位，无需翻日志
        ($out -split "`r?`n" | Select-Object -Last 20 | ForEach-Object { "      $_" }) | Write-Host -ForegroundColor DarkGray
    }
    Write-Host ""
}

# 可选：控制台自检（HybridSchedulerCheck）
if ($IncludeConsoleChecks) {
    Write-Host "  > 运行 HybridSchedulerCheck（控制台自检）..." -ForegroundColor White
    $hscExe = Join-Path $root 'BuildOutput\Debug\net10.0-windows\HybridSchedulerCheck.exe'
    $hscOut = ''
    if (Test-Path $hscExe) {
        $hscOut = (& $hscExe 2>&1 | Out-String)
        if ($hscOut -match 'ALL PASS') {
            Write-Host "  [PASS] HybridSchedulerCheck   ALL PASS" -ForegroundColor Green
        } else {
            $badProjects += 'HybridSchedulerCheck'
            Write-Host "  [FAIL] HybridSchedulerCheck" -ForegroundColor Red
            ($hscOut -split "`r?`n" | Select-Object -Last 20 | ForEach-Object { "      $_" }) | Write-Host -ForegroundColor DarkGray
        }
    } else {
        Write-Host "  [WARN] HybridSchedulerCheck.exe 未找到（请先构建）" -ForegroundColor Yellow
    }
    Write-Host ""
}

# ============ 汇总 ============
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ("  汇总: 工程 {0} | 用例通过 {1} | 用例失败 {2} | 跳过 {3} | 用例总计 {4}" -f $projects.Count, $totPass, $totFail, $totSkip, $totTotal) -ForegroundColor White
if ($badProjects.Count -gt 0) {
    Write-Host ("  失败工程: {0}" -f ($badProjects -join ', ')) -ForegroundColor Red
    Write-Host "  存在失败，退出码 1" -ForegroundColor Red
    Write-Host "================================================================" -ForegroundColor Red
    exit 1
} else {
    Write-Host "  全部通过！" -ForegroundColor Green
    Write-Host "================================================================" -ForegroundColor Green
    exit 0
}
