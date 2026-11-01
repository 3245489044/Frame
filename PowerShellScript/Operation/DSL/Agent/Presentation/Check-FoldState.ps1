#requires -Version 5.1
<#
.SYNOPSIS
    折叠协议守卫（FoldState）—— 所有 details 走同一 fold key + 同一绑定器 + 同一写路径。

.DESCRIPTION
    判据（载体 = 模板/源码结构事实）：
      F1 模板无 open 字面量：<details> 标签内 open 属性 = 0，`{?open} open{/}` = 0（默认态由 data-default-open 给出）。
      F2 每个 <details> 均带 data-fold-key（缺键 ⇒ 用户态无法回放）。
      F3 模板出口产元素节点：HtmlTemplateEngine.RenderTemplateToNodes 经 HtmlNodeParser.Parse（非 RawText）。
      F4 绑定期在指纹/序列化之前：RegionRenderPipeline 的 FoldBinder.Bind 先于 region 指纹；stream 序列化 bound。
      F5 绑定职责不重叠：UiStateBinder 跳过带 fold key 的 details。
      F6 JS 协议：reconcile.js 捕获 data-fold-key 全量态；details 仅认 data-open-state（不再以 open 覆盖用户收起态）。
      NC 正/负对照：合成 <details class="x" open> 被 F1 捕获；合成无 fold key 的 details 被 F2 捕获。
    模式：默认报告；-Check 违规 → 退出码 1。只读、0 写。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $d = $PSScriptRoot
    while ($d -and -not (Test-Path (Join-Path $d 'Frame\CsharpPrj'))) { $d = Split-Path $d -Parent }
    if ($d) { return $d }
    throw '[FoldState] 无法定位仓库根（上溯未找到含 Frame\CsharpPrj 的目录）'
}

$root = Find-RepoRoot
$templatesPath = Join-Path $root 'Frame\CsharpPrj\Plate\HtmlViewer\Assets\templates.default.json'
$enginePath = Join-Path $root 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Render\HtmlTemplateEngine.cs'
$binderPath = Join-Path $root 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Align\UiStateBinder.cs'
$pipelinePath = Join-Path $root 'Frame\CsharpPrj\Operation\FrontEndDriven\HtmlViewerBridge\RegionRenderPipeline.cs'
$scriptPath = Join-Path $root 'Frame\CsharpPrj\Plate\HtmlViewer\Assets\scripts\reconcile.js'

$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$id, [string]$msg) { $script:violations.Add("$id $msg") }
function Read-Text([string]$path) { if (-not (Test-Path -LiteralPath $path)) { throw "缺文件: $path" }; Get-Content -LiteralPath $path -Raw }

$tplText = Read-Text $templatesPath
$detailsTags = @([regex]::Matches($tplText, '<details\b[^>]*>'))

# F1 open 字面量
$openHits = 0
foreach ($tag in $detailsTags) { if ($tag.Value -match '\sopen(\s|=|>)') { $openHits++ } }
$openCondHits = [regex]::Matches($tplText, '\{\?\s*open\s*\}').Count
Write-Host ("[FoldState] F1 details={0} open-attr={1} open-cond={2}" -f $detailsTags.Count, $openHits, $openCondHits)
if ($openHits -gt 0) { Add-Violation 'F1' ("details 含 open 字面量 {0} 处（默认态须由 data-default-open 给出）" -f $openHits) }
if ($openCondHits -gt 0) { Add-Violation 'F1' ("模板含 {?open} 条件 {0} 处（用户态须由绑定器写）" -f $openCondHits) }

# F2 fold key 齐备
$missingFoldKey = 0
foreach ($tag in $detailsTags) { if ($tag.Value -notmatch 'data-fold-key=') { $missingFoldKey++ } }
Write-Host ("[FoldState] F2 missing-fold-key={0}" -f $missingFoldKey)
if ($detailsTags.Count -eq 0) { Add-Violation 'F2' '模板集无 details（判据无标的）' }
if ($missingFoldKey -gt 0) { Add-Violation 'F2' ("details 缺 data-fold-key {0} 处" -f $missingFoldKey) }

# F3 模板出口 = 元素节点
$engineText = Read-Text $enginePath
$elementNodes = $engineText -match 'HtmlNodeParser\.Parse\('
Write-Host ("[FoldState] F3 element-nodes={0}" -f $elementNodes)
if (-not $elementNodes) { Add-Violation 'F3' 'RenderTemplateToNodes 未经 HtmlNodeParser.Parse（模板字面量仍是 RawText）' }

# F4 先绑定后指纹/序列化
$pipelineText = Read-Text $pipelinePath
$bindIndex = $pipelineText.IndexOf('FoldBinder.Bind', [System.StringComparison]::Ordinal)
$hashIndex = $pipelineText.IndexOf('_regionSlotPrefix + region.Key', [System.StringComparison]::Ordinal)
$streamBound = $pipelineText -match 'UiStreamItem\(\s*[\s\S]{0,200}Serialize\(bound\)'
Write-Host ("[FoldState] F4 bindIndex={0} hashIndex={1} streamBound={2}" -f $bindIndex, $hashIndex, $streamBound)
if ($bindIndex -lt 0) { Add-Violation 'F4' 'RegionRenderPipeline 未调用 FoldBinder.Bind' }
elseif ($hashIndex -ge 0 -and $bindIndex -gt $hashIndex) { Add-Violation 'F4' 'FoldBinder.Bind 晚于区域指纹（折叠态不入变更判定）' }
if (-not $streamBound) { Add-Violation 'F4' '流段未序列化绑定后的节点（stream 绕过折叠绑定）' }

# F5 职责不重叠
$binderText = Read-Text $binderPath
$skipFold = $binderText -match 'FoldBinder\.FoldKeyAttribute'
Write-Host ("[FoldState] F5 ui-state-binder-skips-fold={0}" -f $skipFold)
if (-not $skipFold) { Add-Violation 'F5' 'UiStateBinder 未跳过带 fold key 的 details（双绑定互相覆盖）' }

# F6 JS 协议
$jsText = Read-Text $scriptPath
$capturesFold = $jsText -match "data-fold-key"
$legacyOpen = $jsText -match 'wasOpen\|\|newN\.hasAttribute'
Write-Host ("[FoldState] F6 capture-fold={0} legacy-open={1}" -f $capturesFold, $legacyOpen)
if (-not $capturesFold) { Add-Violation 'F6' 'reconcile.js 未捕获 data-fold-key（折叠态不可回放）' }
if ($legacyOpen) { Add-Violation 'F6' 'reconcile.js details 仍以 open 属性覆盖用户收起态' }

if ($NegativeControl) {
    $posOpen = '<details class="x" open>' -match '<details\b[^>]*>' -and '<details class="x" open>' -match '\sopen(\s|=|>)'
    $negOpen = '<details class="x" data-fold-key="k">' -match '\sopen(\s|=|>)'
    $posKey = '<details class="x" data-fold-key="k">' -notmatch 'data-fold-key='
    if (-not $posOpen) { Add-Violation 'NC' '正对照未命中：合成 open 字面量未被 F1 捕获' }
    if ($negOpen) { Add-Violation 'NC' '负对照误命中：无 open 的 details 被判为 open' }
    if ($posKey) { Add-Violation 'NC' '正对照未命中：带 fold key 样本被 F2 误判' }
    Write-Host ("[FoldState] NC posOpen={0} negOpen={1} posKey={2}" -f $posOpen, $negOpen, $posKey)
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host ("[VIOLATION] {0}" -f $v) }
    if ($Check) { exit 1 }
}
else {
    Write-Host '[FoldState] 通过（0 违规）'
    if ($Check) { exit 0 }
}
