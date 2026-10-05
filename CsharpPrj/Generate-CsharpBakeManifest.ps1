# ============================================================
# Generate-CsharpBakeManifest.ps1
# CsharpPrj 权威烘焙 —— 完整性清单（manifest）生成器（Phase 2）
#
# 作用：
#   1) 从 6 伞工程（+ DataCenter 平台库解决方案）递归闭包解析全部被烘焙 csproj → 每个程序集的期望 dll；
#   2) 核对权威目录中每个 dll 是否存在，且**是否新鲜**（dll 时间 ≥ 其编译输入最新时间）；
#   3) 全在且全新鲜 → 写 ".csharp-bake-complete.stamp"（供 EnsureCsharpBaked 快速判定全集已烘焙）；
#      有缺或陈旧 → 删除 stamp 并列出项（触发补烘）。
#
# 消费方：Projects/BuildFramework/CsharpBakeDeploy.targets 的 EnsureCsharpBaked / _RunCsharpBake
# 设计依据：Docs/analysis/2026/9/9.8/2026-09-08-烘焙框架彻底完善-落地方案.md Phase 2
#           + D9/E1（存在性判据 ≠ 新鲜度判据：改了源而 dll 未重建时旧实现报「全部就绪」= 假绿）
# 用法：pwsh Generate-CsharpBakeManifest.ps1 [-Configuration Debug|Release] [-CsharpPrjRoot <dir>] [-SkipFreshnessCheck]
# ============================================================
param(
    [string]$Configuration = 'Debug',
    [string]$CsharpPrjRoot = (Split-Path -Parent $MyInvocation.MyCommand.Path),
    # 逃生开关：只跳过「新鲜度」判据（存在性判据始终生效）。用于陈旧源文件无法通过重烘消除的排查场景。
    [switch]$SkipFreshnessCheck
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($CsharpPrjRoot)
$outDir = Join-Path $root "BuildOutput\$Configuration"
$manifestPath = Join-Path $outDir 'csharp-bake.manifest.json'
$stampPath   = Join-Path $outDir '.csharp-bake-complete.stamp'

# ---------- 1. 收集简单属性映射（用于展开 $(X) 引用）----------
# 注：须连同**导入的生成式属性**一起读——$(Proj_X)/$(Mod_X) 定义在
#     generated\Directory.Build.props.modules.generated.props，只读 Directory.Build.props
#     会令 $(Proj_X) 无法展开 → 闭包递归在伞工程处即中断（清单只剩 6 伞）。
$props = @{}
$propsFiles = @(
    (Join-Path $root 'Directory.Build.props'),
    (Join-Path $root 'generated\Directory.Build.props.modules.generated.props'),
    (Join-Path $root 'generated\Directory.Build.props.versions.generated.props')
)
foreach ($propsFile in $propsFiles) {
    if (-not (Test-Path $propsFile)) { continue }
    $xml = Get-Content $propsFile -Raw
    foreach ($m in [regex]::Matches($xml, '<([A-Za-z][A-Za-z0-9_]*)[^>]*>([^<]+)</\1>')) {
        if ($m.Groups[2].Value -match '[\\/]') { $props[$m.Groups[1].Value] = $m.Groups[2].Value }
    }
}
# 生成式文件把 CsharpPrjDir 定义为 GetFullPath 调用（非简单路径）→ 展开后会残留 $(MSBuildThisFileDirectory)，
# 故以本脚本已解析的权威根目录覆盖之（须在收集之后、展开之前；**保留尾随分隔符**——生成式定义带 '\'，
# $(CsharpPrjDir)BasicMethod\... 依赖它拼出正确路径，缺分隔符会拼成 ...\CsharpPrjBasicMethod\...）。
$props["CsharpPrjDir"] = $root.TrimEnd('\') + '\'
for ($i = 0; $i -lt 8; $i++) {
    foreach ($k in @($props.Keys)) {
        $props[$k] = [regex]::Replace($props[$k], '\$\(([A-Za-z][A-Za-z0-9_]*)\)', {
            param($mm) if ($props.ContainsKey($mm.Groups[1].Value)) { $props[$mm.Groups[1].Value] } else { $mm.Value }
        })
    }
}

# ---------- 2. 解析 ProjectReference（含 $(..) 与相对路径） ----------
function Resolve-Prj([string]$inc, [string]$fromFile) {
    $inc = $inc.Trim()
    $dir = Split-Path -Parent $fromFile
    if ($inc -match '^\$\((MSBuildThisFileDirectory|MSBuildProjectDirectory)\)\\?(.*)$') {
        $inc = Join-Path $dir $matches[2]
    } else {
        # 属性值一律**逐字替换**：生成式定义自带约定（目录型 $(X) 含尾随 '\'，$(Proj_X) 为完整 csproj 路径）；
        # 旧实现统一补 '\' 会把 $(Proj_X) 变成 "X.csproj\"，令下方 .csproj 后缀判定失败 → 闭包递归静默中断。
        $inc = [regex]::Replace($inc, '\$\(([A-Za-z][A-Za-z0-9_]*)\)', {
            param($mm) if ($props.ContainsKey($mm.Groups[1].Value)) { $props[$mm.Groups[1].Value] } else { $mm.Value }
        })
    }
    if ($inc -match '\$\([A-Za-z]') { return $null }   # 无法展开 → 跳过（极少）
    $inc = $inc -replace '/', '\'
    if (-not [IO.Path]::IsPathRooted($inc)) { $inc = Join-Path $dir $inc }
    $inc = [IO.Path]::GetFullPath($inc)
    if ($inc -notmatch '\.csproj$') { $inc = $inc + '.csproj' }
    return $inc
}

# ---------- 3. 从 6 伞 + DataCenter 解决方案递归收集闭包工程 ----------
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

# ---------- 3b. 烘焙根表 + DataCenter 平台库（D9/E1）----------
# 每个根有自己的权威输出目录（DataCenter 的 OutputPath 是自己的 BuildOutput，与 CsharpPrj 不同层），
# 故「期望 dll 路径」必须按工程所属根解析，不能一律用 $outDir。
$bakeRoots = [System.Collections.Generic.List[object]]::new()
[void]$bakeRoots.Add([pscustomobject]@{ label = 'CsharpPrj'; dir = $root; outDir = $outDir })

# DataCenter 平台库不在 CsharpPrj 闭包内（独立解决方案）⇒ 此前不参与烘焙，改了源而 dll 未重建即「假绿」。
# 入口取解决方案（= 烘焙入口，与 Bake-CsharpPrj.ps1 同源），不留第二份工程清单。
$dataCenterRoot = [IO.Path]::GetFullPath((Join-Path $root '..\DataCenter'))
$dataCenterSolution = Join-Path $dataCenterRoot 'DataCenter.slnx'
if (Test-Path $dataCenterSolution) {
    [void]$bakeRoots.Add([pscustomobject]@{
        label  = 'DataCenter'
        dir    = $dataCenterRoot
        outDir = Join-Path $dataCenterRoot "BuildOutput\$Configuration"
    })
    foreach ($m in [regex]::Matches((Get-Content $dataCenterSolution -Raw), '<Project\s+Path="([^"]+)"')) {
        $p = Join-Path $dataCenterRoot (($m.Groups[1].Value) -replace '/', '\')
        if (Test-Path $p) { $stack.Push([IO.Path]::GetFullPath($p)) }
    }
}

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
# 工程可能不在 $root 之下（DataCenter 平台库）⇒ 相对路径与输出目录一律按**所属烘焙根**解析；
# 旧实现 `$f.Substring($root.Length)` 对越根路径会错位/抛异常。
function Get-OwnerRoot([string]$projectFile) {
    $best = $null
    foreach ($r in $bakeRoots) {
        $prefix = $r.dir.TrimEnd('\') + '\'
        if ($projectFile.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            if (-not $best -or $r.dir.Length -gt $best.dir.Length) { $best = $r }
        }
    }
    # 越根工程（Projects\* 经 ProjectReference 进入闭包）：回落首个烘焙根（历史语义——其产物同样落
    # CsharpPrj 权威目录，实测 Projects\Route\Route.dll 即在其中）；不新增「跳过」以免覆盖收缩。
    if (-not $best) { $best = $bakeRoots[0] }
    return $best
}

# ---------- 4a. 兼容工具（PS 5.1） + 工程排除面解析 ----------

# PS 5.1 兼容相对路径：构建目标 _RunCsharpBake 以 `powershell`（5.1 / .NET Framework）调用本脚本，
# 而 [IO.Path]::GetRelativePath 仅 .NET Core 2.0+ 提供 → 5.1 抛 MethodNotFound，令 manifest 刷新恒失败（MSB3073）。
function Get-RelativePathCompat([string]$BaseDir, [string]$TargetPath) {
    $sep = [char]92
    $base = [IO.Path]::GetFullPath($BaseDir).TrimEnd($sep)
    $target = [IO.Path]::GetFullPath($TargetPath)
    $baseParts = $base.Split($sep)
    $targetParts = $target.Split($sep)
    $common = 0
    while ($common -lt $baseParts.Count -and $common -lt $targetParts.Count -and
           [string]::Equals($baseParts[$common], $targetParts[$common], [StringComparison]::OrdinalIgnoreCase)) { $common++ }

    $parts = [System.Collections.Generic.List[string]]::new()
    for ($i = $common; $i -lt $baseParts.Count; $i++) { [void]$parts.Add('..') }
    for ($i = $common; $i -lt $targetParts.Count; $i++) { [void]$parts.Add($targetParts[$i]) }
    return ($parts -join $sep)
}

# 工程 <Compile Remove="glob">（相对工程目录）→ 锚定正则：** 跨目录、* 限单层、? 单字符。
# 作用：把本工程显式摘掉的源排除出「编译输入」扫描。同目录子树里的他被编译源（如
# HttpMethod.Transport 摘 Host 子树、Host 摘 Transport/OpenAI/Guard）若不排除，会给本工程
# 伪造永久陈旧信号 → stamp 永不落 → 每轮构建重复补烘（撞上执行期缺陷即整体失败）。
function ConvertTo-CompileRemoveRegex([string]$Glob) {
    $sep = [char]92
    $sepRe = [regex]::Escape([string]$sep)
    $glob = $Glob.Trim().Replace([char]47, $sep)
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.Append('^')
    for ($i = 0; $i -lt $glob.Length; $i++) {
        $ch = $glob[$i]
        if ($ch -eq '*') {
            if ($i + 1 -lt $glob.Length -and $glob[$i + 1] -eq '*') {
                [void]$sb.Append('.*')
                $i++
                if ($i + 1 -lt $glob.Length -and $glob[$i + 1] -eq $sep) { $i++ }
            }
            else { [void]$sb.Append('[^' + $sepRe + ']*') }
        }
        elseif ($ch -eq '?') { [void]$sb.Append('[^' + $sepRe + ']') }
        else { [void]$sb.Append([regex]::Escape([string]$ch)) }
    }
    [void]$sb.Append('$')
    return $sb.ToString()
}

# ---------- 4b. 新鲜度判据（D9/E1）：dll 是否 ≥ 其编译输入的最新时间 ----------
# 判据动机：本脚本此前只做 Test-Path 存在性检查 ⇒「改了源但 dll 未重建」仍报「全部就绪」（假绿），
# 下游以旧 dll 运行即 MissingMethodException（实测踩坑）。
# 编译输入 = 工程文件 + 工程目录树内 *.cs（排除 obj/bin/BuildOutput/_deprecated 与**其它工程根**子树，
# 后者由各自条目负责——否则「被 ProjectReference 排除的子工程」会给父工程伪造陈旧信号）。
$projectDirs = [System.Collections.Generic.List[string]]::new()
foreach ($f in $seen) { $projectDirs.Add((Split-Path -Parent $f)) }
$excludedDirNames = @('obj', 'bin', 'BuildOutput', '_deprecated', '.git', 'node_modules')

function Get-NewestSourceUtc([string]$projectFile) {
    $dir = Split-Path -Parent $projectFile
    $newest = (Get-Item $projectFile).LastWriteTimeUtc
    $newestFile = $projectFile
    $dirPrefix = $dir.TrimEnd('\') + '\'
    $nested = @($projectDirs | Where-Object {
        $_ -ne $dir -and $_.StartsWith($dirPrefix, [StringComparison]::OrdinalIgnoreCase) })

    # 本工程 <Compile Remove> 排除面（相对工程目录 → 锚定正则）：被摘掉的源不是本工程的编译输入。
    $compileRemoveRes = @()
    try {
        $projDoc = [xml](Get-Content $projectFile -Raw)
        foreach ($node in @($projDoc.Project.ItemGroup.Compile)) {
            if ($node -and $node.Remove) { $compileRemoveRes += (ConvertTo-CompileRemoveRegex ([string]$node.Remove)) }
        }
    } catch { }

    foreach ($file in (Get-ChildItem -Path $dir -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue)) {
        $full = $file.FullName
        $skip = $false
        foreach ($segment in $full.Split('\')) {
            if ($excludedDirNames -contains $segment) { $skip = $true; break }
        }
        if (-not $skip) {
            foreach ($n in $nested) {
                if ($full.StartsWith($n.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { $skip = $true; break }
            }
        }
        if (-not $skip -and $compileRemoveRes.Count -gt 0) {
            $relSource = $full.Substring($dirPrefix.Length)
            foreach ($rx in $compileRemoveRes) {
                if ($relSource -match $rx) { $skip = $true; break }
            }
        }
        if ($skip) { continue }

        if ($file.LastWriteTimeUtc -gt $newest) { $newest = $file.LastWriteTimeUtc; $newestFile = $full }
    }

    return [pscustomobject]@{ newestUtc = $newest; newestFile = $newestFile }
}

$entries = [System.Collections.Generic.List[object]]::new()
$missing = [System.Collections.Generic.List[string]]::new()
$stale = [System.Collections.Generic.List[object]]::new()
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

    $owner = Get-OwnerRoot $f
    # 越根工程的 rel 不可用 Substring（会错位）⇒ 用 GetRelativePath 口径；同根工程结果与旧实现逐字一致。
    $rel = if ($f.StartsWith($owner.dir.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        $f.Substring($owner.dir.Length).TrimStart('\')
    } else {
        Get-RelativePathCompat $owner.dir $f
    }
    $dllPath = Join-Path $owner.outDir $dll
    $exists = Test-Path $dllPath
    if (-not $exists) { $missing.Add($dll) }

    $fresh = $false
    $dllUtc = $null
    $newestSourceUtc = $null
    $newestSourceFile = $null
    $dllUtcText = $null
    $newestSourceUtcText = $null
    if ($exists) {
        $dllUtc = (Get-Item $dllPath).LastWriteTimeUtc
        $src = Get-NewestSourceUtc $f
        $newestSourceUtc = $src.newestUtc
        $newestSourceFile = $src.newestFile
        $dllUtcText = $dllUtc.ToString('o')
        $newestSourceUtcText = $newestSourceUtc.ToString('o')
        $fresh = $dllUtc -ge $newestSourceUtc
        if (-not $fresh) {
            $stale.Add([pscustomobject]@{ dll = $dll; root = $owner.label; project = $rel; newestSource = $newestSourceFile })
        }
    }

    $entries.Add([pscustomobject]@{
        name = $asm; dll = $dll; root = $owner.label; project = $rel
        exists = $exists; fresh = $fresh
        dllUtc = $dllUtcText
        newestSourceUtc = $newestSourceUtcText
        newestSourceFile = $newestSourceFile
    })
}

# ---------- 5. 落 manifest + stamp ----------
# 判据语义（D9/E1）：stamp 只在「全在 ∧ 全新鲜」时写 → 存在缺项或陈旧项即触发补烘；
# 逃生开关 -SkipFreshnessCheck 仅豁免新鲜度（存在性始终生效）。
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
$manifest = [pscustomobject]@{
    generatedUtc = [DateTime]::UtcNow.ToString('o')
    configuration = $Configuration
    roots = @($bakeRoots | ForEach-Object { [pscustomobject]@{ label = $_.label; dir = $_.dir; outDir = $_.outDir } })
    count = $entries.Count
    missingCount = $missing.Count
    staleCount = if ($SkipFreshnessCheck) { 0 } else { $stale.Count }
    entries = $entries
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding utf8

$freshnessOk = $SkipFreshnessCheck -or $stale.Count -eq 0
if ($missing.Count -eq 0 -and $freshnessOk) {
    Set-Content -Path $stampPath -Value ("complete " + [DateTime]::UtcNow.ToString('o')) -Encoding ascii
    Write-Host "[CsharpBakeManifest] OK: $($entries.Count) 程序集全部就绪且新鲜 → stamp 已写" -ForegroundColor Green
} else {
    if (Test-Path $stampPath) { Remove-Item $stampPath -Force }

    if ($missing.Count -gt 0) {
        Write-Host "[CsharpBakeManifest] 缺 $($missing.Count) 个 dll（未写 stamp，将触发补烘）：" -ForegroundColor Yellow
        $missing | Sort-Object | ForEach-Object { Write-Host "   - $_" -ForegroundColor Yellow }
    }

    if (-not $freshnessOk) {
        Write-Host "[CsharpBakeManifest] 陈旧 $($stale.Count) 个 dll（源比产物新，未写 stamp，将触发补烘）：" -ForegroundColor Yellow
        $stale | Sort-Object dll | ForEach-Object {
            Write-Host "   - $($_.dll) [$($_.root)] ← 最新源 $($_.newestSource)" -ForegroundColor Yellow
        }
        Write-Host "     （若确属「产物无需重建」的来源，用 -SkipFreshnessCheck 豁免并在此登记理由）" -ForegroundColor DarkYellow
    }
}
