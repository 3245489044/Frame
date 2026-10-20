# Check-ContextGeometryBoundary.ps1
# 上下文几何内核边界守卫（Share.ContextGeometry）：领域无关化 / 零依赖 / 禁时钟网络 / 段序纪律 / 登记面。
# -Check 违规 exit 1；默认报告模式。只读仓库。

param([switch]$Check)

$ErrorActionPreference = 'Stop'

$repo = $PSScriptRoot
while ($repo -and $repo -ne (Split-Path $repo -Parent)) {
    if ((Test-Path (Join-Path $repo 'Frame')) -and ((Test-Path (Join-Path $repo '.clinerules')) -or (Test-Path (Join-Path $repo 'clinerules')))) { break }
    $repo = Split-Path $repo -Parent
}

$kernelDir = Join-Path $repo 'Frame\CsharpPrj\Share\Frameworks\ContextGeometry'
$modules   = Join-Path $repo 'Frame\CsharpPrj\modules.json'
$bakeRefs  = Join-Path $repo 'Projects\BuildFramework\Graph\props\CsharpBakeRefs.props'
$shareProj = Join-Path $repo 'Frame\CsharpPrj\Share\Share.csproj'

$violations = [System.Collections.Generic.List[string]]::new()

$forbidden = @(
    @{ Pattern = 'using\s+Agent\.';              Why = '禁 Agent 依赖' },
    @{ Pattern = 'using\s+Modules\.';            Why = '禁 Modules 依赖' },
    @{ Pattern = 'using\s+Operation\.';          Why = '禁 Operation 依赖' },
    @{ Pattern = 'DateTime\.UtcNow';             Why = '禁取时钟' },
    @{ Pattern = 'DateTimeOffset\.UtcNow';       Why = '禁取时钟' },
    @{ Pattern = 'DateTime\.Now';                Why = '禁取时钟' },
    @{ Pattern = 'HttpClient';                   Why = '禁网络依赖' },
    @{ Pattern = 'File\.';                       Why = '禁文件 IO' },
    @{ Pattern = 'Directory\.';                  Why = '禁文件 IO' },
    @{ Pattern = 'Console\.';                    Why = '禁控制台 IO' }
)

function Test-ForbiddenText([string]$text) {
    foreach ($item in $forbidden) { if ($text -match $item.Pattern) { return $item } }
    return $null
}

# 正/负对照（防判据恒真/恒假）
if ($null -eq (Test-ForbiddenText 'using Agent.Contract; DateTimeOffset.UtcNow; new HttpClient();')) {
    $violations.Add('[C6] 负对照失败：合成非法样本未被拒')
}
if ($null -ne (Test-ForbiddenText 'public sealed record PrefixSegment(string Key, CacheBand Band, string Hash);')) {
    $violations.Add('[C6] 正对照失败：合法样本被误拒')
}

# C1/C3 内核逐行扫描
if (-not (Test-Path -LiteralPath $kernelDir)) {
    $violations.Add("[C1] 内核目录缺失：$kernelDir")
} else {
    $files = @(Get-ChildItem -LiteralPath $kernelDir -Recurse -Filter '*.cs' -File)
    if ($files.Count -lt 8) { $violations.Add("[C1] 内核源码文件数异常：$($files.Count) < 8") }
    foreach ($file in $files) {
        $lineNo = 0
        foreach ($line in [System.IO.File]::ReadAllLines($file.FullName)) {
            $lineNo++
            $hit = Test-ForbiddenText $line
            if ($null -ne $hit) { $violations.Add("[C1/C3] $($file.Name):$lineNo 命中 $($hit.Pattern)（$($hit.Why)）") }
        }
        # C4 段序纪律：组合内禁按 Key 排序（只允许按 Band 稳定排序）
        $text = [System.IO.File]::ReadAllText($file.FullName)
        if ($text -match 'OrderBy\s*\(\s*\w+\s*=>\s*\w+\.Key') {
            $violations.Add("[C4] $($file.Name) 出现按 Key 排序（段序即语义，只允许 Band 稳定排序）")
        }
    }
}

# C2 零 ProjectReference
$csproj = Join-Path $kernelDir 'Share.ContextGeometry.csproj'
if (-not (Test-Path -LiteralPath $csproj)) {
    $violations.Add("[C2] 内核 csproj 缺失：$csproj")
} elseif ((Get-Content -LiteralPath $csproj -Raw) -match '<ProjectReference') {
    $violations.Add('[C2] Share.ContextGeometry.csproj 含 ProjectReference（须 BCL-only）')
}

# C5 登记面（三处）
foreach ($pair in @(
    @{ Path = $modules;   Token = '"Share.ContextGeometry"';                Name = 'modules.json' },
    @{ Path = $shareProj; Token = 'Proj_Share_ContextGeometry';             Name = 'Share.csproj' },
    @{ Path = $bakeRefs;  Token = 'Include="Share.ContextGeometry"';        Name = 'CsharpBakeRefs.props' }
)) {
    if (-not (Test-Path -LiteralPath $pair.Path)) { $violations.Add("[C5] 缺文件：$($pair.Name)"); continue }
    if ((Get-Content -LiteralPath $pair.Path -Raw) -notmatch [regex]::Escape($pair.Token)) {
        $violations.Add("[C5] $($pair.Name) 未登记 $($pair.Token)")
    }
}

Write-Host '============================================================'
Write-Host '  Share.ContextGeometry 边界守卫'
Write-Host '============================================================'
Write-Host ("  违规: {0}" -f $violations.Count)
foreach ($v in $violations) { Write-Host ("  - {0}" -f $v) -ForegroundColor Yellow }

if ($Check -and $violations.Count -gt 0) { exit 1 }
exit 0
