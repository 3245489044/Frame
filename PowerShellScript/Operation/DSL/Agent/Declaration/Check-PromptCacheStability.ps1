#requires -Version 5.1
<#
.SYNOPSIS
    提示词缓存稳定性与单源门禁（PromptCacheStability）。
.DESCRIPTION
    C1 单源：段声明面在册（[Segment] 含 l1/l2 与别名），且代码侧不再自持别名表；
    C2 必填派生闭包：required_prompt_ports ⊆ 执行体步 inports（端口即结构事实，不靠语义自证）；
    C3 快照单源：UI 不再从节点端口反读提示词层（禁第二构建点）；
    C4 基线：Data/Tests 下存在提示词指纹基线（report 级，不与行为改造同批判红）；
    C5 负对照：合成违规样本必须被判出。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..\..\Meta\TextOps.psm1') -Force -DisableNameChecking
# 根锚单源（A4/D-根锚）：并入 GateRuntime，禁自算四级上溯。
Import-Module (Join-Path $PSScriptRoot '..\..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

function Get-Violations {
    param(
        [string]$SegmentDecl,
        [string]$SeedContract,
        [string]$Preflight,
        [string]$SurfaceResolver,
        [string]$Workflow,
        [bool]$BaselinePresent
    )

    $violations = [System.Collections.Generic.List[string]]::new()

    # C1 声明面
    if ($SegmentDecl -notmatch '(?m)^\s*\[Segment\]\s+prompt\.l1\s*:.*aliases=') { $violations.Add('C1 缺 [Segment] prompt.l1 的 aliases 声明') }
    if ($SegmentDecl -notmatch '(?m)^\s*\[Segment\]\s+prompt\.l2\s*:.*aliases=') { $violations.Add('C1 缺 [Segment] prompt.l2 的 aliases 声明') }
    if ($SeedContract -match 'IReadOnlyDictionary<string,\s*string>\s+Aliases') { $violations.Add('C1 SessionSeedContract 仍自持别名表（应读声明面）') }
    if ($Preflight -match 'IReadOnlyDictionary<string,\s*string\[\]>\s+Aliases') { $violations.Add('C1 PromptPreflight 仍自持别名表（应读声明面）') }

    # C1 字段面：每条 [Segment] 须声明 field=（读模型字段表的上游）；字段名唯一。
    $seenFields = @{}
    foreach ($m in [regex]::Matches($SegmentDecl, '(?m)^\s*\[Segment\]\s+(\S+?)\s*:(.*)$')) {
        $key = $m.Groups[1].Value
        $body = $m.Groups[2].Value
        $fieldMatch = [regex]::Match($body, '\bfield=([^|\s]+)')
        if (-not $fieldMatch.Success) {
            $violations.Add("C1 [Segment] $key 缺 field=（该段不进读模型字段面）")
            continue
        }
        $name = $fieldMatch.Groups[1].Value
        if ($seenFields.ContainsKey($name)) {
            $violations.Add("C1 [Segment] field='$name' 重复（$key 与 $($seenFields[$name])）")
        }
        else {
            $seenFields[$name] = $key
        }
        if ([regex]::IsMatch($body, '\bfield=') -and -not [regex]::IsMatch($body, '\btype=')) {
            $violations.Add("C1 [Segment] $key 有 field= 但缺 type=")
        }
    }

    # C2 必填闭包：required_prompt_ports ⊆ cline_execute.inports
    $required = [regex]::Match($Workflow, '(?m)^\s*\[Flow\][^\r\n]*required_prompt_ports=([^\r\n|]+)')
    if ($required.Success) {
        $exec = [regex]::Match($Workflow, '(?m)^\s*\[Node\]\s+cline_execute\s*:[^\r\n]*inports=([^\r\n|]+)')
        if (-not $exec.Success) {
            $violations.Add('C2 缺 [Node] cline_execute 的 inports 声明（无法复算必填闭包）')
        }
        else {
            $inports = @(ConvertFrom-CommaList -Text $exec.Groups[1].Value)
            foreach ($port in @(ConvertFrom-CommaList -Text $required.Groups[1].Value)) {
                if ($port -notin $inports) { $violations.Add("C2 required_prompt_ports 含未声明入参：$port") }
            }
        }
    }

    # C3 快照单源
    if ($SurfaceResolver -match 'TryReadPromptLayersFromNodesAsync') { $violations.Add('C3 UI 仍从节点端口反读提示词层（第二构建点）') }

    # C4 基线（report 级）
    if (-not $BaselinePresent) { Write-Host 'REPORT PromptCacheStability: 指纹基线未建立（Data\Tests 下无 prompt-golden 基线）' }

    return $violations
}

$segmentFile = Join-Path $Root 'Projects\Plug\Agent\Config\prompt\prompt-segments.ushar'
$seedFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Contract\Prompts\SessionSeedContract.cs'
$preflightFile = Join-Path $Root 'Frame\CsharpPrj\Operation\HierarchyMethod\Nodes\Execution\PromptPreflight.cs'
$surfaceFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Projection\PromptSurfaceResolver.cs'
$workflowFile = Join-Path $Root 'Projects\Plug\Agent\Config\workflows\session\agent-loop.ushar'
$baselineDir = Join-Path $Root 'Data\Tests\prompt-golden'

function Read-Text([string]$path) { if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path -Raw } else { '' } }

$segment = Read-Text $segmentFile
$seed = Read-Text $seedFile
$preflight = Read-Text $preflightFile
$surface = Read-Text $surfaceFile
$workflow = Read-Text $workflowFile
$baseline = Test-Path -LiteralPath $baselineDir

if ($NegativeControl) {
    $probe = Get-Violations -SegmentDecl '' -SeedContract 'static readonly IReadOnlyDictionary<string, string> Aliases =' `
        -Preflight 'static readonly IReadOnlyDictionary<string, string[]> Aliases =' `
        -SurfaceResolver 'TryReadPromptLayersFromNodesAsync' -Workflow '' -BaselinePresent $true
    if ($probe.Count -lt 3) {
        Write-Output 'FAIL PromptCacheStability: 负对照未命中（检测器恒真）'
        exit 1
    }
    Write-Output "PASS PromptCacheStability (negative control: $($probe.Count) violations)"
    exit 0
}

if ($ShowDetail) {
    Write-Output "SegmentDecl=$segmentFile"
    Write-Output "Workflow=$workflowFile"
    Write-Output "Baseline=$baselineDir (present=$baseline)"
}

$violations = Get-Violations -SegmentDecl $segment -SeedContract $seed -Preflight $preflight `
    -SurfaceResolver $surface -Workflow $workflow -BaselinePresent $baseline

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Output "  - $_" }
    Write-Output "FAIL PromptCacheStability: $($violations.Count) violation(s)"
    if ($Check) { exit 1 }
    exit 0
}

Write-Output 'PASS PromptCacheStability'
exit 0
