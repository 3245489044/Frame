#requires -Version 5.1
<#
.SYNOPSIS
    未知可见化守卫（UnknownVisible）—— 「禁静默出口 U」的机器判据（框架建面）。

.DESCRIPTION
    背景（Docs\analysis\2026\10\10.17\2026-10-17-未知可见化框架-禁静默省略-通用设计与施工单.md）：
    缺值被「判空即隐藏」静默吞掉，调用方无从区分「未知」与「不适用」；
    本门禁把「缺值显式占位（U 出口）」由纪律变为机器判据，与 Check-FallbackInventory（T/N/D 回退出口）互补。

    判据：
      G1 载体在册：Observability\unknown_tokens.json 存在 ∧ tokens 含 unknown/dash ∧
         UnknownTokens 门面定义（Unknown / Dash / For( 各 ≥1）∧ 已登记为 EmbeddedResource（csproj）。
      G2 身份维度判空隐藏：DefaultTemplates 内 {?actor} / {?subagent} / {?targets} 计数
         —— **报告态**（只列不判）；呈现层接线（模板去 {?} 包裹 + 供数侧占位）后转强制（届时纳入基线）。
      G3 正/负对照（判据自证）：合成样本 {?actor}<span>{actor}</span>{/} 必被判据命中；
         合成样本 <span>{actor}</span>（无条件绑定）必不被命中（防判据恒真/恒假）。

    模式：默认报告模式（列实测，不设退出码）；-Check 校验模式（G1/G3 违规 → 退出码 1）。只读仓库，零侧写。

.PARAMETER Root
    仓库超根（缺省 = 本脚本上溯五级；含 Frame\ 与 Projects\ 两个子模块）。

.PARAMETER Check
    校验模式：存在违规 → 退出码 1。

.PARAMETER ShowDetail
    输出逐条命中（G2 命中的 {?…} 片段）。

.EXAMPLE
    .\Check-UnknownVisible.ps1
.EXAMPLE
    .\Check-UnknownVisible.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================'
Write-Host '  未知可见化守卫（UnknownVisible：禁静默出口 U）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$contractDir = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Contract\Observability'
$tokenJson   = Join-Path $contractDir 'unknown_tokens.json'
$facadeFile  = Join-Path $contractDir 'UnknownTokens.cs'
$csproj      = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Contract\Agent.Contract.csproj'
$tplFile     = Join-Path $Root 'Frame\CsharpPrj\Plate\HtmlViewer\Assets\templates.default.json'

# ---------------- G1 载体在册 ----------------
if (-not (Test-Path -LiteralPath $tokenJson)) {
    $violations.Add("[G1] 缺件（判据不可判）：$tokenJson")
} else {
    $raw = [System.IO.File]::ReadAllText($tokenJson)
    if ((Get-Count $raw '"unknown"\s*:') -lt 1 -or (Get-Count $raw '"dash"\s*:') -lt 1) {
        $violations.Add('[G1] unknown_tokens.json 缺 tokens（unknown/dash 占位形态单源不全）')
    }
}
if (-not (Test-Path -LiteralPath $facadeFile)) {
    $violations.Add("[G1] 缺件（判据不可判）：$facadeFile")
} else {
    $facade = [System.IO.File]::ReadAllText($facadeFile)
    if ((Get-Count $facade 'static\s+string\s+Unknown\s*=>') -lt 1 -or
        (Get-Count $facade 'static\s+string\s+Dash\s*=>') -lt 1 -or
        (Get-Count $facade 'static\s+string\s+For\s*\(') -lt 1) {
        $violations.Add('[G1] UnknownTokens 门面不全（须具 Unknown / Dash / For( 出入口）')
    }
}
if (Test-Path -LiteralPath $csproj) {
    $proj = [System.IO.File]::ReadAllText($csproj)
    if ((Get-Count $proj 'EmbeddedResource\s+Include="Observability\\unknown_tokens\.json"') -lt 1) {
        $violations.Add('[G1] unknown_tokens.json 未登记为 EmbeddedResource（Agent.Contract.csproj）')
    }
} else {
    $violations.Add("[G1] 缺件（判据不可判）：$csproj")
}

# ---------------- G2 身份维度判空隐藏（报告态） ----------------
$hideRx = '\{\?(actor|subagent|targets)\}'
$hideCount = 0
if (Test-Path -LiteralPath $tplFile) {
    $hideCount = Get-Count ([System.IO.File]::ReadAllText($tplFile)) $hideRx
}

# ---------------- G3 正/负对照（判据自证） ----------------
$posSample = '{?actor}<span class="chip">{actor}</span>{/}'
$negSample = '<span class="chip">{actor}</span>'
if ((Get-Count $posSample $hideRx) -lt 1) {
    $violations.Add('[G3] 正对照失效：判空隐藏样本未被捕获（正则被改坏）')
}
if ((Get-Count $negSample $hideRx) -ne 0) {
    $violations.Add('[G3] 负对照失效：无条件绑定样本被误判为判空隐藏（正则过宽）')
}

# ---------------- 报告 ----------------
$tplRel = if ($tplFile.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)) { $tplFile.Substring($Root.Length).TrimStart('\') } else { $tplFile }
Write-Host ("  实测：载体在册 {0} · 身份维度判空隐藏 {1} 处（{2}）" -f `
    $(if (@($violations | Where-Object { $_ -like '[G1]*' }).Count -gt 0) { '否' } else { '是' }), $hideCount, $tplRel) -ForegroundColor Gray
if ($hideCount -gt 0) {
    Write-Host ("  [REPORT] 身份维度（actor/subagent/targets）仍以 {{?}} 判空隐藏 {0} 处 —— 呈现层接线（M1）后转强制" -f $hideCount) -ForegroundColor Yellow
}
if ($ShowDetail -and (Test-Path -LiteralPath $tplFile)) {
    foreach ($m in [regex]::Matches(([System.IO.File]::ReadAllText($tplFile)), $hideRx)) {
        Write-Host ("    命中: {0}" -f $m.Value) -ForegroundColor DarkGray
    }
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1 载体在册 + G3 正/负对照通过（G2 报告态）' -ForegroundColor Green
    Write-Host ''
    exit 0
}
Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
