<#
.SYNOPSIS
    dotnet test 执行面（通用）：参数构造 / 结果计数解析 / 失败名提取 / 计划与结果渲染 / 汇总退出码。

.DESCRIPTION
    CentralTest 通用测试运行器框架的**判据无关**部分（单源 = 本模块）：
    原实现位于 `Environment\CentralTest\TestRunner\TestRunner.psm1`，本模块为抽取目标（N1/M1，函数体原样搬移）。
    专属部分（工程发现 / 目标解析 / spec 套件语义 / WdUI 启动器）仍留 CentralTest 侧。

    依赖方向（硬约束）：CentralTest → Frame（单向）；本模块零 CentralTest 引用。
#>

Set-StrictMode -Version Latest

function New-DotnetTestArgs {
    <#
    .SYNOPSIS
        组装 dotnet test 参数（filter 之间为 AND，filter 串内部为 OR）。

    .PARAMETER Filters
        过滤表达式的合取项，如 @('(FullyQualifiedName~A|FullyQualifiedName~B)')。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [string[]]$Filters = @(),
        [string]$Configuration = 'Debug',
        [switch]$NoBuild,
        [string]$RunSettings = '',
        [string]$ResultsDir = '',
        [string[]]$Logger = @('console;verbosity=minimal'),
        [string[]]$ExtraArgs = @()
    )

    $testArgs = @('test', $ProjectPath, '--configuration', $Configuration, '--nologo', '-v', 'q')
    foreach ($log in @($Logger)) { if ($log) { $testArgs += @('--logger', $log) } }
    if ($NoBuild) { $testArgs += '--no-build' }
    if ($RunSettings) { $testArgs += @('--settings', $RunSettings) }
    if ($ResultsDir) { $testArgs += @('--results-directory', $ResultsDir) }
    if ($Filters.Count -gt 0) { $testArgs += @('--filter', ($Filters -join '&')) }
    if ($ExtraArgs.Count -gt 0) { $testArgs += $ExtraArgs }
    return , $testArgs
}

function Get-TestResultCounts {
    <#
    .SYNOPSIS
        从 dotnet test 输出解析通过/失败/跳过/总计（兼容中英 locale）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Output
    )

    $m = [regex]::Match($Output, '失败:\s*(\d+)\s*，通过:\s*(\d+)\s*，已跳过:\s*(\d+)\s*，总计:\s*(\d+)')
    if (-not $m.Success) {
        $m = [regex]::Match($Output, 'Failed:\s*(\d+)\s*,\s*Passed:\s*(\d+)\s*,\s*Skipped:\s*(\d+)\s*,\s*Total:\s*(\d+)')
    }
    if ($m.Success) {
        return @{
            Failed = [int]$m.Groups[1].Value; Passed = [int]$m.Groups[2].Value
            Skipped = [int]$m.Groups[3].Value; Total = [int]$m.Groups[4].Value
            Parsed = $true; Empty = $false
        }
    }
    # 过滤器零匹配：dotnet test 退出码为 0 但无任何用例执行 —— 必须显式暴露，避免"假通过"
    $empty = [regex]::IsMatch($Output, '没有测试匹配|No test matches')
    return @{ Failed = 0; Passed = 0; Skipped = 0; Total = 0; Parsed = $false; Empty = $empty }
}

function Get-FailedTestNames {
    <#
    .SYNOPSIS
        从 dotnet test 控制台输出提取失败用例名（兼容中英 locale）。

    .DESCRIPTION
        已知红名单（FD-03）比对需要的是**用例名**而非计数（原先只有计数，名单机制无从建立）。
        本器只解析 console logger 的失败明细行（`失败 <名> [<时长>]` / `Failed <名> [<时长>]`）；
        汇总行（`失败!  - 失败: 9，…`）与跳过行（`已跳过 X [..]`）因 `失败`/`已跳过` 后非空白而不匹配。
        提取为空但失败计数 > 0 的情形由调用方显式报错（不静默降级为「无失败」）。

    .OUTPUTS
        失败用例名（去重、升序）；无失败时为空数组。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Output
    )

    $names = New-Object System.Collections.Generic.List[string]
    foreach ($m in [regex]::Matches($Output, '(?m)^\s*(?:失败|Failed)\s+(?<name>\S.*?)\s+\[')) {
        $name = $m.Groups['name'].Value.Trim()
        if ($name) { $names.Add($name) }
    }

    return , @($names | Sort-Object -Unique)
}

function Write-TestUnitPlan {
    <#
    .SYNOPSIS
        打印运行计划（-DryRun 用）：逐单元输出工程 + 完整过滤命令，不执行任何测试。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object[]]$Units,
        [string]$Root = '',
        [switch]$ShowCommand
    )

    Write-Host ("  计划运行单元: {0} 个" -f $Units.Count) -ForegroundColor Gray
    Write-Host ""
    foreach ($unit in $Units) {
        if ($unit.Kind -eq 'ConsoleCheck') {
            Write-Host ("  [{0}] {1}  (ConsoleCheck)  <- {2}" -f $unit.Order, $unit.ProjectName, $unit.Reason) -ForegroundColor White
            continue
        }
        $filter = if (@($unit.Filters).Count -gt 0) { @($unit.Filters) -join '&' } else { '(全部)' }
        Write-Host ("  [{0}] {1}  <- {2}" -f $unit.Order, $unit.ProjectName, $unit.Reason) -ForegroundColor White
        Write-Host ("        filter: {0}" -f $filter) -ForegroundColor DarkGray
        if ($ShowCommand) {
            # 跨件依赖（本件 = 依赖根，DotnetTest 依赖本件，不得反向 import 造成环）：
            # `Find-RunSettings` 不可用时显式 fail-fast，不静默回落为「无 runsettings」。
            if (-not (Get-Command 'Find-RunSettings' -ErrorAction SilentlyContinue)) {
                throw '[TestRunner] Write-TestUnitPlan -ShowCommand 需同族 Find-RunSettings（请装配 Modules\TestRunner\_init.ps1）'
            }
            $planArgs = New-DotnetTestArgs -ProjectPath $unit.ProjectPath -Filters @($unit.Filters) `
                -RunSettings (Find-RunSettings -ProjectPath $unit.ProjectPath -Root $Root)
            Write-Host ("        cmd   : dotnet {0}" -f ($planArgs -join ' ')) -ForegroundColor DarkGray
        }
    }
    Write-Host ""
}

function Write-TestUnitResult {
    <#
    .SYNOPSIS
        输出单个运行单元的分色结果行；失败时打印输出尾部便于直接定位。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Result,
        [int]$TailLines = 20
    )

    if ($Result.NoTestsMatched) {
        Write-Host ("  [EMPTY] {0,-33} 过滤器零匹配（未执行任何用例），请检查 -Test/-Class/-Category" -f $Result.ProjectName) -ForegroundColor Yellow
        return
    }

    if ($Result.ExitCode -eq 0) {
        if ($Result.Parsed) {
            Write-Host ("  [PASS] {0,-34} 通过 {1,-5} 失败 {2,-4} 跳过 {3,-4} 总计 {4}  ({5}s)" -f `
                $Result.ProjectName, $Result.Passed, $Result.Failed, $Result.Skipped, $Result.Total, `
                ([math]::Round($Result.DurationMs / 1000.0, 1))) -ForegroundColor Green
        } else {
            Write-Host ("  [PASS] {0,-34} (退出码 0，无法解析计数)" -f $Result.ProjectName) -ForegroundColor Green
        }
        return
    }

    if ($Result.Parsed -and $Result.Kind -eq 'MsTest' -and $Result.Total -gt 0) {
        Write-Host ("  [FAIL] {0,-34} 通过 {1,-5} 失败 {2,-4} 跳过 {3,-4} 总计 {4}" -f `
            $Result.ProjectName, $Result.Passed, $Result.Failed, $Result.Skipped, $Result.Total) -ForegroundColor Red
    } else {
        Write-Host ("  [FAIL] {0,-34} (退出码 {1})" -f $Result.ProjectName, $Result.ExitCode) -ForegroundColor Red
    }
    ($Result.Output -split "`r?`n" | Select-Object -Last $TailLines | ForEach-Object { "      $_" }) |
        Write-Host -ForegroundColor DarkGray
}

function Write-TestRunSummary {
    <#
    .SYNOPSIS
        打印汇总并返回进程退出码（存在失败单元 -> 1，否则 0）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object[]]$Results,
        [int]$Units = 0
    )

    $results = $Results
    $pass = 0; $fail = 0; $skip = 0; $total = 0
    $bad = New-Object System.Collections.Generic.List[string]
    foreach ($r in $results) {
        $pass += $r.Passed; $fail += $r.Failed; $skip += $r.Skipped; $total += $r.Total
        if ($r.ExitCode -ne 0 -or $r.NoTestsMatched) { $bad.Add($r.ProjectName) }
    }
    if ($Units -le 0) { $Units = $results.Count }

    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host ("  汇总: 单元 {0}/{1} | 用例通过 {2} | 用例失败 {3} | 跳过 {4} | 用例总计 {5}" -f `
        ($Units - $bad.Count), $Units, $pass, $fail, $skip, $total) -ForegroundColor White

    if ($bad.Count -gt 0) {
        Write-Host ("  失败单元: {0}" -f ($bad -join ', ')) -ForegroundColor Red
        Write-Host "  存在失败，退出码 1" -ForegroundColor Red
        Write-Host "================================================================" -ForegroundColor Red
        return 1
    }

    Write-Host "  全部通过！" -ForegroundColor Green
    Write-Host "================================================================" -ForegroundColor Green
    return 0
}

Export-ModuleMember -Function @(
    'New-DotnetTestArgs',
    'Get-TestResultCounts',
    'Get-FailedTestNames',
    'Write-TestUnitPlan',
    'Write-TestUnitResult',
    'Write-TestRunSummary'
)
