#requires -Version 7.0
<#
.SYNOPSIS
    调用治理守卫（CallGovernance）—— 票据/契约/预算/增益/归属 必须有**执行体**，禁停留在声明层。

.DESCRIPTION
    背景：`CallNecessityGate` / `ShellBudgetFlow` / `CallMergeGate` 等只写在 .clinerules（声明层），
    无任何运行期执行体 ⇒ 判据载体退化为执行者叙述（违 SEP.JudgmentCarrierStructural），
    实际后果：预算不生效、重复无增益调用无人拦、失败归属靠「再跑一次看看」。

    判据（载体 = 声明文件 + 源码结构事实；注释不参与）：
      G1 策略声明闭合：CallGovernance.psd1 在位；Mode ∈ {shadow,enforce}；ReasonCodes ⊇ R1..R8；
         Budgets 含 shell/diagnose/verify；PhaseDeny 的相位 ∈ {diagnose,edit,verify}。
      G2 裁决原语单源：CallGovernor.psm1 含 Open-CallLedger / Resolve-CallAdmissibility / Write-CallOutcome，
         且**零运行**（不含 dotnet / 测试执行体）。
      G3 入口接线：Invoke-Frame.ps1 同时出现 Resolve-CallAdmissibility 与 Write-CallOutcome（拒绝路径 exit 3）。
      G4 归属查表只读：Locate-Failure.ps1 在位，读 known-failures，且不含 dotnet / 测试执行体 / 写盘。
      G5 入参契约：Run-CSharpTests.ps1 含位置参数守卫（$args）与 -Test 误绑守卫。
      G6 声明层对齐：axioms.md 含 AX.CallAdmissibility 与 CALL_DISCRIMINATION_REQUIRED 参数。
      NC 负对照：缺 ReasonCodes 的合成策略必红；含 dotnet 的合成归属脚本必红。

    模式：默认报告；-Check 违规 → 退出码 1；-NegativeControl 自证判据可判红。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $d = $PSScriptRoot
    while ($d -and $d -ne (Split-Path $d -Parent)) {
        if ((Test-Path (Join-Path $d 'Frame')) -and (Test-Path (Join-Path $d 'Projects'))) { return $d }
        $d = Split-Path $d -Parent
    }
    throw '[CallGovernance] 无法定位仓库根（上溯未找到同时含 Frame\ 与 Projects\ 的目录）'
}

$root = Find-RepoRoot
$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$Id, [string]$Msg) { $script:violations.Add("$Id $Msg") }

$policyPath    = Join-Path $root 'Frame\PowerShellScript\Baselines\CallGovernance.psd1'
$governorPath  = Join-Path $root 'Frame\PowerShellScript\Baselines\CallGovernor.psm1'
$entryPath     = Join-Path $root 'Frame\Invoke\Invoke-Frame.ps1'
$locatePath    = Join-Path $root 'Frame\PowerShellScript\Operation\DSL\Tooling\Locate-Failure.ps1'
$runnerPath    = Join-Path $root 'Environment\CentralTest\Internal\Run-CSharpTests.ps1'
$axiomsPath    = Join-Path $root '.clinerules\axioms.md'

function Test-RunFree([string]$text) {
    return -not [regex]::IsMatch($text, '(?m)^\s*(?:&\s*)?dotnet\s+(?:test|build|run)\b')
}

function Test-WriteFree([string]$text) {
    return -not [regex]::IsMatch($text, 'Set-Content|Out-File|Add-Content')
}

function Test-ReasonCodeTable([string]$text) {
    foreach ($code in @('R1', 'R2', 'R3', 'R4', 'R5', 'R6', 'R7', 'R8')) {
        if (-not [regex]::IsMatch($text, ('(?m)^\s*' + $code + '\s*='))) { return $false }
    }
    return $true
}

# ---- G1 策略声明闭合 ----
if (-not (Test-Path -LiteralPath $policyPath)) {
    Add-Violation 'G1' '策略声明缺失：Frame\PowerShellScript\Baselines\CallGovernance.psd1'
}
else {
    $policy = Import-PowerShellDataFile -LiteralPath $policyPath
    foreach ($key in @('Schema', 'Mode', 'ReasonCodes', 'Budgets', 'ActionClass', 'PhaseDeny', 'Attribution', 'LedgerDir')) {
        if (-not $policy.ContainsKey($key)) { Add-Violation 'G1' "策略缺键：$key" }
    }
    if ($policy.ContainsKey('Mode') -and @('shadow', 'enforce') -notcontains [string]$policy['Mode']) {
        Add-Violation 'G1' ("Mode 取值非法：{0}（受控词：shadow/enforce）" -f $policy['Mode'])
    }
    if ($policy.ContainsKey('ReasonCodes')) {
        foreach ($code in @('R1', 'R2', 'R3', 'R4', 'R5', 'R6', 'R7', 'R8')) {
            if (-not $policy['ReasonCodes'].ContainsKey($code)) { Add-Violation 'G1' "原因码缺项：$code" }
        }
    }
    if ($policy.ContainsKey('Budgets')) {
        foreach ($acct in @('shell', 'diagnose', 'verify')) {
            if (-not $policy['Budgets'].ContainsKey($acct)) { Add-Violation 'G1' "预算缺分账：$acct" }
        }
    }
    if ($policy.ContainsKey('PhaseDeny')) {
        foreach ($phase in $policy['PhaseDeny'].Keys) {
            if (@('diagnose', 'edit', 'verify') -notcontains $phase) { Add-Violation 'G1' "PhaseDeny 相位未登记：$phase" }
        }
    }
}

# ---- G2 裁决原语单源 + 零运行 ----
if (-not (Test-Path -LiteralPath $governorPath)) {
    Add-Violation 'G2' '裁决原语缺失：Baselines\CallGovernor.psm1'
}
else {
    $governorText = Get-Content -LiteralPath $governorPath -Raw
    foreach ($fn in @('Open-CallLedger', 'Resolve-CallAdmissibility', 'Write-CallOutcome')) {
        if (-not $governorText.Contains($fn)) { Add-Violation 'G2' "裁决原语缺函数：$fn" }
    }
    if (-not (Test-RunFree $governorText)) { Add-Violation 'G2' '裁决原语出现 dotnet 调用（治理原语须零运行）' }
}

# ---- G3 入口接线 ----
if (-not (Test-Path -LiteralPath $entryPath)) {
    Add-Violation 'G3' '入口缺失：Frame\Invoke\Invoke-Frame.ps1'
}
else {
    $entryText = Get-Content -LiteralPath $entryPath -Raw
    if (-not $entryText.Contains('Resolve-CallAdmissibility')) { Add-Violation 'G3' '入口未调用 Resolve-CallAdmissibility（治理未接线）' }
    if (-not $entryText.Contains('Write-CallOutcome')) { Add-Violation 'G3' '入口未写账（Write-CallOutcome 缺失）' }
    if (-not [regex]::IsMatch($entryText, 'exit\s+3')) { Add-Violation 'G3' '入口缺拒绝退出码（exit 3）' }
}

# ---- G4 归属查表只读 ----
if (-not (Test-Path -LiteralPath $locatePath)) {
    Add-Violation 'G4' '归属查表缺失：Operation\DSL\Tooling\Locate-Failure.ps1'
}
else {
    $locateText = Get-Content -LiteralPath $locatePath -Raw
    if (-not $locateText.Contains('known-failures')) { Add-Violation 'G4' '归属查表未读 known-failures（T2 判据缺失）' }
    if (-not (Test-RunFree $locateText)) { Add-Violation 'G4' '归属查表出现 dotnet 调用（须零运行）' }
    if (-not (Test-WriteFree $locateText)) { Add-Violation 'G4' '归属查表出现写盘（须只读）' }
}

# ---- G5 入参契约 fail-fast ----
if (-not (Test-Path -LiteralPath $runnerPath)) {
    Add-Violation 'G5' '测试执行体缺失：Run-CSharpTests.ps1'
}
else {
    $runnerText = Get-Content -LiteralPath $runnerPath -Raw
    if (-not $runnerText.Contains('$args.Count')) { Add-Violation 'G5' '测试执行体缺位置参数守卫（$args.Count）' }
    if (-not $runnerText.Contains('错位绑定')) { Add-Violation 'G5' '测试执行体缺 switch 误跟值守卫（-Test 误绑）' }
}

# ---- G6 声明层对齐 ----
if (-not (Test-Path -LiteralPath $axiomsPath)) {
    Add-Violation 'G6' '声明层缺失：.clinerules\axioms.md'
}
else {
    $axiomsText = Get-Content -LiteralPath $axiomsPath -Raw
    if (-not $axiomsText.Contains('AX.CallAdmissibility')) { Add-Violation 'G6' 'axioms 缺 AX.CallAdmissibility（执行体无公理可回溯）' }
    if (-not $axiomsText.Contains('CALL_DISCRIMINATION_REQUIRED')) { Add-Violation 'G6' 'axioms 缺 % CALL_DISCRIMINATION_REQUIRED 参数' }
}

# ---- 负对照（判据自证可判红） ----
if ($NegativeControl) {
    $badPolicy = 'R1 = "no-ticket"'
    $goodPolicy = "R1 = `"a`"`nR2 = `"b`"`nR3 = `"c`"`nR4 = `"d`"`nR5 = `"e`"`nR6 = `"f`"`nR7 = `"g`"`nR8 = `"h`""
    if (Test-ReasonCodeTable $badPolicy) { Add-Violation 'NC' '负对照未命中：缺 R2..R8 的合成策略被判闭合' }
    if (-not (Test-ReasonCodeTable $goodPolicy)) { Add-Violation 'NC' '正对照未命中：R1..R8 齐全的合成策略被判缺项' }

    $badLocate = '& dotnet test SomeTests.csproj'
    if (Test-RunFree $badLocate) { Add-Violation 'NC' '负对照未命中：含 dotnet 的合成归属脚本被判只读' }

    $badWrite = 'Set-Content -LiteralPath $p -Value $x'
    if (Test-WriteFree $badWrite) { Add-Violation 'NC' '负对照未命中：含写盘的合成归属脚本被判只读' }

    if (-not (Test-RunFree 'Write-Host ''read-only''')) { Add-Violation 'NC' '正对照误报：只读脚本被判含 dotnet' }
    Write-Host '[CallGovernance] 负对照通过（策略缺码 / 含 dotnet / 含写盘 均可判红）'
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host "[VIOLATION] $v" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[CallGovernance] 通过（0 违规；声明闭合 + 原语零运行 + 入口接线 + 归属只读 + 入参 fail-fast）'
if ($Check) { exit 0 }
