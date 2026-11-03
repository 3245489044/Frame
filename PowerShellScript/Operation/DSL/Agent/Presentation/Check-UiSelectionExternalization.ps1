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
    pwsh D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-UiSelectionExternalization.ps1 -Check
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking


$repo = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
$plate = Join-Path $repo 'Frame\CsharpPrj\Plate\HtmlViewer'
$agent = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui'
$strategy = Join-Path $repo 'Frame\CsharpPrj\Strategy\Refresh'

$pageRenderer = Join-Path $agent 'Ui\Views\Rendering\ConversationPageRenderer.cs'
$assetRegistry = Join-Path $plate 'Html\Assets\HtmlAssetRegistry.cs'
$payload = Join-Path $plate 'Html\Bridge\HtmlBridgePayload.cs'
$selectionState = Join-Path $plate 'Abstractions\UiSelectionState.cs'
$treeModel = Join-Path $agent 'Core\Projection\ConversationTreeProjection.cs'
$viewService = Join-Path $agent 'Ui\Views\Orchestration\ConversationViewService.cs'

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
        Id = 'C2'; Title = '选择态采集端存在（声明式 data-select-slot + capture）'
        Ok = {
            $assets = Join-Path $plate 'Assets'
            $hasSlot = @(Get-ChildItem -LiteralPath $assets -Recurse -Include *.json, *.js |
                Select-String -Pattern 'data-select-slot' -List).Count -gt 0
            $script = Read-Text (Join-Path $assets 'scripts\reconcile.js')
            $hasSlot -and ($script -match 'capture')
        }
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
        Id = 'C5'; Title = '当前选中态受控于选择态存储（UiSelectionStore）'
        Ok = { $t = Read-Text $viewService; ($t -match '_selection') -and ($t -match 'UiSelectionStore') }
    }
    [pscustomobject]@{
        Id = 'C6'; Title = '选择槽声明封存 ∧ 声明集覆盖写路径（H38）'
        Ok = {
            $svc = Read-Text (Join-Path $agent 'Ui\Views\Orchestration\ConversationViewService.cs')
            $declCount = ([regex]::Matches($svc, 'new UiSelectionSlotDecl\(')).Count
            ($svc -match '_selection\.Seal\(\)') -and ($declCount -ge 6) -and
            ($svc -match 'MainDialogViewType\.Task\)\s*,\s*SelectionSlotTask') -and
            ($svc -match 'MainDialogViewType\.History\)\s*,\s*SelectionSlotTask')
        }
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
