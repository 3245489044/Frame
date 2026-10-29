#requires -Version 7.0
<#
.SYNOPSIS
    区域归属守卫（P1）：每个 RegionKind 恰一个生产者；nodes 区域不得承载 feed/board/prompt。
.DESCRIPTION
    C1 渲染器内 `runtimeHtml + nodes` 前置形态 = 0（E-12/B2.2 删除项）；
    C2 `case UiRegionKind.Nodes:` 块内不得出现 RenderDispatchFeed / RenderTaskBoard / RenderPromptItems；
    C3 BuildContent 内 Feed/Board/Prompt/Nodes/Bubbles 各恰一个 case（区域生产者唯一）；
    C4 负对照：合成「nodes 写进 feed」样本必被 C2 捕获。
    纯扫描（只读、0 写入）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))))
$rendererPath = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\ConversationPageRenderer.cs'

$nodesBlock = '(?s)case\s+UiRegionKind\.Nodes\s*:(.*?)(?=case\s+UiRegionKind\.|\}\s*\r?\n\s*var\s+bubbles)'
function Test-NodesLeak([string]$text) {
    $m = [regex]::Match($text, $nodesBlock)
    if (-not $m.Success) { return $false }
    return $m.Groups[1].Value -match 'RenderDispatchFeed|RenderTaskBoard|RenderPromptItems'
}

function Get-RegionCaseCount([string]$text, [string]$kind) {
    return [regex]::Matches($text, "case\s+UiRegionKind\.$kind\b[^:]*:").Count
}

$rules = @(
    [pscustomobject]@{ Id = 'C1'; Title = 'nodes 不再前置 runtime 面板（runtimeHtml + nodes = 0）'
        Ok = { (Get-Content -LiteralPath $rendererPath -Raw) -notmatch 'runtimeHtml\s*\+\s*nodes' } }
    [pscustomobject]@{ Id = 'C2'; Title = 'nodes 区域不承载 feed/board/prompt'
        Ok = { -not (Test-NodesLeak (Get-Content -LiteralPath $rendererPath -Raw)) } }
    [pscustomobject]@{ Id = 'C3'; Title = '五个内容区域各恰一个生产者 case'
        Ok = {
            $text = Get-Content -LiteralPath $rendererPath -Raw
            foreach ($kind in @('Feed', 'Board', 'Prompt', 'Nodes', 'Bubbles')) {
                if ((Get-RegionCaseCount $text $kind) -ne 1) { return $false }
            }
            return $true
        } }
)

if ($NegativeControl) {
    $bad = @'
foreach (var region in model.Regions) {
    switch (region.Kind) {
        case UiRegionKind.Nodes:
            builder.Append(RenderDispatchFeed(board));
            break;
    }
    var bubbles = Array.Empty<object>();
}
'@
    $good = @'
foreach (var region in model.Regions) {
    switch (region.Kind) {
        case UiRegionKind.Nodes:
            builder.Append(d.NodesHtml ?? region.EmptyReason ?? string.Empty);
            break;
    }
    var bubbles = Array.Empty<object>();
}
'@
    if (-not (Test-NodesLeak $bad)) { throw '负对照失败：C2 判据未捕获 nodes 区域承载 feed' }
    if (Test-NodesLeak $good) { throw '负对照失败：C2 判据对合法 nodes 区域误报' }
    Write-Host '[OK] 负对照通过：nodes 越界被捕获，合法样本不误报。' -ForegroundColor Green
}

if (-not $Check -and -not $NegativeControl) {
    Write-Host '用法: -Check（检查） / -NegativeControl（判据自检）' -ForegroundColor Yellow
    return
}

if ($Check) {
    $failed = 0
    foreach ($r in $rules) {
        $pass = $false
        try { $pass = [bool](& $r.Ok) } catch { $pass = $false }
        $mark = if ($pass) { 'PASS' } else { 'FAIL' }
        if (-not $pass) { $failed++ }
        Write-Host ("[{0}] {1} {2}" -f $mark, $r.Id, $r.Title) -ForegroundColor $(if ($pass) { 'Green' } else { 'Red' })
    }
    if ($failed -gt 0) {
        Write-Host "UiRegionOwnership: $failed 项违规（区域职责错位）。" -ForegroundColor Red
        exit 1
    }
    Write-Host 'UiRegionOwnership: 全部通过。' -ForegroundColor Green
}

exit 0
