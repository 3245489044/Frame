<#
.SYNOPSIS
    Pester/探针测试的**展示与汇总面**（通用单源）：节标题 / 单条结果 / 汇总。

.DESCRIPTION
    消重复（2026-09-21 M4）：`PowerShellTests\BasicMethod.Tests\TestHelper.psm1` 原先自持
    `Write-TestHeader` / `Write-TestResult` / `Write-TestSummary`（与 `Test-Case`），现由本模块单一实现、
    调用点不变；`Test-Case` 经实测**无任何消费者**（死导出）随批删除。

    职责边界：机制与本模块既有 `Modules\Tools\Testing\`（`Run-PesterTests.ps1` / `Setup-PesterTestEnv.ps1`，
    = **运行器与环境准备脚本**）不同 —— 本件是**展示函数库**（由测试件按需导入）。

    边界（不留冗余）：曾按方案备过「结果收集器 / 临时工作区 / Assert-ExitCode」等函数，实测**零消费者**
    （仅出现在编译映射里）⇒ 已删除，不做「为未来造框架」；将来某消费方确实需要时，**与其切换同批**引入。

    依赖方向（硬约束）：CentralTest → Frame（单向）；本件零 CentralTest 引用。
#>

Set-StrictMode -Version Latest

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
    'Write-TestHeader',
    'Write-TestResult',
    'Write-TestSummary'
)
