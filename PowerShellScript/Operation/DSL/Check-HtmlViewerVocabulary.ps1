#requires -Version 5.1
<#
.SYNOPSIS
    HtmlViewer 结构词汇守卫 —— 框架内嵌模板键 / 类名 / region 锚零领域语义（防语义回渗）。

.DESCRIPTION
    权威: CsharpPrj\Plate\HtmlViewer\doc\HTML模板去领域语义-通用化重构方案.md（§2 正/负词表、§5 门禁）。
    语义分层：框架（Plate.HtmlViewer）只描述结构（壳 / 分栏 / 面板 / 列表 / 项 / 行 / 分组 / 折叠 / 分段 / 状态）；
    语义（history / task / node / session / message / ...）只能出现在 ①配置 JSON 键值 ②Agent 数据模型 ③域样式注入。

    扫描面（三载体；只提取「结构名词」，不扫 C# 标识符）：
      S1  Frame\CsharpPrj\Plate\HtmlViewer\Html\Assets\HtmlAssetRegistry*.cs
          → 模板键 / 模板值内 class 与片段引用 / CSS 选择器类名 / region 锚
      S2  Frame\CsharpPrj\Operation\FrontEndDriven\HtmlViewerBridge\HtmlStreamRenderer.cs
          → 模板键字面量（round / operationPanel 等真消费点）
      S3  Projects\Plug\Agent\src\Agent.Ui\Ui\Views\ConversationPageRenderer.cs
          → 模板键字面量 + region 键字面量

    判据：
      C1 载体非空（防判据空转）：模板键 ≥ 15 · 类名 ≥ 5 · region 锚 ≥ 5。
      C2 负词表（camelCase / 连字符 / 下划线分片后逐段匹配）命中即 Error。
      C3 region 锚后缀须 ∈ 正词表 {aside,main,seg,head,subhead,overlay}。
      C4 负对照（-NegativeControl）：合成违规样本必须被同一判据命中。

    基线：Baselines\HtmlViewerVocabulary.psd1（AllowCount=0 ⇒ 零豁免；Allow 表为后续显式登记出口）。
    模式：默认报告；-Check 有违规 → 退出码 1。只读仓库，零侧写。
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
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

# 单源基线（缺件即抛：禁隐式空基线）
Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name HtmlViewerVocabulary
$allowCount = [int]$baseline.AllowCount.Value
$allowWords = @()
if ($baseline.Contains('Allow')) { $allowWords = @($baseline.Allow.Keys) }

# 负词表（领域语义；权威 = 方案 §2.2）
$negativeWords = @(
    'history', 'task', 'node', 'conversation', 'conv', 'session', 'agent', 'prompt', 'actor', 'pool',
    'db', 'payload', 'round', 'llm', 'ai', 'chat', 'message', 'bubble', 'tool', 'skill', 'runner', 'workflow'
)
# region 锚正词表（权威 = 方案 §2.3(d)）
$regionPositive = @('aside', 'main', 'seg', 'head', 'subhead', 'overlay')

$rxTemplateKey = '\[\s*"([A-Za-z][A-Za-z0-9_]*)"\s*\]\s*=\s*"""'
$rxModuleKey = '\[\s*"([A-Za-z][A-Za-z0-9_]*)"\s*\]\s*=\s*Css'
$rxClassBody = 'class=\\?"([^"{}]*)"'
$rxFragment = '\{>([A-Za-z][A-Za-z0-9_]*)\}'
$rxCssClass = '\.([a-z][a-z0-9-]*)(?![A-Za-z0-9_])'
$rxRegionLit = '"region-([a-z0-9-]+)"'
$rxRegionConcat = 'RegionNamePrefix\s*\+\s*"([a-z0-9-]+)"'
$rxRenderKey = 'Render\(\s*[^,]*,\s*"([A-Za-z][A-Za-z0-9_]*)"'
$rxTryKey = 'TryGetValue\(\s*"([A-Za-z][A-Za-z0-9_]*)"'
$rxRegionProp = '\["([a-z][a-z0-9-]*)"\]\s*=\s*p\.'

function Get-StrippedText([string]$text)
{
    # 跳过注释（C# 行注 / XML doc / 块注）——R6：注释内出现领域词不算违规
    $t = [regex]::Replace($text, '(?s)/\*.*?\*/', ' ')
    $t = [regex]::Replace($t, '(?m)(?<!:)//.*$', ' ')
    return $t
}

function Split-StructuralToken([string]$token)
{
    $out = [System.Collections.Generic.List[string]]::new()
    foreach ($p in [regex]::Split($token, '[^A-Za-z0-9]+'))
    {
        if (-not $p) { continue }
        foreach ($s in [regex]::Split($p, '(?<=[a-z0-9])(?=[A-Z])'))
        {
            if ($s) { $out.Add($s.ToLowerInvariant()) }
        }
    }
    return $out
}

function Get-WordViolations([string]$label, $tokens)
{
    $v = [System.Collections.Generic.List[string]]::new()
    foreach ($tk in @($tokens))
    {
        foreach ($seg in (Split-StructuralToken $tk))
        {
            if ($allowWords -contains $seg) { continue }
            if ($negativeWords -contains $seg) { $v.Add("$label :: 负词 '$seg'（载体 '$tk'）") }
        }
    }
    return $v
}


$violations = [System.Collections.Generic.List[string]]::new()
$keyTokens = [System.Collections.Generic.List[string]]::new()
$classTokens = [System.Collections.Generic.List[string]]::new()
$regionTokens = [System.Collections.Generic.List[string]]::new()

# ---- S1：框架资产（模板键 / 类名 / 片段引用 / CSS 选择器 / region 锚）----
$assetsDir = Join-Path $Root 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Assets'
if (-not (Test-Path -LiteralPath $assetsDir)) { Write-Error "未找到框架资产目录: $assetsDir"; exit 2 }
$assetFiles = @(Get-ChildItem -LiteralPath $assetsDir -Filter 'HtmlAssetRegistry*.cs' -File)
if ($assetFiles.Count -eq 0) { Write-Error "框架资产目录内无 HtmlAssetRegistry*.cs: $assetsDir"; exit 2 }

foreach ($f in $assetFiles)
{
    $rel = $f.FullName.Substring($Root.Length).TrimStart('\')
    $txt = Get-StrippedText (Get-Content -LiteralPath $f.FullName -Raw)

    $keys = [System.Collections.Generic.List[string]]::new()
    foreach ($m in [regex]::Matches($txt, $rxTemplateKey)) { $keys.Add($m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($txt, $rxModuleKey)) { $keys.Add($m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($txt, $rxFragment)) { $keys.Add($m.Groups[1].Value) }

    $classes = [System.Collections.Generic.List[string]]::new()
    foreach ($m in [regex]::Matches($txt, $rxClassBody))
    {
        foreach ($cls in $m.Groups[1].Value.Split(' ', [System.StringSplitOptions]::RemoveEmptyEntries))
        {
            if ($cls -match '[{}]') { continue }
            $classes.Add($cls)
        }
    }
    foreach ($m in [regex]::Matches($txt, $rxCssClass)) { $classes.Add($m.Groups[1].Value) }

    $regions = [System.Collections.Generic.List[string]]::new()
    foreach ($m in [regex]::Matches($txt, $rxRegionLit)) { $regions.Add($m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($txt, $rxRegionConcat)) { $regions.Add($m.Groups[1].Value) }

    $keyTokens.AddRange($keys)
    $classTokens.AddRange($classes)
    $regionTokens.AddRange($regions)

    foreach ($v in (Get-WordViolations "$rel :: template-key" $keys)) { $violations.Add($v) }
    foreach ($v in (Get-WordViolations "$rel :: class" $classes)) { $violations.Add($v) }
    foreach ($v in (Get-WordViolations "$rel :: region" $regions)) { $violations.Add($v) }
}

# ---- S2/S3：模板键消费点（键字面量 + region 键字面量）----
$consumerSpecs = @(
    [pscustomobject]@{ Path = 'Frame\CsharpPrj\Operation\FrontEndDriven\HtmlViewerBridge\HtmlStreamRenderer.cs'; Region = $false }
    [pscustomobject]@{ Path = 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\ConversationPageRenderer.cs'; Region = $true }
)
foreach ($spec in $consumerSpecs)
{
    $full = Join-Path $Root $spec.Path
    if (-not (Test-Path -LiteralPath $full)) { $violations.Add("消费点缺件: $($spec.Path)"); continue }
    $txt = Get-StrippedText (Get-Content -LiteralPath $full -Raw)

    $keys = [System.Collections.Generic.List[string]]::new()
    foreach ($m in [regex]::Matches($txt, $rxRenderKey)) { $keys.Add($m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($txt, $rxTryKey)) { $keys.Add($m.Groups[1].Value) }
    $keyTokens.AddRange($keys)
    foreach ($v in (Get-WordViolations "$($spec.Path) :: template-key" $keys)) { $violations.Add($v) }

    if ($spec.Region)
    {
        $regions = [System.Collections.Generic.List[string]]::new()
        foreach ($m in [regex]::Matches($txt, $rxRegionProp)) { $regions.Add($m.Groups[1].Value) }
        $regionTokens.AddRange($regions)
        foreach ($v in (Get-WordViolations "$($spec.Path) :: region-key" $regions)) { $violations.Add($v) }
    }
}

# ---- C3：region 锚后缀白名单（正词表）----
foreach ($sfx in ($regionTokens | Sort-Object -Unique))
{
    if ($regionPositive -notcontains $sfx)
    {
        $violations.Add("region 锚后缀 '$sfx' ∉ 正词表（$($regionPositive -join '/')）")
    }
}

# ---- C1：载体非空（防判据空转）----
$keyCount = @($keyTokens | Sort-Object -Unique).Count
$classCount = @($classTokens | Sort-Object -Unique).Count
$regionCount = @($regionTokens | Sort-Object -Unique).Count
if ($keyCount -lt 15) { $violations.Add("[C1] 模板键载体仅 $keyCount 个（< 15）⇒ 提取判据失效（防假绿）") }
if ($classCount -lt 5) { $violations.Add("[C1] 类名载体仅 $classCount 个（< 5）⇒ 提取判据失效（防假绿）") }
if ($regionCount -lt 5) { $violations.Add("[C1] region 锚仅 $regionCount 个（< 5）⇒ 提取判据失效（防假绿）") }

Write-Host '=== [HtmlViewerVocabulary] 结构词汇守卫（框架零领域语义） ==='
Write-Host ("扫描面: S1 资产 {0} 件 · S2/S3 消费点 {1} 件" -f $assetFiles.Count, $consumerSpecs.Count)
Write-Host ("载体唯一计数: 模板键={0} · 类名={1} · region 锚={2} · 基线 AllowCount={3}" -f `
    $keyCount, $classCount, $regionCount, $allowCount)

# ---- C4：负对照（防假绿）----
if ($NegativeControl)
{
    $synthetic = @('task-row', 'taskRuntimePanel', 'region-history', 'blockItem')
    $negHits = @(Get-WordViolations '<synthetic> :: negative-control' $synthetic)
    if ($negHits.Count -eq 0)
    {
        Write-Host '[HtmlViewerVocabulary][FAIL] 负对照未命中：守卫失效（假绿）' -ForegroundColor Red
        exit 1
    }
    Write-Host ("[HtmlViewerVocabulary] 负对照命中 {0} 项（守卫有效）" -f $negHits.Count) -ForegroundColor Green
}

if ($violations.Count -gt 0)
{
    $uniq = @($violations | Sort-Object -Unique)
    Write-Host ("[HtmlViewerVocabulary][FAIL] 违规 {0} 项：" -f $uniq.Count) -ForegroundColor Red
    $uniq | ForEach-Object { Write-Host ("    {0}" -f $_) -ForegroundColor Red }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[HtmlViewerVocabulary][OK] 模板键 / 类名 / region 锚零领域语义（正对照命中 0）' -ForegroundColor Green
exit 0

