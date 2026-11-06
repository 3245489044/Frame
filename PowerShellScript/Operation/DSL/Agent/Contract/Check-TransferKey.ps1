# 键址传递框架门禁（KTF）：键文法/词表/存储/轨/双写声明齐备。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateFacts.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) {
    $d = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
    if (-not $d) { throw "TransferKey gate：未找到仓库根（Projects\Plug\Agent）" }
    $Root = $d
}

$violations = New-Object System.Collections.Generic.List[string]
$ledgerForge = Get-GateLedgerForge -RepoRoot $Root
if (-not $ledgerForge) {
    Write-Host '[FAIL] 无法执行：LedgerForge 未构建（键集比较不可用）' -ForegroundColor Red
    exit 2
}
$agentRoot = Join-Path $Root 'Projects\Plug\Agent'
$configRoot = Join-Path $agentRoot 'Config'
$dataRoot = Join-Path $Root 'Data\Agent\Config'

function Require-File([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path)) { $script:violations.Add("缺文件：$Label ($Path)") }
}

Require-File (Join-Path $agentRoot 'src\Agent.Session\Keys\TransferKeyCodec.cs') 'TransferKeyCodec'
Require-File (Join-Path $agentRoot 'src\Agent.Contract\Runtime\Transfer\ITransferKeyStore.cs') 'TransferKey DTO/端口'
# 词表常量事实源：生成契约常量（partial class 的实现体；接口文件里的同名类只是空壳）。
Require-File (Join-Path $agentRoot 'src\Agent.Contract\Generated\GeneratedContractConstants.g.cs') '生成契约常量'
Require-File (Join-Path $agentRoot 'src\Agent.Modules\Framework\Transfer\TransferKeyStore.cs') 'TransferKeyStore'
Require-File (Join-Path $agentRoot 'src\Agent.Modules\Framework\Transfer\TransferKeyOpsFunctions.cs') 'TransferKeyOpsFunctions'
Require-File (Join-Path $agentRoot 'src\Agent.McpRegistryHost\Tracks\TransferKeyCallableTrack.cs') 'TransferKeyCallableTrack'
Require-File (Join-Path $configRoot 'workflows\transfer-key.ushar') '转移键声明面'
# Data 根缺失不判红（E17）：干净 checkout 无 Data 时只跳过镜像检查并显式提示。
$dataAvailable = Test-Path -LiteralPath $dataRoot
if ($dataAvailable) {
    Require-File (Join-Path $dataRoot 'workflows\transfer-key.ushar') '数据根转移键声明面'
} else {
    Write-GateSkip -Key 'TransferKey' -Reason 'Data\Agent\Config 不存在（无对照对象，声明镜像未验证）'
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

# 键文法 + 词表事实（A3/D6/D11）：唯一抽取值集/边界实现 = C# transfer-facts。
# PS 侧不再解析 [Space]、不再正则读生成常量、不再复制 xfer:/64/层词表/kind 词表。
$codecPath = Join-Path $agentRoot 'src\Agent.Session\Keys\TransferKeyCodec.cs'
$constsPath = Join-Path $agentRoot 'src\Agent.Contract\Generated\GeneratedContractConstants.g.cs'
$transferPath = Join-Path $configRoot 'nodes\transfer.ushar'
if (-not ((Test-Path -LiteralPath $codecPath) -and (Test-Path -LiteralPath $constsPath) -and (Test-Path -LiteralPath $transferPath))) {
    Write-GateSkip -Key 'TransferKey' -Reason '键文法/词表事实源缺失（值集等价与 slot 边界未验证）'
}
else {
    $factsProbe = Get-GateTransferFacts -LedgerForge $ledgerForge -UsharRoot $transferPath -ConstantsPath $constsPath -CodecPath $codecPath
    if (-not $factsProbe.Ok) {
        $violations.Add('无法执行：transfer-facts 输入不可读（值集等价与 slot 边界未验证）')
    }
    else {
        $facts = $factsProbe.Payload
        foreach ($mismatch in @($facts.mismatches)) { $violations.Add("转移键事实不一致：$mismatch") }
        if ([string]::IsNullOrEmpty([string]$facts.codec.prefix)) { $violations.Add('TransferKeyCodec 缺 Prefix 事实') }
        if ([int]$facts.codec.maxSlotLength -le 0) { $violations.Add('TransferKeyCodec 缺 MaxSlotLength 事实') }
        if (@($facts.codec.layers).Count -eq 0) { $violations.Add('TransferKeyCodec 缺 Layers 词表') }
        if (@($facts.codec.kinds).Count -eq 0) { $violations.Add('TransferKeyCodec 缺 Kinds 词表') }
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
# 权威事实源 = 生成的键空间产物（agent.keyspace.generated.json）：
#   产出方 Generate-DeclaredBundle.ps1 -Kind keyspace（消费 [KeySpace] 声明），构建期生成并入库。
#   手写 agent.keyspace.json 已退役（仅存 Config\_deprecated），不得再作判据载体（D1）。
$repoKeyspace = Join-Path $configRoot 'keyspaces\agent.keyspace.generated.json'
$keyspacePaths = @($repoKeyspace)
if ($dataAvailable) { $keyspacePaths += Join-Path $dataRoot 'keyspaces\agent.keyspace.json' }
foreach ($path in $keyspacePaths) {
    if (-not (Test-Path -LiteralPath $path)) { $violations.Add("缺键空间声明：$path"); continue }
    $text = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    if ($text -notmatch '"agent\.transfer"') { $violations.Add("键空间缺 agent.transfer：$path") }
    if ($text -notmatch 'xfer:\{layer\}:\{scope\}:\{kind\}:\{slot\}') { $violations.Add("键空间模板不匹配：$path") }
}

# 工具轨（仓库必检；Data 仅在存在时做镜像检查）
$toolFacePaths = @(Join-Path $configRoot 'toolface\tool-face.ushar')
if ($dataAvailable) { $toolFacePaths += Join-Path $dataRoot 'tool-face.ushar' }
foreach ($path in $toolFacePaths) {
    if (-not (Test-Path -LiteralPath $path)) { $violations.Add("缺工具面声明：$path"); continue }
    $text = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    foreach ($needle in @('ps_runtime', 'transfer_key')) {
        if ($text -notmatch [regex]::Escape($needle)) { $violations.Add("tool-face 缺轨 $needle：$path") }
    }
}

# 子智能体档位（仓库必检；Data 仅在存在时做镜像检查）
$subAgentPaths = @(Join-Path $configRoot 'agents\subagents.ushar')
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
if (-not $dataAvailable) {
    Write-Host '[PASS] TransferKey 仓库面通过（Data 镜像见上方 SKIPPED_NO_TARGET，不计入通过）' -ForegroundColor Green
} else {
    Write-Host '[PASS] TransferKey gate 通过（仓库面 + Data 镜像均已比对）' -ForegroundColor Green
}
exit 0
