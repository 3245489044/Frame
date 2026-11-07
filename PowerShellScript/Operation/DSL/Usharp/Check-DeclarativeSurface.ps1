#requires -Version 5.1
<#
.SYNOPSIS
    DeclarativeSurface 守卫 —— 呈现策略（模板键 / 折叠词 / 面序 / 兜底模板 / 必需键 / 诊断码 / 标签）
    只允许存在于声明面，生产代码内策略字面量 = 0。

.DESCRIPTION
    权威：Docs\analysis\2026\10\10.31\2026-11-01-框架声明式收敛-散布逻辑清单与实施方案.md（S2/S3/S4/S5/S9/S10/S11/S12）
          与 2026-11-01-框架呈现与折叠-重复冗余错误逻辑-重构方案.md（F1/F2）。

    判据（结构事实，禁执行者叙述）：
      D1 声明在册：presentation.regions.json / fold.json / diagnostics.json / labels.json 存在且 schema 匹配；
                    defaults.template 非空；templates.required 非空；fold.modes 非空。
      D2 策略字面量 = 0：载体文件内模板键 / 折叠词 / 面名 字面量命中数 ≤ 基线 AllowCount（=0）。
      D3 诊断码在册：载体代码的诊断码常量与降级码前缀必须在 diagnostics.json 声明。
      D4 事实字段奇偶：read-paths.json 的 facts 必须覆盖 FrameFacts 的 terminal/archived/presentation。
      D5 载体非空：声明区域条目 ≥ 8（防判据空转）。
      D6 负对照：合成样本（含 processRow / round-closed）必须被同一判据命中。

    模式：默认报告；-Check 违规 → 退出码 1；-NegativeControl 额外自证。只读仓库，零侧写。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name DeclarativeSurface
$allowCount = [int]$baseline.AllowCount.Value

$violations = [System.Collections.Generic.List[string]]::new()

$regionsPath = Join-Path $Root 'Projects\Plug\Agent\Config\ui\presentation.regions.json'
$foldPath = Join-Path $Root 'Projects\Plug\Agent\Config\ui\fold.json'
$diagnosticsPath = Join-Path $Root 'Projects\Plug\Agent\Config\ui\diagnostics.json'
$labelsPath = Join-Path $Root 'Projects\Plug\Agent\Config\ui\labels.json'
$readPathsPath = Join-Path $Root 'Projects\Plug\Agent\Config\nodes\read-paths.json'

# ---- D1 声明在册 ----
foreach ($decl in @(
    @{ Path = $regionsPath; Name = 'presentation.regions.json' },
    @{ Path = $foldPath; Name = 'fold.json' },
    @{ Path = $diagnosticsPath; Name = 'diagnostics.json' },
    @{ Path = $labelsPath; Name = 'labels.json' }))
{
    if (-not (Test-Path -LiteralPath $decl.Path))
    {
        $violations.Add("[D1] 声明缺件：$($decl.Name)")
    }
}

$regionCount = 0
$requiredCount = 0
if (Test-Path -LiteralPath $regionsPath)
{
    $regions = Get-Content -LiteralPath $regionsPath -Raw | ConvertFrom-Json
    if ($regions.schema -ne 'presentation-declaration/v1')
    {
        $violations.Add("[D1] presentation.regions.json schema 非法：$($regions.schema)")
    }
    $regionCount = @($regions.regions.PSObject.Properties).Count
    $requiredCount = @($regions.templates.required).Count
    if ([string]::IsNullOrWhiteSpace([string]$regions.defaults.template))
    {
        $violations.Add('[D1] presentation.regions.json defaults.template 为空（禁代码兜底）')
    }
    if ($requiredCount -eq 0)
    {
        $violations.Add('[D1] presentation.regions.json templates.required 为空')
    }
    if (@($regions.surfaces).Count -eq 0)
    {
        $violations.Add('[D1] presentation.regions.json surfaces 为空（面序须声明）')
    }
}

$foldModeCount = 0
if (Test-Path -LiteralPath $foldPath)
{
    $fold = Get-Content -LiteralPath $foldPath -Raw | ConvertFrom-Json
    if ($fold.schema -ne 'presentation-declaration/v1')
    {
        $violations.Add("[D1] fold.json schema 非法：$($fold.schema)")
    }
    $foldModeCount = @($fold.modes.PSObject.Properties).Count
    if ($foldModeCount -eq 0) { $violations.Add('[D1] fold.json modes 为空') }
    if ([string]::IsNullOrWhiteSpace([string]$fold.default)) { $violations.Add('[D1] fold.json default 为空') }
}

$declaredCodes = @()
if (Test-Path -LiteralPath $diagnosticsPath)
{
    $diagnostics = Get-Content -LiteralPath $diagnosticsPath -Raw | ConvertFrom-Json
    $declaredCodes = @($diagnostics.codes.PSObject.Properties | ForEach-Object { $_.Name })
}

# ---- D2 策略字面量 ----
$carriers = @(
    'Frame\CsharpPrj\Operation\FrontEndDriven\HtmlViewerBridge\TemplateSurface.cs',
    'Frame\CsharpPrj\Operation\FrontEndDriven\HtmlViewerBridge\HtmlStreamRenderer.cs',
    'Frame\CsharpPrj\Operation\FrontEndDriven\HtmlViewerBridge\Projection\RowModels.cs',
    'Frame\CsharpPrj\Share\Frameworks\Conversation\Rendering\FoldPolicy.cs',
    'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Rendering\ConversationRendererFactory.cs',
    'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Rendering\ConversationTemplates.cs',
    'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Rendering\ConversationFoldPolicyCatalog.cs',
    'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Rendering\ObservabilityAttachClassification.cs'
)

# 策略字面量（模板键 / 折叠词 / 面名 / stage 前缀）——一处命中即违规
$strategyLiterals = @(
    'roundFold', 'leadFold', 'sectionHeader', 'bubbleRow', 'processRow', 'noticeRow', 'operationRow',
    'attachItem', 'actionPanel', 'renderDegraded', 'emptyP',
    'round-closed', 'round-open', 'panel-open',
    'headPrompt', 'prompt:'
)

function Get-StrippedText([string]$text)
{
    $t = [regex]::Replace($text, '(?s)/\*.*?\*/', ' ')
    $t = [regex]::Replace($t, '(?m)(?<!:)//.*$', ' ')
    return $t
}

function Get-StrategyHits([string]$label, [string]$text)
{
    $hits = [System.Collections.Generic.List[string]]::new()
    $stripped = Get-StrippedText $text
    foreach ($literal in $strategyLiterals)
    {
        if ($stripped.Contains('"' + $literal + '"') -or $stripped.Contains("'" + $literal + "'"))
        {
            $hits.Add("$label :: 策略字面量 '$literal'")
        }
    }
    return $hits
}

foreach ($rel in $carriers)
{
    $full = Join-Path $Root $rel
    if (-not (Test-Path -LiteralPath $full))
    {
        $violations.Add("[D2] 载体缺件：$rel")
        continue
    }

    foreach ($hit in (Get-StrategyHits $rel (Get-Content -LiteralPath $full -Raw)))
    {
        $violations.Add("[D2] $hit")
    }
}

# ---- D3 诊断码在册 ----
foreach ($rel in $carriers)
{
    $full = Join-Path $Root $rel
    if (-not (Test-Path -LiteralPath $full)) { continue }
    $text = Get-StrippedText (Get-Content -LiteralPath $full -Raw)

    foreach ($m in [regex]::Matches($text, 'const\s+string\s+\w+Code\s*=\s*"([a-z0-9-]+)"'))
    {
        $code = $m.Groups[1].Value
        if ($declaredCodes -notcontains $code)
        {
            $violations.Add("[D3] $rel :: 诊断码 '$code' 未在 diagnostics.json 声明")
        }
    }

    foreach ($m in [regex]::Matches($text, 'Degraded\(\s*"([a-z0-9-]+)'))
    {
        $code = $m.Groups[1].Value
        if ($declaredCodes -notcontains $code)
        {
            $violations.Add("[D3] $rel :: 降级码 '$code' 未在 diagnostics.json 声明")
        }
    }
}

# ---- D4 事实字段奇偶（read-paths.json vs FrameFacts）----
if (Test-Path -LiteralPath $readPathsPath)
{
    $readPaths = Get-Content -LiteralPath $readPathsPath -Raw | ConvertFrom-Json
    $declaredFacts = @($readPaths.paths | ForEach-Object { $_.facts } | Where-Object { $_ } | Sort-Object -Unique)
    foreach ($fact in @('terminal', 'archived', 'presentation'))
    {
        if ($declaredFacts -notcontains $fact)
        {
            $violations.Add("[D4] read-paths.json 未声明事实字段 '$fact'（FrameFacts 消费）")
        }
    }
}
else
{
    $violations.Add('[D4] read-paths.json 缺件')
}

# ---- D5 载体非空 ----
if ($regionCount -lt 8) { $violations.Add("[D5] 声明区域仅 $regionCount 条（< 8）⇒ 判据空转") }
if ($foldModeCount -lt 1) { $violations.Add('[D5] 折叠词表空 ⇒ 判据空转') }

Write-Host '=== [DeclarativeSurface] 呈现策略声明化守卫 ==='
Write-Host ("载体 {0} 件 · 区域 {1} · 必需键 {2} · 折叠词 {3} · 诊断码 {4} · 基线 AllowCount={5}" -f `
    $carriers.Count, $regionCount, $requiredCount, $foldModeCount, @($declaredCodes).Count, $allowCount)

# ---- D6 负对照（防假绿）----
if ($NegativeControl)
{
    $synthetic = 'var key = "processRow"; var word = "round-closed";'
    $negHits = @(Get-StrategyHits '<synthetic>' $synthetic)
    if ($negHits.Count -eq 0)
    {
        Write-Host '[DeclarativeSurface][FAIL] 负对照未命中：守卫失效（假绿）' -ForegroundColor Red
        exit 1
    }
    Write-Host ("[DeclarativeSurface] 负对照命中 {0} 项（守卫有效）" -f $negHits.Count) -ForegroundColor Green
}

if ($violations.Count -gt 0)
{
    $uniq = @($violations | Sort-Object -Unique)
    Write-Host ("[DeclarativeSurface][FAIL] 违规 {0} 项：" -f $uniq.Count) -ForegroundColor Red
    $uniq | ForEach-Object { Write-Host ("    {0}" -f $_) -ForegroundColor Red }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[DeclarativeSurface][OK] 呈现策略全部在声明面（生产代码策略字面量 = 0）' -ForegroundColor Green
exit 0
