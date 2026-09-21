<#
.SYNOPSIS
    Pester/探针测试的**结果收集 + 展示 + 汇总面**（通用单源）。

.DESCRIPTION
    消重复（2026-09-21 M4）：`PowerShellTests\BasicMethod.Tests\TestHelper.psm1` 原先自持
    `Write-TestHeader` / `Write-TestResult` / `Write-TestSummary`（与 `Test-Case`），现由本模块单一实现、
    调用点不变；`Test-Case` 经实测**无任何消费者**（死导出）随批删除。

    结果收集器（2026-09-21 M4 余量）：`Tools\Test-SpecConvertTooling.ps1` 原先内联 `Add-Check`（自建清单 +
    自着色 + 自汇总）⇒ 切换为本模块的 `New-TestResultCollector` / `Add-TestCheck` / `Get-TestCheckSummary` /
    `Write-TestCheckSummary`，调用点零语义变化（逐条 `[PASS]/[FAIL]` 行与 `工具自检：n/m 通过` 汇总行格式保持）。

    职责边界：机制与本模块既有 `Modules\Tools\Testing\`（`Run-PesterTests.ps1` / `Setup-PesterTestEnv.ps1`，
    = **运行器与环境准备脚本**）不同 —— 本件是**脚手架函数库**（由测试件按需导入）。

    边界（不留冗余）：曾按方案备过「临时工作区 / `Assert-ExitCode` / `Invoke-TestCase`」等函数，实测**零消费者**
    （仅出现在编译映射里）⇒ 不引入，不做「为未来造框架」；将来某消费方确实需要时，**与其切换同批**引入。

    依赖方向（硬约束）：CentralTest → Frame（单向）；本件零 CentralTest 引用。
#>

Set-StrictMode -Version Latest

function New-TestResultCollector {
    <#
    .SYNOPSIS
        新建结果收集器（探针式自检的清单载体）。
    #>
    [CmdletBinding()]
    param()

    # `, ` 必要：空清单若直接返回会被管道解包成 $null（消费方随后绑定到 `-Results` 即失败）
    return , ([System.Collections.Generic.List[object]]::new())
}

function Add-TestCheck {
    <#
    .SYNOPSIS
        记账一条自检结果并即时打印 `  [PASS]/[FAIL] 名称  <- 详情`。
    .PARAMETER Results
        `New-TestResultCollector` 的产物。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Results,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][bool]$Ok,
        [string]$Detail = ''
    )

    $Results.Add([pscustomobject]@{ Name = $Name; Ok = $Ok; Detail = $Detail })
    Write-Host ('  [{0}] {1}{2}' -f $(if ($Ok) { 'PASS' } else { 'FAIL' }), $Name,
        $(if ($Detail) { '  <- ' + $Detail } else { '' })) -ForegroundColor $(if ($Ok) { 'Green' } else { 'Red' })
}

function Get-TestCheckSummary {
    <#
    .SYNOPSIS
        汇总自检结果。
    .OUTPUTS
        @{ Passed; Failed; Total; FailedNames }
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Results
    )

    $all = @($Results)
    $failed = @($all | Where-Object { -not $_.Ok })
    return [pscustomobject]@{
        Passed      = $all.Count - $failed.Count
        Failed      = $failed.Count
        Total       = $all.Count
        FailedNames = @($failed | ForEach-Object { $_.Name })
    }
}

function Write-TestCheckSummary {
    <#
    .SYNOPSIS
        打印自检汇总并返回进程退出码（有失败 → 1，否则 0）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Results,
        [string]$Title = '工具自检'
    )

    $summary = Get-TestCheckSummary -Results $Results
    Write-Host ''
    Write-Host ('{0}：{1}/{2} 通过' -f $Title, $summary.Passed, $summary.Total) `
        -ForegroundColor $(if ($summary.Failed -eq 0) { 'Green' } else { 'Red' })
    if ($summary.Failed -gt 0) {
        Write-Host ('  失败项：{0}' -f ($summary.FailedNames -join ' ; ')) -ForegroundColor Red
    }
    return $(if ($summary.Failed -gt 0) { 1 } else { 0 })
}

function Write-TestHeader {
    <#
    .SYNOPSIS
        输出测试节标题。
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Title)

    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
}

function Write-TestResult {
    <#
    .SYNOPSIS
        输出单条测试结果（PASS/FAIL + 可选详情）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$TestName,
        [Parameter(Mandatory = $true)][bool]$Passed,
        [string]$Detail = ''
    )

    Write-Host ('{0} {1}' -f $(if ($Passed) { '[PASS]' } else { '[FAIL]' }), $TestName) `
        -ForegroundColor $(if ($Passed) { 'Green' } else { 'Red' })
    if ($Detail) { Write-Host "       $Detail" -ForegroundColor DarkGray }
}

function Write-TestSummary {
    <#
    .SYNOPSIS
        输出测试汇总（通过/失败/总计）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][int]$PassedCount,
        [Parameter(Mandatory = $true)][int]$FailedCount,
        [Parameter(Mandatory = $true)][int]$TotalCount
    )

    Write-Host "`n========================================" -ForegroundColor Magenta
    Write-Host '  测试完成' -ForegroundColor Magenta
    Write-Host "  通过: $PassedCount / $TotalCount" -ForegroundColor Green
    if ($FailedCount -gt 0) { Write-Host "  失败: $FailedCount" -ForegroundColor Red }
    Write-Host '========================================' -ForegroundColor Magenta
}

Export-ModuleMember -Function @(
    'New-TestResultCollector',
    'Add-TestCheck',
    'Get-TestCheckSummary',
    'Write-TestCheckSummary',
    'Write-TestHeader',
    'Write-TestResult',
    'Write-TestSummary'
)
