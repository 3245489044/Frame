#requires -Version 7.0
<#
.SYNOPSIS
    事实生产者单一守卫（UPC D-F2）：同一事实的实现类型定义数必须 = 1（禁同义平行实现）。

.DESCRIPTION
    判据载体 = 源码静态结构（类型定义计数）；执行者叙述不参与。
      FP1 定义唯一：登记事实的 producer 类型在 Projects + Frame\CsharpPrj 中恰有 1 处定义。
      FP2 登记完备：登记表内类型必须可在源码中定位（缺失 ⇒ 判据不可判定）。
      FP3 负对照（-NegativeControl）：合成「第二份同名定义」样本必须判红。
    只读仓库，零侧写。
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

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateFileIndex.psm1') -Force

# 登记表：事实 → 唯一 producer 类型（Definitions = 允许的类声明数；partial 多文件登记 >1）。
$facts = @(
    @{ Fact = 'member-window';     Type = 'MemberWindowReader';        Definitions = 1; Path = 'Projects\Plug\Agent\src\Agent.Modules\Response\Dispatch\MemberWindowReader.cs' },
    @{ Fact = 'member-runtime';    Type = 'TaskRuntimeReportProvider'; Definitions = 1; Path = 'Projects\Plug\Agent\src\Agent.Session\Projection\History\TaskRuntimeReportProvider.cs' },
    @{ Fact = 'port-context';      Type = 'PortContextAssembler';      Definitions = 2; Path = 'Frame\CsharpPrj\Share\Frameworks\PortContext\PortContextAssembler.cs' }
)

function Count-TypeDefinitions {
    param([string[]]$Texts, [string]$Type)
    $rx = "class\s+$([regex]::Escape($Type))\b"
    $count = 0
    foreach ($text in $Texts) { $count += [regex]::Matches($text, $rx).Count }
    return $count
}

function Test-FactProducerSingle {
    param([string]$RootPath, [array]$Facts)

    $violations = [System.Collections.Generic.List[string]]::new()
    $sources = @()
    $sources += @(Get-GateFiles -Root (Join-Path $RootPath 'Projects') -Extension cs | Where-Object { $_ -notmatch '\\BuildOutput\\|\\obj\\' })
    $sources += @(Get-GateFiles -Root (Join-Path $RootPath 'Frame\CsharpPrj') -Extension cs | Where-Object { $_ -notmatch '\\BuildOutput\\|\\obj\\' })
    $texts = @($sources | ForEach-Object { [IO.File]::ReadAllText($_) })

    foreach ($fact in $Facts) {
        $declared = Join-Path $RootPath $fact.Path
        if (-not (Test-Path -LiteralPath $declared)) {
            $violations.Add("[FP2] 事实 $($fact.Fact) 的 producer 文件缺失：$($fact.Path)（判据不可判定）")
            continue
        }

        $count = Count-TypeDefinitions -Texts $texts -Type $fact.Type
        $expected = [int]$fact.Definitions
        if ($count -ne $expected) {
            $violations.Add("[FP1] 事实 $($fact.Fact) 的 producer $($fact.Type) 定义数=$count（须 =$expected：同义平行实现）")
        } else {
            Write-Host ("[ok] {0} → {1} 定义数={2}" -f $fact.Fact, $fact.Type, $count) -ForegroundColor Green
        }
    }

    return $violations
}

if ($NegativeControl) {
    $sample = 'public sealed class MemberWindowReader { } public sealed class MemberWindowReader { }'
    $caught = (Count-TypeDefinitions -Texts @($sample) -Type 'MemberWindowReader') -gt 1
    if (-not $caught) { throw '负对照失败：重复定义样本未被识别（判据空转）' }
    $single = 'public sealed class MemberWindowReader { }'
    if ((Count-TypeDefinitions -Texts @($single) -Type 'MemberWindowReader') -ne 1) { throw '正对照失败：单定义样本未被识别' }
    Write-Host '[OK] 负对照通过：重复 producer 样本可判红。' -ForegroundColor Green
}

if ($Check) {
    $violations = Test-FactProducerSingle -RootPath $Root -Facts $facts
    Write-GateVerdict -Violations $violations -Check -Key 'FactProducerSingle'
}

exit 0
