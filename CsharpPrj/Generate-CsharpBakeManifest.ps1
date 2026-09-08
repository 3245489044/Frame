# ============================================================
# Generate-CsharpBakeManifest.ps1
# CsharpPrj 权威烘焙 —— 完整性清单（manifest）生成器（Phase 2）
#
# 作用：
#   1) 从 6 伞工程递归闭包解析全部被烘焙 csproj → 每个程序集的期望 dll；
#   2) 核对权威目录 $(OutputDir) 中每个 dll 是否存在；
#   3) 全齐 → 写 ".csharp-bake-complete.stamp"（供 EnsureCsharpBaked 快速判定全集已烘焙）；
#      有缺 → 删除 stamp 并列出缺项（触发补烘）。
#
# 消费方：Projects/BuildFramework/CsharpBakeDeploy.targets 的 EnsureCsharpBaked / _RunCsharpBake
# 设计依据：Docs/analysis/2026/9/9.8/2026-09-08-烘焙框架彻底完善-落地方案.md Phase 2
# 用法：pwsh Generate-CsharpBakeManifest.ps1 [-Configuration Debug|Release] [-CsharpPrjRoot <dir>]
# ============================================================
param(
    [string]$Configuration = 'Debug',
    [string]$CsharpPrjRoot = (Split-Path -Parent $MyInvocation.MyCommand.Path)
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($CsharpPrjRoot)
$outDir = Join-Path $root "BuildOutput\$Configuration"
$manifestPath = Join-Path $outDir 'csharp-bake.manifest.json'
$stampPath   = Join-Path $outDir '.csharp-bake-complete.stamp'

# ---------- 1. 从 CsharpPrj\Directory.Build.props 收集简单属性映射（用于展开 $(X) 引用）----------
$props = @{}
$propsFile = Join-Path $root 'Directory.Build.props'
if (Test-Path $propsFile) {
    $xml = Get-Content $propsFile -Raw
    foreach ($m in [regex]::Matches($xml, '<([A-Za-z][A-Za-z0-9_]*)[^>]*>([^<]+)</\1>')) {
        if ($m.Groups[2].Value -match '[\\/]') { $props[$m.Groups[1].Value] = $m.Groups[2].Value }
    }
    for ($i = 0; $i -lt 8; $i++) {
        foreach ($k in @($props.Keys)) {
            $props[$k] = [regex]::Replace($props[$k], '\$\(([A-Za-z][A-Za-z0-9_]*)\)', {
                param($mm) if ($props.ContainsKey($mm.Groups[1].Value)) { $props[$mm.Groups[1].Value] } else { $mm.Value }
            })
        }
    }
}

# ---------- 2. 解析 ProjectReference（含 $(..) 与相对路径） ----------
function Resolve-Prj([string]$inc, [string]$fromFile) {
    $inc = $inc.Trim()
    $dir = Split-Path -Parent $fromFile
    if ($inc -match '^\$\((MSBuildThisFileDirectory|MSBuildProjectDirectory)\)\\?(.*)$') {
        $inc = Join-Path $dir $matches[2]
    } else {
        $inc = [regex]::Replace($inc, '\$\(([A-Za-z][A-Za-z0-9_]*)\)\\?', {
            param($mm) if ($props.ContainsKey($mm.Groups[1].Value)) { ($props[$mm.Groups[1].Value]).TrimEnd('\') + '\' } else { $mm.Value }
        })
    }
    if ($inc -match '\$\([A-Za-z]') { return $null }   # 无法展开 → 跳过（极少）
    $inc = $inc -replace '/', '\'
    if (-not [IO.Path]::IsPathRooted($inc)) { $inc = Join-Path $dir $inc }
    $inc = [IO.Path]::GetFullPath($inc)
    if ($inc -notmatch '\.csproj$') { $inc = $inc + '.csproj' }
    return $inc
}

# ---------- 3. 从 6 伞递归收集闭包工程 ----------
$umbrellas = @(
    (Join-Path $root 'BasicMethod\BasicMethod.csproj'),
    (Join-Path $root 'Modules\Modules.csproj'),
    (Join-Path $root 'Share\Share.csproj'),
    (Join-Path $root 'Operation\Operation.csproj'),
    (Join-Path $root 'Plate\Plate.csproj'),
    (Join-Path $root 'Strategy\Strategy.csproj')
)
$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$stack = New-Object System.Collections.Generic.Stack[string]
foreach ($u in $umbrellas) { if (Test-Path $u) { $stack.Push([IO.Path]::GetFullPath($u)) } }
while ($stack.Count -gt 0) {
    $f = $stack.Pop()
    if (-not $seen.Add($f)) { continue }
    try { $doc = [xml](Get-Content $f -Raw) } catch { Write-Warning "跳过不可解析工程：$f ($($_.Exception.Message))"; continue }
    foreach ($pr in $doc.Project.ItemGroup.ProjectReference) {
        $ref = Resolve-Prj $pr.Include $f
        if ($ref -and (Test-Path $ref) -and -not $seen.Contains($ref)) { $stack.Push($ref) }
    }
}

# ---------- 4. 每个工程 → 期望 dll（AssemblyName 或文件名基底） ----------
$entries = [System.Collections.Generic.List[object]]::new()
$missing = [System.Collections.Generic.List[string]]::new()
$byDll = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($f in $seen) {
    $asm = [IO.Path]::GetFileNameWithoutExtension($f)
    try {
        $doc = [xml](Get-Content $f -Raw)
        # 取首个非空的 <AssemblyName>；无则退回文件名基底（SDK 默认 TargetName）。
        # 注：多 PropertyGroup 时属性枚举会混入 null，必须过滤，否则覆盖成空名。
        $an = @($doc.Project.PropertyGroup.AssemblyName) | Where-Object { -not [string]::IsNullOrEmpty($_) } | Select-Object -First 1
        if ($an) { $asm = $an }
    } catch { }
    $dll = $asm + '.dll'
    if (-not $byDll.Add($dll)) { continue }   # 同名程序集去重
    $rel = $f.Substring($root.Length).TrimStart('\')
    $exists = Test-Path (Join-Path $outDir $dll)
    if (-not $exists) { $missing.Add($dll) }
    $entries.Add([pscustomobject]@{ name = $asm; dll = $dll; project = $rel; exists = $exists })
}

# ---------- 5. 落 manifest + stamp ----------
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
$manifest = [pscustomobject]@{
    generatedUtc = [DateTime]::UtcNow.ToString('o')
    configuration = $Configuration
    count = $entries.Count
    missingCount = $missing.Count
    entries = $entries
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding utf8

if ($missing.Count -eq 0) {
    Set-Content -Path $stampPath -Value ("complete " + [DateTime]::UtcNow.ToString('o')) -Encoding ascii
    Write-Host "[CsharpBakeManifest] OK: $($entries.Count) 程序集全部就绪 → stamp 已写" -ForegroundColor Green
} else {
    if (Test-Path $stampPath) { Remove-Item $stampPath -Force }
    Write-Host "[CsharpBakeManifest] 缺 $($missing.Count) 个 dll（未写 stamp，将触发补烘）：" -ForegroundColor Yellow
    $missing | Sort-Object | ForEach-Object { Write-Host "   - $_" -ForegroundColor Yellow }
}
