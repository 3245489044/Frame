<#
.SYNOPSIS
    工具调用聚拢门禁（A1–A6）：工具装配/注册/连接/观测单点 + 内核纯净 + 无回退支。

.DESCRIPTION
    判据载体 = 源码/工程文件结构事实（非执行者叙述）：
      A1 Launcher 不再 new ToolFaceHost / RegisterSource / ToolFaceAssembler.Build（组合根仅接线）
      A2 工具函数注册单点：RegisterMany( 只允许 ToolFunctionRegistration + 各工具类内部 Register
      A3 外部 MCP 客户端单实例：new McpBridgeClient( 仅 Clients/McpClientPool.cs
      A4 声明面单一：Config/tool-face.json 不存在
      A5 回环回退支已删：AgentMcpLoopback 内无 router.AddTrack
      A6 内核纯净：Agent.Core.Tool.csproj 不含 ModelContextProtocol / CallCenter.Mcp
      A7 依赖方向：Agent.McpRegistryHost.csproj 不含 Agent.Launcher

    返回码：0=通过；1=违例。
#>
[CmdletBinding()]
param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking


# Frame\PowerShellScript\Operation\DSL\Agent -> 上溯 5 级到仓库根
$mainRoot = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
$agentRoot = Join-Path $mainRoot 'Projects\Plug\Agent'
$srcRoot = Join-Path $agentRoot 'src'

if (-not (Test-Path -LiteralPath $srcRoot)) {
    Write-Host "FAIL 工具调用聚拢：Agent src 不存在：$srcRoot" -ForegroundColor Red
    exit 1
}

$violations = New-Object System.Collections.Generic.List[string]

function Get-CsHits([string]$root, [string]$pattern) {
    Get-ChildItem -Path $root -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
        Select-String -Pattern $pattern |
        Where-Object { $_.Path -notmatch '\\(obj|bin|BuildOutput)\\' }
}

# A1 Launcher 不再持有工具装配（ToolFaceFlowCatalog 为纯读投影 CodeRef，排除）
$launcher = Join-Path $srcRoot 'Agent.Launcher'
$hits = Get-CsHits $launcher 'new ToolFaceHost|RegisterSource\(|ToolFaceAssembler\.Build'
$hits = $hits | Where-Object { $_.Path -notmatch 'ToolFaceFlowCatalog\.cs$' }
if (@($hits).Count -gt 0) {
    $violations.Add("A1 Launcher 仍含工具装配：$((@($hits) | ForEach-Object { "$($_.Path):$($_.LineNumber)" }) -join '; ')")
}

# A2 RegisterMany 单点（允许：ToolFunctionRegistration + 各工具类内部）
$allowedRegister = @('ToolFunctionRegistration.cs', 'LocalDbMcpTools.cs', 'DeclarationEditMcpTools.cs', 'GraphTaskTools.cs', 'McpToolFunction.cs', 'WorkflowFunctionRegistrar.cs')
$hits = Get-CsHits $srcRoot 'RegisterMany\(' | Where-Object { $allowedRegister -notcontains (Split-Path -Leaf $_.Path) }
if (@($hits).Count -gt 0) {
    $violations.Add("A2 RegisterMany 越界：$((@($hits) | ForEach-Object { "$($_.Path):$($_.LineNumber)" }) -join '; ')")
}

# A3 客户端单实例
$hits = Get-CsHits $srcRoot 'new McpBridgeClient\(' | Where-Object { (Split-Path -Leaf $_.Path) -ne 'McpClientPool.cs' }
if (@($hits).Count -gt 0) {
    $violations.Add("A3 new McpBridgeClient 越界：$((@($hits) | ForEach-Object { "$($_.Path):$($_.LineNumber)" }) -join '; ')")
}

# A4 声明面单一
if (Test-Path -LiteralPath (Join-Path $agentRoot 'Config\tool-face.json')) {
    $violations.Add('A4 Config/tool-face.json 仍存在（声明面应唯一 = tool-face.ushar）')
}

# A5 回环回退支已删
$loopback = Join-Path $srcRoot 'Agent.McpRegistryHost\AgentMcpLoopback.cs'
if (Test-Path -LiteralPath $loopback) {
    $hits = Select-String -Path $loopback -Pattern 'router\.AddTrack'
    if (@($hits).Count -gt 0) {
        $violations.Add('A5 AgentMcpLoopback 仍含自建 router 回退支（router.AddTrack）')
    }
}

# A6 内核纯净
$coreTool = Join-Path $srcRoot 'Agent.Core\Operation\Tool\Agent.Core.Tool.csproj'
if (Test-Path -LiteralPath $coreTool) {
    $hits = Select-String -Path $coreTool -Pattern 'ModelContextProtocol|CallCenter\.Mcp'
    if (@($hits).Count -gt 0) {
        $violations.Add('A6 Agent.Core.Tool 引用了 MCP 宿主面（ModelContextProtocol / CallCenter.Mcp）')
    }
}

# A7 依赖方向
$hostProj = Join-Path $srcRoot 'Agent.McpRegistryHost\Agent.McpRegistryHost.csproj'
if (Test-Path -LiteralPath $hostProj) {
    $hits = Select-String -Path $hostProj -Pattern 'ProjectReference[^>]*Agent\.Launcher'
    if (@($hits).Count -gt 0) {
        $violations.Add('A7 Agent.McpRegistryHost 引用了 Agent.Launcher（依赖倒置）')
    }
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host "FAIL $v" -ForegroundColor Red }
    exit 1
}

Write-Host 'PASS 工具调用聚拢（A1–A7）' -ForegroundColor Green
exit 0
