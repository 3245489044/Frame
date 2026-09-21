<#
.SYNOPSIS
    TestRunner Framework 一键初始化
.DESCRIPTION
    加载 TestRunner Framework 及其三个子模块（dotnet test 执行面，通用）。

    使用方式：
        . .\TestRunner\_init.ps1

    导出面（判据：`.psm1` 的 Export-ModuleMember 清单 = 本清单）：
        DotnetTestRunner.psm1 : New-DotnetTestArgs / Get-TestResultCounts / Get-FailedTestNames /
                               Write-TestUnitPlan / Write-TestUnitResult / Write-TestRunSummary
        DotnetTest.psm1       : Find-RunSettings / Invoke-TestUnit
        KnownFailures.psm1    : Compare-KnownFailures

    依赖方向：CentralTest → Frame（单向）；本模块零 CentralTest 引用。
    命名纪律：主件不用 `TestRunner.psm1` —— CentralTest 侧同名模块在 M2 切换期共存，同名会互相遮蔽导出面。
.NOTES
    版本: 1.0.0
    来源: Environment\CentralTest\TestRunner\TestRunner.psm1（N1/M1 抽取，函数体原样）
#>

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$runnerModules = @(
    'DotnetTestRunner.psm1',  # 参数构造 / 计数解析 / 失败名 / 计划与结果渲染 / 汇总
    'DotnetTest.psm1',        # runsettings 发现 + 单单元执行（依赖上一件）
    'KnownFailures.psm1'      # 已知红名单棘轮比对
)

foreach ($module in $runnerModules) {
    $modulePath = Join-Path $scriptDir $module
    if (-not (Test-Path -LiteralPath $modulePath)) {
        throw "[TestRunner] 子模块缺失：$modulePath"
    }
    # `-Global` 必要：本件作为**装配点**可能被模块加载（CentralTest `TestRunner.psm1`），届时非全局导入会
    # 落成嵌套模块 —— 算子只在该模块作用域可见，调用方脚本（Run-*.ps1）与并行 runspace 都拿不到（实测）。
    Import-Module -Name $modulePath -Force -Global -DisableNameChecking
}
