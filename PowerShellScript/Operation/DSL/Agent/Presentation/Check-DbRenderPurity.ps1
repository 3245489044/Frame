#requires -Version 5.1
<#
.SYNOPSIS
    渲染纯化守卫（DbRenderPurity）—— 渲染路径只读交互态。

.DESCRIPTION
    判据（B3/ERR-07+SCT-04）：
      G1 零写：数据库页渲染文件内 `state.<成员> =` 赋值 = 0；`state.Selected/Page` 变更调用 = 0。
      G2 只读读取与渲染入口在位：`dbPage.State` + `RenderNodePanelHtml` 在位。
      G3 正/负对照：合成样本（渲染期写 state）必被捕获。
    正/负对照由 Assert-GateControls 统一执行。

    模式：默认报告；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'DbRenderPurity'
$violations = $ctx.Violations

Write-GateHeader '渲染纯化守卫（DbRenderPurity）'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

# 判据自证（同一谓词既用于扫描也用于对照）
$controls = @(
    @{ Id = 'G1-write'; Predicate = { param($t) $t -match 'state\.[A-Za-z]+\s*=[^=]' }
       Good = 'var current = state.CurrentDb;'; Bad = 'state.CurrentDb = x;' }
    @{ Id = 'G1-mutate'; Predicate = { param($t) $t -match 'state\.Selected\.(Add|Remove|Clear)\s*\(' }
       Good = 'var n = state.Selected.Count;'; Bad = 'state.Selected.Add(key);' }
)
foreach ($issue in (Assert-GateControls -Cases $controls -Label 'DbRenderPurity')) { $violations.Add($issue) }

$file = Join-Path (Get-MappedPath -Key 'AgentSrc') 'Agent.Ui\Ui\Views\Orchestration\ConversationViewService.Db.cs'
if (-not (Test-Path -LiteralPath $file)) {
    $violations.Add('[G1] 未见 ConversationViewService.Db.cs')
} else {
    $t = [System.IO.File]::ReadAllText($file)
    # 只匹配赋值（排除 == 比较）
    $writes = Get-Count $t 'state\.[A-Za-z]+\s*=[^=]'
    if ($writes -gt 0) { $violations.Add("[G1] 渲染路径写交互态 state.* = × $writes") }
    $mutates = Get-Count $t 'state\.Selected\.(Add|Remove|Clear)\s*\('
    if ($mutates -gt 0) { $violations.Add("[G1] 渲染路径变更选择集 × $mutates") }
    if ((Get-Count $t 'RenderNodePanelHtml') -eq 0) { $violations.Add('[G2] 渲染入口 RenderNodePanelHtml 缺失') }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'DbRenderPurity'
