#requires -Version 5.1
<#
.SYNOPSIS
    通用关系端口面守卫（GraphPortSurface）—— 对外端口单文件 / 端口文件零实现 / Agent 不越层。

.DESCRIPTION
    PS1 端口命名空间 public 类型只允许在 Relations\Ports\GraphPorts.cs（文件外 = 0）；白名单与实收对账。
    PS2 GraphPorts.cs 无方法体 / 无 new / 无 IO（仅 const / 自动属性 / record / interface / enum）。
    PS3 实现类不被 Agent.*（组合根 Agent.Launcher 除外）引用；Agent.Ui/Agent.Session 不 using 实现命名空间。
    PS4 端口变更须在基线声明 ChangeClass（Compatible|Breaking）+ 迁移说明。
    PS5 端口类型名/常值不含域词（history/task/node）。
    PS6 子命名空间 ⊆ {Kernel, Derivation, Gather, Gather.Adapters, Mounting}。
    PS7 负对照：端口外 public class 必红；端口文件含表达式体必红。

.PARAMETER Check
    校验模式：存在违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

$__gateRoot = $PSScriptRoot
while ($__gateRoot -and -not (Test-Path -LiteralPath (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1'))) { $__gateRoot = Split-Path -Parent $__gateRoot }
if (-not $__gateRoot) { throw '未定位 GateRuntime（Frame\PowerShellScript\Baselines\GateRuntime.psm1）' }
Import-Module (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if (-not $Root) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name GraphPortSurface

$relationsRelative = 'Frame\CsharpPrj\Share\Frameworks\UniversalReference\Relations'
$portRelative = 'Frame\CsharpPrj\Share\Frameworks\UniversalReference\Relations\Ports\GraphPorts.cs'
$portFull = Join-Path $Root $portRelative

$violations = New-Object System.Collections.Generic.List[string]
$details = New-Object System.Collections.Generic.List[string]

$rootNamespacePattern = 'namespace\s+UniversalWorkflowFramework\.Relations\s*[;{]'
$typePattern = '(?m)^\s*public\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+)*(?:class|interface|record|enum|struct|delegate)\s+([A-Za-z_][A-Za-z0-9_]*)'

# 判据载体 = 源码静态结构（去注释后计数），与执行者叙述无关。
function Remove-Comments([string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return '' }
    $withoutBlocks = [regex]::Replace($text, '(?s)/\*.*?\*/', '')
    return [regex]::Replace($withoutBlocks, '(?m)//.*$', '')
}

function Get-RootNamespaceTypes([string]$path) {
    $text = Get-Content -LiteralPath $path -Raw
    if ($text -notmatch $rootNamespacePattern) { return @() }
    $clean = Remove-Comments $text
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($m in [regex]::Matches($clean, $typePattern)) { $names.Add($m.Groups[1].Value) }
    return $names
}

function Get-PortWhitelist([string]$path) {
    $names = New-Object System.Collections.Generic.List[string]
    $inBlock = $false
    foreach ($line in (Get-Content -LiteralPath $path)) {
        if ($line -match 'port-surface-types:') { $inBlock = $true }
        elseif ($inBlock -and $line -notmatch '^\s*//\s+\S') { $inBlock = $false }
        if (-not $inBlock) { continue }
        $body = $line -replace '^\s*//\s*(port-surface-types:)?\s*', ''
        foreach ($piece in ($body -split ',')) {
            $name = $piece.Trim()
            if ($name -match '^[A-Za-z_][A-Za-z0-9_]*$') { $names.Add($name) }
        }
    }
    return $names
}

function Get-PortImplementationViolations([string]$path) {
    $problems = New-Object System.Collections.Generic.List[string]
    $text = Get-Content -LiteralPath $path -Raw
    $clean = Remove-Comments $text

    foreach ($forbidden in @('System.IO', 'Sqlite', 'Dapper', 'LiteDB')) {
        if ($clean -match [regex]::Escape($forbidden)) { $problems.Add("PS2 端口文件含 IO/实现依赖：$forbidden") }
    }
    if ($clean -match '\bnew\s+[A-Za-z_]') { $problems.Add('PS2 端口文件含 new（工厂应迁到 Relations.Kernel）') }
    if ($clean -match '(?m)^\s*(?:private|internal)\s') { $problems.Add('PS2 端口文件含 private/internal 成员') }
    if ($clean -match '(?m)^\s*(?:public|protected)\s+[^;=\r\n]*\([^)\r\n]*\)\s*(?:=>|\{)\s*$') {
        $problems.Add('PS2 端口文件含方法体（表达式体或块体）')
    }
    if ($clean -match '(?m)^\s*(?:public|protected)\s+[^;=()]+\s*=>') {
        $problems.Add('PS2 端口文件含表达式体成员（计算应迁到 Relations.Kernel）')
    }
    return $problems
}

# ---------------- PS1 / PS5：端口命名空间单文件 ----------------
if (-not (Test-Path -LiteralPath $portFull)) {
    $violations.Add("PS1 端口文件缺失：$portRelative")
} else {
    $outside = New-Object System.Collections.Generic.List[string]
    foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $Root $relationsRelative) -Recurse -File -Filter '*.cs')) {
        if ($file.FullName -eq (Resolve-Path -LiteralPath $portFull).Path) { continue }
        foreach ($name in (Get-RootNamespaceTypes $file.FullName)) {
            $outside.Add("$($file.Name):$name")
        }
    }
    if ($outside.Count -gt 0) {
        $violations.Add("PS1 端口命名空间 public 类型出现在 GraphPorts.cs 之外：$($outside -join ', ')")
    } elseif ($ShowDetail) {
        $details.Add('PS1 端口命名空间单文件 OK')
    }

    $actual = @(Get-RootNamespaceTypes $portFull)
    $whitelist = @(Get-PortWhitelist $portFull)
    if ($whitelist.Count -eq 0) {
        $violations.Add('PS1 端口文件缺少 port-surface-types 白名单（不可审计）')
    } else {
        $diff = @($actual | Where-Object { $whitelist -notcontains $_ })
        if ($diff.Count -gt 0) { $violations.Add("PS1 白名单缺登记：$($diff -join ', ')") }
        $stale = @($whitelist | Where-Object { $actual -notcontains $_ })
        if ($stale.Count -gt 0) { $violations.Add("PS1 白名单登记多余：$($stale -join ', ')") }
    }

    $domainWords = @($actual | Where-Object { $_ -match '(?i)history|task|node' })
    if ($domainWords.Count -gt 0) { $violations.Add("PS5 端口类型名含域词：$($domainWords -join ', ')") }

    foreach ($problem in (Get-PortImplementationViolations $portFull)) { $violations.Add($problem) }

    $changeClass = [string]$baseline.ChangeClass.Value
    if ($changeClass -notin @('Compatible', 'Breaking')) {
        $violations.Add("PS4 基线 ChangeClass 非法（须 Compatible|Breaking，实为 '$changeClass'）")
    }
    if ($changeClass -eq 'Breaking' -and [string]::IsNullOrWhiteSpace([string]$baseline.MigrationNote.Value)) {
        $violations.Add('PS4 ChangeClass=Breaking 但 MigrationNote 为空')
    }
}

# ---------------- PS6：子命名空间受限 ----------------
$allowedNamespaces = @('Kernel', 'Derivation', 'Gather', 'Gather.Adapters', 'Mounting')
if ($baseline.AllowedNamespaces) { $allowedNamespaces = @($baseline.AllowedNamespaces) }
$found = New-Object System.Collections.Generic.List[string]
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $Root $relationsRelative) -Recurse -File -Filter '*.cs')) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($m in [regex]::Matches($text, 'namespace\s+UniversalWorkflowFramework\.Relations\.([A-Za-z0-9_.]+)\s*[;{]')) {
        $found.Add($m.Groups[1].Value)
    }
}
$unknown = @($found | Sort-Object -Unique | Where-Object { $allowedNamespaces -notcontains $_ })
if ($unknown.Count -gt 0) { $violations.Add("PS6 未登记的端口子命名空间：$($unknown -join ', ')") }
elseif ($ShowDetail) { $details.Add("PS6 子命名空间 OK（$(($found | Sort-Object -Unique) -join ', ')）") }

# ---------------- PS3：实现类不越层 ----------------
$implTypes = @(
    'DeclaredRelationKernel', 'RelationPlanCatalog', 'RelationPortLease', 'RelationGatherPoint',
    'RelationAdapterRegistry', 'RelationGatherHosting', 'RelationDerivationEngine'
)
$agentRoot = Join-Path $Root 'Projects\Plug\Agent\src'
if (Test-Path -LiteralPath $agentRoot) {
    foreach ($project in (Get-ChildItem -LiteralPath $agentRoot -Directory)) {
        if ($project.Name -eq 'Agent.Launcher') { continue }
        foreach ($file in (Get-ChildItem -LiteralPath $project.FullName -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue)) {
            $text = Get-Content -LiteralPath $file.FullName -Raw
            foreach ($impl in $implTypes) {
                if ($text -match "\b$impl\b") {
                    $violations.Add("PS3 $($project.Name) 引用了实现类 $impl（$($file.Name)）")
                }
            }
            if ($project.Name -in @('Agent.Ui', 'Agent.Session') -and
                $text -match 'using\s+UniversalWorkflowFramework\.Relations\.(Kernel|Derivation|Gather)') {
                $violations.Add("PS3 $($project.Name) 越层 using 实现命名空间（$($file.Name)）")
            }
        }
    }
    if ($ShowDetail) { $details.Add('PS3 Agent 侧实现类引用扫描完成') }
}

# ---------------- PS7：负对照（判定函数必须能判红） ----------------
if ($NegativeControl) {
    $probeDir = Join-Path ([IO.Path]::GetTempPath()) ('graphport-negative-' + [guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Path $probeDir -Force | Out-Null

        $outsideProbe = Join-Path $probeDir 'OutsideProbe.cs'
        Set-Content -LiteralPath $outsideProbe -Encoding UTF8 -Value @'
namespace UniversalWorkflowFramework.Relations;
public sealed class OutsideProbe { }
'@
        if (@(Get-RootNamespaceTypes $outsideProbe).Count -ne 1) {
            $violations.Add('PS7 负对照失效：端口外 public 类型未被检出')
        }

        $implProbe = Join-Path $probeDir 'PortImplProbe.cs'
        Set-Content -LiteralPath $implProbe -Encoding UTF8 -Value @'
namespace UniversalWorkflowFramework.Relations;
public sealed record PortImplProbe(string Id)
{
    public bool Empty => string.IsNullOrEmpty(Id);
}
'@
        if (@(Get-PortImplementationViolations $implProbe).Count -eq 0) {
            $violations.Add('PS7 负对照失效：端口文件表达式体未被检出')
        }
    }
    finally {
        Remove-Item -LiteralPath $probeDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($ShowDetail) { $details.Add('PS7 负对照命中 OK') }
}

# ---------------- 输出 ----------------
if ($ShowDetail) { foreach ($line in $details) { Write-Host "  $line" } }

if ($violations.Count -eq 0) {
    Write-Host 'GraphPortSurface: PASS'
    exit 0
}

foreach ($violation in $violations) { Write-Host "  VIOLATION: $violation" }
if ($Check) { exit 1 }
exit 0

