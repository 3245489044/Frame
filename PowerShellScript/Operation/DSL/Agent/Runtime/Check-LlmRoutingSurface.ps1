#requires -Version 5.1
<#
.SYNOPSIS
    LLM 路由生效面守卫（LlmRoutingSurface）—— 缺省/兜底不得为 local + 禁静默降级 + 自举不得覆写真实 provider + 来源可审计。
.DESCRIPTION
    判据（L1–L10，载体=结构事实/源码文本，只读）：
      L1 禁静默降级：LlmRoutingClient.ResolveBackend 不存在 `catch ... return _local`（解析失败须显式失败可见）。
      L2 缺省声明面：RoutingPolicy 不存在硬编码 `?? LlmRoute.Local`；构造函数对 null 兜底即抛。
      L3 生产兜底非 local：LlmRuntimeFactory 不出现 `new RoutingPolicy(LlmRoute.Local)`；兜底取自配置声明 provider。
      L4 自举不覆写：AgentUiHost.ForceLocalLlm 须带 ExternalConfigApplied 守卫（已声明 provider 不被覆写）。
      L5 来源可审计：LlmRoutingClient 经 ResolveWithOrigin 取来源并落 Record/日志（origin）。
      L6 声明化别名：`RegisterPreset(` 调用点 = 0（命名别名由声明面经 RegisterPresets 批量装载）。
      L7 来源唯一产出：RoutingPolicy 定义 ResolveWithOrigin。
      L8 缺省单源：路由域 `"semantic"` 字面量 = 1（仅 LlmProviderKeys.cs）。
      L9 本地语义单源：全 src `"local"` 字面量 = 1（仅 LlmProviderKeys.cs）。
      L10 单一读面：UI/装配层零 `Routing.Name` / `Routing.Current` 直读（改读 ResolveEffectiveRoute）。
      L11 HTTP 单源：Agent 侧 `new HttpClient(` = 0（连接池统一经 Plate HttpTransportFactory）。
      L12 传输包装已删：Agent 侧 `LlmHttpTransport` 引用 = 0。
      L13 端点单源：Agent 侧 `127.0.0.1:8787` / `api.deepseek.com` 字面量 = 0（单源 = Plate OpenAiEndpointDefaults）。
    模式：默认报告（不设退出码）；-Check 校验（违规 → 退出码 1）。只读仓库，零侧写。
.PARAMETER Check
    校验模式：违规 → 退出码 1。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking


function Find-RepoRoot {
    $d = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
    if ($d) { return $d }
    throw '[LlmRoutingSurface] 无法定位仓库根（上溯未找到含 Projects\Plug\Agent 的目录）'
}

$repoRoot = Find-RepoRoot
$agentRoot = Join-Path $repoRoot 'Projects\Plug\Agent'
$violations = New-Object System.Collections.Generic.List[string]

function Add-Violation([string]$Id, [string]$Msg) { $script:violations.Add("$Id $Msg") }
function Read-Text([string]$Path) { if (Test-Path -LiteralPath $Path) { return (Get-Content -LiteralPath $Path -Raw) } return '' }

$routingClient = Join-Path $agentRoot 'src\Agent.Core\Runtime\State\Infrastructure\LlmRoutingClient.cs'
$routingPolicy = Join-Path $agentRoot 'src\Agent.Contract\Llm\RoutingPolicy.cs'
$runtimeFactory = Join-Path $agentRoot 'src\Agent.Launcher\Composition\Hosting\Llm\LlmRuntimeFactory.cs'
$uiHost = Join-Path $agentRoot 'src\Agent.Ui\Core\Host\AgentUiHost.cs'

$clientText = Read-Text $routingClient
$policyText = Read-Text $routingPolicy
$factoryText = Read-Text $runtimeFactory
$uiHostText = Read-Text $uiHost

# L1 禁静默降级（catch → 本地腿换后端）
if ($clientText -match 'catch\s*\{\s*return\s+_local\s*;') {
    Add-Violation 'L1' 'LlmRoutingClient 存在静默降级 `catch { return _local; }`（解析失败须显式失败可见，禁换后端）'
}

# L2 缺省声明面（禁硬编码 local 兜底）
if ($policyText -match '\?\?\s*LlmRoute\.Local') {
    Add-Violation 'L2' 'RoutingPolicy 存在硬编码兜底 `?? LlmRoute.Local`（缺省须来自配置声明面）'
}
if ($policyText -notmatch 'ArgumentNullException\(nameof\(fallback\)') {
    Add-Violation 'L2' 'RoutingPolicy 构造函数未拒绝 null 兜底（须显式声明兜底，禁隐式默认）'
}

# L3 生产兜底非 local
if ($factoryText -match 'new\s+RoutingPolicy\(\s*LlmRoute\.Local') {
    Add-Violation 'L3' 'LlmRuntimeFactory 以 LlmRoute.Local 作生产兜底（缺省须 = 配置声明的真实 provider）'
}

# L4 自举不覆写真实 provider
$forceBlock = [regex]::Match($uiHostText, 'public\s+static\s+void\s+ForceLocalLlm[\s\S]{0,600}?\n\s*\}')
if (-not $forceBlock.Success) {
    Add-Violation 'L4' 'AgentUiHost.ForceLocalLlm 未找到（守卫载体缺失）'
} elseif ($forceBlock.Value -notmatch 'ExternalConfigApplied') {
    Add-Violation 'L4' 'AgentUiHost.ForceLocalLlm 缺少 ExternalConfigApplied 守卫（自举会覆写配置真实 provider → 静默落 local）'
}

# L5 来源可审计
if ($clientText -notmatch 'ResolveWithOrigin') {
    Add-Violation 'L5' 'LlmRoutingClient 未经 ResolveWithOrigin 取来源（origin 不可审计）'
}
if ($clientText -notmatch 'RouteOrigin\s+origin') {
    Add-Violation 'L5' 'LlmRoutingClient 记录未携带 RouteOrigin 来源维度'
}

# ---- 扩展判据 L6–L10（容量面：声明化 / 单源 / 单一读面）----
$srcRoot = Join-Path $agentRoot 'src'
$srcFiles = @(Get-ChildItem -LiteralPath $srcRoot -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue)

# 判据载体 = 代码（剥离注释行）：注释叙述不计（结构事实载体纪律）。
function Get-CodeText([string]$Path) {
    $lines = Get-Content -LiteralPath $Path -ErrorAction SilentlyContinue
    ($lines | Where-Object { -not $_.TrimStart().StartsWith('//') }) -join "`n"
}

# L6 声明化别名：RegisterPreset( 调用点 = 0（唯一注册入口定义处 BackendRegistry.cs 除外）。
foreach ($f in $srcFiles) {
    if ($f.Name -eq 'BackendRegistry.cs') { continue }
    $code = Get-CodeText $f.FullName
    if ($code -match 'RegisterPreset\(') {
        Add-Violation 'L6' "$($f.Name): 存在 RegisterPreset( 调用（命名别名须由声明面驱动，经 RegisterPresets 批量装载）"
    }
}

# L7 来源唯一产出：RoutingPolicy 定义 ResolveWithOrigin（四维来源唯一产出点）。
if ($policyText -notmatch 'ResolveWithOrigin') {
    Add-Violation 'L7' 'RoutingPolicy 未定义 ResolveWithOrigin（路由来源唯一产出点缺失）'
}

# L8 缺省单源：路由域内 "semantic" 字面量 = 1（仅 LlmProviderKeys.cs）。
$routingScopeFiles = @($srcFiles | Where-Object {
    $_.FullName -match '\\Agent\.Contract\\Llm\\' -or
    $_.FullName -match '\\Agent\.Core\\Runtime\\' -or
    $_.FullName -match '\\Agent\.Launcher\\Composition\\Hosting\\'
})
$semanticCount = 0
foreach ($f in $routingScopeFiles) { $semanticCount += ([regex]::Matches((Get-CodeText $f.FullName), '"semantic"')).Count }
if ($semanticCount -ne 1) {
    Add-Violation 'L8' "路由域 semantic 字面量计数=$semanticCount（须=1，仅 Agent.Contract\Llm\LlmProviderKeys.cs）"
}

# L9 本地语义单源：全 src "local" 字面量 = 1（仅 LlmProviderKeys.cs；结构身份键一并收敛，用户裁定「严格全量」）。
$localCount = 0; $localDefinedInKeys = $false
foreach ($f in $srcFiles) {
    $n = ([regex]::Matches((Get-CodeText $f.FullName), '"local"')).Count
    if ($n -gt 0) {
        $localCount += $n
        if ($f.Name -eq 'LlmProviderKeys.cs') { $localDefinedInKeys = $true }
    }
}
if ($localCount -ne 1 -or -not $localDefinedInKeys) {
    Add-Violation 'L9' "local 字面量计数=$localCount（须=1，且唯一定义点 = LlmProviderKeys.cs）"
}

# L10 单一读面：UI/装配层零 Routing.Name / Routing.Current 直读（改读 ResolveEffectiveRoute）。
foreach ($f in $srcFiles) {
    if ($f.FullName -notmatch '\\(Agent\.Ui|Agent\.Launcher)\\') { continue }
    if ((Get-CodeText $f.FullName) -cmatch 'Routing\??\.(Name|Current)\b') {
        Add-Violation 'L10' "$($f.Name): 存在 Routing.Name/Current 直读（须改读 ResolveEffectiveRoute）"
    }
}

# ---- 扩展判据 L11–L13（HTTP 面单源：Agent 侧不得自建连接池/端点常量；下沉 Plate.HttpMethod）----
$agentHttpCount = 0
$llmTransportCount = 0
$endpointLiteralCount = 0
foreach ($f in $srcFiles) {
    $code = Get-CodeText $f.FullName
    $agentHttpCount += ([regex]::Matches($code, 'new\s+HttpClient\s*\(')).Count
    $llmTransportCount += ([regex]::Matches($code, '\bLlmHttpTransport\b')).Count
    $endpointLiteralCount += ([regex]::Matches($code, '127\.0\.0\.1:8787|api\.deepseek\.com')).Count
}
if ($agentHttpCount -ne 0) {
    Add-Violation 'L11' "Agent 侧 new HttpClient 计数=$agentHttpCount（连接池须统一经 Plate.HttpMethod.Transport.HttpTransportFactory）"
}
if ($llmTransportCount -ne 0) {
    Add-Violation 'L12' "Agent 侧 LlmHttpTransport 引用计数=$llmTransportCount（该包装已删，须直接使用 HttpTransportFactory）"
}
if ($endpointLiteralCount -ne 0) {
    Add-Violation 'L13' "Agent 侧端点字面量计数=$endpointLiteralCount（默认端点须单源 = Plate.HttpMethod.OpenAI.OpenAiEndpointDefaults）"
}

if ($violations.Count -gt 0) {
    Write-Host "[LlmRoutingSurface] 违规 $($violations.Count) 项："
    $violations | ForEach-Object { Write-Host "  - $_" }
    if ($Check) { exit 1 }
} else {
    Write-Host '[LlmRoutingSurface] OK（L1–L13 全绿）'
    if ($Check) { exit 0 }
}
