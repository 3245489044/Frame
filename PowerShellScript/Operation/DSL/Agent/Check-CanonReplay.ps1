#requires -Version 5.1
<#
.SYNOPSIS
    重放对拍守卫（CanonReplay，P2-4 / A-3 / A-5）—— 同一输入必得同一节点集；丢帧必产生可见差集。

.DESCRIPTION
    判据载体 = 同一 TS 执行器（`node build/cli.js canon-replay`）的 `hits`（逐 `kind|ordinal|payloadHash`）。
    本脚本只对拍，不识别（DR-1）。

    判据：
      R1 幂等：同一输入两次运行 ⇒ hits 键集逐项相同（A-5）。
      R2 完整性账：`emitted + countedOnly + unrecognized = total`（D-3）。
      R3 差集可见：人为删一帧 ⇒ hits 键集与完整样本的差集非空 ∧ 告警可见（负对照，防"对拍恒绿"）。

.EXAMPLE
    .\Check-CanonReplay.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$agentsRoot = Join-Path $Root 'Projects\Plug\Agent\Config\agents'
Import-Module (Join-Path $PSScriptRoot '..\..\..\Modules\Parsing\Invoke-CanonCli.psm1') -Force
$cliPath    = Resolve-CanonCliPath -Root $Root
$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  重放对拍守卫（幂等 / 完整性账 / 差集可见）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

if ($null -eq $cliPath)
{
    Write-Host "[FAIL] 缺 canon-replay 入口（先构建 ClineApi）" -ForegroundColor Yellow
    if ($Check) { exit 1 }
    exit 0
}

function Get-ReplayResult([string]$Agent, [string]$RoundFile)
{
    return Invoke-CanonReplay -Root $Root -Agent $Agent -RoundFile $RoundFile
}

function Get-HitKeys([object]$Replay)
{
    if ($null -eq $Replay -or $null -eq $Replay.hits) { return @() }
    return @($Replay.hits | ForEach-Object { "$($_.kind)|$($_.ordinal)|$($_.payloadHash)" } | Sort-Object)
}

$agentDirs = @(if (Test-Path -LiteralPath $agentsRoot) { Get-ChildItem -LiteralPath $agentsRoot -Directory })
foreach ($dir in $agentDirs)
{
    $samples = @(if (Test-Path -LiteralPath (Join-Path $dir.FullName 'samples')) {
        Get-ChildItem -LiteralPath (Join-Path $dir.FullName 'samples') -Filter '*.json' -File
    })
    $sample = @($samples | Where-Object { $_.Name -notlike 'missing-*' }) | Select-Object -First 1
    if ($null -eq $sample) { continue }

    $first  = Get-ReplayResult $dir.Name $sample.FullName
    $second = Get-ReplayResult $dir.Name $sample.FullName
    if ($null -eq $first -or $first.ok -ne $true -or $null -eq $second -or $second.ok -ne $true)
    {
        $violations.Add("[R0] agent '$($dir.Name)' 重放不可用（无法对拍）")
        continue
    }

    # R1 幂等
    $keysA = Get-HitKeys $first
    $keysB = Get-HitKeys $second
    if (($keysA -join "`n") -ne ($keysB -join "`n"))
    {
        $violations.Add("[R1] agent '$($dir.Name)' 两次重放结果不一致（幂等失效）")
    }

    # R2 完整性账
    $stats = $first.stats
    $sum = [int]$stats.emitted + [int]$stats.countedOnly + [int]$stats.unrecognized
    if ($sum -ne [int]$stats.total)
    {
        $violations.Add("[R2] agent '$($dir.Name)' 完整性账不平：$sum ≠ $($stats.total)")
    }

    # R3 差集可见（删最后一帧 ⇒ 有差）
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ("canon-replay-dropframe-" + [guid]::NewGuid().ToString('N') + ".json")
    try
    {
        $doc = Get-Content -LiteralPath $sample.FullName -Raw | ConvertFrom-Json
        $messages = @($doc.snapshot.messages)
        if ($messages.Count -gt 1)
        {
            $dropped = [pscustomobject]@{ snapshot = [pscustomobject]@{ messages = @($messages[0..($messages.Count - 2)]) } }
            ($dropped | ConvertTo-Json -Depth 20) | Set-Content -LiteralPath $temp -Encoding utf8NoBOM
            $mutated = Get-ReplayResult $dir.Name $temp
            $keysMutated = Get-HitKeys $mutated
            $diff = @($keysA | Where-Object { $keysMutated -notcontains $_ })
            if ($diff.Count -eq 0)
            {
                $violations.Add("[R3] agent '$($dir.Name)' 删帧后差集为空（对拍判据无鉴别力 ⇒ 假绿）")
            }
            elseif ($ShowDetail)
            {
                Write-Host ("  - {0}: 删帧差集 {1} 项（示例：{2}）" -f $dir.Name, $diff.Count, $diff[0]) -ForegroundColor Gray
            }
        }
        else
        {
            $violations.Add("[R3] agent '$($dir.Name)' 正例样本消息数 ≤1（无法构造删帧负对照）")
        }
    }
    finally
    {
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
    }
}

Write-Host ""
if ($violations.Count -eq 0)
{
    Write-Host "[OK] 重放对拍守卫：0 违规（幂等 / 账平 / 删帧差集非空）" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 重放对拍守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
