#requires -Version 7.0
<#
.SYNOPSIS
    渲染层零视图分支守卫（P2 / B2）：视图差异只允许是声明（ConversationViewCatalog.Regions）。
.DESCRIPTION
    C1 渲染层（Ui\Views\**）内 `switch (viewType)` / `if (viewType ==` 计数 = 0；
    C2 视图差异在目录声明（ConversationViewCatalog 含 Regions 列）；
    C3 渲染器按 model.Regions 顺序拼接（foreach 单点）；
    C4 负对照：合成 `switch (viewType)` 必被 C1 判据捕获，`switch (region.Kind)` 不得误报。
    纯扫描（只读、0 写入）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))))
$views = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views'

$pattern = 'switch\s*\(\s*viewType\s*\)|if\s*\(\s*viewType\s*=='
$allowed = 'switch\s*\(\s*region\.Kind\s*\)'

function Get-ViewBranchHits([string]$text) {
    $code = $text -replace '(?m)^\s*//.*$', ''
    return [regex]::Matches($code, $pattern).Count
}
function Find-Violations {
    $hits = New-Object System.Collections.Generic.List[string]
    Get-ChildItem -LiteralPath $views -Recurse -Include *.cs -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
        ForEach-Object {
            $text = Get-Content -LiteralPath $_.FullName -Raw
            if ((Get-ViewBranchHits $text) -gt 0) { $hits.Add($_.FullName.Replace($repo, '').TrimStart('\')) }
        }
    return $hits
}

$rules = @(
    [pscustomobject]@{ Id = 'C1'; Title = '渲染层零视图分支（switch/if viewType）'
        Ok = { (Find-Violations).Count -eq 0 } }
    [pscustomobject]@{ Id = 'C2'; Title = '视图差异在目录声明（Regions）'
        Ok = { (Get-Content -LiteralPath (Join-Path $views 'Declaration\ConversationViewCatalog.cs') -Raw) -match 'Regions\s*:' } }
    [pscustomobject]@{ Id = 'C3'; Title = '渲染器按声明 regions 拼接（单点 foreach）'
        Ok = { (Get-Content -LiteralPath (Join-Path $views 'Rendering\ConversationPageRenderer.cs') -Raw) -match 'foreach\s*\(\s*var\s+region\s+in\s+model\.Regions\s*\)' } }
)

if ($NegativeControl) {
    $bad = 'switch (viewType) { case MainDialogViewType.Nodes: break; }'
    $good = 'switch (region.Kind) { case UiRegionKind.Nodes: break; }'
    $caught = (Get-ViewBranchHits $bad) -gt 0 -and (Get-ViewBranchHits $good) -eq 0 -and ($good -match $allowed)
    if (-not $caught) { throw '负对照失败：C1 判据未能区分视图分支与区域分派' }
    Write-Host '[OK] 负对照通过：视图分支被捕获，区域分派不误报。' -ForegroundColor Green
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
        Write-Host "UiNoViewBranch: $failed 项违规（视图差异未声明化）。" -ForegroundColor Red
        exit 1
    }
    Write-Host 'UiNoViewBranch: 全部通过。' -ForegroundColor Green
}

exit 0
