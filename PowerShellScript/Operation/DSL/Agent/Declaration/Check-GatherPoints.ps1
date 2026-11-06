#requires -Version 5.1
<#
.SYNOPSIS
    聚拢点装配守卫（GatherPoints）—— 视图 ↔ 实现 ↔ 门控的装配映射合格性。
.DESCRIPTION
    判据（结构事实 + 单表源码）：
      GP1 PointId：命名（^[a-z][a-z0-9._-]*$，形如 <owner>.<feature>）/ 全局唯一。
      GP2 Views：非空 / 不重复 / 必须存在于 Config\gather\gather-views.generated.json 的 views。
      GP3 实现受控：每个 `class X : IGatherModule` 必须在 AgentGatherPoints.cs 中被 `new X(` 引用（无漏登记）。
      GP4 装配受控：AgentGatherPoints.cs 之外禁止 `new *GatherModule(`（防绕过目录直接装配）。
      GP5 Owner：非空且为 Agent.* 工程名（依赖方向归因键）。
      GP6 GateKey：null 或能在 AgentConfig 开关表中找到同名成员。
      GP7 格式/负对照：每个 `Register(` 必须命中登记行格式；合成样本（缺视图 / 直接 new）必被捕获。
    模式：默认报告（不设退出码）；-Check 违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

$violations = [System.Collections.Generic.List[string]]::new()

$pointsFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Declaration\AgentGatherPoints.cs'
$viewsFile  = Join-Path $Root 'Projects\Plug\Agent\Config\gather\gather-views.generated.json'
$configFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Core\Composition\Configuration\AgentConfig.cs'
$baselineFile = Join-Path $Root 'Frame\PowerShellScript\Baselines\Process\GatherPoints.psd1'
$srcRoot    = Join-Path $Root 'Projects\Plug\Agent\src'

Write-Host ''
Write-Host '============================================================'
Write-Host '  聚拢点装配守卫（GatherPoints）'
Write-Host '============================================================'

# ---------------- 声明面：视图键集（唯一真源 = generated.json） ----------------
$viewKeys = @()
if (-not (Test-Path -LiteralPath $viewsFile)) {
    $violations.Add("[GP2] 聚拢视图声明缺失：$viewsFile（不可判定 ≠ 通过）")
}
else {
    try {
        $viewsDoc = [System.IO.File]::ReadAllText($viewsFile) | ConvertFrom-Json
        if ($null -eq $viewsDoc.views) {
            $violations.Add('[GP2] 聚拢视图声明无 views 段')
        }
        else {
            $viewKeys = @($viewsDoc.views.PSObject.Properties.Name)
        }
    }
    catch {
        $violations.Add("[GP2] 聚拢视图声明不可解析：$($_.Exception.Message)")
    }
}

# ---------------- 登记表：逐行解析 + 规则 ----------------
if (-not (Test-Path -LiteralPath $pointsFile)) {
    $violations.Add("[GP1] 聚拢点登记表缺失：$pointsFile")
}

$pointPattern = 'Register\(\s*"(?<id>[^"]+)"\s*,\s*Views\((?<views>[^)]*)\)\s*,\s*"(?<owner>[^"]+)"\s*,\s*(?<gate>null|"[^"]*")\s*,'
$pointsText = ''
$pointMatches = @()
if (Test-Path -LiteralPath $pointsFile) {
    $pointsText = [System.IO.File]::ReadAllText($pointsFile)
    $pointMatches = @([regex]::Matches($pointsText, $pointPattern))

    # GP7：每个 Register( 调用都必须命中登记行格式（防格式漂移导致静默漏检）。
    $totalPointCalls = ([regex]::Matches($pointsText, '\bRegister\s*\(\s*"')).Count
    if ($totalPointCalls -ne $pointMatches.Count) {
        $violations.Add("[GP7] 登记行格式不匹配：Register( 共 $totalPointCalls 处，命中格式 $($pointMatches.Count) 处（首 4 参数须同一行）")
    }

    if ($pointMatches.Count -eq 0) {
        $violations.Add('[GP1] 未解析到任何聚拢点登记（单表为空 = 装配面失效）')
    }

    $seenIds = @{}
    foreach ($match in $pointMatches) {
        $pointId = $match.Groups['id'].Value
        $owner   = $match.Groups['owner'].Value
        $gateRaw = $match.Groups['gate'].Value
        $gateKey = if ($gateRaw -eq 'null') { $null } else { $gateRaw.Trim('"') }

        # GP1：命名 + 唯一
        if ($pointId -notmatch '^[a-z][a-z0-9._-]*$') {
            $violations.Add("[GP1] PointId 非法：'$pointId'（须匹配 ^[a-z][a-z0-9._-]*$）")
        }
        if ($seenIds.ContainsKey($pointId)) {
            $violations.Add("[GP1] PointId 重复：'$pointId'（同一身份只允许一处权威）")
        }
        else {
            $seenIds[$pointId] = $true
        }

        # GP2：视图非空 / 不重复 / 已声明
        $pointViews = @([regex]::Matches($match.Groups['views'].Value, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
        if ($pointViews.Count -eq 0) {
            $violations.Add("[GP2] '$pointId' 未声明视图")
        }
        $seenViews = @{}
        foreach ($view in $pointViews) {
            if ($seenViews.ContainsKey($view)) {
                $violations.Add("[GP2] '$pointId' 视图重复：'$view'")
            }
            else {
                $seenViews[$view] = $true
            }
            if ($viewKeys -notcontains $view) {
                $violations.Add("[GP2] '$pointId' 视图 '$view' 未在 gather-views.generated.json 登记")
            }
        }

        # GP5：归属工程
        if ([string]::IsNullOrWhiteSpace($owner) -or $owner -notmatch '^Agent\.[A-Za-z0-9_.]+$') {
            $violations.Add("[GP5] '$pointId' Owner 非法：'$owner'（须为 Agent.* 工程名）")
        }

        # GP6：门控键必须在 AgentConfig 开关表中
        if (-not [string]::IsNullOrWhiteSpace($gateKey)) {
            $gateDeclared = $false
            if (Test-Path -LiteralPath $configFile) {
                $gateDeclared = [regex]::IsMatch(
                    [System.IO.File]::ReadAllText($configFile),
                    ('\b' + [regex]::Escape($gateKey) + '\b'))
            }
            if (-not $gateDeclared) {
                $violations.Add("[GP6] '$pointId' 门控键 '$gateKey' 未在 AgentConfig 登记")
            }
        }
    }
}

# ---------------- 正对照基线（防「单表被清空而门禁恒绿」） ----------------
if (Test-Path -LiteralPath $baselineFile) {
    try {
        $baseline = Import-PowerShellDataFile -LiteralPath $baselineFile
        $minPoints = [int]$baseline.MinPoints.Value
        if ($pointMatches.Count -lt $minPoints) {
            $violations.Add("[GP1] 聚拢点数量 $($pointMatches.Count) < 基线下限 $minPoints（删除聚拢点须同步 Baselines\Process\GatherPoints.psd1）")
        }
    }
    catch {
        $violations.Add("[GP1] 基线不可解析：$baselineFile（$($_.Exception.Message)）")
    }
}

# ---------------- 实现受控：所有 IGatherModule 必须被单表引用 ----------------
$implPattern = '(?:sealed\s+|partial\s+|abstract\s+)*class\s+(?<cls>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?:(?:global::)?[A-Za-z0-9_.]+\.)?IGatherModule\b'
$newPattern  = 'new\s+(?<cls>[A-Za-z_][A-Za-z0-9_]*GatherModule)\s*\('
$implClasses = [System.Collections.Generic.List[string]]::new()
$directNews  = 0

if (Test-Path -LiteralPath $srcRoot) {
    foreach ($file in @(Get-ChildItem -LiteralPath $srcRoot -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' })) {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        $code = (($text -split "`n" | Where-Object {
            $t = $_.TrimStart(); -not ($t.StartsWith('//') -or $t.StartsWith('///') -or $t.StartsWith('*'))
        }) -join "`n")

        foreach ($m in [regex]::Matches($code, $implPattern)) {
            $implClasses.Add($m.Groups['cls'].Value)
        }

        if ($file.FullName -ne $pointsFile) {
            foreach ($m in [regex]::Matches($code, $newPattern)) {
                $directNews++
                $violations.Add("[GP4] $($file.FullName)：直接 new $($m.Groups['cls'].Value)( 违规（装配必须经 AgentGatherPoints 单表）")
            }
        }
    }
}

foreach ($impl in @($implClasses | Sort-Object -Unique)) {
    if ($pointsText -notmatch ('new\s+' + [regex]::Escape($impl) + '\s*\(')) {
        $violations.Add("[GP3] IGatherModule 实现 '$impl' 未在 AgentGatherPoints.cs 登记（新增模块必须进单表）")
    }
}

# ---------------- 负对照（防假绿） ----------------
$probePoint = 'Register("ui.ghost", Views("ghost-view"), "Agent.Ui", null,'
if ($probePoint -notmatch $pointPattern) {
    $violations.Add('[GP7] 负对照失效：合成登记行未被格式判据捕获')
}
if ($viewKeys -contains 'ghost-view') {
    $violations.Add('[GP7] 负对照失效：不存在视图未被判据区分')
}
$probeNew = 'new GhostGatherModule(view);'
if ($probeNew -notmatch $newPattern) {
    $violations.Add('[GP7] 负对照失效：合成直接装配未被捕获')
}

# ---------------- 输出 ----------------
if ($ShowDetail) {
    Write-Host ("  聚拢点 {0} 个 / IGatherModule 实现 {1} 个 / 视图 {2} 个 / 单表外直接 new {3} 处" -f `
        $pointMatches.Count, @($implClasses | Sort-Object -Unique).Count, $viewKeys.Count, $directNews) -ForegroundColor Gray
}

if ($violations.Count -eq 0) {
    Write-Host '[OK] 聚拢点装配：0 违规（单表受控 / 视图存在 / 实现全覆盖 / 无绕过装配 / 负对照在位）' -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 聚拢点装配命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }

if ($Check) { exit 1 }
exit 0