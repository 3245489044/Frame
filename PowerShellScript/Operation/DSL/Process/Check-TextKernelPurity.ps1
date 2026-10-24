#requires -Version 5.1
<#
.SYNOPSIS
    通用文本代数纯度门禁（TextKernelPurity）：锁住「内核零领域语义 + 判据/拼接单源」。
.DESCRIPTION
    C1 词表纯净：通用目录代码行不得出现领域常量（prompt.* / 角色名 / 缓存带 / 层号）；
    C2 裸判定受限：IsNullOrEmpty|IsNullOrWhiteSpace|string.Join 仅允许出现在语义承担位白名单；
    C3 单源：TextState 枚举 / TextJoin.Apply / TextCompose.Compose / TextContract.From 各恰 1 个定义点；
    C4 负对照：合成违规样本必须被判出（防检测器恒真）。
    仅扫描 Frame/.../Composition/Text/**/*.cs（注释行不计，注释里的举例不构成领域语义）。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$textDir = Join-Path $Root 'Frame\CsharpPrj\Share\Frameworks\Conversation\Composition\Text'
$allowedRawJudge = @('TextJoin.cs', 'TextStateMap.cs', 'ISegmentContext.cs', 'TextJudge.cs', 'TextKey.cs', 'TextSegmentRegistry.cs')
$domainPattern = 'prompt\.|"system"|"user"|\bCacheBand\b|\bL0\b|\bL1\b|\bL2\b'
$rawJudgePattern = 'IsNullOrEmpty|IsNullOrWhiteSpace|string\.Join'

function Get-CodeLines {
    param([string[]]$Lines)
    foreach ($line in $Lines) {
        $t = $line.Trim()
        if ($t.StartsWith('//') -or $t.StartsWith('*') -or $t.StartsWith('/*') -or $t.Length -eq 0) { continue }
        $line
    }
}

function Get-PurityViolations {
    param([hashtable]$Sources)   # fileName -> code lines

    $violations = [System.Collections.Generic.List[string]]::new()
    if ($Sources.Count -eq 0) { $violations.Add('通用文本代数目录缺失或无源码'); return $violations }

    foreach ($name in $Sources.Keys) {
        foreach ($line in $Sources[$name]) {
            if ($line -match $domainPattern) { $violations.Add("$name 出现领域常量：$($line.Trim())") }
            if (($name -notin $allowedRawJudge) -and ($line -match $rawJudgePattern)) {
                $violations.Add("$name 自写裸判定：$($line.Trim())")
            }
        }
    }

    $all = ($Sources.Keys | ForEach-Object { $Sources[$_] }) -join "`n"
    if (([regex]::Matches($all, 'enum\s+TextState\b')).Count -ne 1) { $violations.Add('TextState 定义点 ≠ 1') }
    if (([regex]::Matches($all, 'public\s+static\s+string\s+Apply\(')).Count -ne 1) { $violations.Add('TextJoin.Apply 定义点 ≠ 1') }
    if (([regex]::Matches($all, 'public\s+static\s+TextAssembly\s+Compose\(')).Count -ne 1) { $violations.Add('TextCompose.Compose 定义点 ≠ 1') }
    if (([regex]::Matches($all, 'public\s+static\s+TextContract\s+From\(')).Count -ne 1) { $violations.Add('TextContract.From 定义点 ≠ 1') }
    return $violations
}

if ($NegativeControl) {
    $probe = Get-PurityViolations -Sources @{
        'Bad.cs' = @('var k = "prompt.l2";', 'var s = string.Join("\n", parts);')
    }
    if ($probe.Count -lt 2) {
        Write-Output 'FAIL TextKernelPurity: 负对照未命中（检测器恒真）'
        exit 1
    }
    Write-Output "PASS TextKernelPurity (negative control: $($probe.Count) violations)"
    exit 0
}

if (-not (Test-Path -LiteralPath $textDir)) {
    Write-Output "FAIL TextKernelPurity: 目录缺失 $textDir"
    if ($Check) { exit 1 }
    exit 0
}

$sources = @{}
foreach ($file in Get-ChildItem -LiteralPath $textDir -Filter *.cs -File) {
    $sources[$file.Name] = @(Get-CodeLines -Lines (Get-Content -LiteralPath $file.FullName))
}

if ($ShowDetail) { Write-Output "TextDir=$textDir | files=$($sources.Count)" }

$violations = Get-PurityViolations -Sources $sources
if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Output "  - $_" }
    Write-Output "FAIL TextKernelPurity: $($violations.Count) violation(s)"
    if ($Check) { exit 1 }
    exit 0
}

Write-Output "PASS TextKernelPurity ($($sources.Count) files)"
exit 0
