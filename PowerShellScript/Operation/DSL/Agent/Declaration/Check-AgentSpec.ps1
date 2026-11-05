#requires -Version 5.1
<#
.SYNOPSIS
    agent 识别声明守卫（AgentSpec）—— 「每个 agent 只写声明、零代码」的机器判据（P1-4 / A-1 / W-1…W-4）。

.DESCRIPTION
    声明面（单源）：
      Projects\Plug\Agent\Config\agents\<agent>\recognizers.json   （词表 + 规则 + default）
      Projects\Plug\Agent\Config\agents\<agent>\round-shape.ushar  （轮次形状契约）
      Projects\Plug\Agent\Config\conversation-canon.ushar          （kind → CSG 映射，唯一映射点）

    判据：
      A1 目录齐备：每个 agent 目录同时有 recognizers.json 与 round-shape.ushar（缺一即违规）。
      A2 声明合法：JSON 合法 / kinds 非空且含兜底类 / agent 字段 = 目录名。
      A3 规则归属：∀rule: rule.kind ∈ kinds。
      A4 末路完备：default 非空 ∧ default.kind ∈ kinds（P5；禁隐式丢弃）。
      A5 谓词合法：match 仅结构形态（标量 / 数组∈集合 / {exists:bool}），禁嵌套对象谓词。
      A6 无过程语义：spec 内禁 order/step/sequence/then/next 键与「先…再…」动词序。
      A7 映射闭合：∀kind ∈ kinds: kind 出现于 conversation-canon.ushar。
      A8 负对照（防假绿）：empty-default / step-key 两个合成样本必须被同一判据判红。

    模式：默认报告模式；-Check 校验模式（违规 > 0 → 退出码 1）。

.EXAMPLE
    .\Check-AgentSpec.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

$agentsRoot = Join-Path $Root 'Projects\Plug\Agent\Config\agents'
$canonPath  = Join-Path $Root 'Projects\Plug\Agent\Config\conversation-canon.ushar'
$fallbackKind = Get-GateUnrecognizedKind   # A7 单源（同 C# RecognizedKinds.Unrecognized）
$processKeys = @('order', 'step', 'sequence', 'then', 'next')

$violations = [System.Collections.Generic.List[string]]::new()
$pending    = [System.Collections.Generic.List[string]]::new()

Import-Module (Join-Path (Get-FrameRoot) 'PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name AgentSpec
$minAgents = [int]$baseline.MinAgents.Value

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  agent 识别声明守卫（目录齐备 / 末路完备 / 结构谓词 / 映射闭合）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

<#
    判据核心（纯函数；负对照直接复用 ⇒ 判据不可被"恒真"绕过）。
    返回违规清单；空 = 通过。
#>
function Get-AgentSpecIssues
{
    param(
        [string]$AgentName,
        [object]$Spec,
        [bool]$HasRoundShape,
        [string]$CanonText
    )

    $issues = [System.Collections.Generic.List[string]]::new()

    if (-not $HasRoundShape) { $issues.Add("[A1] agent '$AgentName' 缺 round-shape.ushar（轮次形状契约是必需声明）") }
    if ($null -eq $Spec) { $issues.Add("[A2] agent '$AgentName' 的 recognizers.json 不可解析"); return $issues }

    $kinds = @($Spec.kinds)
    if ($kinds.Count -eq 0) { $issues.Add("[A2] agent '$AgentName' 的 kinds 为空") }
    if ($kinds -notcontains $fallbackKind) { $issues.Add("[A2] agent '$AgentName' 词表缺兜底类 '$fallbackKind'") }
    if ($Spec.agent -ne $AgentName) { $issues.Add("[A2] agent 字段 '$($Spec.agent)' ≠ 目录名 '$AgentName'（目录名即身份）") }

    foreach ($rule in @($Spec.recognizers))
    {
        if ($kinds -notcontains [string]$rule.kind)
        {
            $issues.Add("[A3] agent '$AgentName' 规则 kind '$($rule.kind)' ∉ kinds")
        }

        foreach ($key in $rule.match.PSObject.Properties.Name)
        {
            if ($processKeys -contains $key)
            {
                $issues.Add("[A6] agent '$AgentName' 规则 match 含过程语义键 '$key'（SEP.DeclarativeProcessDisjoint）")
            }
            $expected = $rule.match.$key
            if ($null -ne $expected -and $expected -isnot [array] -and $expected -is [System.Management.Automation.PSCustomObject])
            {
                if ($expected.PSObject.Properties.Name -notcontains 'exists')
                {
                    $issues.Add("[A5] agent '$AgentName' 规则 match.$key 非结构谓词（仅允许 标量 / 数组∈集合 / {exists:bool}）")
                }
            }
        }

        foreach ($k in @($rule.PSObject.Properties.Name))
        {
            if ($processKeys -contains $k)
            {
                $issues.Add("[A6] agent '$AgentName' 规则含过程语义键 '$k'（声明面禁步骤序）")
            }
        }
    }

    if ($null -eq $Spec.default -or [string]::IsNullOrWhiteSpace([string]$Spec.default.kind))
    {
        $issues.Add("[A4] agent '$AgentName' 缺 default（末路不完备；P5 禁隐式丢弃）")
    }
    elseif ($kinds -notcontains [string]$Spec.default.kind)
    {
        $issues.Add("[A4] agent '$AgentName' default.kind '$($Spec.default.kind)' ∉ kinds")
    }

    # A7 映射闭合：kind 显式登记，或 canon 已声明 unmapped 末路（未登记 ⇒ 归 unrecognized，禁 Default）。
    $hasUnmapped = $CanonText -match '(?m)^\s*unmapped\s*='
    foreach ($kind in $kinds)
    {
        if ($CanonText -notmatch ("(?m)^\s*" + [regex]::Escape($kind) + "\s+→"))
        {
            if (-not $hasUnmapped)
            {
                $issues.Add("[A7] agent '$AgentName' kind '$kind' 未在 conversation-canon.ushar 登记映射（映射闭合）")
            }
            elseif ($kind -eq $fallbackKind)
            {
                $issues.Add("[A7] agent '$AgentName' 兜底类 '$kind' 必须在 conversation-canon.ushar 显式登记（不得仅靠 unmapped）")
            }
        }
    }

    return $issues
}


# ---------------- A1/A2-A7：逐 agent 校验 ----------------
$canonText = if (Test-Path -LiteralPath $canonPath) { Get-Content -LiteralPath $canonPath -Raw } else { '' }
if ($canonText -eq '') { $violations.Add("[A7] 缺 conversation-canon.ushar（kind → CSG 唯一映射点）") }
elseif ($canonText -match '(?i)^\s*(step|order|sequence|then|next)\s*=')
{
    $violations.Add("[A6] conversation-canon.ushar 含过程语义键（声明面禁步骤序）")
}

$agentDirs = @()
if (Test-Path -LiteralPath $agentsRoot)
{
    $agentDirs = @(Get-ChildItem -LiteralPath $agentsRoot -Directory)
}

if ($agentDirs.Count -lt $minAgents)
{
    $violations.Add("[A1] agent 目录数 $($agentDirs.Count) < 基线 $minAgents（声明面被删 ⇒ 识别层静默停用）")
}

foreach ($dir in $agentDirs)
{
    $specFile = Join-Path $dir.FullName 'recognizers.json'
    $shapeFile = Join-Path $dir.FullName 'round-shape.ushar'
    if (-not (Test-Path -LiteralPath $specFile))
    {
        $violations.Add("[A1] agent '$($dir.Name)' 缺 recognizers.json")
        continue
    }

    $spec = $null
    try { $spec = Get-Content -LiteralPath $specFile -Raw | ConvertFrom-Json }
    catch { $violations.Add("[A2] agent '$($dir.Name)' recognizers.json 非法 JSON：$($_.Exception.Message)") }

    $hasShape = Test-Path -LiteralPath $shapeFile
    if ($hasShape)
    {
        $shapeText = Get-Content -LiteralPath $shapeFile -Raw
        foreach ($need in @('required', 'terminal', 'pairings', 'default'))
        {
            if ($shapeText -notmatch ("(?m)^\s*" + $need + "\s*="))
            {
                $violations.Add("[A1] agent '$($dir.Name)' round-shape.ushar 缺 '$need' 判据")
            }
        }
    }

    foreach ($issue in (Get-AgentSpecIssues -AgentName $dir.Name -Spec $spec -HasRoundShape $hasShape -CanonText $canonText))
    {
        $violations.Add($issue)
    }
}

# ---------------- A8 负对照（防假绿） ----------------
$probeEmptyDefault = [pscustomobject]@{ agent = 'probe'; kinds = @('unrecognized'); recognizers = @(); default = $null }
if (@(Get-AgentSpecIssues -AgentName 'probe' -Spec $probeEmptyDefault -HasRoundShape $true -CanonText 'unrecognized → X').Count -eq 0)
{
    $violations.Add("[A8] 负对照失效：缺 default 的样本未被判据捕获（判据恒真）")
}

$probeStepKey = [pscustomobject]@{
    agent = 'probe'
    kinds = @('unrecognized', 'tool-call')
    recognizers = @([pscustomobject]@{ match = [pscustomobject]@{ step = 1 }; kind = 'tool-call' })
    default = [pscustomobject]@{ kind = 'unrecognized' }
}
$probeStepIssues = @(Get-AgentSpecIssues -AgentName 'probe' -Spec $probeStepKey -HasRoundShape $true -CanonText "unrecognized → X`ntool-call → Y")
if (-not ($probeStepIssues -match '过程语义键'))
{
    $violations.Add("[A8] 负对照失效：含 step 键的样本未被判据捕获（判据恒真）")
}

if ($ShowDetail)
{
    Write-Host "  agent 目录：$(@($agentDirs).Count)（基线 ≥ $minAgents）" -ForegroundColor Gray
    Write-Host "  映射单源：$canonPath" -ForegroundColor Gray
    foreach ($d in $agentDirs) { Write-Host "    - $($d.Name)" -ForegroundColor Gray }
}

# ---------------- 汇总 ----------------
Write-Host ""
if ($ShowDetail -and $pending.Count -gt 0)
{
    Write-Host "[AS] --- 迁移期可见化（不算违规） ---" -ForegroundColor DarkGray
    foreach ($item in $pending) { Write-Host "  - $item" -ForegroundColor DarkGray }
}

if ($violations.Count -eq 0)
{
    Write-Host "[OK] agent 识别声明守卫：0 违规（目录齐备 / 末路完备 / 结构谓词 / 映射闭合 / 负对照在位）" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] agent 识别声明守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }
Write-Host "  修复口径：新增 agent ⇒ 只加 Config\agents\<agent>\{recognizers.json, round-shape.ushar}；" -ForegroundColor Gray
Write-Host "            新增 kind ⇒ 同步 kinds + conversation-canon 映射（TS/C# 零代码）。" -ForegroundColor Gray

if ($Check) { exit 1 }
exit 0
