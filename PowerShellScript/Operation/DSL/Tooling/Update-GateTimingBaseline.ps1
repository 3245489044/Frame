#requires -Version 5.1
<#
.SYNOPSIS
    更新门禁耗时基线（R5）—— 默认只补缺 / 降值，升高须显式 -AllowIncrease。

.DESCRIPTION
    消费 Invoke-GateCheck -TimingReport 产出的 JSON：
      · 缺基线 → 写入；
      · 新值 < 旧值 → 下调（棘轮）；
      · 新值 > 旧值 → 默认保留旧值（防慢件借“刷新基线”合法化）；
      · -AllowIncrease → 显式允许升高（须在提交信息中说明原因）。

    基线文件 = Frame\PowerShellScript\Baselines\_gate-timing.psd1（`_` 前缀，不进 GateBaselines 单件清册）。
    本件是写操作，不登记进 GateManifests.psd1。

.PARAMETER Report
    gate-timing-report-v1 JSON（Invoke-GateCheck -TimingReport 产物）。
.PARAMETER Write
    实际写盘；缺省仅预览。
.PARAMETER AllowIncrease
    允许升高基线（缺省禁止）。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Report,
    [switch]$Write,
    [switch]$AllowIncrease
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Report)) { Write-Error "报告不存在：$Report"; exit 2 }

$repoRoot = $PSScriptRoot
while ($repoRoot -and $repoRoot -ne (Split-Path $repoRoot -Parent)) {
    if ((Test-Path (Join-Path $repoRoot 'Projects')) -and (Test-Path (Join-Path $repoRoot 'Frame'))) { break }
    $repoRoot = Split-Path $repoRoot -Parent
}
$baselinePath = Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\_gate-timing.psd1'

$reportData = Get-Content -LiteralPath $Report -Raw -Encoding UTF8 | ConvertFrom-Json
$profile = [string]$reportData.profile
if ([string]::IsNullOrWhiteSpace($profile)) { Write-Error '报告缺少 profile 字段'; exit 2 }

$existing = @{ serial = @{}; parallel4 = @{} }
if (Test-Path -LiteralPath $baselinePath) {
    $data = Import-PowerShellDataFile -LiteralPath $baselinePath
    if ($data.Tiers) {
        foreach ($p in @('serial', 'parallel4')) {
            if ($data.Tiers.ContainsKey($p) -and $null -ne $data.Tiers[$p]) {
                $existing[$p] = @{}
                foreach ($k in $data.Tiers[$p].Keys) { $existing[$p][$k] = $data.Tiers[$p][$k] }
            }
        }
    }
}

if (-not $existing.ContainsKey($profile)) { $existing[$profile] = @{} }
$updated = 0
$kept = 0
foreach ($row in @($reportData.gates)) {
    $name = [string]$row.Name
    if ([string]::IsNullOrWhiteSpace($name)) { continue }
    $ms = [int]$row.Ms
    $old = 0
    if ($existing[$profile].ContainsKey($name)) { $old = [int]$existing[$profile][$name].Value }
    if ($old -le 0 -or $ms -lt $old -or $AllowIncrease) {
        $existing[$profile][$name] = @{ Value = $ms; Origin = 'Derived'; DueWhen = '' }
        $updated++
    } else {
        $kept++
    }
}

function Format-Baseline([hashtable]$Map) {
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine('@{')
    [void]$sb.AppendLine('    # ===== 门禁耗时基线（非单件判据；`_` 前缀 ⇒ 不进入 GateBaselines 单件清册）=====')
    [void]$sb.AppendLine('    # 语义（R5）：逐件毫秒基线 + 慢件预算；超过警告比 → WARN，超过失败比 → FAIL。')
    [void]$sb.AppendLine('    #   · 基线按 profile 分开（serial / parallel4），因为并发下的 wall time 不可横比；')
    [void]$sb.AppendLine('    #   · 只降不升：Update-GateTimingBaseline.ps1 默认只补缺/降值，升高须显式 -AllowIncrease；')
    [void]$sb.AppendLine('    #   · 事件行形态 = @{ Value; Origin; DueWhen }（与 GateBaselines U9/U11 对齐）。')
    [void]$sb.AppendLine("    Schema = 'gate-timing-v1'")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('    Tiers = @{')
    foreach ($p in @('serial', 'parallel4')) {
        [void]$sb.AppendLine("        $p = @{")
        if ($Map.ContainsKey($p)) {
            foreach ($k in @($Map[$p].Keys | Sort-Object)) {
                [void]$sb.AppendLine(("            '{0}' = @{{ Value = {1}; Origin = 'Derived'; DueWhen = '' }}" -f $k, [int]$Map[$p][$k].Value))
            }
        }
        [void]$sb.AppendLine('        }')
    }
    [void]$sb.AppendLine('    }')
    [void]$sb.AppendLine('}')
    return $sb.ToString()
}

$text = Format-Baseline -Map $existing
Write-Host ("profile={0} | 更新 {1} 件 / 保留 {2} 件 | 目标={3}" -f $profile, $updated, $kept, $baselinePath)
if ($Write) {
    Set-Content -LiteralPath $baselinePath -Value $text -Encoding UTF8
    Write-Host '已写入。' -ForegroundColor Green
} else {
    Write-Host '（预览模式：加 -Write 落盘）' -ForegroundColor DarkGray
}
exit 0
