<#
.SYNOPSIS
    CsharpPrj 项目引用校验器 — 悬空引用 / 未属性化 / 层序违规 / 循环依赖，并支持批量转换与清单同步

.DESCRIPTION
    以 modules.json（模块目录单一真源）为基准，对 Frame/CsharpPrj 下全部 csproj 的
    <ProjectReference> 执行四类检查：

      1 悬空引用     Include 解析后指向不存在的文件
      2 未属性化     仍为相对路径字面量（应改为 $(Proj_<项目名>)）
      3 层序违规     违反 modules.json roots.layer（硬规则：Share 不得引用 Strategy/Operation/Frame 外；
                     其余向上引用列为告警）
      4 循环依赖     项目间 A→B ∧ B→A

    并输出反向引用报告（谁引用了 X）。附加能力：

      -Convert       把相对路径字面量机械改写为 $(Proj_<项目名>)
      -SyncManifest  从磁盘重算 modules.json 的 projects 节点（幂等）

.PARAMETER Check
    执行四类检查（默认行为，无参数时亦执行）

.PARAMETER Convert
    把可解析的相对路径引用改写为 $(Proj_<项目名>)，配合 -WhatIf 预览

.PARAMETER SyncManifest
    从磁盘重新扫描并写回 modules.json 的 projects 节点（保留 roots 及其 layer）

.PARAMETER WhatIf
    仅预览，不写盘（-Convert / -SyncManifest 生效）

.PARAMETER Strict
    把层序告警也视为失败（退出码 1）

.EXAMPLE
    .\Test-ProjectReferences.ps1

.EXAMPLE
    .\Test-ProjectReferences.ps1 -Convert -WhatIf

.EXAMPLE
    .\Test-ProjectReferences.ps1 -SyncManifest
#>

[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$Convert,
    [switch]$SyncManifest,
    [switch]$WhatIf,
    [switch]$Strict,
    [string]$ManifestFile = ""
)

$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "ModulePaths.Common.psm1") -Force -ErrorAction Stop

$csharpPrj = Get-CsharpPrjRoot
if (-not $ManifestFile) { $ManifestFile = Get-ModuleManifestPath }

if (-not (Test-Path $ManifestFile)) {
    Write-Host "  [错误] 找不到模块清单: $ManifestFile" -ForegroundColor Red
    exit 1
}

$manifest = Read-ModuleManifest -Path $ManifestFile
$pathMap = Get-ModulePathMap -CsharpPrjRoot $csharpPrj -Manifest $manifest

$refRegex = [regex]'<ProjectReference\s+Include="([^"]+)"'

# 已知例外（modules.json exceptions.references）：仅抑制"层序违规"，不抑制悬空/未属性化
$exceptionSet = @{}
if (($manifest.PSObject.Properties.Name -contains 'exceptions') -and $manifest.exceptions.references) {
    foreach ($e in $manifest.exceptions.references) {
        $exceptionSet["$($e.from)|$($e.to)"] = $e.reason
    }
}

# ============================================================
# 辅助函数
# ============================================================

$rootList = @()
foreach ($r in $manifest.roots.PSObject.Properties) {
    $rootList += [PSCustomObject]@{
        Name  = $r.Name
        Path  = [System.IO.Path]::GetFullPath((Join-Path $csharpPrj $r.Value.path))
        Layer = [int]$r.Value.layer
    }
}
# 长路径优先，避免 "Plate" 误匹配更深的同名前缀
$rootList = $rootList | Sort-Object { $_.Path.Length } -Descending

# 旧式根别名（保留兼容；新引用统一用 $(Proj_<项目名>)）
# 注意：值必须以分隔符结尾，否则与 rest 拼接会粘连出 "CsharpPrj..\..\" 这类无效路径
$legacyAliases = @{
    'CsharpPrjDir'    = "$csharpPrj\"
    'BasicMethodDir'  = (Join-Path $csharpPrj 'BasicMethod\')
    'BasicMethodRoot' = (Join-Path $csharpPrj 'BasicMethod\Map\')
    'PlateDir'        = (Join-Path $csharpPrj 'Plate\')
    'ShareDir'        = (Join-Path $csharpPrj 'Share\')
    'ProtocolDir'     = (Join-Path $csharpPrj 'Protocol\')
}

# Proj_<Safe>（小写）→ 绝对 csproj 路径
$propMap = @{}
foreach ($kv in $pathMap.GetEnumerator()) {
    $propMap[('Proj_' + $kv.Value.Safe).ToLowerInvariant()] = $kv.Value.Path
}

# 把 Include 值解析为绝对 csproj 路径；未知属性形式返回 $null（跳过静态判定）
function Resolve-IncludeValue {
    param([Parameter(Mandatory)][string]$Value, [Parameter(Mandatory)][string]$BaseDir)
    $v = $Value.Trim()
    if ($v.StartsWith('$(')) {
        $m = [regex]::Match($v, '^\$\(([^)]+)\)(.*)$')
        if (-not $m.Success) { return $null }
        $propName = $m.Groups[1].Value
        $rest = $m.Groups[2].Value
        $key = $propName.ToLowerInvariant()
        if ($propMap.ContainsKey($key)) { return $propMap[$key] }
        if ($legacyAliases.ContainsKey($propName)) {
            $combined = ($legacyAliases[$propName] + ($rest -replace '^[\\/]+', '')) -replace '/', '\'
            return [System.IO.Path]::GetFullPath($combined)
        }
        return $null
    }
    return [System.IO.Path]::GetFullPath((Join-Path $BaseDir ($v -replace '/', '\')))
}

function Get-LayerInfo {
    param([Parameter(Mandatory)][string]$AbsPath)
    foreach ($r in $rootList) {
        if ($AbsPath.StartsWith($r.Path, [System.StringComparison]::OrdinalIgnoreCase)) { return $r }
    }
    return $null
}

function Get-ProjectReferences {
    param([Parameter(Mandatory)][System.IO.FileInfo]$ProjectFile)
    $text = Get-Content $ProjectFile.FullName -Raw -Encoding UTF8

    # 注释区间 <!-- ... -->：内含的引用视为历史禁用项，不参与检查与转换
    $commentSpans = @()
    foreach ($cm in [regex]::Matches($text, '<!--.*?-->', [System.Text.RegularExpressions.RegexOptions]::Singleline)) {
        $commentSpans += [PSCustomObject]@{ Start = $cm.Index; End = $cm.Index + $cm.Length }
    }

    $result = @()
    foreach ($m in $refRegex.Matches($text)) {
        $inComment = $false
        foreach ($s in $commentSpans) {
            if ($m.Index -ge $s.Start -and $m.Index -lt $s.End) { $inComment = $true; break }
        }
        if ($inComment) { continue }

        $raw = $m.Groups[1].Value
        $result += [PSCustomObject]@{
            Raw        = $raw
            IsProperty = $raw.TrimStart().StartsWith('$(')
            Abs        = (Resolve-IncludeValue -Value $raw -BaseDir $ProjectFile.DirectoryName)
            ValueStart = $m.Groups[1].Index
            ValueLen   = $m.Groups[1].Length
        }
    }
    return $result
}

# 从根目录（Frame/CsharpPrj）起的相对显示路径
function Get-ShortPath {
    param([Parameter(Mandatory)][string]$AbsPath, [Parameter(Mandatory)][string]$Base)
    if ($AbsPath.StartsWith($Base, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $AbsPath.Substring($Base.Length).TrimStart('\')
    }
    return $AbsPath
}

function Test-FileHasBom {
    param([Parameter(Mandatory)][string]$Path)
    $fs = [System.IO.File]::OpenRead($Path)
    try {
        $b = New-Object byte[] 3
        $n = $fs.Read($b, 0, 3)
        return ($n -eq 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)
    } finally { $fs.Dispose() }
}

function Write-TextPreservingBom {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Text)
    $hasBom = Test-FileHasBom -Path $Path
    $utf8 = New-Object System.Text.UTF8Encoding $hasBom
    [System.IO.File]::WriteAllText($Path, $Text, $utf8)
}

# ============================================================
# -SyncManifest：从磁盘重算 modules.json 的 projects 节点
# ============================================================

if ($SyncManifest) {
    Write-Host ""
    Write-Host "[SyncManifest] 从磁盘重建 modules.json projects 节点" -ForegroundColor Cyan

    $projects = [ordered]@{}
    $dupes = @()
    foreach ($p in Get-EnumeratedProjects -CsharpPrjRoot $csharpPrj) {
        $rel = $p.FullName.Substring($csharpPrj.Length).TrimStart('\')
        if ($projects.Contains($p.BaseName)) { $dupes += $p.BaseName }
        $projects[$p.BaseName] = $rel
    }
    if ($dupes.Count -gt 0) {
        Write-Host "  [错误] 项目名重复（属性名将冲突）: $($dupes -join ', ')" -ForegroundColor Red
        exit 1
    }

    $roots = [ordered]@{}
    foreach ($r in $manifest.roots.PSObject.Properties) {
        $roots[$r.Name] = [ordered]@{ path = $r.Value.path; layer = [int]$r.Value.layer }
    }

    $out = [ordered]@{
        '$schema' = 'module-paths/v1'
        comment   = $manifest.comment
        roots     = $roots
    }
    # 例外清单需原样保留（由人维护，不由磁盘推导）
    if ($manifest.PSObject.Properties.Name -contains 'exceptions') {
        $out['exceptions'] = $manifest.exceptions
    }
    $out['projects'] = $projects
    $json = $out | ConvertTo-Json -Depth 6

    if ($WhatIf) {
        Write-Host "  [预览] 将写入 $ManifestFile（$($projects.Count) 个项目）" -ForegroundColor Yellow
    } else {
        $utf8NoBom = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText($ManifestFile, $json, $utf8NoBom)
        Write-Host "  [成功] 已同步 $($projects.Count) 个项目 → $ManifestFile" -ForegroundColor Green
    }
    Write-Host ""
    if (-not $Check -and -not $Convert) { exit 0 }
}

# ============================================================
# -Convert：相对路径字面量 → $(Proj_<项目名>)
# ============================================================

if ($Convert) {
    Write-Host ""
    Write-Host "[Convert] 机械改写相对路径引用为属性形式" -ForegroundColor Cyan

    $filesChanged = 0
    $convertedCount = 0
    $unresolved = @()

    foreach ($proj in Get-EnumeratedProjects -CsharpPrjRoot $csharpPrj) {
        $refs = @(Get-ProjectReferences -ProjectFile $proj | Where-Object { -not $_.IsProperty })
        if ($refs.Count -eq 0) { continue }

        $text = Get-Content $proj.FullName -Raw -Encoding UTF8
        $newText = $text
        $fileConverted = 0

        # 从后向前替换，避免索引位移
        foreach ($t in ($refs | Sort-Object { $_.ValueStart } -Descending)) {
            $key = $t.Abs.ToLowerInvariant()
            if ($pathMap.ContainsKey($key)) {
                $replacement = '$(Proj_' + $pathMap[$key].Safe + ')'
                $newText = $newText.Remove($t.ValueStart, $t.ValueLen).Insert($t.ValueStart, $replacement)
                $fileConverted++
            } else {
                $unresolved += "$($proj.BaseName): $($t.Raw)"
            }
        }

        if ($fileConverted -gt 0) {
            $filesChanged++
            $convertedCount += $fileConverted
            if (-not $WhatIf) {
                Write-TextPreservingBom -Path $proj.FullName -Text $newText
            }
        }
    }

    if ($WhatIf) {
        Write-Host "  [预览] 将改写 $filesChanged 个 csproj、$convertedCount 条引用（未写盘）" -ForegroundColor Yellow
    } else {
        Write-Host "  [成功] 已改写 $filesChanged 个 csproj、$convertedCount 条引用" -ForegroundColor Green
    }

    if ($unresolved.Count -gt 0) {
        Write-Host "  [跳过] $($unresolved.Count) 条无法映射到 modules.json（保持原样）:" -ForegroundColor Yellow
        $unresolved | Sort-Object -Unique | ForEach-Object { Write-Host "      $_" -ForegroundColor Yellow }
    }
    Write-Host ""

    if (-not $Check) { exit 0 }
}

# ============================================================
# 检查 1-4：悬空 / 未属性化 / 层序 / 循环依赖
# ============================================================

Write-Host ""
Write-Host "[Check] CsharpPrj 项目引用校验" -ForegroundColor Cyan
Write-Host "  清单: $ManifestFile" -ForegroundColor Gray

$allProjects = Get-EnumeratedProjects -CsharpPrjRoot $csharpPrj
Write-Host "  扫描 $($allProjects.Count) 个 csproj" -ForegroundColor Gray

$dangling = @()
$nonProperty = @()
$layerHard = @()
$layerWarn = @()
$excepted = @()
$unresolvable = @()
$totalRefs = 0
$graph = @{}
$reverseRef = @{}

foreach ($proj in $allProjects) {
    $me = $proj.BaseName
    if (-not $graph.ContainsKey($me)) { $graph[$me] = @() }
    $meLayer = Get-LayerInfo -AbsPath $proj.FullName

    foreach ($r in Get-ProjectReferences -ProjectFile $proj) {
        $totalRefs++
        if (-not $r.IsProperty) {
            if (-not $exceptionSet.ContainsKey("$me|$($r.Raw)")) { $nonProperty += "$me -> $($r.Raw)" }
        }

        if ([string]::IsNullOrEmpty($r.Abs)) {
            if ($r.IsProperty) { $unresolvable += "$me -> $($r.Raw)" }
            continue
        }
        if (-not (Test-Path $r.Abs)) {
            $dangling += [PSCustomObject]@{ From = $me; Raw = $r.Raw }
            continue
        }

        $target = [System.IO.Path]::GetFileNameWithoutExtension($r.Abs)
        if (-not $reverseRef.ContainsKey($target)) {
            $reverseRef[$target] = New-Object System.Collections.Generic.HashSet[string]
        }
        [void]$reverseRef[$target].Add($me)

        $targetLayer = Get-LayerInfo -AbsPath $r.Abs
        $rel = Get-ShortPath -AbsPath $r.Abs -Base $csharpPrj
        $exKey = "$me|$($r.Raw)"

        # 目标不在 CsharpPrj 七个根之内 = 跨出 Frame
        if ($targetLayer -eq $null) {
            if ($meLayer -ne $null -and $meLayer.Name -eq 'Share') {
                if ($exceptionSet.ContainsKey($exKey)) { $excepted += "$me -> $($r.Raw)" }
                else { $layerHard += "$me (Share) -> [Frame 外] $($r.Abs)" }
            } else {
                $layerWarn += "$me -> [Frame 外] $($r.Abs)"
            }
            continue
        }

        $graph[$me] += $target

        if ($meLayer -eq $null) { continue }

        if ($meLayer.Name -eq 'Share' -and ($targetLayer.Name -eq 'Strategy' -or $targetLayer.Name -eq 'Operation')) {
            if ($exceptionSet.ContainsKey($exKey)) { $excepted += "$me -> $($r.Raw)" }
            else { $layerHard += "$me (Share) -> $rel" }
        }
        elseif ($targetLayer.Layer -gt $meLayer.Layer) {
            $layerWarn += "$me ($($meLayer.Name),L$($meLayer.Layer)) -> $($targetLayer.Name)(L$($targetLayer.Layer))"
        }
    }
}

# --- 孤儿项目判定：从伞工程（根目录直属项目 / Protocol 全部）BFS 可达性 ---
$umbrellas = @()
foreach ($proj in $allProjects) {
    $root = Get-LayerInfo -AbsPath $proj.FullName
    if ($root -eq $null) { continue }
    if ((Split-Path $proj.FullName -Parent) -eq $root.Path.TrimEnd('\')) { $umbrellas += $proj.BaseName }
    elseif ($root.Name -eq 'Protocol') { $umbrellas += $proj.BaseName }
}

$reachable = New-Object System.Collections.Generic.HashSet[string]
$queue = New-Object System.Collections.Generic.Queue[string]
foreach ($u in $umbrellas) {
    if ($reachable.Add($u)) { $queue.Enqueue($u) }
}
while ($queue.Count -gt 0) {
    $cur = $queue.Dequeue()
    if (-not $graph.ContainsKey($cur)) { continue }
    foreach ($nxt in @($graph[$cur])) {
        if ($graph.ContainsKey($nxt) -and $reachable.Add($nxt)) { $queue.Enqueue($nxt) }
    }
}
$umbrellaSet = New-Object System.Collections.Generic.HashSet[string]
foreach ($u in $umbrellas) { [void]$umbrellaSet.Add($u) }
$orphanSet = New-Object System.Collections.Generic.HashSet[string]
foreach ($proj in $allProjects) {
    if (-not $reachable.Contains($proj.BaseName)) { [void]$orphanSet.Add($proj.BaseName) }
}

$danglingReachable = @($dangling | Where-Object { -not $orphanSet.Contains($_.From) })
$danglingOrphan = @($dangling | Where-Object { $orphanSet.Contains($_.From) })

# 非属性化引用：孤儿项目内的计入信息项（不影响构建），其余计入失败项
$nonPropertyFail = @($nonProperty | Where-Object { -not $orphanSet.Contains(($_ -split ' -> ', 2)[0]) })
$nonPropertyInfo = @($nonProperty | Where-Object { $orphanSet.Contains(($_ -split ' -> ', 2)[0]) })

# --- 检查 4：循环依赖（DFS 回边检测，仅限 CsharpPrj 内部）---
$script:color = @{}
$script:path = New-Object System.Collections.Generic.List[string]
$script:cycles = New-Object System.Collections.Generic.List[string]
$script:cycleSet = New-Object System.Collections.Generic.HashSet[string]
foreach ($k in $graph.Keys) { $script:color[$k] = 0 }

function Invoke-CycleDfs {
    param([string]$Node)
    $script:color[$Node] = 1
    $script:path.Add($Node)
    foreach ($nxt in @($graph[$Node])) {
        if (-not $graph.ContainsKey($nxt)) { continue }
        if (-not $script:color.ContainsKey($nxt)) { $script:color[$nxt] = 0 }
        if ($script:color[$nxt] -eq 1) {
            $idx = $script:path.IndexOf($nxt)
            if ($idx -ge 0) {
                $nodes = @($script:path.GetRange($idx, $script:path.Count - $idx))
                $key = (($nodes | Sort-Object) -join '|')
                if (-not $script:cycleSet.Contains($key)) {
                    [void]$script:cycleSet.Add($key)
                    $script:cycles.Add((($nodes + $nxt) -join ' -> '))
                }
            }
        } elseif ($script:color[$nxt] -eq 0) {
            Invoke-CycleDfs -Node $nxt
        }
    }
    $script:path.RemoveAt($script:path.Count - 1)
    $script:color[$Node] = 2
}

foreach ($k in @($graph.Keys)) {
    if (-not $script:color.ContainsKey($k)) { $script:color[$k] = 0 }
    if ($script:color[$k] -eq 0) { Invoke-CycleDfs -Node $k }
}

# ============================================================
# 结果输出
# ============================================================

Write-Host ""

Write-Host "--- [1/4] 悬空引用 ---" -ForegroundColor White
if ($danglingReachable.Count -eq 0 -and $danglingOrphan.Count -eq 0) {
    Write-Host "  [通过] 全部引用指向存在的文件" -ForegroundColor Green
} else {
    if ($danglingReachable.Count -gt 0) {
        Write-Host "  [失败] $($danglingReachable.Count) 条悬空引用（可达项目）:" -ForegroundColor Red
        $danglingReachable | Sort-Object { $_.From } | ForEach-Object { Write-Host "      $($_.From) -> $($_.Raw)" -ForegroundColor Red }
    }
    if ($danglingOrphan.Count -gt 0) {
        Write-Host "  [孤儿] $($danglingOrphan.Count) 条悬空引用位于不可达项目（不影响伞构建；建议按 RedundancyElimination 复核）:" -ForegroundColor DarkYellow
        $danglingOrphan | Sort-Object { $_.From } | ForEach-Object { Write-Host "      $($_.From) -> $($_.Raw)" -ForegroundColor DarkYellow }
    }
}

Write-Host "--- [2/4] 未属性化引用（相对路径字面量）---" -ForegroundColor White
if ($nonPropertyFail.Count -eq 0) {
    Write-Host "  [通过] 全部 $totalRefs 条引用已属性化（或属已登记例外）" -ForegroundColor Green
} else {
    Write-Host "  [待迁移] $($nonPropertyFail.Count)/$totalRefs 条仍为相对路径（可运行 -Convert 批量改写）" -ForegroundColor Yellow
    $nonPropertyFail | Sort-Object -Unique | Select-Object -First 20 | ForEach-Object { Write-Host "      $_" -ForegroundColor Yellow }
    if ($nonPropertyFail.Count -gt 20) { Write-Host "      ...（其余 $($nonPropertyFail.Count - 20) 条略）" -ForegroundColor DarkGray }
}
if ($nonPropertyInfo.Count -gt 0) {
    Write-Host "  [孤儿] $($nonPropertyInfo.Count) 条位于不可达项目（随该项目一并处置）" -ForegroundColor DarkGray
}
if ($unresolvable.Count -gt 0) {
    Write-Host "  [提示] $($unresolvable.Count) 条属性引用无法静态解析（已跳过层序/环判定）" -ForegroundColor DarkGray
    $unresolvable | Sort-Object -Unique | Select-Object -First 10 | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkGray }
}

Write-Host "--- [3/4] 层序违规 ---" -ForegroundColor White
if ($layerHard.Count -eq 0 -and $layerWarn.Count -eq 0 -and $excepted.Count -eq 0) {
    Write-Host "  [通过] 无层序违规" -ForegroundColor Green
} else {
    if ($layerHard.Count -gt 0) {
        Write-Host "  [失败] $($layerHard.Count) 条硬规则违规（Share 不得引用 Strategy/Operation/Frame 外）:" -ForegroundColor Red
        $layerHard | Sort-Object -Unique | ForEach-Object { Write-Host "      $_" -ForegroundColor Red }
    }
    if ($excepted.Count -gt 0) {
        Write-Host "  [例外] $($excepted.Count) 条已登记例外（modules.json exceptions.references）:" -ForegroundColor DarkGray
        $excepted | Sort-Object -Unique | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkGray }
    }
    if ($layerWarn.Count -gt 0) {
        Write-Host "  [告警] $($layerWarn.Count) 条向上引用（现状允许，不阻断）:" -ForegroundColor Yellow
        $layerWarn | Sort-Object -Unique | ForEach-Object { Write-Host "      $_" -ForegroundColor Yellow }
    }
}

Write-Host "--- [4/4] 循环依赖 ---" -ForegroundColor White
if ($script:cycles.Count -eq 0) {
    Write-Host "  [通过] 无循环依赖" -ForegroundColor Green
} else {
    Write-Host "  [失败] $($script:cycles.Count) 个循环:" -ForegroundColor Red
    $script:cycles | ForEach-Object { Write-Host "      $_" -ForegroundColor Red }
}

# --- 反向引用报告（被引用最多的模块，供变更影响分析）---
Write-Host ""
Write-Host "--- 反向引用报告（Top 15 被引用模块）---" -ForegroundColor White
$topReferenced = $reverseRef.GetEnumerator() |
    Sort-Object { $_.Value.Count } -Descending |
    Select-Object -First 15
foreach ($e in $topReferenced) {
    Write-Host ("      {0,-42} <- {1} 处引用" -f $e.Key, $e.Value.Count) -ForegroundColor Gray
}

# ============================================================
# 汇总
# ============================================================

Write-Host ""
$fail = ($danglingReachable.Count -gt 0) -or ($nonPropertyFail.Count -gt 0) -or ($layerHard.Count -gt 0) -or ($script:cycles.Count -gt 0)
if ($Strict -and $layerWarn.Count -gt 0) { $fail = $true }

if ($orphanSet.Count -gt 0) {
    Write-Host "提示: $($orphanSet.Count) 个不可达项目（不被伞工程引用；可能为独立工具或冗余项）: $((@($orphanSet) | Sort-Object) -join ', ')" -ForegroundColor DarkYellow
}

if ($fail) {
    Write-Host "结论: [失败] 悬空 $($danglingReachable.Count) / 未属性化 $($nonPropertyFail.Count) / 硬违规 $($layerHard.Count) / 循环 $($script:cycles.Count)" -ForegroundColor Red
    exit 1
}

Write-Host "结论: [通过] 引用体系健康（引用 $totalRefs 条，告警 $($layerWarn.Count) 条，例外 $($excepted.Count) 条，孤儿 $($orphanSet.Count) 个）" -ForegroundColor Green
exit 0
