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
        EnvScope.psm1    : Enter-EnvScope / Exit-EnvScope / Test-EnvScopeResidue

    边界（显式登记）：`Report.psm1`（报告分节/kv 表渲染）与 `Batch.psm1`（子进程批量驱动 + 并发 + 逐 job 记账）
    须与消费方（`Tools\Convert-CSharpToSpec.ps1` 外壳重写）**同批**落地（M3），故本批不产出 —— 避免「只新增」
    之外的空壳件与随之而来的第二份实现。

    依赖方向：CentralTest → Frame（单向）；本模块零 CentralTest 引用。
.NOTES
    版本: 1.0.0
    来源: Environment\CentralTest\Tools\Convert-CSharpToSpec.ps1（外壳段，N2/M1）
#>

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$toolKitModules = @(
    'ToolData.psm1',      # 数据真源 fail-fast 加载 + 点路径取值 + 消息模板
    'Diagnostics.psm1',   # `文件:行:列: 码 消息` 记账与汇总
    'EnvScope.psm1'       # 进程级环境变量作用域（FD-11 口径）
)

foreach ($module in $toolKitModules) {
    $modulePath = Join-Path $scriptDir $module
    if (-not (Test-Path -LiteralPath $modulePath)) {
        throw "[ToolKit] 子模块缺失：$modulePath"
    }
    Import-Module -Name $modulePath -Force -DisableNameChecking
}
