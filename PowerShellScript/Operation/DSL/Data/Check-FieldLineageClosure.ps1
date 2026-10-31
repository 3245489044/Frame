# Check-FieldLineageClosure.ps1
# 字段血缘端点闭合判据（DP1 / X3 / C17）：data.* 边的目标字段必须 ⊆ 目标产品出字段。
# 结构事实载体：闭合判定体的存在 + 调用点计数 + 台账结构门（outFields 非空）三件可机器复算事实。
# 判据（-Check 时非零退出）：
#   ① LineageProjector.cs 含闭合判定体 EnsureEndpointClosed 且调用点 >= 2（声明边 + 观测边各一次）
#   ② 判定体含越权写禁词（'越权写'）与目标字段域（'OutFields'）
#   ③ products.schema.json 的 outFields 声明 minItems=1（结构门层先堵空承诺）
# 负对照（单一实现）：DataCenter.Tests\DataflowProjectionTests 越权 toFields 须抛。

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$projector = Join-Path $Root 'Frame\DataCenter\DataCenter.Core\Dataflow\LineageProjector.cs'
$schema = Join-Path $Root 'Frame\DataCenter\schemas\products.schema.json'

$violations = [System.Collections.Generic.List[string]]::new()
foreach ($path in @($projector, $schema)) {
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host "缺件：$path"
        if ($Check) { exit 1 }
        exit 0
    }
}

$text = Get-Content -LiteralPath $projector -Raw
$closureCalls = ([regex]::Matches($text, 'EnsureEndpointClosed\(')).Count
if ($closureCalls -lt 3) {
    $violations.Add("端点闭合调用点 = $closureCalls（须 >= 3：定义 + 声明边 + 观测边）")
}
foreach ($token in @('越权写', 'OutFields')) {
    if ($text -notmatch [regex]::Escape($token)) { $violations.Add("闭合判定体缺载体标记：$token") }
}

$schemaText = Get-Content -LiteralPath $schema -Raw
if ($schemaText -notmatch '"outFields"\s*:\s*\{[^}]*"minItems"\s*:\s*1') {
    $violations.Add("products.schema.json: outFields 未声明 minItems=1（空承诺未在结构门阻断）")
}

Write-Host ("Check-FieldLineageClosure: 闭合调用点={0} 违规={1}" -f $closureCalls, $violations.Count)
if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "  [违规] $_" }
    if ($Check) { exit 1 }
}

exit 0
