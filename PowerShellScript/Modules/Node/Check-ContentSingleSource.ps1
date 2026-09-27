#requires -Version 5.1
<#
.SYNOPSIS
    内容单源门禁（ContentSingleSource）—— 会话全链工作流化 N1–N5 / J1–J6 的可机检判据。
    《Projects\Plug\Agent\doc\会话全链工作流化-全文基于节点与工具调用不丢弃-重构方案.md》§6.1

.DESCRIPTION
    不变式（方案 §1.1）：N1 节点即载体 / N2 一事实一节点 / N3 多位置非双写 / N4 其余皆引用 / N5 面即投影。
    判据（方案 §6.1）：
      J1 内容单身份（同一 hash(text) 的 identityKey 数 ≤ 1）—— **真身为运行期**（库内载体面），
         静态仅可判其**写路径载体面**：本脚本以 5 条写路径中可静态识别者作代理断言（J1a–J1e），真身列 ATTEST；
      J2 骨架持引用（端口值含全文 = 0）；
      J3 边指内容节点 —— 真身运行期，列 ATTEST；
      J4 取全文单入口（J4a–J4e；W5 引入 deref 后收敛）；
      J5 工具调用不丢弃 —— 运行期对账，列 ATTEST；
      J6 写侧零角色过滤（`Role != ConversationRole.Tool` 调用点 = 0）。

    口径（与 Check-SessionDuplication.ps1 一致，勿擅改）：
      ① 计数 = 命中**文件数**（Search-Repo -List 语义），非出现次数；
      ② 每条断言只用一个字面量 pattern + 单个仓（Search-Repo 现实现下多 pattern / 多仓不生效）；
      ③ 检索一律经 Frame\Invoke\Search-Repo.ps1（唯一检索设施；禁手写等价命令，project.md §1.9）。

    两种模式：
      默认（基线模式）—— 阈值 = Baseline：**只拦新增**（回归）；未达 Target 的项以 [待消除] 可见化，不判失败。
      -Final（终态模式）—— 阈值 = Target：W6 收口后启用，未达 Target 即失败。
      基线锁于 2026-09-27（W1 实测）；「只减不增」。

.PARAMETER Root
    仓库根（默认由本脚本上溯四级解析：Frame\PowerShellScript\Modules\Node → 仓库根）。

.PARAMETER Final
    终态模式：按各断言 Target 判失败（W6 收口后启用）。

.PARAMETER ShowDetail
    输出逐条实测计数（含达标项）。

.EXAMPLE
    .\Check-ContentSingleSource.ps1 -ShowDetail

.EXAMPLE
    .\Check-ContentSingleSource.ps1 -Final
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Final,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    # 脚本位于 Frame\PowerShellScript\Modules\Node\，上溯四级即仓库根
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$searchRepo = Join-Path $Root 'Frame\Invoke\Search-Repo.ps1'
if (-not (Test-Path -LiteralPath $searchRepo)) {
    Write-Host '[CS] 违规 1 项：'
    Write-Host "  - 检索设施缺失：$searchRepo（全部判据不可判，禁静默通过）"
    exit 2
}

# 子进程 shell：Search-Repo.ps1 末尾调用 exit ⇒ 必须隔离进程，否则会终止本脚本
$shellExe = if (Test-Path (Join-Path $PSHOME 'pwsh.exe')) {
    Join-Path $PSHOME 'pwsh.exe'
} else {
    Join-Path $PSHOME 'powershell.exe'
}

# 检索一次 → 命中**文件数**；不可用返回 -1（零静默失败：不可用即违规，不得当作 0 通过）
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

$agentAll = 'Plug/Agent/src/**/*.cs'   # Projects 侧统一收窄到 Agent 面（方案 §6.1 口径）


$assertions = @(
    # ---- J1 内容单身份（静态代理：§2.1 五条写路径中的可静态识别者）----
    [pscustomobject]@{ Id = 'J1a'; Title = '整文档 blob 写点（ur_histories UpsertAsync；W4 转 legacy）'; Pattern = 'UpsertAsync'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 6; Target = $null; Batch = 'W4'; Kind = 'search' }
    [pscustomobject]@{ Id = 'J1b'; Title = '内容副本投影落点（RoundMessagePayload；W4 只挂边）'; Pattern = 'RoundMessagePayload'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 2; Target = $null; Batch = 'W4'; Kind = 'search' }
    [pscustomobject]@{ Id = 'J1c'; Title = '归档 blob 写点（HistoryClineMessageArchive；W4 转 archive-raw）'; Pattern = 'HistoryClineMessageArchive'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 7; Target = $null; Batch = 'W4'; Kind = 'search' }
    [pscustomobject]@{ Id = 'J1d'; Title = '副本消息键构造点（BuildMessageKey；W4 退场）'; Pattern = 'BuildMessageKey'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 3; Target = 0; Batch = 'W4'; Kind = 'search' }
    [pscustomobject]@{ Id = 'J1e'; Title = '历史 blob 载体面（ur_histories；W4 收口）'; Pattern = 'ur_histories'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 30; Target = $null; Batch = 'W4'; Kind = 'search' }

    # ---- J2 骨架持引用（端口值含全文 = 0；W4 改 {node_ref,digest,preview}）----
    [pscustomobject]@{ Id = 'J2'; Title = '步骤节点端口全文取值（ClineResult.Text）'; Pattern = 'ClineResult.Text'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 3; Target = 0; Batch = 'W4'; Kind = 'search' }

    # ---- J4 取全文单入口（W5 引入 deref 后收敛）----
    [pscustomobject]@{ Id = 'J4a'; Title = '任务节点读面（GetTaskNodeKeysAsync）'; Pattern = 'GetTaskNodeKeysAsync'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 4; Target = $null; Batch = 'W5'; Kind = 'search' }
    [pscustomobject]@{ Id = 'J4b'; Title = '历史 blob 读面（GetHistoryAsync）'; Pattern = 'GetHistoryAsync'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 7; Target = $null; Batch = 'W5'; Kind = 'search' }
    [pscustomobject]@{ Id = 'J4c'; Title = '会话日志读面（GetLogAsync）'; Pattern = 'GetLogAsync'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 2; Target = $null; Batch = 'W5'; Kind = 'search' }
    [pscustomobject]@{ Id = 'J4d'; Title = '历史存储端口面（IHistoryStore；W5 deref 收口）'; Pattern = 'IHistoryStore'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 18; Target = $null; Batch = 'W5'; Kind = 'search' }
    [pscustomobject]@{ Id = 'J4e'; Title = '节点库载体面（ur_nodes；W5 读面收口）'; Pattern = 'ur_nodes'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 54; Target = $null; Batch = 'W5'; Kind = 'search' }

    # ---- J6 写侧零角色过滤（W5 先删该过滤）----
    [pscustomobject]@{ Id = 'J6'; Title = '写侧角色过滤（Role != ConversationRole.Tool）'; Pattern = 'Role != ConversationRole.Tool'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 1; Target = 0; Batch = 'W5'; Kind = 'search' }

    # ---- 运行期判据（非静态可判 ⇒ ATTEST 可见化，不判失败）----
    [pscustomobject]@{ Id = 'J1'; Title = '内容单身份真身（同 hash(text) 的 identityKey 数 ≤ 1；W4 同轮正文身份数 3 → 1）'; Pattern = ''; PathSpec = ''; Repo = ''; Baseline = $null; Target = $null; Batch = 'W4'; Kind = 'attest' }
    [pscustomobject]@{ Id = 'J3'; Title = '边指内容节点（session_owns / cross_db_link 目标 = 内容节点键；用例断言）'; Pattern = ''; PathSpec = ''; Repo = ''; Baseline = $null; Target = $null; Batch = 'W4'; Kind = 'attest' }
    [pscustomobject]@{ Id = 'J5'; Title = '工具调用不丢弃（tool_use 节点数 = 归档 use_tool 条数；W2 起真实 run 对账）'; Pattern = ''; PathSpec = ''; Repo = ''; Baseline = $null; Target = $null; Batch = 'W2'; Kind = 'attest' }
)

# ---- 执行断言 ----
$violations = [System.Collections.Generic.List[string]]::new()
$pending = [System.Collections.Generic.List[string]]::new()
$attest = [System.Collections.Generic.List[string]]::new()
$passed = 0

foreach ($a in $assertions) {
    if ($a.Kind -eq 'attest') {
        $attest.Add("$($a.Id) $($a.Title)")
        continue
    }

    $actual = Get-SearchFileCount -Pattern $a.Pattern -PathSpec $a.PathSpec -Repo $a.Repo
    if ($actual -lt 0) {
        # 检索不可用 ⇒ 判据缺证：不可判（不得按 0 通过；对齐 EmptyResultGate 的「未命中 ≠ 不存在」）
        $violations.Add("$($a.Id) 检索不可用（pattern='$($a.Pattern)' pathspec='$($a.PathSpec)' repo='$($a.Repo)'）—— 判据缺证即违规")
        continue
    }

    $finalMode = $Final -and ($null -ne $a.Target)
    $threshold = if ($finalMode) { $a.Target } else { $a.Baseline }

    if ($actual -gt $threshold) {
        $hint = if ($finalMode) { "目标 $($a.Target)" } else { "基线 $($a.Baseline)" }
        $violations.Add("$($a.Id) $($a.Title)：实测 $actual > $hint（批 $($a.Batch)）—— 内容多身份写点新增/未收敛")
        if ($ShowDetail) {
            Write-Host ("  [超阈] {0,-4} {1,-52} {2,3}  ({3})" -f $a.Id, $a.Title, $actual, $hint)
        }
        continue
    }

    if ((-not $Final) -and ($null -ne $a.Target) -and ($actual -gt $a.Target)) {
        $pending.Add("$($a.Id) $($a.Title)：实测 $actual → 目标 $($a.Target)（批 $($a.Batch)）")
    } else {
        $passed++
    }

    if ($ShowDetail) {
        Write-Host ("  [实测] {0,-4} {1,-52} {2,3}  (基线 {3} / 目标 {4})" -f $a.Id, $a.Title, $actual, $a.Baseline, $a.Target)
    }
}

# ---- 报告 ----
$mode = if ($Final) { '终态模式 (-Final)' } else { '基线模式（拦新增内容多身份写点）' }
Write-Host "[CS] 内容单源门禁 · $mode"
Write-Host ("[CS] 断言 {0} 条（搜索 {1} / 运行期 {2}） | 通过 {3} | 待消除 {4} | 违规 {5}" -f `
    $assertions.Count, ($assertions | Where-Object { $_.Kind -eq 'search' }).Count, $attest.Count, $passed, $pending.Count, $violations.Count)

if ($pending.Count -gt 0) {
    Write-Host '[CS] --- 待消除（基线已锁定，批完成后转 Target 判据） ---'
    foreach ($p in $pending) { Write-Host "  - $p" }
}
if ($attest.Count -gt 0) {
    Write-Host '[CS] --- 运行期判据（库内载体面 / GUI / 用例，不判失败） ---'
    foreach ($t in $attest) { Write-Host "  - $t" }
}

if ($violations.Count -gt 0) {
    Write-Host "[CS] 违规 $($violations.Count) 项："
    foreach ($v in $violations) { Write-Host "  - $v" }
    exit 1
}

Write-Host "[CS] 通过：无新增内容多身份写点（待消除 $($pending.Count) 项见上）"
exit 0
