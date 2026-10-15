<#
.SYNOPSIS
    Dispatcher Framework 一键初始化
.DESCRIPTION
    加载「入口薄转发」的**通用机制件**（M5 抽取）—— 消除 Frame\Invoke 各入口逐字重复的
    「按名传参 + 退出码归一（0/1/2）+ 目标存在性 + SpecCli 参数装配」样板。

    使用方式：
        . .\Dispatcher\_init.ps1

    导出面（判据：`.psm1` 的 Export-ModuleMember 清单 = 本清单）：
        Dispatcher.psm1 : Resolve-DispatchTarget / New-DispatchParams / New-SpecCliArgs /
                          Get-NormalizedExitCode / Import-DispatchTable / Get-DispatchEntry

    依赖方向（硬约束）：CentralTest → Frame（单向）；本模块零 CentralTest 引用。
.notes
    版本: 1.0.0
    来源: Frame\Invoke\*.ps1（19 件薄转发的公共段，M5 抽取）
#>

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$modulePath = Join-Path $scriptDir 'Dispatcher.psm1'

if (-not (Test-Path -LiteralPath $modulePath)) {
    throw "[Dispatcher] 子模块缺失：$modulePath"
}

# `-Global` 必要：装配点可能被模块加载（与 ToolKit / TestRunner 同族），非全局导入会落成嵌套模块，
# 算子只在模块作用域可见 ⇒ 入口脚本取不到（实测同族踩过）。
Import-Module -Name $modulePath -Force -Global -DisableNameChecking