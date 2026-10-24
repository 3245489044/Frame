#requires -Version 5.1
<#
.SYNOPSIS
    UI 流原语闭合守卫（UiBudget）：Append/Trim 动词「契约声明 ⇔ 协调器实现 ⇔ JS 落地」三层齐备。

.DESCRIPTION
    根因（设计 §4.1 P3/P4）：无增量动词 ⇒ 所有非叶变化退化为整区/整页重写；无帧预算 ⇒ 事件产生速度
    直接等于 DOM 操作速度。本件先把「动词闭合」做成结构判据（B2 批的完成判据），预算阈值判据随后续批扩项。

    判据（结构事实：符号/分支在位，禁执行者叙述）：
      B1 契约层：UiWritePlan 声明 Append/Trim 种类 + UiStreamAppend/UiStreamTrim 载荷；
      B2 协调器层：HtmlBackendRuntime 存在 `case UiWriteKind.Append:` 与 `case UiWriteKind.Trim:` 分支；
      B3 原语层：DomOps 声明 Append/Trim 常量；
      B4 JS 层：HtmlAssetRegistry 注册 `append:streamAppend` 与 `trim:streamTrim`（缺失即落地断链）；
      B5 负对照：缺 `case UiWriteKind.Append:` 的合成样本必须判违规（防判据恒真）。
      B6 刷新分层：RegionRenderPipeline 的 Tick 唯一产地（AsTick）只允许 Leaf 动词；
      B7 气泡段流契约：方向枚举 / JS 方向分支 / 段键前缀 / 渲染器分段入口（四层）；
      B10 帧预算/指标/灰度开关：选项 + 指标 + 降级入口三载体结构事实；
      B8/B9/B11 负对照：合成「Tick 产树」「无方向分支」「缺开关」样本必须判违规。

    模式：默认报告（不设退出码）；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()
Write-Host '=== [UiBudget] UI 流原语闭合（Append/Trim 三层落地） ==='

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name UiBudget

# 层 → (文件, 必须出现的结构事实, 判据标签)
$layers = @(
    @{ Tag = 'B1'; File = 'Frame\CsharpPrj\Plate\HtmlViewer\Abstractions\UiWritePlan.cs';
       Must = @('Append,', 'Trim,', 'record UiStreamAppend', 'record UiStreamTrim', 'record UiStreamItem') },
    @{ Tag = 'B2'; File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\HtmlBackendRuntime.cs';
       Must = @('case UiWriteKind.Append:', 'case UiWriteKind.Trim:', 'WriteStreamCoreStatusAsync', 'WriteStreamAsync') },
    @{ Tag = 'B3'; File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\DomOps.cs';
       Must = @('Append = "append"', 'Trim = "trim"') },
    @{ Tag = 'B4'; File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Assets\HtmlAssetRegistry.cs';
       Must = @('append:streamAppend', 'trim:streamTrim', 'function streamAppend', 'function streamTrim') }
)

foreach ($layer in $layers)
{
    $abs = Join-Path $Root $layer.File
    if (-not (Test-Path -LiteralPath $abs))
    {
        $violations.Add(("[{0}] 层文件不在位：{1}" -f $layer.Tag, $layer.File))
        continue
    }

    $text = Get-Content -LiteralPath $abs -Raw -Encoding UTF8
    foreach ($m in $layer.Must)
    {
        if ($text.IndexOf($m, [StringComparison]::Ordinal) -lt 0)
        {
            $violations.Add(("[{0}] 缺结构事实「{1}」：{2}" -f $layer.Tag, $m, $layer.File))
        }
    }

    if ($ShowDetail) { Write-Host ("  [{0}] {1} 已核（{2} 项）" -f $layer.Tag, $layer.File, $layer.Must.Count) -ForegroundColor DarkGray }
}

# B0 单源一致性：基线声明的闭合层必须全部进判据面（防「基线登记了层、脚本却不判」）
$declaredLayers = @($baseline.VerbClosureLayers.Value)
$scriptLayers = @($layers | ForEach-Object { $_.File })
foreach ($d in $declaredLayers)
{
    if ($scriptLayers -notcontains $d) { $violations.Add("[B0] 基线声明层未进判据面：$d") }
}

# B5 负对照：缺 Append 分支的合成样本必须判违规
$synthetic = @'
switch (plan.Kind)
{
    case UiWriteKind.Region:
        break;
}
'@
$required = @('case UiWriteKind.Append:', 'case UiWriteKind.Trim:')
$missingInSynthetic = @($required | Where-Object { $synthetic.IndexOf($_, [StringComparison]::Ordinal) -lt 0 })
if ($missingInSynthetic.Count -eq 0)
{
    $violations.Add('[B5] 负对照失效：缺 Append/Trim 分支的合成样本未被判违规（判据恒真）')
}

# ---------------- 结构事实判定原语（单源：正/负对照共用同一判定） ----------------
function Get-MissingTokens {
    param([string]$Text, [object[]]$Must)
    return @($Must | Where-Object { $Text.IndexOf([string]$_, [StringComparison]::Ordinal) -lt 0 })
}

# B6：Tick 只产叶子（唯一产地 AsTick 的函数体窗口判据：必含 Tick/Leaf，禁区域/树/流动词）
function Test-TickBody {
    param([string]$Text, [hashtable]$Spec)
    $entry = [string]$Spec.EntryToken
    $idx = $Text.IndexOf($entry, [StringComparison]::Ordinal)
    if ($idx -lt 0) { return @{ Ok = $false; Reason = "缺 Tick 唯一产地「$entry」" } }
    $len = [Math]::Min([int]$Spec.WindowRadius, $Text.Length - $idx)
    $window = $Text.Substring($idx, $len)
    foreach ($t in @([string]$Spec.TickToken, [string]$Spec.LeafToken)) {
        if ($window.IndexOf($t, [StringComparison]::Ordinal) -lt 0) {
            return @{ Ok = $false; Reason = "Tick 产地缺结构事实「$t」" }
        }
    }
    foreach ($f in @($Spec.ForbiddenTokens)) {
        if ($window.IndexOf([string]$f, [StringComparison]::Ordinal) -ge 0) {
            return @{ Ok = $false; Reason = "Tick 产地出现禁用动词「$f」" }
        }
    }
    return @{ Ok = $true; Reason = '' }
}

$tickSpec = $baseline.TickPath.Value
$tickRel = [string]$tickSpec.Pipeline
$tickAbs = Join-Path $Root $tickRel
if (-not (Test-Path -LiteralPath $tickAbs))
{
    $violations.Add("[B6] 判据无标的（文件不在位）：$tickRel")
}
else
{
    $tickResult = Test-TickBody -Text (Get-Content -LiteralPath $tickAbs -Raw -Encoding UTF8) -Spec $tickSpec
    if (-not $tickResult.Ok) { $violations.Add("[B6] $($tickResult.Reason)：$tickRel") }
    elseif ($ShowDetail) { Write-Host "  [B6] $tickRel Tick 产地已核（只产叶子）" -ForegroundColor DarkGray }
}

# B7：气泡段流契约四层（方向枚举 / JS 方向分支 / 段键前缀 / 渲染器分段入口）
$streamLayers = @($baseline.StreamSectionContract.Value)
if ($streamLayers.Count -eq 0) { $violations.Add('[B7] 基线缺 StreamSectionContract（判据无标的）') }
foreach ($layer in $streamLayers)
{
    $layerRel = [string]$layer.File
    $layerAbs = Join-Path $Root $layerRel
    if (-not (Test-Path -LiteralPath $layerAbs))
    {
        $violations.Add("[B7] 层文件不在位：$layerRel")
        continue
    }

    $layerText = Get-Content -LiteralPath $layerAbs -Raw -Encoding UTF8
    $missing = @(Get-MissingTokens -Text $layerText -Must @($layer.Must))
    foreach ($m in $missing) { $violations.Add("[B7] 缺结构事实「$m」：$layerRel") }
    if ($ShowDetail -and $missing.Count -eq 0) { Write-Host "  [B7] $layerRel 已核（$(@($layer.Must).Count) 项）" -ForegroundColor DarkGray }
}

# B10：帧预算 + 指标 + 灰度开关（三载体结构事实：选项 / 指标 / 降级入口）
$runtimeLayers = @($baseline.WriteRuntimeContract.Value)
if ($runtimeLayers.Count -eq 0) { $violations.Add('[B10] 基线缺 WriteRuntimeContract（判据无标的）') }
foreach ($layer in $runtimeLayers)
{
    $layerRel = [string]$layer.File
    $layerAbs = Join-Path $Root $layerRel
    if (-not (Test-Path -LiteralPath $layerAbs))
    {
        $violations.Add("[B10] 层文件不在位：$layerRel")
        continue
    }

    $layerText = Get-Content -LiteralPath $layerAbs -Raw -Encoding UTF8
    $missing = @(Get-MissingTokens -Text $layerText -Must @($layer.Must))
    foreach ($m in $missing) { $violations.Add("[B10] 缺结构事实「$m」：$layerRel") }
    if ($ShowDetail -and $missing.Count -eq 0) { Write-Host "  [B10] $layerRel 已核（$(@($layer.Must).Count) 项）" -ForegroundColor DarkGray }
}

# B11 负对照：缺开关的合成选项样本必须判缺（防预算/开关判据恒真）
$negativeOptions = 'public sealed record UiWriteRuntimeOptions { public bool SingleWrite { get; init; } = true; }'
$negativeOptionMissing = @(Get-MissingTokens -Text $negativeOptions -Must @('SingleWrite', 'FrameBudget', 'InteractiveFastPath'))
if ($negativeOptionMissing.Count -eq 0) { $violations.Add('[B11] 负对照失效：缺开关样本未被判缺（判据恒真）') }

# B8 负对照：合成「Tick 产树」样本必须判违规（防 Tick 判据恒真）
$negativeTick = 'private static RenderOutput AsTick(UiWritePlan plan) { var leaf = UiWriteKind.Leaf; ' +
                'return RenderOutput.Write(UiWritePlan.Trees(trees, null, null, null, UiWriteSource.Tick)); }'
$negativeTickResult = Test-TickBody -Text $negativeTick -Spec @{
    EntryToken = 'AsTick('; TickToken = 'UiWriteSource.Tick'; LeafToken = 'UiWriteKind.Leaf'
    ForbiddenTokens = @('UiWritePlan.Trees('); WindowRadius = 600
}
if ($negativeTickResult.Ok) { $violations.Add('[B8] 负对照失效：合成「Tick 产树」样本未被判违规（判据恒真）') }

# B9 负对照：缺方向分支的合成流原语必须判缺（防段流契约判据恒真）
$negativeStream = 'function streamAppend(host,payload){items.forEach(function(it){host.appendChild(it);});}'
$negativeMissing = @(Get-MissingTokens -Text $negativeStream -Must @('direction', 'oldestFirst'))
if ($negativeMissing.Count -eq 0) { $violations.Add('[B9] 负对照失效：无方向分支样本未被判缺（判据恒真）') }

if ($violations.Count -eq 0)
{
    Write-Host '[OK] UiBudget：Append/Trim 四层闭合 + Tick 只产叶子 + 气泡段流契约 + 帧预算/指标/开关；正/负对照全绿。' -ForegroundColor Green
    exit 0
}

Write-Host ("[FAIL] UiBudget 命中 {0} 处：" -f $violations.Count) -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host ("  - {0}" -f $v) -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
