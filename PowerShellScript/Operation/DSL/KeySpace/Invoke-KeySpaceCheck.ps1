# Invoke-KeySpaceCheck.ps1
# KeySpace 组合动作（P3/P4）：只编排既有门禁，不复制任何判据。
#   · 默认：一次调用覆盖 GateManifests 中 Domain='KeySpace' 的全部门禁（Invoke-GateCheck -Domain）。
#   · -Json / -EmitActual：直接向 4 个带棘轮基线的门禁取 actual 表并聚合（供一次写基线）。
[CmdletBinding()]
param(
    [switch]$Json,
    [switch]$EmitActual,
    [ValidateSet('fast', 'full')][string]$Tier = 'fast',
    [int]$Parallel = 4,
    [string[]]$Only = @()
)

$ErrorActionPreference = 'Continue'
$gateCheck = Join-Path $PSScriptRoot '..\Tooling\Invoke-GateCheck.ps1'

if (-not $Json -and -not $EmitActual) {
    if ($Only.Count -gt 0) { & $gateCheck -Only $Only -Parallel $Parallel }
    else { & $gateCheck -Domain KeySpace -Tier $Tier -Parallel $Parallel }
    exit $(if ($null -eq $LASTEXITCODE) { 2 } else { $LASTEXITCODE })
}

# ---- actual 聚合面（判据载体 = 各门禁 -EmitActual 的结构化输出）----
$targets = if ($Only.Count -gt 0) { $Only } else { @('KeyOpsClosedSet', 'KeyDeclSingleSource', 'KeyRoundTrip', 'KeyCorpusGolden') }
$pwsh = Join-Path $PSHOME 'pwsh.exe'
if (-not (Test-Path -LiteralPath $pwsh)) { $pwsh = 'powershell.exe' }

$payload = [ordered]@{ schema = 'keyspace-check-v1'; passed = $true; gates = [ordered]@{} }
foreach ($name in $targets) {
    $path = Join-Path $PSScriptRoot ("Check-$name.ps1")
    if (-not (Test-Path -LiteralPath $path)) {
        $payload.gates[$name] = [ordered]@{ passed = $false; violations = @("脚本不存在：$path") }
        $payload.passed = $false
        continue
    }
    $raw = & $pwsh -NoProfile -File $path -Check -NegativeControl -EmitActual 2>$null
    $rc = if ($null -eq $LASTEXITCODE) { 2 } else { [int]$LASTEXITCODE }
    $text = (@($raw) | ForEach-Object { $_.ToString() }) -join "`n"
    $parsed = $null
    $s = $text.IndexOf('{'); $e = $text.LastIndexOf('}')
    if ($s -ge 0 -and $e -gt $s) {
        try { $parsed = $text.Substring($s, $e - $s + 1) | ConvertFrom-Json } catch { $parsed = $null }
    }
    if ($null -eq $parsed) {
        $payload.gates[$name] = [ordered]@{ passed = $false; violations = @("actual 不可解析：$text") }
        $payload.passed = $false
        continue
    }
    if ($EmitActual) {
        $payload.gates[$name] = $parsed.actual
    } else {
        $payload.gates[$name] = [ordered]@{ passed = ($rc -eq 0); actual = $parsed.actual }
    }
    if ($rc -ne 0) { $payload.passed = $false }
}

$payload | ConvertTo-Json -Depth 12
if ($payload.passed) { exit 0 }
exit 1
