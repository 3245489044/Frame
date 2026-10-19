# 键址传递框架门禁（KTF）：键文法/词表/存储/轨/双写声明齐备。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $d = $PSScriptRoot
    while ($d -and -not (Test-Path (Join-Path $d 'Projects\Plug\Agent'))) { $d = Split-Path $d -Parent }
    if (-not $d) { throw "TransferKey gate：未找到仓库根（Projects\Plug\Agent）" }
    $Root = $d
}

$violations = New-Object System.Collections.Generic.List[string]
$agentRoot = Join-Path $Root 'Projects\Plug\Agent'
$configRoot = Join-Path $agentRoot 'Config'
$dataRoot = Join-Path $Root 'Data\Agent\Config'

function Require-File([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path)) { $script:violations.Add("缺文件：$Label ($Path)") }
}

Require-File (Join-Path $agentRoot 'src\Agent.Session\Keys\TransferKeyCodec.cs') 'TransferKeyCodec'
Require-File (Join-Path $agentRoot 'src\Agent.Contract\Runtime\Transfer\ITransferKeyStore.cs') 'TransferKey DTO/端口'
Require-File (Join-Path $agentRoot 'src\Agent.Modules\Framework\Transfer\TransferKeyStore.cs') 'TransferKeyStore'
Require-File (Join-Path $agentRoot 'src\Agent.Modules\Framework\Transfer\TransferKeyOpsFunctions.cs') 'TransferKeyOpsFunctions'
Require-File (Join-Path $agentRoot 'src\Agent.McpRegistryHost\Tracks\TransferKeyCallableTrack.cs') 'TransferKeyCallableTrack'
Require-File (Join-Path $configRoot 'workflows\transfer-key.ushar') '转移键声明面'
Require-File (Join-Path $dataRoot 'workflows\transfer-key.ushar') '数据根转移键声明面'

# 键文法：前缀 + 三段词表 + slot 有界（单一实现 = TransferKeyCodec）
$codec = Join-Path $agentRoot 'src\Agent.Session\Keys\TransferKeyCodec.cs'
if (Test-Path -LiteralPath $codec) {
    $text = Get-Content -LiteralPath $codec -Raw -Encoding UTF8
    foreach ($needle in @('xfer:', 'history', 'task', 'node', 'request', 'result', 'event', 'context', 'ack', 'MaxSlotLength = 64')) {
        if ($text -notmatch [regex]::Escape($needle)) { $violations.Add("TransferKeyCodec 缺：$needle") }
    }
}

# 词表：方向 / kind / status 单一真源
$transfer = Join-Path $configRoot 'nodes\transfer.ushar'
if (Test-Path -LiteralPath $transfer) {
    $text = Get-Content -LiteralPath $transfer -Raw -Encoding UTF8
    foreach ($needle in @('TransferDirection', 'TransferKeyKind', 'TransferStatus', 'published', 'claimed', 'consumed', 'acked', 'expired')) {
        if ($text -notmatch [regex]::Escape($needle)) { $violations.Add("transfer.ushar 缺：$needle") }
    }
}

# 声明面：6 个 transfer_key_* 单步
$flow = Join-Path $configRoot 'workflows\transfer-key.ushar'
if (Test-Path -LiteralPath $flow) {
    $text = Get-Content -LiteralPath $flow -Raw -Encoding UTF8
    foreach ($fn in @('transfer_key_publish', 'transfer_key_claim', 'transfer_key_read', 'transfer_key_ack', 'transfer_key_route', 'transfer_key_link')) {
        if ($text -notmatch ("fn\s*=\s*" + [regex]::Escape($fn))) { $violations.Add("transfer-key.ushar 缺 fn=$fn") }
    }
}

# 键空间族（仓库 + 数据根）
foreach ($path in @((Join-Path $configRoot 'keyspaces\agent.keyspace.json'), (Join-Path $dataRoot 'keyspaces\agent.keyspace.json'))) {
    if (-not (Test-Path -LiteralPath $path)) { $violations.Add("缺键空间声明：$path"); continue }
    $text = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    if ($text -notmatch '"agent\.transfer"') { $violations.Add("键空间缺 agent.transfer：$path") }
    if ($text -notmatch 'xfer:\{layer\}:\{scope\}:\{kind\}:\{slot\}') { $violations.Add("键空间模板不匹配：$path") }
}

# 工具轨（仓库 + 数据根）
foreach ($path in @((Join-Path $configRoot 'tool-face.ushar'), (Join-Path $dataRoot 'tool-face.ushar'))) {
    if (-not (Test-Path -LiteralPath $path)) { $violations.Add("缺工具面声明：$path"); continue }
    $text = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    foreach ($needle in @('ps_runtime', 'transfer_key')) {
        if ($text -notmatch [regex]::Escape($needle)) { $violations.Add("tool-face 缺轨 $needle：$path") }
    }
}

# 子智能体档位（执行面全量）
foreach ($path in @((Join-Path $configRoot 'subagents.ushar'), (Join-Path $dataRoot 'subagents.ushar'))) {
    if (-not (Test-Path -LiteralPath $path)) { $violations.Add("缺子智能体声明：$path"); continue }
    $text = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    if ($text -notmatch 'tool_face\s*=\s*full') { $violations.Add("powershell 缺 tool_face=full：$path") }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "VIOLATION: $_" -ForegroundColor Red }
    if ($Check) { exit 1 }
    exit 0
}
Write-Host 'TransferKey gate OK' -ForegroundColor Green
exit 0
