#requires -Version 5.1
<#
.SYNOPSIS
    写入单位守卫（SectionWrite）：L1 写入口只接受 SectionPlan[]，禁「按 region 逐目标产出写目标」的第二条路径。

.DESCRIPTION
    判据（结构事实：符号/分支在位，禁执行者叙述）：
      S1 基线声明的层全部进判据面（防「登记了却不判」）；
      S2 节批量写面在位：ISurfaceWriter.SubmitAsync + SurfaceWrite 消费预算/批量脚本；
      S3 写入口分支在位：PlanWrite 有 UseSectionWriter 且经 SectionPlanAdapter.From(...)；
      S4 负对照：缺 SubmitAsync/SectionPlan 的合成样本必须判缺（防判据恒真）。
    模式：默认报告（不设退出码）；-Check 违规 → 退出码 1。
#>
[CmdletBinding()]
param([string]$Root = '', [switch]$Check, [switch]$ShowDetail)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path }

function Get-MissingTokens {
    param([string]$Text, [string[]]$Must)
    return @($Must | Where-Object { $Text.IndexOf($_, [StringComparison]::Ordinal) -lt 0 })
}

$violations = [System.Collections.Generic.List[string]]::new()
Write-Host '=== [SectionWrite] 写入单位 = 节（SectionPlan[] 唯一写面） ==='

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name SectionWrite
$layers = @($baseline.SectionWriteContract.Value)
if ($layers.Count -eq 0) { $violations.Add('[S1] 基线缺 SectionWriteContract（判据无标的）') }

foreach ($layer in $layers) {
    $rel = [string]$layer.File
    $abs = Join-Path $Root $rel
    if (-not (Test-Path -LiteralPath $abs)) { $violations.Add("[S2] 层文件不在位：$rel"); continue }
    $text = Get-Content -LiteralPath $abs -Raw -Encoding UTF8
    foreach ($m in (Get-MissingTokens -Text $text -Must @($layer.Must))) {
        $violations.Add("[S2] 缺结构事实「$m」：$rel")
    }
    if ($ShowDetail) { Write-Host "  [S2] $rel 已核（$(@($layer.Must).Count) 项）" -ForegroundColor DarkGray }
}

# S4 负对照：只有旧逐目标写、没有节批量入口的样本必须判缺
$synthetic = 'public void Write(UiWritePlan plan){ foreach(var t in plan.Targets){ WriteCore(t); } }'
$negMissing = Get-MissingTokens -Text $synthetic -Must @('SubmitAsync', 'SectionPlan')
if ($negMissing.Count -eq 0) { $violations.Add('[S4] 负对照失效：缺节批量入口样本未被判缺（判据恒真）') }

if ($violations.Count -eq 0) {
    Write-Host '[OK] SectionWrite：节批量写面在位 + 写入口分支在位；负对照绿。' -ForegroundColor Green
    exit 0
}
Write-Host ("[FAIL] SectionWrite 命中 {0} 处：" -f $violations.Count) -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host ("  - {0}" -f $v) -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
