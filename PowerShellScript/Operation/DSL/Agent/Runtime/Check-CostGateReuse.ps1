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
    Where-Object { $_.FullName -notmatch '\\\\(obj|bin)\\\\' })

# D1：pricing.json 解析只允许 CostConfigLoader（无第二份价格表读取/解析路径）。
foreach ($f in $files) {
    $text = Get-Content -LiteralPath $f.FullName -Raw
    if ($text -match 'pricing\.json' -and $f.Name -ne 'CostConfigLoader.cs') {
        Add-Violation 'D1' "第二份价格表读取点：$($f.FullName)"
    }
}

# D2：成本比较实现唯一（C_group ≤ C_single 的判据只允许一处）。
$cmp = @($files | Where-Object {
    (Get-Content -LiteralPath $_.FullName -Raw) -match 'groupTokens\s*>\s*singleTokens'
})
if ($cmp.Count -ne 1 -or $cmp[0].Name -ne 'ConcurrencyBudgetGate.cs') {
    Add-Violation 'D2' "成本比较实现应唯一于 ConcurrencyBudgetGate.cs，实际 $(@($cmp | ForEach-Object Name) -join ',')"
}

# D3：门禁消费价格表（IPriceTable）而非自带单价常量。
$gateFile = Join-Path $agentRoot 'Agent.Core\Engine\Concurrency\ConcurrencyBudgetGate.cs'
$gateText = if (Test-Path -LiteralPath $gateFile) { Get-Content -LiteralPath $gateFile -Raw } else { '' }
if ($gateText -notmatch 'IPriceTable') {
    Add-Violation 'D3' 'ConcurrencyBudgetGate 未消费 IPriceTable（价格来源不单一）'
}

# D4：预算/名额上限只允许来自声明策略（ConcurrencyPolicy），禁第二份配置读取。
if ($gateText -notmatch 'ConcurrencyPolicy\.ConcurrencyBudget') {
    Add-Violation 'D4' 'ConcurrencyBudgetGate 未读声明策略（ConcurrencyPolicy.ConcurrencyBudget）'
}

# D5：账本复用（ICostStore / ICostQuery），禁新建账本类型。
$recorder = Join-Path $agentRoot 'Agent.Core\Engine\Concurrency\ConcurrencyCostRecorder.cs'
$recorderText = if (Test-Path -LiteralPath $recorder) { Get-Content -LiteralPath $recorder -Raw } else { '' }
if ($recorderText -notmatch 'ICostStore' -or $recorderText -notmatch 'ICostQuery') {
    Add-Violation 'D5' '并发成本收口未复用 ICostStore/ICostQuery（疑似新建账本）'
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
    }
    if (-not ($checks.D1 -and $checks.D2)) {
        Add-Violation 'D6' '负对照未触发（判据恒真/过窄）'
    }
}

if ($violations.Count -eq 0) {
    Write-Host '[OK] CostGateReuse：D1–D6 通过。' -ForegroundColor Green
    exit 0
}

Write-Host '[FAIL] CostGateReuse 违规：' -ForegroundColor Red
$violations | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
