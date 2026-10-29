#requires -Version 5.1
<#
.SYNOPSIS
    UMF 失败姿态门禁（MountFailurePosture）—— 旧写回退点只减不增。

.DESCRIPTION
    判据（施工单 §5.1 Check-MountFailurePosture）：
      P1  生产代码「门面为空 ⇒ 走旧写」的**三元回退形态**点数 ≤ 基线 LegacyFallbacks（只减不增）。
      P2  `MountBindingCatalog.Default` 在**生产**组合根出现 ⇒ 违规（G4：声明唯一来源；仅测试夹具可用）。
      NEG 合成一行 `_mounts is null ? 旧写 : 新写` 必须被判红。

.EXAMPLE
    .\Check-MountFailurePosture.ps1 -Check -NegativeControl
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

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  UMF MountFailurePosture（旧写回退只减不增）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

$baselinePath = Join-Path $Root 'Frame\PowerShellScript\Baselines\Node\MountFailurePosture.psd1'
if (-not (Test-Path -LiteralPath $baselinePath)) {
    Write-Host "  [失败] 缺基线：$baselinePath" -ForegroundColor Red
    exit 1
}
$baseline = Import-PowerShellDataFile -LiteralPath $baselinePath
$fallbackBudget = [int]$baseline.Facts.LegacyFallbacks.Value
$defaultBudget = [int]$baseline.Facts.DefaultBindingsUsage.Value

function Test-LegacyFallback([string]$line) {
    # 三元回退：挂载门面判空后走旧写（两种书写方向）；只认与 mount 相关的标识符。
    return (($line -match '_\w*[Mm]ount\w*\s+is\s+null\s*\?') -or
            ($line -match '_\w*[Mm]ount\w*\s+is\s+not\s+null\s*\?') -or
            ($line -match '\?\s*[^;]*[Mm]ount(?:Gateway|Port)') -or
            ($line -match '[Mm]ount(?:Gateway|Port)\s+is\s+null\s*\?'))
}

$violations = [System.Collections.Generic.List[string]]::new()
$fallbackHits = 0
$defaultHits = 0

$scanRoots = @(
    (Join-Path $Root 'Projects\Plug\Agent\src'),
    (Join-Path $Root 'Frame\CsharpPrj')
)

foreach ($rootDir in $scanRoots) {
    if (-not (Test-Path -LiteralPath $rootDir)) { continue }
    $files = Get-ChildItem -LiteralPath $rootDir -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|bin|BuildOutput)\\' -and $_.FullName -notlike '*\_deprecated\*' }
    foreach ($file in $files) {
        $rel = $file.FullName.Replace($Root + '\', '')
        $isTest = $rel -match '(Tests|\.Tests)\\' -or $rel -match '\\Tests\\'
        foreach ($line in [System.IO.File]::ReadAllLines($file.FullName)) {
            $trim = $line.TrimStart()
            if ($trim.StartsWith('//') -or $trim.StartsWith('*') -or $trim.StartsWith('/*')) { continue }

            if (-not $isTest -and (Test-LegacyFallback $line)) {
                $fallbackHits++
                $violations.Add("P1 旧写回退：$rel → $($line.Trim())")
            }

            if (-not $isTest -and $line -match 'MountBindingCatalog\.Default') {
                $defaultHits++
                $violations.Add("P2 生产回退 C# 镜像：$rel → $($line.Trim())（声明缺失须可见化，禁用 Default 掩盖）")
            }
        }
    }
}

if ($fallbackHits -gt $fallbackBudget) {
    $violations.Add("P1 旧写回退 $fallbackHits > 基线 $fallbackBudget（只减不增；DueWhen=$($baseline.Facts.LegacyFallbacks.DueWhen)）")
}
if ($defaultHits -gt $defaultBudget) {
    $violations.Add("P2 Default 使用 $defaultHits > 基线 $defaultBudget（目标 0：仅测试夹具可用）")
}

if ($NegativeControl) {
    $sample = 'var port = _mounts is null ? legacyPort : _mounts;'
    if (-not (Test-LegacyFallback $sample)) {
        $violations.Add('NEG 负对照失效：合成三元回退未被判据命中（判据空转）')
    }
    Write-Host '  [负对照] 合成三元回退已被同一判据命中' -ForegroundColor DarkGray
}

Write-Host "  旧写回退 = $fallbackHits / 基线 $fallbackBudget；Default 使用 = $defaultHits / 基线 $defaultBudget" -ForegroundColor DarkGray

if ($ShowDetail -and $violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "  [违规] $_" -ForegroundColor Red }
}

if ($violations.Count -gt 0) {
    Write-Host "  [失败] 违规 $($violations.Count) 项" -ForegroundColor Red
    if ($Check) { exit 1 }
}

Write-Host '  [通过] 旧写回退与 C# 镜像用量均未超基线' -ForegroundColor Green
exit 0
