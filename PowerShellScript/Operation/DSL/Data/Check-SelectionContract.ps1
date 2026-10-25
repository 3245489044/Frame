#requires -Version 5.1
<#
.SYNOPSIS
    选择态契约门禁（SelectionContract）——「作用域 × 槽」选择态的结构不变量可机检。

.DESCRIPTION
    判据形态（对齐 Check-ConversationReadSingleSource.ps1 口径）：
      ① 计数 = 命中**文件数**（Search-Repo -List 语义）；
      ② 每条断言只用一个字面量 pattern + 单个仓；
      ③ 检索一律经 Frame\Invoke\Invoke-Frame.ps1 -Action search-repo（唯一检索设施）。

    口径（治「切作用域丢选中」类缺陷的回归面）：
      · C1 切换器必须在声明面携带 view 槽（否则当前作用域不可回投，后端只能靠全局槽猜）。
      · C2 锚回退链（显式→粘滞→声明默认→Unknown）只有一处实现。
      · C3 领域消费点 = 2（会话页 + DB 页）——证明该能力是通用件而非为单一页面特制。
      · C4 导航命令禁止把空锚归一成占位键（BuildViewOutputAsync(ResolveSessionName(...)) 形态）。
    两种模式：默认 = 基线模式（只拦新增）；-Check = 同阈（供 GateManifests 契约）。

.EXAMPLE
    .\Check-SelectionContract.ps1 -Check -NegativeControl
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$searchEntry = Join-Path $Root 'Frame\Invoke\Invoke-Frame.ps1'
if (-not (Test-Path -LiteralPath $searchEntry)) {
    Write-Host '[SC] 违规 1 项：'
    Write-Host "  - 检索设施缺失：$searchEntry（全部判据不可判，禁静默通过）"
    exit 2
}

$shellExe = if (Test-Path (Join-Path $PSHOME 'pwsh.exe')) {
    Join-Path $PSHOME 'pwsh.exe'
} else {
    Join-Path $PSHOME 'powershell.exe'
}

function Get-SearchFileCount {
    param(
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$PathSpec,
        [Parameter(Mandatory = $true)][string]$Repo
    )

    # 检索语义：默认只查 git 索引（新文件/新目录不可见 ⇒ 门禁会漏「新增文件里的违规」）；
    # 选择态契约必须覆盖新增文件，故固定 -Untracked（未跟踪文件纳入判据面）。
    $cliArgs = @('-NoProfile', '-File', $searchEntry, '-Action', 'search-repo', '-Pattern', $Pattern, '-PathSpec', $PathSpec, '-Repo', $Repo, '-List', '-Untracked')
    $raw = @(& $shellExe @cliArgs 2>&1 | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0) { return -1 }

    foreach ($line in $raw) {
        if ($line -match '合计命中：\s*(\d+)') { return [int]$Matches[1] }
    }
    foreach ($line in $raw) {
        if ($line -match '（\s*(\d+)\s*命中）') { return [int]$Matches[1] }
    }
    return -1
}

$frameViewer = 'CsharpPrj/Plate/HtmlViewer/**/*.cs'
$frameAssets = 'CsharpPrj/Plate/HtmlViewer/Html/Assets/*.cs'
$agentAll = 'Plug/Agent/src/**/*.cs'
$navigationForm = 'BuildViewOutputAsync(ResolveSessionName'

$assertions = @(
    # ---- 正对照（检索面自检；命中 0 即判据面失效，禁假绿）----
    [pscustomobject]@{ Id = 'C0'; Title = '正对照：选择态存储可用'; Pattern = 'class UiSelectionStore'; PathSpec = $frameViewer; Repo = 'Frame'; Baseline = 1; Target = 1 }

    # ---- 声明面：切换器必须携带 view 槽（作用域可回投）----
    [pscustomobject]@{ Id = 'C1'; Title = 'segItem 声明 view 选择槽'; Pattern = 'data-select-slot="view"'; PathSpec = $frameAssets; Repo = 'Frame'; Baseline = 1; Target = 1 }

    # ---- 回退链唯一实现 ----
    [pscustomobject]@{ Id = 'C2'; Title = '锚回退链唯一实现（UiSelectionBinder.Resolve）'; Pattern = 'public static UiAnchor Resolve('; PathSpec = $frameViewer; Repo = 'Frame'; Baseline = 1; Target = 1 }

    # ---- 第二消费方（通用性实证：会话页 + DB 页）----
    [pscustomobject]@{ Id = 'C3'; Title = '领域消费点 = 2（会话页 + DB 页）'; Pattern = 'UiSelectionStore _selection'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 2; Target = 2 }

    # ---- 导航禁归一化占位键（已销账 → 0，再现即红）----
    [pscustomobject]@{ Id = 'C4'; Title = '导航禁把空锚归一为占位键'; Pattern = 'BuildViewOutputAsync(ResolveSessionName'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 0; Target = 0 }
)

$violations = [System.Collections.Generic.List[string]]::new()
$pending = [System.Collections.Generic.List[string]]::new()
$passed = 0

foreach ($a in $assertions) {
    $actual = Get-SearchFileCount -Pattern $a.Pattern -PathSpec $a.PathSpec -Repo $a.Repo
    if ($actual -lt 0) {
        $violations.Add("$($a.Id) 检索不可用（pattern='$($a.Pattern)'）—— 判据缺证即违规")
        continue
    }

    # 判据活性（防恒绿）：基线为正的断言实测 0 ⇒ 载体不可能命中断言面（pattern 写坏/被转义），判违规。
    if ($a.Baseline -gt 0 -and $actual -eq 0) {
        $violations.Add("$($a.Id) 判据面失效：基线 $($a.Baseline) 但实测 0（pattern='$($a.Pattern)'）—— 恒绿断言")
        continue
    }

    if ($actual -gt $a.Baseline) {
        $violations.Add("$($a.Id) $($a.Title)：实测 $actual > 基线 $($a.Baseline) —— 选择态契约新增违反面")
        if ($ShowDetail) { Write-Host ("  [超阈] {0,-4} {1,-46} {2,3} (基线 {3})" -f $a.Id, $a.Title, $actual, $a.Baseline) }
        continue
    }
    if (($null -ne $a.Target) -and ($actual -gt $a.Target)) {
        $pending.Add("$($a.Id) $($a.Title)：实测 $actual → 目标 $($a.Target)")
    } else {
        $passed++
    }
    if ($ShowDetail) { Write-Host ("  [实测] {0,-4} {1,-46} {2,3}  (基线 {3})" -f $a.Id, $a.Title, $actual, $a.Baseline) }
}

# ---- 负对照（判据可区分正/负样本；禁恒绿断言）----
if ($NegativeControl) {
    $bad = 'ConversationViewService.Shared.BuildViewOutputAsync(ResolveSessionName(msg.Session), ct)'
    $good = 'ConversationViewService.Shared.BuildViewOutputAsync(name, ct, msg.History)'
    $caught = $bad.Contains($navigationForm) -and (-not $good.Contains($navigationForm))
    if (-not $caught) {
        $violations.Add('C5 负对照失败：C4 判据未能区分正/负样本（恒真断言）')
    } elseif ($ShowDetail) {
        Write-Host '  [负对照] C4 判据可区分正/负样本'
    }
}

Write-Host '[SC] 选择态契约门禁 · 基线模式（拦新增）'
Write-Host ("[SC] 断言 {0} 条 | 通过 {1} | 待消除 {2} | 违规 {3}" -f $assertions.Count, $passed, $pending.Count, $violations.Count)
if ($pending.Count -gt 0) {
    Write-Host '[SC] --- 待消除 ---'
    foreach ($p in $pending) { Write-Host "  - $p" }
}
if ($violations.Count -gt 0) {
    Write-Host "[SC] 违规 $($violations.Count) 项："
    foreach ($v in $violations) { Write-Host "  - $v" }
    exit 1
}
Write-Host '[SC] 通过：作用域选择态声明/回退/消费面齐备'
exit 0
