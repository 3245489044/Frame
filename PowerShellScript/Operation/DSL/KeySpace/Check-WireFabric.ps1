#requires -Version 5.1
<#
.SYNOPSIS
    端口接盒框架门禁（WireFabric）—— 框架在位 / 依赖面受控 / 测试在册（结构事实）。

.DESCRIPTION
    判据载体 = 文件存在性 + csproj 引用集合 + 注册表条目（结构事实；不含动机/收益叙述）。
    与既有 `Wiring`（节点接线生命周期）**不同名不同域**，禁合并。

      C0 在册：GateManifests.psd1 含 WireFabric 登记块
      C1 工程在位：框架 csproj 存在 ∧ 依赖面 = 允许集（Proj_KeySpace / Proj_Parsing_Keys）
      C2 模型在位：Model/Compile/Runtime/Diagnostics 关键文件齐（基线 RequiredFiles）
      C3 测试在册：TestTargets.psd1 含 wiring 别名
      C4 正对照：解析到的 ProjectReference 数 ≥ 1（工程被掏空 ⇒ 判据无标的）
      C5 负对照：同一判据函数对「多出第三个引用」的合成 csproj 文本须判红

    模式：默认报告；-Check 违规 → 退出码 1。只读仓库，零侧写。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force

$baseline = Import-GateBaseline -Name WireFabric
$violations = [System.Collections.Generic.List[string]]::new()

Write-GateHeader '端口接盒框架守卫（WireFabric：在位 / 依赖面 / 测试在册）'

# ---------------- 判据单源：csproj 引用集合 → 违规清单 ----------------
function Test-WireReferenceFace {
    param([string]$ProjectText, [string[]]$Allowed)

    $issues = [System.Collections.Generic.List[string]]::new()
    $refs = [regex]::Matches($ProjectText, '<ProjectReference\s+Include="([^"]+)"') |
        ForEach-Object { $_.Groups[1].Value }

    if (@($refs).Count -lt 1) {
        $issues.Add('[C4] 未解析到任何 ProjectReference（工程可能被掏空）')
        return , $issues
    }

    foreach ($reference in $refs) {
        $key = [regex]::Match($reference, 'Proj_([A-Za-z0-9_]+)').Groups[1].Value
        if ([string]::IsNullOrWhiteSpace($key) -or $Allowed -notcontains ('Proj_' + $key)) {
            $issues.Add("[C1] 依赖面越界：$reference（允许 = $($Allowed -join ', ')）")
        }
    }

    return , $issues
}

$projectRel = [string]$baseline.FrameworkProject.Value
$projectPath = Join-Path $Root $projectRel
$allowed = @($baseline.AllowedReferences | ForEach-Object { [string]$_.Value })

if (-not (Test-Path -LiteralPath $projectPath)) {
    $violations.Add("[C1] 框架工程缺失：$projectRel")
} else {
    $projectText = Get-Content -LiteralPath $projectPath -Raw
    foreach ($issue in (Test-WireReferenceFace -ProjectText $projectText -Allowed $allowed)) {
        $violations.Add($issue)
    }
}

foreach ($entry in @($baseline.RequiredFiles)) {
    $rel = [string]$entry.Value
    if (-not (Test-Path -LiteralPath (Join-Path $Root $rel))) {
        $violations.Add("[C2] 框架文件缺失：$rel")
    }
}

$manifests = Join-Path $Root 'Frame\PowerShellScript\Operation\DSL\Registry\GateManifests.psd1'
if (-not (Test-Path -LiteralPath $manifests) -or
    -not ((Get-Content -LiteralPath $manifests -Raw) -match '(?m)^\s{4}WireFabric\s*=\s*@\{')) {
    $violations.Add('[C0] GateManifests.psd1 未登记 WireFabric')
}

$testTargets = Join-Path $Root 'Environment\CentralTest\TestRunner\TestTargets.psd1'
$alias = [string]$baseline.TestAlias.Value
if (-not (Test-Path -LiteralPath $testTargets) -or
    -not ((Get-Content -LiteralPath $testTargets -Raw) -match ([regex]::Escape("'" + $alias + "'")))) {
    $violations.Add("[C3] TestTargets.psd1 未登记测试别名：$alias")
}

# ---------------- 判据单源：接线声明完整性（结构事实：id/from/to 与规模） ----------------
function Test-WiringDeclaration {
    param($Declaration, [int]$WireFloor)

    $issues = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $Declaration) {
        $issues.Add('[C6] 声明不可解析（null）')
        return , $issues
    }

    $wires = @($Declaration.wires)
    if ($wires.Count -lt $WireFloor) {
        $issues.Add("[C6] 接线规模不足：wires=$($wires.Count) < 下限 $WireFloor")
    }

    foreach ($wire in $wires) {
        $id = [string]$wire.id
        $from = [string]$wire.from
        $to = [string]$wire.to
        if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($from) -or [string]::IsNullOrWhiteSpace($to)) {
            $issues.Add("[C6] wire 缺 id/from/to（id='$id', from='$from', to='$to'）")
        }
    }

    return , $issues
}

$declarationRel = [string]$baseline.WiringDeclaration.Value
$declarationPath = Join-Path $Root $declarationRel
$wireFloor = [int]$baseline.WiringDeclarationWireFloor.Value

if (-not (Test-Path -LiteralPath $declarationPath)) {
    $violations.Add("[C6] 接线声明缺失：$declarationRel")
} else {
    $parsed = $null
    try {
        $parsed = Get-Content -LiteralPath $declarationPath -Raw | ConvertFrom-Json
    } catch {
        $violations.Add("[C6] 接线声明 JSON 解析失败：$($_.Exception.Message)")
    }

    if ($null -ne $parsed) {
        foreach ($issue in (Test-WiringDeclaration -Declaration $parsed -WireFloor $wireFloor)) {
            $violations.Add($issue)
        }
    }
}

# ---------------- C8：声明唯一源指纹（UI 配置 → 生成物，禁手改漂移） ----------------
$sourcePaths = @($baseline.WiringSources | ForEach-Object { [string]$_.Value })
if ($null -ne $parsed) {
    $sourceBytes = [System.Collections.Generic.List[byte]]::new()
    foreach ($rel in $sourcePaths) {
        $full = Join-Path $Root $rel
        if (Test-Path -LiteralPath $full) {
            $sourceBytes.AddRange([System.IO.File]::ReadAllBytes($full))
        } else {
            $violations.Add("[C8] 声明源缺失：$rel")
        }
    }

    if ($sourceBytes.Count -gt 0) {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try { $actualHash = ([BitConverter]::ToString($sha.ComputeHash($sourceBytes.ToArray())) -replace '-', '').ToLowerInvariant() }
        finally { $sha.Dispose() }

        $declaredHash = [string]$parsed.sourceHash
        if ([string]::IsNullOrWhiteSpace($declaredHash) -or $declaredHash -ne $actualHash) {
            $violations.Add("[C8] 声明指纹与 UI 源不一致（declared='$declaredHash', actual='$actualHash'）⇒ 重跑 New-WireFabricDeclaration.ps1")
        }
    }
}

# ---------------- C9：UI 入口覆盖率（生成器自证；判据单源 = 声明内 coverage） ----------------
function Test-CoverageDeclaration {
    param($Declaration, [int]$Floor)
    $issues = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $Declaration -or $null -eq $Declaration.coverage) {
        $issues.Add('[C9] 声明缺 coverage（重跑生成器）')
        return , $issues
    }
    $entries = [int]$Declaration.coverage.entries
    $covered = [int]$Declaration.coverage.covered
    if ($entries -le 0) { $issues.Add('[C9] 入口集合为空（判据无标的）'); return , $issues }
    if ($covered -ne $entries) {
        $issues.Add("[C9] 覆盖率不足：covered=$covered / entries=$entries")
        return , $issues
    }
    $percent = [math]::Round(100.0 * $covered / $entries)
    if ($percent -lt $Floor) { $issues.Add("[C9] 覆盖率 $percent% < 下限 $Floor%") }
    return , $issues
}

$coverageFloor = [int]$baseline.CoverageFloor.Value
foreach ($issue in (Test-CoverageDeclaration -Declaration $parsed -Floor $coverageFloor)) { $violations.Add($issue) }

# ---------------- C10：同一 cmd 源端口唯一 + 事件链解析预算 ----------------
function Test-CommandSourceDeclaration {
    param($Declaration, [int]$Max)
    $issues = [System.Collections.Generic.List[string]]::new()
    $counts = @{}
    foreach ($wire in @($Declaration.wires)) {
        $id = [string]$wire.id
        if ($id -like 'wi_cmd_*' -and $id -notlike 'wi_cmd_result_*') {
            $from = [string]$wire.from
            if (-not $counts.ContainsKey($from)) { $counts[$from] = 0 }
            $counts[$from]++
        }
    }
    foreach ($pair in $counts.GetEnumerator()) {
        if ($pair.Value -gt $Max) { $issues.Add("[C10] 源端口多写：from=$($pair.Key) wires=$($pair.Value)") }
    }
    foreach ($box in @($Declaration.boxes)) {
        if ([string]$box.id -eq 'ui.command_source') { $issues.Add('[C10] 存在共享 ui.command_source（禁 judge 分派）') }
    }
    return , $issues
}

$commandSourceMax = [int]$baseline.CommandSourceMax.Value
foreach ($issue in (Test-CommandSourceDeclaration -Declaration $parsed -Max $commandSourceMax)) { $violations.Add($issue) }

$parseBudget = [int]$baseline.ParseCountMax.Value
$parseTotal = 0
foreach ($rel in @($baseline.ParseFiles | ForEach-Object { [string]$_.Value })) {
    $full = Join-Path $Root $rel
    if (-not (Test-Path -LiteralPath $full)) { $violations.Add("[C10] 解析预算文件缺失：$rel"); continue }
    $text = Get-Content -LiteralPath $full -Raw
    $parseTotal += ([regex]::Matches($text, 'JsonDocument\.Parse')).Count
}
if ($parseTotal -gt $parseBudget) { $violations.Add("[C10] 事件链解析次数=$parseTotal > 上限 $parseBudget") }

# ---------------- C12：Form 无 System.Object ----------------
$forbiddenFormClr = [string]$baseline.ForbiddenFormClr.Value
foreach ($form in @($parsed.forms)) {
    if ([string]$form.clr -eq $forbiddenFormClr) { $violations.Add("[C12] Form 使用 $forbiddenFormClr 兜底：$($form.id)") }
}

# ---------------- C15：profile 子集闭合（图闭包，不复刻 D1–D8） ----------------
foreach ($profile in @($parsed.profiles)) {
    $enable = @($profile.enable)
    if ($enable.Count -eq 0) { $violations.Add("[C15] profile 空子集：$($profile.id)"); continue }
    $enabledWires = @($parsed.wires | Where-Object { $enable -contains [string]$_.id })
    $supplied = @{}
    foreach ($w in $enabledWires) { $supplied[[string]$w.to] = $true }
    foreach ($w in $enabledWires) {
        $to = [string]$w.to
        $targetBox = $to.Split('.')[0]
        $box = @($parsed.boxes | Where-Object { [string]$_.id -eq $targetBox })
        if ($box.Count -eq 0) { continue }
        foreach ($port in @($box[0].ports)) {
            if ([string]$port.dir -ne 'in' -or -not [bool]$port.required) { continue }
            $ref = $targetBox + '.' + [string]$port.name
            if (-not $supplied.ContainsKey($ref)) { $violations.Add("[C15] profile=$($profile.id) 必填输入无供给：$ref") }
        }
    }
}

# ---------------- C13（静态面）：ledger 单写者 ----------------
$ledgerTotal = 0
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $Root 'Frame\CsharpPrj\Modules\Wiring') -Recurse -Filter '*.cs' -File) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    $ledgerTotal += ([regex]::Matches($text, '_ledger\.Record\(')).Count
}
if ($ledgerTotal -ne 1) { $violations.Add("[C13] ledger 写者数=$ledgerTotal（期望 1，Junction 单写者）") }

# 负对照：C9 / C10 判据必须能被合成样本命中（判据恒真即红）
$negativeCoverage = [pscustomobject]@{ coverage = @{ entries = 2; covered = 1 } }
if (@(Test-CoverageDeclaration -Declaration $negativeCoverage -Floor 100 | Where-Object { $_ -match '\[C9\]' }).Count -eq 0) {
    $violations.Add('[C9] 负对照失效：覆盖率不足未被判红')
}
$negativeCmd = [pscustomobject]@{
    boxes = @([pscustomobject]@{ id = 'ui.command.composersend' })
    wires = @(
        [pscustomobject]@{ id = 'wi_cmd_composersend'; from = 'ui.command_source.composersend.event' }
        [pscustomobject]@{ id = 'wi_cmd_composersend2'; from = 'ui.command_source.composersend.event' }
    )
}
if (@(Test-CommandSourceDeclaration -Declaration $negativeCmd -Max 1 | Where-Object { $_ -match '\[C10\]' }).Count -eq 0) {
    $violations.Add('[C10] 负对照失效：同源双 wire 未被判红')
}


# 负对照：合成「缺 from」的 wire 必须被同一判据函数命中
$synthetic = [pscustomobject]@{
    wires = @(
        [pscustomobject]@{ id = 'w_ok'; from = 'a.x'; to = 'b.y' }
        [pscustomobject]@{ id = 'w_bad'; from = ''; to = 'b.y' }
    )
}
$negativeDeclaration = Test-WiringDeclaration -Declaration $synthetic -WireFloor 1
if (@($negativeDeclaration | Where-Object { $_ -match '\[C6\]' }).Count -eq 0) {
    $violations.Add('[C7] 负对照失效：缺 from 的 wire 未被判红（判据恒真？）')
}

# 负对照：合成「多出第三个引用」必须被同一判据函数命中
$negativeText = '<Project><ItemGroup><ProjectReference Include="$(Proj_KeySpace)" />' +
    '<ProjectReference Include="$(Proj_Parsing_Keys)" />' +
    '<ProjectReference Include="$(Proj_Strategy)" /></ItemGroup></Project>'
$negative = Test-WireReferenceFace -ProjectText $negativeText -Allowed $allowed
if (@($negative | Where-Object { $_ -match '\[C1\]' }).Count -eq 0) {
    $violations.Add('[C5] 负对照失效：第三个引用未被判红（判据恒真？）')
}

if ($ShowDetail) {
    Write-Host "  实测：允许引用 = $($allowed -join ', ')；校验文件 $(@($baseline.RequiredFiles).Count) 件"
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'WireFabric'
