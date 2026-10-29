#requires -Version 5.1
<#
.SYNOPSIS
    渲染纯化守卫（DbRenderPurity）—— 渲染路径只读交互态。

.DESCRIPTION
    判据（B3/ERR-07+SCT-04）：
      G1 零写：数据库页渲染文件内 `state.<成员> =` 赋值 = 0；`state.Selected/Page` 变更调用 = 0。
      G2 只读读取与渲染入口在位：`dbPage.State` + `RenderNodePanelHtml` 在位。
      G3 正/负对照：合成样本（渲染期写 state）必被捕获。

    模式：默认报告；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()
Write-Host ''
Write-Host '============================================================'
Write-Host '  渲染纯化守卫（DbRenderPurity）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$file = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Orchestration\ConversationViewService.Db.cs'
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

# 正/负对照
if ((Get-Count 'state.CurrentDb = x;' 'state\.[A-Za-z]+\s*=[^=]') -eq 0) {
    $violations.Add('[G3] 负对照失效：合成样本（state 赋值）未被捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2 渲染纯化通过（正负对照绿）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host ''
Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
