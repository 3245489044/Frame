#requires -Version 5.1
<#
.SYNOPSIS
    表写声明完备性守卫（TableWriteDeclaration）—— 「表即声明」的写面（write:）fail-closed + 行端口注册表单源。

.DESCRIPTION
    判据（行写通用化 S5/S7）：
      G1 声明在位：`Projects\Plug\Agent\Config\nodes\tables.ushar` 存在且含 `[Table]` 声明。
      G2 写面完备（fail-closed）：每个 `[Table]` 块须含 `write:` 段；`editable=false`（或缺省）须附 `reason=`。
      G3 行端口注册表单源：`AgentWriteSources.cs` 含 `TryGetRecord` 且四存储键（Nodes/Tasks/Histories/Graph）齐全。
      G4 正/负对照：缺 `write:` 段的合成样本必被 G2 捕获（防判据空转）。

    模式：默认报告；-Check 时违规 → 退出码 1。

.PARAMETER Root
    仓库根（默认为本脚本位置上溯五级）。
.PARAMETER Check
    校验模式。
.PARAMETER ShowDetail
    输出全部检查详情。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================'
Write-Host '  表写声明完备性守卫（TableWriteDeclaration：fail-closed / 注册表单源）'
Write-Host '============================================================'

$tablesFile = Join-Path $Root 'Projects\Plug\Agent\Config\nodes\tables.ushar'
$writeSources = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Core\Functions\Batch\AgentWriteSources.cs'

# ---------------- 解析 [Table] 块 → 写面完备性 ----------------
function Get-TableWriteGaps([string]$Text) {
    $gaps = [System.Collections.Generic.List[string]]::new()
    $cur = $null
    foreach ($line in ($Text -split "`r?`n")) {
        if ($line -match '^\s*\[Table\]\s+([^:]+):') {
            if ($null -ne $cur -and -not $cur.HasWrite) { $gaps.Add("$($cur.Name): 缺 write: 段（fail-closed 声明不完整）") }
            if ($null -ne $cur -and -not $cur.Editable -and -not $cur.HasReason) { $gaps.Add("$($cur.Name): 不可写但缺 reason= 可见化原因") }
            $cur = @{ Name = $Matches[1].Trim(); HasWrite = $false; Editable = $false; HasReason = $false }
            continue
        }
        if ($null -eq $cur) { continue }
        if ($line -match '^\s*write\s*:') { $cur.HasWrite = $true }
        if ($line -match 'editable\s*=\s*true') { $cur.Editable = $true }
        if ($line -match 'reason\s*=') { $cur.HasReason = $true }
    }
    if ($null -ne $cur -and -not $cur.HasWrite) { $gaps.Add("$($cur.Name): 缺 write: 段（fail-closed 声明不完整）") }
    if ($null -ne $cur -and -not $cur.Editable -and -not $cur.HasReason) { $gaps.Add("$($cur.Name): 不可写但缺 reason= 可见化原因") }
    return $gaps
}

# ---------------- G1 声明在位 ----------------
if (-not (Test-Path -LiteralPath $tablesFile)) {
    $violations.Add("[G1] 缺件：表声明（$tablesFile）")
    $tableText = ''
} else {
    $tableText = [System.IO.File]::ReadAllText($tablesFile)
    if ($tableText -notmatch '\[Table\]') {
        $violations.Add('[G1] tables.ushar 无任何 [Table] 声明')
    }
}

# ---------------- G2 写面完备（fail-closed） ----------------
if ($tableText.Length -gt 0) {
    foreach ($g in (Get-TableWriteGaps $tableText)) { $violations.Add("[G2] $g") }
}

# ---------------- G3 行端口注册表单源 ----------------
if (-not (Test-Path -LiteralPath $writeSources)) {
    $violations.Add("[G3] 缺件：写源注册表（$writeSources）")
} else {
    $src = [System.IO.File]::ReadAllText($writeSources)
    if ($src -notmatch 'TryGetRecord') { $violations.Add('[G3] 写源注册表缺行端口入口 TryGetRecord') }
    foreach ($key in @('DbStoreKeys\.Nodes', 'DbStoreKeys\.Tasks', 'DbStoreKeys\.Histories', 'DbStoreKeys\.Graph')) {
        if ($src -notmatch $key) { $violations.Add("[G3] 行端口注册表缺存储键（$key）") }
    }
}

# ---------------- G4 正/负对照 ----------------
$probe = Get-TableWriteGaps "[Table] probe: container=probe`n  Id : int | auto"
if ($probe.Count -eq 0) {
    $violations.Add('[G4] 负对照失效：缺 write: 段的合成样本未被 G2 捕获')
}
$negProbe = Get-TableWriteGaps "[Table] ok: container=ok`n  write: editable=false | reason=只读演示"
if ($negProbe.Count -ne 0) {
    $violations.Add('[G4] 正对照失效：完备声明样本被误报')
}

if ($ShowDetail) { Write-Host ("  声明文本长度={0}" -f $tableText.Length) -ForegroundColor Gray }

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2/G3/G4 全部通过（写面完备 fail-closed + 注册表单源 + 正负对照绿）' -ForegroundColor Green
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
