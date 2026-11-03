#requires -Version 5.1
<#
.SYNOPSIS
    生效面断言门禁（EffectiveFace）—— 断言「运行时装载面」与「构建产物」同源（SHA256 内容指纹）。

.DESCRIPTION
    B′ 对偶判据（产物 ↔ 生效面）：ANTI-STALE-ARTIFACT-VERIFY 管「源码 ↔ 产物」；
    本件管「**跑的面 == 刚构建的面**」，把「编译了 ≠ 生效」变成可机械复算的结构事实。

    判据（结构事实 · 内容指纹；非 mtime / 非执行者叙述）：
      [E0] 正对照：代表件数 ≥ 基线（判据无标的 ⇒ 假绿）。
      [E1] 生效面代表件在位（Agent.Launcher.dll + 共享核心件）。
      [E2] 代表件指纹 == 对应「构建产物」指纹（同源）：
             Agent.Launcher.dll                                → Projects\Plug\Agent\BuildOutput\<Config>
             Share.HostAssembly / Share.Conversation /
             Plate.HtmlViewer / WorkFlowMethod                 → Frame\CsharpPrj\BuildOutput\<Config>
      [E3] 正/负对照（防判据空转）：对齐判据对「同内容」命中、对**合成违规**（不同内容）必命中。
      输出：[EFFECTIVE] member=… face=… face_sha256=… built_sha256=… aligned=…（生效面坐标 + 内容指纹）。

    模型：
      dev（默认/就地态）：生效面 = 就地（Agent 面 = BuildOutput\<Config>；共享面 = CsharpPrj\BuildOutput\<Config>）。
      locked（热更新态）：生效面 = <GenRoot>\current → gen-<n>（Agent.* 与共享核心件同批）。

    模式：默认报告；-Check 违规 → 退出码 1。只读仓库；零侧写。
    基线 = Baselines\Agent\EffectiveFace.psd1（代表件清单 / 下限）。

.PARAMETER Root
    仓库根（默认由本脚本位置上溯四级推导）。
.PARAMETER Configuration
    构建配置（Debug/Release，默认 Debug）。
.PARAMETER Mode
    装载面模式：dev（就地，默认）/ locked（代目录）。
.PARAMETER Check
    校验模式：违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string]$Configuration = 'Debug',
    [ValidateSet('dev', 'locked')][string]$Mode = 'dev',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..\..\Meta\TextOps.psm1') -Force -DisableNameChecking

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) {
    # 仓库超根由门禁运行时解析（与脚本所在目录深度无关）
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

$violations = [System.Collections.Generic.List[string]]::new()

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$ef = Import-GateBaseline -Name EffectiveFace

$members = @($ef.Members.Value)
$minReps = [int]$ef.MinReps.Value

# 判据载体 = 内容指纹（SHA256 十六进制小写）；不可读 ⇒ $null（判据不可得，不误判）。
function Get-EffectiveFingerprint([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path)) { return $null }
    try { return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    catch { return $null }
}

# 对齐判据（[E2] 本体）—— 抽为函数，供正/负对照复用（防判据分裂）。
function Test-EffectiveAligned([string]$faceFp, [string]$builtFp) {
    return [bool]($faceFp -and $builtFp -and ($faceFp -eq $builtFp))
}

# 构建产物权威目录（每代表件的「新构建」来源）。
$agentBuildDir  = Join-Path $Root "Projects\Plug\Agent\BuildOutput\$Configuration"
$csharpBuildDir = Join-Path $Root "Frame\CsharpPrj\BuildOutput\$Configuration"
# 生效面代根 = 真实装载面（Runtime\Plugins\Agent\current → gen-<n>）；构建暂存面仅作兜底。
# 历史上两处代根并存（Runtime 有 gen-12/13，BuildOutput 有 gen-1），断言错面 ⇒ 门禁假绿。
$runtimeAgentRoot = Join-Path $Root 'Runtime\Plugins\Agent'
$genRoot = Join-Path $runtimeAgentRoot 'Generations'
if (-not (Test-Path -LiteralPath $genRoot)) {
    $genRoot = Join-Path $Root 'Projects\Plug\Agent\BuildOutput\Generations'
}

# G7-2：代表件 = 显式清单 ∪ 构建产物清单驱动的 glob（拆分程序集自动纳入，禁手抄漂移）。
$memberGlobs = @()
if ($ef.ContainsKey('MemberGlobs')) { $memberGlobs = @($ef.MemberGlobs.Value) }
$expandedMembers = [System.Collections.Generic.List[string]]::new()
foreach ($m in $members) { if (-not $expandedMembers.Contains($m)) { $expandedMembers.Add($m) } }
$globMatched = 0
foreach ($pattern in $memberGlobs) {
    if (-not (Test-Path -LiteralPath $agentBuildDir)) { continue }
    foreach ($file in @(Get-ChildItem -LiteralPath $agentBuildDir -Filter $pattern -File -ErrorAction SilentlyContinue)) {
        $globMatched++
        if (-not $expandedMembers.Contains($file.Name)) { $expandedMembers.Add($file.Name) }
    }
}
if ($memberGlobs.Count -gt 0 -and $globMatched -eq 0) {
    $violations.Add(("[E0] 产物清单驱动面为空：{0} 下无匹配 {1}（构建未产或目录错位）" -f $agentBuildDir, ($memberGlobs -join ',')))
}
$members = $expandedMembers.ToArray()

# 生效面目录（按模式）：locked = current→gen-<n>（回落最高代）；dev = 就地构建产物目录。
$genDir = $null
if ($Mode -eq 'locked') {
    # current 指针在插件根（Runtime\Plugins\Agent\current）；旧布局可能放在代根内，二者兼容。
    $ptr = Join-Path $runtimeAgentRoot 'current'
    if (-not (Test-Path -LiteralPath $ptr)) { $ptr = Join-Path $genRoot 'current' }
    if (Test-Path -LiteralPath $ptr) {
        $pname = (Get-Content -LiteralPath $ptr -Raw -ErrorAction SilentlyContinue).Trim()
        if ($pname -match '^gen-[1-9]\d*$' -and (Test-Path -LiteralPath (Join-Path $genRoot $pname))) {
            $genDir = Join-Path $genRoot $pname
        }
    }
    if (-not $genDir) {
        $violations.Add(("[E7] locked 模式未解析到代目录：current 指针缺失/无效（{0}）—— 断言无标的" -f $ptr))
    }
    if (-not $genDir -and (Test-Path -LiteralPath $genRoot)) {
        $gens = @(Get-ChildItem -LiteralPath $genRoot -Directory -ErrorAction SilentlyContinue |
                  Where-Object { $_.Name -match '^gen-\d+$' } |
                  Sort-Object { [int]($_.Name.Substring(4)) })
        if ($gens.Count -gt 0) { $genDir = $gens[$gens.Count - 1].FullName }
    }
}

function Get-BuildOutputDir([string]$name) {
    # Agent 面 = 本仓插件产物目录（Agent.* 单程序集同批）；其余 = 共享核心件目录。
    # 禁按「Launcher 特例」写死：模块层（Agent.Modules.dll）漏判会退回共享目录 ⇒ 恒判未对齐。
    if ($name -like 'Agent.*') { return $agentBuildDir }
    return $csharpBuildDir
}

function Get-FaceDir([string]$name) {
    if ($Mode -eq 'locked') { return $genDir }
    return (Get-BuildOutputDir -name $name)
}

Write-Host '=== [EffectiveFace] 生效面断言（运行时装载面 == 构建产物；内容指纹） ==='
Write-Host ("模式: {0} | 配置: {1}" -f $Mode, $Configuration)
if ($Mode -eq 'locked') {
    Write-Host ("生效代目录: {0}" -f $(if ($genDir) { $genDir } else { '(未解析 —— locked 模式须先产代)' }))
} else {
    Write-Host ("生效面(就地): Agent={0} | 共享={1}" -f $agentBuildDir, $csharpBuildDir)
}

$report = [System.Collections.Generic.List[object]]::new()
foreach ($m in $members) {
    $faceDir  = Get-FaceDir -name $m
    $builtDir = Get-BuildOutputDir -name $m
    $facePath  = if ($faceDir) { Join-Path $faceDir $m } else { $null }
    $builtPath = Join-Path $builtDir $m

    $fpFace  = Get-EffectiveFingerprint $facePath
    $fpBuilt = Get-EffectiveFingerprint $builtPath
    $aligned = Test-EffectiveAligned -faceFp $fpFace -builtFp $fpBuilt

    $report.Add([pscustomobject]@{
        Name = $m; FaceDir = $faceDir; Ok = $aligned; FpFace = $fpFace; FpBuilt = $fpBuilt
    })

    # 生效面坐标 + 内容指纹（结构化输出；供 AI/完成门读「生效面 + 指纹」）。
    Write-Host ("  [EFFECTIVE] member={0} face={1} face_sha256={2} built_sha256={3} aligned={4}" -f `
        $m,
        $(if ($facePath) { $facePath } else { '(none)' }),
        $(if ($fpFace) { $fpFace } else { '(none)' }),
        $(if ($fpBuilt) { $fpBuilt } else { '(none)' }),
        $aligned)

    if (-not $facePath -or -not $fpFace) {
        $violations.Add(("[E1] {0} 在生效面不在位（Mode={1}, face={2}）" -f $m, $Mode, $(if ($faceDir) { $faceDir } else { '(未解析)' })))
    } elseif (-not $fpBuilt) {
        $violations.Add(("[E2] {0} 构建产物不在位：{1}（请先构建）" -f $m, $builtPath))
    } elseif ($fpFace -ne $fpBuilt) {
        $violations.Add(("[E2] {0} 生效面指纹 != 构建产物指纹（{1} vs {2}）" -f $m, $fpFace, $fpBuilt))
    }
}

if ($report.Count -lt $minReps) {
    $violations.Add(("[E0] 代表件数 {0} < 基线 {1}（判据无标的 ⇒ 假绿）" -f $report.Count, $minReps))
}

# ---- [E3] 正/负对照（防判据空转；只读：按名复用既有 Check-*.ps1 作样本，与 Family 目录分层解耦） ----
$ctrlA = Find-GateScript -Name 'EffectiveFace'
$ctrlB = Find-GateScript -Name 'GenerationLayout'
$fpA = Get-EffectiveFingerprint $ctrlA
$fpB = Get-EffectiveFingerprint $ctrlB
# 正对照：同内容 ⇒ 判「对齐」（命中；证明判据非恒假）。
if (-not (Test-EffectiveAligned -faceFp $fpA -builtFp $fpA)) {
    $violations.Add('[E3] 正对照失败：同一内容被判「未对齐」（判据空转）')
}
# 负对照：**合成违规**（不同内容）⇒ 必判「未对齐」（证明判据非恒真）。
if (Test-EffectiveAligned -faceFp $fpA -builtFp $fpB) {
    $violations.Add('[E3] 负对照失败：合成违规（不同内容）未被判「未对齐」（判据失效）')
}
if ($fpA -eq $fpB) {
    $violations.Add('[E3] 负对照失效：样本两文件内容相同（无法构造违规样本）')
}

# ---- [E4] 声明面指纹（Config\**\*.json|*.ushar 内容指纹聚合；声明漂移可复算） ----
$configRoot = Join-Path $Root 'Projects\Plug\Agent\Config'
$declFiles = @()
if (Test-Path -LiteralPath $configRoot) {
    $declFiles = @(Get-ChildItem -LiteralPath $configRoot -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in '.json', '.ushar' } | Sort-Object FullName)
}
$declHash = $null
if ($declFiles.Count -gt 0) {
    $sb = [System.Text.StringBuilder]::new()
    foreach ($f in $declFiles) {
        $rel = $f.FullName.Substring($configRoot.Length).TrimStart('\')
        $fp = Get-EffectiveFingerprint $f.FullName
        [void]$sb.Append($rel).Append('=').Append($fp).Append("`n")
    }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($sb.ToString())
    $declHash = Get-Sha256Hex -Bytes $bytes
}
$loadFace = if ($Mode -eq 'locked') { if ($genDir) { Split-Path -Leaf $genDir } else { '(unresolved)' } } else { 'inplace' }
Write-Host ("  [EFFECTIVE] decl_sha256={0} decl_files={1} load_face={2} mode={3}" -f `
    $(if ($declHash) { $declHash } else { '(none)' }), $declFiles.Count, $loadFace, $Mode)
if (-not $declHash) {
    $violations.Add(("[E4] 声明面指纹不可得：{0} 下无 .json/.ushar 声明（判据无标的 ⇒ 假绿）" -f $configRoot))
}

# ---- [E5] 声明面 P↔S 对账（视图内容域 / 读路径域 ⊆ 聚拢声明域） ----
$uiRegionsPath = Join-Path $configRoot 'ui\ui-regions.json'
$gatherPath   = Join-Path $configRoot 'nodes\node-gather.ushar'
$readPathsPath = Join-Path $configRoot 'nodes\read-paths.json'
if ((Test-Path -LiteralPath $gatherPath) -and (Test-Path -LiteralPath $uiRegionsPath)) {
    $gatherText = Get-Content -LiteralPath $gatherPath -Raw
    $gatherDomains = @([regex]::Matches($gatherText, '(?m)^\[Gather\]\s+(\S+)') | ForEach-Object { $_.Groups[1].Value })
    $uiRegions = Get-Content -LiteralPath $uiRegionsPath -Raw | ConvertFrom-Json
    foreach ($v in @($uiRegions.views)) {
        $domain = $v.content.domain
        if ([string]::IsNullOrWhiteSpace($domain)) {
            $violations.Add(("[E5] 视图 '{0}' 未声明 content.domain（内容身份缺失）" -f $v.view))
        } elseif ($gatherDomains -notcontains $domain) {
            $violations.Add(("[E5] 视图 '{0}' 的内容域 '{1}' 未在 node-gather.ushar 声明（装配期不会产出该域）" -f $v.view, $domain))
        }
    }
    if (Test-Path -LiteralPath $readPathsPath) {
        $readPaths = Get-Content -LiteralPath $readPathsPath -Raw | ConvertFrom-Json
        foreach ($p in @($readPaths.paths)) {
            if ($gatherDomains -notcontains $p.domain) {
                $violations.Add(("[E5] 读路径 '{0}' 的域 '{1}' 未在 node-gather.ushar 声明" -f $p.id, $p.domain))
            }
        }
    } else {
        $violations.Add(("[E5] 读路径事实声明缺失：{0}" -f $readPathsPath))
    }
} else {
    $violations.Add('[E5] 判据不可判：缺 ui-regions.json 或 node-gather.ushar')
}

# ---- [E6] 域装配无 C# 写死（读适配器构造唯一来源 = 声明） ----
$catalogPath = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Framework\Gather\NodeGatherCatalog.cs'
if (Test-Path -LiteralPath $catalogPath) {
    $catalogText = Get-Content -LiteralPath $catalogPath -Raw
    if ($catalogText -match 'AdapterFactory\s*:') {
        $violations.Add('[E6] NodeGatherCatalog 仍含 AdapterFactory 字面装配（读适配器须由 read-adapter 声明驱动）')
    }
    if ($catalogText -match 'frameSelectionId\s*:') {
        $violations.Add('[E6] NodeGatherCatalog 仍含 frameSelectionId 字面值（须由 frame-selection 声明驱动）')
    }
} else {
    $violations.Add(("[E6] 判据不可判：缺 {0}" -f $catalogPath))
}

foreach ($r in $report) {
    $mark = if ($r.Ok) { 'OK ' } else { 'BAD' }
    Write-Host ("  [{0}] {1}" -f $mark, $r.Name)
}

Write-Host ("代表件: {0} | 违规命中: {1}" -f $report.Count, $violations.Count)
if ($violations.Count -gt 0) {
    Write-Host '--- 违规清单 ---'
    $violations | ForEach-Object { Write-Host ("  [违规] {0}" -f $_) }
}

if ($Check -and $violations.Count -gt 0) {
    Write-Host "校验失败：生效面未对齐（Mode=$Mode）—— 任务未完成（禁报「已修复」）。"
    exit 1
}
exit 0
