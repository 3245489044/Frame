#requires -Version 7.0
<#
.SYNOPSIS
    读取代数单源守卫（D03/D05）：空/失败/未装配必须经 UiReadState，禁字符串哨兵与本地三态枚举。
.DESCRIPTION
    C1 框架在位的代数：Frame UiReadGateway 含 UiReadState 四态（Ok/Empty/NotAttached/Failed）+ UiReadSpec。
    C2 领域消费：ConversationViewService 使用 UiReadGateway + UiReadState。
    C3 旧三态不残留：领域不得再出现 TaskConversationStatus / TaskConversationShape / TaskConversationResult。
    C4 负对照：合成 `private enum TaskConversationStatus` 样本必须被 C3 捕获。
    纯扫描（只读、0 写入）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))))
$gateway = Join-Path $repo 'Frame\CsharpPrj\Plate\HtmlViewer\Abstractions\UiReadGateway.cs'
$viewService = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Orchestration\ConversationViewService.cs'
$legacyPattern = 'TaskConversationStatus|TaskConversationShape|TaskConversationResult'

function Read-Text([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { throw "缺文件: $path" }
    return Get-Content -LiteralPath $path -Raw
}

$rules = @(
    [pscustomobject]@{ Id = 'C1'; Title = '框架代数在位（UiReadState 四态 + UiReadSpec）'
        Ok = { $t = Read-Text $gateway
            ($t -match 'enum UiReadState') -and ($t -match 'Ok') -and ($t -match 'Empty') -and ($t -match 'NotAttached') -and ($t -match 'Failed') -and ($t -match 'record UiReadSpec') } }
    [pscustomobject]@{ Id = 'C2'; Title = '领域消费读网关（UiReadGateway + UiReadState）'
        Ok = { $t = Read-Text $viewService; ($t -match 'UiReadGateway') -and ($t -match 'UiReadState') } }
    [pscustomobject]@{ Id = 'C3'; Title = '旧三态枚举不残留'
        Ok = { -not ((Read-Text $viewService) -match $legacyPattern) } }
)

if ($NegativeControl) {
    $bad = 'private enum TaskConversationStatus { Ok, Empty, NotAttached, Failed }'
    $good = 'private readonly record struct ReadResult(UiReadState State);'
    $caught = ($bad -match $legacyPattern) -and (-not ($good -match $legacyPattern))
    if (-not $caught) { throw '负对照失败：C3 判据未能区分正/负样本' }
    Write-Host '[OK] 负对照通过：C3 判据可捕获旧三态枚举。' -ForegroundColor Green
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
        Write-Host "UiReadOutcome: $failed 项违规（读取三态出现第二实现）。" -ForegroundColor Red
        exit 1
    }
    Write-Host 'UiReadOutcome: 全部通过。' -ForegroundColor Green
}

exit 0
