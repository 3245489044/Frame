# Check-CostGateReuse.ps1
# 成本门禁复用守卫（D1–D5）：价格表单一来源 / 无第二份预算比较实现 / 判据只读声明与只读查询端口。
# 用法：pwsh -File Check-CostGateReuse.ps1 [-Check] [-NegativeControl]

[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
while ($repoRoot -and -not (Test-Path (Join-Path $repoRoot 'Projects\Plug\Agent'))) {
    $parent = Split-Path $repoRoot -Parent
    if ($parent -eq $repoRoot) { $repoRoot = $null; break }
    $repoRoot = $parent
}
if (-not $repoRoot) { Write-Host '[FAIL] 无法定位仓库根'; exit 2 }

$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$code, [string]$message) { $violations.Add("[$code] $message") | Out-Null }

$agentRoot = Join-Path $repoRoot 'Projects\Plug\Agent\src'
$files = @(Get-ChildItem -LiteralPath $agentRoot -Recurse -File -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' })

# 并发内核已下沉 Frame（Modules.Concurrency）：D2/D3/D4/D5 的判据载体同时覆盖该目录。
$concurrencyRoot = Join-Path $repoRoot 'Frame\CsharpPrj\Modules\Concurrency'
$files = @($files) + @(Get-ChildItem -LiteralPath $concurrencyRoot -Recurse -File -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' })

# D1：pricing.json 解析只允许 CostConfigLoader（路径常量/引用不算解析）。
foreach ($f in $files) {
    if ($f.Name -eq 'CostConfigLoader.cs') { continue }
    $text = Get-Content -LiteralPath $f.FullName -Raw
    if ($text -notmatch 'pricing\.json') { continue }
    if ($text -match 'JsonDocument\.Parse|File\.ReadAll(Text|Lines)|LoadPriceQuotes\s*\(') {
        Add-Violation 'D1' "第二份价格表解析点：$($f.FullName)"
    }
}

# D2：成本比较实现唯一（C_group ≤ C_single 的判据只允许一处）。
$cmp = @($files | Where-Object {
    (Get-Content -LiteralPath $_.FullName -Raw) -match 'groupTokens\s*>\s*singleTokens'
})
if ($cmp.Count -ne 1 -or $cmp[0].Name -ne 'ConcurrencyBudgetGate.cs') {
    Add-Violation 'D2' "成本比较实现应唯一于 ConcurrencyBudgetGate.cs，实际 $(@($cmp | ForEach-Object Name) -join ',')"
}

# D3：门禁消费价格源端口（ICostPriceSource）而非自带单价常量。
$gateFile = Join-Path $repoRoot 'Frame\CsharpPrj\Modules\Concurrency\Gate\ConcurrencyBudgetGate.cs'
$gateText = if (Test-Path -LiteralPath $gateFile) { Get-Content -LiteralPath $gateFile -Raw } else { '' }
if ($gateText -notmatch 'ICostPriceSource') {
    Add-Violation 'D3' 'ConcurrencyBudgetGate 未消费 ICostPriceSource（价格来源不单一）'
}

# D4：预算/名额上限只允许来自声明快照（ConcurrencyOptions），禁第二份配置读取。
if ($gateText -notmatch 'ConcurrencyOptions') {
    Add-Violation 'D4' 'ConcurrencyBudgetGate 未读声明快照（ConcurrencyOptions）'
}

# D5：账本复用（IConcurrencyCostSink / IConcurrencyUsageQuery 端口；Agent 适配器映射 ICostStore / ICostQuery），禁新建账本类型。
$recorder = Join-Path $repoRoot 'Frame\CsharpPrj\Modules\Concurrency\Cost\ConcurrencyCostRecorder.cs'
$recorderText = if (Test-Path -LiteralPath $recorder) { Get-Content -LiteralPath $recorder -Raw } else { '' }
if ($recorderText -notmatch 'IConcurrencyCostSink' -or $recorderText -notmatch 'IConcurrencyUsageQuery') {
    Add-Violation 'D5' '并发成本收口未复用成本端口（疑似新建账本）'
}
$costAdapter = Join-Path $agentRoot 'Agent.Core\Engine\Concurrency\Adapters\CostLedgerSinkAdapter.cs'
$costAdapterText = if (Test-Path -LiteralPath $costAdapter) { Get-Content -LiteralPath $costAdapter -Raw } else { '' }
if ($costAdapterText -notmatch 'ICostStore' -or $costAdapterText -notmatch 'ICostQuery') {
    Add-Violation 'D5' '成本端口未映射既有账本（ICostStore / ICostQuery）'
}

# D7：组结算（含 detach）统一写同一账本——detach 必须有后台结算路径（组行 + 成本 span + 校准）。
$portFile = Join-Path $agentRoot 'Agent.Launcher\Composition\Ports\TaskPoolConcurrencyPlanPort.cs'
$portText = if (Test-Path -LiteralPath $portFile) { Get-Content -LiteralPath $portFile -Raw } else { '' }
$adapterFile = Join-Path $agentRoot 'Agent.Core\Engine\TaskPool\AgentTaskPool.cs'
$adapterText = if (Test-Path -LiteralPath $adapterFile) { Get-Content -LiteralPath $adapterFile -Raw } else { '' }
if ($portText -notmatch 'SettleDetachedAsync' -or $adapterText -notmatch '_costRecorder') {
    Add-Violation 'D7' 'detach 未走后台结算/成本账（SettleDetachedAsync + _costRecorder 必须同时存在）'
}

# D8：校准桶按真实工作流（禁写死 agent-taskpool 单桶）。
if ($recorderText -match '"agent-taskpool"' -or $recorderText -notmatch 'WorkflowOf\s*\(') {
    Add-Violation 'D8' '校准桶写死单桶（应经 WorkflowOf 按组键前缀分桶）'
}

# D9：成本记账实例唯一（组合根 ConcurrencyComposition 内建；适配器禁自建第二本账）。
$recorderNews = @($files | Where-Object {
    (Get-Content -LiteralPath $_.FullName -Raw) -match 'new\s+ConcurrencyCostRecorder\s*\('
})
if ($recorderNews.Count -ne 1 -or $recorderNews[0].Name -ne 'ConcurrencyComposition.cs') {
    Add-Violation 'D9' "ConcurrencyCostRecorder 构造点应唯一于 ConcurrencyComposition.cs，实际 $(@($recorderNews | ForEach-Object Name) -join ',')"
}

# D10：策略快照单次生成（组合根一次；适配器禁再调 ConcurrencyPolicy.Snapshot）。
$snapshotSites = @($files | Where-Object {
    (Get-Content -LiteralPath $_.FullName -Raw) -match 'ConcurrencyPolicy\s*\.\s*Snapshot\s*\('
})
if ($snapshotSites.Count -ne 1 -or $snapshotSites[0].Name -ne 'DataStoreHosting.cs') {
    Add-Violation 'D10' "ConcurrencyPolicy.Snapshot() 应唯一于 DataStoreHosting.cs，实际 $(@($snapshotSites | ForEach-Object Name) -join ',')"
}

# D11：组提交只读票据（禁适配器二次评估）。
if ($adapterText -match '_concurrencyGate\s*\.\s*EvaluateAsync') {
    Add-Violation 'D11' 'AgentTaskPool.SubmitGroupAsync 仍保留无票据二次评估（应统一 CONCURRENCY_ADMISSION_REQUIRED）'
}
if ($adapterText -notmatch 'CONCURRENCY_ADMISSION_REQUIRED') {
    Add-Violation 'D11' 'AgentTaskPool 缺少 CONCURRENCY_ADMISSION_REQUIRED 拒绝码（票据单源判据不可判）'
}

# D6：负对照（判据必须能判红）。
if ($NegativeControl) {
    $negative = @'
File.ReadAllText("Config/cost/pricing.json")
if (groupTokens > singleTokens) { deny(); }
'@
    $checks = @{
        D1 = ($negative -match 'pricing\.json')
        D2 = ($negative -match 'groupTokens\s*>\s*singleTokens')
        D9 = ("var r = new ConcurrencyCostRecorder(o, s, q);" -match 'new\s+ConcurrencyCostRecorder\s*\(')
        D10 = ("var o = ConcurrencyPolicy.Snapshot();" -match 'ConcurrencyPolicy\s*\.\s*Snapshot\s*\(')
        D11 = ("await _concurrencyGate.EvaluateAsync(m, t, ct);" -match '_concurrencyGate\s*\.\s*EvaluateAsync')
    }
    if (-not ($checks.D1 -and $checks.D2 -and $checks.D9 -and $checks.D10 -and $checks.D11)) {
        Add-Violation 'D6' '负对照未触发（判据恒真/过窄）'
    }
}

if ($violations.Count -eq 0) {
    Write-Host '[OK] CostGateReuse：D1–D11 通过。' -ForegroundColor Green
    exit 0
}

Write-Host '[FAIL] CostGateReuse 违规：' -ForegroundColor Red
$violations | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
