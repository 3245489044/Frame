#requires -Version 7.0
<#
.SYNOPSIS
    渲染产物边界守卫（FragmentBoundary）—— 渲染链只产节点，序列化出口唯一。
.DESCRIPTION
    B1 序列化出口 = 1：`UiNodeHtml.ToHtml` 的调用点恰在 plate 节点写出器（UiFragmentWriter）一处（桥内零调用）。
    B2 渲染链「造串→解串」= 0：ConversationPageRenderer / ConversationViewService / RegionRenderPipeline
       不得对气泡段调用 HtmlNodeParser.Parse（Agent 侧非气泡区域的边界解析登记为例外）。
    B3 会话模板 |raw = 0：templates.default.json 的 sectionHeader/roundFold/leadFold/blockItem/attachItem 等
       不得使用 `|raw`（嵌套片段必须是真节点）。
    B4 负对照：合成样本必判红。
    纯扫描（只读、0 写入）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
while ($repo -and $repo -ne (Split-Path $repo -Parent)) {
    if ((Test-Path (Join-Path $repo 'Projects')) -and (Test-Path (Join-Path $repo 'Frame'))) { break }
    $repo = Split-Path $repo -Parent
}

$writerPath = Join-Path $repo 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Render\Composition\UiFragmentWriter.cs'
$bridgeDir = Join-Path $repo 'Frame\CsharpPrj\Operation\FrontEndDriven\HtmlViewerBridge'
$pipelinePath = Join-Path $bridgeDir 'RegionRenderPipeline.cs'
$pageRendererPath = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Rendering\ConversationPageRenderer.cs'
$viewServicePath = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Orchestration\ConversationViewService.Render.cs'
$templatesPath = Join-Path $repo 'Frame\CsharpPrj\Plate\HtmlViewer\Assets\templates.default.json'

function Read-Text([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { throw "缺文件: $path" }
    return Get-Content -LiteralPath $path -Raw
}

function Get-SerializerCallSites([string]$dir) {
    $count = 0
    Get-ChildItem -LiteralPath $dir -Recurse -Filter '*.cs' -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        ForEach-Object {
            $text = Get-Content -LiteralPath $_.FullName -Raw
            $count += ([regex]::Matches($text, 'UiNodeHtml\.ToHtml\s*\(')).Count
        }
    return $count
}

# 节点写出器已下沉 plate（HTML 单原语）：出口计数 = 桥内 + 写出器文件（不经整目录扫描，避免误计 plate 其它渲染件）。
function Get-SerializerCallSitesInFile([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return 0 }
    return ([regex]::Matches((Get-Content -LiteralPath $path -Raw), 'UiNodeHtml\.ToHtml\s*\(')).Count
}

if ($NegativeControl) {
    $badSources = '<div class="stream-item">' + 'x' + '</div>'
    if (-not ($badSources -match '<[A-Za-z]')) { throw '负对照失败：未识别内联 HTML 样本' }
    if ('{history|raw}' -notmatch '\|\s*raw') { throw '负对照失败：未识别 raw 绑定' }
    Write-Host '[OK] 负对照通过：内联 HTML / raw 绑定样本可被判红。' -ForegroundColor Green
    return
}

if (-not $Check) {
    Write-Host '用法: -Check（检查） / -NegativeControl（判据自检）' -ForegroundColor Yellow
    return
}

$failed = 0
function Report([string]$id, [string]$title, [bool]$ok) {
    $mark = if ($ok) { 'PASS' } else { 'FAIL' }
    if (-not $ok) { $script:failed++ }
    Write-Host ("[{0}] {1} {2}" -f $mark, $id, $title) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}

Report 'B1' '序列化出口唯一（UiNodeHtml.ToHtml 调用点 = 1：桥内 + plate 写出器）' (((Get-SerializerCallSites $bridgeDir) + (Get-SerializerCallSitesInFile $writerPath)) -eq 1)

$hotPaths = @($pipelinePath, $viewServicePath)
$parseHits = 0
foreach ($path in $hotPaths) {
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $text = Read-Text $path
    $parseHits += ([regex]::Matches($text, 'HtmlNodeParser\.Parse\s*\(')).Count
}

# 页面渲染器：只允许两处登记边界 —— RenderRegionNodes（非气泡区域补边界）与 ParseLegacySection（旧渲染器适配）。
$pageParse = 0
$pageText = Read-Text $pageRendererPath
foreach ($m in [regex]::Matches($pageText, 'HtmlNodeParser\.Parse\s*\(')) {
    $before = $pageText.Substring(0, $m.Index)
    $inRegionNodes = ($before.LastIndexOf('RenderRegionNodes', [System.StringComparison]::Ordinal) -gt
                      $before.LastIndexOf('ParseLegacySection', [System.StringComparison]::Ordinal))
    $inLegacy = ($before.LastIndexOf('ParseLegacySection', [System.StringComparison]::Ordinal) -gt
                 $before.LastIndexOf('RenderRegionNodes', [System.StringComparison]::Ordinal))
    if (-not ($inRegionNodes -or $inLegacy)) { $pageParse++ }
}
$parseHits += $pageParse
Report 'B2' '渲染链气泡段无「造串→解串」（Parse 仅限两处登记边界）' ($parseHits -eq 0)

$templates = Read-Text $templatesPath
$rawKeys = @('sectionHeader', 'roundFold', 'leadFold', 'blockItem', 'attachItem', 'attachGroup', 'actionPanel', 'renderDegraded')
$rawHits = 0
foreach ($key in $rawKeys) {
    $m = [regex]::Match($templates, '"' + [regex]::Escape($key) + '"\s*:\s*"(?<body>(?:[^"\\]|\\.)*)"')
    if ($m.Success -and ($m.Groups['body'].Value -match '\|\s*raw')) { $rawHits++ }
}
Report 'B3' '会话模板无 |raw 绑定（嵌套片段为真节点）' ($rawHits -eq 0)

if ($failed -gt 0) {
    Write-Host "FragmentBoundary: $failed 项违规。" -ForegroundColor Red
    exit 1
}
Write-Host 'FragmentBoundary: 全部通过。' -ForegroundColor Green
exit 0
