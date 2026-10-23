# Check-KeyDomainStrict.ps1
# 键域严格化门禁（K-01 / R-K1 / R-K3 机检面）
#
# 判据（-Check 时违规即退出码 1；全部为结构事实，不含执行者叙述）：
#   KD1 构造收口：`new KeyRef(` / `new KeyDomainId(` 只允许出现在 Modules\KeySpace 内
#                  （域绑定只能经具名工厂/注册表；越界 = 域语义泄漏到调用点）。
#   KD2 域标识派生：`new KeyDomainId(` 只允许出现在 Modules\KeySpace 内（域标识必须经 Of/注册表）。
#   KD3 注册表接线：声明驱动注册表（KeyDomainRegistry）必须在产品侧被引用（防「有类型无消费」）。
#   KD4 负对照：合成样本必须命中 KD1（判据非空转）。
#   （域词隔离由 KS5 单源承担：Check-KeySpaceCoverage.ps1 —— 本门禁不重复实现。）
#
# 模式：默认报告；-Check 违规 → 退出码 1。只读仓库，零侧写。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateFileIndex.psm1') -Force

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

$violations = [System.Collections.Generic.List[string]]::new()
$scanRoots = @(
    (Join-Path $Root 'Frame\CsharpPrj\Modules'),
    (Join-Path $Root 'Projects\Plug\Agent\src')
)
$files = @()
foreach ($scanRoot in $scanRoots) {
    if (-not (Test-Path -LiteralPath $scanRoot)) { continue }
    $files += @(Get-GateFiles -Root $scanRoot -Extension cs |
        Where-Object { $_ -notmatch '\\obj\\' -and $_ -notmatch '\\bin\\' -and $_ -notmatch '\\BuildOutput\\' })
}

foreach ($file in $files) {
    $rel = $file.Substring($Root.Length + 1).Replace('\', '/')
    $text = Get-Content -LiteralPath $file -Raw
    $inKeySpace = $rel -like 'Frame/CsharpPrj/Modules/KeySpace/*'

    if (-not $inKeySpace) {
        if ($text -match 'new\s+KeyRef\s*\(') {
            $violations.Add("KD1 越界构造 KeyRef：$rel（域绑定必须经具名工厂）")
        }
        if ($text -match 'new\s+KeyDomainId\s*\(') {
            $violations.Add("KD2 越界构造 KeyDomainId：$rel（域标识必须经 Of/注册表）")
        }
    }
}

$registryConsumers = @($files | Where-Object {
    $text = Get-Content -LiteralPath $_ -Raw
    $text -match 'KeyDomainRegistry' -and $_.Substring($Root.Length + 1) -notlike 'Frame\CsharpPrj\Modules\KeySpace\*'
})
if ($registryConsumers.Count -eq 0) {
    $violations.Add('KD3 域注册表无产品侧消费方（KeyDomainRegistry 未被引用）')
}

if ($NegativeControl) {
    $probeA = 'var k = new KeyRef("f", "v", KeyPath.Empty);'
    if ($probeA -notmatch 'new\s+KeyRef\s*\(') { $violations.Add('KD4 负对照失败：KeyRef 构造样本未命中') }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'KeyDomainStrict'
