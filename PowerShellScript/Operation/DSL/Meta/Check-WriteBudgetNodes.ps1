#requires -Version 5.1
<#
.SYNOPSIS
    预算维度守卫（WriteBudgetNodes）：预算以节点数为主（D4），选项/策略/写入口三处闭合。

.DESCRIPTION
    判据：
      B1 基线声明层全部进判据面；
      B2 选项含 MaxNodesPerBeat/MaxNodesPerBeatBackground；策略含 MaxNodes 与降级链；
      B3 写入口真实消费 MaxNodesPerBeat 并记降级原因（OnDropped）；
      B4 负对照：只有 ops 预算（无 nodes）的合成样本必须判缺。
    模式：默认报告；-Check 违规 → 退出码 1。
#>
[CmdletBinding()]
param([string]$Root = '', [switch]$Check, [switch]$ShowDetail)

$ErrorActionPreference = 'Stop'

# 根定位（深度无关）：GateRuntime → DirectoryMap 以「Frame\ + Projects\」marker 上溯（迁 Environment 后不依赖脚本深度）
$__repoRoot = $PSScriptRoot
while ($__repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $__repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $__repoRoot 'Projects')))) { $__repoRoot = Split-Path -Parent $__repoRoot }
if (-not $__repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $__repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Detail:$ShowDetail -Key 'WriteBudgetNodes'
$Root = $ctx.Root

function Get-MissingTokens {
    param([string]$Text, [string[]]$Must)
    return @($Must | Where-Object { $Text.IndexOf($_, [StringComparison]::Ordinal) -lt 0 })
}

$violations = [System.Collections.Generic.List[string]]::new()
Write-Host '=== [WriteBudgetNodes] 预算主维度 = 节点数 ==='

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name WriteBudgetNodes
$layers = @($baseline.BudgetContract.Value)
if ($layers.Count -eq 0) { $violations.Add('[B1] 基线缺 BudgetContract（判据无标的）') }

foreach ($layer in $layers) {
    $rel = [string]$layer.File
    $abs = Join-Path $Root $rel
    if (-not (Test-Path -LiteralPath $abs)) { $violations.Add("[B2] 层文件不在位：$rel"); continue }
    $text = Get-Content -LiteralPath $abs -Raw -Encoding UTF8
    foreach ($m in (Get-MissingTokens -Text $text -Must @($layer.Must))) {
        $violations.Add("[B2] 缺结构事实「$m」：$rel")
    }
    if ($ShowDetail) { Write-Host "  [B2] $rel 已核（$(@($layer.Must).Count) 项）" -ForegroundColor DarkGray }
}

$neg = 'public record Opt { public int MaxOpsPerFlush { get; init; } = 256; }'
$negMissing = Get-MissingTokens -Text $neg -Must @('MaxNodesPerBeat', 'MaxNodesPerBeatBackground')
if ($negMissing.Count -eq 0) { $violations.Add('[B4] 负对照失效：只有 ops 预算样本未被判缺') }

if ($violations.Count -eq 0) {
    Write-Host '[OK] WriteBudgetNodes：节点主预算三层闭合；负对照绿。' -ForegroundColor Green
    exit 0
}
Write-Host ("[FAIL] WriteBudgetNodes 命中 {0} 处：" -f $violations.Count) -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host ("  - {0}" -f $v) -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
