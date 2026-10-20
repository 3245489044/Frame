# Check-ContextGeometryConvergence.ps1
# 上下文几何聚拢点守卫（K0.5/G4）：白名单外零内层端口解析 / 裁剪单入口 / 命中口径单源 / Host 不作为上帝对象。
# -Check 违规 exit 1；默认报告模式。只读仓库。

param([switch]$Check)

$ErrorActionPreference = 'Stop'

$repo = $PSScriptRoot
while ($repo -and $repo -ne (Split-Path $repo -Parent)) {
    if ((Test-Path (Join-Path $repo 'Frame')) -and (Test-Path (Join-Path $repo 'Projects'))) { break }
    $repo = Split-Path $repo -Parent
}

$agentSrc = Join-Path $repo 'Projects\Plug\Agent\src'
$hostFile = Join-Path $agentSrc 'Agent.Modules\Framework\ContextGeometry\ContextGeometryHost.cs'
$wiringFile = Join-Path $agentSrc 'Agent.Modules\Framework\ContextGeometry\GeometryWiring.cs'

$violations = [System.Collections.Generic.List[string]]::new()

function Get-CsFiles([string]$root) {
    if (-not (Test-Path -LiteralPath $root)) { return @() }
    return @(Get-ChildItem -LiteralPath $root -Recurse -Filter '*.cs' -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|BuildOutput)\\' })
}

$files = Get-CsFiles $agentSrc

# C1：内层几何端口解析只允许出现在 GeometryWiring / ContextGeometryHost
$portPattern = 'Resolve<\s*(GeometryPolicy|IToolFaceExposurePolicy|ICacheTelemetrySink|IKeepAliveDriver|IContextProjection|IPrefixSnapshotStore|IToolFaceGate)\s*>'
$allowed = @($hostFile.ToLowerInvariant(), $wiringFile.ToLowerInvariant(),
    (Join-Path $agentSrc 'Agent.Core\Engine\Signal\ConversationChatKernel.cs').ToLowerInvariant())
foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    if ($text -notmatch $portPattern) { continue }
    if ($allowed -contains $file.FullName.ToLowerInvariant()) { continue }
    $violations.Add("[C1] 白名单外解析内层几何端口：$($file.FullName.Substring($repo.Length + 1))")
}

# C2：裁剪单入口（ToolFaceFilter.Apply 只允许 Host 内 + 适配器内定义）
$applyHits = @()
foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $count = ([regex]::Matches($text, 'ToolFaceFilter\.Apply\s*\(')).Count
    if ($count -gt 0) { $applyHits += ,@($file.FullName, $count) }
}
$applyTotal = ($applyHits | ForEach-Object { $_[1] } | Measure-Object -Sum).Sum
if ($null -eq $applyTotal) { $applyTotal = 0 }
if ($applyTotal -gt 1) {
    $violations.Add("[C2] ToolFaceFilter.Apply 调用点 = $applyTotal（>1；应收敛到聚拢点）")
}

# C3：命中口径单源（CacheHitAccounting.Evaluate 只允许内核策略与聚合器）
$evalHits = @()
foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    if ($text -match 'CacheHitAccounting\.Evaluate\s*\(') {
        $evalHits += $file.FullName.Substring($repo.Length + 1)
    }
}
foreach ($hit in $evalHits) {
    if ($hit -notmatch 'CacheProbePolicy\.cs$' -and $hit -notmatch 'CacheTelemetryAggregator\.cs$') {
        $violations.Add("[C3] 命中口径旁路：$hit")
    }
}

# C5：Host 不作为上帝对象（公共方法数 <= 12；行数 <= 400）
if (Test-Path -LiteralPath $hostFile) {
    $hostText = [System.IO.File]::ReadAllText($hostFile)
    $methodCount = ([regex]::Matches($hostText, '(?m)^\s{8}public\s+(?!class|sealed|record|interface)[\w<>,\[\]\s\.\?]+\s+\w+\s*\(')).Count
    if ($methodCount -gt 12) { $violations.Add("[C5] ContextGeometryHost 公共方法数 = $methodCount（>12）") }
    $lineCount = (Get-Content -LiteralPath $hostFile).Count
    if ($lineCount -gt 400) { $violations.Add("[C5] ContextGeometryHost 行数 = $lineCount（>400）") }
} else {
    $violations.Add('[C5] ContextGeometryHost 缺失')
}

# 正/负对照（防判据空转）
if ($portPattern -notmatch 'GeometryPolicy') { $violations.Add('[C6] 负对照失败：端口模式未含 GeometryPolicy') }
if ('ToolFaceFilter.Apply(x)' -notmatch 'ToolFaceFilter\.Apply\s*\(') { $violations.Add('[C6] 正对照失败：Apply 模式未命中合法样本') }

Write-Host '============================================================'
Write-Host '  上下文几何聚拢点守卫（K0.5）'
Write-Host '============================================================'
Write-Host ("  违规: {0}" -f $violations.Count)
foreach ($v in $violations) { Write-Host ("  - {0}" -f $v) -ForegroundColor Yellow }

if ($Check -and $violations.Count -gt 0) { exit 1 }
exit 0
