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
if ([string]::IsNullOrWhiteSpace($Root)) {
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
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
$genRoot        = Join-Path $Root 'Projects\Plug\Agent\BuildOutput\Generations'

# 生效面目录（按模式）：locked = current→gen-<n>（回落最高代）；dev = 就地构建产物目录。
$genDir = $null
if ($Mode -eq 'locked') {
    $ptr = Join-Path $genRoot 'current'
    if (Test-Path -LiteralPath $ptr) {
        $pname = (Get-Content -LiteralPath $ptr -Raw -ErrorAction SilentlyContinue).Trim()
        if ($pname -match '^gen-[1-9]\d*$' -and (Test-Path -LiteralPath (Join-Path $genRoot $pname))) {
            $genDir = Join-Path $genRoot $pname
        }
    }
    if (-not $genDir -and (Test-Path -LiteralPath $genRoot)) {
        $gens = @(Get-ChildItem -LiteralPath $genRoot -Directory -ErrorAction SilentlyContinue |
                  Where-Object { $_.Name -match '^gen-\d+$' } |
                  Sort-Object { [int]($_.Name.Substring(4)) })
        if ($gens.Count -gt 0) { $genDir = $gens[$gens.Count - 1].FullName }
    }
}

function Get-BuildOutputDir([string]$name) {
    if ($name -eq 'Agent.Launcher.dll') { return $agentBuildDir }
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

# ---- [E3] 正/负对照（防判据空转；只读：复用本目录既有 Check-*.ps1 作样本） ----
$ctrlA = Join-Path $PSScriptRoot 'Check-EffectiveFace.ps1'
$ctrlB = Join-Path $PSScriptRoot 'Check-GenerationLayout.ps1'
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
