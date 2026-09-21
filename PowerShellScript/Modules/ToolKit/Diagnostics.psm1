<#
.SYNOPSIS
    规格诊断收口（通用）：`文件:行:列: 码 消息` 记账 + 汇总。

.DESCRIPTION
    自 `Tools\Convert-CSharpToSpec.ps1` 的 `Add-Violation` 与 `Tools\Expand-SpecSource.ps1` 同码同定位口径抽取
    （N2/M1）。定位面（文件/行/列）与诊断码由调用方给出；本件不假设任何领域码表。

    依赖方向（硬约束）：CentralTest → Frame（单向）；本件零 CentralTest 引用。
#>

Set-StrictMode -Version Latest

function New-ToolDiagnostic {
    <#
    .SYNOPSIS
        新建诊断收集器。
    #>
    [CmdletBinding()]
    param()

    return , ([System.Collections.Generic.List[object]]::new())
}

function Add-ToolViolation {
    <#
    .SYNOPSIS
        记账一条违规并返回其一行文本（`文件:行:列: 码 消息`）。
    .PARAMETER Line
        行号（未知传 0）。
    .PARAMETER Column
        列号（未知传 0）。
    .OUTPUTS
        格式化文本（调用方决定是打印还是回填报告）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Diagnostics,
        [Parameter(Mandatory = $true)][string]$File,
        [int]$Line = 0,
        [int]$Column = 0,
        [Parameter(Mandatory = $true)][string]$Code,
        [Parameter(Mandatory = $true)][string]$Message
    )

    $Diagnostics.Add([pscustomobject]@{
            File = $File; Line = $Line; Column = $Column; Code = $Code; Message = $Message
        })
    return ('{0}:{1}:{2}: {3} {4}' -f $File, $Line, $Column, $Code, $Message)
}

function Format-ToolViolation {
    <#
    .SYNOPSIS
        把违规项渲染为一行文本（读面；与 `Add-ToolViolation` 的输出口径同源）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Violation
    )

    return ('{0}:{1}:{2}: {3} {4}' -f $Violation.File, $Violation.Line, $Violation.Column,
        $Violation.Code, $Violation.Message)
}

function Get-ToolDiagnosticSummary {
    <#
    .SYNOPSIS
        汇总诊断（按码计数 + 总数）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Diagnostics
    )

    $all = @($Diagnostics)
    $byCode = [ordered]@{}
    foreach ($item in $all) {
        $byCode[$item.Code] = 1 + [int]$byCode[$item.Code]
    }
    return [pscustomobject]@{ Total = $all.Count; ByCode = $byCode }
}

function Write-ToolDiagnosticSummary {
    <#
    .SYNOPSIS
        打印逐条违规与按码汇总，并返回进程退出码（有违规 -> 1，否则 0）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Diagnostics,
        [string]$Title = '诊断'
    )

    $summary = Get-ToolDiagnosticSummary -Diagnostics $Diagnostics
    if ($summary.Total -eq 0) {
        Write-Host ("[{0}] 无违规。" -f $Title) -ForegroundColor Green
        return 0
    }

    Write-Host ("[{0}] 违规 {1} 条：" -f $Title, $summary.Total) -ForegroundColor Red
    foreach ($item in @($Diagnostics)) { Write-Host ("  {0}" -f (Format-ToolViolation $item)) -ForegroundColor Red }
    foreach ($code in $summary.ByCode.Keys) {
        Write-Host ("  {0}: {1}" -f $code, $summary.ByCode[$code]) -ForegroundColor Yellow
    }
    return 1
}

Export-ModuleMember -Function @(
    'New-ToolDiagnostic', 'Add-ToolViolation', 'Format-ToolViolation',
    'Get-ToolDiagnosticSummary', 'Write-ToolDiagnosticSummary'
)
