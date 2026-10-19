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
# Data 根缺失不判红（E17）：干净 checkout 无 Data 时只跳过镜像检查并显式提示。
$dataAvailable = Test-Path -LiteralPath $dataRoot
if ($dataAvailable) {
    Require-File (Join-Path $dataRoot 'workflows\transfer-key.ushar') '数据根转移键声明面'
} else {
    Write-Host 'SKIP: Data\Agent\Config 不存在，跳过声明镜像检查。' -ForegroundColor Yellow
}

# 路由单实现（D1）：唯一实现文件存在，且两个入口都调用它（结构事实，不 grep 文案）。
$routing = Join-Path $agentRoot 'src\Agent.Modules\Framework\Transfer\TransferKeyRouting.cs'
if (-not (Test-Path -LiteralPath $routing)) {
    $violations.Add('缺 TransferKeyRouting.cs（路由唯一实现）')
} else {
    foreach ($entry in @(
            (Join-Path $agentRoot 'src\Agent.Modules\Framework\Transfer\TransferKeyOpsFunctions.cs'),
            (Join-Path $agentRoot 'src\Agent.McpRegistryHost\Tracks\TransferKeyCallableTrack.cs'))) {
        if (-not (Test-Path -LiteralPath $entry)) { $violations.Add("缺路由入口：$entry"); continue }
        $entryText = Get-Content -LiteralPath $entry -Raw -Encoding UTF8
        if ($entryText -notmatch 'TransferKeyRouting\.RouteAsync') {
            $violations.Add("路由入口未复用唯一实现：$entry")
        }
    }
}

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

# 词表集合等价（D6）：声明面 [Space] 值集 == C# 常量值集（不再靠子串包含）。
function Read-UsharpSpaceValues([string]$Path, [string]$SpaceName) {
    $values = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $Path)) { return $values }
    $in = $false
    foreach ($line in (Get-Content -LiteralPath $Path -Encoding UTF8)) {
        if ($line -match ('^\s*\[Space\]\s+' + [regex]::Escape($SpaceName) + '\b')) { $in = $true; continue }
        if ($in) {
            if ($line -match '^\s*\[Space\]') { break }
            $t = $line.Trim()
            if ($t -and -not $t.StartsWith('#')) { $values.Add($t) }
        }
    }
    return $values
}
function Read-CSharpConstValues([string]$Text, [string]$ClassName) {
    $m = [regex]::Match($Text, ('(?s)class\s+' + [regex]::Escape($ClassName) + '\s*\{(?<body>.*?)\n\s*\}'))
    if (-not $m.Success) { return @() }
    return @([regex]::Matches($m.Groups['body'].Value, 'public\s+const\s+string\s+\w+\s*=\s*"([^"]+)"') |
        ForEach-Object { $_.Groups[1].Value })
}

$contractText = Get-Content -LiteralPath (Join-Path $agentRoot 'src\Agent.Contract\Runtime\Transfer\ITransferKeyStore.cs') -Raw -Encoding UTF8
$transferPath = Join-Path $configRoot 'nodes\transfer.ushar'
$pairs = @(
    @{ Space = 'TransferDirection'; Class = 'TransferDirections'; Label = '方向词表' },
    @{ Space = 'TransferKeyKind'; Class = 'TransferKinds'; Label = '种类词表' },
    @{ Space = 'TransferStatus'; Class = 'TransferStatuses'; Label = '状态词表' }
)
foreach ($pair in $pairs) {
    $declared = @(Read-UsharpSpaceValues $transferPath $pair.Space | Sort-Object -Unique)
    $code = @(Read-CSharpConstValues $contractText $pair.Class | Sort-Object -Unique)
    if (($declared -join '|') -ne ($code -join '|')) {
        $violations.Add("$($pair.Label)集合不等价：declared=[$($declared -join ',')] code=[$($code -join ',')]")
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

# 键空间族（仓库必检；Data 仅在存在时做镜像检查）
$keyspacePaths = @(Join-Path $configRoot 'keyspaces\agent.keyspace.json')
if ($dataAvailable) { $keyspacePaths += Join-Path $dataRoot 'keyspaces\agent.keyspace.json' }
foreach ($path in $keyspacePaths) {
    if (-not (Test-Path -LiteralPath $path)) { $violations.Add("缺键空间声明：$path"); continue }
    $text = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    if ($text -notmatch '"agent\.transfer"') { $violations.Add("键空间缺 agent.transfer：$path") }
    if ($text -notmatch 'xfer:\{layer\}:\{scope\}:\{kind\}:\{slot\}') { $violations.Add("键空间模板不匹配：$path") }
}

# 工具轨（仓库必检；Data 仅在存在时做镜像检查）
$toolFacePaths = @(Join-Path $configRoot 'tool-face.ushar')
if ($dataAvailable) { $toolFacePaths += Join-Path $dataRoot 'tool-face.ushar' }
foreach ($path in $toolFacePaths) {
    if (-not (Test-Path -LiteralPath $path)) { $violations.Add("缺工具面声明：$path"); continue }
    $text = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    foreach ($needle in @('ps_runtime', 'transfer_key')) {
        if ($text -notmatch [regex]::Escape($needle)) { $violations.Add("tool-face 缺轨 $needle：$path") }
    }
}

# 子智能体档位（仓库必检；Data 仅在存在时做镜像检查）
$subAgentPaths = @(Join-Path $configRoot 'subagents.ushar')
if ($dataAvailable) { $subAgentPaths += Join-Path $dataRoot 'subagents.ushar' }
foreach ($path in $subAgentPaths) {
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
