#requires -Version 7.0
<#
.SYNOPSIS
    选择态写入单源守卫（E04）：选择态只能由 ConversationViewService（渲染入口）写。
.DESCRIPTION
    C1 写入点收敛：全仓 `SetCurrentHistory(` / `SetCurrentTask(` 只允许出现在 ConversationViewService.cs
       （声明 + 单写者；handler / 刷新适配 / 其它文件出现即双写）。
    C2 申报面在位：BuildViewOutputAsync 具备 rememberTask 形参（handler 只声明，不写）。
    C3 负对照：合成 `ConversationViewService.Shared.SetCurrentHistory(...)` 必须被 C1 捕获。
    纯扫描（只读、0 写入）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

$repo = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
$agent = Join-Path $repo 'Projects\Plug\Agent\src'
$viewService = Join-Path $agent 'Agent.Ui\Ui\Views\Orchestration\ConversationViewService.cs'

function Find-WriteSites {
    $hits = New-Object System.Collections.Generic.List[string]
    Get-ChildItem -LiteralPath $agent -Recurse -Include *.cs -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|BuildOutput' } |
        ForEach-Object {
            if ($_.Name -like 'ConversationViewService*.cs') { return }
            $m = Select-String -LiteralPath $_.FullName -Pattern 'SetCurrentHistory\(|SetCurrentTask\('
            if ($m) { $hits.Add($_.FullName.Replace($repo, '').TrimStart('\')) }
        }
    return $hits
}

$rules = @(
    [pscustomobject]@{ Id = 'C1'; Title = '写入点收敛（仅 ConversationViewService.cs）'
        Ok = { (Find-WriteSites).Count -eq 0 } }
    [pscustomobject]@{ Id = 'C2'; Title = '申报面在位（BuildViewOutputAsync 具备 rememberTask）'
        Ok = { (Get-Content -LiteralPath (Join-Path $agent 'Agent.Ui\Ui\Views\Orchestration\ConversationViewService.Render.cs') -Raw) -match 'rememberTask' } }
    [pscustomobject]@{ Id = 'C4'; Title = 'SetCurrentTask 只写目标作用域（显式视图参数）'
        Ok = {
            $t = Get-Content -LiteralPath (Join-Path $agent 'Agent.Ui\Ui\Views\Orchestration\ConversationViewService.Selection.cs') -Raw
            ($t -match 'SetCurrentTask\(MainDialogViewType') -and ($t -notmatch 'SetCurrentTask\(string') } }
    [pscustomobject]@{ Id = 'C5'; Title = 'rememberTask 使用点击键（非锚替换后的 name）'
        Ok = {
            $t = Get-Content -LiteralPath (Join-Path $agent 'Agent.Ui\Core\Functions\Conversation\AgentUiLibraryConversation.cs') -Raw
            ($t -match 'rememberTask:\s*string\.IsNullOrWhiteSpace\(msg\.Session\)\s*\?\s*null\s*:\s*msg\.Session') -and
            ($t -notmatch 'rememberTask:[^\r\n]*\bname\b') } }
)

if ($NegativeControl) {
    $bad = 'ConversationViewService.Shared.SetCurrentHistory(msg.History);'
    $good = 'ConversationViewService.Shared.BuildViewOutputAsync(name, ct, msg.History);'
    $pattern = 'SetCurrentHistory\(|SetCurrentTask\('
    $caught = ($bad -match $pattern) -and (-not ($good -match $pattern))
    if (-not $caught) { throw '负对照失败：C1 判据未能区分正/负样本' }
    $bad2 = 'rememberTask: string.IsNullOrWhiteSpace(msg.Session) ? null : name'
    $good2 = 'rememberTask: string.IsNullOrWhiteSpace(msg.Session) ? null : msg.Session'
    $caught2 = ($bad2 -match 'rememberTask:[^\r\n]*\bname\b') -and (-not ($good2 -match 'rememberTask:[^\r\n]*\bname\b'))
    if (-not $caught2) { throw '负对照失败：C5 判据未能区分正/负样本' }
    Write-Host '[OK] 负对照通过：C1/C5 判据可捕获 handler 直接写入与键替换。' -ForegroundColor Green
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
        Write-Host "UiAnchorWrites: $failed 项违规（选择态出现第二写者）。" -ForegroundColor Red
        exit 1
    }
    Write-Host 'UiAnchorWrites: 全部通过。' -ForegroundColor Green
}

exit 0
