#requires -Version 5.1
<#
.SYNOPSIS
    UMF 声明对账门禁（MountDeclaration）—— 绑定 ↔ [Relation] 双向一致。

.DESCRIPTION
    判据（设计单 §9.1 Check-MountDeclaration 的当前可判定子集）：

      D1  `MountBindingCatalog.DefaultBindings()` 中每个 `RelationOperation:` 必须在
          `Config\relations\relation-ops.ushar` 的 `[Relation]` 声明中存在。
      D2  每个 `intent=ensure` 的写 operation 至少被一个 Mount 绑定（禁未绑定写入口）。
      D3  绑定字段词表：Stage/Family/Posture 只能是设计单闭集。
      D4  负对照：合成悬空 relation id 必须被同一判据命中。

.PARAMETER Root
    仓库根目录（默认由脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Node → 仓库根）。

.EXAMPLE
    .\Check-MountDeclaration.ps1 -Check -NegativeControl
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()
$relationFile = Join-Path $Root 'Projects\Plug\Agent\Config\relations\relation-ops.ushar'
$mountFile = Join-Path $Root 'Projects\Plug\Agent\Config\relations\mounts.ushar'
$catalogFile = Join-Path $Root 'Frame\CsharpPrj\Share\Frameworks\UniversalReference\Relations\Mounting\MountBindingCatalog.cs'

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  UMF MountDeclaration（绑定 ↔ 关系声明对账）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

if (-not (Test-Path $relationFile)) { Write-Host "  [失败] 缺声明面：$relationFile" -ForegroundColor Red; exit 1 }

# [Relation] id → intent
$relations = @{}
foreach ($line in Get-Content -Path $relationFile) {
    if ($line -notmatch '^\[Relation\]\s+([A-Za-z0-9_.-]+)\s*:') { continue }
    $id = $Matches[1]
    $intent = if ($line -match '\|\s*intent=([A-Za-z]+)') { $Matches[1].ToLowerInvariant() } else { 'ensure' }
    $relations[$id] = $intent
}

$stages = @('live', 'provision', 'settle', 'explicit', 'rebuild')
$families = @('structural', 'presentation')
$postures = @('require', 'best-effort', 'reduced')
$coalesces = @('edge', 'none')
$builtinTriggers = @('node.persisted', 'round.settled', 'task.created', 'command.explicit', 'rebuild.requested')

$bound = New-Object System.Collections.Generic.List[string]

if (Test-Path $mountFile) {
    # 声明面优先：逐条 [Mount] 校验（D1/D3）
    foreach ($line in Get-Content -Path $mountFile) {
        if ($line -notmatch '^\[Mount\]\s+([A-Za-z0-9_.-]+)\s*:') { continue }
        $mountId = $Matches[1]
        $relation = if ($line -match '\|\s*relation=([A-Za-z0-9_.-]+)') { $Matches[1] } else { '' }
        $trigger = if ($line -match '\|\s*trigger=([^|]+)') { $Matches[1].Trim() } else { '' }
        $stage = if ($line -match '\|\s*stage=([A-Za-z-]+)') { $Matches[1] } else { '' }
        $family = if ($line -match '\|\s*family=([A-Za-z-]+)') { $Matches[1] } else { '' }
        $coalesce = if ($line -match '\|\s*coalesce=([A-Za-z-]+)') { $Matches[1] } else { '' }
        $posture = if ($line -match '\|\s*posture=([A-Za-z-]+)') { $Matches[1] } else { '' }
        $order = if ($line -match '\|\s*order=([0-9]+)') { [int]$Matches[1] } else { 0 }

        if ([string]::IsNullOrWhiteSpace($relation)) {
            $violations.Add("D1 [Mount] $mountId 缺 relation=")
        } else {
            $bound.Add($relation)
        }

        if ($stage -notin $stages) { $violations.Add("D3 [Mount] $mountId stage='$stage' 非法") }
        if ($family -notin $families) { $violations.Add("D3 [Mount] $mountId family='$family' 非法") }
        if ($coalesce -notin $coalesces) { $violations.Add("D3 [Mount] $mountId coalesce='$coalesce' 非法") }
        if ($posture -notin $postures) { $violations.Add("D3 [Mount] $mountId posture='$posture' 非法") }
        if ($order -le 0) { $violations.Add("D3 [Mount] $mountId order 必须 > 0") }

        if ([string]::IsNullOrWhiteSpace($trigger)) {
            $violations.Add("D3 [Mount] $mountId trigger= 不能为空")
        } else {
            foreach ($t in ($trigger -split ',')) {
                $tt = $t.Trim()
                if ($tt -and ($builtinTriggers -notcontains $tt)) {
                    $violations.Add("D3 [Mount] $mountId trigger='$tt' 非内置触发（须在 [Obs] kind=event 声明或词表内）")
                }
            }
        }
    }
} elseif (Test-Path $catalogFile) {
    # 过渡期：无声明文件 ⇒ 对账 C# 镜像
    foreach ($line in Get-Content -Path $catalogFile) {
        if ($line -match 'RelationOperation:\s*"([A-Za-z0-9_.-]+)"') { $bound.Add($Matches[1]) }
    }
} else {
    $violations.Add('D1 既无 mounts.ushar 也无 C# 镜像：绑定面缺失')
}

if ($bound.Count -eq 0) { $violations.Add('D1 未解析到任何 Mount → relation 绑定（判据空转）') }

foreach ($op in $bound) {
    if (-not $relations.ContainsKey($op)) {
        $violations.Add("D1 悬空绑定：$op 未在 relation-ops.ushar 的 [Relation] 声明")
    }
}

$ensureOps = @($relations.Keys | Where-Object { $relations[$_] -eq 'ensure' })
foreach ($op in $ensureOps) {
    if ($bound -notcontains $op) {
        $violations.Add("D2 未绑定写入口：$op 是 ensure operation 但无任何 [Mount] 绑定")
    }
}

if ($NegativeControl) {
    $sample = "RelationOperation: `"mount_does_not_exist`","
    if ($sample -match 'RelationOperation:\s*"([A-Za-z0-9_.-]+)"') {
        $sampleId = $Matches[1]
        if ($relations.ContainsKey($sampleId)) {
            $violations.Add('NEG 负对照失效：合成悬空 id 被误判为已声明')
        }
    } else {
        $violations.Add('NEG 负对照失效：合成样本未被判据命中')
    }
    Write-Host '  [负对照] 合成悬空 relation id 已被同一判据命中' -ForegroundColor DarkGray
}

if ($ShowDetail -and $violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "  [违规] $_" -ForegroundColor Red }
}

if ($violations.Count -gt 0) {
    Write-Host "  [失败] 违规 $($violations.Count) 项" -ForegroundColor Red
    exit 1
}

Write-Host "  [通过] 绑定 $($bound.Count) 条 ⇔ ensure operation $($ensureOps.Count) 条；声面对账一致" -ForegroundColor Green
exit 0
