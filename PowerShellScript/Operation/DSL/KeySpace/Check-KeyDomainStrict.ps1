# Check-KeyDomainStrict.ps1
# 键域严格化门禁（K-01 / R-K1 / R-K3 机检面）
#
# 判据（-Check 时违规即退出码 1；全部为结构事实，不含执行者叙述）：
#   KD1 构造收口：`new KeyRef(` / `new KeyDomainId(` 只允许出现在 Modules\KeySpace 内
#                  （域绑定只能经具名工厂/注册表；越界 = 域语义泄漏到调用点）。
#   KD2 域标识派生：`new KeyDomainId(` 只允许出现在 Modules\KeySpace 内（域标识必须经 Of/注册表）。
#   KD3 注册表接线：声明驱动注册表（KeyDomainRegistry）必须在产品侧被引用（防「有类型无消费」）。
#   KD4 负对照：合成样本必须命中 KD1（判据非空转）。
#   KD5 2 参 KeyRef.Of 产品侧计数 ≤ 基线（R7-C 棘轮）。
#   KD6 键/实体派生单点：Agent.Session 内 `KeyRef.Of(` / `new KeyEntity(` 调用点 = 0
#       （产品侧键策略单源文件 AgentKeySpaceDeclaration.cs 除外；实体构造唯一宿主 = KeyEntityFactory）。
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
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateBaseline.psm1') -Force

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

# ---- KD5（R7-C 棘轮）：产品侧 2 参 KeyRef.Of 计数 ≤ 基线（框架 4 处按设计保留，不入本判据）----
$baselinePath = Join-Path $Root 'Frame\PowerShellScript\Baselines\KeySpace\KeyDomainStrict.psd1'
$twoArgBudget = -1
if (Test-Path -LiteralPath $baselinePath) {
    $baseline = Import-GateBaseline -Name KeyDomainStrict
    $twoArgBudget = [int]$baseline.KD5TwoArgKeyRefOf.Value
} else {
    $violations.Add('KD5 基线缺失：Baselines\KeySpace\KeyDomainStrict.psd1')
}

if ($twoArgBudget -ge 0) {
    $twoArgPattern = 'KeyRef\.Of\(\s*[^,()]+,\s*[^,()]+\)'
    $twoArgCount = 0
    foreach ($file in @($files | Where-Object { $_.Substring($Root.Length + 1) -like 'Projects\Plug\Agent\src\*' })) {
        $twoArgCount += ([regex]::Matches((Get-Content -LiteralPath $file -Raw), $twoArgPattern)).Count
    }

    if ($twoArgCount -gt $twoArgBudget) {
        $violations.Add("KD5 产品侧 2 参 KeyRef.Of 超基线：actual=$twoArgCount, budget=$twoArgBudget（须改具名域工厂）")
    }
}

# ---- KD6（键派生单点）：Agent.Session 内键/实体构造只经具名工厂 ----
# 豁免面 = 产品侧键策略单源文件（族/域词唯一出处）；其余调用点一律经
# KeyEntityFactory（实体）或 AgentKeySpaceDeclaration.TaskRef/MemberRef（键）。
$sessionRoot = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Session'
$kd6Budget0 = 'Projects/Plug/Agent/src/Agent.Session/Keys/AgentKeySpaceDeclaration.cs'
$kd6Count = 0
if (Test-Path -LiteralPath $sessionRoot) {
    foreach ($file in @($files | Where-Object {
                $_.StartsWith($sessionRoot, [System.StringComparison]::OrdinalIgnoreCase) })) {
        $rel6 = $file.Substring($Root.Length + 1).Replace('\', '/')
        if ($rel6 -eq $kd6Budget0) { continue }
        $text6 = Get-Content -LiteralPath $file -Raw
        $kd6Count += ([regex]::Matches($text6, 'KeyRef\.Of\(')).Count
        $kd6Count += ([regex]::Matches($text6, 'new\s+KeyEntity\s*\(')).Count
    }
}
if ($kd6Count -gt 0) {
    $violations.Add("KD6 键/实体派生越界：Agent.Session 内 KeyRef.Of/new KeyEntity 调用点=$kd6Count（应经 KeyEntityFactory / 具名工厂）")
}

if ($NegativeControl) {
    $probeA = 'var k = new KeyRef("f", "v", KeyPath.Empty);'
    if ($probeA -notmatch 'new\s+KeyRef\s*\(') { $violations.Add('KD4 负对照失败：KeyRef 构造样本未命中') }

    $probeB = 'var k = KeyRef.Of("task", key);'
    if ($probeB -notmatch 'KeyRef\.Of\(\s*[^,()]+,\s*[^,()]+\)') {
        $violations.Add('KD4 负对照失败：2 参 KeyRef.Of 样本未命中（KD5 判据空转）')
    }

    $probeC = 'var e = new KeyEntity("id", Array.Empty<KeyRef>());'
    if ($probeC -notmatch 'new\s+KeyEntity\s*\(') {
        $violations.Add('KD4 负对照失败：new KeyEntity 样本未命中（KD6 判据空转）')
    }
    if ('var k = AgentKeySpaceDeclaration.TaskRef(v);' -match 'KeyRef\.Of\(') {
        $violations.Add('KD4 负对照失效：具名工厂样本被 KD6 误命中')
    }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'KeyDomainStrict'
