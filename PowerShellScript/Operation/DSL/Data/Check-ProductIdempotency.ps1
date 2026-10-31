# Check-ProductIdempotency.ps1
# 幂等键判据（DP6 / X5 / C17）：幂等键必填且不含易变分量（at/seq/round/timestamp）。
# 结构事实载体：结构门 pattern + 语义判据注册 + 易变分量词表三件（非执行者叙述）。
# 判据（-Check 时非零退出）：
#   ① products.schema.json 的 idempotencyKey 含负向前瞻 (at|seq|round|timestamp)
#   ② StandardRuleSet.cs 注册 C17 判据
#   ③ StandardConsistency.cs 含易变分量正则单源
# 负对照（单一实现）：DataCenter.Standards.Tests\ProductsLedgerTests 违规文案逐字断言。

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$schema = Join-Path $Root 'Frame\DataCenter\schemas\products.schema.json'
$ruleSet = Join-Path $Root 'Frame\DataCenter\DataCenter.Standards\StandardRuleSet.cs'
$consistency = Join-Path $Root 'Frame\DataCenter\DataCenter.Standards\StandardConsistency.cs'

$violations = [System.Collections.Generic.List[string]]::new()
foreach ($path in @($schema, $ruleSet, $consistency)) {
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host "缺件：$path"
        if ($Check) { exit 1 }
        exit 0
    }
}

$schemaText = Get-Content -LiteralPath $schema -Raw
if ($schemaText -notmatch '"idempotencyKey"\s*:\s*\{[^}]*\(at\|seq\|round\|timestamp\)') {
    $violations.Add("products.schema.json: idempotencyKey 缺易变分量负向前瞻")
}

$ruleText = Get-Content -LiteralPath $ruleSet -Raw
if ($ruleText -notmatch '"C17"') {
    $violations.Add("StandardRuleSet 未注册 C17 判据")
}

$consistencyText = Get-Content -LiteralPath $consistency -Raw
if ($consistencyText -notmatch '\\b\(at\|seq\|round\|timestamp\)\\b') {
    $violations.Add("StandardConsistency 缺易变分量正则单源")
}

Write-Host ("Check-ProductIdempotency: 违规={0}" -f $violations.Count)
if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "  [违规] $_" }
    if ($Check) { exit 1 }
}

exit 0
