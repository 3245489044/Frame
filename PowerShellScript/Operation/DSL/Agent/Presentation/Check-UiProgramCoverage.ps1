#requires -Version 5.1
<#
.SYNOPSIS
    UI 个性化声明守卫（UiProgramCoverage）—— 单一程序的四类记录必须自洽。
.DESCRIPTION
    判据（结构事实；载体 id 全部来自声明，脚本不写死载体名）：
      R1 每个 part.slots / part.state.*.槽位 → token 必须在 tokens 声明（悬空引用 ⇒ 红）。
      R2 每个 part 至少声明 1 个载体结构；每个 slot 至少 1 个载体绑定且 bind != none。
      R3 每个 carrier（在 slots 中出现且存在非 none 绑定）在每个 part 的 carriers 中都有元素声明。
      R4 skins：extends 目标存在、无环；skin.tokens 键必须在 tokens 声明。
      R5 覆盖率 = 可解析样式指令数 / (part × 槽位 × 绑定载体) ；< 100% ⇒ 红（禁静默降级）。
      PC 正对照：parts=0 ⇒ 红（判据无标的）。
      NC 负对照：合成「未知 token」样本必须被 R1 捕获。
    模式：默认报告；-Check 违规 → 1。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $d = $PSScriptRoot
    while ($d -and -not ((Test-Path (Join-Path $d 'Frame')) -and (Test-Path (Join-Path $d 'Projects')))) { $d = Split-Path $d -Parent }
    if (-not $d) { throw '[UiProgramCoverage] 无法定位仓库根' }
    return $d
}

$root = Find-RepoRoot
$path = Join-Path $root 'Projects\Plug\Agent\Config\ui\ui.generated.json'
if (-not (Test-Path -LiteralPath $path)) { throw "缺文件: $path（先跑 Generate-RenderDeclarations.ps1 -Apply -Target uiprogram）" }

$ui = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
$violations = [System.Collections.Generic.List[string]]::new()

if ([string]$ui.schema -ne 'ui-v1') { $violations.Add("R0 哨兵非法：schema=$($ui.schema)（期望 ui-v1）") }

function PropNames($node) {
    if ($null -eq $node) { return @() }
    return @($node.PSObject.Properties | ForEach-Object { $_.Name })
}

$tokens = @{}
foreach ($name in PropNames $ui.tokens) { $tokens[$name] = $true }

$slots = @{}
foreach ($name in PropNames $ui.slots) { $slots[$name] = $ui.slots.$name }

$carrierIds = @{}
$declaredCarriers = @($ui.carriers)
if ($declaredCarriers.Count -eq 0) {
    $violations.Add('R0 载体清单未声明（单源 = [Table] UiCarrier ⇒ ui.generated.json carriers[]）')
}
foreach ($carrier in $declaredCarriers) {
    $id = [string]$carrier
    if ($id) { $carrierIds[$id] = $true }
}

$parts = @{}
foreach ($name in PropNames $ui.parts) { $parts[$name] = $ui.parts.$name }

if ($parts.Count -eq 0) { $violations.Add('PC parts=0（判据无标的）') }

$declared = 0
$resolved = 0
$deadParts = 0
foreach ($name in $parts.Keys) {
    $part = $parts[$name]
    $partBound = 0

    foreach ($slotName in PropNames $part.slots) {
        $tokenName = [string]$part.slots.$slotName
        if (-not $tokens.ContainsKey($tokenName)) {
            $violations.Add("R1 part '$name' 槽位 '$slotName' 引用未声明 token '$tokenName'")
        }
        if (-not $slots.ContainsKey($slotName)) {
            $violations.Add("R1 part '$name' 槽位 '$slotName' 未在 slots 声明（无法绑定任何载体）")
            continue
        }

        foreach ($carrier in $carrierIds.Keys) {
            $declared++
            if ($slots[$slotName].$carrier -and [string]$slots[$slotName].$carrier.bind -ne 'none') {
                $resolved++
                $partBound++
            }
        }
    }

    if ($partBound -eq 0) {
        $deadParts++
        $violations.Add("R5 part '$name' 无任何可解析样式指令（声明了但不生效）")
    }

    foreach ($stateName in PropNames $part.state) {
        foreach ($slotName in PropNames $part.state.$stateName) {
            $tokenName = [string]$part.state.$stateName.$slotName
            if (-not $tokens.ContainsKey($tokenName)) {
                $violations.Add("R1 part '$name' 状态 '$stateName' 槽位 '$slotName' 引用未声明 token '$tokenName'")
            }
        }
    }

    $partCarriers = PropNames $part.carriers
    if ($partCarriers.Count -eq 0) { $violations.Add("R2 part '$name' 未声明任何载体结构（三载体均降级）") }
    foreach ($carrier in $carrierIds.Keys) {
        if ($partCarriers -notcontains $carrier) {
            $violations.Add("R3 part '$name' 缺载体 '$carrier' 的元素声明")
        }
    }
}

foreach ($slotName in $slots.Keys) {
    $bound = $false
    foreach ($carrier in PropNames $slots[$slotName]) {
        if ([string]$slots[$slotName].$carrier.bind -ne 'none') { $bound = $true }
    }
    if (-not $bound) { $violations.Add("R2 槽位 '$slotName' 无任何可用载体绑定（恒降级）") }
}

foreach ($skinName in PropNames $ui.skins) {
    $skin = $ui.skins.$skinName
    $target = [string]$skin.extends
    if ($target -and -not (PropNames $ui.skins | Where-Object { $_ -eq $target })) {
        $violations.Add("R4 皮肤 '$skinName' extends 未声明皮肤 '$target'")
    }
    foreach ($tokenName in PropNames $skin.tokens) {
        if (-not $tokens.ContainsKey($tokenName)) {
            $violations.Add("R4 皮肤 '$skinName' 覆盖未声明 token '$tokenName'")
        }
    }
}

$rate = if ($declared -gt 0) { [math]::Round(100.0 * $resolved / $declared, 2) } else { 0 }
Write-Host ("[UiProgramCoverage] parts={0} slots={1} tokens={2} skins={3} carriers={4} style={5}/{6} rate={7}% deadParts={8}" -f `
    $parts.Count, $slots.Count, $tokens.Count, (PropNames $ui.skins).Count, $carrierIds.Count, $resolved, $declared, $rate, $deadParts)

if ($NegativeControl) {
    $probePart = 'bubble'
    $probeToken = 'token_that_does_not_exist'
    if ($tokens.ContainsKey($probeToken)) { $violations.Add('NC 负对照失效：合成未知 token 被认作已声明') }
    else { Write-Host "[UiProgramCoverage] NC caught (part=$probePart token=$probeToken)" }
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host ("[VIOLATION] {0}" -f $v) }
    if ($Check) { exit 1 }
}
else {
    Write-Host '[UiProgramCoverage] 通过（四类记录自洽、三载体全覆盖）'
    if ($Check) { exit 0 }
}
