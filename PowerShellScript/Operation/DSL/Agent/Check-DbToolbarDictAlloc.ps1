#requires -Version 5.1
<#
.SYNOPSIS
    数据库页工具条字典分配守卫（DbToolbarDictAlloc）—— 渲染路径零逐命令 `new Dictionary{type=db}`。

.DESCRIPTION
    判据（B3/RED-13）：
      G1 零分配：ConversationViewService.Db.cs 渲染路径内 `new Dictionary<string, string> { ["type"` = 0。
      G2 工厂在位：DbCmdAttrs.For 存在且该渲染文件引用之。
      G3 正/负对照：合成样本（逐命令字典）必被捕获。

    模式：默认报告；-Check 时违规 → 退出码 1。

.PARAMETER Root
    仓库根（默认为本脚本位置上溯五级）。
.PARAMETER Check
    校验模式。
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
Write-Host '  数据库页工具条字典分配守卫（DbToolbarDictAlloc）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$viewFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\ConversationViewService.Db.cs'
$factory  = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\DbCmdAttrs.cs'
$pattern  = 'new\s+Dictionary<string,\s*string>\s*\{\s*\["type"'

if (-not (Test-Path -LiteralPath $viewFile)) {
    $violations.Add("[G1] 缺件：$viewFile")
} else {
    $t = [System.IO.File]::ReadAllText($viewFile)
    $hits = Get-Count $t $pattern
    if ($hits -ne 0) { $violations.Add("[G1] 渲染路径逐命令字典命中 $hits 处（应 0，改走 DbCmdAttrs.For）") }
    if ($t -notmatch 'DbCmdAttrs\.For') { $violations.Add('[G2] 渲染文件未引用 DbCmdAttrs.For') }
    if (-not (Test-Path -LiteralPath $factory)) { $violations.Add('[G2] 缺工厂 DbCmdAttrs.cs') }
}

# 正/负对照
if ((Get-Count 'Data: new Dictionary<string, string> { ["type"] = db });' $pattern) -eq 0) {
    $violations.Add('[G3] 负对照失效：合成样本（逐命令字典）未被捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2 工具条字典单源通过（正负对照绿）' -ForegroundColor Green
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
