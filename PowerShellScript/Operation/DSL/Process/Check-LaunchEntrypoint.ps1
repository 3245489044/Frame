<#
.SYNOPSIS
    启动入口门禁（Gate=LaunchEntrypoint）—— 外部 bat 一律薄转发到 Invoke-Frame -Action launch。
.DESCRIPTION
    判据载体 = EnableMent\*.bat 的文本结构（禁执行者叙述）。
      P1 入口脚本数 ≥ 基线（正对照；防扫描面坍缩）
      P2 禁直呼 FileDriven.Launcher.exe / 运行测试GUI模式.ps1（两条链各自定义成功 ⇒ ANTI-DUAL-PATH）
      P3 每个入口脚本须引用 Invoke-Frame.ps1（唯一入口）
    退出码：0 无违规 / 1 违规 / 2 前置缺失。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'
$baselinePath = Join-Path $PSScriptRoot '..\..\..\Baselines\Meta\LaunchEntrypoint.psd1'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..\..'))

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  启动入口守卫（LaunchEntrypoint）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $baselinePath)) { Write-Host "  [FAIL] 基线缺失: $baselinePath" -ForegroundColor Red; exit 2 }
$baseline = Import-PowerShellDataFile -LiteralPath $baselinePath

$entryDir = Join-Path $repoRoot ([string]$baseline.EntryDirRelative.Value)
if (-not (Test-Path -LiteralPath $entryDir)) { Write-Host "  [FAIL] 入口目录缺失: $entryDir" -ForegroundColor Red; exit 2 }

$bats = @(Get-ChildItem -LiteralPath $entryDir -Filter '*.bat' -File -ErrorAction SilentlyContinue)
$violations = New-Object System.Collections.Generic.List[string]

$minEntries = [int]$baseline.MinEntryScripts.Value
Write-Host ("  入口脚本: {0} 个（正对照下限 {1}）" -f $bats.Count, $minEntries) -ForegroundColor Gray
if ($bats.Count -lt $minEntries) {
    $violations.Add(("[P1] 入口脚本数 {0} < 基线 {1}（禁判绿）" -f $bats.Count, $minEntries))
}

$forbidden = @($baseline.ForbiddenPatterns.Value)
$required = [string]$baseline.RequiredPattern.Value

foreach ($f in $bats) {
    $text = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8
    foreach ($pat in $forbidden) {
        if ($text -like ("*{0}*" -f $pat)) {
            $violations.Add(("[P2] {0}: 直呼 '{1}'（应经 Invoke-Frame.ps1 -Action launch）" -f $f.Name, $pat))
        }
    }
    if ($text -notlike ("*{0}*" -f $required)) {
        $violations.Add(("[P3] {0}: 未引用唯一入口 {1}" -f $f.Name, $required))
    }
}

Write-Host ''
if ($violations.Count -gt 0) {
    Write-Host ("  [FAIL] 违规 {0} 项" -f $violations.Count) -ForegroundColor Red
    foreach ($v in $violations) { Write-Host ("         {0}" -f $v) -ForegroundColor DarkRed }
    if ($Check) { exit 1 }
    exit 0
}
Write-Host '  [OK] 启动入口唯一（全部经 Invoke-Frame.ps1）' -ForegroundColor Green
exit 0
