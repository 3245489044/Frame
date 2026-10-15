#requires -Version 7.0
<#
.SYNOPSIS
    UI 选择态外置一致性守卫（框架级态外置）。
.DESCRIPTION
    锁定「选择态经结构化协议回投」的框架不变式，禁止回退到「扫 DOM 选择器反查领域属性」的旧法：
      C1  会话页刷新协议不得再用 data-poll-ctx 反查（PollAttributes 第三/四参须为 null）。
      C2  选择态采集端存在（JS capture 产出 uiState.select，声明式 [data-select-slot]）。
      C3  选择态消费端存在（HtmlBridgePayload.SelectionKeys + UiSelectionState）。
      C4  历史归属解析不得伪造 default（ConversationTreeModel.ResolveHistoryForTask 无 `return SessionKeys.Default`）。
      C5  当前选中历史受控态存在（ConversationViewService._currentHistory）。
    纯扫描（只读、0 写入）；命中即 Error。
.PARAMETER Check
    执行检查。
.PARAMETER NegativeControl
    负对照：向临时样本注入一处违规，断言守卫能捕获（自检用）。
.EXAMPLE
    pwsh D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Check-UiSelectionExternalization.ps1 -Check
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
$plate = Join-Path $repo 'Frame\CsharpPrj\Plate\HtmlViewer'
$agent = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui'
$strategy = Join-Path $repo 'Frame\CsharpPrj\Strategy\Refresh'

$pageRenderer = Join-Path $agent 'Ui\Views\ConversationPageRenderer.cs'
$assetRegistry = Join-Path $plate 'Html\Assets\HtmlAssetRegistry.cs'
$payload = Join-Path $plate 'Html\Bridge\HtmlBridgePayload.cs'
$selectionState = Join-Path $plate 'Abstractions\UiSelectionState.cs'
$treeModel = Join-Path $agent 'Ui\Views\ConversationTreeModel.cs'
$viewService = Join-Path $agent 'Ui\Views\ConversationViewService.cs'

function Read-Text([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { throw "缺文件: $path" }
    return Get-Content -LiteralPath $path -Raw
}

$rules = @(
    [pscustomobject]@{
        Id = 'C1'; Title = '刷新协议不得再用 data-poll-ctx 反查领域属性'
        Ok = { -not ((Read-Text $pageRenderer) -match 'PollAttributes\("[^"]*"\s*,\s*"[^"]+"') }
    }
    [pscustomobject]@{
        Id = 'C2'; Title = '选择态采集端存在（capture 产出 select + [data-select-slot]）'
        Ok = { $t = Read-Text $assetRegistry; ($t -match "data-select-slot") -and ($t -match 'select:sel') }
    }
    [pscustomobject]@{
        Id = 'C3'; Title = '选择态消费端存在（SelectionKeys + UiSelectionState）'
        Ok = { ((Read-Text $payload) -match 'SelectionKeys') -and (Test-Path -LiteralPath $selectionState) }
    }
    [pscustomobject]@{
        Id = 'C4'; Title = '历史归属解析不得伪造 default'
        Ok = { -not ((Read-Text $treeModel) -match 'ResolveHistoryForTask[\s\S]*?return SessionKeys\.Default') }
    }
    [pscustomobject]@{
        Id = 'C5'; Title = '当前选中历史受控态存在（_currentHistory）'
        Ok = { (Read-Text $viewService) -match '_currentHistory' }
    }
)

if ($NegativeControl) {
    # 负对照：注入违规文本，断言 C1/C4 能捕获（说明判据非恒真）。
    $tmpDir = Join-Path ([System.IO.Path]::GetTempPath()) ("uiselection-neg-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmpDir | Out-Null
    try {
        $bad = 'var x = HtmlRefreshProtocol.PollAttributes("*", ".item.active", "session,history");'
        $ok = 'var x = HtmlRefreshProtocol.PollAttributes("*", null, null);'
        $caught = ($bad -match 'PollAttributes\("[^"]*"\s*,\s*"[^"]+"') -and (-not ($ok -match 'PollAttributes\("[^"]*"\s*,\s*"[^"]+"'))
        if (-not $caught) { throw "负对照失败：C1 判据未能区分正/负样本" }
        $bad2 = 'return SessionKeys.Default;'
        $ok2 = 'return null;'
        $caught2 = ($bad2 -match 'SessionKeys\.Default') -and (-not ($ok2 -match 'SessionKeys\.Default'))
        if (-not $caught2) { throw "负对照失败：C4 判据未能区分正/负样本" }
        Write-Host "[OK] 负对照通过：C1/C4 判据可捕获违规样本。" -ForegroundColor Green
    }
    finally {
        Remove-Item -LiteralPath $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not $Check -and -not $NegativeControl) {
    Write-Host "用法: -Check（检查） / -NegativeControl（判据自检）" -ForegroundColor Yellow
    return
}

if ($Check) {
    $failed = 0
    foreach ($r in $rules) {
        $pass = $false
        try { $pass = [bool](& $r.Ok) } catch { $pass = $false }
        $mark = if ($pass) { 'PASS' } else { 'FAIL'; }
        if (-not $pass) { $failed++ }
        $color = if ($pass) { 'Green' } else { 'Red' }
        Write-Host ("[{0}] {1} {2}" -f $mark, $r.Id, $r.Title) -ForegroundColor $color
    }
    if ($failed -gt 0) {
        Write-Host "UiSelectionExternalization: $failed 项违规（选择态外置被破坏）。" -ForegroundColor Red
        exit 1
    }
    Write-Host "UiSelectionExternalization: 全部通过。" -ForegroundColor Green
}

# 显式成功退出码（聚合器 Invoke-GateCheck 依据 $LASTEXITCODE 裁决；未显式退出会被判 2=未知）。
exit 0
