# Check-ContextGeometryPrefixPurity.ps1
# 前缀纯度守卫（R3）：进前缀的正文段禁含信封字段（MessageId/Timestamp/TraceId/CreatedAt）。
# -Check 违规 exit 1；默认报告模式。只读仓库。
param([switch]$Check)

$ErrorActionPreference = 'Stop'

$repo = $PSScriptRoot
while ($repo -and $repo -ne (Split-Path $repo -Parent)) {
    if ((Test-Path (Join-Path $repo 'Frame')) -and (Test-Path (Join-Path $repo 'Projects'))) { break }
    $repo = Split-Path $repo -Parent
}

$targets = @(
    (Join-Path $repo 'Projects\Plug\Agent\src\Agent.Modules\SubAgent\Runtime\RolePrefixSource.cs'),
    (Join-Path $repo 'Projects\Plug\Agent\src\Agent.Modules\Framework\ContextGeometry\GeometryRequestAdapters.cs'),
    (Join-Path $repo 'Projects\Plug\Agent\src\Agent.Modules\Framework\ContextGeometry\ContextGeometryHost.cs')
)

# 信封字段（禁入 L0/Skill/Tool 段）；仅检测「写入前缀正文」的形态。
$forbidden = @(
    @{ Pattern = 'BodyText\s*=\s*[^;]*(MessageId|Timestamp|TraceId|CreatedAt)'; Why = '信封字段进入前缀正文' },
    @{ Pattern = 'CanonicalText\s*=\s*[^;]*(MessageId|Timestamp|TraceId|CreatedAt)'; Why = '信封字段进入规范字节' },
    @{ Pattern = 'new\("prefix\.(system|tools|role)"[^)]*(MessageId|Timestamp|TraceId|CreatedAt)'; Why = '信封字段进入前缀段' }
)

$violations = [System.Collections.Generic.List[string]]::new()

# 正/负对照（防判据恒真/恒假）
function Test-Purity([string]$text) {
    foreach ($item in $forbidden) { if ($text -match $item.Pattern) { return $item } }
    return $null
}
if ($null -eq (Test-Purity 'BodyText = "prefix" + MessageId;')) { $violations.Add('[R3] 负对照失败：非法样本未被拒') }
if ($null -ne (Test-Purity 'BodyText = canonicalPrefix.System;')) { $violations.Add('[R3] 正对照失败：合法样本被误拒') }

foreach ($file in $targets) {
    if (-not (Test-Path -LiteralPath $file)) { $violations.Add("[R3] 缺失目标文件：$file"); continue }
    $lineNo = 0
    foreach ($line in [System.IO.File]::ReadAllLines($file)) {
        $lineNo++
        $hit = Test-Purity $line
        if ($null -ne $hit) { $violations.Add("[R3] $(Split-Path $file -Leaf):$lineNo 命中（$($hit.Why)）") }
    }
}

Write-Host '============================================================'
Write-Host '  前缀纯度守卫（R3）'
Write-Host '============================================================'
Write-Host ("  违规: {0}" -f $violations.Count)
foreach ($v in $violations) { Write-Host ("  - {0}" -f $v) -ForegroundColor Yellow }

if ($Check -and $violations.Count -gt 0) { exit 1 }
exit 0
