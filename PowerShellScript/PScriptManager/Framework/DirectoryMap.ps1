#requires -Version 5.1
<#
.SYNOPSIS
    DirectoryMap —— 仓库目录映射基础框架（纯原语，无判据语义）。

.DESCRIPTION
    把「仓库根解析 / 约定目录 / 门禁 Domain→目录 / 按名发现脚本」收敛到**唯一单源**
    = Framework\directory-map.json（禁在消费脚本内联硬编码落点）。

    设计对齐既有约定：
      · 仓库**超根** = 同时含 Frame\ 与 Projects\ 的目录（Frame/Projects 均为 git 子模块，
        各带 .git ⇒ 不可用 `.git` 哨兵上溯，会命中子模块根）。
      · Frame 子模块根另供给 Get-FrameRoot（PScriptManager/Baselines 等 Frame 侧锚点）。
      · 与深度无关：从任意子目录上溯，结果一致。
      · 门禁 Domain → **候选目录数组**（Node/Spec 跨目录：Node 落在 Modules\Node ∪ DSL\Node，
        Spec 落在 DSL\Spec ∪ CentralTest\Tools\Gate）。

    形态（与 FrameworkInvoke.psm1 / FrameworkRequires.ps1 同范式）：
      · DirectoryMap.ps1   —— dot-source 入口（函数落在调用方作用域）；
      · DirectoryMap.psm1  —— 模块版（Import-Module，点源本文件后导出）。

.NOTES
    用法（消费脚本）：
        . (Join-Path $PSScriptRoot '..\PScriptManager\Framework\DirectoryMap.ps1')
        $root = Get-RepositoryRoot
        $dir  = Get-MappedPath -Key 'Baselines'
#>

$script:DM_MapPath   = Join-Path $PSScriptRoot 'directory-map.json'
$script:DM_SuperRoot = ''
$script:DM_FrameRoot = ''
$script:DM_Map       = $null

<#
.SYNOPSIS
    解析仓库**超根**（同时含 Frame\ 与 Projects\ 的目录 = 子模块 Frame/Projects 的父仓）。
.DESCRIPTION
    ⚠ 本仓 Frame\ 与 Projects\ 均为 git 子模块（各带自己的 .git），故 `.git` 哨兵上溯
    **不可**用于定位超根（会命中最内层子模块根）。超根恒以「同时含 Frame\ 与 Projects\」
    的 marker 上溯判定（取最外层命中），与门禁脚本既有口径一致（D1 收敛）。
    与深度无关，进程内缓存。
.PARAMETER StartPath
    起点目录（缺省 = 本文件所在目录）。
#>
function Get-SuperRoot {
    param([string]$StartPath)

    if ($script:DM_SuperRoot) { return $script:DM_SuperRoot }

    $cand = if ($StartPath) { $StartPath } else { $PSScriptRoot }
    $hit = ''
    while ($cand) {
        if ((Test-Path -LiteralPath (Join-Path $cand 'Frame')) -and (Test-Path -LiteralPath (Join-Path $cand 'Projects'))) { $hit = $cand }
        $parent = Split-Path -Parent $cand
        if (-not $parent -or $parent -eq $cand) { break }
        $cand = $parent
    }
    if (-not $hit) { throw '找不到仓库超根（未命中同时含 Frame\ 与 Projects\ 的目录）' }
    $script:DM_SuperRoot = $hit
    return $hit
}

<#
.SYNOPSIS
    仓库超根别名（门禁脚本的「仓库根」= 超根）。见 Get-SuperRoot。
#>
function Get-RepositoryRoot {
    param([string]$StartPath)
    return Get-SuperRoot -StartPath $StartPath
}

<#
.SYNOPSIS
    Frame 子模块根（= 超根\Frame；PScriptManager / Baselines 等 Frame 侧框架的锚点）。
#>
function Get-FrameRoot {
    if ($script:DM_FrameRoot) { return $script:DM_FrameRoot }
    $map = Get-DirectoryMap
    $rel = 'Frame'
    if ($map.paths.PSObject.Properties['Frame']) { $rel = [string]$map.paths.Frame }
    $script:DM_FrameRoot = Join-Path (Get-SuperRoot) ($rel.Replace('/', '\'))
    return $script:DM_FrameRoot
}

<#
.SYNOPSIS
    读取并缓存目录映射数据单源（Framework\directory-map.json）。
#>
function Get-DirectoryMap {
    if ($script:DM_Map) { return $script:DM_Map }
    if (-not (Test-Path -LiteralPath $script:DM_MapPath)) {
        throw "目录映射数据缺失：$script:DM_MapPath"
    }
    $script:DM_Map = Get-Content -LiteralPath $script:DM_MapPath -Raw -Encoding UTF8 | ConvertFrom-Json
    return $script:DM_Map
}

<#
.SYNOPSIS
    逻辑键 → 绝对路径（缺键即抛，禁静默回空）。
#>
function Get-MappedPath {
    param([Parameter(Mandatory = $true)][string]$Key)
    $map = Get-DirectoryMap
    $rel = $map.paths.PSObject.Properties[$Key]
    if (-not $rel) { throw "目录映射无此键：$Key（见 directory-map.json 的 paths）" }
    return (Join-Path (Get-RepositoryRoot) ([string]$rel.Value).Replace('/', '\'))
}

<#
.SYNOPSIS
    Domain → 候选目录绝对路径数组（仅返回存在者；缺域即抛）。
#>
function Get-DomainPaths {
    param([Parameter(Mandatory = $true)][string]$Domain)
    $map = Get-DirectoryMap
    $rel = $map.gateDomains.PSObject.Properties[$Domain]
    if (-not $rel) { throw "目录映射无此 Domain：$Domain（见 directory-map.json 的 gateDomains）" }
    $root = Get-RepositoryRoot
    $out = [System.Collections.Generic.List[string]]::new()
    foreach ($r in @($rel.Value)) {
        $abs = Join-Path $root ([string]$r).Replace('/', '\')
        if ((Test-Path -LiteralPath $abs) -and -not $out.Contains($abs)) { $out.Add($abs) }
    }
    if ($out.Count -eq 0) { throw "Domain '$Domain' 的候选目录均不存在：$(@($rel.Value) -join ' / ')" }
    return $out.ToArray()
}


<#
.SYNOPSIS
    全部门禁 Domain 名（gateDomains 的键，升序）——供扫描面对账等遍历使用。
#>
function Get-GateDomains {
    $map = Get-DirectoryMap
    return @($map.gateDomains.PSObject.Properties.Name | Sort-Object)
}

<#
.SYNOPSIS
    Domain 的**首选**目录（候选数组首个存在者）。
#>
function Get-DomainPath {
    param([Parameter(Mandatory = $true)][string]$Domain)
    return @(Get-DomainPaths -Domain $Domain)[0]
}

<#
.SYNOPSIS
    查询门名 → 脚本文件名的例外（无例外返回空串）。
#>
function Get-GateScriptOverride {
    param([Parameter(Mandatory = $true)][string]$Gate)
    $map = Get-DirectoryMap
    $o = $map.scriptNameOverrides.PSObject.Properties[$Gate]
    if (-not $o) { return '' }
    return [string]$o.Value
}

<#
.SYNOPSIS
    门名 → 脚本叶子名（默认 Check-<Gate>.ps1，例外查 scriptNameOverrides）。
#>
function Get-GateScriptLeafName {
    param([Parameter(Mandatory = $true)][string]$Gate)
    $o = Get-GateScriptOverride -Gate $Gate
    if ($o) { return $o }
    return "Check-$Gate.ps1"
}

<#
.SYNOPSIS
    按 Domain（+ 名称例外）解析门脚本落点的绝对路径。
.DESCRIPTION
    在 Domain 的候选目录内按叶子名发现；命中 > 1 即抛（歧义即红）。
    -AllowFallback 时，候选目录内未命中则在全 DSL\ 递归兜底。
.PARAMETER Gate
    门名（GateManifests.psd1 的键）。
.PARAMETER Domain
    登记 Domain（缺省 = 仅兜底 DSL\ 递归）。
.PARAMETER AllowFallback
    候选目录未命中时允许全 DSL\ 兜底发现。
#>
function Resolve-GateScript {
    param(
        [Parameter(Mandatory = $true)][string]$Gate,
        [string]$Domain = '',
        [switch]$AllowFallback
    )
    $leaf = Get-GateScriptLeafName -Gate $Gate
    $hits = [System.Collections.Generic.List[string]]::new()

    if ($Domain) {
        foreach ($dir in @(Get-DomainPaths -Domain $Domain)) {
            foreach ($f in (Get-ChildItem -LiteralPath $dir -Recurse -Filter $leaf -File -ErrorAction SilentlyContinue)) {
                if (-not $hits.Contains($f.FullName)) { $hits.Add($f.FullName) }
            }
            if ($hits.Count -gt 0) { break }
        }
    }

    if ($hits.Count -eq 0 -and ($AllowFallback -or -not $Domain)) {
        $dsl = Get-MappedPath -Key 'DSL'
        foreach ($f in (Get-ChildItem -LiteralPath $dsl -Recurse -Filter $leaf -File -ErrorAction SilentlyContinue)) {
            if (-not $hits.Contains($f.FullName)) { $hits.Add($f.FullName) }
        }
    }

    if ($hits.Count -eq 0) { throw "门脚本未找到：$leaf（Gate=$Gate / Domain=$Domain）" }
    if ($hits.Count -gt 1) { throw "门脚本歧义（重名 >1）：$leaf → $($hits -join ' / ')" }
    return $hits[0]
}

<#
.SYNOPSIS
    门脚本落点的**仓库根相对**串（'\' 分隔），供索引/对账共用。
#>
function Get-GateScriptCell {
    param(
        [Parameter(Mandatory = $true)][string]$Gate,
        [string]$Domain = '',
        [switch]$AllowFallback
    )
    try {
        $abs = Resolve-GateScript -Gate $Gate -Domain $Domain -AllowFallback:$AllowFallback
    }
    catch {
        # 脚本不存在时回退 spec（单一 runner 承载）；与 GateRuntime.Find-GateTarget 同口径。
        $spec = Join-Path (Get-MappedPath -Key 'CentralTestGate') ('Spec\' + $Gate + '.gate.psd1')
        if (-not (Test-Path -LiteralPath $spec)) { throw }
        $abs = $spec
    }
    $root = Get-RepositoryRoot
    return ($abs.Substring($root.Length + 1) -replace '/', '\')
}
