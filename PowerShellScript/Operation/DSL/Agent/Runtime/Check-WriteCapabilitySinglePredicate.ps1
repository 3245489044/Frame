#requires -Version 5.1
<#
.SYNOPSIS
    域写能力单谓词守卫（WriteCapabilitySinglePredicate）—— 能力判定入口唯一。

.DESCRIPTION
    判据（B3/SCT-01）：
      G1 单谓词在位：`AgentWriteSources.Capability(` 定义 = 1（枚举 WriteCapability 取值域在位）。
      G2 旧三处判定归零：Agent src 内 `IsInlineEditable`/`IsKnownWriteDomain`/`IsRowWritable` 引用 = 0。
      G3 正/负对照：合成样本（旧判定名）必被捕获。

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
Write-Host '  域写能力单谓词守卫（WriteCapabilitySinglePredicate）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$src = Join-Path $Root 'Projects\Plug\Agent\src'
$sources = Join-Path $src 'Agent.Ui\Core\Functions\Batch\AgentWriteSources.cs'
$capDefs = 0
$legacyRefs = 0
foreach ($f in (Get-ChildItem -Path $src -Recurse -Include '*.cs' -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj|BuildOutput)\\' })) {
    $t = [System.IO.File]::ReadAllText($f.FullName)
    $c1 = Get-Count $t 'internal\s+static\s+WriteCapability\s+Capability\s*\('
    if ($c1 -gt 0) { $capDefs += $c1 }
    $c2 = Get-Count $t 'IsInlineEditable|IsKnownWriteDomain|IsRowWritable'
    if ($c2 -gt 0) { $legacyRefs += $c2; $violations.Add("[G2] 旧能力判定引用：$($f.Name) × $c2") }
}
if ($capDefs -ne 1) { $violations.Add("[G1] 单谓词 Capability 定义数 ≠ 1（实际 $capDefs）") }
if (-not (Test-Path -LiteralPath $sources)) { $violations.Add('[G1] 未见 AgentWriteSources.cs') }
elseif ((Get-Count ([System.IO.File]::ReadAllText($sources)) 'enum\s+WriteCapability') -eq 0) {
    $violations.Add('[G1] WriteCapability 枚举缺失')
}

# 正/负对照
if ((Get-Count 'IsInlineEditable(x)' 'IsInlineEditable|IsKnownWriteDomain|IsRowWritable') -eq 0) {
    $violations.Add('[G3] 负对照失效：合成样本（旧判定名）未被捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2 域写能力单谓词通过（正负对照绿）' -ForegroundColor Green
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
