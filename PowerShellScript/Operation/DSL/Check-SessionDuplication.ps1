#requires -Version 5.1
<#
.SYNOPSIS
    会话逻辑重复消除门禁（SessionDuplication）—— 台账 D1–D17 的可机检判据。
    《Docs\analysis\2026\9\9.26\Agent会话逻辑框架层重构-重复逻辑消除-重构方案.md》§6.2

.DESCRIPTION
    判据形态（对齐方案 §6.2 口径）：
      ① 计数 = 命中**文件数**（Search-Repo -List 语义），非出现次数；
      ② 每条断言只用一个字面量 pattern + 单个仓（Search-Repo 现实现下多 pattern / 多仓不生效）；
      ③ 检索一律经 Frame\Invoke\Search-Repo.ps1（唯一检索设施；禁手写等价命令，project.md §1.9）。

    两种模式：
      默认（基线模式）—— 阈值 = Baseline：**只拦新增重复点**（回归）；未达 Target 的项以 [待消除] 可见化，
                          不判失败。批开发期用此模式（灰名单），避免门禁恒红而失去信号。
      -Final（终态模式）—— 阈值 = Target：批完成后启用，未达 Target 即失败（方案 §6.2 终态断言）。

    口径登记（与方案 §6.2 / §7.2 对齐，勿擅改）：
      · A7 阶段键按 §7.2 D-6 建议① —— 「键 → 槽位类型」固定映射为**合法宿主绑定**，故 Target = 1（非 0）；
        本断言只拦「第二处阶段清单 / 第二处 switch」，不拦该映射本体。
      · A1 与 §6.2 的 A12、A3e/A3f 与 §6.2 的 A11 为同族编号（台账 D1/D4），此处一并落地，
        不重复检索同一 pattern。
      · A10 位置集零漂移为**运行期**判据（data\init-session 日志 / GUI 人肉），静态不可判
        ⇒ 列为 ATTEST（可见化，不判失败）；其静态替代物 = 4b 已建的
        AgentInstanceHosting.ReconcilePlacements（集合相等，漂移即 Error 诊断）。
      · 检索面口径：Projects 侧统一收窄到 `Plug/Agent/src/**/*.cs`（Agent 面）—— 若用 `*.cs` 全仓面，
        无关域会污染计数（实测 `RegisterDefaults(` 全仓 3 文件 vs Agent 面 2 文件，第 3 个在 `Route/src`），
        使台账「→0」的终态不可达。
      · A2b 口径（2026-09-26 收窄）：只认**静态**归一实现（`static string? ResolveSessionKey`，词表层 ClineDaemonEndpoint）；
        归档侧私有 `ResolveSessionKey` 仅转发平台单源 `SessionKeys.Resolve`，不构成第二实现。
        A2c–A2e 为「单源消费面」断言：锁定消费方必须经平台 `SessionKeys`（防再次散落自实现）。
        A1/A1b/A2 已销账（2026-09-26）→ Baseline 与 Target 均为 0（再现即红）。
      · 前置：A1–A3f 的**终态**依赖方案 §7.2 D-8（instances 身份模板拆分）与 D-9（键文法发射通电）裁决；
        未裁前保持基线模式即可（本脚本不阻塞）。

.PARAMETER Root
    仓库根（默认由本脚本上溯四级解析，与 Check-FactStandard.ps1 一致）。

.PARAMETER Final
    终态模式：按各断言 Target 判失败（批完成后启用）。

.PARAMETER ShowDetail
    输出逐条实测计数（含达标项）。

.EXAMPLE
    .\Check-SessionDuplication.ps1 -ShowDetail
    .\Check-SessionDuplication.ps1 -Final
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Final,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$searchRepo = Join-Path $Root 'Frame\Invoke\Search-Repo.ps1'
if (-not (Test-Path -LiteralPath $searchRepo)) {
    Write-Host "[SD] 违规 1 项："
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

$agentAll = 'Plug/Agent/src/**/*.cs'          # Projects 侧统一收窄到 Agent 面（避免 Route/Information 等无关域污染判据）
$launcherAll = 'Plug/Agent/src/Agent.Launcher/**/*.cs'
$launcherModules = 'Plug/Agent/src/Agent.Launcher/Modules/**/*.cs'
$frameConversation = 'CsharpPrj/Share/Frameworks/Conversation/**/*.cs'

# 台账断言表 —— Baseline = 2026-09-26 实测；Target = 批完成后终态（$null ⇒ 目标待批内定，仅拦回归）
$assertions = @(
    # ---- 正对照（检索面自检；命中 0 即判据面失效，禁假绿）----
    [pscustomobject]@{ Id = 'A0'; Title = '正对照：检索面可用'; Pattern = 'class ConversationSessionService'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 1; Target = 1; Batch = '—'; Kind = 'search' }

    # ---- C-1 身份与键单源（D1–D4）----
    [pscustomobject]@{ Id = 'A1'; Title = '会话名默认值调用点单源（D1/A12；2026-09-26 销账 → 0，单源 = 平台 SessionKeys.Default）'; Pattern = 'DefaultSessionName'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 0; Target = 0; Batch = 'C-1'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A1b'; Title = '归档默认会话键字段单源（D1；销账 → 0）'; Pattern = '_defaultSessionKey'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 0; Target = 0; Batch = 'C-1'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A2'; Title = '会话键归一第二套实现（D2/_keyMapper；销账 → 0）'; Pattern = '_keyMapper'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 0; Target = 0; Batch = 'C-1'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A2b'; Title = '会话键归一唯一静态实现（D2；词表层 ClineDaemonEndpoint；归档侧私有方法仅转发平台单源）'; Pattern = 'static string? ResolveSessionKey'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 1; Target = 1; Batch = 'C-1'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A2c'; Title = '归一单源消费面·Normalize（工作流层须经平台单源）'; Pattern = 'SessionKeys.Normalize'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 1; Target = 1; Batch = 'C-1'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A2d'; Title = '归一单源消费面·Resolve（归档层须经平台单源）'; Pattern = 'SessionKeys.Resolve'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 1; Target = 1; Batch = 'C-1'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A2e'; Title = '默认值单源消费面·Default（UI 面须经平台单源）'; Pattern = 'SessionKeys.Default'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 2; Target = 2; Batch = 'C-1'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A3a'; Title = '归档键前缀常量（D4/NodeKeyPrefix）'; Pattern = 'NodeKeyPrefix'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 1; Target = 0; Batch = 'C-1'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A3b'; Title = '归档轮次中缀常量（D4/RoundInfix）'; Pattern = 'RoundInfix'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 1; Target = 0; Batch = 'C-1'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A3c'; Title = '反解前缀常量（D4/ArchivePrefix）'; Pattern = 'ArchivePrefix'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 1; Target = 0; Batch = 'C-1'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A3d'; Title = '反解轮标记常量（D4/ArchiveRoundMarker）'; Pattern = 'ArchiveRoundMarker'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 1; Target = 0; Batch = 'C-1'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A3e'; Title = '键模板字面量·归档前缀（D4/A11）'; Pattern = '"cline.session."'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 2; Target = 0; Batch = 'C-1'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A3f'; Title = '键模板字面量·轮次中缀（D4/A11）'; Pattern = '".round."'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 2; Target = 0; Batch = 'C-1'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A3g'; Title = '轮次键存量形态·R 拼接（D3；S1 已收敛 → 0）'; Pattern = '+ "R" +'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 0; Target = 0; Batch = 'C-1'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A3h'; Title = '轮次键存量形态·@ 拼接（D3；S1 已收敛 → 0）'; Pattern = '"@" +'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 0; Target = 0; Batch = 'C-1'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A3i'; Title = '轮次键字面量·#R（D3；唯一生成器 FactIds.Round）'; Pattern = '"#R"'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 0; Target = 0; Batch = 'C-1'; Kind = 'search' }

    # ---- C-3 会话态迁实例（D14）----
    [pscustomobject]@{ Id = 'A8'; Title = '会话态零视图寄居（D14/_currentSession）'; Pattern = '_currentSession'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 1; Target = 0; Batch = 'C-3'; Kind = 'search' }

    # ---- C-4 层职责归位（D7/D8）----
    [pscustomobject]@{ Id = 'A5'; Title = '提示词装配单源（D7/PromptSnapshotBuilder.Build）'; Pattern = 'PromptSnapshotBuilder.Build'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 2; Target = 1; Batch = 'C-4'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A6a'; Title = '工具面 UI 零定位·目录面（D8/IToolFaceCatalog）'; Pattern = 'IToolFaceCatalog'; PathSpec = $launcherModules; Repo = 'Projects'; Baseline = 1; Target = 0; Batch = 'C-4'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A6b'; Title = '工具面 UI 零定位·快照面（D8/IToolFaceSnapshotStore）'; Pattern = 'IToolFaceSnapshotStore'; PathSpec = $launcherModules; Repo = 'Projects'; Baseline = 1; Target = 0; Batch = 'C-4'; Kind = 'search' }

    # ---- C-5 装配契约化 + 阶段声明化（D6/D12/D15）----
    [pscustomobject]@{ Id = 'A4'; Title = '渲染器构造单源（D6；2026-09-26 销账 → 1，单源 = ConversationRendererFactory）'; Pattern = 'new HtmlStreamRendererOptions'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 1; Target = 1; Batch = 'C-5'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A7'; Title = '阶段键映射不新增第二处清单（D12；口径见头部 D-6①）'; Pattern = 'case "initialize"'; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 1; Target = 1; Batch = 'C-5'; Kind = 'search' }

    # ---- C-6 读模型单源（D10/D11）----
    [pscustomobject]@{ Id = 'A15'; Title = '兼容历史投影器删除（D10/new HistoryProjector(）'; Pattern = 'new HistoryProjector('; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 1; Target = 0; Batch = 'C-6'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A16'; Title = 'UI 不直读历史真源（D11/GetHistoryAsync）'; Pattern = 'GetHistoryAsync'; PathSpec = $launcherAll; Repo = 'Projects'; Baseline = 4; Target = 0; Batch = 'C-6'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A17'; Title = 'UI 不直读日志真源（D11/GetLogAsync）'; Pattern = 'GetLogAsync'; PathSpec = $launcherAll; Repo = 'Projects'; Baseline = 0; Target = 0; Batch = 'C-6'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A18'; Title = 'UI 不直读任务构建真源（D11/BuildTaskAsync）'; Pattern = 'BuildTaskAsync'; PathSpec = $launcherAll; Repo = 'Projects'; Baseline = 2; Target = 0; Batch = 'C-6'; Kind = 'search' }

    # ---- C-7 载体无关化收尾（D9/D17；Target 待批内定 ⇒ 现仅拦回归）----
    [pscustomobject]@{ Id = 'A9'; Title = 'wire 静态注册零（D17/RegisterDefaults(）'; Pattern = 'RegisterDefaults('; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 2; Target = 0; Batch = 'C-7'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A13'; Title = '分层投影实现收敛（D9/ToStages(）'; Pattern = 'ToStages('; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 3; Target = $null; Batch = 'C-7'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A14'; Title = '重复抽取器删除（D9/ExtractStages(）'; Pattern = 'ExtractStages('; PathSpec = $agentAll; Repo = 'Projects'; Baseline = 1; Target = $null; Batch = 'C-7'; Kind = 'search' }
    [pscustomobject]@{ Id = 'A14b'; Title = '同族异名异型投影（D9/ToContributions(）'; Pattern = 'ToContributions('; PathSpec = $frameConversation; Repo = 'Frame'; Baseline = 3; Target = $null; Batch = 'C-7'; Kind = 'search' }

    # ---- 运行期判据（非静态可判 ⇒ ATTEST 可见化，不判失败）----
    [pscustomobject]@{ Id = 'A10'; Title = '位置集零漂移（D16；运行期：data\init-session 内 declaration-drift 命中 = 0）'; Pattern = ''; PathSpec = ''; Repo = ''; Baseline = $null; Target = $null; Batch = 'C-2'; Kind = 'attest' }
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
        $violations.Add("$($a.Id) $($a.Title)：实测 $actual > $hint（批 $($a.Batch)）—— 重复点新增/未收敛")
        if ($ShowDetail) {
            Write-Host ("  [超阈] {0,-4} {1,-46} {2,3}  ({3})" -f $a.Id, $a.Title, $actual, $hint)
        }
        continue
    }

    if ((-not $Final) -and ($null -ne $a.Target) -and ($actual -gt $a.Target)) {
        $pending.Add("$($a.Id) $($a.Title)：实测 $actual → 目标 $($a.Target)（批 $($a.Batch)）")
    } else {
        $passed++
    }

    if ($ShowDetail) {
        Write-Host ("  [实测] {0,-4} {1,-46} {2,3}  (基线 {3} / 目标 {4})" -f $a.Id, $a.Title, $actual, $a.Baseline, $a.Target)
    }
}

# ---- 报告 ----
$mode = if ($Final) { '终态模式 (-Final)' } else { '基线模式（拦新增重复点）' }
Write-Host "[SD] 会话逻辑重复消除门禁 · $mode"
Write-Host ("[SD] 断言 {0} 条（搜索 {1} / 运行期 {2}） | 通过 {3} | 待消除 {4} | 违规 {5}" -f `
    $assertions.Count, ($assertions | Where-Object { $_.Kind -eq 'search' }).Count, $attest.Count, $passed, $pending.Count, $violations.Count)

if ($pending.Count -gt 0) {
    Write-Host '[SD] --- 待消除（基线已锁定，批完成后转 Target 判据） ---'
    foreach ($p in $pending) { Write-Host "  - $p" }
}
if ($attest.Count -gt 0) {
    Write-Host '[SD] --- 运行期判据（GUI 人肉 / 日志核对，不判失败） ---'
    foreach ($t in $attest) { Write-Host "  - $t" }
}

if ($violations.Count -gt 0) {
    Write-Host "[SD] 违规 $($violations.Count) 项："
    foreach ($v in $violations) { Write-Host "  - $v" }
    exit 1
}

Write-Host "[SD] 通过：无新增重复点（待消除 $($pending.Count) 项见上）"
exit 0
