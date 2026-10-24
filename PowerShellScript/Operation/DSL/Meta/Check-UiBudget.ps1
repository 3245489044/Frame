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

if ($violations.Count -eq 0)
{
    Write-Host '[OK] UiBudget：Append/Trim 契约-协调器-原语-JS 四层闭合；负对照绿。' -ForegroundColor Green
    exit 0
}

Write-Host ("[FAIL] UiBudget 命中 {0} 处：" -f $violations.Count) -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host ("  - {0}" -f $v) -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
