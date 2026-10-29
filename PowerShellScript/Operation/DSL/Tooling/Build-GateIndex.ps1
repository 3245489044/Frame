#requires -Version 5.1
<#
.SYNOPSIS
    门禁索引编译器 —— 从 GateManifests.psd1（门禁登记册）的分类单源 Domain 生成分类索引表 index.generated.md。

.DESCRIPTION
    P0 逻辑模块化：分类轴（Domain）的**唯一真源** = GateManifests.psd1 每件的 Domain 字段
    （由脚本源码归属到「被守卫面」：Meta/Agent/Node/Parsing/Data/Spec/Process/Usharp/Declarations/Build）。
    本脚本把该单源编译为按域分组的索引表（标记圈定，禁手改），与 Build-ModuleIndex.ps1 同范式：
    编译产物 + 写回校验 + 幂等。

    守卫 = Check-GateTaxonomy.ps1（复算等价；清单 ⇄ 索引 ⇄ 脚本文件 三方对账）。

.PARAMETER WhatIf
    仅预览生成结果，不写盘。

.EXAMPLE
    .\Build-GateIndex.ps1
.EXAMPLE
    .\Build-GateIndex.ps1 -WhatIf
#>
[CmdletBinding()]
param(
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

$manifestPath = Join-Path $PSScriptRoot '..\Registry\GateManifests.psd1'
$outPath      = Join-Path $PSScriptRoot '..\Registry\index.generated.md'

if (-not (Test-Path -LiteralPath $manifestPath)) {
    Write-Error "门禁清单不存在：$manifestPath"
    exit 2
}

# 落点口径单源 = 门禁运行时（委托 PScriptManager\Framework\DirectoryMap）：
#   Domain → 候选目录（+ 名称例外）派生；与目录布局解耦；歧义即抛。
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

$gates = Import-PowerShellDataFile -LiteralPath $manifestPath

$rows = foreach ($name in ($gates.Keys | Sort-Object)) {
    $spec   = $gates[$name]
    $domain = [string]$spec.Domain
    $tier   = if ($spec.ContainsKey('Tier')) { [string]$spec.Tier } else { 'full' }
    $family = if ($spec.ContainsKey('Family')) { [string]$spec.Family } else { '—' }
    $form   = if ($spec.ContainsKey('Form')) { [string]$spec.Form } else { '—' }
    [pscustomobject]@{
        Domain = $domain
        Gate   = [string]$name
        Tier   = $tier
        Expect = [int]$spec.Expect
        Family = $family
        Form   = $form
        Script = (Get-GateScriptCell -Gate $name -Domain $domain -AllowFallback)
    }
}

$domains = @($rows.Domain | Sort-Object -Unique)

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('# 门禁索引（生成产物，禁手改）')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('> 由 `Operation\DSL\Tooling\Build-GateIndex.ps1` 从 `Operation\DSL\Registry\GateManifests.psd1` 的 `Domain` / `Family` / `Form` 字段编译生成。')
[void]$sb.AppendLine('> 新增/改域/改名后重跑生成器（幂等）；守卫 = `Operation\DSL\Meta\Check-GateTaxonomy.ps1`。')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('<!-- PROTO_AUTO:gateIndex:BEGIN -->')
[void]$sb.AppendLine('')
foreach ($d in $domains) {
    $items = @($rows | Where-Object { $_.Domain -eq $d } | Sort-Object Gate)
    [void]$sb.AppendLine("## $d（$($items.Count) 件）")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| Gate | Tier | Expect | Family | Form | 脚本 |')
    [void]$sb.AppendLine('|---|---|---|---|---|---|')
    foreach ($it in $items) {
        [void]$sb.AppendLine("| ``$($it.Gate)`` | $($it.Tier) | $($it.Expect) | $($it.Family) | $($it.Form) | ``$($it.Script)`` |")
    }
    [void]$sb.AppendLine('')
}
[void]$sb.AppendLine('<!-- PROTO_AUTO:gateIndex:END -->')
$content = $sb.ToString().TrimEnd("`r", "`n")

Write-Host "== 门禁索引编译器 ==" -ForegroundColor Cyan
Write-Host "登记门数: $($rows.Count) / 域数: $($domains.Count)"
$byDomain = $domains | ForEach-Object { $d = $_; "$d [" + (@($rows | Where-Object { $_.Domain -eq $d }).Count) + "]" }
Write-Host "域分布: $($byDomain -join ', ')"

if ($WhatIf) {
    Write-Host ''
    Write-Host '--- [WhatIf] 生成的索引预览 ---' -ForegroundColor Yellow
    $content
    return
}

Set-Content -LiteralPath $outPath -Value $content -Encoding UTF8

$verify = (Get-Content -LiteralPath $outPath -Raw -Encoding UTF8).TrimEnd("`r", "`n")
if ($verify -ne $content) {
    Write-Host '写回校验: FAILED（读回内容与写入不一致）' -ForegroundColor Red
    exit 1
}
Write-Host "写回校验: OK → $outPath" -ForegroundColor Green
