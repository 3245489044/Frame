#requires -Version 5.1
<#
.SYNOPSIS
    双原语守卫（BulkThreshold）：存在 changeRatio 计算与 BulkReplace 分支，阈值来自配置。

.DESCRIPTION
    判据：
      K1 基线声明层全部进判据面（策略 / 选项 / 写入口 / JS 原语 / DomOps 常量）；
      K2 BulkThresholdPolicy 含键集合差与 BulkReplace；
      K3 JS 侧 __uiReconcile.bulk 在位（缺失即落地断链）；
      K4 负对照：只有 Patch 的实现样本必须判违规（防判据恒真）。
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
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Detail:$ShowDetail -Key 'BulkThreshold'
$Root = $ctx.Root
$violations = $ctx.Violations

function Get-MissingTokens {
    param([string]$Text, [string[]]$Must)
    return @($Must | Where-Object { $Text.IndexOf($_, [StringComparison]::Ordinal) -lt 0 })
}

$violations = [System.Collections.Generic.List[string]]::new()
Write-Host '=== [BulkThreshold] 双原语（Patch / BulkReplace） ==='

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name BulkThreshold
$layers = @($baseline.BulkContract.Value)
if ($layers.Count -eq 0) { $violations.Add('[K1] 基线缺 BulkContract（判据无标的）') }

foreach ($layer in $layers) {
    $rel = [string]$layer.File
    $abs = Join-Path $Root $rel
    if (-not (Test-Path -LiteralPath $abs)) { $violations.Add("[K2] 层文件不在位：$rel"); continue }
    $text = Get-Content -LiteralPath $abs -Raw -Encoding UTF8
    foreach ($m in (Get-MissingTokens -Text $text -Must @($layer.Must))) {
        $violations.Add("[K2] 缺结构事实「$m」：$rel")
    }
    if ($ShowDetail) { Write-Host "  [K2] $rel 已核（$(@($layer.Must).Count) 项）" -ForegroundColor DarkGray }
}

# K4 负对照：只有 Patch 的原语实现必须判缺
$neg = "function patch(host,html){reconcileChildren(host,html);return 'OK';}"
$negMissing = Get-MissingTokens -Text $neg -Must @('function bulk', 'ChangeRatio')
if ($negMissing.Count -eq 0) { $violations.Add('[K4] 负对照失效：只有 Patch 的样本未被判缺') }

if ($violations.Count -eq 0) {
    Write-Host '[OK] BulkThreshold：双原语 + 阈值配置化在位；负对照绿。' -ForegroundColor Green
    exit 0
}
Write-Host ("[FAIL] BulkThreshold 命中 {0} 处：" -f $violations.Count) -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host ("  - {0}" -f $v) -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
