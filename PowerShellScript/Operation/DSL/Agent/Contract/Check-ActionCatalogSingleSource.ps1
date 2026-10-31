#requires -Version 5.1
<#
.SYNOPSIS
    动作目录单源门禁：运行期注册表 → 加载期校验器投影（禁第二张手写白名单）。

.DESCRIPTION
    [A1] CommandSystemCoordinator 暴露 RegisteredActions（运行期真实注册表）；
    [A2] FileDrivenPipelineRunner 在管线执行前把 RegisteredActions 注入 CommandLineValidator；
    [A3] 正对照：WaitUntilSystem.HandledActions 含 wait-until/waituntil（能力在位）；
    [A4] 负对照：合成未声明动作不得出现在已登记动作集；
    [A5] CommandLineValidator 保留 SetRegisteredCommands（唯一投影入口）。
    只读源码；-Check 时违规 > 0 退出码 1。

    框架接线：根定位/违规清单 → GateRuntime.New-GateContext；裁决 → Write-GateVerdict。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位 + 上下文 + 裁决；与目录深度解耦）
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'ActionCatalogSingleSource'
$violations = $ctx.Violations

Write-GateHeader '动作目录单源守卫（ActionCatalogSingleSource）'

$runner      = Join-Path $ctx.Root 'Projects\FileDriven\FileDriven.Execution\Pipeline\FileDrivenPipelineRunner.cs'
$coordinator = Join-Path $ctx.Root 'Frame\CsharpPrj\Operation\BackEndDriven\CommandExecution\CommandCoordinator.cs'
$waitUntil   = Join-Path $ctx.Root 'Frame\CsharpPrj\Operation\BackEndDriven\CommandExecution\WaitUntilSystem.cs'
$validator   = Join-Path $ctx.Root 'Projects\FileDriven\FileDriven.Utilities\CommandLineValidator.cs'

foreach ($file in @($runner, $coordinator, $waitUntil, $validator)) {
    if (-not (Test-Path -LiteralPath $file)) { $violations.Add("缺少动作目录面文件: $file") }
}

if ($violations.Count -eq 0) {
    $coordText = Get-Content -LiteralPath $coordinator -Raw
    $runnerText = Get-Content -LiteralPath $runner -Raw
    $waitText = Get-Content -LiteralPath $waitUntil -Raw
    $validatorText = Get-Content -LiteralPath $validator -Raw

    # [A1] 运行期注册表投影口
    if ($coordText -notmatch 'RegisteredActions') {
        $violations.Add('[A1] CommandSystemCoordinator 未暴露 RegisteredActions（运行期注册表不可投影）')
    }

    # [A2] 加载期校验器注入（唯一执行入口）
    if ($runnerText -notmatch 'SetRegisteredCommands\s*\(\s*commandInputPort\.Coordinator\.RegisteredActions\s*\)') {
        $violations.Add('[A2] FileDrivenPipelineRunner 未在脚本加载前注入 RegisteredActions（第二白名单仍是权威）')
    }

    # [A5] 投影入口存在
    if ($validatorText -notmatch 'public static void SetRegisteredCommands') {
        $violations.Add('[A5] CommandLineValidator 缺少 SetRegisteredCommands（无法投影运行期注册表）')
    }

    # [A3] 正对照：wait-until 能力在位
    $handled = @([regex]::Matches($waitText, '"([a-zA-Z0-9\-]+)"') | ForEach-Object { $_.Groups[1].Value })
    if (-not ($handled -contains 'wait-until') -or -not ($handled -contains 'waituntil')) {
        $violations.Add('[A3] 正对照失败：WaitUntilSystem.HandledActions 未同时登记 wait-until/waituntil')
    }

    # [A4] 负对照：合成未声明动作不得被承认
    $synthetic = 'not-a-command'
    if ($handled -contains $synthetic) {
        $violations.Add('[A4] 负对照失败：未声明的合成动作被 WaitUntilSystem 承认')
    }
    if ($validatorText -match [regex]::Escape("'$synthetic'") -or
        $validatorText -match [regex]::Escape('"' + $synthetic + '"')) {
        $violations.Add('[A4] 负对照失败：未声明的合成动作出现在校验器白名单')
    }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'ActionCatalogSingleSource'
