#requires -Version 5.1
<#
.SYNOPSIS
    双载体互斥门（DualCarrierExclusion）—— 治 D3「同键双载体并存且无对账」。

.DESCRIPTION
    僵尸脚本 = 端口块内 `impl delegate`（求值走 C# 提供者）**与** `script [...]`（一段永不被求值的
    声明体）**并存**；二者互不校验 ⇒ 死脚本随时间腐化（设计文档 §3.2 D3：防腐设施已摘除，无守卫报警）。
    本件把该语义收敛为一条机器判据：新出现「delegate ∧ script」即红。

    判据（D1–D3）：
      D1 新增即红：实测僵尸端口集 ∖ 基线集 = 新增（非空即违规）。
      D2 只减不增：基线集 ∖ 实测集 = 可销账（可见化，不违规；销账后须下调基线）。
      D3 正/负对照（防假绿）：合成样本自证「delegate ∧ script 命中 / 仅其一不命中 / scripted ∧ script 不命中」。

    载体（结构事实）：`Environment\CentralTest\AgentTest\Agent.TestShared\Standards\ports\*.specs` 的 `port` 块。
    基线 = Baselines\DualCarrierExclusion.psd1（现存 5 处为存量债务，Origin=Debt，销账条件见 DueWhen）。

    模式：默认报告模式（不设退出码）；-Check 校验模式（违规 → 退出码 1）。
    依赖方向（硬约束）：本件只读仓库；零侧写。

.PARAMETER Root
    仓库根目录（默认由本脚本位置上溯推导，禁硬编码绝对路径）。
.PARAMETER Check
    校验模式：违规 → 退出码 1。
.PARAMETER ShowDetail
    输出逐命中点明细。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

# 定位仓库根（含 Frame\ 与 Environment\）—— 相对 $PSScriptRoot 上溯，禁硬编码绝对路径
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = $PSScriptRoot
    while ($Root -and $Root -ne (Split-Path $Root -Parent)) {
        if ((Test-Path (Join-Path $Root 'Frame')) -and (Test-Path (Join-Path $Root 'Environment'))) { break }
        $Root = Split-Path $Root -Parent
    }
}

# 基线单源 = Baselines\DualCarrierExclusion.psd1（禁内联数值基线；守卫 = Operation\DSL\Check-GateBaselines.ps1）
Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name DualCarrierExclusion

$violations = [System.Collections.Generic.List[string]]::new()
$notes = [System.Collections.Generic.List[string]]::new()

# ---- 纯函数：单块文本 → 是否「impl delegate ∧ script [...]」双载体 ----
function Test-ZombieBlock {
    param([Parameter(Mandatory = $true)][string]$Block)
    $hasDelegate = $Block -match '(?m)^\s*impl\s+delegate\b'
    $hasScript = $Block -match '(?m)^\s*script\s*\['
    return ($hasDelegate -and $hasScript)
}

# ---- D3 正/负对照（防假绿：判据对合成样本须命中/不命中） ----
$posSample = "port A.B`n  impl delegate`n    id A.B`n    script [ { } ]"
$negDelegateOnly = "port A.B`n  impl delegate`n    id A.B"
$negScripted = "port A.B`n  impl scripted`n    id A.B`n    script [ { } ]"
if (-not (Test-ZombieBlock -Block $posSample)) {
    $violations.Add('[D3] 正对照失效：合成「delegate ∧ script」未被判为僵尸（判据被改坏）')
}
if (Test-ZombieBlock -Block $negDelegateOnly) {
    $violations.Add('[D3] 负对照失效：仅 impl delegate 被误判为僵尸（判据过宽）')
}
if (Test-ZombieBlock -Block $negScripted) {
    $violations.Add('[D3] 负对照失效：impl scripted + script 被误判为僵尸（判据过宽）')
}

# ---- D1/D2 扫描：端口声明面逐 port 块判定 ----
# 扫描根 = 端口声明面单源（仓库相对；与 SemanticCentralization 硬编码 Frame\CsharpPrj 同惯例）
$portsRel = 'Environment\CentralTest\AgentTest\Agent.TestShared\Standards\ports'
$portsDir = Join-Path $Root $portsRel

$observed = [System.Collections.Generic.List[string]]::new()
if (Test-Path -LiteralPath $portsDir) {
    foreach ($f in @(Get-ChildItem -LiteralPath $portsDir -Filter '*.specs' -File -ErrorAction SilentlyContinue)) {
        $content = Get-Content -LiteralPath $f.FullName -Raw
        foreach ($m in [regex]::Matches($content, '(?ms)^port\s+(\S+).*?(?=^port\s+\S+|\z)')) {
            if (Test-ZombieBlock -Block $m.Value) { [void]$observed.Add($m.Groups[1].Value) }
        }
    }
} else {
    $violations.Add("[D1] 端口目录不存在：$portsRel（判据无标的，禁假绿）")
}
$observed = @($observed | Sort-Object -Unique)

$baselinePorts = @($baseline.ZombiePorts.Value | Sort-Object -Unique)
$newHits = @($observed | Where-Object { $baselinePorts -notcontains $_ })
$cleared = @($baselinePorts | Where-Object { $observed -notcontains $_ })

foreach ($k in $newHits) {
    $violations.Add("[D1] 新增僵尸脚本（impl delegate ∧ script 并存）：$k —— 二者择一：删僵尸 script 或改 impl kind")
}
foreach ($k in $cleared) {
    $notes.Add("[D2] 可销账（僵尸已清除，请下调基线 ZombiePorts）：$k")
}

# ---- 报告 ----
Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  双载体互斥门（DualCarrierExclusion · D3 僵尸脚本）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ("  端口目录: {0}" -f $portsRel) -ForegroundColor Gray
Write-Host ("  实测僵尸端口: {0} | 基线: {1} | 新增: {2} | 可销账: {3} | 违规: {4}" -f `
        $observed.Count, $baselinePorts.Count, $newHits.Count, $cleared.Count, $violations.Count) -ForegroundColor Gray

if ($ShowDetail) {
    foreach ($k in $observed) { Write-Host ("    [僵尸] {0}" -f $k) -ForegroundColor DarkGray }
}
foreach ($n in $notes) { Write-Host "  ! $n" -ForegroundColor Yellow }

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host '[OK] 双载体互斥门：0 违规（无新增 delegate ∧ script 并存；正/负对照绿）' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host ("[FAIL] 双载体互斥门命中 {0} 处：" -f $violations.Count) -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
Write-Host '  修复口径：端口块禁同时保留 delegate 求值体与 script 声明体；僵尸 script 须删或改 impl kind' -ForegroundColor Gray
if ($Check) { exit 1 }
exit 0
