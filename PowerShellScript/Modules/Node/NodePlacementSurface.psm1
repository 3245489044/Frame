#requires -Version 5.1
<#
.SYNOPSIS
    Node 位置面共享解析（wire-id 归一 + 声明并集 + 派生面对照）—— 门禁只读对账与产品生成共用一份。

.DESCRIPTION
    动机（GD §5.1 G3）：`Check-DomainWireSingleSource.ps1` 与产品生成器
    `Operation\DSL\Node\New-NodePlacementDeclaration.ps1` 各持一份逐字相同的 `ConvertTo-WireId`，
    并各持一份「Config\nodes\*.ushar 的 [Domain] → placements → id=」并集采集。

    边界（不变）：
      · 本模块**只读**（解析/归并/返回集合），不写任何派生面；
      · 门禁 = 只读对账（调用 `Get-NodePlacementWireIdSet` / `Get-NodePlacementSurfaceSet`）；
      · 产品生成器 = 只写侧（调用同一解析结果后自行写 instances\nodes.json / placement.ushar）。

.NOTES
    用法：
        Import-Module (Join-Path $PSRoot 'Frame\PowerShellScript\Modules\Node\NodePlacementSurface.psm1') -DisableNameChecking
        $wire = @(Get-NodePlacementWireIdSet -Root $root)
        $surface = Get-NodePlacementSurfaceSet -Path $placementFile
#>

# 文本单源（缺件 → ''）：嵌套导入**不加 -Force**（避免卸载调用方已加载的 TextOps）。
Import-Module (Join-Path $PSScriptRoot '..\..\Operation\DSL\Meta\TextOps.psm1') -DisableNameChecking

<#
.SYNOPSIS
    wire-id 归一：`-` → `_`，全小写。
.DESCRIPTION
    与两份源实现逐字等价：`($Id -replace '-', '_').ToLowerInvariant()`。
.PARAMETER Id
    声明 id（如 'agent-core'）
.OUTPUTS
    [string] wire 形（如 'agent_core'）
#>
function Get-NodePlacementWireId {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [AllowEmptyString()]
        [string]$Id
    )
    return ($Id -replace '-', '_').ToLowerInvariant()
}

<#
.SYNOPSIS
    单个声明文件的 placements.id 并集（去重，保持声明序）。
.DESCRIPTION
    解析口径（与两份源逐字一致）：
      · `#` 行注释先剥离；
      · `[Section]` 行重置 placements 状态；
      · `placements` 行记录缩进，缩进回退即退出块；
      · 块内 `id = xxx` 收集（去重）。
.PARAMETER Path
    声明文件路径（优先）
.PARAMETER Text
    声明文本（未给 Path 时使用）
.OUTPUTS
    [string[]] 位置 id（可能为空；调用方用 @() 包裹）
#>
function Get-NodePlacementDeclaredId {
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)]
        [string]$Path = '',

        [AllowNull()]
        [AllowEmptyString()]
        [string]$Text = ''
    )

    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        if (-not (Test-Path -LiteralPath $Path)) { return }
        $Text = Read-TextFile -Path $Path
    }
    if ($null -eq $Text -or $Text.Length -eq 0) { return }

    $ids = [System.Collections.Generic.List[string]]::new()
    $inPlacements = $false
    $placementsIndent = -1
    foreach ($raw in ($Text -split "`r?`n")) {
        $line = (Remove-LineComment -Text $raw -Marker '#').TrimEnd()
        if ([string]::IsNullOrWhiteSpace($line)) { continue }

        $indent = $line.Length - $line.TrimStart().Length
        $trim = $line.Trim()
        if ($trim.StartsWith('[')) { $inPlacements = $false; continue }
        if ($trim -match '^placements$') { $inPlacements = $true; $placementsIndent = $indent; continue }
        if ($inPlacements -and $indent -le $placementsIndent) { $inPlacements = $false }
        if (-not $inPlacements) { continue }

        $m = [regex]::Match($trim, '^id\s*=\s*(?<id>[^\s|]+)')
        if ($m.Success) {
            $id = $m.Groups['id'].Value
            if (-not $ids.Contains($id)) { $ids.Add($id) }
        }
    }
    return [string[]]$ids.ToArray()
}

<#
.SYNOPSIS
    声明目录下全部 *.ushar 的 placements.id 并集（唯一真源）。
.PARAMETER Root
    声明目录（如 Config\nodes）
.OUTPUTS
    [string[]] 位置 id（保持文件名序 + 声明序；可能为空）
#>
function Get-NodePlacementUnion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$Root
    )

    if (-not (Test-Path -LiteralPath $Root)) { return }

    $union = [System.Collections.Generic.List[string]]::new()
    foreach ($file in (Get-ChildItem -LiteralPath $Root -Filter '*.ushar' -File -Recurse -ErrorAction SilentlyContinue | Sort-Object Name)) {
        foreach ($id in @(Get-NodePlacementDeclaredId -Path $file.FullName)) {
            if (-not $union.Contains([string]$id)) { $union.Add([string]$id) }
        }
    }
    return [string[]]$union.ToArray()
}

<#
.SYNOPSIS
    声明并集的 wire-id 集合（去重 + 升序）。
.PARAMETER Root
    声明目录（如 Config\nodes）
.PARAMETER Path
    单个声明文件（与 Root 二选一；用于定点核对）
.OUTPUTS
    [string[]] wire-id 升序集合（可能为空）
#>
function Get-NodePlacementWireIdSet {
    [CmdletBinding()]
    param(
        [string]$Root = '',
        [string]$Path = ''
    )

    $ids = @()
    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        $ids = @(Get-NodePlacementDeclaredId -Path $Path)
    }
    elseif (-not [string]::IsNullOrWhiteSpace($Root)) {
        $ids = @(Get-NodePlacementUnion -Root $Root)
    }
    if ($ids.Count -eq 0) { return }

    $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($id in $ids) { [void]$set.Add((Get-NodePlacementWireId -Id ([string]$id))) }
    return [string[]]@($set | Sort-Object)
}

<#
.SYNOPSIS
    placement.ushar 派生面：LocPlacement 位置行 + LocToPlacement 左值。
.DESCRIPTION
    段识别锚定节头（`[Matrix] LocToPlacement` 的 over=LocPlacement 含子串，禁用子串判据）。
    `agent_round` 是固定基础行，不参与对账（与 Check-DomainWireSingleSource 口径一致）。
.PARAMETER Path
    placement.ushar 路径
.PARAMETER ExcludeToken
    不参与对账的固定位置（默认 'agent_round'）
.OUTPUTS
    { Loc; Matrix } 两个升序字符串数组
#>
function Get-NodePlacementSurfaceSet {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$Path,

        [string]$ExcludeToken = 'agent_round'
    )

    $empty = [pscustomobject]@{ Loc = @(); Matrix = @() }
    if (-not (Test-Path -LiteralPath $Path)) { return $empty }

    $section = ''
    $loc = [System.Collections.Generic.List[string]]::new()
    $matrix = [System.Collections.Generic.List[string]]::new()
    foreach ($raw in ((Read-TextFile -Path $Path) -split "`r?`n")) {
        $line = (Remove-LineComment -Text $raw -Marker '#').TrimEnd()
        if ($line -match '^\s*\[') {
            if ($line -match '\[Matrix\]\s*LocToPlacement') { $section = 'matrix' }
            elseif ($line -match '\[Space\]\s*LocPlacement') { $section = 'loc' }
            else { $section = 'other' }
            continue
        }
        if ($section -eq 'loc' -and $line.Trim()) { $loc.Add($line.Trim()) }
        elseif ($section -eq 'matrix' -and $line -match '=') { $matrix.Add(($line -split '=')[0].Trim()) }
    }

    $locSet = @($loc | Where-Object { $_ -ne $ExcludeToken } | Sort-Object)
    $matrixSet = @($matrix | Sort-Object)
    return [pscustomobject]@{
        Loc    = [string[]]$locSet
        Matrix = [string[]]$matrixSet
    }
}

Export-ModuleMember -Function @(
    'Get-NodePlacementWireId',
    'Get-NodePlacementDeclaredId',
    'Get-NodePlacementUnion',
    'Get-NodePlacementWireIdSet',
    'Get-NodePlacementSurfaceSet'
)
