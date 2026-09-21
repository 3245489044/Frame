<#
.SYNOPSIS
    Pester/探针测试脚手架（通用单源）：结果收集 · 断言 · 临时工作区 · 环境初始化 · 汇总。

.DESCRIPTION
    消除「测试脚手架三份写法」：`PowerShellTests\BasicMethod.Tests\TestHelper.psm1`（Write-TestHeader /
    Write-TestResult / Test-Case / Get-TestTempDir / Initialize-TestEnvironment / Cleanup-TestEnvironment /
    New-TestFilePath / Write-TestSummary）与各测试内联的 `Add-Check`（如 `Tools\Test-SpecConvertTooling.ps1`）。

    机制与本模块既有 `Modules\Tools\Testing\`（`Run-PesterTests.ps1` / `Setup-PesterTestEnv.ps1`，
    职责 = **运行器与环境准备脚本**）分工不同：本件是**脚手架函数库**（由测试件按需导入）。

    命名纪律：`Add-Check` 通用化为 `Add-TestCheck` —— `Add-Check` 在多个测试件内为**脚本级**定义，
    模块导出的同名函数会与之同名遮蔽，故不复用该名。

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

    return , ([System.Collections.Generic.List[object]]::new())
}

function Add-TestCheck {
    <#
    .SYNOPSIS
        记账一条自检结果并即时打印 `[PASS]/[FAIL] 名称 <- 详情`。
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
        打印自检汇总并返回进程退出码（有失败 -> 1，否则 0）。
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

function Invoke-TestCase {
    <#
    .SYNOPSIS
        测试用例包装器：执行脚本块，捕获异常并输出 PASS/FAIL。
    .OUTPUTS
        $true = 通过；$false = 抛异常。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$ScriptBlock
    )

    Write-Host "=== $Name ===" -ForegroundColor Cyan
    try {
        & $ScriptBlock
        Write-Host "[PASS] $Name" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "[FAIL] $Name : $_" -ForegroundColor Red
        return $false
    }
}

function New-TestTempWorkspace {
    <#
    .SYNOPSIS
        新建临时工作区（存在则先清空）。
    .PARAMETER Root
        父目录（缺省 = 系统临时目录）。
    .OUTPUTS
        工作区绝对路径。
    #>
    [CmdletBinding()]
    param(
        [string]$Root = '',
        [string]$Name = 'ps-test-temp'
    )

    if (-not $Root) { $Root = [System.IO.Path]::GetTempPath() }
    $path = Join-Path $Root $Name
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    New-Item -Path $path -ItemType Directory -Force | Out-Null
    Write-Host "[初始化] 测试工作区已准备: $path" -ForegroundColor DarkGray
    return $path
}

function Remove-TestTempWorkspace {
    <#
    .SYNOPSIS
        清理临时工作区（不存在即静默返回）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
        Write-Host "[清理] 测试工作区已删除: $Path" -ForegroundColor DarkGray
    }
}

function New-TestFilePath {
    <#
    .SYNOPSIS
        取工作区内文件的完整路径（不创建文件）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$FileName
    )

    return (Join-Path $Root $FileName)
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

function Assert-ExitCode {
    <#
    .SYNOPSIS
        断言退出码（不符即抛，供 Pester `Should -Throw` 或探针直接使用）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][int]$Expected,
        [Parameter(Mandatory = $true)][int]$Actual,
        [string]$Context = ''
    )

    if ($Expected -ne $Actual) {
        $suffix = $(if ($Context) { "（$Context）" } else { '' })
        throw ("[PesterKit] 退出码不符{suffix}：期望 {0}，实际 {1}" -f $Expected, $Actual)
    }
}

Export-ModuleMember -Function @(
    'New-TestResultCollector',
    'Add-TestCheck',
    'Get-TestCheckSummary',
    'Write-TestCheckSummary',
    'Write-TestHeader',
    'Write-TestResult',
    'Invoke-TestCase',
    'New-TestTempWorkspace',
    'Remove-TestTempWorkspace',
    'New-TestFilePath',
    'Write-TestSummary',
    'Assert-ExitCode'
)
