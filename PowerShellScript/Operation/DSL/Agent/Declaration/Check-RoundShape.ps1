#requires -Version 5.1
<#
.SYNOPSIS
    轮次形状守卫（RoundShape，A-2 / P2-4 / P3-3）—— 样本轮次必须满足 round-shape.ushar。

.DESCRIPTION
    判据载体 = **同一 TS 执行器**（`node build/cli.js canon-replay`）输出的 `roundShape.ok/issues`
    （DR-1：禁在 PowerShell 重写识别逻辑；本脚本只断言，不识别）。

    判据：
      S1 正例样本（samples\*.json 非 `missing-*`）⇒ `roundShape.ok = true`。
      S2 负例样本（`missing-*`）⇒ `roundShape.ok = false`（缺配对必须红；否则判据无鉴别力）。
      S3 每个 agent 至少 1 正例 + 1 负例（负对照缺失 ⇒ 假绿）。
      S4 完整性账：`emitted + countedOnly + unrecognized = total`（D-3）。

.PARAMETER Root
    仓库根目录（默认上溯四级）。

.EXAMPLE
    .\Check-RoundShape.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

$agentsRoot = Join-Path $Root 'Projects\Plug\Agent\Config\agents'
Import-Module (Join-Path (Get-FrameRoot) 'PowerShellScript\Modules\Parsing\Invoke-CanonCli.psm1') -Force
$cliPath    = Resolve-CanonCliPath -Root $Root

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  轮次形状守卫（required / terminal=exactly(n) / pairings / 完整性账）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

if ($null -eq $cliPath)
{
    $violations.Add("[S0] 缺 canon-replay 入口（先构建 ClineApi：pnpm -C Frame/TypeScriptPrj/TsWorkspace/ClineApi run build）")
}

# 从 CLI 输出提取 JSON（node 告警行走 stderr/首行，禁因此判失败）—— 单实现见 Invoke-CanonCli.psm1。

$agentDirs = @(if (Test-Path -LiteralPath $agentsRoot) { Get-ChildItem -LiteralPath $agentsRoot -Directory })
foreach ($dir in $agentDirs)
{
    $samples = @(if (Test-Path -LiteralPath (Join-Path $dir.FullName 'samples')) {
        Get-ChildItem -LiteralPath (Join-Path $dir.FullName 'samples') -Filter '*.json' -File
    })

    $positive = @($samples | Where-Object { $_.Name -notlike 'missing-*' })
    $negative = @($samples | Where-Object { $_.Name -like 'missing-*' })
    if ($positive.Count -eq 0 -or $negative.Count -eq 0)
    {
        $violations.Add("[S3] agent '$($dir.Name)' 样本不足（需 1 正例 + 1 负例；现正=$($positive.Count) 负=$($negative.Count)）")
        continue
    }

    foreach ($sample in $samples)
    {
        if ($null -eq $cliPath) { break }
        $expectOk = $sample.Name -notlike 'missing-*'
        $result = Invoke-CanonReplay -Root $Root -Agent $dir.Name -RoundFile $sample.FullName
        $raw = ($result | ConvertTo-Json -Depth 12 -Compress)

        if ($null -eq $result -or $result.ok -ne $true)
        {
            $violations.Add("[S1] agent '$($dir.Name)' 样本 '$($sample.Name)' 重放失败：$($raw.Trim().Substring(0, [Math]::Min(200, $raw.Trim().Length)))")
            continue
        }

        if ($expectOk -and $result.roundShape.ok -ne $true)
        {
            $violations.Add("[S1] agent '$($dir.Name)' 正例 '$($sample.Name)' 未满足 RoundShape：$($result.roundShape.issues -join '; ')")
        }
        if (-not $expectOk -and $result.roundShape.ok -eq $true)
        {
            $violations.Add("[S2] agent '$($dir.Name)' 负例 '$($sample.Name)' 竟通过 RoundShape（判据无鉴别力 ⇒ 假绿）")
        }

        $stats = $result.stats
        $sum = [int]$stats.emitted + [int]$stats.countedOnly + [int]$stats.unrecognized
        if ($sum -ne [int]$stats.total)
        {
            $violations.Add("[S4] agent '$($dir.Name)' 样本 '$($sample.Name)' 完整性账不平：emitted+countedOnly+unrecognized=$sum ≠ total=$($stats.total)")
        }

        if ($ShowDetail)
        {
            Write-Host ("  - {0}/{1}: hits={2} faults={3} roundShape={4}" -f $dir.Name, $sample.Name, @($result.hits).Count, $result.canonical.metadata.faults, $result.roundShape.ok) -ForegroundColor Gray
        }
    }
}

Write-Host ""
if ($violations.Count -eq 0)
{
    Write-Host "[OK] 轮次形状守卫：0 违规（样本轮次全部满足形状契约；负对照判红）" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 轮次形状守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
