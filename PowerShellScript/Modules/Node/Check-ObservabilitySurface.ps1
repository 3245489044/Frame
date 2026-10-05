#requires -Version 5.1
<#
.SYNOPSIS
    可见化观测面守卫（F-P0-1c / C-5）—— 「禁静默事件必须在部署态可检索」的机器判据。

.DESCRIPTION
    背景（C-5）：AgentLog 出口的关键可见化分支此前只写 LogRepository（graph-log.db，非 SQLite 形态）⇒ 部署态无读取面。
    治理（F-P0-1a/1b）：关键 msgId 登记为可见化名册（EventKind = msgId 原形）→ 经观测总线写入 events.jsonl；
    AgentLog 出口按名册镜像（未登记即短返）⇒ 「名册 ↔ 发射面」必须成对在位，缺一即判据失效。

    判据：
      C1 名册存在且已登记为 EmbeddedResource
      C2 名册事件数 ≥ 基线（只增不减）
      C3 正对照：判据点名的关键键在册（缺任一 ⇒ 部署态不可检索）
      C4 负对照：乱造键必须在册之外（防「校验恒真」假绿）
      C5 分离性：名册键 ∩ 语义台账键 = ∅（同一 EventKind 第二作者 ⇒ 组合根 RegisterVisibilityContracts 抛）
      C6 发射面在位：AgentTrace.Visibility 定义 + AgentLog 出口镜像 + ModuleLog 出口镜像（防「声明在而没人发」）
      C7 正对照：ModuleLog 面（Agent.Modules 10 个调用点）关键键在册（F-P0-1d：该门面不经 AgentLog）
      C8 部署态读面（F-P0-1i / D13b①）：直读事件区（events.jsonl），断言"存在 ∧ 关键键可检索"。
        默认报告态（未跑产品会话的环境不得恒红）；-VerifyDeploy 转违规（动态面取证）。
        -EventStorePath 可指向临时事件区（正/负对照用，不依赖部署态）。

    模式：默认报告模式（列出实测，不设退出码）；-Check 校验模式（存在违规 → 退出码 1）。

.PARAMETER Root
    仓库根目录（默认由本脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Node → 仓库根）。

.PARAMETER Check
    校验模式：存在违规 → 退出码 1。

.PARAMETER ShowDetail
    输出逐条实测（含达标项）。

.PARAMETER VerifyDeploy
    部署态动态面取证：事件区缺失或关键键不可检索 ⇒ 违规（默认仅报告，不拦）。

.PARAMETER EventStorePath
    事件区路径覆盖（正/负对照用）；缺省 = 部署态 data\observability\events.jsonl。

.EXAMPLE
    .\Check-ObservabilitySurface.ps1 -ShowDetail

.EXAMPLE
    .\Check-ObservabilitySurface.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail,
    [switch]$VerifyDeploy,
    [string]$EventStorePath = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

# 基线单源 = Baselines\ObservabilitySurface.psd1（禁内联数值基线）
Import-Module (Join-Path $PSScriptRoot '..\..\Baselines\GateBaseline.psm1') -Force
$g = Import-GateBaseline -Name ObservabilitySurface
# 基线（只增不减）：名册最小事件数 + 上游 F-6 判据点名的关键键（F-P0-1d 后 12 → 46）
$EventBaseline = [int]$g.EventCount.Value
$KeyVisible = @(
    'NodeDomainPort_ReadTrimmed',
    'ConversationView_TaskNodesPanel_readParity',
    'ProcessNodeSink_EmptyIdentity',
    'ProcessNodeSink_ContainerMissing'
)

# F-P0-1d：Agent.Modules 日志门面（ModuleLog）10 个调用点不经 AgentLog ⇒ 其「禁静默」面须单独在册，
# 否则名册只覆盖 agent: 前缀面，NodeSpec 声明装配族 + Cline 诊断族在部署态仍不可检索。
$KeyModuleLogFace = @(
    'AgentNodeSpec_FrameworkAssembled',
    'AgentNodeSpec_DefinitionDiagnostic',
    'AgentNodeSpec_SpecSourceMissing',
    'AgentInstanceSpec_DeclarationLoaded',
    'ClineDiagnostics_Event',
    'ClinePersist_Frame',
    'ClineExecutorFunction_ExecuteClineAsync_failed',
    'ClineExecutorFunction_BuildStandardContext_failed'
)

$violations = [System.Collections.Generic.List[string]]::new()

$ledgerRel  = 'Projects\Plug\Agent\src\Agent.Contract\Observability\visibility_semantics.json'
$semRel     = 'Projects\Plug\Agent\src\Agent.Contract\Observability\observability_semantics.json'
$csprojRel  = 'Projects\Plug\Agent\src\Agent.Contract\Agent.Contract.csproj'
$traceRel   = 'Projects\Plug\Agent\src\Agent.Operation\Observe\AgentTrace.cs'
$logRel     = 'Projects\Plug\Agent\src\Agent.Axioms\Diagnostics\AgentLog.cs'
$modLogRel  = 'Projects\Plug\Agent\src\Agent.Modules\Diagnostics\ModuleLog.cs'

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  可见化观测面守卫（F-P0-1c：名册 ↔ 发射面成对在位）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

# ---------------- C1 名册存在 + 资源登记 ----------------
$ledgerPath = Join-Path $Root $ledgerRel
if (-not (Test-Path -LiteralPath $ledgerPath))
{
    $violations.Add("[C1] 可见化名册缺失：$ledgerRel（F-P0-1a 未落地 ⇒ 可见化事件不可检索）")
    $keys = @()
}
else
{
    $csprojPath = Join-Path $Root $csprojRel
    $registered = (Test-Path -LiteralPath $csprojPath) -and
        @(Select-String -Path $csprojPath -Pattern 'visibility_semantics\.json' -ErrorAction SilentlyContinue).Count -gt 0
    if (-not $registered)
    {
        $violations.Add("[C1] 名册未登记为 EmbeddedResource：$csprojRel 缺 visibility_semantics.json（装载链断裂）")
    }

    $ledgerJson = Get-Content -LiteralPath $ledgerPath -Raw
    $parsed = $null
    try { $parsed = $ledgerJson | ConvertFrom-Json } catch { $violations.Add("[C1] 名册 JSON 非法：$($_.Exception.Message)") }

    $keys = @()
    if ($null -ne $parsed -and $null -ne $parsed.events)
    {
        $keys = @($parsed.events.PSObject.Properties | ForEach-Object { $_.Name })
    }
}

# ---------------- C2 事件数 ≥ 基线 ----------------
if ($keys.Count -lt $EventBaseline)
{
    $violations.Add("[C2] 名册事件数 $($keys.Count) < 基线 $EventBaseline（名册被削 ⇒ 覆盖不足）")
}

# ---------------- C3 正对照：关键键在册 ----------------
foreach ($key in $KeyVisible)
{
    if ($keys -notcontains $key)
    {
        $violations.Add("[C3] 关键键不在册：$key（F-6 判据失效：部署态不可检索）")
    }
}

# ---------------- C4 负对照：乱造键不在册（防假绿） ----------------
$bogus = 'NodeDomainPort_ReadTrimmed__negative_control__'
if ($keys -contains $bogus)
{
    $violations.Add("[C4] 负对照失败：乱造键竟在册（判据恒真，C3 无判别力）")
}

# ---------------- C5 分离性：名册键 ∩ 语义台账键 = ∅ ----------------
$semPath = Join-Path $Root $semRel
if (Test-Path -LiteralPath $semPath)
{
    $semKeys = @()
    try
    {
        $semParsed = (Get-Content -LiteralPath $semPath -Raw) | ConvertFrom-Json
        if ($null -ne $semParsed.events)
        {
            $semKeys = @($semParsed.events.PSObject.Properties | ForEach-Object { $_.Name })
        }
    }
    catch { $violations.Add("[C5] 语义台账 JSON 非法：$($_.Exception.Message)") }

    $overlap = @($keys | Where-Object { $semKeys -contains $_ })
    if ($overlap.Count -gt 0)
    {
        $violations.Add("[C5] 名册与语义台账键相交（第二作者）：$($overlap -join ', ')")
    }
}
else
{
    $violations.Add("[C5] 语义台账缺失：$semRel（无法判定分离性）")
}

# ---------------- C6 发射面在位 ----------------
$tracePath = Join-Path $Root $traceRel
$traceOk = (Test-Path -LiteralPath $tracePath) -and
    @(Select-String -Path $tracePath -Pattern 'public static bool Visibility\(' -ErrorAction SilentlyContinue).Count -gt 0
if (-not $traceOk)
{
    $violations.Add("[C6] 发射面缺失：$traceRel 无 AgentTrace.Visibility（声明在而没人发）")
}

$logPath = Join-Path $Root $logRel
$logOk = (Test-Path -LiteralPath $logPath) -and
    @(Select-String -Path $logPath -Pattern 'AgentTrace\.Visibility\(' -ErrorAction SilentlyContinue).Count -gt 0
if (-not $logOk)
{
    $violations.Add("[C6] 镜像未接线：$logRel 出口未调用 AgentTrace.Visibility（可见化键永不发射）")
}

# C6 续：第二门面（F-P0-1d）——Agent.Modules 的 ModuleLog 不经 AgentLog，须经同一镜像入口接线，
# 否则其 10 个调用点的「禁静默」面永不发射（名册在册而无人发的第二种形态）。
$modLogPath = Join-Path $Root $modLogRel
$modLogOk = (Test-Path -LiteralPath $modLogPath) -and
    @(Select-String -Path $modLogPath -Pattern 'MirrorVisibility\(' -ErrorAction SilentlyContinue).Count -gt 0
if (-not $modLogOk)
{
    $violations.Add("[C6] 第二门面镜像未接线：$modLogRel 出口未调用 AgentLog.MirrorVisibility（ModuleLog 面 10 站点永不发射）")
}

# ---------------- C7 正对照：ModuleLog 面关键键在册（F-P0-1d） ----------------
foreach ($key in $KeyModuleLogFace)
{
    if ($keys -notcontains $key)
    {
        $violations.Add("[C7] ModuleLog 面关键键不在册：$key（Agent.Modules 该站点部署态不可检索）")
    }
}

# ---------------- C8 部署态读面：事件区可检索（F-P0-1i / D13b①） ----------------
# 「可读观测面 ≥ 1」的判据载体：门禁**直读**事件区（PowerShell，零 C# 读者）——断言"存在 ∧ 关键键可检索"。
# 默认报告态（未跑产品会话的环境不得恒红）；-VerifyDeploy 转违规（部署态动态面收尾取证）。
if ([string]::IsNullOrWhiteSpace($EventStorePath))
{
    $EventStorePath = Join-Path $Root 'Projects\Information\State\data\observability\events.jsonl'
}
$storeExists = Test-Path -LiteralPath $EventStorePath
$deployHits = @()
if ($storeExists)
{
    $deployHits = @($KeyVisible | Where-Object {
        @(Select-String -LiteralPath $EventStorePath -Pattern $_ -SimpleMatch -ErrorAction SilentlyContinue).Count -gt 0
    })
}
if ($VerifyDeploy)
{
    if (-not $storeExists)
    {
        $violations.Add("[C8] 部署态事件区缺失：$EventStorePath（无 flush 驱动 ∨ 未跑产品会话 ⇒ 可读观测面 = 0）")
    }
    else
    {
        $deployMissing = @($KeyVisible | Where-Object { $deployHits -notcontains $_ })
        if ($deployMissing.Count -gt 0)
        {
            $violations.Add("[C8] 部署态事件区不可检索关键键：$($deployMissing -join ', ')（事件区在而键缺 ⇒ 读面不成立）")
        }
    }
}

# ---------------- 汇总 ----------------
Write-Host ("  实测：名册事件 {0}（基线 {1}）· AgentLog 面关键键 {2}/{3} · ModuleLog 面关键键 {4}/{5} · 语义台账分离 {6} · 部署态事件区 {7}（关键键命中 {8}/{9}）" -f `
    $keys.Count, $EventBaseline, (@($KeyVisible | Where-Object { $keys -contains $_ }).Count), $KeyVisible.Count, `
    (@($KeyModuleLogFace | Where-Object { $keys -contains $_ }).Count), $KeyModuleLogFace.Count, `
    $(if ($violations -match 'C5') { '违例' } else { '是' }), `
    $(if ($storeExists) { '有' } else { '无' }), $deployHits.Count, $KeyVisible.Count)

if ($ShowDetail)
{
    Write-Host '  --- 名册键 ---' -ForegroundColor DarkGray
    foreach ($k in $keys) { Write-Host "    - $k" -ForegroundColor DarkGray }
}

if ($violations.Count -gt 0)
{
    Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
}
else
{
    Write-Host '  [成功] C1–C8 全部通过（名册在册 ∧ 正负对照在位 ∧ 双门面发射面成对 ∧ ModuleLog 面在册 ∧ 部署态读面在位）' -ForegroundColor Green
}

if ($Check -and $violations.Count -gt 0) { exit 1 }
exit 0
