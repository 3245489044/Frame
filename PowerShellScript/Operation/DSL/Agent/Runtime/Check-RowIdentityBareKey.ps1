#requires -Version 5.1
<#
.SYNOPSIS
    行身份裸键回退单处守卫（RowIdentityBareKey）—— 裸键回退路径 = 1。

.DESCRIPTION
    判据（B3/SCT-10）：
      G1 单处分派：Agent src 内 `RowSelection.Decode(` 只允许出现在 `DomainRoute.cs`（唯一分派点）。
      G2 兼容面单源：`DomainRoute.DecodeEntry` 在位。
      G3 正/负对照：合成样本（他处裸键解码）必被捕获（Assert-GateControls 执行）。

    模式：默认报告；-Check 时违规 → 退出码 1。

    框架接线：源文件枚举 → GateFileIndex.Get-GateFiles；扫描 → Find-GatePattern；
            根定位/裁决 → GateRuntime。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateFileIndex.psm1') -Force
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'RowIdentityBareKey'
$violations = $ctx.Violations

Write-GateHeader '行身份裸键回退单处守卫（RowIdentityBareKey）'

# 判据自证
$controls = @(
    @{ Id = 'G3'; Predicate = { param($t) $t -match 'RowSelection\.Decode\s*\(' }
       Good = 'var entry = DomainRoute.DecodeEntry(key);'; Bad = 'RowSelection.Decode(entry, fallback)' }
)
foreach ($issue in (Assert-GateControls -Cases $controls -Label 'RowIdentityBareKey')) { $violations.Add($issue) }

$src = Get-MappedPath -Key 'AgentSrc'

# 源文件枚举（git 索引单源；禁 bin/obj/BuildOutput）
$files = @(Get-GateFiles -Root $src -Extension cs -Exclude '\\(bin|obj|BuildOutput)\\')
$hits = @(Find-GatePattern -Files $files -Pattern 'RowSelection\.Decode\s*\(')

$count = $hits.Count
foreach ($h in $hits) {
    if ((Split-Path $h.File -Leaf) -ne 'DomainRoute.cs') {
        $violations.Add("[G1] 越界裸键解码：$([IO.Path]::GetFileName($h.File)):$($h.Line)（应经 DomainRoute.DecodeEntry）")
    }
}
if ($count -ne 1) { $violations.Add("[G1] 裸键解码总处数 ≠ 1（实际 $count）") }

$route = Join-Path $src 'Agent.Ui\Core\Functions\Db\DomainRoute.cs'
if (-not (Test-Path -LiteralPath $route)) {
    $violations.Add('[G2] 未见 DomainRoute.cs（唯一分派点缺失）')
} elseif ((Get-Content -LiteralPath $route -Raw) -notmatch 'DecodeEntry') {
    $violations.Add('[G2] 唯一兼容入口 DomainRoute.DecodeEntry 缺失')
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'RowIdentityBareKey'
