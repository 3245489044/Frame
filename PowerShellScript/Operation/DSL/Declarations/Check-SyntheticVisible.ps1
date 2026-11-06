#requires -Version 7.0
<#
.SYNOPSIS
    合成壳可见守卫（UPC D-F7）：合成项必须带 Synthetic 标记且不得清除空因。

.DESCRIPTION
    判据载体 = 源码静态结构（合成标记调用 / 清空因赋值）。
      SV1 标记在位：合成分支必须调用 AsSynthetic 且使用 SyntheticOnly 码。
      SV2 不清空因：投影器不得出现 `rowsEmptyReason = null`（清因 = 伪正常）。
      SV3 模型可见：卡片模型必须含 ContextSynthetic 字段（渲染侧可辨）。
      SV4 负对照（-NegativeControl）：合成「清空因」样本必须判红。
    只读仓库，零侧写。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

# 根定位（深度无关）：GateRuntime → DirectoryMap 以「Frame\ + Projects\」marker 上溯（迁 Environment 后不依赖脚本深度）
$__repoRoot = $PSScriptRoot
while ($__repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $__repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $__repoRoot 'Projects')))) { $__repoRoot = Split-Path -Parent $__repoRoot }
if (-not $__repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $__repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
if ([string]::IsNullOrWhiteSpace($Root)) {
    $ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'SyntheticVisible'
    $Root = $ctx.Root
}

$agentSrc = Get-MappedPath -Key 'AgentSrc'
$projector = Join-Path $agentSrc 'Agent.Ui\Core\Projection\HistoryBoardReadModelProjector.cs'
$model = Join-Path $agentSrc 'Agent.Ui\Ui\Views\Projection\HistoryBoardModel.cs'
$rxClearReason = 'rowsEmptyReason\s*=\s*null'
$violations = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $projector)) {
    $violations.Add("[SV0] 投影器缺失：$projector（判据不可判定）")
} else {
    $text = [IO.File]::ReadAllText($projector)
    if (-not ($text -match 'AsSynthetic')) { $violations.Add('[SV1] 合成分支未调用 AsSynthetic（合成无标记）') }
    if (-not ($text -match 'SyntheticOnly')) { $violations.Add('[SV1] 合成分支未使用 SyntheticOnly 码（空因缺失）') }

    # 只判"清空因"赋值：跳过局部变量声明（string? rowsEmptyReason = null 是初始化，不是清因）。
    foreach ($line in [IO.File]::ReadAllLines($projector)) {
        $trim = $line.Trim()
        if ($trim.StartsWith('//')) { continue }
        if ($trim -match '^string\??\s+rowsEmptyReason') { continue }
        if ($trim -match 'rowsEmptyReason\s*=\s*null') {
            $violations.Add('[SV2] 出现 rowsEmptyReason = null（合成壳清空因 = 伪正常）')
            break
        }
    }
}

if (Test-Path -LiteralPath $model) {
    $modelText = [IO.File]::ReadAllText($model)
    if (-not ($modelText -match 'ContextSynthetic')) { $violations.Add('[SV3] 卡片模型无 ContextSynthetic（渲染不可辨）') }
} else {
    $violations.Add("[SV0] 卡片模型缺失：$model（判据不可判定）")
}

if ($NegativeControl) {
    $sample = 'rowsEmptyReason = null; synthetic = true;'
    if (-not [regex]::IsMatch($sample, $rxClearReason)) { throw '负对照失败：清空因样本未被识别（判据空转）' }
    $positive = 'return PortContext.AsSynthetic(PortContext.Empty(id, PortContextEmptyCodes.SyntheticOnly));'
    if (-not ($positive -match 'AsSynthetic' -and $positive -match 'SyntheticOnly')) { throw '正对照失败：合规合成样本未被识别' }
    Write-Host '[OK] 负对照通过：合成壳清空因样本可判红。' -ForegroundColor Green
}

if ($Check) {
    Write-GateVerdict -Violations $violations -Check -Key 'SyntheticVisible'
}

exit 0
