<#
.SYNOPSIS
    启动入口门禁（Gate=LaunchEntrypoint）—— K1 入口零逻辑：外部 bat 只做 2 行转发。

.DESCRIPTION
    判据载体 = EnableMent\*.bat 的文本结构（去注释/空行后的有效行 + 禁用 token；禁执行者叙述）：
      P1 入口脚本数 ≥ 基线（正对照；防扫描面坍缩）
      K1 每条入口有效行数 ≤ MaxEffectiveLines，且不含 if/set/pause/chcp/call/for
      K2 每条入口须引用唯一入口 Invoke-Frame.ps1
      K3 每条入口须显式声明 -Product（契约绑定，禁隐式产品）
    退出码：0 无违规 / 1 违规 / 2 前置缺失。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name LaunchEntrypoint
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..\..'))

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  启动入口守卫（LaunchEntrypoint / K1 入口零逻辑）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

$entryDir = Join-Path $repoRoot ([string]$baseline.EntryDirRelative.Value)
if (-not (Test-Path -LiteralPath $entryDir)) { Write-Host "  [FAIL] 入口目录缺失: $entryDir" -ForegroundColor Red; exit 2 }

$bats = @(Get-ChildItem -LiteralPath $entryDir -Filter '*.bat' -File -ErrorAction SilentlyContinue)
$violations = [System.Collections.Generic.List[string]]::new()

$minEntries = [int]$baseline.MinEntryScripts.Value
$maxLines = [int]$baseline.MaxEffectiveLines.Value
$forbidden = @($baseline.ForbiddenTokens.Value)
$required = [string]$baseline.RequiredPattern.Value
$productArg = [string]$baseline.RequiredProductArg.Value

Write-Host ("  入口脚本: {0} 个（正对照下限 {1}，有效行上限 {2}）" -f $bats.Count, $minEntries, $maxLines) -ForegroundColor Gray
if ($bats.Count -lt $minEntries) {
    $violations.Add(("[P1] 入口脚本数 {0} < 基线 {1}（禁判绿）" -f $bats.Count, $minEntries))
}

foreach ($f in $bats) {
    $lines = @(Get-Content -LiteralPath $f.FullName -Encoding UTF8)
    $effective = @($lines | Where-Object {
            $t = $_.Trim()
            ($t.Length -gt 0) -and
            (-not $t.StartsWith('::')) -and
            ($t -notmatch '^(?i)@?rem(\s|$)')
        })

    if ($effective.Count -gt $maxLines) {
        $violations.Add(("[K1] {0}: 有效行 {1} > 上限 {2}（入口只允许转发，禁止控制流）" -f $f.Name, $effective.Count, $maxLines))
    }

    $body = ($effective -join "`n")
    foreach ($token in $forbidden) {
        if ($body -match ("(?im)(^|\s){0}(\s|$|[.(])" -f [regex]::Escape($token))) {
            $violations.Add(("[K1] {0}: 含禁用 token '{1}'（解析事故面：交给执行体，不留在 bat）" -f $f.Name, $token))
        }
    }

    if ($body -notmatch [regex]::Escape($required)) {
        $violations.Add(("[K2] {0}: 未引用唯一入口 {1}" -f $f.Name, $required))
    }
    if (-not [string]::IsNullOrWhiteSpace($productArg) -and $body -notmatch ('{0}\s+\S+' -f [regex]::Escape($productArg))) {
        $violations.Add(("[K3] {0}: 未显式声明 {1}（产品绑定须在命令行）" -f $f.Name, $productArg))
    }
}

Write-Host ''
if ($violations.Count -gt 0) {
    Write-Host ("  [FAIL] 违规 {0} 项" -f $violations.Count) -ForegroundColor Red
    foreach ($v in $violations) { Write-Host ("         {0}" -f $v) -ForegroundColor DarkRed }
    if ($Check) { exit 1 }
    exit 0
}
Write-Host '  [OK] 启动入口零逻辑（2 行转发，无控制流/编码/pause）' -ForegroundColor Green
exit 0
