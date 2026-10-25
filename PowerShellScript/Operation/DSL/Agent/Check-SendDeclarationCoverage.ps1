#requires -Version 5.1
<#
.SYNOPSIS
    发送面声明覆盖守卫（SendDeclarationCoverage）—— F2 发送面声明载体框架。

.DESCRIPTION
    判据：
      G1 零裸声明：`AgentUiLibraryConversation` 内 `ConversationSendStandardAsync(` 调用不得以 `, null, null)` 结尾
         （必须经 `SessionSendDeclaration` 显式派生透传）。
      G2 字面量单源：`Agent.Ui` 源内 `"dispatch"` 字面量 = 0（缺省模式只可引 `SessionPresentation.DefaultMode`）。
      G3 正/负对照：合成样本（裸 `null, null`）必被 G1 捕获（防判据空转）。
      G4 空集策略单源：`Agent.Ui` 源内 `IsEmptyHistory|IsKnownEmptyHistory` 引用 = 0
         （空集唯一裁决点 = 声明面 `history.dispatch.empty`，经 TargetSetPlanner + StateResponseCapability 消费）。
      G5 成员集读取单源：`Agent.Ui` 源内 `GetMembersAsync` 调用 = 0
         （读取经 SendTargetResolver → HistoryMemberSetReader，发送面不自行枚举）。

    模式：默认报告；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================'
Write-Host '  发送面声明覆盖守卫（SendDeclarationCoverage：F2）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$convFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Core\Functions\Conversation\AgentUiLibraryConversation.cs'
$uiDir    = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui'

if (-not (Test-Path -LiteralPath $convFile)) {
    $violations.Add("[G1] 缺件（判据不可判）：$convFile")
} else {
    $t = [System.IO.File]::ReadAllText($convFile)
    $naked = Get-Count $t 'ConversationSendStandardAsync\([^;]*,\s*null,\s*null\)'
    if ($naked -gt 0) { $violations.Add("[G1] 发送入口裸 null,null 传递 $naked 处（应经 SessionSendDeclaration 显式声明）") }
    if ($t -notmatch 'SessionSendDeclaration\.FromView') {
        $violations.Add('[G1] 发送入口未派生 SessionSendDeclaration.FromView（声明面缺失）')
    }
    # 负对照：合成样本必被捕获。
    $probe = 'await ConversationSendStandardAsync(key, text, ct, agent, null, null).ConfigureAwait(false);'
    if ((Get-Count $probe 'ConversationSendStandardAsync\([^;]*,\s*null,\s*null\)') -eq 0) {
        $violations.Add('[G3] 负对照失效：裸 null,null 合成样本未被判据捕获')
    }
}

# G2/G4/G5 共用同一次 UI 源枚举（单次扫描；禁重复递归）
$uiFiles = @()
if (Test-Path -LiteralPath $uiDir) {
    $uiFiles = @(Get-ChildItem -LiteralPath $uiDir -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue)
    $lit = [System.Collections.Generic.List[string]]::new()
    foreach ($f in $uiFiles) {
        if ([System.IO.File]::ReadAllText($f.FullName) -match '"dispatch"') { $lit.Add($f.Name) }
    }
    if ($lit.Count -gt 0) { $violations.Add("[G2] UI 层仍含 ""dispatch"" 字面量 $($lit.Count) 处（应引 SessionPresentation.DefaultMode）：$($lit -join ', ')") }

    # G4/G5 策略与读取单源：发送面不得自行裁决空集，也不得自行枚举成员。
    $policyHits = @($uiFiles | Where-Object { [System.IO.File]::ReadAllText($_.FullName) -match 'IsEmptyHistory|IsKnownEmptyHistory' })
    if ($policyHits.Count -gt 0) {
        $names = ($policyHits | ForEach-Object { $_.Name }) -join ', '
        $violations.Add("[G4] 发送面仍含空集策略分支 $($policyHits.Count) 处（唯一裁决点=history.dispatch.empty）：$names")
    }
    $memberHits = @($uiFiles | Where-Object { [System.IO.File]::ReadAllText($_.FullName) -match 'GetMembersAsync' })
    if ($memberHits.Count -gt 0) {
        $names = ($memberHits | ForEach-Object { $_.Name }) -join ', '
        $violations.Add("[G5] 发送面仍直读成员集 $($memberHits.Count) 处（应经 SendTargetResolver/HistoryMemberSetReader）：$names")
    }
    # 负对照：两条判据的合成样本必须命中。
    if ((Get-Count 'target.IsKnownEmptyHistory' 'IsEmptyHistory|IsKnownEmptyHistory') -eq 0) {
        $violations.Add('[G4] 负对照失效：空集策略合成样本未被判据捕获')
    }
    if ((Get-Count 'tasks.GetMembersAsync(historyKey, ct)' 'GetMembersAsync') -eq 0) {
        $violations.Add('[G5] 负对照失效：成员集直读合成样本未被判据捕获')
    }
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1-G5 发送面声明/策略/读取单源通过（零裸声明 + 字面量单源 + 空集策略单点 + 成员集读取单源）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
