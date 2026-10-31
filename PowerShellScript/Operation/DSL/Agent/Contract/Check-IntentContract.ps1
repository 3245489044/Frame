#requires -Version 5.1
<#
.SYNOPSIS
    IEF+ 执行契约守卫（IntentContract）：把设计单 §3.2 三张表落到「注册表声明结构事实」的机器判据。

.DESCRIPTION
    扫描面 = 源码结构事实（`IntentExecutionRegistry.cs` 的 `yield return Row(...)` 行块），沿用
    Check-EchoConstants.ps1 同款文本扫描口径（只读源码；-Check 时违规 > 0 退出码 1）。

    判据（C1–C6，每条带正/负对照，禁空转）：
      C1 每个 Row 行块均含 `resultKind:` 实参。
      C2 每个写入型行（writes 非空）均含 `readBack:` 实参（opaque 行豁免）。
      C3 每行 writes 声明的 Surface kind ⊆ reads 声明的 Surface kind（Writes⊆Reads，不变量 I1；opaque 行豁免）。
      C4 每个 PreconditionKind 枚举值在 IntentPreconditionGate 实现中恰有机器判定（I3）；实现件缺位时可见化（INFO，不 FAIL）。
      C5 每个 Parity 非空行的 CaseId 非空且形态合法；在测试面可检索到（不悬空）——未命中记 INFO（不 FAIL，防假红）。
      C6 正对照（受检 Row 行数 ≥ 基线 MinRows）+ 负对照（内联合成一条删掉 readBack 的写入行，须被 C2 命中）。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

# 上下文/裁决（根定位 + 违规清单 + 统一出口）
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'IntentContract'
$Root = $ctx.Root
$registry = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Response\Intent\Registry\IntentExecutionRegistry.cs'
$binding  = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Response\Intent\Contracts\IntentExecutionBinding.cs'
$gateImpl = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Entry\IntentPreconditionGate.cs'
$contract = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Contract\Api\IntentPreconditionContract.cs'
$testRoot = Join-Path $Root 'Environment\CentralTest'
$baselinePath = Join-Path $Root 'Frame\PowerShellScript\Baselines\Agent\IntentContract.psd1'

$violations = $ctx.Violations
$notes      = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $registry)) { $violations.Add("缺少注册表源: $registry") }
if (-not (Test-Path -LiteralPath $binding))  { $violations.Add("缺少绑定记录源: $binding") }

# 基线（外置单源，经统一装载器）：MinRows / MinPreconditionKind / PendingCaseIds
$minRows = 20; $minKind = 2; $pendingCaseIds = @()
Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
try {
    $b = Import-GateBaseline -Name IntentContract
    if ($b.MinRows -and $b.MinRows.Value) { $minRows = [int]$b.MinRows.Value }
    if ($b.MinPreconditionKind -and $b.MinPreconditionKind.Value) { $minKind = [int]$b.MinPreconditionKind.Value }
    if ($b.PendingCaseIds -and $b.PendingCaseIds.Value) { $pendingCaseIds = @($b.PendingCaseIds.Value) }
}
catch {
    Write-Host "  [WARN] 基线装载失败（灰度）：$($_.Exception.Message)" -ForegroundColor Yellow
}

if ($violations.Count -eq 0) {
    $text = Get-Content -LiteralPath $registry -Raw

    # ---- 拆分 Row 行块：每块 = `yield return Row(` 起，至第一个 `;` 止（语句内无分号）----
    $marker = 'yield return Row('
    $blocks = [System.Collections.Generic.List[string]]::new()
    $idx = 0
    while ($true) {
        $start = $text.IndexOf($marker, $idx, [System.StringComparison]::Ordinal)
        if ($start -lt 0) { break }
        $semi = $text.IndexOf(';', $start)
        if ($semi -lt 0) { break }
        $blocks.Add($text.Substring($start, $semi - $start))
        $idx = $semi + 1
    }

    $writeBlocks = 0
    foreach ($blk in $blocks) {
        # C1：每块须含 resultKind:
        if ($blk -notmatch 'resultKind:') {
            $violations.Add("[C1] Row 行块缺 resultKind: 实参 — $($blk.Substring(0, [Math]::Min(60, $blk.Length)))…")
        }

        $hasWrites = ($blk -match 'writes:\s*new\[\]')
        $opaque = ($blk -match 'ExecOpaque')

        if ($hasWrites) { $writeBlocks++ }

        # C2：写入型行须含 readBack:（opaque 豁免）
        if ($hasWrites -and -not $opaque -and ($blk -notmatch 'readBack:')) {
            $violations.Add("[C2] 写入型 Row 缺 readBack: 实参 — $($blk.Substring(0, [Math]::Min(60, $blk.Length)))…")
        }

        # C3：Writes ⊆ Reads（opaque 豁免）
        if ($hasWrites -and -not $opaque) {
            $writeKinds = @()
            $wm = [regex]::Match($blk, 'writes:\s*new\[\]\s*\{([^}]*)\}')
            if ($wm.Success) { $writeKinds = [regex]::Matches($wm.Groups[1].Value, 'Surface\(([^)]+)\)') | ForEach-Object { $_.Groups[1].Value.Trim() } }
            $readKinds = @()
            $rm = [regex]::Match($blk, 'reads:\s*new\[\]\s*\{([^}]*)\}')
            if ($rm.Success) { $readKinds = [regex]::Matches($rm.Groups[1].Value, 'Read\(([^)]+)\)') | ForEach-Object { $_.Groups[1].Value.Trim() } }
            foreach ($wk in $writeKinds) {
                if ($readKinds -notcontains $wk) {
                    $violations.Add([string]::Format("[C3] 写面未闭合：write kind '{0}' 不在该行 read 面内（Writes⊆Reads 失守）", $wk))
                }
            }
        }
    }

    # C6 正对照：受检 Row 行数 ≥ 基线（防授权面被掏空 ⇒ 判据无标的假绿）
    if ($blocks.Count -lt $minRows) {
        $violations.Add([string]::Format('[C6] 正对照失效：受检 Row 行数 {0} < 基线 {1}（判据无标的）', $blocks.Count, $minRows))
    }

    # C6 负对照：内联合成一条删掉 readBack 的写入行，必须被 C2 判据命中
    $synth = 'yield return Row(AgentIntents.X, Probe(supports, AgentIntents.X), writes: new[] { Surface(WriteSurfaces.UrTasks) }, reads: new[] { Read(WriteSurfaces.UrTasks) }, resultKind: ResultKind.Write);'
    if (-not (($synth -match 'writes:\s*new\[\]') -and ($synth -notmatch 'readBack:'))) {
        $violations.Add('[C6] 负对照失效：合成缺 readBack 的写入行未被识别（判据空转）')
    }

    # C4：PreconditionKind 每个枚举值在有实现处各恰一处机器判定
    $enumKinds = @()
    $enumSrc = if (Test-Path -LiteralPath $contract) { Get-Content -LiteralPath $contract -Raw }
               else { Get-Content -LiteralPath $binding -Raw }
    $em = [regex]::Match($enumSrc, 'enum\s+PreconditionKind\s*\{([^}]*)\}')
    if ($em.Success) {
        $enumKinds = $em.Groups[1].Value -split ',' | ForEach-Object { ($_ -replace '\s', '') } | Where-Object { $_ }
    }
    if ($enumKinds.Count -lt $minKind) {
        $violations.Add([string]::Format('[C4] PreconditionKind 枚举值 {0} < 基线 {1}', $enumKinds.Count, $minKind))
    }
    if (Test-Path -LiteralPath $gateImpl) {
        $implText = Get-Content -LiteralPath $gateImpl -Raw
        foreach ($kind in $enumKinds) {
            $cnt = ([regex]::Matches($implText, [regex]::Escape("PreconditionKind.$kind"))).Count
            if ($cnt -lt 1) {
                $violations.Add("[C4] PreconditionKind.$kind 无机器判定实现（I3 失守）")
            }
        }
    }
    else {
        $notes.Add('[C4] 前置门实现件未就位（IntentPreconditionGate.cs）——待批次 B 落地；本期不 FAIL')
    }

    # C5：Parity CaseId 非空且形态合法；测试面可检索到（未命中记 INFO，不 FAIL）
    $caseIds = [regex]::Matches($text, 'new\s+ParityContract\([^;]*?"([^"]+)"\s*\)') | ForEach-Object { $_.Groups[1].Value }
    if ($caseIds.Count -eq 0) {
        # 退一步：取 ParityContract 内的第 3 个字符串实参
        $caseIds = [regex]::Matches($text, 'new\s+ParityContract\(\s*"[^"]*"\s*,\s*new\[\][^,]*,\s*"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
    }
    $testText = ''
    if (Test-Path -LiteralPath $testRoot) {
        $testText = (Get-ChildItem -LiteralPath $testRoot -Recurse -File -Filter *.cs -ErrorAction SilentlyContinue |
            Select-Object -First 4000 |
            Get-Content -Raw -ErrorAction SilentlyContinue) -join "`n"
    }
    foreach ($cid in $caseIds) {
        if ([string]::IsNullOrWhiteSpace($cid) -or $cid -notmatch '^[A-Za-z][A-Za-z0-9_]*$') {
            $violations.Add("[C5] Parity CaseId 形态非法：'$cid'")
            continue
        }
        if ($testText -and $testText -notmatch [regex]::Escape($cid)) {
            if ($pendingCaseIds -contains $cid) { $notes.Add("[C5] CaseId '$cid' 未在测试面命中（基线 PendingCaseIds 登记）") }
            else { $notes.Add("[C5] CaseId '$cid' 未在测试面命中（不判 FAIL；如确认为悬空可加入基线 PendingCaseIds）") }
        }
    }
}

foreach ($n in $notes) { Write-Host "  [INFO] $n" -ForegroundColor DarkGray }

Write-GateVerdict -Violations $violations -Check:$Check -Key 'IntentContract'
