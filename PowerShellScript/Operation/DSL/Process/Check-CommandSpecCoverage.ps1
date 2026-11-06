#requires -Version 5.1
<#
.SYNOPSIS
    命令声明覆盖门禁（CommandSpecCoverage）—— 命令系统与声明表必须一一对应。

.DESCRIPTION
    判据载体 = 源码/声明文件的结构事实（非执行者叙述）：
      R1 注册面：CommandSystemBase 子类 `base(entityManager, "动作"[, "别名"])` 的动作名集合
      D1 声明面：invocation.specs.json 的 specs[].action 集合（单一声明面）
      J1 读取面：经 BasicMethod\Core\Json.psm1 的 Load-JsonConfig（PowerShell 侧 JSON 读取不另写）
      C1 注册未声明 ⇒ 红（新命令绕过声明表）
      C2 声明未注册 ⇒ 红（声明表残留/拼写漂移）
      C3 声明条目缺 targetSpace ⇒ 红（命名空间是契约的一部分）
    模式：默认报告；-Check 有违规退出码 1；-NegativeControl 用合成样本验证判据未失活。

    权威：axioms:SEP.JudgmentCarrierStructural / Strategy.SingleImplementation
    消费方：formula:Gate.CallAdmissibilityGate（R1 声明存在）、
            Modules.Parsing.Invocation.InvocationContractDeclaration（evidence.recomputeCmd）
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$specsRel  = 'Frame\CsharpPrj\Modules\Data\Parsing\Parsing\Invocation\Resources\invocation.specs.json'
$schemaRel = 'Frame\CsharpPrj\Modules\Data\Parsing\Parsing\Invocation\Resources\invocation.specs.v1.schema.json'
$jsonModuleRel = 'Frame\PowerShellScript\BasicMethod\Core\Json.psm1'
$systemDirs = @(
    'Frame\CsharpPrj\Operation\BackEndDriven\CommandExecution',
    'Frame\CsharpPrj\Operation\FrontEndDriven\ControlRender\Commands'
)

$jsonModule = Join-Path $Root $jsonModuleRel
if (Test-Path -LiteralPath $jsonModule) {
    Import-Module $jsonModule -Force -DisableNameChecking
}

function Get-RegisteredActions {
    param([string]$Base)
    $set = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($rel in $systemDirs) {
        $dir = Join-Path $Base $rel
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $dir -Filter '*.cs' -File -Recurse) {
            $text = Get-Content -LiteralPath $file.FullName -Raw
            foreach ($m in [regex]::Matches($text, 'base\(\s*entityManager\s*,\s*"([^"]+)"')) {
                [void]$set.Add($m.Groups[1].Value.Trim())
            }
        }
    }
    return $set
}

function Get-DeclaredActions {
    param([string]$SpecsPath, [System.Collections.Generic.List[string]]$Issues)
    $set = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    if (-not (Test-Path -LiteralPath $SpecsPath)) {
        $Issues.Add("声明表不存在: $SpecsPath")
        return $set
    }

    if (-not (Get-Command -Name 'Load-JsonConfig' -ErrorAction SilentlyContinue)) {
        $Issues.Add("JSON 读取设施不可用（$jsonModuleRel 未导入）")
        return $set
    }

    $sentinel = @{ __loadFailed__ = $true }
    $document = Load-JsonConfig -ConfigPath $SpecsPath -BuiltIn $sentinel
    if ($document -and $document['__loadFailed__']) {
        $Issues.Add("声明表不可解析: $SpecsPath")
        return $set
    }

    $specs = $document['specs']
    if ($null -eq $specs) {
        $Issues.Add("声明表缺少 specs 节点: $SpecsPath")
        return $set
    }

    foreach ($entry in @($specs)) {
        $action = [string]$entry['action']
        if ([string]::IsNullOrWhiteSpace($action)) {
            $Issues.Add('声明条目缺少 action（判据无标的）')
            continue
        }

        if ([string]::IsNullOrWhiteSpace([string]$entry['targetSpace'])) {
            $Issues.Add("声明条目 '$action' 缺少 targetSpace（命名空间未声明）")
        }

        [void]$set.Add($action.Trim())
    }

    return $set
}

function Compare-ActionSets {
    param($Registered, $Declared)
    $missingDecl = @($Registered | Where-Object { -not $Declared.Contains($_) } | Sort-Object)
    $missingReg  = @($Declared   | Where-Object { -not $Registered.Contains($_) } | Sort-Object)
    return [pscustomobject]@{
        RegisteredNotDeclared = $missingDecl
        DeclaredNotRegistered = $missingReg
        TotalRegistered = $Registered.Count
        TotalDeclared = $Declared.Count
    }
}

$issues = [System.Collections.Generic.List[string]]::new()

$registered = Get-RegisteredActions -Base $Root
$declared = Get-DeclaredActions -SpecsPath (Join-Path $Root $specsRel) -Issues $issues
$result = Compare-ActionSets -Registered $registered -Declared $declared

foreach ($a in $result.RegisteredNotDeclared) { $issues.Add("注册未声明: '$a'（命令系统存在但 invocation.specs.json 无声明）") }
foreach ($a in $result.DeclaredNotRegistered) { $issues.Add("声明未注册: '$a'（声明表条目无对应命令系统）") }

# 正对照：声明表非空且注册面非空，否则判「判据无标的」
if ($result.TotalDeclared -eq 0) { $issues.Add('判据无标的：声明表未解析到任何条目（路径/字段漂移）') }
if ($result.TotalRegistered -eq 0) { $issues.Add('判据无标的：注册面未解析到任何命令（目录/正则漂移）') }

# Schema 文件必须存在（C# 侧用它做结构校验；缺失 ⇒ 校验维度失活）
if (-not (Test-Path -LiteralPath (Join-Path $Root $schemaRel))) {
    $issues.Add("Schema 文件不存在: $schemaRel（声明结构校验失活）")
}

if ($NegativeControl) {
    $fakeReg = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$fakeReg.Add('ghost-command')
    $fakeDecl = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$fakeDecl.Add('assert')
    $neg = Compare-ActionSets -Registered $fakeReg -Declared $fakeDecl
    if ($neg.RegisteredNotDeclared.Count -eq 0 -or $neg.DeclaredNotRegistered.Count -eq 0) {
        $issues.Add('负对照失效：合成样本未被判红（判据已失活）')
    }
}

Write-Host "[CommandSpecCoverage] 注册动作=$($result.TotalRegistered) 声明条目=$($result.TotalDeclared)"
if ($issues.Count -eq 0) {
    Write-Host '[CommandSpecCoverage] 通过：注册面与声明面一致'
    exit 0
}

foreach ($i in $issues) { Write-Host "[CommandSpecCoverage] $i" }
if ($Check) { exit 1 }
exit 0
