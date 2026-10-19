#requires -Version 5.1
<#
.SYNOPSIS
    分发追踪 schema 门禁（DTC-9）：phase 词表封闭 ∧ 新遥测 msgId 已登记。
.DESCRIPTION
    判据载体 = 结构事实（源码枚举 + msgId 注册表），非执行者叙述。
    ① DispatchTraceSchema.Phases 必须与管线中的阶段字面量一致（六段封闭）；
    ② 分发/休眠遥测的 AgentLog msgId 必须已在 agent_log_messages.json / module_log_messages.json 登记
       （未登记 ⇒ 运行期只出「日志消息定义未找到」，诊断静默 = 不可复核）。
    模式：默认报告；-Check 违规 → 退出码 1。只读仓库，零侧写。
.PARAMETER Check
    校验模式（退出码裁决）。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
while ($repoRoot -and $repoRoot -ne (Split-Path $repoRoot -Parent)) {
    if ((Test-Path (Join-Path $repoRoot 'Projects')) -and (Test-Path (Join-Path $repoRoot 'Frame'))) { break }
    $repoRoot = Split-Path $repoRoot -Parent
}

$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$Code, [string]$Message) {
    $violations.Add("$Code $Message")
    Write-Host "  [RED] $Code $Message" -ForegroundColor Red
}

# ① phase 词表封闭
$schemaFile = Join-Path $repoRoot 'Frame\CsharpPrj\Modules\KeySpace\Dispatch\DispatchTrace.cs'
$expected = @('resolve', 'normalize', 'plan', 'gate', 'activate', 'observe')
if (-not (Test-Path -LiteralPath $schemaFile)) {
    Add-Violation 'DTS1' "缺少 DispatchTraceSchema 载体: $schemaFile"
}
else {
    $schemaText = Get-Content -LiteralPath $schemaFile -Raw
    foreach ($phase in $expected) {
        if ($schemaText -notmatch [regex]::Escape("`"$phase`"")) {
            Add-Violation 'DTS1' "schema 缺少阶段字面量: $phase"
        }
    }
    if ($schemaText -notmatch 'IsKnown') { Add-Violation 'DTS1' 'schema 缺少 IsKnown 发射前校验' }
}

# ② 遥测 msgId 登记
$registered = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$logFiles = @(
    (Join-Path $repoRoot 'Projects\Plug\Agent\src\Agent.Core\Diagnostics\agent_log_messages.json'),
    (Join-Path $repoRoot 'Projects\Plug\Agent\src\Agent.Modules\Framework\Diagnostics\module_log_messages.json')
)
foreach ($file in $logFiles) {
    if (-not (Test-Path -LiteralPath $file)) { Add-Violation 'DTS2' "缺少日志消息定义: $file"; continue }
    $json = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    foreach ($name in $json.messages.PSObject.Properties.Name) { [void]$registered.Add($name) }
}

$domainRoot = Join-Path $repoRoot 'Projects\Plug\Agent\src'
$emitFiles = Get-ChildItem -Path $domainRoot -Recurse -File -Filter '*.cs' |
    Select-String -Pattern 'AgentLog\.Log\("(?<id>Dispatch[A-Za-z0-9_]+|Dormant[A-Za-z0-9_]+)"' -AllMatches
foreach ($match in $emitFiles) {
    foreach ($m in $match.Matches) {
        $id = $m.Groups['id'].Value
        if (-not $registered.Contains($id)) {
            Add-Violation 'DTS2' "遥测 msgId 未登记: $id（$($match.Path)）"
        }
    }
}

if ($violations.Count -eq 0) {
    Write-Host "  [OK] DispatchTraceSchema：阶段封闭 ∧ 遥测 msgId 已登记" -ForegroundColor Green
}

if ($Check -and $violations.Count -gt 0) { exit 1 }
