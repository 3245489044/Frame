#requires -Version 5.1
<#
.SYNOPSIS
    会话页读面单一真源门禁（ConversationReadSingleSource）——「读面只读真源、零合成、零回退」的可机检判据。
    《Docs\analysis\Agent\refactor\2026-10-12-会话页读写单一真源缺口-填补与逻辑整理.md》§8.2

.DESCRIPTION
    判据形态（对齐 Check-SessionDuplication.ps1 口径）：
      ① 计数 = 命中**文件数**（Search-Repo -List 语义）；
      ② 每条断言只用一个字面量 pattern + 单个仓；
      ③ 检索一律经 Frame\Invoke\Search-Repo.ps1（唯一检索设施）。

    两种模式：
      默认（基线模式）—— 阈值 = Baseline：**只拦新增**（回归）；未达 Target 以 [待消除] 可见化，不判失败。
      -Check            —— 阈值 = Baseline（与默认相同，供 GateManifests 的 -Check 契约）。

    口径：
      · C1/C2 是**已销账**项（Baseline = Target = 0）：读面再现「兜底造 default 容器 / 合并映射」即红。
      · C3 锁定「列历史」实现点 = 1（唯一读面 = ConversationTreeModel 委派）。
      · C4 锁定读面 SessionKeys.Default 载体**只减不增**（Q3：仅作「当前选中」占位，不产行）。

.EXAMPLE
    .\Check-ConversationReadSingleSource.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$searchRepo = Join-Path $Root 'Frame\Invoke\Search-Repo.ps1'
if (-not (Test-Path -LiteralPath $searchRepo)) {
    Write-Host '[CRSS] 违规 1 项：'
    Write-Host "  - 检索设施缺失：$searchRepo（全部判据不可判，禁静默通过）"
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

    $cliArgs = @('-NoProfile', '-File', $searchRepo, '-Pattern', $Pattern, '-PathSpec', $PathSpec, '-Repo', $Repo, '-List')
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

$agentAll = 'Plug/Agent/src/**/*.cs'
$launcherModules = 'Plug/Agent/src/Agent.Launcher/Modules/**/*.cs'

$assertions = @(
    # ---- 正对照（检索面自检；命中 0 即判据面失效，禁假绿）----
    [pscustomobject]@{ Id = 'C0'; Title = '正对照：检索面可用'; Pattern = 'class ConversationSessionService'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 1; Target = 1; Kind = 'search' }

    # ---- 读面零合成（已销账 → 0，再现即红）----
    [pscustomobject]@{ Id = 'C1'; Title = '读面零兜底造 default 容器（F1/R1）'; Pattern = 'new HistoryNode(SessionKeys.Default'; PathSpec = $launcherModules; Repo = 'Projects'; Baseline = 0; Target = 0; Kind = 'search' }
    [pscustomobject]@{ Id = 'C2'; Title = '读面零合并映射残留（R5/MergeNodeKeyMaps）'; Pattern = 'MergeNodeKeyMaps'; PathSpec = $launcherModules; Repo = 'Projects'; Baseline = 0; Target = 0; Kind = 'search' }

    # ---- 列历史唯一实现点 ----
    [pscustomobject]@{ Id = 'C3'; Title = '「列历史」实现点 = 1（唯一读面委派）'; Pattern = 'ListHistoriesAsync'; PathSpec = $launcherModules; Repo = 'Projects'; Baseline = 1; Target = 1; Kind = 'search' }

    # ---- 读面 default 载体只减不增（Q3：仅作占位，不产行）----
    [pscustomobject]@{ Id = 'C4'; Title = '读面 SessionKeys.Default 载体只减不增（Q3 占位）'; Pattern = 'SessionKeys.Default'; PathSpec = $launcherModules; Repo = 'Projects'; Baseline = 2; Target = $null; Kind = 'search' }
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

    if ($actual -gt $a.Baseline) {
        $violations.Add("$($a.Id) $($a.Title)：实测 $actual > 基线 $($a.Baseline) —— 读面合成点新增")
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

Write-Host '[CRSS] 会话页读面单一真源门禁 · 基线模式（拦新增）'
Write-Host ("[CRSS] 断言 {0} 条 | 通过 {1} | 待消除 {2} | 违规 {3}" -f $assertions.Count, $passed, $pending.Count, $violations.Count)
if ($pending.Count -gt 0) {
    Write-Host '[CRSS] --- 待消除 ---'
    foreach ($p in $pending) { Write-Host "  - $p" }
}
if ($violations.Count -gt 0) {
    Write-Host "[CRSS] 违规 $($violations.Count) 项："
    foreach ($v in $violations) { Write-Host "  - $v" }
    exit 1
}
Write-Host '[CRSS] 通过：读面零合成/零回退（空 = 空）'
exit 0
