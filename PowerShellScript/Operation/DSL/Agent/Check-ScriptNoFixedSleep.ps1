#requires -Version 5.1
<#
.SYNOPSIS
    脚本禁固定等待门禁（替代 sleep ≥ 1000ms 的同步手段；棘轮基线只降不升）。

.DESCRIPTION
      [S1] 脚本声明面不得**新增** action=sleep 且 target ≥ 阈值（默认 1000ms）的落点：
           固定等待使失败落在「未跑完」而非强判据（实测 90000ms 定格 → 窗口关闭 → totalCommands=0）。
           存量按 `Baselines\FixedSleepBaseline.psd1` 冻结（每文件计数），只降不升。
      [S2] 正/负对照：合成样本（sleep 90000）必被捕获，合成样本（sleep 300）不得误报。
      [S3] 轮询能力在位：WaitUntilSystem 存在且已在 CommandCoordinator 注册。
      [S4] 收录口径：新增脚本落点超出基线 → 违规；低于基线 → 提示下调（不失败）。
    只读源码/脚本；-Check 时违规 > 0 退出码 1；-WriteBaseline 为唯一写操作（重算基线）。

.PARAMETER Root
    仓库根（默认上溯五级）。

.PARAMETER Threshold
    固定等待阈值（毫秒，默认 1000）：≥ 阈值纳入基线/违规；短等待（UI 让位）不计。

.PARAMETER Check
    校验模式。

.PARAMETER WriteBaseline
    按当前脚本面重写基线（棘轮只降不升由人工评审保证；本开关仅生成）。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [int]$Threshold = 1000,
    [switch]$Check,
    [switch]$WriteBaseline
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$scriptRoot = Join-Path $Root 'Projects\Information\DrivenConfig\Scripts'
$baselinePath = Join-Path $Root 'Frame\PowerShellScript\Baselines\FixedSleepBaseline.psd1'
$waitUntil = Join-Path $Root 'Frame\CsharpPrj\Operation\BackEndDriven\CommandExecution\WaitUntilSystem.cs'
$coordinator = Join-Path $Root 'Frame\CsharpPrj\Operation\BackEndDriven\CommandExecution\CommandCoordinator.cs'

$violations = [System.Collections.Generic.List[string]]::new()
$notes = [System.Collections.Generic.List[string]]::new()

function Get-SleepHits {
    param($Node, [string]$File, [string]$Path)
    if ($null -eq $Node -or $Node -is [string] -or $Node -is [int] -or $Node -is [long] -or $Node -is [bool]) { return }
    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [System.Management.Automation.PSCustomObject]) {
        $i = 0
        foreach ($item in $Node) {
            Get-SleepHits -Node $item -File $File -Path "$Path[$i]"
            $i++
        }
        return
    }
    $action = $Node.action
    $target = $Node.target
    if ($action -eq 'sleep' -and "$target" -match '^\d+$' -and [int]$target -ge $Threshold) {
        "$File|$Path|$target"
    }
    foreach ($p in $Node.PSObject.Properties) {
        Get-SleepHits -Node $p.Value -File $File -Path "$Path.$($p.Name)"
    }
}

# [S2] 正/负对照
$pos = @(Get-SleepHits -Node ('{"commands":[{"action":"sleep","target":"90000"}]}' | ConvertFrom-Json) -File '<positive-control>' -Path '$')
if ($pos.Count -ne 1) { $violations.Add("[S2] 正对照失败：合成样本 sleep=90000 命中 $($pos.Count) 次（期望 1）") }
$neg = @(Get-SleepHits -Node ('{"commands":[{"action":"sleep","target":"300"}]}' | ConvertFrom-Json) -File '<negative-control>' -Path '$')
if ($neg.Count -ne 0) { $violations.Add("[S2] 负对照失败：合成样本 sleep=300 被误报 $($neg.Count) 次（期望 0）") }

# [S1] 扫描脚本声明面（按文件聚合计数）
$counts = @{}
if (-not (Test-Path -LiteralPath $scriptRoot)) {
    $violations.Add("[S1] 未找到脚本根: $scriptRoot")
}
else {
    $files = Get-ChildItem -LiteralPath $scriptRoot -Recurse -Filter *.json -File -ErrorAction SilentlyContinue
    foreach ($file in $files) {
        try {
            $json = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
            $relative = $file.FullName.Substring($scriptRoot.Length).TrimStart('\', '/') -replace '\\', '/'
            $hits = @(Get-SleepHits -Node $json -File $relative -Path '$')
            if ($hits.Count -gt 0) {
                $counts[$relative] = $hits.Count
                foreach ($hit in $hits) {
                    $parts = $hit -split '\|'
                    $notes.Add("$($parts[0]) $($parts[1]) (sleep=$($parts[2]) ms)")
                }
            }
        }
        catch {
            $violations.Add("[S1] 脚本 JSON 解析失败（无法审计）：$($file.FullName) — $($_.Exception.Message)")
        }
    }
}

if ($WriteBaseline) {
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# 固定等待基线（棘轮只降不升）：由 Check-ScriptNoFixedSleep.ps1 -WriteBaseline 生成')
    $lines.Add('@{')
    foreach ($kv in ($counts.GetEnumerator() | Sort-Object Name)) {
        $lines.Add("    '$($kv.Name)' = $($kv.Value)")
    }
    $lines.Add('}')
    Set-Content -LiteralPath $baselinePath -Value $lines -Encoding UTF8
    Write-Host "[已写基线] $baselinePath（文件数=$($counts.Count)）"
    exit 0
}

# [S4] 与基线对账（只降不升）
if (-not (Test-Path -LiteralPath $baselinePath)) {
    $violations.Add("[S4] 缺少基线文件（先执行 -WriteBaseline）：$baselinePath")
}
else {
    $baseline = Import-PowerShellDataFile -LiteralPath $baselinePath
    foreach ($file in $counts.Keys) {
        $allowed = if ($baseline.ContainsKey($file)) { [int]$baseline[$file] } else { 0 }
        if ($counts[$file] -gt $allowed) {
            $violations.Add("[S4] 固定等待新增落点：$file 实测 $($counts[$file]) / 基线 $allowed")
        }
        elseif ($counts[$file] -lt $allowed) {
            $notes.Add("待下调基线：$file 实测 $($counts[$file]) / 基线 $allowed")
        }
    }
    foreach ($file in $baseline.Keys) {
        if (-not $counts.ContainsKey($file) -and [int]$baseline[$file] -gt 0) {
            $notes.Add("待下调基线：$file 实测 0 / 基线 $($baseline[$file])")
        }
    }
}

# [S3] 有界轮询能力在位（无转义，固定字符串匹配）
if (-not (Test-Path -LiteralPath $waitUntil)) {
    $violations.Add('[S3] 缺少 WaitUntilSystem.cs（wait-until 有界轮询能力缺失）')
}
elseif (-not (Select-String -LiteralPath $coordinator -Pattern 'new WaitUntilSystem(' -SimpleMatch -Quiet)) {
    $violations.Add('[S3] CommandCoordinator 未注册 WaitUntilSystem（wait-until 不可用）')
}

if ($notes.Count -gt 0) {
    $notes | ForEach-Object { Write-Host "[提示] $_" }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "[违规] $_" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host "[通过] 脚本固定等待无新增（≥ ${Threshold}ms 仅存基线内）；wait-until 有界轮询在位（正/负对照命中）"
exit 0

