#requires -Version 5.1
<#
.SYNOPSIS
    声明面定位器（PowerShell 侧）—— 与 C# DeclarationLocator 同口径：角色 + basename → 物理路径。

.DESCRIPTION
    布局真源 = Config\declaration-layout.json（role / dirs / recursive / exclude / naming）。
    门禁脚本禁止硬编码 Config\nodes\x.ushar；一律经本模块按 role + basename 解析。
    <para>解析顺序固定：declared-path → flat → subdiv-same-name → declared-subdir → wildcard。</para>
    <para>**同层多命中 = Ambiguous（禁止静默取首项）**。</para>
    <para>新代码一律用 Resolve-DeclarationDocument：任何失败都返回显式状态（不返回 $null）。
    Resolve-DeclarationFile 是兼容包装（失败仍返回 $null，供既有门禁逐步迁移）。</para>
#>

function Get-DeclarationRepoRoot {
    $dir = $PSScriptRoot
    while ($dir -and -not ((Test-Path -LiteralPath (Join-Path $dir 'Frame')) -and (Test-Path -LiteralPath (Join-Path $dir 'Projects')))) {
        $parent = Split-Path -Parent $dir
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    if (-not $dir) { throw '声明定位器：仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
    return $dir
}

function Get-DeclarationConfigRoot {
    return (Join-Path (Get-DeclarationRepoRoot) 'Projects\Plug\Agent\Config')
}

function Get-DeclarationLayout {
    param([string]$ConfigRoot = '')
    if ([string]::IsNullOrWhiteSpace($ConfigRoot)) { $ConfigRoot = Get-DeclarationConfigRoot }
    $layoutPath = Join-Path $ConfigRoot 'declaration-layout.json'
    if (-not (Test-Path -LiteralPath $layoutPath)) { throw "声明定位器：布局真源缺失 $layoutPath" }
    return (Get-Content -LiteralPath $layoutPath -Raw -Encoding UTF8 | ConvertFrom-Json)
}

function Test-DeclarationExcluded {
    param([string]$FullPath, [string]$Root, [object[]]$Exclude)
    # 5.1 兼容：[IO.Path]::GetRelativePath 是 .NET Core 专属，改为去根前缀（大小写不敏感）。
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([char[]]@('\', '/'))
    $fullFull = [IO.Path]::GetFullPath($FullPath)
    $prefix = $rootFull + [IO.Path]::DirectorySeparatorChar
    $relative = if ($fullFull.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        $fullFull.Substring($prefix.Length)
    } else { $fullFull }
    $segments = $relative.Split([char[]]@('\', '/'))
    foreach ($ex in $Exclude) {
        if ([string]::IsNullOrWhiteSpace($ex)) { continue }
        if ($segments -contains $ex) { return $true }
    }
    return $false
}

function New-DeclarationLocation {
    param(
        [string]$Role,
        [string]$Name,
        [string]$Status,
        [string]$Path = $null,
        [object[]]$Candidates = @(),
        [object[]]$Tried = @(),
        [string]$Detail = '',
        [string]$Strategy = ''
    )
    $code = switch ($Status) {
        'Resolved' { 'decl.resolved' }
        'LayoutUnavailable' { 'decl.layout-unavailable' }
        'LayoutInvalid' { 'decl.layout-invalid' }
        'RoleUnknown' { 'decl.role-unknown' }
        'NameInvalid' { 'decl.name-invalid' }
        'Ambiguous' { 'decl.ambiguous' }
        default { 'decl.not-found' }
    }
    return [pscustomobject]@{
        Role       = $Role
        Name       = $Name
        Status     = $Status
        Code       = $code
        Path       = $Path
        Candidates = @($Candidates)
        Tried      = @($Tried)
        Detail     = $Detail
        Strategy   = $Strategy
        IsResolved = ($Status -eq 'Resolved')
        IsFailure  = ($Status -ne 'Resolved')
    }
}

function Get-DeclarationNameVariants {
    param([string]$Name, [string]$Extension = '.ushar')
    # 逻辑名已带扩展名（含多段扩展名，如 history-dispatch.rules.ushar）⇒ 原样保留，不再追加。
    if ($Name -match '\.[A-Za-z0-9]+$') { return @($Name) }
    if ([string]::IsNullOrWhiteSpace($Extension)) { $Extension = '.ushar' }
    if (-not $Extension.StartsWith('.')) { $Extension = '.' + $Extension }
    return @("$Name$Extension")
}

function Get-DeclarationLayoutScope {
    param($Layout, [string]$Role)
    return ($Layout.roles | Where-Object { $_.role -eq $Role } | Select-Object -First 1)
}

function Test-DeclarationGlobMatch {
    param([string]$FileName, [object[]]$Patterns)
    foreach ($pattern in @($Patterns)) {
        if ([string]::IsNullOrWhiteSpace($pattern)) { continue }
        if ($FileName -like $pattern) { return $true }
    }
    return $false
}

<#
.SYNOPSIS
    按角色 + 逻辑名解析声明文件（多布局；失败显式返回状态，不返回 $null）。
.DESCRIPTION
    解析顺序：declared-path（逻辑名含分隔符 / naming=full-path）→ flat（<dir>/<name>）→
    subdir-same-name（<dir>/<basename>/<name>）→ declared-subdir（<dir>/<subdir>/<name>）→ wildcard（枚举 + basename/patterns）。
    同层多命中 ⇒ Ambiguous（禁止静默取首项）。
#>
function Resolve-DeclarationDocument {
    param(
        [Parameter(Mandatory = $true)][string]$Role,
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$ConfigRoot = '',
        [string]$Extension = '.ushar',
        [switch]$Strict
    )

    if ([string]::IsNullOrWhiteSpace($ConfigRoot)) { $ConfigRoot = Get-DeclarationConfigRoot }

    $trimmed = if ($null -eq $Name) { '' } else { $Name.Trim() }
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return New-DeclarationLocation -Role $Role -Name $trimmed -Status 'NameInvalid' -Detail 'decl.name-invalid: 逻辑名为空'
    }

    $tried = New-Object System.Collections.Generic.List[string]
    try {
        $layout = Get-DeclarationLayout -ConfigRoot $ConfigRoot
    } catch {
        $status = if ($_.Exception.Message -match '缺失') { 'LayoutUnavailable' } else { 'LayoutInvalid' }
        return New-DeclarationLocation -Role $Role -Name $trimmed -Status $status -Detail $_.Exception.Message
    }

    $scope = Get-DeclarationLayoutScope -Layout $layout -Role $Role
    if (-not $scope) {
        return New-DeclarationLocation -Role $Role -Name $trimmed -Status 'RoleUnknown' `
            -Detail "decl.role-unknown: 角色 '$Role' 未在布局中登记"
    }

    $variants = @(Get-DeclarationNameVariants -Name $trimmed -Extension $Extension)
    $baseName = [IO.Path]::GetFileNameWithoutExtension([IO.Path]::GetFileName($trimmed))

    function Add-TriedPath([string]$Candidate) {
        if (-not $tried.Contains($Candidate)) { [void]$tried.Add($Candidate) }
    }

    function Resolve-HitSet([object[]]$Candidates) {
        $hits = @()
        foreach ($candidate in @($Candidates)) {
            Add-TriedPath $candidate
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                if (-not ($hits -contains $candidate)) { $hits += $candidate }
            }
        }
        return @($hits)
    }

    # 1) declared-path：逻辑名含分隔符 / 布局声明 full-path ⇒ 相对布局根的显式路径。
    if ($trimmed -match '[\\/]' -or $scope.naming -eq 'full-path') {
        $candidates = @($variants | ForEach-Object { Join-Path $ConfigRoot $_ })
        $hits = Resolve-HitSet $candidates
        if ($hits.Count -gt 1) {
            return New-DeclarationLocation -Role $Role -Name $trimmed -Status 'Ambiguous' -Candidates $hits -Tried $tried `
                -Strategy 'declared-path' -Detail "decl.ambiguous: 同层命中 $($hits.Count) 项（declared-path）；禁止静默取首项"
        }
        if ($hits.Count -eq 1) {
            return New-DeclarationLocation -Role $Role -Name $trimmed -Status 'Resolved' -Path $hits[0] -Tried $tried `
                -Strategy 'declared-path' -Detail 'decl.resolved: 命中（declared-path）'
        }
    }

    # 2) flat：<dir>/<name>（同层 = 全部声明目录；多命中 ⇒ 歧义）。
    $hits = @()
    foreach ($dir in @($scope.dirs)) {
        $dirFull = Join-Path $ConfigRoot $dir
        if (-not (Test-Path -LiteralPath $dirFull -PathType Container)) { Add-TriedPath $dirFull; continue }
        $hits += @(Resolve-HitSet @($variants | ForEach-Object { Join-Path $dirFull $_ }))
    }
    $hits = @($hits | Sort-Object -Unique)
    if ($hits.Count -gt 1) {
        return New-DeclarationLocation -Role $Role -Name $trimmed -Status 'Ambiguous' -Candidates $hits -Tried $tried `
            -Strategy 'flat' -Detail "decl.ambiguous: 同层命中 $($hits.Count) 项（flat）；禁止静默取首项"
    }
    if ($hits.Count -eq 1) {
        return New-DeclarationLocation -Role $Role -Name $trimmed -Status 'Resolved' -Path $hits[0] -Tried $tried `
            -Strategy 'flat' -Detail 'decl.resolved: 命中（flat）'
    }

    # 3) subdir-same-name：<dir>/<basename>/<name>（同层 = 全部声明目录）。
    $hits = @()
    foreach ($dir in @($scope.dirs)) {
        $dirFull = Join-Path (Join-Path $ConfigRoot $dir) $baseName
        if (-not (Test-Path -LiteralPath $dirFull -PathType Container)) { Add-TriedPath $dirFull; continue }
        $hits += @(Resolve-HitSet @($variants | ForEach-Object { Join-Path $dirFull $_ }))
    }
    $hits = @($hits | Sort-Object -Unique)
    if ($hits.Count -gt 1) {
        return New-DeclarationLocation -Role $Role -Name $trimmed -Status 'Ambiguous' -Candidates $hits -Tried $tried `
            -Strategy 'subdir-same-name' -Detail "decl.ambiguous: 同层命中 $($hits.Count) 项（subdir-same-name）；禁止静默取首项"
    }
    if ($hits.Count -eq 1) {
        return New-DeclarationLocation -Role $Role -Name $trimmed -Status 'Resolved' -Path $hits[0] -Tried $tried `
            -Strategy 'subdir-same-name' -Detail 'decl.resolved: 命中（subdir-same-name）'
    }

    # 4) declared-subdir：<dir>/<subdir>/<name>（只按布局声明，不猜目录名；同层 = 全部 dir × subdir）。
    $hits = @()
    foreach ($subdir in @($scope.subdirs)) {
        if ([string]::IsNullOrWhiteSpace($subdir)) { continue }
        foreach ($dir in @($scope.dirs)) {
            $dirFull = Join-Path (Join-Path $ConfigRoot $dir) $subdir
            if (-not (Test-Path -LiteralPath $dirFull -PathType Container)) { Add-TriedPath $dirFull; continue }
            $hits += @(Resolve-HitSet @($variants | ForEach-Object { Join-Path $dirFull $_ }))
        }
    }
    $hits = @($hits | Sort-Object -Unique)
    if ($hits.Count -gt 1) {
        return New-DeclarationLocation -Role $Role -Name $trimmed -Status 'Ambiguous' -Candidates $hits -Tried $tried `
            -Strategy 'declared-subdir' -Detail "decl.ambiguous: 同层命中 $($hits.Count) 项（declared-subdir）；禁止静默取首项"
    }
    if ($hits.Count -eq 1) {
        return New-DeclarationLocation -Role $Role -Name $trimmed -Status 'Resolved' -Path $hits[0] -Tried $tried `
            -Strategy 'declared-subdir' -Detail 'decl.resolved: 命中（declared-subdir）'
    }

    # 5) wildcard：目录内枚举（按 recursive），patterns 或 basename 匹配。
    $wildcardHits = @()
    $patterns = @($scope.patterns)
    foreach ($dir in @($scope.dirs)) {
        $dirFull = Join-Path $ConfigRoot $dir
        if (-not (Test-Path -LiteralPath $dirFull -PathType Container)) { Add-TriedPath $dirFull; continue }
        $files = @(Get-ChildItem -LiteralPath $dirFull -File -Recurse:([bool]$scope.recursive) -ErrorAction SilentlyContinue |
            Where-Object { -not (Test-DeclarationExcluded -FullPath $_.FullName -Root $ConfigRoot -Exclude @($scope.exclude)) })
        foreach ($file in $files) {
            $matched = if ($patterns.Count -gt 0) {
                (Test-DeclarationGlobMatch -FileName $file.Name -Patterns $patterns) -or
                ($file.Name -eq [IO.Path]::GetFileName($variants[0]))
            } else {
                ([IO.Path]::GetFileNameWithoutExtension($file.Name) -eq $baseName) -or
                ($file.Name -eq [IO.Path]::GetFileName($variants[0]))
            }
            if ($matched -and -not ($wildcardHits -contains $file.FullName)) { $wildcardHits += $file.FullName }
        }
    }
    $wildcardHits = @($wildcardHits | Sort-Object)
    if ($wildcardHits.Count -gt 1) {
        return New-DeclarationLocation -Role $Role -Name $trimmed -Status 'Ambiguous' -Candidates $wildcardHits -Tried $tried `
            -Strategy 'wildcard' -Detail "decl.ambiguous: 同层命中 $($wildcardHits.Count) 项（wildcard）；禁止静默取首项"
    }
    if ($wildcardHits.Count -eq 1) {
        return New-DeclarationLocation -Role $Role -Name $trimmed -Status 'Resolved' -Path $wildcardHits[0] -Tried $tried `
            -Strategy 'wildcard' -Detail 'decl.resolved: 命中（wildcard）'
    }

    $result = New-DeclarationLocation -Role $Role -Name $trimmed -Status 'NotFound' -Tried $tried `
        -Detail "decl.not-found: 角色 '$Role' 目录集内未命中 '$trimmed'（已尝试 $($tried.Count) 个候选）"
    if ($Strict) { throw ("{0}: {1}" -f $result.Code, $result.Detail) }
    return $result
}

function Resolve-DeclarationFile {
    param(
        [Parameter(Mandatory = $true)][string]$Role,
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$ConfigRoot = '',
        [string]$Extension = '.ushar'
    )
    # 兼容包装：既有门禁按「$null = 未命中」判红。新代码请用 Resolve-DeclarationDocument（显式状态）。
    $document = Resolve-DeclarationDocument -Role $Role -Name $Name -ConfigRoot $ConfigRoot -Extension $Extension
    if ($document.IsFailure) { return $null }
    return $document.Path
}

function Resolve-DeclarationFileAnyRole {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$ConfigRoot = '',
        [string]$Extension = '.ushar'
    )
    if ([string]::IsNullOrWhiteSpace($ConfigRoot)) { $ConfigRoot = Get-DeclarationConfigRoot }
    $layout = Get-DeclarationLayout -ConfigRoot $ConfigRoot
    foreach ($scope in @($layout.roles)) {
        $document = Resolve-DeclarationDocument -Role $scope.role -Name $Name -ConfigRoot $ConfigRoot -Extension $Extension
        if ($document.IsResolved) { return $document.Path }
    }
    return $null
}

Export-ModuleMember -Function Get-DeclarationRepoRoot, Get-DeclarationConfigRoot, Get-DeclarationLayout, Resolve-DeclarationDocument, Resolve-DeclarationFile, Resolve-DeclarationFileAnyRole
