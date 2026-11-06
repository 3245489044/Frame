#requires -Version 5.1
<#
.SYNOPSIS
    遥测字段守卫（UiTelemetryFields）：每节 nodes/bytes/jsMs/outcome，每拍 batchNodes/lane/queueMs/droppedSections，msgId 已注册。

.DESCRIPTION
    判据：
      T1 基线声明层全部进判据面；
      T2 SectionTelemetry 节行含四字段、拍行含批次字段；
      T3 UiWriteMetrics 暴露 batchNodes/sectionsBulk/droppedSections；
      T4 日志 msgId 已注册（UiWriteMetrics / UiWriteSection）；
      T5 负对照：缺 jsMs 的节行样本必须判缺。
    模式：默认报告；-Check 违规 → 退出码 1。
#>
[CmdletBinding()]
param([string]$Root = '', [switch]$Check, [switch]$ShowDetail)

$ErrorActionPreference = 'Stop'

# 根定位（深度无关）：GateRuntime → DirectoryMap 以「Frame\ + Projects\」marker 上溯（迁 Environment 后不依赖脚本深度）
$__repoRoot = $PSScriptRoot
while ($__repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $__repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $__repoRoot 'Projects'))) { $__repoRoot = Split-Path -Parent $__repoRoot }
if (-not $__repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $__repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Detail:$ShowDetail -Key 'UiTelemetryFields'
$Root = $ctx.Root

function Get-MissingTokens {
    param([string]$Text, [string[]]$Must)
    return @($Must | Where-Object { $Text.IndexOf($_, [StringComparison]::Ordinal) -lt 0 })
}

$violations = [System.Collections.Generic.List[string]]::new()
Write-Host '=== [UiTelemetryFields] 节/拍遥测字段齐备 ==='

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name UiTelemetryFields
$layers = @($baseline.TelemetryContract.Value)
if ($layers.Count -eq 0) { $violations.Add('[T1] 基线缺 TelemetryContract（判据无标的）') }

foreach ($layer in $layers) {
    $rel = [string]$layer.File
    $abs = Join-Path $Root $rel
    if (-not (Test-Path -LiteralPath $abs)) { $violations.Add("[T2] 层文件不在位：$rel"); continue }
    $text = Get-Content -LiteralPath $abs -Raw -Encoding UTF8
    foreach ($m in (Get-MissingTokens -Text $text -Must @($layer.Must))) {
        $violations.Add("[T2] 缺结构事实「$m」：$rel")
    }
    if ($ShowDetail) { Write-Host "  [T2] $rel 已核（$(@($layer.Must).Count) 项）" -ForegroundColor DarkGray }
}

# T5 负对照：缺 jsMs 的节行样本必须判缺
$neg = 'section={id} nodes={n} bytes={b} outcome=applied'
$negMissing = Get-MissingTokens -Text $neg -Must @('nodes=', 'bytes=', 'jsMs=', 'outcome=')
if ($negMissing.Count -eq 0) { $violations.Add('[T5] 负对照失效：缺 jsMs 的样本未被判缺') }

if ($violations.Count -eq 0) {
    Write-Host '[OK] UiTelemetryFields：节/拍字段齐备 + msgId 已注册；负对照绿。' -ForegroundColor Green
    exit 0
}
Write-Host ("[FAIL] UiTelemetryFields 命中 {0} 处：" -f $violations.Count) -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host ("  - {0}" -f $v) -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
