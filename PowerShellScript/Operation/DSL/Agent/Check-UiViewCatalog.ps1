#requires -Version 7.0
<#
.SYNOPSIS
    视图词汇单源守卫（D01）：History/Task/Nodes 的类型键/作用域/解析只允许在 ConversationViewCatalog 声明。
.DESCRIPTION
    C1 声明在位：ConversationViewCatalog.cs 含三视图字面量（node/task/history）。
    C2 消费方全部查表：ViewScope / ConversationPageRenderer / AgentUiLibraryConversation / ConversationViewService
       均出现 ConversationViewCatalog 引用（本地再写一份映射即破坏单源）。
    C3 领域旧映射不残留：上述四文件不得出现 `MainDialogViewType.X => "node|task|history"` 形态的本地映射。
    C4 负对照：合成旧映射样本必须被 C3 判据捕获。
    纯扫描（只读、0 写入）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))))
$agent = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui'
$views = Join-Path $agent 'Ui\Views'
$conv = Join-Path $agent 'Core\Functions\Conversation'

function Read-Text([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { throw "缺文件: $path" }
    return Get-Content -LiteralPath $path -Raw
}

$localMapPattern = 'MainDialogViewType\.(Task|Nodes|History)\s*=>\s*"(task|node|nodes|history)"'
$catalog = Join-Path $views 'ConversationViewCatalog.cs'
$consumers = @(
    (Join-Path $views 'ViewScope.cs'),
    (Join-Path $views 'ConversationPageRenderer.cs'),
    (Join-Path $views 'ConversationViewService.cs'),
    (Join-Path $conv 'AgentUiLibraryConversation.cs')
)

$rules = @(
    [pscustomobject]@{ Id = 'C1'; Title = '目录声明在位（node/task/history）'
        Ok = { $t = Read-Text $catalog; ($t -match '"node"') -and ($t -match '"task"') -and ($t -match '"history"') } }
    [pscustomobject]@{ Id = 'C2'; Title = '四个消费方均查表（ConversationViewCatalog）'
        Ok = { foreach ($f in $consumers) { if ((Read-Text $f) -notmatch 'ConversationViewCatalog') { return $false } }; return $true } }
    [pscustomobject]@{ Id = 'C3'; Title = '领域旧映射不残留（禁本地 MainDialogViewType => "type"）'
        Ok = { foreach ($f in $consumers) { if ((Read-Text $f) -match $localMapPattern) { return $false } }; return $true } }
)

if ($NegativeControl) {
    $bad = 'MainDialogViewType.Task => "task",'
    $ok = 'MainDialogViewType.Task => ConversationViewCatalog.TypeKeyOf(MainDialogViewType.Task),'
    $caught = ($bad -match $localMapPattern) -and (-not ($ok -match $localMapPattern))
    if (-not $caught) { throw '负对照失败：C3 判据未能区分正/负样本' }
    Write-Host '[OK] 负对照通过：C3 判据可捕获本地旧映射。' -ForegroundColor Green
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
        Write-Host "UiViewCatalog: $failed 项违规（视图词汇出现第二来源）。" -ForegroundColor Red
        exit 1
    }
    Write-Host 'UiViewCatalog: 全部通过。' -ForegroundColor Green
}

exit 0
