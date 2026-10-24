#requires -Version 5.1
<#
.SYNOPSIS
    UI 增量契约守卫（UiIncremental）：append-only 面（日志/过程帧/气泡）必须走流追加，禁整页重建。

.DESCRIPTION
    根因（设计 §3 D-UI-04）：日志是天然 append-only 流，却被建模为「整页快照」——
    每次变更 = LogHtmlFoldRenderer.Render 全量重建 400 块 + 整文档注入（滚动丢失、成本随量线性增长）。

    判据（结构事实，落在源码文本/调用点，禁执行者叙述）：
      I1 增量契约在位：日志 sink 必须同时出现 `UiWritePlan.Append(`（流追加载荷）与 `RenderBlock(`（单块标记同源）。
      I2 整页原语归零：日志 sink 不得再出现 `SetHtmlAsync(`（唯一整页出口 = UiWritePlan.FullDocument）。
      I3 整页渲染收口：`LogHtmlFoldRenderer.Render(` 只允许出现在 sink（首挂载/自愈快照）与渲染器自身定义处。
      I4 负对照：合成「只有整页、无 Append」样本必须判违规（防判据恒真）。

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

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name UiIncremental
$sinkRel = [string]$baseline.SinkFile.Value
$rendererRel = [string]$baseline.RendererFile.Value
$sinkAbs = Join-Path $Root ($sinkRel -replace '/', '\')
$rendererAbs = Join-Path $Root ($rendererRel -replace '/', '\')

$violations = [System.Collections.Generic.List[string]]::new()
Write-Host '=== [UiIncremental] UI 增量契约（append-only 面禁整页重建） ==='

# I1/I2：日志 sink 契约（必需要素 + 禁止要素）
if (-not (Test-Path -LiteralPath $sinkAbs))
{
    $violations.Add("[I1] 日志 sink 不在位（$sinkRel）——增量契约判据无标的")
}
else
{
    $sinkText = Get-Content -LiteralPath $sinkAbs -Raw -Encoding UTF8
    foreach ($m in @('UiWritePlan.Append(', 'RenderBlock('))
    {
        if ($sinkText.IndexOf($m, [StringComparison]::Ordinal) -lt 0)
        {
            $violations.Add("[I1] 日志 sink 缺增量契约要素：$m")
        }
    }

    foreach ($f in @('SetHtmlAsync('))
    {
        if ($sinkText.IndexOf($f, [StringComparison]::Ordinal) -ge 0)
        {
            $violations.Add("[I2] 日志 sink 仍直呼整文档原语：$f")
        }
    }

    if ($ShowDetail) { Write-Host "  [sink] $sinkRel 契约要素已核" -ForegroundColor DarkGray }
}

# I3：整页渲染调用面收口
$scanRoots = @((Join-Path $Root 'Frame\CsharpPrj'), (Join-Path $Root 'Projects'))
$files = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
foreach ($r in $scanRoots)
{
    if (-not (Test-Path -LiteralPath $r)) { continue }
    foreach ($f in (Get-ChildItem -LiteralPath $r -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue))
    {
        if ($f.FullName -match '\\(obj|bin|BuildOutput)\\') { continue }
        if ($f.FullName -like '*\_deprecated\*') { continue }
        $files.Add($f)
    }
}

$allowedFull = @($sinkAbs, $rendererAbs)
$renderHits = @()
if ($files.Count -gt 0)
{
    $renderHits = @(Select-String -Path $files.FullName -Pattern 'LogHtmlFoldRenderer\.Render\(' -ErrorAction SilentlyContinue)
}
foreach ($h in $renderHits)
{
    if ($allowedFull -contains $h.Path) { continue }
    $rel = $h.Path.Substring($Root.Length).TrimStart('\', '/')
    $violations.Add(("[I3] 整页渲染越界调用：{0}:{1}（append-only 面须走 UiWritePlan.Append）" -f $rel, $h.LineNumber))
}

# I4 负对照：缺 Append 的合成样本必须判违规
$synthetic = "private static async Task PushAsync(HtmlViewer viewer, string html) { await viewer.SetHtmlAsync(html); }"
$negMissing = @(@('UiWritePlan.Append(', 'RenderBlock(') | Where-Object { $synthetic.IndexOf($_, [StringComparison]::Ordinal) -lt 0 })
$negForbidden = @(@('SetHtmlAsync(') | Where-Object { $synthetic.IndexOf($_, [StringComparison]::Ordinal) -ge 0 })
if ($negMissing.Count -eq 0 -and $negForbidden.Count -eq 0)
{
    $violations.Add('[I4] 负对照失效：合成「整页-only」样本未被判违规（匹配器失效）')
}

Write-Host ("扫描 {0} 件 .cs | 整页渲染命中 {1} 处" -f $files.Count, $renderHits.Count) -ForegroundColor Gray

if ($violations.Count -eq 0)
{
    Write-Host '[OK] UiIncremental：append-only 面增量契约在位，整页渲染收口；负对照绿。' -ForegroundColor Green
    exit 0
}

Write-Host ("[FAIL] UiIncremental 命中 {0} 处：" -f $violations.Count) -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host ("  - {0}" -f $v) -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
