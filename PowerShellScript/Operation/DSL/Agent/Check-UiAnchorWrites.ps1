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
$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))))
$agent = Join-Path $repo 'Projects\Plug\Agent\src'
$viewService = Join-Path $agent 'Agent.Ui\Ui\Views\Orchestration\ConversationViewService.cs'

function Find-WriteSites {
    $hits = New-Object System.Collections.Generic.List[string]
    Get-ChildItem -LiteralPath $agent -Recurse -Include *.cs -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|BuildOutput' } |
        ForEach-Object {
            if ($_.FullName -eq $viewService) { return }
            $m = Select-String -LiteralPath $_.FullName -Pattern 'SetCurrentHistory\(|SetCurrentTask\('
            if ($m) { $hits.Add($_.FullName.Replace($repo, '').TrimStart('\')) }
        }
    return $hits
}

$rules = @(
    [pscustomobject]@{ Id = 'C1'; Title = '写入点收敛（仅 ConversationViewService.cs）'
        Ok = { (Find-WriteSites).Count -eq 0 } }
    [pscustomobject]@{ Id = 'C2'; Title = '申报面在位（BuildViewOutputAsync 具备 rememberTask）'
        Ok = { (Get-Content -LiteralPath $viewService -Raw) -match 'rememberTask' } }
)

if ($NegativeControl) {
    $bad = 'ConversationViewService.Shared.SetCurrentHistory(msg.History);'
    $good = 'ConversationViewService.Shared.BuildViewOutputAsync(name, ct, msg.History);'
    $pattern = 'SetCurrentHistory\(|SetCurrentTask\('
    $caught = ($bad -match $pattern) -and (-not ($good -match $pattern))
    if (-not $caught) { throw '负对照失败：C1 判据未能区分正/负样本' }
    Write-Host '[OK] 负对照通过：C1 判据可捕获 handler 直接写入。' -ForegroundColor Green
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
