<#
.SYNOPSIS
    ToolKit Framework 一键初始化
.DESCRIPTION
    加载 CLI 工具外壳的**通用机制件**（N2 + N7）。

    使用方式：
        . .\ToolKit\_init.ps1

    导出面（判据：`.psm1` 的 Export-ModuleMember 清单 = 本清单）：
        ToolData.psm1    : Import-ToolData / Get-ToolDataValue / Get-ToolMessage
        Diagnostics.psm1 : New-ToolDiagnostic / Add-ToolViolation / Format-ToolViolation /
                           Get-ToolDiagnosticSummary / Write-ToolDiagnosticSummary
        EnvScope.psm1    : Enter-EnvScope / Set-EnvScopeVariable / Clear-EnvScopeVariable / Exit-EnvScope /
                           Test-EnvScopeResidue
        Batch.psm1       : Invoke-ProcessBatch / Invoke-ProcessBatchJob / Get-BatchSummary /
                           Get-BatchOutputLines / Write-BatchReport

    边界（显式登记）：报告渲染未提取 —— 实测其重复面仅「分节/空行」与表格表头两行样板，且两处消费方
    （`Convert-CSharpToSpec` 的数据驱动 `$RPT.*` 模板 / `Get-SpecIr` 的硬编码 markdown 表）格式不同，
    抽取只增间接层（对齐判据口径「不许以更整洁为由重构」），故不做。

    依赖方向：CentralTest → Frame（单向）；本模块零 CentralTest 引用。
.NOTES
    版本: 1.0.0
    来源: Environment\CentralTest\Tools\Convert-CSharpToSpec.ps1（外壳段，N2/M1）
#>

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$toolKitModules = @(
    'ToolData.psm1',      # 数据真源 fail-fast 加载 + 点路径取值 + 消息模板
    'Diagnostics.psm1',   # `文件:行:列: 码 消息` 记账与汇总
    'EnvScope.psm1',      # 进程级环境变量作用域（FD-11 口径）
    'Batch.psm1'          # 子进程批量驱动（并发 + 逐 job 记账 + 证据落盘）
)

foreach ($module in $toolKitModules) {
    $modulePath = Join-Path $scriptDir $module
    if (-not (Test-Path -LiteralPath $modulePath)) {
        throw "[ToolKit] 子模块缺失：$modulePath"
    }
    Import-Module -Name $modulePath -Force -DisableNameChecking
}
